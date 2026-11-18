using FluentMigrator;
using Smartstore.Core.Catalog.Products;
using Smartstore.Core.Checkout.Cart;
using Smartstore.Core.Checkout.Orders;
using Smartstore.Core.Identity;
using Smartstore.Core.Logging;
using Smartstore.Data.Migrations;

namespace Smartstore.Core.Data.Migrations;

/// <summary>
/// Adds the independent AppUser schema without changing or deleting legacy
/// Customer accounts. This is a Smartstore FluentMigrator migration; do not
/// scaffold an EF Core migration for these same objects.
/// </summary>
[MigrationVersion("2026-11-18 10:30:00", "Core: IndependentAppUserIdentity")]
internal class AppUserIdentity : Migration
{
    private const string AppUserTable = nameof(AppUser);
    private const string AppUserCustomerTable = nameof(AppUserCustomer);
    private const string AppUserRoleMappingTable = nameof(AppUserRoleMapping);
    private const string AppUserAddressMappingTable = nameof(AppUserAddressMapping);

    public override void Up()
    {
        if (!Schema.Table(AppUserTable).Exists())
        {
            Create.Table(AppUserTable)
                .WithColumn(nameof(AppUser.Id)).AsInt32().PrimaryKey().Identity()
                .WithColumn(nameof(AppUser.UserGuid)).AsGuid().NotNullable()
                .WithColumn(nameof(AppUser.ExternalSubject)).AsString(450).Nullable()
                .WithColumn(nameof(AppUser.LegacyIdentityCustomerId)).AsInt32().Nullable()
                .WithColumn(nameof(AppUser.Username)).AsString(500).Nullable()
                .WithColumn(nameof(AppUser.Email)).AsString(500).Nullable()
                .WithColumn(nameof(AppUser.PhoneNumber)).AsString(40).Nullable()
                .WithColumn(nameof(AppUser.PhoneVerified)).AsBoolean().NotNullable().WithDefaultValue(false)
                .WithColumn(nameof(AppUser.PasswordHash)).AsString(1024).Nullable()
                .WithColumn(nameof(AppUser.SecurityStamp)).AsString(64).Nullable()
                .WithColumn(nameof(AppUser.EmailVerified)).AsBoolean().NotNullable().WithDefaultValue(false)
                .WithColumn(nameof(AppUser.EmailVerificationTokenHash)).AsString(int.MaxValue).Nullable()
                .WithColumn(nameof(AppUser.EmailVerificationExpiresUtc)).AsDateTime2().Nullable()
                .WithColumn(nameof(AppUser.EmailVerificationLastSentUtc)).AsDateTime2().Nullable()
                .WithColumn(nameof(AppUser.PasswordResetTokenHash)).AsString(128).Nullable()
                .WithColumn(nameof(AppUser.PasswordResetExpiresUtc)).AsDateTime2().Nullable()
                .WithColumn(nameof(AppUser.PasswordResetLastSentUtc)).AsDateTime2().Nullable()
                .WithColumn(nameof(AppUser.OtpHash)).AsString(1024).Nullable()
                .WithColumn(nameof(AppUser.OtpExpiresUtc)).AsDateTime2().Nullable()
                .WithColumn(nameof(AppUser.OtpPurpose)).AsString(int.MaxValue).Nullable()
                .WithColumn(nameof(AppUser.OtpLastSentUtc)).AsDateTime2().Nullable()
                .WithColumn(nameof(AppUser.OtpFailedAttempts)).AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn(nameof(AppUser.FailedLoginAttempts)).AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn(nameof(AppUser.LockoutEndUtc)).AsDateTime2().Nullable()
                .WithColumn(nameof(AppUser.DefaultBillingAddressId)).AsInt32().Nullable()
                .WithColumn(nameof(AppUser.DefaultShippingAddressId)).AsInt32().Nullable()
                .WithColumn(nameof(AppUser.Active)).AsBoolean().NotNullable().WithDefaultValue(true)
                .WithColumn(nameof(AppUser.CreatedOnUtc)).AsDateTime2().NotNullable()
                    .WithDefaultValue(SystemMethods.CurrentUTCDateTime);
        }

        EnsureUniqueIndex(AppUserTable, "UX_AppUser_UserGuid", nameof(AppUser.UserGuid));

        CreateMappingTable(AppUserCustomerTable, nameof(AppUserCustomer.AppUserId),
            nameof(AppUserCustomer.CustomerId), nameof(AppUserCustomer.Active));
        CreateMappingTable(AppUserRoleMappingTable, nameof(AppUserRoleMapping.AppUserId),
            nameof(AppUserRoleMapping.CustomerRoleId));
        CreateMappingTable(AppUserAddressMappingTable, nameof(AppUserAddressMapping.AppUserId),
            nameof(AppUserAddressMapping.AddressId));

        EnsureUniqueIndex(AppUserCustomerTable, "UX_AppUserCustomer_AppUserId_CustomerId",
            nameof(AppUserCustomer.AppUserId), nameof(AppUserCustomer.CustomerId));
        EnsureIndex(AppUserCustomerTable, "IX_AppUserCustomer_CustomerId",
            nameof(AppUserCustomer.CustomerId));
        EnsureUniqueIndex(AppUserRoleMappingTable, "IX_AppUserRoleMapping_AppUserId_CustomerRoleId",
            nameof(AppUserRoleMapping.AppUserId), nameof(AppUserRoleMapping.CustomerRoleId));
        EnsureUniqueIndex(AppUserAddressMappingTable, "IX_AppUserAddressMapping_AppUserId_AddressId",
            nameof(AppUserAddressMapping.AppUserId), nameof(AppUserAddressMapping.AddressId));

        EnsureForeignKey(AppUserCustomerTable, nameof(AppUserCustomer.AppUserId),
            AppUserTable, "FK_AppUserCustomer_AppUser_AppUserId");
        EnsureForeignKey(AppUserCustomerTable, nameof(AppUserCustomer.CustomerId),
            nameof(Customer), "FK_AppUserCustomer_Customer_CustomerId");
        EnsureForeignKey(AppUserRoleMappingTable, nameof(AppUserRoleMapping.AppUserId),
            AppUserTable, "FK_AppUserRoleMapping_AppUser_AppUserId");
        EnsureForeignKey(AppUserRoleMappingTable, nameof(AppUserRoleMapping.CustomerRoleId),
            nameof(CustomerRole), "FK_AppUserRoleMapping_CustomerRole_CustomerRoleId");
        EnsureForeignKey(AppUserAddressMappingTable, nameof(AppUserAddressMapping.AppUserId),
            AppUserTable, "FK_AppUserAddressMapping_AppUser_AppUserId");
        EnsureForeignKey(AppUserAddressMappingTable, nameof(AppUserAddressMapping.AddressId),
            "Address", "FK_AppUserAddressMapping_Address_AddressId");

        // Existing tables are preserved; only new nullable ownership columns are added.
        EnsureOptionalOwnerColumn(nameof(Order), nameof(Order.PlacedByAppUserId));
        EnsureOptionalOwnerColumn(nameof(ShoppingCartItem), nameof(ShoppingCartItem.AppUserId));
        EnsureOptionalOwnerColumn(nameof(BackInStockSubscription), nameof(BackInStockSubscription.AppUserId));
        EnsureOptionalOwnerColumn(nameof(ReturnCase), nameof(ReturnCase.AppUserId));
        EnsureOptionalOwnerColumn(nameof(CustomerContent), nameof(CustomerContent.AppUserId));
        EnsureOptionalOwnerColumn(nameof(ExternalAuthenticationRecord), nameof(ExternalAuthenticationRecord.AppUserId));
        EnsureOptionalOwnerColumn(nameof(RewardPointsHistory), nameof(RewardPointsHistory.AppUserId));
        EnsureOptionalOwnerColumn(nameof(WalletHistory), nameof(WalletHistory.AppUserId));
        EnsureOptionalOwnerColumn(nameof(ActivityLog), nameof(ActivityLog.AppUserId));

        // SQL Server filtered unique indexes tolerate multiple NULL values and
        // prevent concurrent registration from assigning the same identifier.
        IfDatabase("SqlServer").Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_AppUser_Username' AND object_id = OBJECT_ID(N'AppUser'))
    CREATE UNIQUE INDEX [UX_AppUser_Username] ON [AppUser] ([Username]) WHERE [Username] IS NOT NULL;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_AppUser_Email' AND object_id = OBJECT_ID(N'AppUser'))
    CREATE UNIQUE INDEX [UX_AppUser_Email] ON [AppUser] ([Email]) WHERE [Email] IS NOT NULL;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_AppUser_PhoneNumber' AND object_id = OBJECT_ID(N'AppUser'))
    CREATE UNIQUE INDEX [UX_AppUser_PhoneNumber] ON [AppUser] ([PhoneNumber]) WHERE [PhoneNumber] IS NOT NULL;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_AppUser_LegacyIdentityCustomerId' AND object_id = OBJECT_ID(N'AppUser'))
    CREATE UNIQUE INDEX [UX_AppUser_LegacyIdentityCustomerId] ON [AppUser] ([LegacyIdentityCustomerId]) WHERE [LegacyIdentityCustomerId] IS NOT NULL;
");
    }

    public override void Down()
    {
        // Intentionally non-destructive. Independent identities, their hashes,
        // memberships and actor ownership must never be silently deleted by a
        // rollback. Restore a reviewed database backup for a full rollback.
    }

    private void CreateMappingTable(string table, string left, string right, string active = null)
    {
        if (Schema.Table(table).Exists())
        {
            return;
        }

        var creation = Create.Table(table)
            .WithColumn(nameof(BaseEntity.Id)).AsInt32().PrimaryKey().Identity()
            .WithColumn(left).AsInt32().NotNullable()
            .WithColumn(right).AsInt32().NotNullable();

        if (active != null)
        {
            creation.WithColumn(active).AsBoolean().NotNullable().WithDefaultValue(true);
        }
    }

    private void EnsureOptionalOwnerColumn(string table, string column)
    {
        if (!Schema.Table(table).Column(column).Exists())
        {
            Create.Column(column).OnTable(table).AsInt32().Nullable();
        }

        EnsureIndex(table, "IX_" + table + "_" + column, column);
    }

    private void EnsureIndex(string table, string name, string column)
    {
        if (!Schema.Table(table).Index(name).Exists())
        {
            Create.Index(name).OnTable(table).OnColumn(column).Ascending();
        }
    }

    private void EnsureUniqueIndex(string table, string name, string column, string nextColumn = null)
    {
        if (Schema.Table(table).Index(name).Exists())
        {
            return;
        }

        if (nextColumn == null)
        {
            Create.Index(name).OnTable(table).OnColumn(column).Ascending()
                .WithOptions().Unique();
        }
        else
        {
            Create.Index(name).OnTable(table).OnColumn(column).Ascending()
                .OnColumn(nextColumn).Ascending().WithOptions().Unique();
        }
    }

    private void EnsureForeignKey(string table, string column, string referencedTable, string name)
    {
        if (!Schema.Table(table).Constraint(name).Exists())
        {
            Create.ForeignKey(name)
                .FromTable(table).ForeignColumn(column)
                .ToTable(referencedTable).PrimaryColumn(nameof(BaseEntity.Id));
        }
    }
}
