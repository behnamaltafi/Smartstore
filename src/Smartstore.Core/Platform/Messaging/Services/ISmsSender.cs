namespace Smartstore.Core.Messaging;

/// <summary>
/// Sends an SMS without exposing any vendor-specific implementation to callers.
/// </summary>
public interface ISmsSender
{
    /// <summary>
    /// Sends a single SMS to one recipient and returns an accepted message identifier.
    /// A successful result means accepted by the provider, not delivered to the handset.
    /// </summary>
    Task<SmsSendResult> SendAsync(SmsSendRequest request, CancellationToken cancelToken = default);
}

/// <summary>
/// A single recipient and message body. OTP creation and verification are deliberately separate.
/// </summary>
public enum SmsMessageKind { General, Otp, Test }

/// <summary>Mark OTP explicitly so audit storage never persists authentication secrets.</summary>
public sealed record SmsSendRequest(
    string PhoneNumber, string Text, SmsMessageKind Kind = SmsMessageKind.General);

/// <summary>
/// The provider that accepted the message and its provider-generated identifier.
/// </summary>
public sealed record SmsSendResult(string Provider, string MessageId);
