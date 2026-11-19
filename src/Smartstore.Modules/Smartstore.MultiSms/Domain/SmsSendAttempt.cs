using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Smartstore.Domain;

namespace Smartstore.MultiSms.Domain;

/// <summary>One transport attempt; provider error details and SMS bodies are excluded.</summary>
[Table("MultiSmsSendAttempt")]
[Index(nameof(SmsMessageId), Name = "IX_MultiSmsSendAttempt_Message")]
public sealed class SmsSendAttempt : BaseEntity
{
    public int SmsMessageId { get; set; }

    [Required, MaxLength(50)]
    public string Provider { get; set; }

    [Required, MaxLength(20)]
    public string Status { get; set; }

    [MaxLength(150)]
    public string ProviderMessageId { get; set; }

    [MaxLength(60)]
    public string ErrorCategory { get; set; }

    public DateTime StartedOnUtc { get; set; }
    public int DurationMs { get; set; }
}
