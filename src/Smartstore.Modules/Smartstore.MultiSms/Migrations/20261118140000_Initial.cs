using FluentMigrator;
using Smartstore.Core.Data.Migrations;
using Smartstore.Data.Migrations;

namespace Smartstore.MultiSms.Migrations;

/// <summary>
/// Creates SMS-login-only tables without changing Smartstore Customer,
/// Identity cookies, or existing storefront tables.
/// </summary>
[MigrationVersion("2026-11-18 14:00:00", "MultiSms: NativeOtpLogin")]
internal sealed class Initial : Migration
{
    public override void Up()
    {
        if (!Schema.Table("MultiSmsLoginPhone").Exists())
        {
            Create.Table("MultiSmsLoginPhone")
                .WithIdColumn()
                .WithColumn("CustomerId").AsInt32().NotNullable()
                .WithColumn("Phone").AsString(16).NotNullable()
                .WithColumn("VerifiedOnUtc").AsDateTime2().NotNullable();
        }

        if (!Schema.Table("MultiSmsLoginPhone").Index("UX_MultiSmsLoginPhone_CustomerId").Exists())
        {
            Create.Index("UX_MultiSmsLoginPhone_CustomerId")
                .OnTable("MultiSmsLoginPhone").OnColumn("CustomerId").Ascending()
                .WithOptions().Unique();
        }

        if (!Schema.Table("MultiSmsLoginPhone").Index("UX_MultiSmsLoginPhone_Phone").Exists())
        {
            Create.Index("UX_MultiSmsLoginPhone_Phone")
                .OnTable("MultiSmsLoginPhone").OnColumn("Phone").Ascending()
                .WithOptions().Unique();
        }

        if (!Schema.Table("MultiSmsOtpChallenge").Exists())
        {
            Create.Table("MultiSmsOtpChallenge")
                .WithIdColumn()
                .WithColumn("Phone").AsString(16).NotNullable()
                .WithColumn("Purpose").AsString(80).NotNullable()
                .WithColumn("ProtectedCode").AsString(1024).Nullable()
                .WithColumn("ExpiresOnUtc").AsDateTime2().NotNullable()
                .WithColumn("LastSentOnUtc").AsDateTime2().NotNullable()
                .WithColumn("WindowStartUtc").AsDateTime2().NotNullable()
                .WithColumn("SentInWindow").AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn("FailedAttempts").AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn("Version").AsInt32().NotNullable().WithDefaultValue(0);
        }

        if (!Schema.Table("MultiSmsOtpChallenge").Index("UX_MultiSmsOtpChallenge_Phone_Purpose").Exists())
        {
            Create.Index("UX_MultiSmsOtpChallenge_Phone_Purpose")
                .OnTable("MultiSmsOtpChallenge")
                .OnColumn("Phone").Ascending()
                .OnColumn("Purpose").Ascending()
                .WithOptions().Unique();
        }
        if (!Schema.Table("MultiSmsOtpQuota").Exists())
        {
            Create.Table("MultiSmsOtpQuota")
                .WithIdColumn()
                .WithColumn("QuotaKey").AsString(64).NotNullable()
                .WithColumn("WindowStartUtc").AsDateTime2().NotNullable()
                .WithColumn("LastSendUtc").AsDateTime2().NotNullable()
                .WithColumn("SentCount").AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn("Version").AsInt32().NotNullable().WithDefaultValue(0);
        }

        if (!Schema.Table("MultiSmsOtpQuota").Index("UX_MultiSmsOtpQuota_QuotaKey").Exists())
        {
            Create.Index("UX_MultiSmsOtpQuota_QuotaKey")
                .OnTable("MultiSmsOtpQuota").OnColumn("QuotaKey").Ascending()
                .WithOptions().Unique();
        }
    }

    public override void Down()
    {
        // Login enrollment and audit-sensitive challenge state must not be
        // silently deleted when uninstalling or rolling back the module.
    }
}
