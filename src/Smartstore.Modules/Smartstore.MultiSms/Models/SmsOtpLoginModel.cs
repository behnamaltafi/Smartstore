namespace Smartstore.MultiSms.Models;

/// <summary>
/// Form model for OTP login and verified phone enrollment.
/// </summary>
public sealed class SmsOtpLoginModel
{
    public string Phone { get; set; }
    public string Code { get; set; }
    public string ReturnUrl { get; set; }
    /// <summary>Preserves the guest-checkout option from the native login page.</summary>
    public bool CheckoutAsGuest { get; set; }
    public string VerifiedPhone { get; set; }
    public bool CodeRequested { get; set; }
    public bool IsEnrolling { get; set; }
    public string StatusMessage { get; set; }
}
