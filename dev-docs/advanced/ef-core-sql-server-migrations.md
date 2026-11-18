# SQL Server EF Core migration scaffolding

Smartstore's existing schema migrations use FluentMigrator. The SQL Server design-time
factory in `src/Smartstore.Web/Infrastructure/SmartDbContextDesignTimeFactory.cs`
allows **EF Core migration scaffolding for the real `SmartDbContext` model**, without
running the Smartstore database initializer. It does not automatically switch the
runtime schema owner from FluentMigrator to EF Core.

## Prerequisites

- .NET SDK and EF Core tooling version **10.0.8**, matching the repository's EF Core packages.
- A local Smartstore installation in `src/Smartstore.Web`, including an installed
  tenant's `App_Data/Tenants/Default/Settings.txt` (or the currently selected tenant).
- That tenant must use **SQL Server**. The design-time factory intentionally rejects
  other database providers.
- An explicit **backup** of the target database before applying migrations.

The factory reads the same local tenant settings, registered model assemblies,
provider-specific mappings, and dependency-injection setup as the storefront. It
does **not** call `InitializeDatabases()`, `EnsureCreated()`, `Migrate()`, or any
schema-changing method.

## Generate a migration

From Visual Studio's Package Manager Console (with the Smartstore solution open):

```powershell
Add-Migration SqlServerBaseline -Context SmartDbContext -Project Smartstore.Core -StartupProject Smartstore.Web -OutputDir EfMigrations
```

After the baseline is established, generate subsequent changes the same way:

```powershell
Add-Migration AddAppUserCredentials -Context SmartDbContext -Project Smartstore.Core -StartupProject Smartstore.Web -OutputDir EfMigrations
```

From the CLI, run the equivalent commands from the solution root:

```powershell
dotnet tool install --global dotnet-ef --version 10.0.8

dotnet ef migrations add SqlServerBaseline --context SmartDbContext --project src/Smartstore.Core/Smartstore.Core.csproj --startup-project src/Smartstore.Web/Smartstore.Web.csproj --output-dir EfMigrations
```

New migrations and the EF model snapshot are stored separately under
`src/Smartstore.Core/EfMigrations`. Do not place them in the existing
`src/Smartstore.Core/Migrations` FluentMigrator folder.

## Mandatory first-migration review (baseline)

**Do not run `Update-Database` immediately after creating `SqlServerBaseline`.**
Because the existing Smartstore tables were created by FluentMigrator and EF Core
has no existing model snapshot, the first EF migration may attempt to create *all*
tables again.

Before applying the first migration:

1. Review the generated `Up()`, `Down()` and model snapshot against the actual
   SQL Server schema. Retain in `Up()` **only** changes genuinely missing from
   the existing database; do not recreate pre-existing tables, indexes, or FKs.
2. If **every** table and column in the EF model already exists with compatible
   schema, the baseline can have empty `Up()` and `Down()` methods while keeping
   the full model snapshot. If new `AppUser` tables/columns are **not** present,
   **do not** use an empty baseline: either retain their creation operations in
   the reviewed initial migration, or create them using the existing controlled
   schema process *before* recording an empty baseline.
3. Check generated SQL before applying anything:

   ```powershell
   dotnet ef migrations script 0 SqlServerBaseline --context SmartDbContext --project src/Smartstore.Core/Smartstore.Core.csproj --startup-project src/Smartstore.Web/Smartstore.Web.csproj --output baseline-review.sql
   ```

4. Test on a **copy** of the real database, inspect the diff and only then update
   the intended database:

   ```powershell
   Update-Database -Context SmartDbContext -Project Smartstore.Core -StartupProject Smartstore.Web
   ```

EF Core tracks its changes in `__EFMigrationsHistory`; Smartstore continues to track
its legacy migrations in `__MigrationVersionInfo`. Both systems may operate on the
same SQL Server database, but **must not independently modify the same table, index,
or constraint**. Treat switching ownership of legacy tables as a deliberate separate
migration project, not an automatic consequence of enabling `Add-Migration`.

## Notes

- `Add-Migration` reads the local model and creates source files; it does not
  update the database.
- `Update-Database` **does** modify the database. It is intentionally not run
  automatically by the design-time factory.
- If the design-time tool reports that Smartstore is not installed, complete the
  local tenant configuration first; setting only a connection string in an EF
  CLI argument does not initialize Smartstore's custom model bootstrapping.
- New fields on an existing entity are included in EF model diffs, but any schema
  objects still owned by FluentMigrator require coordination to avoid collisions.
