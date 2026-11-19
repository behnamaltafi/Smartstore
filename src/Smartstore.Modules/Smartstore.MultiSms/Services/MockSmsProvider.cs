using Microsoft.Extensions.Hosting;
using Smartstore.MultiSms.Configuration;

namespace Smartstore.MultiSms.Services;

/// <summary>Explicitly development-only transport; never reaches an external SMS endpoint.</summary>
public sealed class MockSmsProvider : ISmsProvider
{
    private readonly IHostEnvironment _environment;
    private readonly DevelopmentSmsInbox _inbox;

    public MockSmsProvider(IHostEnvironment environment, DevelopmentSmsInbox inbox)
    {
        _environment = environment;
        _inbox = inbox;
    }

    public string Name => "Mock";

    public bool IsAvailable(SmsGatewaySettings settings)
        => _environment.IsDevelopment() && settings.MockEnabled;

    public Task<string> SendAsync(string phone, string text, SmsGatewaySettings settings, CancellationToken cancelToken)
    {
        cancelToken.ThrowIfCancellationRequested();
        if (!IsAvailable(settings))
        {
            throw new SmsProviderException("Development SMS provider is not available.");
        }

        return Task.FromResult(_inbox.Receive(phone, text));
    }
}
