using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Smartstore.Core.Identity;

/// <summary>
/// Represents an application user independently of any commerce customer.
/// </summary>
[Index(nameof(UserGuid), IsUnique = true, Name = "UX_AppUser_UserGuid")]
[Index(nameof(Username), IsUnique = true, Name = "UX_AppUser_Username")]
[Index(nameof(Email), IsUnique = true, Name = "UX_AppUser_Email")]
[Index(nameof(PhoneNumber), IsUnique = true, Name = "UX_AppUser_PhoneNumber")]
[Index(nameof(LegacyIdentityCustomerId), IsUnique = true, Name = "UX_AppUser_LegacyIdentityCustomerId")]
public partial class AppUser : BaseEntity
{
    /// <summary>
    /// Stable public identifier; it is not a customer identifier.
    /// </summary>
    public Guid UserGuid { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Optional issuer-qualified subject of an external identity provider.
    /// </summary>
    [StringLength(450)]
    public string ExternalSubject { get; set; }

    /// <summary>
    /// Temporary explicit mapping to the legacy authenticated account, not the buyer.
    /// This value must never be inferred from an email address or a phone number.
    /// </summary>
    public int? LegacyIdentityCustomerId { get; set; }

    /// <summary>Username of the person, not the commercial customer.</summary>
    [StringLength(500)]
    public string Username { get; set; }

    /// <summary>User-owned email; not a valid signal for automatic customer membership.</summary>
    [StringLength(500)]
    public string Email { get; set; }

    /// <summary>User-owned phone number for future OTP login.</summary>
    [StringLength(40)]
    public string PhoneNumber { get; set; }

    public bool PhoneVerified { get; set; }

    /// <summary>Independent PBKDF2 password hash, not a Customer credential.</summary>
    [StringLength(1024)]
    public string PasswordHash { get; set; }

    /// <summary>Cookie revocation stamp; rotated after credential changes.</summary>
    [StringLength(64)]
    public string SecurityStamp { get; set; } = Guid.NewGuid().ToString("N");

    public bool EmailVerified { get; set; }
    public string EmailVerificationTokenHash { get; set; }
    public DateTime? EmailVerificationExpiresUtc { get; set; }
    public DateTime? EmailVerificationLastSentUtc { get; set; }
    [StringLength(128)]
    public string PasswordResetTokenHash { get; set; }
    public DateTime? PasswordResetExpiresUtc { get; set; }
    public DateTime? PasswordResetLastSentUtc { get; set; }
    [StringLength(1024)]
    public string OtpHash { get; set; }
    public DateTime? OtpExpiresUtc { get; set; }
    public string OtpPurpose { get; set; }
    public DateTime? OtpLastSentUtc { get; set; }
    public int OtpFailedAttempts { get; set; }
    public int FailedLoginAttempts { get; set; }
    public DateTime? LockoutEndUtc { get; set; }

    /// <summary>Default billing address owned by this person, not a business Customer.</summary>
    public int? DefaultBillingAddressId { get; set; }

    /// <summary>Default shipping address owned by this person, not a business Customer.</summary>
    public int? DefaultShippingAddressId { get; set; }

    public bool Active { get; set; } = true;

    public DateTime CreatedOnUtc { get; set; } = DateTime.UtcNow;
}
