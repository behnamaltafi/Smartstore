using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Smartstore.Core.Data;

namespace Smartstore.Core.Identity;

/// <summary>
/// Resolves an AppUser from a trusted application cookie. During the legacy
/// Identity&lt;Customer&gt; bridge the login account must remain explicitly linked.
/// </summary>
public class CurrentAppUserContext : ICurrentAppUserContext
{
    public const string UserGuidClaim = "app_user_guid";
    public const string TrustedClaimIssuer = "Smartstore";

    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly SmartDbContext _db;

    public CurrentAppUserContext(IHttpContextAccessor httpContextAccessor, SmartDbContext db)
    {
        _httpContextAccessor = httpContextAccessor;
        _db = db;
    }

    private static ClaimsIdentity[] GetTrustedCookieIdentities(ClaimsPrincipal principal)
    {
        return principal?.Identities
            .Where(identity => identity.IsAuthenticated &&
                identity.AuthenticationType == IdentityConstants.ApplicationScheme &&
                identity.HasClaim(c => c.Type == UserGuidClaim && c.Issuer == TrustedClaimIssuer))
            .ToArray() ?? [];
    }

    private static ClaimsIdentity[] GetIndependentCookieIdentities(ClaimsPrincipal principal)
    {
        return principal?.Identities
            .Where(identity => identity.IsAuthenticated &&
                identity.AuthenticationType == AppUserAuthenticationDefaults.Scheme &&
                identity.HasClaim(c => c.Type == AppUserAuthenticationDefaults.GuidClaim &&
                    c.Issuer == AppUserAuthenticationDefaults.Issuer))
            .ToArray() ?? [];
    }

    // Invalid or ambiguous AppUser claims must not fall back to legacy
    // Customer permissions.
    public bool IsAppUserIdentityPresent =>
        GetIndependentCookieIdentities(_httpContextAccessor.HttpContext?.User).Length > 0 ||
        GetTrustedCookieIdentities(_httpContextAccessor.HttpContext?.User).Length > 0;

    /// <summary>
    /// Read an unambiguous cookie binding without querying the database.
    /// Used by both synchronous and asynchronous permission checks.
    /// </summary>
    public static bool TryReadTrustedLegacyBinding(
        ClaimsPrincipal principal, out Guid userGuid, out int legacyLoginId)
    {
        userGuid = Guid.Empty;
        legacyLoginId = 0;
        var identities = GetTrustedCookieIdentities(principal);
        if (identities.Length != 1)
            return false;

        var identity = identities[0];
        var userClaims = identity.Claims
            .Where(c => c.Type == UserGuidClaim && c.Issuer == TrustedClaimIssuer)
            .ToArray();
        var loginClaims = identity.Claims
            .Where(c => c.Type == ClaimTypes.NameIdentifier)
            .ToArray();

        if (userClaims.Length != 1 || loginClaims.Length != 1 ||
            !Guid.TryParse(userClaims[0].Value, out userGuid) || userGuid == Guid.Empty ||
            !int.TryParse(loginClaims[0].Value, out legacyLoginId) || legacyLoginId <= 0)
        {
            userGuid = Guid.Empty;
            legacyLoginId = 0;
            return false;
        }

        return true;
    }

    public virtual async Task<AppUser> GetCurrentUserAsync(CancellationToken cancelToken = default)
    {
        var independent = GetIndependentCookieIdentities(_httpContextAccessor.HttpContext?.User);
        // A legacy Identity<Customer> cookie is never sufficient for AppUser
        // authentication after this cutover. It must not revive a prior actor.
        if (independent.Length != 1)
            return null;

        var identity = independent[0];
        var guids = identity.Claims.Where(c =>
            c.Type == AppUserAuthenticationDefaults.GuidClaim &&
            c.Issuer == AppUserAuthenticationDefaults.Issuer).ToArray();
        var stamps = identity.Claims.Where(c =>
            c.Type == AppUserAuthenticationDefaults.SecurityStampClaim &&
            c.Issuer == AppUserAuthenticationDefaults.Issuer).ToArray();
        if (guids.Length != 1 || stamps.Length != 1 ||
            !Guid.TryParse(guids[0].Value, out var independentGuid) ||
            independentGuid == Guid.Empty || string.IsNullOrWhiteSpace(stamps[0].Value))
            return null;

        var users = await _db.AppUsers.AsNoTracking().Where(x =>
            x.Active && x.UserGuid == independentGuid &&
            x.SecurityStamp == stamps[0].Value)
            .Take(2).ToArrayAsync(cancelToken);
        return users.Length == 1 ? users[0] : null;
    }}
