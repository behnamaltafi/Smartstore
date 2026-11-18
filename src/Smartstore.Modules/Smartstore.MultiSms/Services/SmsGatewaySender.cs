using Microsoft.Extensions.Logging;
using Smartstore.Core.Configuration;
using Smartstore.Core.Messaging;
using Smartstore.MultiSms.Configuration;

namespace Smartstore.MultiSms.Services;

/// <summary>
/// Runtime selection and failover. Each send obtains current settings rather than caching
/// the selected provider in a singleton, so changes take effect without application restart.
/// </summary>
public sealed class SmsGatewaySender : ISmsSender
{
    private readonly ISettingFactory _settings;
    private readonly IReadOnlyDictionary<string, ISmsProvider> _providers;
    private readonly ILogger<SmsGatewaySender> _logger;

    public SmsGatewaySender(ISettingFactory settings, IEnumerable<ISmsProvider> providers, ILogger<SmsGatewaySender> logger)
    {
        _settings = settings;
        _providers = providers.ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);
        _logger = logger;
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

        try
        {
            if (!primary.IsAvailable(settings))
            {
                throw new SmsProviderException("Primary SMS provider is not enabled or configured.", safeToFailover: true);
            }

            var messageId = await primary.SendAsync(request.PhoneNumber, request.Text, settings, cancelToken);
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

            _logger.LogWarning("SMS provider {Provider} explicitly rejected delivery; switching to {Fallback}.",
                primary.Name, fallback.Name);
            var fallbackId = await fallback.SendAsync(request.PhoneNumber, request.Text, settings, cancelToken);
            return new SmsSendResult(fallback.Name, fallbackId);
        }
    }
}
