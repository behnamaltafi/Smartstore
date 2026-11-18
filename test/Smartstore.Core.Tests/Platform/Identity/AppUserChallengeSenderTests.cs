using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Moq;
using NUnit.Framework;
using Smartstore.Core.Identity;
using Smartstore.Core.Messaging;

namespace Smartstore.Core.Tests.Platform.Identity;

[TestFixture]
public sealed class AppUserChallengeSenderTests
{
    private static IConfiguration EmptyConfiguration()
        => new ConfigurationBuilder().Build();

    [Test]
    public async Task SmsOtpUsesTheSingleIndependentGateway()
    {
        var gateway = new Mock<ISmsSender>();
        SmsSendRequest captured = null;
        gateway.Setup(x => x.SendAsync(It.IsAny<SmsSendRequest>(), It.IsAny<CancellationToken>()))
            .Callback<SmsSendRequest, CancellationToken>((request, _) => captured = request)
            .ReturnsAsync(new SmsSendResult("Kavenegar", "12345"));

        var sender = new ConfiguredAppUserChallengeSender(
            EmptyConfiguration(), new[] { gateway.Object });

        var sent = await sender.SendOtpAsync("+989121234567", "123456");

        Assert.Multiple(() =>
        {
            Assert.That(sent, Is.True);
            Assert.That(captured, Is.Not.Null);
            Assert.That(captured.PhoneNumber, Is.EqualTo("+989121234567"));
            Assert.That(captured.Text, Does.Contain("123456"));
        });

        gateway.Verify(x => x.SendAsync(It.IsAny<SmsSendRequest>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task SmsOtpFailsClosedWhenModuleIsNotInstalled()
    {
        var sender = new ConfiguredAppUserChallengeSender(
            EmptyConfiguration(), Enumerable.Empty<ISmsSender>());

        Assert.That(await sender.SendOtpAsync("+989121234567", "123456"), Is.False);
    }

    [Test]
    public async Task SmsOtpNeverRetriesAmbiguousFailure()
    {
        var gateway = new Mock<ISmsSender>();
        gateway.Setup(x => x.SendAsync(It.IsAny<SmsSendRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Delivery status unknown"));

        var sender = new ConfiguredAppUserChallengeSender(
            EmptyConfiguration(), new[] { gateway.Object });

        Assert.That(await sender.SendOtpAsync("+989121234567", "123456"), Is.False);
        gateway.Verify(x => x.SendAsync(It.IsAny<SmsSendRequest>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
