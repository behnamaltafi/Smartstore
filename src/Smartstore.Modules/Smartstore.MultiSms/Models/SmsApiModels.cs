#nullable enable

using System.ComponentModel.DataAnnotations;

namespace Smartstore.MultiSms.Models;

/// <summary>Requests a code for a mobile number.</summary>
public class SmsCodeRequest
{
    /// <summary>Gets or sets the Iranian mobile number in local or international format.</summary>
    [Required, StringLength(20)]
    public string Phone { get; set; } = string.Empty;
}

/// <summary>Submits a single-use verification code.</summary>
public sealed class SmsCodeVerificationRequest : SmsCodeRequest
{
    /// <summary>Gets or sets the six-digit SMS code.</summary>
    [Required, RegularExpression("^[0-9]{6}$")]
    public string Code { get; set; } = string.Empty;
}

/// <summary>Requests an administrator-only delivery test using saved provider settings.</summary>
public sealed class SmsTestRequest : SmsCodeRequest
{
    /// <summary>Gets or sets the message body, which is never echoed in the response.</summary>
    [Required, StringLength(1500)]
    public string Text { get; set; } = string.Empty;
}


/// <summary>Authenticated profile update after phone OTP login.</summary>
public sealed class SmsProfileApiRequest
{
    [Required, StringLength(100)]
    public string Username { get; set; } = string.Empty;

    [StringLength(225)]
    public string? FirstName { get; set; }

    [StringLength(225)]
    public string? LastName { get; set; }

    [EmailAddress, StringLength(500)]
    public string? Email { get; set; }
}
