using Microsoft.Extensions.DependencyInjection;
using Smartstore.Core.Messaging;
using Smartstore.Engine;
using Smartstore.Engine.Builders;
using Smartstore.MultiSms.Services;

namespace Smartstore.MultiSms;

internal sealed class Startup : StarterBase
{
    public override void ConfigureServices(IServiceCollection services, IApplicationContext appContext)
    {
        services.AddSingleton<SmsCredentialProtection>();
        services.AddSingleton<ISmsProvider, KavenegarSmsProvider>();
        services.AddSingleton<ISmsProvider, MelipayamakSmsProvider>();
        services.AddScoped<ISmsSender, SmsGatewaySender>();
    }
}
