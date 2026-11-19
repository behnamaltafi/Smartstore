using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Smartstore.Core.Data;
using Smartstore.Core.Identity;
using Smartstore.Events;
using Smartstore.MultiSms.Models;
using Smartstore.MultiSms.Services;

namespace Smartstore.MultiSms.Controllers;

/// <summary>
/// JSON endpoints using native Customer cookies and the same OTP rules as the storefront.
/// Obtain a CSRF token from Session and include it in all subsequent POST requests.
/// </summary>
[ApiController]
[Route("api/sms-login")]
[AutoValidateAntiforgeryToken]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class SmsLoginApiController : ControllerBase
{
    private readonly ISmsOtpLoginService _otp;
    private readonly SignInManager<Customer> _signInManager;
    private readonly UserManager<Customer> _userManager;
    private readonly SmartDbContext _db;
    private readonly IAntiforgery _antiforgery;
    private readonly IEventPublisher _events;

    public SmsLoginApiController(ISmsOtpLoginService otp,
        SignInManager<Customer> signInManager,
        UserManager<Customer> userManager,
        SmartDbContext db,
        IAntiforgery antiforgery,
        IEventPublisher events)
    {
        _otp = otp;
        _signInManager = signInManager;
        _userManager = userManager;
        _db = db;
        _antiforgery = antiforgery;
        _events = events;
    }

    /// <summary>Gets feature availability, the current session and a CSRF request token.</summary>
    [HttpGet("session"), AllowAnonymous]
    public async Task<IActionResult> Session(CancellationToken ct)
    {
        bool enabled = await _otp.IsEnabledAsync();
        var customer = await GetCustomerAsync();
        var tokens = _antiforgery.GetAndStoreTokens(HttpContext);
        return Ok(new
        {
            enabled,
            isAuthenticated = customer is not null,
            profileRequired = enabled && customer is not null &&
                await _otp.IsProfileIncompleteAsync(customer.Id, ct),
            phone = enabled && customer is not null ? await _otp.GetVerifiedPhoneAsync(customer.Id, ct) : null,
            requestToken = tokens.RequestToken,
            headerName = tokens.HeaderName
        });
    }

    /// <summary>Requests a login code with an account-independent response.</summary>
    [HttpPost("request"), AllowAnonymous]
    public async Task<IActionResult> RequestCode([FromBody] SmsCodeRequest request, CancellationToken ct)
    {
        if (!await _otp.IsEnabledAsync())
        {
            return NotFound();
        }

        await _otp.RequestLoginCodeAsync(request.Phone, ct);
        return Ok(new { code = "code_requested" });
    }

    /// <summary>Consumes an OTP and creates the native, non-persistent login cookie.</summary>
    [HttpPost("verify"), AllowAnonymous]
    public async Task<IActionResult> VerifyCode([FromBody] SmsCodeVerificationRequest request, CancellationToken ct)
    {
        if (!await _otp.IsEnabledAsync())
        {
            return NotFound();
        }

        var customer = await _otp.ConfirmLoginAsync(request.Phone, request.Code, ct);
        if (customer is null || customer.Deleted || !customer.Active || !customer.IsRegistered() ||
            !await _signInManager.CanSignInAsync(customer) ||
            await _userManager.IsLockedOutAsync(customer) ||
            await _userManager.GetTwoFactorEnabledAsync(customer))
        {
            return BadRequest(new { code = "invalid_code" });
        }

        await _signInManager.SignInAsync(customer, isPersistent: false);
        await _events.PublishAsync(new CustomerSignedInEvent { Customer = customer });
        customer.LastLoginDateUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        bool profileRequired = await _otp.IsProfileIncompleteAsync(customer.Id, ct);
        return Ok(new { code = "signed_in", profileRequired, profilePath = "/sms-login/profile" });
    }

    /// <summary>Updates the phone-linked Customer's optional profile using the auth cookie.</summary>
    [HttpPost("profile")]
    public async Task<IActionResult> CompleteProfile([FromBody] SmsProfileApiRequest request, CancellationToken ct)
    {
        if (!await _otp.IsEnabledAsync())
        {
            return NotFound();
        }

        var customer = await GetCustomerAsync();
        if (customer is null)
        {
            return Unauthorized();
        }

        var result = await _otp.CompleteProfileAsync(
            customer, request.Username, request.FirstName, request.LastName, request.Email, ct);
        if (!result.Succeeded)
        {
            return BadRequest(new { code = "invalid_profile", errors = result.Errors.Select(x => x.Description) });
        }

        return Ok(new { code = "profile_saved" });
    }

    /// <summary>Requests a phone binding for the currently signed-in account only.</summary>
    [HttpPost("enroll/request")]
    [HttpPost("profile/phone/request")]
    public async Task<IActionResult> RequestEnrollment([FromBody] SmsCodeRequest request, CancellationToken ct)
    {
        if (!await _otp.IsEnabledAsync())
        {
            return NotFound();
        }

        var customer = await GetCustomerAsync();
        if (customer is null)
        {
            return Unauthorized();
        }

        await _otp.RequestEnrollmentCodeAsync(customer, request.Phone, ct);
        return Ok(new { code = "code_requested" });
    }

    /// <summary>Confirms a phone binding without accepting a client-supplied customer ID.</summary>
    [HttpPost("enroll/verify")]
    [HttpPost("profile/phone/verify")]
    public async Task<IActionResult> ConfirmEnrollment([FromBody] SmsCodeVerificationRequest request, CancellationToken ct)
    {
        if (!await _otp.IsEnabledAsync())
        {
            return NotFound();
        }

        var customer = await GetCustomerAsync();
        if (customer is null)
        {
            return Unauthorized();
        }

        if (!await _otp.ConfirmEnrollmentAsync(customer, request.Phone, request.Code, ct))
        {
            return BadRequest(new { code = "invalid_code" });
        }

        return Ok(new { code = "phone_verified" });
    }

    private async Task<Customer> GetCustomerAsync()
    {
        if (!_signInManager.IsSignedIn(User))
        {
            return null;
        }

        var customer = await _userManager.GetUserAsync(User);
        return customer is not null && customer.Active && !customer.Deleted && customer.IsRegistered()
            ? customer
            : null;
    }
}
