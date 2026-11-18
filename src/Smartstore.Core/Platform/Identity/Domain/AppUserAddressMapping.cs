using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Smartstore.Core.Common;

namespace Smartstore.Core.Identity;

internal class AppUserAddressMappingMap : IEntityTypeConfiguration<AppUserAddressMapping>
{
    public void Configure(EntityTypeBuilder<AppUserAddressMapping> builder)
    {
        builder.HasIndex(x => new { x.AppUserId, x.AddressId }).IsUnique();

        builder.HasOne<AppUser>().WithMany().HasForeignKey(x => x.AppUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Address>().WithMany().HasForeignKey(x => x.AddressId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>
/// An independent user's address-book entry.
/// </summary>
public class AppUserAddressMapping : BaseEntity
{
    public int AppUserId { get; set; }

    public int AddressId { get; set; }
}
