using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Smartstore.Core.Data;
using Smartstore.Core.Identity;

namespace Smartstore.Web.Controllers;

/// <summary>
/// Exposes person-owned profile resources without crossing the boundary into
/// the commercial customer selected for order placement.
/// </summary>
[ApiController]
[Authorize]
[Route("api/app-user/profile")]
public sealed class AppUserProfileController : PublicController
{
    private readonly ICurrentAppUserContext _currentUser;
    private readonly IAppUserProfileService _profile;
    private readonly IAppUserCustomerService _memberships;
    private readonly SmartDbContext _db;

    public AppUserProfileController(
        ICurrentAppUserContext currentUser,
        IAppUserProfileService profile,
        IAppUserCustomerService memberships,
        SmartDbContext db)
    {
        _currentUser = currentUser;
        _profile = profile;
        _memberships = memberships;
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> GetProfile(CancellationToken cancelToken)
    {
        var user = await _currentUser.GetCurrentUserAsync(cancelToken);
        if (user is null)
            return Unauthorized();

        var customers = await _memberships.GetAccessibleCustomerIdsAsync(user.Id, cancelToken);
        return Ok(new
        {
            user.Id,
            user.UserGuid,
            user.Username,
            user.Email,
            user.PhoneNumber,
            user.PhoneVerified,
            user.DefaultBillingAddressId,
            user.DefaultShippingAddressId,
            customerIds = customers
        });
    }

    /// <summary>
    /// Update only current person-owned profile fields. Email is intentionally
    /// read-only pending a verified email-change workflow.
    /// </summary>
    [HttpPut]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateProfile(
        [FromBody] UpdateProfileRequest request, CancellationToken cancelToken)
    {
        var actor = await _currentUser.GetCurrentUserAsync(cancelToken);
        if (actor is null)
            return Unauthorized();

        if (request is null || string.IsNullOrWhiteSpace(request.Username) ||
            request.Username.Length > 500 || request.PhoneNumber?.Length > 40)
            return BadRequest("Invalid profile data.");

        var newUsername = request.Username.Trim();
        var normalizedUsername = newUsername.ToUpperInvariant();
        var duplicate = await _db.AppUsers.AsNoTracking().AnyAsync(x =>
            x.Id != actor.Id && x.Active && x.Username != null &&
            x.Username.ToUpper() == normalizedUsername, cancelToken);
        if (duplicate)
            return Conflict("Username is already taken.");

        var owned = await _db.AppUsers.FirstOrDefaultAsync(x =>
            x.Id == actor.Id && x.Active, cancelToken);
        if (owned is null)
            return Unauthorized();

        owned.Username = newUsername;
        var newPhone = request.PhoneNumber?.Trim();
        // Keep a single canonical international form to avoid claiming the
        // same SMS destination under multiple textual phone representations.
        if (!string.IsNullOrWhiteSpace(newPhone) &&
            (newPhone[0] != '+' || newPhone.Length < 9 || newPhone.Length > 16 ||
             !newPhone.Skip(1).All(char.IsDigit)))
            return BadRequest("Phone number must be in E.164 format.");

        if (!string.IsNullOrWhiteSpace(newPhone) &&
            await _db.AppUsers.AsNoTracking().AnyAsync(x =>
                x.Id != actor.Id && x.PhoneNumber == newPhone, cancelToken))
            return Conflict("Phone number is already in use.");

        if (!string.Equals(owned.PhoneNumber, newPhone, StringComparison.Ordinal))
        {
            owned.PhoneNumber = newPhone;
            owned.PhoneVerified = false;
        }
        await _db.SaveChangesAsync(cancelToken);
        return Ok(new
        {
            owned.Id,
            owned.Username,
            owned.Email,
            owned.PhoneNumber,
            owned.PhoneVerified
        });
    }

    public sealed class UpdateProfileRequest
    {
        public string Username { get; set; }
        public string PhoneNumber { get; set; }
    }

    [HttpGet("addresses")]
    public async Task<IActionResult> GetAddresses(CancellationToken cancelToken)
    {
        if (await _currentUser.GetCurrentUserAsync(cancelToken) is null)
            return Unauthorized();

        var addresses = await _profile.GetCurrentUserAddressesAsync(cancelToken);
        return Ok(addresses.Select(x => new
        {
            x.Id,
            x.FirstName,
            x.LastName,
            x.Email,
            x.Address1,
            x.Address2,
            x.City,
            x.ZipPostalCode,
            x.PhoneNumber,
            x.CountryId,
            x.StateProvinceId
        }));
    }

    [HttpGet("roles")]
    public async Task<IActionResult> GetRoles(CancellationToken cancelToken)
    {
        if (await _currentUser.GetCurrentUserAsync(cancelToken) is null)
            return Unauthorized();

        var roles = await _profile.GetCurrentUserRolesAsync(cancelToken);
        return Ok(roles.Select(x => new { x.Id, x.Name, x.SystemName }));
    }

    [HttpGet("cart")]
    public async Task<IActionResult> GetCart(CancellationToken cancelToken)
    {
        if (await _currentUser.GetCurrentUserAsync(cancelToken) is null)
            return Unauthorized();

        var items = await _profile.GetCurrentUserCartItemsAsync(cancelToken);
        return Ok(items.Select(x => new
        {
            x.Id,
            x.ProductId,
            x.StoreId,
            x.Quantity,
            x.ShoppingCartTypeId
        }));
    }
}
