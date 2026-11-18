using Microsoft.AspNetCore.Http;

namespace Smartstore.Core.Identity;

/// <summary>
/// Keeps buyer selection separate from authentication and performs authorization
/// on every use, including after memberships have been revoked.
/// </summary>
public class AppUserBuyerContext : IAppUserBuyerContext
{
    public const string CookieName = "smartstore.selected-customer";

    private readonly IHttpContextAccessor _accessor;
    private readonly ICurrentAppUserContext _appUserContext;
    private readonly IAppUserCustomerService _memberships;

    public AppUserBuyerContext(
        IHttpContextAccessor accessor,
        ICurrentAppUserContext appUserContext,
        IAppUserCustomerService memberships)
    {
        _accessor = accessor;
        _appUserContext = appUserContext;
        _memberships = memberships;
    }

    public async Task<int?> GetSelectedCustomerIdAsync(CancellationToken cancelToken = default)
    {
        var httpContext = _accessor.HttpContext;
        if (httpContext is null || !_appUserContext.IsAppUserIdentityPresent)
            return null;

        if (!httpContext.Request.Cookies.TryGetValue(CookieName, out var raw) ||
            !int.TryParse(raw, out var customerId) || customerId <= 0)
            return null;

        var user = await _appUserContext.GetCurrentUserAsync(cancelToken);
        if (user is null || !await _memberships.CanAccessCustomerAsync(user.Id, customerId, cancelToken))
            return null;

        return customerId;
    }

    public async Task<bool> SelectCustomerAsync(int customerId, CancellationToken cancelToken = default)
    {
        var httpContext = _accessor.HttpContext;
        if (httpContext is null || customerId <= 0 || !_appUserContext.IsAppUserIdentityPresent)
            return false;

        var user = await _appUserContext.GetCurrentUserAsync(cancelToken);
        if (user is null || !await _memberships.CanAccessCustomerAsync(user.Id, customerId, cancelToken))
            return false;

        httpContext.Response.Cookies.Append(CookieName, customerId.ToString(), new CookieOptions
        {
            HttpOnly = true,
            Secure = httpContext.Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            IsEssential = true,
            Path = "/",
            MaxAge = TimeSpan.FromDays(7)
        });

        return true;
    }

    public void ClearSelection()
    {
        _accessor.HttpContext?.Response.Cookies.Delete(CookieName, new CookieOptions { Path = "/" });
    }
}
