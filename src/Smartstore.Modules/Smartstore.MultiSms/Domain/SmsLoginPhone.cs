using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Smartstore.Domain;

namespace Smartstore.MultiSms.Domain;

/// <summary>
/// A phone number verified by a signed-in native Smartstore Customer.
/// Unverified customer profile phone attributes never authorize SMS login.
/// </summary>
[Table("MultiSmsLoginPhone")]
[Index(nameof(CustomerId), IsUnique = true, Name = "UX_MultiSmsLoginPhone_CustomerId")]
[Index(nameof(Phone), IsUnique = true, Name = "UX_MultiSmsLoginPhone_Phone")]
public sealed class SmsLoginPhone : BaseEntity
{
    public int CustomerId { get; set; }

    [Required, MaxLength(16)]
    public string Phone { get; set; }

    public DateTime VerifiedOnUtc { get; set; }

    /// <summary>Null until a newly registered customer finishes the optional-details screen.</summary>
    public DateTime? ProfileCompletedOnUtc { get; set; }
}
