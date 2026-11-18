using System;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Smartstore.Core.Common;
using Smartstore.Core.Identity;

namespace Smartstore.Core.Tests.Platform.Identity;

[TestFixture]
public sealed class AppUserAddressBookServiceTests
{
    [Test]
    public async Task AnonymousUserCannotCreatePersonalAddress()
    {
        var svc = new AppUserAddressBookService(
            new Mock<ICurrentAppUserContext>().Object, null);

        Assert.That(await svc.AddAddressAsync(new Address { Address1 = "Example" }), Is.Null);
    }

    [Test]
    public async Task AnonymousUserCannotReplaceAnotherPersonsAddress()
    {
        var svc = new AppUserAddressBookService(
            new Mock<ICurrentAppUserContext>().Object, null);

        Assert.That(await svc.ReplaceAddressAsync(
            999, new Address { Address1 = "Example" }), Is.False);
    }

    [Test]
    public async Task AnonymousUserCannotRemoveAnotherPersonsAddress()
    {
        var svc = new AppUserAddressBookService(
            new Mock<ICurrentAppUserContext>().Object, null);

        Assert.That(await svc.RemoveAddressAsync(999), Is.False);
    }

    [Test]
    public async Task AnonymousUserCannotSetAnotherPersonsDefaultAddress()
    {
        var svc = new AppUserAddressBookService(
            new Mock<ICurrentAppUserContext>().Object, null);

        Assert.That(await svc.SetDefaultAddressAsync(999), Is.False);
    }
}
