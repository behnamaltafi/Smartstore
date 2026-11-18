namespace Smartstore.Core.Identity;

/// <summary>
/// Returns resources owned by the authenticated person, not by a commercial customer.
/// </summary>
public interface IAppUserProfileService
{
    Task<IReadOnlyList<Smartstore.Core.Common.Address>> GetCurrentUserAddressesAsync(
        CancellationToken cancelToken = default);

    Task<IReadOnlyList<Smartstore.Core.Checkout.Cart.ShoppingCartItem>> GetCurrentUserCartItemsAsync(
        CancellationToken cancelToken = default);

    Task<IReadOnlyList<CustomerRole>> GetCurrentUserRolesAsync(
        CancellationToken cancelToken = default);
}
