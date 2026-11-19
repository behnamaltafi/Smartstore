using System;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using Smartstore.Core.Data;
using Smartstore.Core.Identity;
using Smartstore.Core.Messaging;
using Smartstore.Core.Security;
using Smartstore.Events;
using Smartstore.MultiSms.Controllers;
using Smartstore.MultiSms.Models;
using Smartstore.MultiSms.Services;

namespace Smartstore.Core.Tests.Platform.Messaging;

[TestFixture]
public class SmsApiControllerTests
{
    [Test]
    public async Task Login_requests_return_the_same_response_for_known_and_unknown_numbers()
    {
        var otp = new Mock<ISmsOtpLoginService>();
        otp.Setup(x => x.IsEnabledAsync()).ReturnsAsync(true);
        var controller = CreateLoginController(otp);

        var known = (OkObjectResult)await controller.RequestCode(new SmsCodeRequest { Phone = "09123456789" }, default);
        var unknown = (OkObjectResult)await controller.RequestCode(new SmsCodeRequest { Phone = "09351234567" }, default);

        Assert.That(JsonSerializer.Serialize(unknown.Value), Is.EqualTo(JsonSerializer.Serialize(known.Value)));
        otp.Verify(x => x.RequestLoginCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Test]
    public async Task Disabled_login_does_not_issue_codes()
    {
        var otp = new Mock<ISmsOtpLoginService>();
        var result = await CreateLoginController(otp).RequestCode(new SmsCodeRequest { Phone = "09123456789" }, default);

        Assert.That(result, Is.TypeOf<NotFoundResult>());
        otp.Verify(x => x.RequestLoginCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task Anonymous_enrollment_is_rejected_without_issuing_or_confirming_codes()
    {
        var otp = new Mock<ISmsOtpLoginService>();
        otp.Setup(x => x.IsEnabledAsync()).ReturnsAsync(true);
        var controller = CreateLoginController(otp);

        Assert.That(await controller.RequestEnrollment(new SmsCodeRequest(), default), Is.TypeOf<UnauthorizedResult>());
        Assert.That(await controller.ConfirmEnrollment(new SmsCodeVerificationRequest(), default), Is.TypeOf<UnauthorizedResult>());
        otp.Verify(x => x.RequestEnrollmentCodeAsync(It.IsAny<Customer>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        otp.Verify(x => x.ConfirmEnrollmentAsync(It.IsAny<Customer>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task Enrollment_uses_the_authenticated_customer()
    {
        var otp = new Mock<ISmsOtpLoginService>();
        otp.Setup(x => x.IsEnabledAsync()).ReturnsAsync(true);
        var customer = RegisteredCustomer();
        var users = CreateUserManager();
        users.Setup(x => x.GetUserAsync(It.IsAny<ClaimsPrincipal>())).ReturnsAsync(customer);
        var signIn = CreateSignInManager(users);
        signIn.Setup(x => x.IsSignedIn(It.IsAny<ClaimsPrincipal>())).Returns(true);
        var controller = CreateLoginController(otp, users, signIn);

        var result = await controller.RequestEnrollment(new SmsCodeRequest { Phone = "09123456789" }, default);

        Assert.That(result, Is.TypeOf<OkObjectResult>());
        otp.Verify(x => x.RequestEnrollmentCodeAsync(customer, "09123456789", It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestCase("locked")]
    [TestCase("two_factor")]
    [TestCase("inactive")]
    [TestCase("deleted")]
    [TestCase("not_allowed")]
    [TestCase("unknown")]
    public async Task Login_never_signs_in_an_ineligible_customer(string reason)
    {
        var customer = RegisteredCustomer();
        customer.Active = reason != "inactive";
        customer.Deleted = reason == "deleted";
        var otp = new Mock<ISmsOtpLoginService>();
        otp.Setup(x => x.IsEnabledAsync()).ReturnsAsync(true);
        otp.Setup(x => x.ConfirmLoginAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(reason == "unknown" ? null : customer);
        var users = CreateUserManager();
        users.Setup(x => x.IsLockedOutAsync(customer)).ReturnsAsync(reason == "locked");
        users.Setup(x => x.GetTwoFactorEnabledAsync(customer)).ReturnsAsync(reason == "two_factor");
        var signIn = CreateSignInManager(users);
        signIn.Setup(x => x.CanSignInAsync(customer)).ReturnsAsync(reason != "not_allowed");

        var result = await CreateLoginController(otp, users, signIn).VerifyCode(
            new SmsCodeVerificationRequest { Phone = "09123456789", Code = "123456" }, default);

        Assert.That(result, Is.TypeOf<BadRequestObjectResult>());
        signIn.Verify(x => x.SignInAsync(It.IsAny<Customer>(), It.IsAny<bool>(), It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task Verified_login_uses_a_non_persistent_native_cookie_and_updates_last_login()
    {
        var customer = RegisteredCustomer();
        var otp = new Mock<ISmsOtpLoginService>();
        otp.Setup(x => x.IsEnabledAsync()).ReturnsAsync(true);
        otp.Setup(x => x.ConfirmLoginAsync("09123456789", "123456", It.IsAny<CancellationToken>())).ReturnsAsync(customer);
        var users = CreateUserManager();
        var signIn = CreateSignInManager(users);
        signIn.Setup(x => x.CanSignInAsync(customer)).ReturnsAsync(true);
        using var db = new Mock<SmartDbContext>(new DbContextOptions<SmartDbContext>()).Object;
        Mock.Get(db).Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        var controller = CreateLoginController(otp, users, signIn, db);

        var result = await controller.VerifyCode(new SmsCodeVerificationRequest { Phone = "09123456789", Code = "123456" }, default);

        Assert.That(result, Is.TypeOf<OkObjectResult>());
        Assert.That(customer.LastLoginDateUtc, Is.Not.Null);
        signIn.Verify(x => x.SignInAsync(customer, false, null), Times.Once);
        Mock.Get(db).Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task Session_returns_a_csrf_token_and_never_reads_a_guests_phone()
    {
        var otp = new Mock<ISmsOtpLoginService>();
        otp.Setup(x => x.IsEnabledAsync()).ReturnsAsync(true);
        var antiforgery = new Mock<IAntiforgery>();
        antiforgery.Setup(x => x.GetAndStoreTokens(It.IsAny<HttpContext>()))
            .Returns(new AntiforgeryTokenSet("request-token", "cookie-token", "__RequestVerificationToken", "X-XSRF-Token"));
        var users = CreateUserManager();
        var controller = new SmsLoginApiController(otp.Object, CreateSignInManager(users).Object, users.Object, null, antiforgery.Object, Mock.Of<IEventPublisher>())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        var result = (OkObjectResult)await controller.Session(default);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result.Value));
        Assert.That(json.RootElement.GetProperty("requestToken").GetString(), Is.EqualTo("request-token"));
        Assert.That(json.RootElement.GetProperty("isAuthenticated").GetBoolean(), Is.False);
        otp.Verify(x => x.GetVerifiedPhoneAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestCase(false, false, false, 401)]
    [TestCase(true, false, false, 403)]
    [TestCase(true, true, false, 403)]
    [TestCase(true, false, true, 403)]
    public async Task Test_delivery_requires_a_native_session_backend_access_and_settings_permission(
        bool authenticated, bool backendAccess, bool settingsAccess, int status)
    {
        var customer = RegisteredCustomer();
        var users = CreateUserManager();
        users.Setup(x => x.GetUserAsync(It.IsAny<ClaimsPrincipal>())).ReturnsAsync(customer);
        var signIn = CreateSignInManager(users);
        signIn.Setup(x => x.IsSignedIn(It.IsAny<ClaimsPrincipal>())).Returns(authenticated);
        var permissions = new Mock<IPermissionService>();
        permissions.Setup(x => x.AuthorizeAsync(Permissions.System.AccessBackend, customer, false)).ReturnsAsync(backendAccess);
        permissions.Setup(x => x.AuthorizeAsync("configuration.setting", customer, false)).ReturnsAsync(settingsAccess);
        var sender = new Mock<ISmsSender>(MockBehavior.Strict);
        var controller = new MultiSmsApiController(sender.Object, users.Object, signIn.Object, permissions.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        var result = await controller.TestSms(new SmsTestRequest { Phone = "09123456789", Text = "Test" }, default);

        int? actual = result is ObjectResult objectResult ? objectResult.StatusCode : ((StatusCodeResult)result).StatusCode;
        Assert.That(actual, Is.EqualTo(status));
        sender.Verify(x => x.SendAsync(It.IsAny<SmsSendRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static SmsLoginApiController CreateLoginController(Mock<ISmsOtpLoginService> otp,
        Mock<UserManager<Customer>> users = null, Mock<SignInManager<Customer>> signIn = null, SmartDbContext db = null)
    {
        users ??= CreateUserManager();
        signIn ??= CreateSignInManager(users);
        return new SmsLoginApiController(otp.Object, signIn.Object, users.Object, db, Mock.Of<IAntiforgery>(), Mock.Of<IEventPublisher>())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
    }

    private static Customer RegisteredCustomer()
    {
        var customer = new Customer { Id = 42, Active = true };
        customer.CustomerRoleMappings.Add(new CustomerRoleMapping
        {
            CustomerRole = new CustomerRole { Active = true, SystemName = SystemCustomerRoleNames.Registered }
        });
        return customer;
    }

    private static Mock<UserManager<Customer>> CreateUserManager()
        => new(Mock.Of<IUserStore<Customer>>(), Options.Create(new IdentityOptions()),
            Mock.Of<IPasswordHasher<Customer>>(), Array.Empty<IUserValidator<Customer>>(),
            Array.Empty<IPasswordValidator<Customer>>(), Mock.Of<ILookupNormalizer>(), new IdentityErrorDescriber(),
            Mock.Of<IServiceProvider>(), NullLogger<UserManager<Customer>>.Instance);

    private static Mock<SignInManager<Customer>> CreateSignInManager(Mock<UserManager<Customer>> users)
        => new(users.Object, Mock.Of<IHttpContextAccessor>(), Mock.Of<IUserClaimsPrincipalFactory<Customer>>(),
            Options.Create(new IdentityOptions()), NullLogger<SignInManager<Customer>>.Instance,
            Mock.Of<IAuthenticationSchemeProvider>(), Mock.Of<IUserConfirmation<Customer>>());
}
