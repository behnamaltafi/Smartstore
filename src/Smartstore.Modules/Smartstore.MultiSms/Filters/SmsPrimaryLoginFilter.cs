using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Smartstore.Core.Configuration;
using Smartstore.MultiSms.Configuration;

namespace Smartstore.MultiSms.Filters;

/// <summary>
/// Selects the storefront's primary login method while keeping explicit password
/// sign-in and native password POST handling available.
/// </summary>
public sealed class SmsPrimaryLoginFilter : IAsyncActionFilter
{
    private readonly ISettingFactory _settings;

    public SmsPrimaryLoginFilter(ISettingFactory settings)
    {
        _settings = settings;
    }

    /// <inheritdoc />
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (!HttpMethods.IsGet(context.HttpContext.Request.Method) ||
            string.Equals(context.HttpContext.Request.Query["method"], "password", StringComparison.OrdinalIgnoreCase))
        {
            await next();
            return;
        }

        var settings = await _settings.LoadSettingsAsync<SmsGatewaySettings>();
        if (!settings.Enabled || !settings.OtpLoginEnabled || !settings.SmsAsPrimaryLogin)
        {
            await next();
            return;
        }

        var controller = (Controller)context.Controller;
        context.ActionArguments.TryGetValue("returnUrl", out var returnUrlArgument);
        context.ActionArguments.TryGetValue("checkoutAsGuest", out var checkoutAsGuest);
        string returnUrl = returnUrlArgument as string;

        context.Result = new RedirectToActionResult("Index", "SmsLogin", new
        {
            area = string.Empty,
            returnUrl = controller.Url.IsLocalUrl(returnUrl) ? returnUrl : null,
            checkoutAsGuest = checkoutAsGuest is true
        });
    }
}
