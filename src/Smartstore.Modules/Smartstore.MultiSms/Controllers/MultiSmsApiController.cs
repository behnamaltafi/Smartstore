using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Smartstore.Core.Identity;
using Smartstore.Core.Messaging;
using Smartstore.Core.Security;
using Smartstore.MultiSms.Models;

namespace Smartstore.MultiSms.Controllers;

/// <summary>Provides a protected JSON delivery test for shop administrators.</summary>
[ApiController]
[Route("api/sms-gateway")]
[AutoValidateAntiforgeryToken]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class MultiSmsApiController : ControllerBase
{
    private readonly ISmsSender _sender;
    private readonly UserManager<Customer> _userManager;
    private readonly SignInManager<Customer> _signInManager;
    private readonly IPermissionService _permissions;

    public MultiSmsApiController(ISmsSender sender, UserManager<Customer> userManager,
        SignInManager<Customer> signInManager, IPermissionService permissions)
    {
        _sender = sender;
        _userManager = userManager;
        _signInManager = signInManager;
        _permissions = permissions;
    }

    /// <summary>Sends a test message using saved settings without returning provider errors or message text.</summary>
    [HttpPost("test")]
    public async Task<IActionResult> TestSms([FromBody] SmsTestRequest request, CancellationToken ct)
    {
        if (!_signInManager.IsSignedIn(User))
        {
            return Unauthorized();
        }

        var customer = await _userManager.GetUserAsync(User);
        if (customer is null || !customer.Active || customer.Deleted || !customer.IsRegistered())
        {
            return Unauthorized();
        }

        if (!await _permissions.AuthorizeAsync(Permissions.System.AccessBackend, customer) ||
            !await _permissions.AuthorizeAsync("configuration.setting", customer))
        {
            return StatusCode(403, new { code = "access_denied" });
        }

        try
        {
            var result = await _sender.SendAsync(new SmsSendRequest(request.Phone, request.Text, SmsMessageKind.Test), ct);
            return Ok(new { code = "accepted", provider = result.Provider, messageId = result.MessageId });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return StatusCode(502, new { code = "sms_not_confirmed" });
        }
    }
}
