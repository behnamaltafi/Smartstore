namespace Smartstore.Core.Identity;

/// <summary>
/// Stores a user's selected commercial buyer. Every read and write validates
/// the explicit AppUserCustomer relationship; the cookie itself is not trusted.
/// </summary>
public interface IAppUserBuyerContext
{
    Task<int?> GetSelectedCustomerIdAsync(CancellationToken cancelToken = default);
    Task<bool> SelectCustomerAsync(int customerId, CancellationToken cancelToken = default);
    void ClearSelection();
}
