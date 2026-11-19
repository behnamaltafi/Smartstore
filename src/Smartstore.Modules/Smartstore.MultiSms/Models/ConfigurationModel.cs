namespace Smartstore.MultiSms.Models;

[LocalizedDisplay("Plugins.Sms.MultiSms.Fields.")]
public sealed class ConfigurationModel : ModelBase
{
    [LocalizedDisplay("*Enabled")]
    public bool Enabled { get; set; }

    [LocalizedDisplay("*OtpLoginEnabled")]
    public bool OtpLoginEnabled { get; set; }

    /// <summary>Gets or sets whether SMS is the primary storefront sign-in method.</summary>
    [LocalizedDisplay("*SmsAsPrimaryLogin")]
    public bool SmsAsPrimaryLogin { get; set; }

    [LocalizedDisplay("*MockEnabled")]
    public bool MockEnabled { get; set; }

    [LocalizedDisplay("*HistoryEnabled")]
    public bool HistoryEnabled { get; set; }

    [LocalizedDisplay("*HistoryRetentionDays")]
    public int HistoryRetentionDays { get; set; } = 90;

    [LocalizedDisplay("*StoreNonOtpMessageBody")]
    public bool StoreNonOtpMessageBody { get; set; }

    public bool DevelopmentMode { get; set; }

    [LocalizedDisplay("*PrimaryProvider")]
    public string PrimaryProvider { get; set; } = "Kavenegar";

    [LocalizedDisplay("*EnableFailover")]
    public bool EnableFailover { get; set; }

    [LocalizedDisplay("*FallbackProvider")]
    public string FallbackProvider { get; set; } = "Melipayamak";

    [LocalizedDisplay("*KavenegarEnabled")]
    public bool KavenegarEnabled { get; set; }

    [LocalizedDisplay("*KavenegarSender")]
    public string KavenegarSender { get; set; }

    [LocalizedDisplay("*NewKavenegarApiKey")]
    public string NewKavenegarApiKey { get; set; }

    [LocalizedDisplay("*ClearKavenegarApiKey")]
    public bool ClearKavenegarApiKey { get; set; }

    public bool HasKavenegarApiKey { get; set; }

    [LocalizedDisplay("*MelipayamakEnabled")]
    public bool MelipayamakEnabled { get; set; }

    [LocalizedDisplay("*MelipayamakSender")]
    public string MelipayamakSender { get; set; }

    [LocalizedDisplay("*MelipayamakUsername")]
    public string MelipayamakUsername { get; set; }

    [LocalizedDisplay("*NewMelipayamakPassword")]
    public string NewMelipayamakPassword { get; set; }

    [LocalizedDisplay("*ClearMelipayamakPassword")]
    public bool ClearMelipayamakPassword { get; set; }

    public bool HasMelipayamakPassword { get; set; }

    [LocalizedDisplay("*TestPhone")]
    public string TestPhone { get; set; }

    [LocalizedDisplay("*TestMessage")]
    public string TestMessage { get; set; }

    public string[] AvailableProviders { get; set; } = [];

    public bool? TestSucceeded { get; set; }
    public string TestResult { get; set; }
}
