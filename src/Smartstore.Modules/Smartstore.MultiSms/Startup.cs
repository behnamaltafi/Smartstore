using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Mvc;
using Smartstore.Core.Data;
using Smartstore.Core.Messaging;
using Smartstore.Core.Widgets;
using Smartstore.Data;
using Smartstore.Data.Providers;
using Smartstore.Engine;
using Smartstore.Engine.Builders;
using Smartstore.MultiSms.Domain;
using Smartstore.MultiSms.Filters;
using Smartstore.MultiSms.Services;
using Smartstore.Web.Controllers;

namespace Smartstore.MultiSms;

/// <summary>
/// Registers independent SMS transports and SQL-backed native Customer OTP.
/// </summary>
internal sealed class Startup : StarterBase
{
    public override void ConfigureServices(IServiceCollection services, IApplicationContext appContext)
    {
        services.AddSingleton<SmsCredentialProtection>();
        services.AddSingleton<ISmsProvider, KavenegarSmsProvider>();
        services.AddSingleton<ISmsProvider, MelipayamakSmsProvider>();
        services.AddSingleton<DevelopmentSmsInbox>();
        services.AddSingleton<ISmsProvider, MockSmsProvider>();
        services.AddScoped<SmsHistoryStore>();
        services.AddScoped<ISmsHistoryWriter>(sp => sp.GetRequiredService<SmsHistoryStore>());
        services.AddScoped<ISmsSender, SmsGatewaySender>();
        services.AddScoped<SmsOtpLoginService>();
        services.AddScoped<ISmsOtpLoginService>(services => services.GetRequiredService<SmsOtpLoginService>());
        services.AddScoped<IWidgetSource, SmsLoginWidgetSource>();
        services.Configure<MvcOptions>(options => options.Filters
            .AddEndpointFilter<SmsPrimaryLoginFilter, PublicController>()
            .ForController("Identity")
            .ForAction("Login"));
        services.AddTransient<IDbContextConfigurationSource<SmartDbContext>, MultiSmsModelConfigurer>();
    }

    private sealed class MultiSmsModelConfigurer : IDbContextConfigurationSource<SmartDbContext>
    {
        public void Configure(IServiceProvider services, DbContextOptionsBuilder builder)
        {
            builder.UseDbFactory(factory => factory.AddModelAssembly(typeof(SmsLoginPhone).Assembly));
        }
    }
}
