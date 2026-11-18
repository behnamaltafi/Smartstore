using Autofac;
using Autofac.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Smartstore.Core.Data;
using Smartstore.Data;
using Smartstore.Engine;

namespace Smartstore.Web.Infrastructure;

/// <summary>
/// Builds the real Smartstore model for EF Core migration scaffolding without running
/// the application's database initializer or FluentMigrator.
/// </summary>
/// <remarks>
/// Design-time scaffolding requires an installed local Smartstore tenant. Creating a
/// migration does not connect to the database or apply schema changes.
/// </remarks>
public sealed class SmartDbContextDesignTimeFactory : IDesignTimeDbContextFactory<SmartDbContext>
{
    public SmartDbContext CreateDbContext(string[] args)
    {
        var contentRoot = FindWebContentRoot();
        var environmentName = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? Environments.Development;

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ContentRootPath = contentRoot,
            EnvironmentName = environmentName,
            Args = args
        });

        builder.Configuration
            .AddJsonFile("Config/connections.json", optional: true)
            .AddJsonFile($"Config/connections.{environmentName}.json", optional: true)
            .AddJsonFile("Config/usersettings.json", optional: true)
            .AddJsonFile($"Config/usersettings.{environmentName}.json", optional: true);

        builder.Host.UseServiceProviderFactory(new AutofacServiceProviderFactory());

        var appContext = new SmartApplicationContext(builder.Environment, builder.Configuration, NullLogger.Instance);
        var engine = EngineFactory.Create(appContext.AppConfiguration);
        var engineStarter = engine.Start(appContext);

        if (!appContext.IsInstalled)
        {
            throw new InvalidOperationException(
                "EF Core migration scaffolding requires an installed Smartstore tenant. " +
                "Configure the local web application's App_Data/Tenants/Default/Settings.txt first.");
        }

        if (DataSettings.Instance.DbFactory.DbSystem != DbSystemType.SqlServer)
        {
            throw new NotSupportedException(
                "This EF Core design-time factory supports SQL Server only.");
        }

        engineStarter.ConfigureServices(builder.Services);
        builder.Host.ConfigureContainer<ContainerBuilder>(engineStarter.ConfigureContainer);

        // Unlike Program.cs, design-time creation must not call InitializeDatabases().
        // That prevents migration scaffolding from applying pending FluentMigrator changes.
        var app = builder.Build();

        ((IServiceProviderContainer)appContext).ApplicationServices = app.Services;

        engine.Scope = new ScopedServiceContainer(
            app.Services.GetRequiredService<ILifetimeScopeAccessor>(),
            app.Services.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>(),
            app.Services.AsLifetimeScope());

        return app.Services.GetRequiredService<IDbContextFactory<SmartDbContext>>().CreateDbContext();
    }

    private static string FindWebContentRoot()
    {
        DirectoryInfo directory = new(Directory.GetCurrentDirectory());

        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Smartstore.Web.csproj")))
            {
                return directory.FullName;
            }

            var webDirectory = Path.Combine(directory.FullName, "src", "Smartstore.Web");
            if (File.Exists(Path.Combine(webDirectory, "Smartstore.Web.csproj")))
            {
                return webDirectory;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Cannot find src/Smartstore.Web. Run EF Core tooling from the Smartstore solution directory.");
    }
}
