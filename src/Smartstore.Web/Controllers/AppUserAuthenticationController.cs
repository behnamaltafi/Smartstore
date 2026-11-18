using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Smartstore.Core.Identity;
using Microsoft.Extensions.Configuration;

namespace Smartstore.Web.Controllers;

/// <summary>
/// An independent person login API; neither Customer credentials nor any
/// commercial buyer association is accepted here.
/// </summary>
[ApiController]
[Route("api/app-user/auth")]
public sealed class AppUserAuthenticationController : PublicController
{
    private readonly AppUserCredentialService _credentials;
    private readonly ICurrentAppUserContext _current;
    private readonly IAppUserBuyerContext _buyers;
    private readonly IAppUserChallengeSender _delivery;
    private readonly IAntiforgery _antiforgery;
    private readonly IAuthenticationSchemeProvider _schemes;
    private readonly CustomerSettings _registrationPolicy;
    private readonly IConfiguration _otpPolicy;

    public AppUserAuthenticationController(
        AppUserCredentialService credentials,
        ICurrentAppUserContext current,
        IAppUserBuyerContext buyers,
        IAppUserChallengeSender delivery,
        IAntiforgery antiforgery,
        IAuthenticationSchemeProvider schemes,
        CustomerSettings registrationPolicy,
        IConfiguration otpPolicy)
    {
        _credentials = credentials;
        _current = current;
        _buyers = buyers;
        _delivery = delivery;
        _antiforgery = antiforgery;
        _schemes = schemes;
        _registrationPolicy = registrationPolicy;
        _otpPolicy = otpPolicy;
    }

    [AllowAnonymous]
    [HttpGet("antiforgery")]
    public IActionResult Antiforgery() => Ok(new
    {
        token = _antiforgery.GetAndStoreTokens(HttpContext).RequestToken
    });

    [AllowAnonymous]
    [HttpPost("register")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        if (request is null)
            return BadRequest();

        // JSON and MVC registration must enforce the same store policy.
        if (_registrationPolicy.UserRegistrationType != UserRegistrationType.Standard)
            return StatusCode(403, "Independent AppUser registration requires an explicit activation/approval policy.");
        // API registration does not run the public MVC CAPTCHA/consent filters.
        if (!_otpPolicy.GetValue<bool>("AppUserAuth:AllowApiRegistration"))
            return NotFound();

        var user = await _credentials.RegisterAsync(request.Username, request.Email, request.Password, request.Phone);
        if (user is null)
            return BadRequest("Invalid registration data or account already exists.");

        await SignInUserAsync(user, false);
        var confirmationToken = await _credentials.BeginEmailVerificationAsync(user);
        if (confirmationToken is not null)
            await _delivery.SendEmailVerificationAsync(user.Email, confirmationToken);
        return Ok(new { user.Id, user.UserGuid, user.Username });
    }

    [AllowAnonymous]
    [HttpPost("login")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        if (request is null)
            return BadRequest();

        var user = await _credentials.AuthenticatePasswordAsync(request.UsernameOrEmail, request.Password);
        if (user is null)
            return Unauthorized("Invalid credentials.");

        await SignInUserAsync(user, request.RememberMe);
        return Ok(new { user.Id, user.UserGuid, user.Username });
    }

    [Authorize(AuthenticationSchemes = AppUserAuthenticationDefaults.Scheme)]
    [HttpPost("logout")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        _buyers.ClearSelection();
        await HttpContext.SignOutAsync(AppUserAuthenticationDefaults.Scheme);
        await HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
        return NoContent();
    }

    [Authorize(AuthenticationSchemes = AppUserAuthenticationDefaults.Scheme)]
    [HttpPost("password/change")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        var actor = await _current.GetCurrentUserAsync();
        if (actor is null)
            return Unauthorized();
        if (request is null || !await _credentials.ChangePasswordAsync(
            actor, request.OldPassword, request.NewPassword))
            return BadRequest("Incorrect password or invalid new password.");

        // Rotating the stamp invalidates all outstanding cookies.
        await HttpContext.SignOutAsync(AppUserAuthenticationDefaults.Scheme);
        _buyers.ClearSelection();
        return NoContent();
    }

    [Authorize(AuthenticationSchemes = AppUserAuthenticationDefaults.Scheme)]
    [HttpPost("email/verify/request")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RequestEmailVerification()
    {
        var user = await _current.GetCurrentUserAsync();
        if (user is null)
            return Unauthorized();

        var token = await _credentials.BeginEmailVerificationAsync(user);
        if (token is not null)
            await _delivery.SendEmailVerificationAsync(user.Email, token);
        return Accepted();
    }

    [AllowAnonymous]
    [HttpPost("email/verify/complete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CompleteEmailVerification([FromBody] VerifyEmailRequest request)
    {
        if (request is null || !await _credentials.CompleteEmailVerificationAsync(
            request.Email, request.Token))
            return BadRequest("Invalid or expired email verification.");

        return NoContent();
    }

    [AllowAnonymous]
    [HttpPost("password/reset/request")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RequestPasswordReset([FromBody] EmailRequest request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Email))
            return BadRequest();

        var token = await _credentials.BeginPasswordResetAsync(request.Email);
        if (token is not null)
            await _delivery.SendPasswordResetAsync(request.Email.Trim(), token);

        // Does not disclose whether the address is registered.
        return Accepted();
    }

    [AllowAnonymous]
    [HttpPost("password/reset/complete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CompletePasswordReset([FromBody] CompleteResetRequest request)
    {
        if (request is null || !await _credentials.CompletePasswordResetAsync(
            request.Email, request.Token, request.NewPassword))
            return BadRequest("Invalid or expired password reset token.");

        return NoContent();
    }

    [AllowAnonymous]
    [HttpPost("otp/request")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RequestLoginOtp([FromBody] PhoneRequest request)
    {
        if (!_otpPolicy.GetValue<bool>("AppUserAuth:Otp:Enabled"))
            return NotFound();
        if (request is null)
            return BadRequest();

        var code = await _credentials.BeginOtpAsync(request.Phone);
        if (code is not null)
            await _delivery.SendOtpAsync(request.Phone, code);

        return Accepted();
    }

    [AllowAnonymous]
    [HttpPost("otp/verify")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> VerifyLoginOtp([FromBody] VerifyOtpRequest request)
    {
        if (!_otpPolicy.GetValue<bool>("AppUserAuth:Otp:Enabled"))
            return NotFound();
        if (request is null)
            return BadRequest();

        var user = await _credentials.VerifyOtpAsync(request.Phone, request.Code);
        if (user is null)
            return Unauthorized("Invalid or expired OTP.");

        await SignInUserAsync(user, false);
        return Ok(new { user.Id, user.UserGuid });
    }

    [Authorize(AuthenticationSchemes = AppUserAuthenticationDefaults.Scheme)]
    [HttpPost("phone/verify/request")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RequestPhoneVerification()
    {
        var actor = await _current.GetCurrentUserAsync();
        if (actor is null || string.IsNullOrWhiteSpace(actor.PhoneNumber))
            return BadRequest("Set your phone number in your AppUser profile first.");

        var code = await _credentials.BeginOtpAsync(actor.PhoneNumber, actor);
        if (code is null)
            return BadRequest("Unable to issue a phone verification code.");
        if (!await _delivery.SendOtpAsync(actor.PhoneNumber, code))
            return StatusCode(503, "SMS gateway is not configured.");
        return Accepted();
    }

    [Authorize(AuthenticationSchemes = AppUserAuthenticationDefaults.Scheme)]
    [HttpPost("phone/verify/complete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CompletePhoneVerification([FromBody] VerifyOtpRequest request)
    {
        var actor = await _current.GetCurrentUserAsync();
        if (actor is null)
            return Unauthorized();
        if (request is null || request.Phone != actor.PhoneNumber ||
            await _credentials.VerifyOtpAsync(request.Phone, request.Code, actor) is null)
            return BadRequest("Invalid or expired phone verification code.");
        return NoContent();
    }

    [AllowAnonymous]
    [HttpGet("external/challenge")]
    public async Task<IActionResult> ExternalChallenge([FromQuery] string provider)
    {
        if (string.IsNullOrWhiteSpace(provider) || provider.Length > 100 ||
            provider == IdentityConstants.ApplicationScheme ||
            provider == IdentityConstants.ExternalScheme ||
            provider == AppUserAuthenticationDefaults.Scheme ||
            await _schemes.GetSchemeAsync(provider) is null)
            return BadRequest("Unknown external provider.");

        var properties = new AuthenticationProperties
        {
            RedirectUri = Url.Action(nameof(ExternalCallback), "AppUserAuthentication")
        };
        properties.Items["AppUserExternalProvider"] = provider;
        return Challenge(properties, provider);
    }

    [AllowAnonymous]
    [HttpGet("external/callback")]
    public async Task<IActionResult> ExternalCallback()
    {
        var ticket = await HttpContext.AuthenticateAsync(IdentityConstants.ExternalScheme);
        if (!ticket.Succeeded || ticket.Principal is null ||
            ticket.Properties?.Items.TryGetValue("AppUserExternalProvider", out var provider) != true ||
            string.IsNullOrWhiteSpace(provider))
            return Unauthorized();

        var subjects = ticket.Principal.FindAll(ClaimTypes.NameIdentifier).ToArray();
        if (subjects.Length != 1 || string.IsNullOrWhiteSpace(subjects[0].Value))
            return Unauthorized();

        var verified = ticket.Principal.FindFirst("email_verified")?.Value
            .Equals("true", StringComparison.OrdinalIgnoreCase) == true;
        var verifiedEmail = verified
            ? ticket.Principal.FindFirstValue(ClaimTypes.Email)
            : null;
        var user = await _credentials.AuthenticateExternalAsync(
            provider, subjects[0].Value, verifiedEmail,
            allowProvisioning: _registrationPolicy.UserRegistrationType == UserRegistrationType.Standard &&
                _otpPolicy.GetValue<bool>("AppUserAuth:AllowExternalAutoProvision"));

        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
        if (user is null)
            return Forbid();

        await SignInUserAsync(user, false);
        return Redirect("~/");
    }

    private Task SignInUserAsync(AppUser user, bool persistent) =>
        SignInUserAsync(HttpContext, _buyers, user, persistent);

    /// <summary>Reusable independent user sign-in for HTML and JSON flows.</summary>
    public static async Task SignInUserAsync(
        Microsoft.AspNetCore.Http.HttpContext http,
        IAppUserBuyerContext buyers, AppUser user, bool persistent)
    {
        // Do not keep the former person's Customer or buyer selection.
        await http.SignOutAsync(IdentityConstants.ApplicationScheme);
        buyers.ClearSelection();

        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(AppUserAuthenticationDefaults.IdClaim, user.Id.ToString(),
                ClaimValueTypes.Integer32, AppUserAuthenticationDefaults.Issuer),
            new Claim(AppUserAuthenticationDefaults.GuidClaim, user.UserGuid.ToString("D"),
                ClaimValueTypes.String, AppUserAuthenticationDefaults.Issuer),
            new Claim(AppUserAuthenticationDefaults.SecurityStampClaim, user.SecurityStamp,
                ClaimValueTypes.String, AppUserAuthenticationDefaults.Issuer)
        }, AppUserAuthenticationDefaults.Scheme));

        await http.SignInAsync(AppUserAuthenticationDefaults.Scheme,
            principal, new AuthenticationProperties
            {
                IsPersistent = persistent,
                IssuedUtc = DateTimeOffset.UtcNow
            });
    }

    public sealed record RegisterRequest(string Username, string Email, string Password, string Phone = null);
    public sealed record LoginRequest(string UsernameOrEmail, string Password, bool RememberMe);
    public sealed record ChangePasswordRequest(string OldPassword, string NewPassword);
    public sealed record EmailRequest(string Email);
    public sealed record VerifyEmailRequest(string Email, string Token);
    public sealed record CompleteResetRequest(string Email, string Token, string NewPassword);
    public sealed record PhoneRequest(string Phone);
    public sealed record VerifyOtpRequest(string Phone, string Code);
}
