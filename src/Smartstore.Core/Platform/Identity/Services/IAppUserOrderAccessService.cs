namespace Smartstore.Core.Identity;

/// <summary>
/// Authorizes the authenticated person to act for a commercial buyer.
/// </summary>
public interface IAppUserOrderAccessService
{
    Task<int?> ResolveAuthorizedCustomerIdAsync(int customerId, CancellationToken cancelToken = default);

    Task<bool> CanAccessOrderAsync(int orderId, CancellationToken cancelToken = default);
}
