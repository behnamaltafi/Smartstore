using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Moq;
using NUnit.Framework;
using Smartstore.Core.Identity;

namespace Smartstore.Core.Tests.Platform.Identity;

[TestFixture]
public class AppUserBuyerContextTests
{
    [Test]
    public async Task ForgedBuyerCookieWithoutMembershipIsRejected()
    {
        var http = new DefaultHttpContext();
        http.Request.Headers.Cookie = "smartstore.selected-customer=42";

        var users = new Mock<ICurrentAppUserContext>();
        users.SetupGet(x => x.IsAppUserIdentityPresent).Returns(true);
        users.Setup(x => x.GetCurrentUserAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AppUser { Id = 10 });

        var links = new Mock<IAppUserCustomerService>();
        links.Setup(x => x.CanAccessCustomerAsync(10, 42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var sut = new AppUserBuyerContext(
            new HttpContextAccessor { HttpContext = http }, users.Object, links.Object);

        Assert.That(await sut.GetSelectedCustomerIdAsync(), Is.Null);
    }

    [Test]
    public async Task OnlyAuthorizedBuyerIsReturned()
    {
        var http = new DefaultHttpContext();
        http.Request.Headers.Cookie = "smartstore.selected-customer=42";

        var users = new Mock<ICurrentAppUserContext>();
        users.SetupGet(x => x.IsAppUserIdentityPresent).Returns(true);
        users.Setup(x => x.GetCurrentUserAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AppUser { Id = 10 });

        var links = new Mock<IAppUserCustomerService>();
        links.Setup(x => x.CanAccessCustomerAsync(10, 42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var sut = new AppUserBuyerContext(
            new HttpContextAccessor { HttpContext = http }, users.Object, links.Object);

        Assert.That(await sut.GetSelectedCustomerIdAsync(), Is.EqualTo(42));
    }

    [Test]
    public async Task BuyerCannotBeSelectedWithoutAuthenticatedAppUser()
    {
        var users = new Mock<ICurrentAppUserContext>();
        var sut = new AppUserBuyerContext(
            new HttpContextAccessor { HttpContext = new DefaultHttpContext() },
            users.Object, new Mock<IAppUserCustomerService>().Object);

        Assert.That(await sut.SelectCustomerAsync(42), Is.False);
    }
}
