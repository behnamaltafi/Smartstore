using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Smartstore.Core.Common;
using Smartstore.Core.Identity;

namespace Smartstore.Web.Controllers;

/// <summary>
/// Personal address-book CRUD. The identity of the owner always comes from
/// the validated authenticated AppUser, never from a CustomerId or request body.
/// </summary>
[ApiController]
[Authorize]
[Route("api/app-user/addresses")]
public sealed class AppUserAddressesController : PublicController
{
    private readonly ICurrentAppUserContext _current;
    private readonly IAppUserAddressBookService _addresses;

    public AppUserAddressesController(
        ICurrentAppUserContext current,
        IAppUserAddressBookService addresses)
    {
        _current = current;
        _addresses = addresses;
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        [FromBody] AddressRequest request, CancellationToken cancelToken)
    {
        if (await _current.GetCurrentUserAsync(cancelToken) is null)
            return Unauthorized();

        var id = await _addresses.AddAddressAsync(request.ToEntity(), cancelToken);
        return id.HasValue ? Ok(new { addressId = id.Value }) : Unauthorized();
    }

    [HttpPut("{id:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update(
        int id, [FromBody] AddressRequest request, CancellationToken cancelToken)
    {
        if (await _current.GetCurrentUserAsync(cancelToken) is null)
            return Unauthorized();

        return await _addresses.ReplaceAddressAsync(id, request.ToEntity(), cancelToken)
            ? NoContent()
            : NotFound();
    }

    [HttpDelete("{id:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken cancelToken)
    {
        if (await _current.GetCurrentUserAsync(cancelToken) is null)
            return Unauthorized();

        return await _addresses.RemoveAddressAsync(id, cancelToken)
            ? NoContent()
            : NotFound();
    }

    [HttpPost("{id:int}/default")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetDefault(int id, CancellationToken cancelToken)
    {
        if (await _current.GetCurrentUserAsync(cancelToken) is null)
            return Unauthorized();

        return await _addresses.SetDefaultAddressAsync(id, cancelToken)
            ? NoContent()
            : NotFound();
    }

    public sealed class AddressRequest
    {
        [Required, StringLength(225)]
        public string FirstName { get; set; }

        [Required, StringLength(225)]
        public string LastName { get; set; }

        [EmailAddress, StringLength(255)]
        public string Email { get; set; }

        [StringLength(255)]
        public string Company { get; set; }

        [Required, StringLength(500)]
        public string Address1 { get; set; }

        [StringLength(500)]
        public string Address2 { get; set; }

        [Required, StringLength(100)]
        public string City { get; set; }

        [StringLength(50)]
        public string ZipPostalCode { get; set; }

        [StringLength(100)]
        public string PhoneNumber { get; set; }

        public int? CountryId { get; set; }
        public int? StateProvinceId { get; set; }

        internal Address ToEntity() => new()
        {
            FirstName = FirstName,
            LastName = LastName,
            Email = Email,
            Company = Company,
            Address1 = Address1,
            Address2 = Address2,
            City = City,
            ZipPostalCode = ZipPostalCode,
            PhoneNumber = PhoneNumber,
            CountryId = CountryId,
            StateProvinceId = StateProvinceId
        };
    }
}
