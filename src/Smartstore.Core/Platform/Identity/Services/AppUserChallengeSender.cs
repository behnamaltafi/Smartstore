using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Mail;
using Microsoft.Extensions.Configuration;

namespace Smartstore.Core.Identity;

/// <summary>Replaceable challenge delivery transport for the independent AppUser.</summary>
public interface IAppUserChallengeSender
{
    Task<bool> SendPasswordResetAsync(string email, string token);
    Task<bool> SendEmailVerificationAsync(string email, string token);
    Task<bool> SendOtpAsync(string phone, string code);
}

/// <summary>
/// TLS SMTP and an opt-in HTTPS SMS gateway. Configure provider credentials
/// through secrets, never source control. An absent gateway fails closed.
/// </summary>
public sealed class ConfiguredAppUserChallengeSender : IAppUserChallengeSender
{
    private readonly IConfiguration _settings;
    private readonly IHttpClientFactory _clients;

    public ConfiguredAppUserChallengeSender(IConfiguration settings, IHttpClientFactory clients)
    {
        _settings = settings;
        _clients = clients;
    }

    public async Task<bool> SendOtpAsync(string phone, string code)
    {
        var endpoint = _settings["AppUserAuth:Sms:Endpoint"];
        var apiKey = _settings["AppUserAuth:Sms:ApiKey"];
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var url) ||
            url.Scheme != Uri.UriSchemeHttps || string.IsNullOrWhiteSpace(apiKey))
            return false;

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(new
            {
                phone,
                message = $"Your Smartstore verification code is {code}. Valid for 5 minutes."
            })
        };
        request.Headers.TryAddWithoutValidation("X-Api-Key", apiKey);
        try
        {
            using var response = await _clients.CreateClient().SendAsync(request);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }

    public Task<bool> SendPasswordResetAsync(string email, string token) =>
        SendEmailAsync(email, "Smartstore AppUser password recovery",
            "Use this one-time reset token (15 minutes). Do not share it.\\n\\n" +
            BuildLinkOrToken("identity/passwordrecoveryconfirm", email, token));

    public Task<bool> SendEmailVerificationAsync(string email, string token) =>
        SendEmailAsync(email, "Smartstore AppUser email activation",
            "Confirm this email address using this one-time link (30 minutes).\\n\\n" +
            BuildLinkOrToken("customer/activation", email, token));

    private string BuildLinkOrToken(string path, string email, string token)
    {
        var origin = _settings["AppUserAuth:PublicBaseUrl"];
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length != 0)
            return token;

        return new Uri(uri, path + "?token=" + Uri.EscapeDataString(token) +
            "&email=" + Uri.EscapeDataString(email)).AbsoluteUri;
    }

    private async Task<bool> SendEmailAsync(string email, string subject, string body)
    {
        var section = _settings.GetSection("AppUserAuth:Smtp");
        var host = section["Host"];
        var from = section["From"];
        var username = section["Username"];
        var password = section["Password"];
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(from) ||
            string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            return false;

        using var smtp = new SmtpClient(host, section.GetValue<int?>("Port") ?? 587)
        {
            EnableSsl = true,
            UseDefaultCredentials = false,
            Credentials = new NetworkCredential(username, password),
            DeliveryMethod = SmtpDeliveryMethod.Network
        };
        using var message = new MailMessage(from, email)
        {
            Subject = subject,
            Body = body
        };
        try
        {
            await smtp.SendMailAsync(message);
            return true;
        }
        catch (SmtpException)
        {
            return false;
        }
    }
}
