#nullable enable

namespace Smartstore.Core.Identity;

/// <summary>
/// Resolves customer access for independently authenticated application users.
/// The supplied user ID must come from a validated authentication principal.
/// </summary>
public interface IAppUserCustomerService
{
    Task<IReadOnlyList<int>> GetAccessibleCustomerIdsAsync(int userId, CancellationToken cancelToken = default);

    Task<bool> CanAccessCustomerAsync(int userId, int customerId, CancellationToken cancelToken = default);

    Task LinkAsync(int userId, int customerId, CancellationToken cancelToken = default);

    Task UnlinkAsync(int userId, int customerId, CancellationToken cancelToken = default);
}
