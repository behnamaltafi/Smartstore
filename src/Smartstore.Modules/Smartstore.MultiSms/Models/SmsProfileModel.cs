using System.ComponentModel.DataAnnotations;

namespace Smartstore.MultiSms.Models;

/// <summary>Additional details for native Customer accounts authenticated by mobile number.</summary>
public sealed class SmsProfileModel
{
    [Required, StringLength(100)]
    public string Username { get; set; }

    [StringLength(225)]
    public string FirstName { get; set; }

    [StringLength(225)]
    public string LastName { get; set; }

    [EmailAddress, StringLength(500)]
    public string Email { get; set; }

    public string Phone { get; set; }
    public string ReturnUrl { get; set; }
}
