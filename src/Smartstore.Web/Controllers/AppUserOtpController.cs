using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Smartstore.Core.Identity;
using Smartstore.Web.Models.Identity;

namespace Smartstore.Web.Controllers;

/// <summary>Web-form passwordless AppUser sign-in via configurable SMS sender.</summary>
[AllowAnonymous]
[Route("app-user/otp")]
public sealed class AppUserOtpController : PublicController
{
    private readonly AppUserCredentialService _credentials;
    private readonly IAppUserChallengeSender _sender;
    private readonly IAppUserBuyerContext _buyers;
    private readonly IConfiguration _configuration;

    public AppUserOtpController(AppUserCredentialService credentials,
        IAppUserChallengeSender sender, IAppUserBuyerContext buyers,
        IConfiguration configuration)
    {
        _credentials = credentials;
        _sender = sender;
        _buyers = buyers;
        _configuration = configuration;
    }

    [HttpGet("")]
    public IActionResult Index(string returnUrl = null)
    {
        if (!_configuration.GetValue<bool>("AppUserAuth:Otp:Enabled"))
            return NotFound();

        return View(new AppUserOtpModel
        {
            ReturnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl : null
        });
    }

    [HttpPost("send")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Send(AppUserOtpModel model)
    {
        if (!_configuration.GetValue<bool>("AppUserAuth:Otp:Enabled"))
            return NotFound();

        model.ReturnUrl = Url.IsLocalUrl(model.ReturnUrl) ? model.ReturnUrl : null;
        if (string.IsNullOrWhiteSpace(model.Phone) ||
            model.Phone.Length < 9 || model.Phone.Length > 16 ||
            model.Phone[0] != '+' || !model.Phone.Skip(1).All(char.IsDigit))
        {
            ModelState.AddModelError(nameof(model.Phone), "Enter a phone number in E.164 format.");
            return View("Index", model);
        }

        var code = await _credentials.BeginOtpAsync(model.Phone);
        if (code is not null)
            await _sender.SendOtpAsync(model.Phone, code);

        // The response does not reveal whether a phone number is registered.
        model.CodeRequested = true;
        ModelState.Clear();
        return View("Index", model);
    }

    [HttpPost("verify")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Verify(AppUserOtpModel model)
    {
        if (!_configuration.GetValue<bool>("AppUserAuth:Otp:Enabled"))
            return NotFound();

        model.ReturnUrl = Url.IsLocalUrl(model.ReturnUrl) ? model.ReturnUrl : null;
        var actor = await _credentials.VerifyOtpAsync(model.Phone, model.Code);
        if (actor is null)
        {
            ModelState.AddModelError(nameof(model.Code), "The verification code is invalid or expired.");
            model.CodeRequested = true;
            return View("Index", model);
        }

        await AppUserAuthenticationController.SignInUserAsync(
            HttpContext, _buyers, actor, false);
        return !string.IsNullOrEmpty(model.ReturnUrl)
            ? LocalRedirect(model.ReturnUrl)
            : RedirectToRoute("Homepage");
    }
}
