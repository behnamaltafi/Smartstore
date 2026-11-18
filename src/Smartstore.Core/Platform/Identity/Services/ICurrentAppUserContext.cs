#nullable enable

namespace Smartstore.Core.Identity;

/// <summary>
/// Resolves a verified application principal without using the legacy Customer identity.
/// </summary>
public interface ICurrentAppUserContext
{
    Task<AppUser?> GetCurrentUserAsync(CancellationToken cancelToken = default);

    /// <summary>True when an authenticated principal carries an AppUser identity claim.</summary>
    bool IsAppUserIdentityPresent { get; }
}
