using Microsoft.AspNetCore.Mvc;
using Smartstore.Core.Identity;

namespace Smartstore.Web.Controllers;

/// <summary>
/// Allows a signed-in application user to select an authorized commercial buyer.
/// The selected buyer is never an authentication principal.
/// </summary>
[ApiController]
[Route("api/app-user/customers")]
public class AppUserCustomerSelectionController : PublicController
{
    private readonly ICurrentAppUserContext _users;
    private readonly IAppUserCustomerService _memberships;
    private readonly IAppUserBuyerContext _buyer;

    public AppUserCustomerSelectionController(
        ICurrentAppUserContext users,
        IAppUserCustomerService memberships,
        IAppUserBuyerContext buyer)
    {
        _users = users;
        _memberships = memberships;
        _buyer = buyer;
    }

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var user = await _users.GetCurrentUserAsync();
        if (user is null)
            return Unauthorized();

        var ids = await _memberships.GetAccessibleCustomerIdsAsync(user.Id);
        var selected = await _buyer.GetSelectedCustomerIdAsync();
        return Ok(new { customerIds = ids, selectedCustomerId = selected });
    }

    [HttpPost("select")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Select([FromBody] SelectBuyerRequest request)
    {
        if (await _users.GetCurrentUserAsync() is null)
            return Unauthorized();

        if (request is null || !await _buyer.SelectCustomerAsync(request.CustomerId))
            return Forbid();

        return Ok(new { selectedCustomerId = request.CustomerId });
    }

    [HttpPost("clear")]
    [ValidateAntiForgeryToken]
    public IActionResult Clear()
    {
        _buyer.ClearSelection();
        return Ok();
    }

    public sealed class SelectBuyerRequest
    {
        public int CustomerId { get; set; }
    }
}
