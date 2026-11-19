using FluentMigrator;
using Smartstore.Core.Data.Migrations;

namespace Smartstore.MultiSms.Migrations;

/// <summary>
/// New SMS registrations have an optional profile-completion step.
/// Existing manually-enrolled accounts remain completed after the upgrade.
/// </summary>
[MigrationVersion("2026-11-19 09:00:00", "MultiSms: UnifiedPhoneRegistration")]
internal sealed class UnifiedPhoneRegistration : Migration
{
    public override void Up()
    {
        if (!Schema.Table("MultiSmsLoginPhone").Column("ProfileCompletedOnUtc").Exists())
        {
            Alter.Table("MultiSmsLoginPhone")
                .AddColumn("ProfileCompletedOnUtc").AsDateTime2().Nullable();

            Execute.Sql("UPDATE MultiSmsLoginPhone SET ProfileCompletedOnUtc = VerifiedOnUtc WHERE ProfileCompletedOnUtc IS NULL");
        }
    }

    public override void Down()
    {
        // Preserve phone bindings and completed profiles when reverting a deployment.
    }
}
