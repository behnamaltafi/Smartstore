using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Smartstore.Core.Identity;

internal class AppUserCustomerMap : IEntityTypeConfiguration<AppUserCustomer>
{
    public void Configure(EntityTypeBuilder<AppUserCustomer> builder)
    {
        builder.HasOne<AppUser>()
            .WithMany()
            .HasForeignKey(x => x.AppUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Customer>()
            .WithMany()
            .HasForeignKey(x => x.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>
/// Explicit authorization association between application users and commerce customers.
/// </summary>
[Index(nameof(AppUserId), nameof(CustomerId), IsUnique = true, Name = "UX_AppUserCustomer_AppUserId_CustomerId")]
[Index(nameof(CustomerId), Name = "IX_AppUserCustomer_CustomerId")]
public partial class AppUserCustomer : BaseEntity
{
    public int AppUserId { get; set; }

    public int CustomerId { get; set; }

    public bool Active { get; set; } = true;
}
