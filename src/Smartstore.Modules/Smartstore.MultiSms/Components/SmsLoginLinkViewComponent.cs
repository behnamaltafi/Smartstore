using Microsoft.AspNetCore.Mvc;
using Smartstore.MultiSms.Services;
using Smartstore.Web.Components;

namespace Smartstore.MultiSms.Components;

/// <summary>
/// Adds the native OTP option to the existing Smartstore login widget zone.
/// </summary>
public sealed class SmsLoginLinkViewComponent : SmartViewComponent
{
    private readonly SmsOtpLoginService _otp;

    public SmsLoginLinkViewComponent(SmsOtpLoginService otp)
    {
        _otp = otp;
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        if (!await _otp.IsEnabledAsync())
        {
            return Empty();
        }

        return View();
    }
}
