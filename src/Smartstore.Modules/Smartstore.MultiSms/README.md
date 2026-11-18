# Multi-provider SMS Gateway

Independent Smartstore module for the upcoming OTP-login implementation.

## Supported providers

- **Kavenegar**: regular SMS send endpoint, API key and sender ID.
- **Melipayamak**: official legacy REST `SendSMS` endpoint, username/password and sender ID.

Providers implement `ISmsProvider`. Register new provider implementations with DI;
the public `ISmsSender` contract is defined in `Smartstore.Core.Messaging`.

## Enable the module

1. Build `Smartstore.sln`, which deploys the module into `Smartstore.Web/Modules/Smartstore.MultiSms`.
2. In Smartstore's **Admin → Plugins/Modules**, install **Multi-provider SMS Gateway**.
3. Open its **Configure** page (also linked in the Modules admin menu).
4. Enable and configure at least one provider; enter the API credentials and sender.
5. Choose the primary provider and optionally enable the distinct fallback provider.
6. Save the configuration. The next SMS uses these settings, without a process restart.
7. Use **Send test SMS** to test the saved configuration with your real test number.

The settings page is restricted by Smartstore's `AdminController` authorization.
No anonymous/public SMS endpoint is created.

## Integration: future OTP feature

Inject `Smartstore.Core.Messaging.ISmsSender` in a service and call:

```csharp
var receipt = await smsSender.SendAsync(
    new SmsSendRequest("+989121234567", "Your verification code is: 123456"), cancelToken);
```

`receipt` means **accepted by the provider**, not delivered to the phone.
The OTP service must independently implement secure random code generation, server-side
hashing, expiration, replay prevention, user/IP/phone rate limiting, attempt lockouts,
audit trails, and (if needed) provider-specific OTP template support. None of these
authentication behaviors is part of this module.

## Failover semantics

- Provider selection is loaded from Smartstore settings **on each call**; the settings cache
  invalidation makes a saved change visible to subsequent sends across app instances.
- Only **definite provider rejection** (e.g. an explicit API rejection) may trigger fallback.
- A timeout, connection failure, bad response, or unknown final state **must not** trigger
  automatic resend because the first provider might already have accepted the message.
- There are no automatic retries; the caller should not blindly retry an ambiguous failure.
- Each provider uses a reused HttpClient with a 12-second timeout, plus connection lifetime renewal.

## Secrets and deployment

- API credentials are encrypted using **ASP.NET Core Data Protection** before being saved
  to Smartstore settings. Existing secrets are not displayed in admin forms; submitting a
  blank password retains the old value.
- Set up a **persistent Data Protection key ring** for production. Share it between all nodes.
  Back it up; losing those keys makes saved SMS credentials undecryptable.
- Do not put credentials into logs, diagnostics, or Git. Only a privileged admin can
  edit and test the configuration. Avoid logging raw SMS bodies (especially OTP codes).
- For Kavenegar, the official URL includes the API key; the module uses a private HttpClient
  rather than the default HttpClientFactory HTTP request logging pipeline.
- For deployments with multiple instances, configure the Smartstore shared settings cache
  as appropriate; the database-backed config is authoritative.

## Scope

No schema migration is required; the module uses Smartstore's existing settings store.
The legacy `Smartstore.Clickatell` module is left intact. Disable it independently
if its order-notification behavior is unwanted.

No network calls are made during compilation/installation. **Real delivery tests require
valid credentials, account balance, and a reachable provider API**, which CI cannot
validate automatically.
