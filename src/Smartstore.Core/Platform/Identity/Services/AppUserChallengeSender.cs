using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Configuration;
using Smartstore.Core.Messaging;

namespace Smartstore.Core.Identity;

/// <summary>Replaceable challenge delivery transport for the independent AppUser.</summary>
public interface IAppUserChallengeSender
{
    Task<bool> SendPasswordResetAsync(string email, string token);
    Task<bool> SendEmailVerificationAsync(string email, string token);
    Task<bool> SendOtpAsync(string phone, string code);
}

/// <summary>
/// SMTP delivery and a pluggable SMS gateway for independent AppUser challenges.
/// Missing or disabled SMS providers fail closed. A successful send means provider
/// acceptance, not confirmed handset delivery.
/// </summary>
public sealed class ConfiguredAppUserChallengeSender : IAppUserChallengeSender
{
    private readonly IConfiguration _settings;
    private readonly ISmsSender[] _smsSenders;

    public ConfiguredAppUserChallengeSender(IConfiguration settings, IEnumerable<ISmsSender> smsSenders)
    {
        _settings = settings;
        _smsSenders = smsSenders.ToArray();
    }

    public async Task<bool> SendOtpAsync(string phone, string code)
    {
        // An absent SMS module must not silently fall back to a separate,
        // provider-specific endpoint or create an unaudited authentication path.
        if (_smsSenders.Length != 1 || string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        try
        {
            await _smsSenders[0].SendAsync(new SmsSendRequest(
                phone, $"Your Smartstore verification code is {code}. Valid for 5 minutes."));
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The provider may have accepted a request whose outcome was ambiguous.
            // Do not retry or fail over here; the provider gateway enforces its own
            // safe-failover policy and OTP responses remain non-enumerating.
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
