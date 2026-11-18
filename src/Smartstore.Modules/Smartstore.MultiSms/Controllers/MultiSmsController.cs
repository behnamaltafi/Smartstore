using Microsoft.AspNetCore.Mvc;
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
    private readonly string[] _providerNames;

    public MultiSmsController(
        SmsCredentialProtection protection,
        ISmsSender sender,
        IEnumerable<ISmsProvider> providers)
    {
        _protection = protection;
        _sender = sender;
        _providerNames = providers.Select(x => x.Name)
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

        if (model.Enabled)
        {
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
            model.HasKavenegarApiKey = !string.IsNullOrEmpty(settings.EncryptedKavenegarApiKey);
            model.HasMelipayamakPassword = !string.IsNullOrEmpty(settings.EncryptedMelipayamakPassword);
            return View(model);
        }

        settings.Enabled = model.Enabled;
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

            var result = await _sender.SendAsync(new SmsSendRequest(input.TestPhone, input.TestMessage), cancelToken);
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

    private ConfigurationModel CreateModel(SmsGatewaySettings settings)
        => new()
        {
            AvailableProviders = _providerNames,
            Enabled = settings.Enabled,
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
