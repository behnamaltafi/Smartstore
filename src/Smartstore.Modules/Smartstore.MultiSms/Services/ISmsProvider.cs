using Smartstore.MultiSms.Configuration;

namespace Smartstore.MultiSms.Services;

public interface ISmsProvider
{
    string Name { get; }
    bool IsAvailable(SmsGatewaySettings settings);
    Task<string> SendAsync(string phone, string text, SmsGatewaySettings settings, CancellationToken cancelToken);
}

/// <summary>
/// Only explicit provider rejection is safe for automatic failover.
/// Timeouts, connection resets and malformed responses are ambiguous: the SMS may already exist.
/// </summary>
public sealed class SmsProviderException : Exception
{
    public SmsProviderException(string message, bool safeToFailover = false)
        : base(message)
    {
        SafeToFailover = safeToFailover;
    }

    public bool SafeToFailover { get; }
}
