using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Smartstore.Core.Configuration;
using Smartstore.Core.Messaging;
using Smartstore.MultiSms.Configuration;
using Smartstore.MultiSms.Domain;

namespace Smartstore.MultiSms.Services;

/// <summary>
/// Runtime-selected transport with best-effort history for EVERY provider attempt.
/// Never sends a second SMS after ambiguous timeouts or connection failures.
/// </summary>
public sealed class SmsGatewaySender : ISmsSender
{
    private readonly ISettingFactory _settings;
    private readonly IReadOnlyDictionary<string, ISmsProvider> _providers;
    private readonly ILogger<SmsGatewaySender> _logger;
    private readonly ISmsHistoryWriter _history;

    // Optional history parameter keeps existing isolated sender unit tests compatible.
    public SmsGatewaySender(
        ISettingFactory settings,
        IEnumerable<ISmsProvider> providers,
        ILogger<SmsGatewaySender> logger,
        ISmsHistoryWriter history = null)
    {
        _settings = settings;
        _providers = providers.ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);
        _logger = logger;
        _history = history;
    }

    public async Task<SmsSendResult> SendAsync(SmsSendRequest request, CancellationToken cancelToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.PhoneNumber) || request.PhoneNumber.Length > 20 ||
            !request.PhoneNumber.Select((ch, i) => (ch >= '0' && ch <= '9') || (i == 0 && ch == '+')).All(valid => valid) ||
            string.IsNullOrWhiteSpace(request.Text) || request.Text.Length > 1500)
        {
            throw new ArgumentException("Invalid SMS destination or message.");
        }

        var settings = await _settings.LoadSettingsAsync<SmsGatewaySettings>();
        if (!settings.Enabled)
        {
            throw new InvalidOperationException("SMS delivery is disabled.");
        }

        if (!_providers.TryGetValue(settings.PrimaryProvider ?? "", out var primary))
        {
            throw new InvalidOperationException("Primary SMS provider is not recognized.");
        }

        // A Development configuration copied to production is never allowed to
        // silently fall back from Mock to a real payable SMS transport.
        if (string.Equals(primary.Name, "Mock", StringComparison.OrdinalIgnoreCase) &&
            !primary.IsAvailable(settings))
        {
            throw new InvalidOperationException("Mock SMS transport can only run in Development.");
        }

        SmsMessage historyMessage = null;
        if (_history is not null)
        {
            try
            {
                historyMessage = await _history.BeginAsync(request, settings, cancelToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A history database outage must not prevent a real SMS from being sent.
                _logger.LogWarning("Could not initialize SMS history; continuing with provider.");
            }
        }

        try
        {
            var messageId = await AttemptAsync(primary, request, settings, historyMessage, cancelToken);
            return new SmsSendResult(primary.Name, messageId);
        }
        catch (SmsProviderException ex) when (ex.SafeToFailover && settings.EnableFailover && !cancelToken.IsCancellationRequested)
        {
            if (string.Equals(primary.Name, settings.FallbackProvider, StringComparison.OrdinalIgnoreCase) ||
                !_providers.TryGetValue(settings.FallbackProvider ?? "", out var fallback) ||
                !fallback.IsAvailable(settings))
            {
                throw;
            }

            _logger.LogWarning(
                "SMS provider {Provider} definitely rejected delivery; switching to {Fallback}.",
                primary.Name, fallback.Name);
            var fallbackId = await AttemptAsync(fallback, request, settings, historyMessage, cancelToken);
            return new SmsSendResult(fallback.Name, fallbackId);
        }
    }

    private async Task<string> AttemptAsync(
        ISmsProvider provider, SmsSendRequest request, SmsGatewaySettings settings,
        SmsMessage message, CancellationToken ct)
    {
        var startedOnUtc = DateTime.UtcNow;
        var timer = Stopwatch.StartNew();
        string messageId;

        // Only provider-side failures may qualify for failover. The optional
        // database audit is always performed *outside* the provider try/catch.
        try
        {
            if (!provider.IsAvailable(settings))
            {
                throw new SmsProviderException("SMS provider is unavailable or not configured.", safeToFailover: true);
            }

            messageId = await provider.SendAsync(request.PhoneNumber, request.Text, settings, ct);
        }
        catch (Exception ex)
        {
            // Cancellation may happen after the provider accepted the SMS.
            // Keep an Unknown audit result, but never retry it.
            timer.Stop();
            await RecordSafelyAsync(message, provider.Name, SmsHistoryFormatter.FailedStatus(ex),
                null, SmsHistoryFormatter.SafeCategory(ex), startedOnUtc,
                (int)Math.Min(timer.ElapsedMilliseconds, int.MaxValue));
            throw;
        }

        timer.Stop();
        await RecordSafelyAsync(message, provider.Name, "Accepted", messageId,
            null, startedOnUtc, (int)Math.Min(timer.ElapsedMilliseconds, int.MaxValue));
        return messageId;
    }

    private async Task RecordSafelyAsync(SmsMessage message, string provider,
        string status, string providerMessageId, string errorCategory,
        DateTime startedOnUtc, int durationMs)
    {
        if (_history is null || message is null) return;

        try
        {
            await _history.RecordAttemptAsync(message, provider, status, providerMessageId,
                errorCategory, startedOnUtc, durationMs, CancellationToken.None);
        }
        catch (Exception)
        {
            // Never turn an accepted SMS into a transport exception or duplicate send.
            _logger.LogWarning("SMS attempt audit could not be recorded.");
        }
    }
}
