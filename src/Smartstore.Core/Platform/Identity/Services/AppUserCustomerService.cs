using Microsoft.EntityFrameworkCore;
using Smartstore.Core.Data;

namespace Smartstore.Core.Identity;

/// <summary>
/// Manages explicit memberships; never implicitly links records by email or phone.
/// </summary>
public class AppUserCustomerService : IAppUserCustomerService
{
    private readonly SmartDbContext _db;

    public AppUserCustomerService(SmartDbContext db)
    {
        _db = db;
    }

    public virtual async Task<IReadOnlyList<int>> GetAccessibleCustomerIdsAsync(
        int userId, CancellationToken cancelToken = default)
    {
        if (userId <= 0)
            return Array.Empty<int>();

        return await (
            from link in _db.AppUserCustomers.AsNoTracking()
            join user in _db.AppUsers.AsNoTracking() on link.AppUserId equals user.Id
            join customer in _db.Customers.AsNoTracking() on link.CustomerId equals customer.Id
            where user.Id == userId && user.Active && link.Active && customer.Active && !customer.Deleted && !customer.IsSystemAccount
            select customer.Id
        ).ToArrayAsync(cancelToken);
    }

    public virtual async Task<bool> CanAccessCustomerAsync(
        int userId, int customerId, CancellationToken cancelToken = default)
    {
        if (userId <= 0 || customerId <= 0)
            return false;

        return await (
            from link in _db.AppUserCustomers.AsNoTracking()
            join user in _db.AppUsers.AsNoTracking() on link.AppUserId equals user.Id
            join customer in _db.Customers.AsNoTracking() on link.CustomerId equals customer.Id
            where user.Id == userId && customer.Id == customerId &&
                user.Active && link.Active && customer.Active && !customer.Deleted && !customer.IsSystemAccount
            select link.Id
        ).AnyAsync(cancelToken);
    }

    public virtual async Task LinkAsync(
        int userId, int customerId, CancellationToken cancelToken = default)
    {
        if (userId <= 0 || customerId <= 0)
            throw new ArgumentOutOfRangeException(nameof(userId), "User and customer IDs must be positive.");

        bool validUser = await _db.AppUsers.AnyAsync(x => x.Id == userId && x.Active, cancelToken);
        bool validCustomer = await _db.Customers.AnyAsync(
            x => x.Id == customerId && x.Active && !x.Deleted && !x.IsSystemAccount, cancelToken);

        if (!validUser || !validCustomer)
            throw new InvalidOperationException("Both the user and the customer must exist and be active.");

        var link = await _db.AppUserCustomers
            .FirstOrDefaultAsync(x => x.AppUserId == userId && x.CustomerId == customerId, cancelToken);

        if (link is null)
        {
            _db.AppUserCustomers.Add(new AppUserCustomer { AppUserId = userId, CustomerId = customerId });
        }
        else if (!link.Active)
        {
            link.Active = true;
        }
        else
        {
            return;
        }

        await _db.SaveChangesAsync(cancelToken);
    }

    public virtual async Task UnlinkAsync(
        int userId, int customerId, CancellationToken cancelToken = default)
    {
        var link = await _db.AppUserCustomers
            .FirstOrDefaultAsync(x => x.AppUserId == userId && x.CustomerId == customerId, cancelToken);

        if (link is { Active: true })
        {
            link.Active = false;
            await _db.SaveChangesAsync(cancelToken);
        }
    }
}
