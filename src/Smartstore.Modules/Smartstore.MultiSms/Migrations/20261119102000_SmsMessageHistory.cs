using FluentMigrator;
using Smartstore.Core.Data.Migrations;
using Smartstore.Data.Migrations;

namespace Smartstore.MultiSms.Migrations;

/// <summary>Module-owned SQL Server history. No changes to the Smartstore Customer schema.</summary>
[MigrationVersion("2026-11-19 10:20:00", "MultiSms: HistoryAndAttempts")]
internal sealed class SmsMessageHistory : Migration
{
    public override void Up()
    {
        if (!Schema.Table("MultiSmsMessage").Exists())
        {
            Create.Table("MultiSmsMessage")
                .WithIdColumn()
                .WithColumn("Phone").AsString(20).NotNullable()
                .WithColumn("Kind").AsString(20).NotNullable()
                .WithColumn("Status").AsString(20).NotNullable()
                .WithColumn("Provider").AsString(50).Nullable()
                .WithColumn("ProviderMessageId").AsString(150).Nullable()
                .WithColumn("Body").AsString(1500).Nullable()
                .WithColumn("CreatedOnUtc").AsDateTime2().NotNullable()
                .WithColumn("FinishedOnUtc").AsDateTime2().Nullable()
                .WithColumn("AttemptCount").AsInt32().NotNullable().WithDefaultValue(0);
        }

        if (!Schema.Table("MultiSmsMessage").Index("IX_MultiSmsMessage_Created").Exists())
        {
            Create.Index("IX_MultiSmsMessage_Created")
                .OnTable("MultiSmsMessage").OnColumn("CreatedOnUtc").Ascending();
        }
        if (!Schema.Table("MultiSmsMessage").Index("IX_MultiSmsMessage_Phone_Created").Exists())
        {
            Create.Index("IX_MultiSmsMessage_Phone_Created")
                .OnTable("MultiSmsMessage").OnColumn("Phone").Ascending()
                .OnColumn("CreatedOnUtc").Ascending();
        }

        if (!Schema.Table("MultiSmsSendAttempt").Exists())
        {
            Create.Table("MultiSmsSendAttempt")
                .WithIdColumn()
                .WithColumn("SmsMessageId").AsInt32().NotNullable()
                .WithColumn("Provider").AsString(50).NotNullable()
                .WithColumn("Status").AsString(20).NotNullable()
                .WithColumn("ProviderMessageId").AsString(150).Nullable()
                .WithColumn("ErrorCategory").AsString(60).Nullable()
                .WithColumn("StartedOnUtc").AsDateTime2().NotNullable()
                .WithColumn("DurationMs").AsInt32().NotNullable();
        }

        if (!Schema.Table("MultiSmsSendAttempt").Index("IX_MultiSmsSendAttempt_Message").Exists())
        {
            Create.Index("IX_MultiSmsSendAttempt_Message")
                .OnTable("MultiSmsSendAttempt").OnColumn("SmsMessageId").Ascending();
        }
    }

    public override void Down()
    {
        // Never automatically discard operational history during deployment rollback.
    }
}
