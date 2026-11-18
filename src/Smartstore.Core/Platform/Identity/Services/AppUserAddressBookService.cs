using Microsoft.EntityFrameworkCore;
using Smartstore.Core.Common;
using Smartstore.Core.Data;

namespace Smartstore.Core.Identity;

/// <summary>
/// An AppUser owns the mapping, not the Address entity itself. Replacing an
/// entry is copy-on-write so another user or an order never observes changes.
/// </summary>
public class AppUserAddressBookService : IAppUserAddressBookService
{
    private readonly ICurrentAppUserContext _current;
    private readonly SmartDbContext _db;

    public AppUserAddressBookService(ICurrentAppUserContext current, SmartDbContext db)
    {
        _current = current;
        _db = db;
    }

    public virtual async Task<int?> AddAddressAsync(Address address, CancellationToken cancelToken = default)
    {
        ArgumentNullException.ThrowIfNull(address);
        var actor = await _current.GetCurrentUserAsync(cancelToken);
        if (actor is null)
            return null;

        var fresh = CopyAddress(address);
        await using var tx = await _db.Database.BeginTransactionAsync(cancelToken);
        _db.Addresses.Add(fresh);
        await _db.SaveChangesAsync(cancelToken);

        _db.AppUserAddressMappings.Add(new AppUserAddressMapping
        {
            AppUserId = actor.Id,
            AddressId = fresh.Id
        });
        await _db.SaveChangesAsync(cancelToken);
        await tx.CommitAsync(cancelToken);

        return fresh.Id;
    }

    public virtual async Task<bool> ReplaceAddressAsync(
        int addressId, Address address, CancellationToken cancelToken = default)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (addressId <= 0)
            return false;

        var actor = await _current.GetCurrentUserAsync(cancelToken);
        if (actor is null)
            return false;

        var link = await _db.AppUserAddressMappings.SingleOrDefaultAsync(
            x => x.AppUserId == actor.Id && x.AddressId == addressId, cancelToken);
        if (link is null)
            return false;

        var fresh = CopyAddress(address);
        await using var tx = await _db.Database.BeginTransactionAsync(cancelToken);
        _db.Addresses.Add(fresh);
        await _db.SaveChangesAsync(cancelToken);

        link.AddressId = fresh.Id;

        // Retain personal default selections when the address is replaced.
        var profile = await _db.AppUsers.FirstOrDefaultAsync(x => x.Id == actor.Id, cancelToken);
        if (profile is not null)
        {
            if (profile.DefaultBillingAddressId == addressId)
                profile.DefaultBillingAddressId = fresh.Id;
            if (profile.DefaultShippingAddressId == addressId)
                profile.DefaultShippingAddressId = fresh.Id;
        }

        await _db.SaveChangesAsync(cancelToken);
        await tx.CommitAsync(cancelToken);

        return true;
    }

    public virtual async Task<bool> RemoveAddressAsync(
        int addressId, CancellationToken cancelToken = default)
    {
        if (addressId <= 0)
            return false;

        var actor = await _current.GetCurrentUserAsync(cancelToken);
        if (actor is null)
            return false;

        var link = await _db.AppUserAddressMappings.SingleOrDefaultAsync(
            x => x.AppUserId == actor.Id && x.AddressId == addressId, cancelToken);
        if (link is null)
            return false;

        // Keep the referenced Address row intact for other users and orders.
        _db.AppUserAddressMappings.Remove(link);
        var profile = await _db.AppUsers.FirstOrDefaultAsync(x => x.Id == actor.Id, cancelToken);
        if (profile is not null)
        {
            if (profile.DefaultBillingAddressId == addressId)
                profile.DefaultBillingAddressId = null;
            if (profile.DefaultShippingAddressId == addressId)
                profile.DefaultShippingAddressId = null;
        }

        await _db.SaveChangesAsync(cancelToken);
        return true;
    }

    public virtual async Task<bool> SetDefaultAddressAsync(
        int addressId, CancellationToken cancelToken = default)
    {
        if (addressId <= 0)
            return false;

        var actor = await _current.GetCurrentUserAsync(cancelToken);
        if (actor is null)
            return false;

        var link = await _db.AppUserAddressMappings.AsNoTracking().AnyAsync(
            x => x.AppUserId == actor.Id && x.AddressId == addressId, cancelToken);
        if (!link)
            return false;

        var address = await _db.Addresses.AsNoTracking()
            .Include(x => x.Country)
            .FirstOrDefaultAsync(x => x.Id == addressId, cancelToken);
        if (address is null ||
            (address.Country is not null &&
             (!address.Country.AllowsBilling || !address.Country.AllowsShipping)))
            return false;

        var profile = await _db.AppUsers.FirstOrDefaultAsync(
            x => x.Id == actor.Id && x.Active, cancelToken);
        if (profile is null)
            return false;

        profile.DefaultBillingAddressId = addressId;
        profile.DefaultShippingAddressId = addressId;
        await _db.SaveChangesAsync(cancelToken);
        return true;
    }

    private static Address CopyAddress(Address source) => new()
    {
        Salutation = source.Salutation,
        Title = source.Title,
        FirstName = source.FirstName,
        LastName = source.LastName,
        Email = source.Email,
        Company = source.Company,
        CountryId = source.CountryId,
        StateProvinceId = source.StateProvinceId,
        City = source.City,
        Address1 = source.Address1,
        Address2 = source.Address2,
        ZipPostalCode = source.ZipPostalCode,
        PhoneNumber = source.PhoneNumber,
        FaxNumber = source.FaxNumber,
        CreatedOnUtc = DateTime.UtcNow
    };
}
