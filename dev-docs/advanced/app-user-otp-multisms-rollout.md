# AppUser OTP + MultiSms: SQL Server rollout checklist

This change set combines the draft identity separation and the draft MultiSms module.
It is **not production-ready** until an installed SQL Server tenant and actual SMS
credentials have been tested. The main branch remains unchanged during review.

## Ownership and database migration

- Smartstore's core schema is owned by **FluentMigrator**, recorded in
  `__MigrationVersionInfo`. The migration
  `src/Smartstore.Core/Migrations/20261118103000_AppUserIdentity.cs` creates
  `AppUser`, `AppUserCustomer`, `AppUserRoleMapping`,
  `AppUserAddressMapping` and new nullable user-ownership columns.
- It preserves all existing `Customer` rows, passwords, orders and address data.
  It **does not** copy old passwords, invent customer memberships from matching
  email/phone, or backfill `AppUser` identities.
- The migration deliberately has a non-destructive `Down()`. Plan a restore
  from a reviewed backup rather than dropping identity or order attribution.
- The independent MultiSms module stores encrypted settings through the
  existing Smartstore settings mechanism. **No SMS-specific table migration
  is needed** for the current synchronous sender.
- Do not run `Add-Migration` over these objects as a second schema owner.
  The experimental EF Core design-time factory is described in
  `dev-docs/advanced/ef-core-sql-server-migrations.md` and requires a reviewed
  baseline. A generated EF baseline that blindly creates all existing tables
  must never be applied to a populated Smartstore database.

## Safe review and verification

1. Back up an installed SQL Server tenant database and clone it into staging.
2. Build `Smartstore.sln` and run the focused identity and SMS tests.
3. On staging, inspect the pending FluentMigrator SQL and apply the migration
   through the normal Smartstore startup migration process. Verify four tables,
   mapping indexes/foreign keys, user unique indexes and optional ownership columns.
4. Define and test an explicit migration of historical login accounts before
   enabling AppUser authentication for an existing customer base. Never equate
   a person with a commercial `Customer` by a phone or email match.
5. Install the `Smartstore.MultiSms` module in Admin, configure encrypted
   Kavenegar and/or Melipayamak credentials, select the primary provider and
   optional safe fallback, and send a test SMS. Persist/shared Data Protection
   keys are required across instances.
6. Enable `AppUserAuth:Otp:Enabled` only after the credentials and staging
   flows work. The sign-in form is `/app-user/otp`.
7. Test OTP issue, expiry, wrong code/lockout, resend throttling, concurrent
   attempts, forgotten password, login/logout and invalidated sessions, using
   a real phone and SQL Server. Test a definite provider rejection and an
   ambiguous HTTP timeout separately. Neither must produce duplicate sends.
8. Verify checkout, payment, order history, actor-versus-buyer boundaries,
   legacy plugins and non-standard registration policies before merging to main.

## Authentication-to-sender integration

- `IAppUserChallengeSender` now uses the single installed `ISmsSender`.
  No installed SMS module means OTP delivery fails closed rather than sending
  through the old `AppUserAuth:Sms:Endpoint` path.
- SMS providers are selected from persisted runtime settings for each send.
- An accepted provider receipt is **not** handset delivery confirmation.
  Provider delivery callbacks, persistent outbox, template catalog and
  analytics are separate follow-on capabilities.
- The OTP service still enforces one-time hashing, expiry, lockout and
  rate limits. Sender configuration must never log authentication codes or keys.

## Commands

```powershell
dotnet build Smartstore.sln -c Release
dotnet test test/Smartstore.Core.Tests/Smartstore.Core.Tests.csproj -c Release --filter "FullyQualifiedName~AppUser|FullyQualifiedName~SmsGateway"
```

**Never run `Update-Database` or apply the migration to a live database as an
unreviewed first step.** SQL Server schema changes occur when Smartstore's
normal FluentMigrator bootstrapping is allowed to run against the configured
tenant, not when the source migration file is committed.
