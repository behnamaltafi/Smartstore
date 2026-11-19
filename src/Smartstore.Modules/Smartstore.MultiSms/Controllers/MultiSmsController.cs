using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Smartstore.Core.Messaging;
using Smartstore.Core.Security;
using Smartstore.MultiSms.Configuration;
using Smartstore.MultiSms.Models;
using Smartstore.MultiSms.Services;
using Smartstore.Web.Controllers;
using Smartstore.Web.Modelling.Settings;

namespace Smartstore.MultiSms.Controllers;

/// <summary>
/// Protected administration-only configuration and manual delivery smoke test.
/// No anonymous send endpoint is exposed by this module.
/// </summary>
[Permission("configuration.setting")]
public sealed class MultiSmsController : AdminController
{
    private readonly SmsCredentialProtection _protection;
    private readonly ISmsSender _sender;
    private readonly IHostEnvironment _environment;
    private readonly SmsHistoryStore _history;
    private readonly DevelopmentSmsInbox _inbox;
    private readonly string[] _providerNames;

    public MultiSmsController(
        SmsCredentialProtection protection,
        ISmsSender sender,
        IEnumerable<ISmsProvider> providers,
        IHostEnvironment environment,
        SmsHistoryStore history,
        DevelopmentSmsInbox inbox)
    {
        _protection = protection;
        _sender = sender;
        _environment = environment;
        _history = history;
        _inbox = inbox;
        _providerNames = providers
            .Where(x => environment.IsDevelopment() || !string.Equals(x.Name, "Mock", StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToArray();
    }

    [LoadSetting(false)]
    public IActionResult Configure(SmsGatewaySettings settings)
        => View(CreateModel(settings));

    [HttpPost, SaveSetting(false)]
    public IActionResult Configure(ConfigurationModel model, SmsGatewaySettings settings)
    {
        if (model.ClearKavenegarApiKey)
        {
            settings.EncryptedKavenegarApiKey = null;
        }
        else if (!string.IsNullOrWhiteSpace(model.NewKavenegarApiKey))
        {
            settings.EncryptedKavenegarApiKey = _protection.Protect(model.NewKavenegarApiKey);
        }

        if (model.ClearMelipayamakPassword)
        {
            settings.EncryptedMelipayamakPassword = null;
        }
        else if (!string.IsNullOrWhiteSpace(model.NewMelipayamakPassword))
        {
            settings.EncryptedMelipayamakPassword = _protection.Protect(model.NewMelipayamakPassword);
        }

        if (!IsValidProvider(model.PrimaryProvider))
        {
            ModelState.AddModelError(nameof(model.PrimaryProvider), "Unsupported SMS provider.");
        }

        if (model.EnableFailover && (!IsValidProvider(model.FallbackProvider) ||
            string.Equals(model.PrimaryProvider, model.FallbackProvider, StringComparison.OrdinalIgnoreCase)))
        {
            ModelState.AddModelError(nameof(model.FallbackProvider), "Choose a different supported fallback provider.");
        }

        if (model.HistoryRetentionDays < 1 || model.HistoryRetentionDays > 3650)
        {
            ModelState.AddModelError(nameof(model.HistoryRetentionDays), "Retention must be between 1 and 3650 days.");
        }

        if (model.Enabled)
        {
            if (model.PrimaryProvider == "Mock" && (!_environment.IsDevelopment() || !model.MockEnabled))
            {
                ModelState.AddModelError(nameof(model.PrimaryProvider), "Mock provider is available only when enabled in Development.");
            }
            if (model.PrimaryProvider == "Kavenegar" &&
                (!model.KavenegarEnabled || string.IsNullOrWhiteSpace(model.KavenegarSender) ||
                 string.IsNullOrWhiteSpace(settings.EncryptedKavenegarApiKey)))
            {
                ModelState.AddModelError(nameof(model.PrimaryProvider), "Configure and enable the Kavenegar sender and key.");
            }

            if (model.PrimaryProvider == "Melipayamak" &&
                (!model.MelipayamakEnabled || string.IsNullOrWhiteSpace(model.MelipayamakSender) ||
                 string.IsNullOrWhiteSpace(model.MelipayamakUsername) ||
                 string.IsNullOrWhiteSpace(settings.EncryptedMelipayamakPassword)))
            {
                ModelState.AddModelError(nameof(model.PrimaryProvider), "Configure and enable the Melipayamak credentials and sender.");
            }
        }

        if (model.Enabled && model.EnableFailover)
        {
            if (model.FallbackProvider == "Mock" && (!_environment.IsDevelopment() || !model.MockEnabled))
            {
                ModelState.AddModelError(nameof(model.FallbackProvider), "Mock fallback is available only in Development.");
            }
            if (model.FallbackProvider == "Kavenegar" &&
                (!model.KavenegarEnabled || string.IsNullOrWhiteSpace(model.KavenegarSender) ||
                 string.IsNullOrWhiteSpace(settings.EncryptedKavenegarApiKey)))
            {
                ModelState.AddModelError(nameof(model.FallbackProvider), "Configure and enable the fallback Kavenegar provider.");
            }

            if (model.FallbackProvider == "Melipayamak" &&
                (!model.MelipayamakEnabled || string.IsNullOrWhiteSpace(model.MelipayamakSender) ||
                 string.IsNullOrWhiteSpace(model.MelipayamakUsername) ||
                 string.IsNullOrWhiteSpace(settings.EncryptedMelipayamakPassword)))
            {
                ModelState.AddModelError(nameof(model.FallbackProvider), "Configure and enable the fallback Melipayamak provider.");
            }
        }

        if (!ModelState.IsValid)
        {
            model.NewKavenegarApiKey = null;
            model.NewMelipayamakPassword = null;
            ModelState.Remove(nameof(model.NewKavenegarApiKey));
            ModelState.Remove(nameof(model.NewMelipayamakPassword));
            model.AvailableProviders = _providerNames;
            model.DevelopmentMode = _environment.IsDevelopment();
            model.HasKavenegarApiKey = !string.IsNullOrEmpty(settings.EncryptedKavenegarApiKey);
            model.HasMelipayamakPassword = !string.IsNullOrEmpty(settings.EncryptedMelipayamakPassword);
            return View(model);
        }

        settings.Enabled = model.Enabled;
        settings.OtpLoginEnabled = model.OtpLoginEnabled;
        settings.MockEnabled = _environment.IsDevelopment() && model.MockEnabled;
        settings.HistoryEnabled = model.HistoryEnabled;
        settings.HistoryRetentionDays = model.HistoryRetentionDays;
        settings.StoreNonOtpMessageBody = model.StoreNonOtpMessageBody;
        settings.SmsAsPrimaryLogin = model.SmsAsPrimaryLogin;
        settings.PrimaryProvider = model.PrimaryProvider;
        settings.EnableFailover = model.EnableFailover;
        settings.FallbackProvider = model.FallbackProvider;
        settings.KavenegarEnabled = model.KavenegarEnabled;
        settings.KavenegarSender = model.KavenegarSender?.Trim();
        settings.MelipayamakEnabled = model.MelipayamakEnabled;
        settings.MelipayamakSender = model.MelipayamakSender?.Trim();
        settings.MelipayamakUsername = model.MelipayamakUsername?.Trim();

        NotifySuccess(T("Admin.Common.DataSuccessfullySaved"));
        return RedirectToAction(nameof(Configure));
    }

    [HttpPost, LoadSetting(false)]
    public async Task<IActionResult> TestSms(ConfigurationModel input, SmsGatewaySettings settings, CancellationToken cancelToken)
    {
        var model = CreateModel(settings);
        model.TestPhone = input.TestPhone;
        model.TestMessage = null; // Do not echo OTP-like text or the submitted body in a response.
        ModelState.Remove(nameof(model.TestMessage));

        try
        {
            if (string.IsNullOrWhiteSpace(input.TestPhone) || string.IsNullOrWhiteSpace(input.TestMessage))
            {
                throw new ArgumentException("Enter a recipient and test message.");
            }

            var result = await _sender.SendAsync(new SmsSendRequest(input.TestPhone, input.TestMessage, SmsMessageKind.Test), cancelToken);
            model.TestSucceeded = true;
            model.TestResult = T("Plugins.Sms.MultiSms.TestAccepted", result.Provider, result.MessageId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            model.TestSucceeded = false;
            model.TestResult = T("Plugins.Sms.MultiSms.TestRejected");
        }

        return View(nameof(Configure), model);
    }

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> History(
        string phone = null, string kind = null, string status = null,
        int page = 1, CancellationToken ct = default)
    {
        page = Math.Clamp(page, 1, 100_000);
        phone = string.IsNullOrWhiteSpace(phone) ? null
            : SmsPhoneNormalizer.Normalize(phone) ?? phone.Trim();
        kind = kind is "Otp" or "Test" or "General" ? kind : null;
        status = status is "Accepted" or "Rejected" or "Unknown" or "Pending" ? status : null;

        var data = await _history.SearchAsync(phone, kind, status, page, 30, ct);
        return View(new SmsHistoryModel
        {
            Messages = data.Items,
            Total = data.Total,
            Page = page,
            Phone = phone,
            Kind = kind,
            Status = status
        });
    }

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Message(int id, CancellationToken ct = default)
    {
        var message = await _history.FindAsync(id, ct);
        if (message is null) return NotFound();

        return View(new SmsHistoryDetailsModel
        {
            Message = message,
            Attempts = await _history.AttemptsAsync(id, ct)
        });
    }

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult DevelopmentInbox()
    {
        // Defense in depth: production NEVER reads in-memory OTPs.
        if (!_environment.IsDevelopment() || !_inbox.IsAvailable)
        {
            return NotFound();
        }

        return View(_inbox.Snapshot());
    }

    private ConfigurationModel CreateModel(SmsGatewaySettings settings)
        => new()
        {
            AvailableProviders = _providerNames,
            DevelopmentMode = _environment.IsDevelopment(),
            MockEnabled = settings.MockEnabled,
            HistoryEnabled = settings.HistoryEnabled,
            HistoryRetentionDays = settings.HistoryRetentionDays,
            StoreNonOtpMessageBody = settings.StoreNonOtpMessageBody,
            Enabled = settings.Enabled,
            OtpLoginEnabled = settings.OtpLoginEnabled,
            SmsAsPrimaryLogin = settings.SmsAsPrimaryLogin,
            PrimaryProvider = settings.PrimaryProvider,
            EnableFailover = settings.EnableFailover,
            FallbackProvider = settings.FallbackProvider,
            KavenegarEnabled = settings.KavenegarEnabled,
            KavenegarSender = settings.KavenegarSender,
            HasKavenegarApiKey = !string.IsNullOrWhiteSpace(settings.EncryptedKavenegarApiKey),
            MelipayamakEnabled = settings.MelipayamakEnabled,
            MelipayamakSender = settings.MelipayamakSender,
            MelipayamakUsername = settings.MelipayamakUsername,
            HasMelipayamakPassword = !string.IsNullOrWhiteSpace(settings.EncryptedMelipayamakPassword)
        };

    private bool IsValidProvider(string provider)
        => _providerNames.Contains(provider, StringComparer.OrdinalIgnoreCase);
}
