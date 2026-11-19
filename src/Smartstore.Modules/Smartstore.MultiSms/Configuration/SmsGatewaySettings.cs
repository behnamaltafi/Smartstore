using Smartstore.Core.Configuration;

namespace Smartstore.MultiSms.Configuration;

/// <summary>
/// Settings are loaded afresh through Smartstore's settings cache on each send.
/// Secrets must contain ASP.NET Core Data Protection ciphertext, not plaintext.
/// </summary>
public sealed class SmsGatewaySettings : ISettings
{
    public bool Enabled { get; set; }
    public bool OtpLoginEnabled { get; set; }
    /// <summary>Only effective when the process is running in Development.</summary>
    public bool MockEnabled { get; set; }
    public bool HistoryEnabled { get; set; } = true;
    public int HistoryRetentionDays { get; set; } = 90;
    /// <summary>OTP and test bodies are never persisted regardless of this flag.</summary>
    public bool StoreNonOtpMessageBody { get; set; }
    /// <summary>Uses SMS as the default storefront login when SMS and OTP are enabled.</summary>
    public bool SmsAsPrimaryLogin { get; set; } = true;
    public string PrimaryProvider { get; set; } = "Kavenegar";
    public bool EnableFailover { get; set; }
    public string FallbackProvider { get; set; } = "Melipayamak";
    public bool KavenegarEnabled { get; set; } = true;
    public string KavenegarSender { get; set; }
    public string EncryptedKavenegarApiKey { get; set; }
    public bool MelipayamakEnabled { get; set; }
    public string MelipayamakSender { get; set; }
    public string MelipayamakUsername { get; set; }
    public string EncryptedMelipayamakPassword { get; set; }
}
