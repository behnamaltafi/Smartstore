using System;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Smartstore.Core.Identity;

namespace Smartstore.Core.Tests.Platform.Identity;

[TestFixture]
public sealed class AppUserOrderAccessServiceTests
{
    [Test]
    public async Task UserWithoutExplicitBuyerMembershipCannotActForBuyer()
    {
        var users = new Mock<ICurrentAppUserContext>();
        users.Setup(x => x.GetCurrentUserAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AppUser { Id = 7 });

        var memberships = new Mock<IAppUserCustomerService>();
        memberships.Setup(x => x.CanAccessCustomerAsync(7, 22, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var sut = new AppUserOrderAccessService(users.Object, memberships.Object, null);

        Assert.That(await sut.ResolveAuthorizedCustomerIdAsync(22), Is.Null);
    }

    [Test]
    public async Task BuyerSelectionUsesAuthenticatedAppUser()
    {
        var users = new Mock<ICurrentAppUserContext>();
        users.Setup(x => x.GetCurrentUserAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AppUser { Id = 7 });

        var memberships = new Mock<IAppUserCustomerService>();
        memberships.Setup(x => x.CanAccessCustomerAsync(7, 22, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var sut = new AppUserOrderAccessService(users.Object, memberships.Object, null);

        Assert.That(await sut.ResolveAuthorizedCustomerIdAsync(22), Is.EqualTo(22));
        memberships.Verify(x => x.CanAccessCustomerAsync(7, 22, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task MissingAppUserCannotSelectCommercialBuyer()
    {
        var sut = new AppUserOrderAccessService(
            new Mock<ICurrentAppUserContext>().Object,
            new Mock<IAppUserCustomerService>().Object,
            null);

        Assert.That(await sut.ResolveAuthorizedCustomerIdAsync(22), Is.Null);
    }
}
