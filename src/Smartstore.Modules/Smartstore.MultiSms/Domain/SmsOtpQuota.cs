using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Smartstore.Domain;

namespace Smartstore.MultiSms.Domain;

/// <summary>
/// Persistent cross-node quota. An authenticated account cannot generate
/// unlimited SMS sends by requesting codes for different mobile numbers.
/// </summary>
[Table("MultiSmsOtpQuota")]
[Index(nameof(QuotaKey), IsUnique = true, Name = "UX_MultiSmsOtpQuota_QuotaKey")]
public sealed class SmsOtpQuota : BaseEntity
{
    [Required, MaxLength(64)]
    public string QuotaKey { get; set; }

    public DateTime WindowStartUtc { get; set; }
    public DateTime LastSendUtc { get; set; }
    public int SentCount { get; set; }

    [ConcurrencyCheck]
    public int Version { get; set; }
}
