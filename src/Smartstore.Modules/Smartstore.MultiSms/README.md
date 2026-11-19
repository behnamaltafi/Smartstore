# Native Smartstore SMS Gateway and OTP login

This module is developed on a **fresh branch from Smartstore main**. It uses
Smartstore's existing ASP.NET Core Identity `Customer` and
`SignInManager<Customer>`. It does **not** add an `AppUser` entity, replace
Customer authentication, or depend on earlier identity-separation branches.

## Included

- A standalone `ISmsSender` contract in Smartstore.Core; extend with
  additional `ISmsProvider` implementations without changing OTP logic.
- Kavenegar and Melipayamak senders, runtime primary selection and safe
  failover only after definite rejection. Timeouts/unknown outcomes are not
  automatically retried to avoid duplicate SMS.
- Smartstore Admin module configuration: enable/disable SMS, choose primary
  and fallback providers, rotate encrypted credentials, send a test SMS,
  and independently enable/disable OTP login.
- Unified Customer **sign-in or registration** by verified mobile at `/sms-login`.
  Existing phone bindings sign in; unknown numbers create an original Smartstore
  Customer only AFTER the OTP is verified and only if native registration
  mode is Standard. New users are redirected to `/sms-login/profile`.
- The default username is the local mobile number (`09...`) and can be changed
  later on the profile page; mobile login always uses the verified phone binding.
- Email is optional in the SMS profile form. Native Smartstore still requires
  an Email value in its UserValidator, so phone-only customers have a reserved
  non-deliverable internal `sms-989...@accounts.invalid` placeholder until
  they supply a real email. Do not use it for transactional email/newsletters.
- Existing password-based accounts must explicitly verify and link a phone
  inside the normal customer account profile (`Customer → Info`); matching
  a username or unverified profile phone is NOT proof of ownership. Original password, external and checkout
  flows remain available.
- Encrypted-at-rest one-time codes, 5 minute lifetime, at most five
  verification attempts per challenge, 2 minute resend cooldown and
  10 sends in the challenge's rolling 24-hour window. Authenticated
  enrollment additionally has a cross-phone per-account limit.
- Migration: `Migrations/20261118140000_Initial.cs` creates three
  **module-owned** tables through Smartstore FluentMigrator:
  `MultiSmsLoginPhone`, `MultiSmsOtpChallenge` and `MultiSmsOtpQuota`.
  `Migrations/20261119090000_UnifiedPhoneRegistration.cs` also tracks
  profile completion on the module-owned phone-binding table. Neither
  migration modifies `Customer`, `Order` or default Identity tables.

## SQL Server message and attempt history

The central `SmsGatewaySender` records a parent `MultiSmsMessage` row
and a `MultiSmsSendAttempt` row for each transport attempt. Safe failover
creates **two attempt rows** under the same message: e.g.
Kavenegar/Rejected → Melipayamak/Accepted.

- `Accepted` means provider acceptance **only**, not handset delivery.
- `Rejected` means a definite provider rejection; `Unknown` means a
  potentially delivered request failed without a definitive receipt.
  Ambiguous sends are NEVER automatically retried.
- The default `HistoryEnabled=true` and `HistoryRetentionDays=90`
  may be changed in Admin settings (1–3650 days).
  Expired records are removed in bounded batches during subsequent history
  writes (at most once every six hours per running process); this is
  **best-effort request-triggered cleanup**, not a guaranteed exact-time
  scheduler. A disabled/idle gateway will not run retention cleanup.
- Full recipient phone numbers are retained in SQL to permit exact-match
  administrator search; admin history views mask them. Restrict database access
  accordingly. No message body is stored by default.
- The optional `StoreNonOtpMessageBody` flag stores bodies of messages
  explicitly classified `General`. OTP and manual Test message bodies are
  never persisted even if enabled.
- History write errors are non-blocking: they cannot turn an accepted SMS into
  a retryable error. The admin status is operational history, not a delivery
  status verification service. There is no delivery callback integration yet.
- Additional migration `20261119102000_SmsMessageHistory.cs` creates the
  two module-owned history tables (FluentMigrator), without touching Customer
  or core Smartstore schema.

The history is at Admin → Settings → SMS History or from the SMS module's
configuration page. The history listing includes a filter by full destination
phone, message type, status, pagination and drill-down into attempts.

## Development Mock provider and temporary inbox

1. Run the Smartstore web host with
   `ASPNETCORE_ENVIRONMENT=Development` (use a local **test database**).
2. Install/update the MultiSms module and apply its native SQL migrations.
3. In Admin → SMS Gateway: enable the gateway and OTP, check
   **Enable development Mock SMS**, set **Primary provider = Mock**,
   and save. No Kavenegar/Melipayamak key is required.
4. Open `/sms-login`, enter a test Iranian mobile and request the OTP.
5. Go to Admin → SMS Gateway → **Development SMS Inbox**.
   The test SMS and its code are visible there for **at most five minutes**,
   in process memory only. Use the code to complete login or auto-registration.
6. Open Admin → **SMS History** to see the request metadata, its accepted
   Mock attempt and the mock provider message ID. There is no OTP text or
   plaintext code in the SQL history.

**The mock is forcibly blocked outside Development**, regardless of settings,
and a production deployment with a stale primary Mock setting will fail closed
rather than sending real SMS as an unintended fallback. The inbox is
administrative-only, with no API that exposes OTPs to anonymous clients.
The temporary inbox is **single-instance only** and is cleared on app restart;
when running multiple development nodes, read from the same node that
processed the send.

## Install and configure

1. Build `Smartstore.sln` and install **Multi-provider SMS Gateway** under
   **Admin → Plugins/Modules**.
2. Open its Configure page. Configure Kavenegar and/or Melipayamak,
   enable **SMS Gateway**, and use **Send test SMS** before enabling OTP.
3. Enable **SMS one-time-code login**. Settings take effect for subsequent
   requests without a process restart.
4. **Sign in with SMS** appears automatically on the existing password login
   screen when both SMS Gateway and SMS one-time-code login are enabled.
   Separate CMS widget activation is not required. Alternatively, navigate
   to `/sms-login` directly.
5. On `/sms-login`, enter any valid Iranian mobile and verify the OTP:
   a previously verified number signs in; an unknown number creates a native
   Customer after successful verification, if Standard registration is allowed.
6. The first login for a new Customer opens `/sms-login/profile` to confirm
   the phone-based username and optionally enter name and email. The same page
   can later be used to change the public username without changing OTP login.
   Customers with an existing password account should use the password link
   and manage their phone under the native customer profile.
7. Back up the SQL Server database before installing/upgrading on a live shop.
   Verify the schema and restore procedure on a staging clone first.

## Mobile number management in profile

The old standalone `/sms-login/enroll` page is no longer used. Both native
`Customer → Info` and the new-customer `/sms-login/profile` show an inline
verified-mobile editor. Authenticated customers can **add** their first
login mobile or **change** it. Both actions request a fresh OTP for the
destination number. Only successful verification updates the unique phone
binding and `Customer.GenericAttributes.Phone` (contact number). Existing
OTP login remains bound to the previous number until confirmation succeeds.
The old `/sms-login/enroll` GET route remains a compatibility redirect to
the native profile. The historical `/api/sms-login/enroll/*` routes remain
aliases for the new `/api/sms-login/profile/phone/*` endpoints.

The native customer-info field labeled Phone is **not** the authentication
authority. Never log a customer in using an unverified profile field.
Changing the sign-in mobile currently requires an authenticated session and
a code sent to the *new* mobile; consider a stronger re-authentication step
for higher-assurance deployments.

## Acceptance scenarios

- Unbound number: OTP verifies mobile → creates one native Registered Customer
  with a mobile username → signs in → asks for optional profile details.
- Verified number: OTP signs in to the same existing account, even after the
  username changes.
- Legacy account with an unverified matching username or profile phone:
  signup fails closed instead of silently taking over an account; the
  customer must sign in using the old method and explicitly enroll the phone.
- Registration disabled, pending email validation or administrator approval:
  auto-registration is disabled rather than bypassing native approval rules.
  Existing verified SMS accounts may still sign in.
- Email is optional in this module's UI but native Smartstore still requires
  an internally valid Email. Other native screens may show the reserved
  placeholder, so core-wide optional-email semantics need separate work.
- A Development-only Mock provider and temporary admin inbox are now available;
  they let you test the complete OTP flow without a real SMS panel. Production
  must still be verified against real Kavenegar/Melipayamak credentials.

## Security and deployment

- Enrollment is explicitly tied to an authenticated, active, registered
  Customer. A phone from `Customer.GenericAttributes.Phone` is **never**
  considered a verified login identity.
- Native Smartstore cookies and Identity manager are reused. Customers with
  a separate two-factor-authentication requirement must continue using their
  existing sign-in method; this OTP route refuses to bypass that requirement.
- Challenge state is stored in the shop database (shared across nodes).
  Concurrent updates use optimistic concurrency. Unique indexes stop
  phone and challenge duplicates.
- Codes and provider secrets use ASP.NET Core Data Protection. Persist and
  share the Data Protection key ring across all application nodes; protect and
  back up the keys. Losing the ring invalidates pending OTPs and stored
  provider credentials.
- Do not log OTP bodies, Data Protection secrets, or provider URLs containing
  API credentials. A provider receipt confirms acceptance, **not delivery**.
- The built-in OTP throttles are per-number. Apply **global/per-IP limits at
  your reverse proxy or WAF** to stop SMS pumping across many phone numbers.
- Confirm all storefront registration approval rules, captcha strategy,
  checkout and Identity permissions in staging. Real provider sending cannot
  be tested without valid SMS provider credentials and account balance.

## Primary login method

In module configuration, **Use SMS as the primary sign-in method** controls
whether the native `/login` page opens SMS sign-in. It defaults to enabled and
only applies when both the SMS Gateway and SMS login are enabled. Turn it off
to make the password form primary while keeping the SMS option available.
Changes take effect on the next request without restarting the application.

The password form remains reachable at `/login?method=password`, including for
administrators and users who have not enrolled a phone yet. Native password
POST handling is preserved. The legacy `/sms-login/enroll` URL now redirects to the native customer
profile (and sends unauthenticated users to the password form first). Local return
URLs and guest checkout are preserved; external return URLs are discarded.

## JSON API

These endpoints are available independently of the optional OData WebApi module.
They use native Customer cookies, not JWTs or administrative OData credentials.

| Method | Path | Purpose |
| --- | --- | --- |
| GET | `/api/sms-login/session` | Availability, session, verified phone, profileRequired and CSRF token |
| POST | `/api/sms-login/request` | Request login OTP: `{ "phone": "09123456789" }` |
| POST | `/api/sms-login/verify` | Sign in or register: returns `profileRequired` and `profilePath` |
| POST | `/api/sms-login/profile` | Change username and save optional name/email using a signed-in cookie |
| POST | `/api/sms-login/profile/phone/request` | Request OTP to add or change the sign-in phone from profile |
| POST | `/api/sms-login/profile/phone/verify` | Confirm and save the new phone only after receiving an OTP |
| POST | `/api/sms-gateway/test` | Admin test: `{ "phone": "09123456789", "text": "Test" }` |

Keep cookies between requests. Read `requestToken` and `headerName` from Session,
then include that header on every POST, including anonymous login requests.
Fetch a fresh Session token after login or switching accounts. Missing/invalid
CSRF tokens return HTTP 400. Enrollment without a native login returns 401.
OTP-disabled endpoints return 404. Invalid codes return 400 with
`{ "code": "invalid_code" }`. Code requests return the same
`{ "code": "code_requested" }` for known and unknown numbers; they do not confirm
delivery. Existing cooldowns, quotas and attempt limits apply.

The delivery test additionally requires both `system.accessbackend` and
`configuration.setting` permissions. Success returns `accepted`, provider and
message ID; provider failures return HTTP 502 with `sms_not_confirmed`, without
credentials, exception details or message contents.

Example from a same-origin browser application (prepend the shop's path base
when hosted in a subdirectory):

```javascript
const session = await fetch('/api/sms-login/session', {
    credentials: 'same-origin'
}).then(response => response.json());

const response = await fetch('/api/sms-login/request', {
    method: 'POST',
    credentials: 'same-origin',
    headers: {
        'Content-Type': 'application/json',
        [session.headerName]: session.requestToken
    },
    body: JSON.stringify({ phone: '09123456789' })
});
```

The OTP views explicitly use the host `_Layout` to load the active theme and
generated assets. They display one step at a time and support resending a code
or changing the phone number without JavaScript.

## Verify in development

```powershell
dotnet build Smartstore.sln -c Release
dotnet test test/Smartstore.Core.Tests/Smartstore.Core.Tests.csproj -c Release --filter "FullyQualifiedName~SmsGatewaySenderTests|FullyQualifiedName~SmsPhoneNormalizerTests|FullyQualifiedName~SmsApiControllerTests|FullyQualifiedName~SmsLoginWidgetSourceTests|FullyQualifiedName~SmsPrimaryLoginFilterTests|FullyQualifiedName~SmsMockAndHistoryTests"
```

Do not run EF Core `Add-Migration` for these module tables. Smartstore's
native FluentMigrator migration is the schema owner for these objects, and
migration history is recorded in `__MigrationVersionInfo`.
