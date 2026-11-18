using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Smartstore.Core.Identity;

internal class AppUserRoleMappingMap : IEntityTypeConfiguration<AppUserRoleMapping>
{
    public void Configure(EntityTypeBuilder<AppUserRoleMapping> builder)
    {
        builder.HasIndex(x => new { x.AppUserId, x.CustomerRoleId }).IsUnique();

        builder.HasOne<AppUser>().WithMany().HasForeignKey(x => x.AppUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<CustomerRole>().WithMany().HasForeignKey(x => x.CustomerRoleId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>
/// User-owned role membership; the shared role definitions use the legacy
/// CustomerRole name until the role/permission cutover.
/// </summary>
public class AppUserRoleMapping : BaseEntity
{
    public int AppUserId { get; set; }

    public int CustomerRoleId { get; set; }
}
