using Microsoft.EntityFrameworkCore;
using Smartstore.Core.Data;

namespace Smartstore.Core.Identity;

/// <summary>
/// Selects a commercial buyer only after validating explicit memberships.
/// Customer selection is never a login or user identity change.
/// </summary>
public class AppUserOrderAccessService : IAppUserOrderAccessService
{
    private readonly ICurrentAppUserContext _userContext;
    private readonly IAppUserCustomerService _memberships;
    private readonly SmartDbContext _db;

    public AppUserOrderAccessService(
        ICurrentAppUserContext userContext,
        IAppUserCustomerService memberships,
        SmartDbContext db)
    {
        _userContext = userContext;
        _memberships = memberships;
        _db = db;
    }

    public virtual async Task<int?> ResolveAuthorizedCustomerIdAsync(
        int customerId, CancellationToken cancelToken = default)
    {
        if (customerId <= 0)
            return null;

        var user = await _userContext.GetCurrentUserAsync(cancelToken);
        if (user is null ||
            !await _memberships.CanAccessCustomerAsync(user.Id, customerId, cancelToken))
        {
            return null;
        }

        return customerId;
    }

    public virtual async Task<bool> CanAccessOrderAsync(
        int orderId, CancellationToken cancelToken = default)
    {
        if (orderId <= 0)
            return false;

        var order = await _db.Orders.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == orderId && !x.Deleted, cancelToken);

        return order is not null &&
            await ResolveAuthorizedCustomerIdAsync(order.CustomerId, cancelToken) is not null;
    }
}
