using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Smartstore.Domain;

namespace Smartstore.MultiSms.Domain;

/// <summary>
/// Shared SQL-backed OTP state; cryptographic codes are protected by the
/// application key ring and never stored as plaintext.
/// </summary>
[Table("MultiSmsOtpChallenge")]
[Index(nameof(Phone), nameof(Purpose), IsUnique = true, Name = "UX_MultiSmsOtpChallenge_Phone_Purpose")]
public sealed class SmsOtpChallenge : BaseEntity
{
    [Required, MaxLength(16)]
    public string Phone { get; set; }

    [Required, MaxLength(80)]
    public string Purpose { get; set; }

    [MaxLength(1024)]
    public string ProtectedCode { get; set; }

    public DateTime ExpiresOnUtc { get; set; }
    public DateTime LastSentOnUtc { get; set; }
    public DateTime WindowStartUtc { get; set; }
    public int SentInWindow { get; set; }
    public int FailedAttempts { get; set; }

    // Optimistic concurrency also protects single-use consumption.
    [ConcurrencyCheck]
    public int Version { get; set; }
}
