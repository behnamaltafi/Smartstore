namespace Smartstore.Core.Identity;

/// <summary>Separate ASP.NET cookie identity for a real AppUser, not Identity&lt;Customer&gt;.</summary>
public static class AppUserAuthenticationDefaults
{
    public const string Scheme = "Smartstore.AppUser";
    public const string Issuer = "Smartstore.AppUser";
    public const string GuidClaim = "app_user_guid";
    public const string SecurityStampClaim = "app_user_security_stamp";
    public const string IdClaim = "app_user_id";
}
