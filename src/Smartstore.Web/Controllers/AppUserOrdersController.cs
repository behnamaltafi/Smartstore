using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Smartstore.Core.Data;
using Smartstore.Core.Identity;

namespace Smartstore.Web.Controllers;

/// <summary>
/// Read-only order history across commercial buyers that the signed-in
/// application user is explicitly allowed to access.
/// </summary>
[ApiController]
[Authorize]
[Route("api/app-user/orders")]
public sealed class AppUserOrdersController : PublicController
{
    private readonly ICurrentAppUserContext _users;
    private readonly IAppUserCustomerService _memberships;
    private readonly SmartDbContext _db;

    public AppUserOrdersController(
        ICurrentAppUserContext users,
        IAppUserCustomerService memberships,
        SmartDbContext db)
    {
        _users = users;
        _memberships = memberships;
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> GetOrders(
        int page = 1, int pageSize = 20, CancellationToken cancelToken = default)
    {
        var actor = await _users.GetCurrentUserAsync(cancelToken);
        if (actor is null)
            return Unauthorized();

        page = Math.Clamp(page, 1, 100_000);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var buyerIds = await _memberships.GetAccessibleCustomerIdsAsync(actor.Id, cancelToken);
        if (buyerIds.Count == 0)
            return Ok(new { items = Array.Empty<object>(), page, pageSize });

        var ids = buyerIds.ToArray();
        var storeId = Services.StoreContext.CurrentStore.Id;
        var items = await _db.Orders.AsNoTracking()
            .Where(o => !o.Deleted && o.StoreId == storeId && ids.Contains(o.CustomerId))
            .OrderByDescending(o => o.CreatedOnUtc)
            .ThenByDescending(o => o.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(o => new
            {
                o.Id,
                o.CustomerId,
                o.PlacedByAppUserId,
                o.OrderNumber,
                o.CreatedOnUtc,
                o.OrderTotal
            })
            .ToArrayAsync(cancelToken);

        return Ok(new { items, page, pageSize });
    }
}
