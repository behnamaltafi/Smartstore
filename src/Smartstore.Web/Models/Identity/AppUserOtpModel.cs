using System.ComponentModel.DataAnnotations;

namespace Smartstore.Web.Models.Identity;

/// <summary>Standalone passwordless sign-in; no Customer identity fields.</summary>
public sealed class AppUserOtpModel
{
    [Required]
    [Phone]
    public string Phone { get; set; }

    public string Code { get; set; }
    public string ReturnUrl { get; set; }
    public bool CodeRequested { get; set; }
}
