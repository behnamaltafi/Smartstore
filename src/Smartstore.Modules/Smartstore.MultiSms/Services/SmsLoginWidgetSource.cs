using Smartstore.Core.Widgets;
using Smartstore.MultiSms.Components;

namespace Smartstore.MultiSms.Services;

/// <summary>
/// Adds SMS sign-in to the native login page whenever OTP is enabled,
/// without requiring separate CMS widget activation.
/// </summary>
public sealed class SmsLoginWidgetSource : IWidgetSource
{
    private readonly ISmsOtpLoginService _otp;

    public SmsLoginWidgetSource(ISmsOtpLoginService otp)
    {
        _otp = otp;
    }

    /// <inheritdoc />
    public int Order => 0;

    /// <inheritdoc />
    public async Task<IEnumerable<Widget>> GetWidgetsAsync(IWidgetZone zone, bool isPublicArea, object model = null)
    {
        ArgumentNullException.ThrowIfNull(zone);

        if (!isPublicArea ||
            (!string.Equals(zone.Name, "after_login_buttons", StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(zone.Name, "customer_info_top", StringComparison.OrdinalIgnoreCase)) ||
            !await _otp.IsEnabledAsync())
        {
            return Array.Empty<Widget>();
        }

        return string.Equals(zone.Name, "customer_info_top", StringComparison.OrdinalIgnoreCase)
            ? [new ComponentWidget(typeof(SmsProfileLinkViewComponent)) { Key = "Smartstore.MultiSms.ProfileLink" }]
            : [new ComponentWidget(typeof(SmsLoginLinkViewComponent)) { Key = "Smartstore.MultiSms.LoginLink" }];
    }
}
