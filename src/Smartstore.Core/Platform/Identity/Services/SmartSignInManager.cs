using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Smartstore.Core.Data;

namespace Smartstore.Core.Identity;

public class SmartSignInManager : SignInManager<Customer>
{
    private readonly Lazy<SmartDbContext> _db;
    private readonly CustomerSettings _customerSettings;

    public SmartSignInManager(UserManager<Customer> userManager,
        IHttpContextAccessor contextAccessor,
        IUserClaimsPrincipalFactory<Customer> claimsFactory,
        IOptions<IdentityOptions> optionsAccessor,
        ILogger<SignInManager<Customer>> logger,
        IAuthenticationSchemeProvider schemes,
        IUserConfirmation<Customer> confirmation,
        Lazy<SmartDbContext> db,
        CustomerSettings customerSettings)
        : base(userManager, contextAccessor, claimsFactory, optionsAccessor, logger, schemes, confirmation)
    {
        _db = db;
        _customerSettings = customerSettings;
    }

    /// <summary>
    /// During the migration window, legacy sign-in may be explicitly associated with
    /// a separate application user. It is never inferred from matching contact details.
    /// </summary>
    public override async Task<ClaimsPrincipal> CreateUserPrincipalAsync(Customer user)
    {
        var principal = await base.CreateUserPrincipalAsync(user);

        // ASP.NET Identity<Customer> must issue an immutable login ID in its
        // signed application cookie. An AppUser is only attached to that exact
        // legacy login account, never to a matching email or the selected buyer.
        var identity = principal.Identities.FirstOrDefault(x => x.IsAuthenticated);
        var loginIds = identity?.Claims.Where(c => c.Type == ClaimTypes.NameIdentifier).ToArray();
        if (identity is not null && loginIds?.Length == 1 &&
            loginIds[0].Value == user.Id.ToString())
        {
            // Ambiguous legacy mappings must fail closed rather than picking
            // whichever AppUser happens to be returned first.
            var matches = await _db.Value.AppUsers.AsNoTracking()
                .Where(x => x.LegacyIdentityCustomerId == user.Id && x.Active)
                .Take(2)
                .ToArrayAsync();
            if (matches.Length == 1)
                identity.AddClaim(new Claim(CurrentAppUserContext.UserGuidClaim,
                    matches[0].UserGuid.ToString("D"), ClaimValueTypes.String,
                    CurrentAppUserContext.TrustedClaimIssuer));
        }

        return principal;
    }

    /// <inheritdoc />
    public override async Task<SignInResult> PasswordSignInAsync(string userNameOrEmail, string password, bool isPersistent, bool lockoutOnFailure)
    {
        Customer user;

        if (_customerSettings.CustomerLoginType == CustomerLoginType.Email)
        {
            user = await UserManager.FindByEmailAsync(userNameOrEmail);
        }
        else if (_customerSettings.CustomerLoginType == CustomerLoginType.Username)
        {
            user = await UserManager.FindByNameAsync(userNameOrEmail);
        }
        else
        {
            user = await UserManager.FindByEmailAsync(userNameOrEmail) ?? await UserManager.FindByNameAsync(userNameOrEmail);
        }

        if (user == null)
        {
            return SignInResult.Failed;
        }

        return await PasswordSignInAsync(user, password, isPersistent, lockoutOnFailure);
    }

    /// <inheritdoc />
    public override async Task<SignInResult> PasswordSignInAsync(Customer user, string password, bool isPersistent, bool lockoutOnFailure)
    {
        if (user == null || user.Deleted)
        {
            return SignInResult.Failed;
        }

        if (!user.Active)
        {
            return SignInResult.NotAllowed;
        }

        if (!user.IsRegistered())
        {
            return SignInResult.NotAllowed;
        }

        var result = await base.PasswordSignInAsync(user, password, isPersistent, lockoutOnFailure);

        if (result.Succeeded)
        {
            // Do not carry the buyer selected by a previous logged-in person
            // into this new authenticated browser session.
            Context.Response.Cookies.Delete(AppUserBuyerContext.CookieName,
                new CookieOptions { Path = "/" });

            user.LastLoginDateUtc = DateTime.UtcNow;
            _db.Value.TryUpdate(user);
            await _db.Value.SaveChangesAsync();
        }

        return result;
    }
}