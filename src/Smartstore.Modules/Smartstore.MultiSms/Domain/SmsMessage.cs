using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Smartstore.Domain;

namespace Smartstore.MultiSms.Domain;

/// <summary>Persisted message metadata. OTP content and credentials are NEVER written here.</summary>
[Table("MultiSmsMessage")]
[Index(nameof(CreatedOnUtc), Name = "IX_MultiSmsMessage_Created")]
[Index(nameof(Phone), nameof(CreatedOnUtc), Name = "IX_MultiSmsMessage_Phone_Created")]
public sealed class SmsMessage : BaseEntity
{
    [Required, MaxLength(20)]
    public string Phone { get; set; }

    [Required, MaxLength(20)]
    public string Kind { get; set; }

    [Required, MaxLength(20)]
    public string Status { get; set; }

    [MaxLength(50)]
    public string Provider { get; set; }

    [MaxLength(150)]
    public string ProviderMessageId { get; set; }

    [MaxLength(1500)]
    public string Body { get; set; }

    public DateTime CreatedOnUtc { get; set; }
    public DateTime? FinishedOnUtc { get; set; }
    public int AttemptCount { get; set; }
}
