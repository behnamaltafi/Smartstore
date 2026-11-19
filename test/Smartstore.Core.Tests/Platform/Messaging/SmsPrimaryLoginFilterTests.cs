using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Moq;
using NUnit.Framework;
using Smartstore.Core.Configuration;
using Smartstore.MultiSms.Configuration;
using Smartstore.MultiSms.Filters;

namespace Smartstore.Core.Tests.Platform.Messaging;

[TestFixture]
public class SmsPrimaryLoginFilterTests
{
    [Test]
    public async Task Enabled_primary_sms_redirects_the_native_login_and_preserves_checkout_context()
    {
        var settings = SettingsFactory(true, true, true);
        var context = Context("GET", "", "/checkout", true);
        bool nextCalled = false;

        await new SmsPrimaryLoginFilter(settings.Object).OnActionExecutionAsync(context, () =>
        {
            nextCalled = true;
            return Task.FromResult(new ActionExecutedContext(context, context.Filters, context.Controller));
        });

        var redirect = context.Result as RedirectToActionResult;
        Assert.That(redirect, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(redirect.ControllerName, Is.EqualTo("SmsLogin"));
            Assert.That(redirect.ActionName, Is.EqualTo("Index"));
            Assert.That(redirect.RouteValues["returnUrl"], Is.EqualTo("/checkout"));
            Assert.That(redirect.RouteValues["checkoutAsGuest"], Is.True);
            Assert.That(nextCalled, Is.False);
        });
    }

    [TestCase(false, true, true)]
    [TestCase(true, false, true)]
    [TestCase(true, true, false)]
    public async Task Disabling_any_required_setting_keeps_the_native_password_login(
        bool gatewayEnabled, bool otpEnabled, bool primarySms)
    {
        var settings = SettingsFactory(gatewayEnabled, otpEnabled, primarySms);
        var context = Context("GET");
        bool nextCalled = false;

        await new SmsPrimaryLoginFilter(settings.Object).OnActionExecutionAsync(context, () =>
        {
            nextCalled = true;
            return Task.FromResult(new ActionExecutedContext(context, context.Filters, context.Controller));
        });

        Assert.That(nextCalled, Is.True);
        Assert.That(context.Result, Is.Null);
    }

    [TestCase("POST", "")]
    [TestCase("GET", "?method=password")]
    [TestCase("GET", "?method=PASSWORD")]
    public async Task Password_posts_and_explicit_password_links_are_never_redirected(string method, string query)
    {
        var settings = new Mock<ISettingFactory>(MockBehavior.Strict);
        var context = Context(method, query);
        bool nextCalled = false;

        await new SmsPrimaryLoginFilter(settings.Object).OnActionExecutionAsync(context, () =>
        {
            nextCalled = true;
            return Task.FromResult(new ActionExecutedContext(context, context.Filters, context.Controller));
        });

        Assert.That(nextCalled, Is.True);
        Assert.That(context.Result, Is.Null);
        settings.Verify(x => x.LoadSettingsAsync<SmsGatewaySettings>(0), Times.Never);
    }

    [TestCase("https://example.org/steal")]
    [TestCase("//example.org/steal")]
    public async Task Unsafe_return_urls_are_discarded(string returnUrl)
    {
        var context = Context("GET", "", returnUrl);

        await new SmsPrimaryLoginFilter(SettingsFactory(true, true, true).Object)
            .OnActionExecutionAsync(context, () => throw new System.InvalidOperationException("Unexpected password action."));

        Assert.That(((RedirectToActionResult)context.Result).RouteValues["returnUrl"], Is.Null);
    }

    private static Mock<ISettingFactory> SettingsFactory(bool enabled, bool otp, bool primary)
    {
        var settings = new Mock<ISettingFactory>();
        settings.Setup(x => x.LoadSettingsAsync<SmsGatewaySettings>(0)).ReturnsAsync(new SmsGatewaySettings
        {
            Enabled = enabled,
            OtpLoginEnabled = otp,
            SmsAsPrimaryLogin = primary
        });
        return settings;
    }

    private static ActionExecutingContext Context(string method, string query = "", string returnUrl = null, bool checkoutAsGuest = false)
    {
        var http = new DefaultHttpContext();
        http.Request.Method = method;
        http.Request.QueryString = new QueryString(query);
        var url = new Mock<IUrlHelper>();
        url.Setup(x => x.IsLocalUrl(It.IsAny<string>())).Returns<string>(value => value == "/checkout");
        var controller = new TestController { Url = url.Object };
        var action = new ActionContext(http, new RouteData(), new ControllerActionDescriptor());
        return new ActionExecutingContext(action, new List<IFilterMetadata>(), new Dictionary<string, object>
        {
            ["returnUrl"] = returnUrl,
            ["checkoutAsGuest"] = checkoutAsGuest
        }, controller);
    }

    private sealed class TestController : Controller
    {
    }
}
