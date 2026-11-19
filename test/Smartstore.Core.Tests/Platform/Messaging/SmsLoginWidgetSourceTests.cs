using System.Linq;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Smartstore.Core.Widgets;
using Smartstore.MultiSms.Components;
using Smartstore.MultiSms.Services;

namespace Smartstore.Core.Tests.Platform.Messaging;

[TestFixture]
public class SmsLoginWidgetSourceTests
{
    [Test]
    public async Task Enabled_sms_login_is_available_without_cms_widget_activation()
    {
        var otp = new Mock<ISmsOtpLoginService>();
        otp.Setup(x => x.IsEnabledAsync()).ReturnsAsync(true);
        var source = new SmsLoginWidgetSource(otp.Object);

        var widgets = (await source.GetWidgetsAsync(new PlainWidgetZone("after_login_buttons"), true)).ToArray();

        Assert.That(widgets, Has.Length.EqualTo(1));
        Assert.That(((ComponentWidget)widgets[0]).ComponentType, Is.EqualTo(typeof(SmsLoginLinkViewComponent)));
    }

    [Test]
    public async Task Verified_mobile_profile_editor_is_available_on_native_customer_info_page()
    {
        var otp = new Mock<ISmsOtpLoginService>();
        otp.Setup(x => x.IsEnabledAsync()).ReturnsAsync(true);
        var source = new SmsLoginWidgetSource(otp.Object);

        var widgets = (await source.GetWidgetsAsync(new PlainWidgetZone("customer_info_top"), true)).ToArray();

        Assert.That(widgets, Has.Length.EqualTo(1));
        Assert.That(((ComponentWidget)widgets[0]).ComponentType, Is.EqualTo(typeof(SmsProfileLinkViewComponent)));
    }

    [Test]
    public async Task Runtime_setting_changes_immediately_control_visibility()
    {
        var otp = new Mock<ISmsOtpLoginService>();
        otp.SetupSequence(x => x.IsEnabledAsync()).ReturnsAsync(true).ReturnsAsync(false);
        var source = new SmsLoginWidgetSource(otp.Object);
        var zone = new PlainWidgetZone("after_login_buttons");

        Assert.That(await source.GetWidgetsAsync(zone, true), Is.Not.Empty);
        Assert.That(await source.GetWidgetsAsync(zone, true), Is.Empty);
    }

    [TestCase("after_login_buttons", false)]
    [TestCase("head", true)]
    [TestCase("external_auth_buttons", true)]
    public async Task Other_zones_and_admin_pages_do_not_load_otp_settings(string zoneName, bool publicArea)
    {
        var otp = new Mock<ISmsOtpLoginService>(MockBehavior.Strict);
        var source = new SmsLoginWidgetSource(otp.Object);

        Assert.That(await source.GetWidgetsAsync(new PlainWidgetZone(zoneName), publicArea), Is.Empty);
        otp.Verify(x => x.IsEnabledAsync(), Times.Never);
    }
}
