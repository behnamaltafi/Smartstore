using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Smartstore.Core.Identity;
using Smartstore.MultiSms.Services;
using Smartstore.Web.Components;

namespace Smartstore.MultiSms.Components;

/// <summary>Inline OTP-protected mobile editor in the native customer profile.</summary>
public sealed class SmsProfileLinkViewComponent : SmartViewComponent
{
    private readonly ISmsOtpLoginService _otp;
    private readonly UserManager<Customer> _userManager;
    private readonly SignInManager<Customer> _signInManager;

    public SmsProfileLinkViewComponent(ISmsOtpLoginService otp,
        UserManager<Customer> userManager, SignInManager<Customer> signInManager)
    {
        _otp = otp;
        _userManager = userManager;
        _signInManager = signInManager;
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        if (!await _otp.IsEnabledAsync() || !_signInManager.IsSignedIn(HttpContext.User))
        {
            return Empty();
        }

        var customer = await _userManager.GetUserAsync(HttpContext.User);
        if (customer is null || !customer.Active || customer.Deleted || !customer.IsRegistered())
        {
            return Empty();
        }

        var phone = await _otp.GetVerifiedPhoneAsync(customer.Id);
        return View("Default", phone is null ? null : SmsPhoneNormalizer.ToProviderPhone(phone));
    }
}
