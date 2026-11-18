using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Rendering;
using Smartstore.ComponentModel;
using Smartstore.Core.Checkout.Cart;
using Smartstore.Core.Checkout.Tax;
using Smartstore.Core.Common.Configuration;
using Smartstore.Core.Common.Services;
using Smartstore.Core.Identity;
using Smartstore.Core.Localization;
using Smartstore.Core.Localization.Routing;
using Smartstore.Core.Logging;
using Smartstore.Core.Messaging;
using Smartstore.Core.Security;
using Smartstore.Core.Seo.Routing;
using Smartstore.Core.Stores;
using Smartstore.Core.Web;
using Smartstore.Web.Models.Customers;
using Smartstore.Web.Models.Identity;
using Smartstore.Web.Rendering;

namespace Smartstore.Web.Controllers;

public class IdentityController : PublicController
{
    private readonly SmartDbContext _db;
    private readonly UserManager<Customer> _userManager;
    private readonly SignInManager<Customer> _signInManager;
    private readonly RoleManager<CustomerRole> _roleManager;
    private readonly ITaxService _taxService;
    private readonly IAddressService _addressService;
    private readonly IShoppingCartService _shoppingCartService;
    private readonly INewsletterSubscriptionService _newsletterSubscriptionService;
    private readonly IMessageFactory _messageFactory;
    private readonly IWebHelper _webHelper;
    private readonly IDateTimeHelper _dateTimeHelper;
    private readonly CustomerSettings _customerSettings;
    private readonly CaptchaSettings _captchaSettings;
    private readonly DateTimeSettings _dateTimeSettings;
    private readonly TaxSettings _taxSettings;
    private readonly LocalizationSettings _localizationSettings;
    private readonly RewardPointsSettings _rewardPointsSettings;
    private readonly IAppUserBuyerContext _buyerContext;
    private readonly ICurrentAppUserContext _appUserContext;
    private readonly AppUserCredentialService _appUserCredentials;
    private readonly IAppUserChallengeSender _appUserDelivery;

    public IdentityController(
        SmartDbContext db,
        UserManager<Customer> userManager,
        SignInManager<Customer> signInManager,
        RoleManager<CustomerRole> roleManager,
        ITaxService taxService,
        IAddressService addressService,
        IShoppingCartService shoppingCartService,
        INewsletterSubscriptionService newsletterSubscriptionService,
        IMessageFactory messageFactory,
        IWebHelper webHelper,
        IDateTimeHelper dateTimeHelper,
        CustomerSettings customerSettings,
        CaptchaSettings captchaSettings,
        DateTimeSettings dateTimeSettings,
        TaxSettings taxSettings,
        LocalizationSettings localizationSettings,
        RewardPointsSettings rewardPointsSettings,
        IAppUserBuyerContext buyerContext = null,
        ICurrentAppUserContext appUserContext = null,
        AppUserCredentialService appUserCredentials = null,
        IAppUserChallengeSender appUserDelivery = null)
    {
        _db = db;
        _userManager = userManager;
        _signInManager = signInManager;
        _roleManager = roleManager;
        _taxService = taxService;
        _addressService = addressService;
        _shoppingCartService = shoppingCartService;
        _newsletterSubscriptionService = newsletterSubscriptionService;
        _messageFactory = messageFactory;
        _webHelper = webHelper;
        _dateTimeHelper = dateTimeHelper;
        _customerSettings = customerSettings;
        _captchaSettings = captchaSettings;
        _dateTimeSettings = dateTimeSettings;
        _taxSettings = taxSettings;
        _localizationSettings = localizationSettings;
        _rewardPointsSettings = rewardPointsSettings;
        _buyerContext = buyerContext;
        _appUserContext = appUserContext;
        _appUserCredentials = appUserCredentials;
        _appUserDelivery = appUserDelivery;
    }

    #region Login / Logout / Register

    [HttpGet]
    [TypeFilter(typeof(DisplayExternalAuthWidgets))]
    [AllowAnonymous, NeverAuthorize, CheckStoreClosed(false)]
    [LocalizedRoute("/login", Name = "Login")]
    public IActionResult Login(bool? checkoutAsGuest, string returnUrl = null)
    {
        var model = new LoginModel
        {
            CustomerLoginType = _customerSettings.CustomerLoginType,
            CheckoutAsGuest = checkoutAsGuest.GetValueOrDefault(),
        };

        ViewBag.ReturnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl : Url.Content("~/");

        return View(model);
    }

    [HttpPost]
    [TypeFilter(typeof(DisplayExternalAuthWidgets))]
    [AllowAnonymous, NeverAuthorize]
    [ValidateCaptcha(CaptchaSettings.Targets.Login)]
    [ValidateAntiForgeryToken, CheckStoreClosed(false)]
    [LocalizedRoute("/login", Name = "Login")]
    public async Task<IActionResult> Login(LoginModel model, string returnUrl)
    {
        // Public storefront login is AppUser-only; no Customer password check.
        if (!Url.IsLocalUrl(returnUrl))
            returnUrl = string.Empty;
        ViewBag.ReturnUrl = returnUrl;

        if (ModelState.IsValid)
        {
            var identifier = model.CustomerLoginType == CustomerLoginType.Username
                ? model.Username
                : model.CustomerLoginType == CustomerLoginType.Email
                    ? model.Email : model.UsernameOrEmail;
            var actor = await _appUserCredentials.AuthenticatePasswordAsync(
                identifier, model.Password);
            if (actor is not null)
            {
                await AppUserAuthenticationController.SignInUserAsync(
                    HttpContext, _buyerContext, actor, model.RememberMe);
                return returnUrl.HasValue() && returnUrl != "/"
                    ? RedirectToReferrer(returnUrl)
                    : RedirectToRoute("Homepage");
            }

            ModelState.AddModelError(string.Empty, T("Account.Login.WrongCredentials"));
        }

        model.CustomerLoginType = _customerSettings.CustomerLoginType;
        return View(model);
    }

        [NeverAuthorize, CheckStoreClosed(false)]
    [DisallowRobot(true)]
    [LocalizedRoute("/logout", Name = "Logout")]
    public async Task<IActionResult> Logout()
    {
        var workContext = Services.WorkContext;
        var db = Services.DbContext;

        if (workContext.CurrentImpersonator != null)
        {
            // Logout impersonated customer.
            workContext.CurrentImpersonator.GenericAttributes.ImpersonatedCustomerId = null;
            await db.SaveChangesAsync();

            // Redirect back to customer details page (admin area).
            return RedirectToAction("Edit", "Customer", new { id = workContext.CurrentCustomer.Id, area = "Admin" });
        }
        else
        {
            // Standard logout
            Services.ActivityLogger.LogActivity(KnownActivityLogTypes.PublicStoreLogout, T("ActivityLog.PublicStore.Logout"));

            await _signInManager.SignOutAsync();
            await HttpContext.SignOutAsync(AppUserAuthenticationDefaults.Scheme);
            _buyerContext?.ClearSelection();
            await db.SaveChangesAsync();

            return RedirectToRoute("Homepage");
        }
    }

    [HttpGet]
    [AllowAnonymous, NeverAuthorize]
    [LocalizedRoute("/register", Name = "Register")]
    public async Task<IActionResult> Register(string returnUrl = null)
    {
        // Check whether registration is allowed.
        if (_customerSettings.UserRegistrationType == UserRegistrationType.Disabled)
        {
            return RedirectToRoute("RegisterResult", new { resultId = (int)UserRegistrationType.Disabled });
        }

        ViewBag.ReturnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl : string.Empty;

        var model = new RegisterModel();
        await PrepareRegisterModelAsync(model);

        return View(model);
    }

    [HttpPost]
    [AllowAnonymous, NeverAuthorize]
    [ValidateCaptcha(CaptchaSettings.Targets.Registration)]
    [ValidateAntiForgeryToken, ValidateHoneypot]
    [LocalizedRoute("/register", Name = "Register")]
    public async Task<IActionResult> Register(RegisterModel model, string returnUrl = null)
    {
        if (_customerSettings.UserRegistrationType == UserRegistrationType.Disabled)
            return RedirectToRoute("RegisterResult", new { resultId = (int)UserRegistrationType.Disabled });

        if (!Url.IsLocalUrl(returnUrl))
            returnUrl = string.Empty;
        ViewData["ReturnUrl"] = returnUrl;

        // Do not silently bypass existing email-validation/admin-approval
        // registration policies until corresponding AppUser workflows exist.
        if (_customerSettings.UserRegistrationType != UserRegistrationType.Standard)
        {
            ModelState.AddModelError(string.Empty,
                "Independent AppUser registration requires its own approval/activation policy.");
        }

        if (ModelState.IsValid)
        {
            var username = model.Username.HasValue() ? model.Username : model.Email;
            var user = await _appUserCredentials.RegisterAsync(
                username, model.Email, model.Password, model.Phone);
            if (user is not null)
            {
                await AppUserAuthenticationController.SignInUserAsync(
                    HttpContext, _buyerContext, user, false);
                var verification = await _appUserCredentials.BeginEmailVerificationAsync(user);
                if (verification is not null)
                    await _appUserDelivery.SendEmailVerificationAsync(user.Email, verification);
                return returnUrl.HasValue()
                    ? RedirectToReferrer(returnUrl)
                    : RedirectToRoute("Homepage");
            }

            ModelState.AddModelError(string.Empty, "Invalid registration details or account already exists.");
        }

        await PrepareRegisterModelAsync(model);
        return View(model);
    }

        [HttpGet]
    [AllowAnonymous, NeverAuthorize]
    [LocalizedRoute("/registerresult", Name = "RegisterResult")]
    public IActionResult RegisterResult(int? resultId, string message = "")
    {
        var resultText = string.Empty;

        if (resultId != null)
        {
            switch ((UserRegistrationType)resultId)
            {
                case UserRegistrationType.Disabled:
                    resultText = T("Account.Register.Result.Disabled");
                    break;
                case UserRegistrationType.Standard:
                    resultText = T("Account.Register.Result.Standard");
                    break;
                case UserRegistrationType.AdminApproval:
                    resultText = T("Account.Register.Result.AdminApproval");
                    break;
                case UserRegistrationType.EmailValidation:
                    resultText = T("Account.Register.Result.EmailValidation");
                    break;
                default:
                    break;
            }
        }

        ViewBag.RegisterResult = resultText.Grow(message.Truncate(200).HtmlEncode());

        return View();
    }

    [HttpGet]
    [AllowAnonymous, NeverAuthorize]
    [LocalizedRoute("/customer/activation", Name = "AccountActivation")]
    public async Task<IActionResult> AccountActivation(string token, string email)
    {
        // Confirm ownership of an AppUser email, never a Customer login.
        if (!await _appUserCredentials.CompleteEmailVerificationAsync(email, token))
        {
            NotifyError(T("Account.AccountActivation.InvalidEmailOrToken"));
            return RedirectToRoute("Homepage");
        }

        ViewBag.ActivationResult = T("Account.AccountActivation.Activated");
        return View();
    }
    #endregion

    #region Profile

    [DisallowRobot]
    [LocalizedRoute("/profile/{id:int}", Name = "CustomerProfile")]
    public async Task<IActionResult> CustomerProfile(int id)
    {
        if (!_customerSettings.AllowViewingProfiles)
        {
            return NotFound();
        }

        var customer = await _db.Customers
            .IncludeCustomerRoles()
            .FindByIdAsync(id, false);

        // Guests do not have a customer profile.
        if (customer?.IsGuest() ?? true)
        {
            return NotFound();
        }

        var info = new ProfileInfoModel
        {
            Id = customer.Id,
            Avatar = await customer.MapAsync(null, true)
        };

        // Location.
        if (_customerSettings.ShowCustomersLocation)
        {
            var country = await _db.Countries.FindByIdAsync(customer.GenericAttributes.CountryId ?? 0, false);

            info.LocationEnabled = country != null;
            info.Location = country?.GetLocalized(x => x.Name);
        }

        // Registration date.
        if (_customerSettings.ShowCustomersJoinDate)
        {
            info.JoinDateEnabled = true;
            info.JoinDate = _dateTimeHelper.ConvertToUserTime(customer.CreatedOnUtc, DateTimeKind.Utc);
        }

        // Birth date.
        if (_customerSettings.DateOfBirthEnabled && customer.BirthDate.HasValue)
        {
            info.DateOfBirthEnabled = true;
            info.DateOfBirth = _dateTimeHelper.ConvertToUserTime(customer.BirthDate.Value, DateTimeKind.Utc);
        }

        var model = new ProfileIndexModel
        {
            Id = customer.Id,
            CustomerName = customer.FormatUserName(_customerSettings, T, true),
            ProfileInfo = info
        };

        return View(model);
    }

    #endregion

    #region Change password

    [DisallowRobot]
    public async Task<IActionResult> ChangePassword()
    {
        // The account page authenticates the person, not their commercial buyer.
        if (_appUserContext is null || await _appUserContext.GetCurrentUserAsync() is null)
            return ChallengeOrForbid();

        return View(new ChangePasswordModel());
    }

    [HttpPost]
    public async Task<IActionResult> ChangePassword(ChangePasswordModel model)
    {
        var actor = _appUserContext is null ? null : await _appUserContext.GetCurrentUserAsync();
        if (actor is null)
            return ChallengeOrForbid();

        if (ModelState.IsValid)
        {
            if (await _appUserCredentials.ChangePasswordAsync(
                actor, model.OldPassword, model.NewPassword))
            {
                await HttpContext.SignOutAsync(AppUserAuthenticationDefaults.Scheme);
                _buyerContext?.ClearSelection();
                model.Result = T("Account.ChangePassword.Success");
            }
            else
            {
                ModelState.AddModelError(string.Empty, T("Account.Login.WrongCredentials"));
            }
        }

        return View(model);
    }

    #endregion

    #region Password recovery

    [DisallowRobot]
    [LocalizedRoute("/passwordrecovery", Name = "PasswordRecovery")]
    public IActionResult PasswordRecovery()
    {
        var model = new PasswordRecoveryModel();
        return View(model);
    }

    [HttpPost, DisallowRobot]
    [ValidateCaptcha(CaptchaSettings.Targets.PasswordRecovery)]
    [LocalizedRoute("/passwordrecovery", Name = "PasswordRecovery")]
    [FormValueRequired("send-email")]
    public async Task<IActionResult> PasswordRecovery(PasswordRecoveryModel model)
    {
        if (ModelState.IsValid)
        {
            var token = await _appUserCredentials.BeginPasswordResetAsync(model.Email);
            if (token is not null)
                await _appUserDelivery.SendPasswordResetAsync(model.Email, token);

            model.ResultState = PasswordRecoveryResultState.Success;
            model.ResultMessage = T("Account.PasswordRecovery.EmailHasBeenSent");
        }

        return View(model);
    }

    public IActionResult PasswordRecoveryConfirm(string token, string email)
    {
        var model = new PasswordRecoveryConfirmModel
        {
            Email = email,
            Token = token
        };

        return View(model);
    }

    [HttpPost, ActionName("PasswordRecoveryConfirm")]
    [FormValueRequired("set-password")]
    public async Task<IActionResult> PasswordRecoveryConfirmPOST(PasswordRecoveryConfirmModel model)
    {
        if (ModelState.IsValid)
        {
            if (await _appUserCredentials.CompletePasswordResetAsync(
                model.Email, model.Token, model.NewPassword))
            {
                model.SuccessfullyChanged = true;
                model.Result = T("Account.PasswordRecovery.PasswordHasBeenChanged");
            }
            else
                ModelState.AddModelError(string.Empty, T("Account.PasswordRecovery.WrongToken"));
        }

        return View(model);
    }

    #endregion

    #region External login

    [HttpGet]
    [AllowAnonymous, DisallowRobot]
    public IActionResult ExternalLogin(string provider)
    {
        // Issue an independent external challenge, not SignInManager<Customer>.
        return RedirectToAction(nameof(AppUserAuthenticationController.ExternalChallenge),
            "AppUserAuthentication", new { provider });
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> ExternalLoginCallback(string returnUrl = null, string remoteError = null)
    {
        // Legacy callbacks must never create or sign in Customer credentials.
        // New challenges are issued by AppUserAuthenticationController only.
        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
        return RedirectToRoute("Login");
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult ExternalErrorCallback(string provider, string errorMessage)
    {
        if (provider.HasValue() || errorMessage.HasValue())
        {
            Logger.Error($"Error from external provider {provider.Truncate(200).HtmlEncode()}: {errorMessage.Truncate(200).HtmlEncode()}");
        }

        NotifyError(T("ExternalAuthentication.ConfigError"));
        return RedirectToAction(nameof(Login));
    }

    #endregion

    #region Access

    [HttpGet]
    [AllowAnonymous, NeverAuthorize]
    [LocalizedRoute("/access-denied", Name = "AccessDenied")]
    public IActionResult AccessDenied(string returnUrl = null)
    {
        throw new AccessDeniedException(null, Url.IsLocalUrl(returnUrl) ? returnUrl : null);
    }

    #endregion

    #region Helpers

    private async Task PrepareRegisterModelAsync(RegisterModel model)
    {
        model.AllowCustomersToSetTimeZone = _dateTimeSettings.AllowCustomersToSetTimeZone;
        model.DisplayVatNumber = _taxSettings.EuVatEnabled;
        model.VatRequired = _taxSettings.VatRequired;

        MiniMapper.Map(_customerSettings, model);

        model.UsernamesEnabled = _customerSettings.CustomerLoginType != CustomerLoginType.Email;
        model.CheckUsernameAvailabilityEnabled = _customerSettings.CheckUsernameAvailabilityEnabled;
        model.DisplayCaptcha = _captchaSettings.IsActiveTarget(CaptchaSettings.Targets.Registration);

        ViewBag.AvailableTimeZones = _dateTimeHelper.GetSystemTimeZones()
            .ToSelectListItems(_dateTimeHelper.DefaultStoreTimeZone.Id);

        if (_customerSettings.CountryEnabled && _customerSettings.StateProvinceEnabled)
        {
            var stateProvinces = await _db.StateProvinces.GetStateProvincesByCountryIdAsync(model.CountryId);

            ViewBag.AvailableStates = stateProvinces.ToSelectListItems(model.StateProvinceId ?? 0) ??
            [
                new SelectListItem { Text = T("Address.OtherNonUS"), Value = "0" }
            ];
        }
    }

    /// <summary>
    /// Assigns customer roles, publishes an event, sends email messages, signs the customer in depending on configuration & returns appropriate redirect.
    /// </summary>
    private async Task<IActionResult> FinalizeCustomerRegistrationAsync(Customer customer, string returnUrl)
    {
        // Remove ClientIdent: no other "same-building" guest should be identified by this ident.
        customer.ClientIdent = null;

        // Add reward points for customer registration (if enabled).
        if (_rewardPointsSettings.Enabled && _rewardPointsSettings.PointsForRegistration > 0)
        {
            customer.AddRewardPointsHistoryEntry(_rewardPointsSettings.PointsForRegistration, T("RewardPoints.Message.RegisteredAsCustomer"));
        }

        await Services.EventPublisher.PublishAsync(new CustomerRegisteredEvent { Customer = customer });

        // Notifications
        if (_customerSettings.NotifyNewCustomerRegistration)
        {
            await _messageFactory.SendCustomerRegisteredNotificationMessageAsync(customer, _localizationSettings.DefaultAdminLanguageId);
        }

        switch (_customerSettings.UserRegistrationType)
        {
            case UserRegistrationType.EmailValidation:
            {
                // Send an email with generated token.
                var code = await _userManager.GenerateEmailConfirmationTokenAsync(customer);

                customer.GenericAttributes.AccountActivationToken = code;
                await _db.SaveChangesAsync();
                await _messageFactory.SendCustomerEmailValidationMessageAsync(customer, Services.WorkContext.WorkingLanguage.Id);

                return RedirectToRoute("RegisterResult", new { resultId = (int)UserRegistrationType.EmailValidation });
            }
            case UserRegistrationType.AdminApproval:
            {
                return RedirectToRoute("RegisterResult", new { resultId = (int)UserRegistrationType.AdminApproval });
            }
            case UserRegistrationType.Standard:
            {
                // Send customer welcome message.
                await _messageFactory.SendCustomerWelcomeMessageAsync(customer, Services.WorkContext.WorkingLanguage.Id);
                await _signInManager.SignInAsync(customer, isPersistent: false);

                var redirectUrl = Url.RouteUrl("RegisterResult", new { resultId = (int)UserRegistrationType.Standard });
                if (Url.IsLocalUrl(returnUrl))
                {
                    redirectUrl = _webHelper.ModifyQueryString(redirectUrl, "returnUrl=" + returnUrl.UrlEncode());
                }

                return Redirect(redirectUrl);
            }
            default:
            {
                return RedirectToRoute("Homepage");
            }
        }
    }

    private async Task<bool> CheckRegisteredRole(Customer customer)
    {
        if (customer.IsSystemAccount || (customer.Username.IsEmpty() && customer.Email.IsEmpty()))
        {
            return false;
        }

        var updated = false;
        var guestRoleMappings = customer.CustomerRoleMappings
            .Where(x => !x.IsSystemMapping && x.CustomerRole.SystemName.EqualsNoCase(SystemCustomerRoleNames.Guests))
            .ToArray();

        if (guestRoleMappings.Length > 0)
        {
            _db.CustomerRoleMappings.RemoveRange(guestRoleMappings);
            updated = true;
        }

        if (!customer.IsRegistered())
        {
            var registeredRole = await _db.CustomerRoles
                .AsNoTracking()
                .Where(x => x.SystemName == SystemCustomerRoleNames.Registered)
                .OrderBy(x => x.Id)
                .FirstOrDefaultAsync();

            if (registeredRole != null)
            {
                _db.CustomerRoleMappings.Add(new()
                {
                    CustomerId = customer.Id,
                    CustomerRoleId = registeredRole.Id
                });

                // Add customer to an additional role.
                var roleIdToAdd = _customerSettings.RegisterCustomerRoleId ?? 0;
                if (roleIdToAdd != 0 && roleIdToAdd != registeredRole.Id)
                {
                    var customerRole = await _roleManager.FindByIdAsync(roleIdToAdd);
                    if (customerRole != null)
                    {
                        await _userManager.AddToRoleAsync(customer, customerRole.Name);
                    }
                }

                updated = true;
            }
        }

        return updated;
    }

    private async Task MapRegisterModelToCustomerAsync(Customer customer, RegisterModel model)
    {
        // Properties
        if (_dateTimeSettings.AllowCustomersToSetTimeZone)
        {
            customer.TimeZoneId = model.TimeZoneId;
        }

        // VAT number
        if (_taxSettings.EuVatEnabled)
        {
            customer.GenericAttributes.VatNumber = model.VatNumber;

            var vatCheckResult = await _taxService.GetVatNumberStatusAsync(model.VatNumber);
            customer.VatNumberStatusId = (int)vatCheckResult.Status;

            // Send VAT number admin notification.
            if (model.VatNumber.HasValue() && _taxSettings.EuVatEmailAdminWhenNewVatSubmitted)
            {
                await _messageFactory.SendNewVatSubmittedStoreOwnerNotificationAsync(customer, model.VatNumber, vatCheckResult.Address, _localizationSettings.DefaultAdminLanguageId);
            }
        }

        // Form fields
        customer.FirstName = model.FirstName;
        customer.LastName = model.LastName;

        if (_customerSettings.CompanyEnabled)
        {
            customer.Company = model.Company;
        }

        if (_customerSettings.DateOfBirthEnabled && model.DateOfBirth.HasValue)
        {
            customer.BirthDate = model.DateOfBirth;
        }

        if (_customerSettings.CustomerNumberMethod == CustomerNumberMethod.AutomaticallySet && customer.CustomerNumber.IsEmpty())
        {
            customer.CustomerNumber = customer.Id.Convert<string>();
        }
        if (_customerSettings.GenderEnabled)
        {
            customer.Gender = model.Gender;
        }
        if (_customerSettings.ZipPostalCodeEnabled)
        {
            customer.GenericAttributes.ZipPostalCode = model.ZipPostalCode;
        }
        if (_customerSettings.CountryEnabled)
        {
            customer.GenericAttributes.CountryId = model.CountryId;
        }
        if (_customerSettings.StreetAddressEnabled)
        {
            customer.GenericAttributes.StreetAddress = model.StreetAddress;
        }
        if (_customerSettings.StreetAddress2Enabled)
        {
            customer.GenericAttributes.StreetAddress2 = model.StreetAddress2;
        }
        if (_customerSettings.CityEnabled)
        {
            customer.GenericAttributes.City = model.City;
        }
        if (_customerSettings.CountryEnabled && _customerSettings.StateProvinceEnabled)
        {
            customer.GenericAttributes.StateProvinceId = model.StateProvinceId;
        }
        if (_customerSettings.PhoneEnabled)
        {
            customer.GenericAttributes.Phone = model.Phone;
        }
        if (_customerSettings.FaxEnabled)
        {
            customer.GenericAttributes.Fax = model.Fax;
        }

        // Newsletter subscription
        if (_customerSettings.NewsletterEnabled && model.Newsletter)
        {
            await _newsletterSubscriptionService.SubscribeAsync(customer.Email, customer, true);
        }

        // Insert default address (if possible).
        var defaultAddress = new Address
        {
            Title = customer.Title,
            FirstName = customer.FirstName,
            LastName = customer.LastName,
            Email = customer.Email,
            Company = customer.Company,
            CountryId = customer.GenericAttributes.CountryId,
            ZipPostalCode = customer.GenericAttributes.ZipPostalCode,
            StateProvinceId = customer.GenericAttributes.StateProvinceId,
            City = customer.GenericAttributes.City,
            Address1 = customer.GenericAttributes.StreetAddress,
            Address2 = customer.GenericAttributes.StreetAddress2,
            PhoneNumber = customer.GenericAttributes.Phone,
            FaxNumber = customer.GenericAttributes.Fax,
            CreatedOnUtc = customer.CreatedOnUtc
        };

        if (await _addressService.IsAddressValidAsync(defaultAddress))
        {
            // Set default addresses.
            customer.Addresses.Add(defaultAddress);
            customer.BillingAddress = defaultAddress;
            customer.ShippingAddress = defaultAddress;
        }

        _db.TryUpdate(customer);
        await _db.SaveChangesAsync();
    }

    private async Task AssignCustomerRolesAsync(Customer customer)
    {
        // Add customer to 'Registered' role.
        var registeredRole = await _db.CustomerRoles
            .Where(x => x.SystemName == SystemCustomerRoleNames.Registered)
            .FirstOrDefaultAsync();

        await _userManager.AddToRoleAsync(customer, registeredRole.Name);

        // Add customer to an additional role.
        var roleIdToAdd = _customerSettings.RegisterCustomerRoleId ?? 0;
        if (roleIdToAdd != 0 && roleIdToAdd != registeredRole.Id)
        {
            var customerRole = await _roleManager.FindByIdAsync(roleIdToAdd);
            if (customerRole != null)
            {
                await _userManager.AddToRoleAsync(customer, customerRole.Name);
            }
        }

        // Remove customer from 'Guests' role.
        await _userManager.RemoveFromRoleAsync(customer, SystemCustomerRoleNames.Guests);
    }

    private void AddModelStateErrors(IdentityResult result, bool passwordValidation = false)
    {
        if (!result.Succeeded)
        {
            var descriptions = result.Errors.Select(x => x.Description).Distinct();

            if (passwordValidation)
            {
                ModelState.AddModelError(string.Empty, T("Account.Register.Result.MeetPasswordRequirements", string.Join(", ", descriptions)));
            }
            else
            {
                descriptions.Each(x => ModelState.AddModelError(string.Empty, x));
            }
        }
    }

    private async Task FinalizeLoginAsync(Customer guest, Customer registered, bool logActivity)
    {
        await _shoppingCartService.MigrateCartAsync(guest, registered);
        await Services.EventPublisher.PublishAsync(new CustomerSignedInEvent { Customer = registered });

        if (logActivity)
        {
            Services.ActivityLogger.LogActivity(KnownActivityLogTypes.PublicStoreLogin, T("ActivityLog.PublicStore.Login"), registered);
        }
    }

    #endregion
}
