using Microsoft.EntityFrameworkCore;
using Smartstore.Core.Common;
using Smartstore.Core.Checkout.Cart;
using Smartstore.Core.Data;

namespace Smartstore.Core.Identity;

/// <summary>
/// Reads personal address and role associations without consulting the buyer/customer context.
/// </summary>
public class AppUserProfileService : IAppUserProfileService
{
    private readonly ICurrentAppUserContext _currentUser;
    private readonly SmartDbContext _db;

    public AppUserProfileService(ICurrentAppUserContext currentUser, SmartDbContext db)
    {
        _currentUser = currentUser;
        _db = db;
    }

    public virtual async Task<IReadOnlyList<ShoppingCartItem>> GetCurrentUserCartItemsAsync(
        CancellationToken cancelToken = default)
    {
        var user = await _currentUser.GetCurrentUserAsync(cancelToken);
        if (user is null)
            return Array.Empty<ShoppingCartItem>();

        return await _db.ShoppingCartItems.AsNoTracking()
            .Where(x => x.AppUserId == user.Id)
            .ToArrayAsync(cancelToken);
    }

    public virtual async Task<IReadOnlyList<Address>> GetCurrentUserAddressesAsync(
        CancellationToken cancelToken = default)
    {
        var user = await _currentUser.GetCurrentUserAsync(cancelToken);
        if (user is null)
            return Array.Empty<Address>();

        return await (
            from mapping in _db.AppUserAddressMappings.AsNoTracking()
            join address in _db.Addresses.AsNoTracking() on mapping.AddressId equals address.Id
            where mapping.AppUserId == user.Id
            select address
        ).ToArrayAsync(cancelToken);
    }

    public virtual async Task<IReadOnlyList<CustomerRole>> GetCurrentUserRolesAsync(
        CancellationToken cancelToken = default)
    {
        var user = await _currentUser.GetCurrentUserAsync(cancelToken);
        if (user is null)
            return Array.Empty<CustomerRole>();

        return await (
            from mapping in _db.AppUserRoleMappings.AsNoTracking()
            join role in _db.CustomerRoles.AsNoTracking() on mapping.CustomerRoleId equals role.Id
            where mapping.AppUserId == user.Id && role.Active
            select role
        ).ToArrayAsync(cancelToken);
    }
}
