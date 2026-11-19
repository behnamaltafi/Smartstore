using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using Smartstore.Core.Configuration;
using Smartstore.Core.Messaging;
using Smartstore.MultiSms.Configuration;
using Smartstore.MultiSms.Domain;
using Smartstore.MultiSms.Services;

namespace Smartstore.Core.Tests.Platform.Messaging;

[TestFixture]
public class SmsHistoryTransportTests
{
    private static readonly SmsSendRequest Otp = new("09123456789", "Secret 123456", SmsMessageKind.Otp);

    [Test]
    public async Task History_failure_does_not_turn_accepted_SMS_into_retry_or_failure()
    {
        var settings = Config();
        var history = new Mock<ISmsHistoryWriter>();
        history.Setup(x => x.BeginAsync(It.IsAny<SmsSendRequest>(),
                It.IsAny<SmsGatewaySettings>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SmsMessage { Id = 12 });
        history.Setup(x => x.RecordAttemptAsync(It.IsAny<SmsMessage>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("SQL audit unavailable"));

        var primary = new StubProvider("Kavenegar");
        var fallback = new StubProvider("Melipayamak");
        var sender = CreateSender(settings, history.Object, primary, fallback);

        var result = await sender.SendAsync(Otp);

        Assert.Multiple(() =>
        {
            Assert.That(result.Provider, Is.EqualTo("Kavenegar"));
            Assert.That(primary.Calls, Is.EqualTo(1));
            Assert.That(fallback.Calls, Is.Zero);
        });
    }

    [Test]
    public async Task Failed_history_initialization_does_not_prevent_real_transport()
    {
        var history = new Mock<ISmsHistoryWriter>();
        history.Setup(x => x.BeginAsync(It.IsAny<SmsSendRequest>(),
                It.IsAny<SmsGatewaySettings>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Storage unavailable"));

        var provider = new StubProvider("Kavenegar");
        var sender = CreateSender(Config(), history.Object, provider);
        var receipt = await sender.SendAsync(Otp);

        Assert.That(receipt.Provider, Is.EqualTo("Kavenegar"));
        Assert.That(provider.Calls, Is.EqualTo(1));
    }

    [Test]
    public async Task Safe_failover_audits_both_provider_attempts_under_one_message()
    {
        var config = Config();
        var primary = new StubProvider("Kavenegar")
        {
            Failure = new SmsProviderException("Rejected", safeToFailover: true)
        };
        var fallback = new StubProvider("Melipayamak");
        var history = new Mock<ISmsHistoryWriter>();
        var message = new SmsMessage { Id = 42 };
        var events = new List<string>();

        history.Setup(x => x.BeginAsync(It.IsAny<SmsSendRequest>(),
                It.IsAny<SmsGatewaySettings>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(message);

        history.Setup(x => x.RecordAttemptAsync(It.IsAny<SmsMessage>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .Callback<SmsMessage, string, string, string, string, DateTime, int, CancellationToken>(
                (m, provider, status, id, error, started, ms, ct) =>
                {
                    Assert.That(m, Is.SameAs(message));
                    events.Add(provider + ":" + status);
                })
            .Returns(Task.CompletedTask);

        var receipt = await CreateSender(config, history.Object, primary, fallback).SendAsync(Otp);

        Assert.Multiple(() =>
        {
            Assert.That(receipt.Provider, Is.EqualTo("Melipayamak"));
            Assert.That(events, Is.EqualTo(new[] { "Kavenegar:Rejected", "Melipayamak:Accepted" }));
            Assert.That(primary.Calls, Is.EqualTo(1));
            Assert.That(fallback.Calls, Is.EqualTo(1));
        });
    }

    [Test]
    public void Ambiguous_provider_failure_audits_unknown_and_never_attempts_fallback()
    {
        var primary = new StubProvider("Kavenegar") { Failure = new HttpRequestException("Timeout") };
        var fallback = new StubProvider("Melipayamak");
        var history = new Mock<ISmsHistoryWriter>();
        history.Setup(x => x.BeginAsync(It.IsAny<SmsSendRequest>(),
                It.IsAny<SmsGatewaySettings>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SmsMessage { Id = 1 });

        var sender = CreateSender(Config(), history.Object, primary, fallback);
        Assert.ThrowsAsync<HttpRequestException>(async () => await sender.SendAsync(Otp));

        history.Verify(x => x.RecordAttemptAsync(It.IsAny<SmsMessage>(), "Kavenegar",
            "Unknown", null, "Transport", It.IsAny<DateTime>(),
            It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);

        Assert.That(fallback.Calls, Is.Zero);
    }

    private static SmsGatewaySender CreateSender(
        SmsGatewaySettings settings, ISmsHistoryWriter history, params ISmsProvider[] providers)
    {
        var factory = new Mock<ISettingFactory>();
        factory.Setup(x => x.LoadSettingsAsync<SmsGatewaySettings>(0))
            .ReturnsAsync(settings);
        return new SmsGatewaySender(factory.Object, providers,
            NullLogger<SmsGatewaySender>.Instance, history);
    }

    private static SmsGatewaySettings Config() => new()
    {
        Enabled = true, HistoryEnabled = true,
        PrimaryProvider = "Kavenegar", EnableFailover = true,
        FallbackProvider = "Melipayamak"
    };

    private sealed class StubProvider : ISmsProvider
    {
        public StubProvider(string name) => Name = name;
        public string Name { get; }
        public int Calls { get; private set; }
        public Exception Failure { get; set; }

        public bool IsAvailable(SmsGatewaySettings settings) => true;

        public Task<string> SendAsync(string phone, string text,
            SmsGatewaySettings settings, CancellationToken cancelToken)
        {
            Calls++;
            if (Failure is not null) throw Failure;
            return Task.FromResult("test-receipt");
        }
    }
}
