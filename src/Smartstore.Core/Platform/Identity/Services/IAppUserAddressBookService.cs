using Smartstore.Core.Common;

namespace Smartstore.Core.Identity;

/// <summary>
/// User-owned address operations, independent of the current business buyer.
/// </summary>
public interface IAppUserAddressBookService
{
    Task<int?> AddAddressAsync(Address address, CancellationToken cancelToken = default);
    Task<bool> ReplaceAddressAsync(int addressId, Address address, CancellationToken cancelToken = default);
    Task<bool> RemoveAddressAsync(int addressId, CancellationToken cancelToken = default);
    Task<bool> SetDefaultAddressAsync(int addressId, CancellationToken cancelToken = default);
}
