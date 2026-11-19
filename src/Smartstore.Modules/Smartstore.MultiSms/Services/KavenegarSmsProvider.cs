using Smartstore.MultiSms.Configuration;

namespace Smartstore.MultiSms.Services;

/// <summary>
/// Kavenegar REST API: POST /v1/{apiKey}/sms/send.json.
/// Do not use HttpClientFactory logging for this API: its URL contains the API key.
/// </summary>
public sealed class KavenegarSmsProvider : ISmsProvider
{
    private static readonly HttpClient Client = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5)
    })
    {
        Timeout = TimeSpan.FromSeconds(12)
    };

    private readonly SmsCredentialProtection _credentials;

    public KavenegarSmsProvider(SmsCredentialProtection credentials)
    {
        _credentials = credentials;
    }

    public string Name => "Kavenegar";

    public bool IsAvailable(SmsGatewaySettings settings)
        => settings.KavenegarEnabled &&
           !string.IsNullOrWhiteSpace(settings.EncryptedKavenegarApiKey) &&
           !string.IsNullOrWhiteSpace(settings.KavenegarSender);

    public async Task<string> SendAsync(string phone, string text, SmsGatewaySettings settings, CancellationToken cancelToken)
    {
        var key = _credentials.Unprotect(settings.EncryptedKavenegarApiKey);
        var url = $"https://api.kavenegar.com/v1/{Uri.EscapeDataString(key)}/sms/send.json";
        using var body = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["sender"] = settings.KavenegarSender,
            ["receptor"] = SmsPhoneNormalizer.ToProviderPhone(phone),
            ["message"] = text
        });

        // Never log the outgoing URL or raw response: they can contain credentials or OTPs.
        using var response = await Client.PostAsync(url, body, cancelToken);
        if (!response.IsSuccessStatusCode)
        {
            bool safe = response.StatusCode is HttpStatusCode.Unauthorized or
                HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests;
            throw new SmsProviderException($"Kavenegar HTTP {(int)response.StatusCode}.", safe);
        }

        var raw = await response.Content.ReadAsStringAsync(cancelToken);
        try
        {
            using var json = JsonDocument.Parse(raw);
            int status = json.RootElement.GetProperty("return").GetProperty("status").GetInt32();
            if (status != 200)
            {
                throw new SmsProviderException($"Kavenegar rejected SMS (status {status}).", safeToFailover: true);
            }

            var entries = json.RootElement.GetProperty("entries");
            if (entries.ValueKind != JsonValueKind.Array || entries.GetArrayLength() == 0)
            {
                throw new SmsProviderException("Kavenegar returned no message confirmation.");
            }

            var id = entries[0].GetProperty("messageid");
            return id.ValueKind == JsonValueKind.String ? id.GetString() : id.GetRawText();
        }
        catch (JsonException)
        {
            throw new SmsProviderException("Kavenegar response could not be validated.");
        }
        catch (KeyNotFoundException)
        {
            throw new SmsProviderException("Kavenegar confirmation is incomplete.");
        }
    }
}
