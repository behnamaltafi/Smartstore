# User–Customer separation: phase-1 integration and remaining work

> **DRAFT / DO NOT DEPLOY OR MERGE AS A COMPLETED CUTOVER.**
> No database migrations are included. The repository owner will author them
> at the end, once the domain and runtime work are complete.

## Target ownership and identity boundary

- **AppUser** is an authenticated person. Person-specific logins, roles,
  permissions, address books, carts, wishlists, reviews, reward balances,
  preferences and other personal records belong to an AppUser.
- **Customer** is a business buyer/account. `Order.CustomerId` identifies
  the buyer on an order, not the user who placed it.
- **AppUserCustomer** is an explicit many-to-many permission link:
  multiple people may represent one Customer, and a person may represent
  multiple Customers.
- **Order.PlacedByAppUserId** identifies the human who placed the order.
- Catalog/store/shared resources are not automatically user-owned.

## Changes currently on this branch

1. New AppUser, AppUserCustomer, AppUserAddressMapping and
   AppUserRoleMapping models and corresponding infrastructure.
2. Nullable transitional AppUserId references on several existing personal
   entities. Legacy CustomerId references have not all been removed.
3. CurrentAppUserContext validates a dedicated `app_user_guid` claim and
   rejects invalid/missing identities; it never interprets Customer.Id as User.Id.
4. Explicit `LegacyIdentityCustomerId` supports a *reviewed* migration
   mapping between the **old login account** and AppUser. Legacy sign-in
   issues a dedicated claim only for an explicitly linked, active AppUser.
   No user/buyer link is created automatically from email or mobile number.
5. Permission evaluation in AppUser sessions uses user role mappings,
   not the roles of the selected commercial buyer.
6. The personal cart uses `AppUserId` for AppUser session reads/writes.
   Additional guards reject cross-user item mutations and whole-cart deletion.
   Buyer switching never migrates a personal cart.
7. Buyer selection lives in `IAppUserBuyerContext` and the
   `api/app-user/customers` endpoints; membership is revalidated when used,
   including after revocation. Logout clears the selection cookie.
8. **Security correction:** the existing WorkContext remains tied to the
   legacy login/session. It is *never* globally replaced with the selected
   commercial buyer, because legacy account/profile controllers edit it.
9. At the order boundary, placement requires an explicitly selected,
   authorized business buyer, separately checks that the old checkout
   CustomerId is exactly `AppUser.LegacyIdentityCustomerId`, and persists
   the selected buyer in `Order.CustomerId`; human actor is separately
   recorded in `Order.PlacedByAppUserId`.
10. User-facing order operations check buyer membership; legacy order-history
    pages are restricted to the selected buyer when an AppUser session exists.
11. New read-only endpoints `api/app-user/orders` and
    `api/app-user/profile` expose only orders of currently authorized buyers,
    and only the authenticated user's own profile/address/role/cart records.
12. Added NUnit unit tests for legacy/invalid identity claims, buyer-cookie
    validation, and membership checks. **Tests have not been executed.**

## Important unresolved cutover work

- The existing **primary** username/password/registration/reset/OTP and
  external-authentication flows still use Customer-based credentials.
  In particular, Smartstore still registers `AddIdentity<Customer, CustomerRole>()`.
  The reviewed mapping is a transitional bridge, not independent AppUser login.
- Many older storefront/admin/controllers and services still read/write
  Customer-owned address books, permissions, accounts, rewards/wallet,
  privacy/reviews, recurring orders and other personal data.
  New AppUser columns are not an end-to-end data migration.
- Legacy checkout calculations, discounts, taxation, shipping, credit/rewards
  and payment gateways may still assume the old login CustomerId equals the
  commercial buyer. Before a production cutover, **audit every pricing/payment
  dependency** and implement the intended user/buyer separation consistently.
- API/admin/background/recurring-order authorization paths need the same
  explicit buyer boundary and regression coverage.
- Existing customer records, cart data, roles, addresses, order and identity
  relationships need a reviewed backfill plan. Do not infer access merely
  because two entities have the same phone/email.
- Run `dotnet build`, targeted unit tests, full authentication/authorization
  tests, security and checkout/payment regressions and database smoke tests.

## Migration policy — explicitly deferred

Do **not** add FluentMigrator migrations or change installation schema in this
branch. At the end, the repository owner will create the migration(s) to
safely establish user/link tables, indexes, unique constraints and foreign
keys; populate verified legacy mappings and per-record user ownership; verify
data integrity; and only then retire legacy relationships.

**This branch cannot be run against an unchanged database:** new AppUser
tables and AppUserId columns will not exist. Creating/applying the schema and
staged backfill is a prerequisite for functional integration tests and deployment.

## Minimum test scenarios after migrations

1. A single AppUser with membership in two buyers can switch buyers
   while the personal cart and roles remain unchanged.
2. Two AppUsers representing the same buyer can access that buyer's orders,
   but not each other's personal addresses/cart/roles.
3. A forged or stale buyer-selection cookie, revoked membership and an inactive
   user/buyer must fail closed.
4. A caller must not specify an unrelated legacy checkout CustomerId.
5. A user with matching email/mobile but **no membership** cannot view,
   modify, pay for or create an order for the buyer.
6. Admin/system/background operations must not accidentally inherit a
   shopper's buyer authorization or accept it by default.
7. Orders must store the business CustomerId and actor AppUserId correctly,
   and pricing/payment/tax/credit/returns remain consistent.
8. Register/login/logout/OTP/password-reset/external auth and full account
   CRUD must be verified after moving them to AppUser.


## Latest integration: person-owned address book

- New `IAppUserAddressBookService` and a dedicated authenticated
  `api/app-user/addresses` CRUD API in both stores.
- The legacy storefront address list/add/edit/delete UI is routed through
  AppUser personal address mappings when a validated AppUser claim is present,
  rather than through the selected commercial buyer.
- Copy-on-write address editing allocates a new Address and updates only the
  current AppUser link. Deleting an entry removes only the mapping; it never
  deletes the address row referenced by old orders or another person.
- `AppUser.DefaultBillingAddressId` and
  `AppUser.DefaultShippingAddressId` store personal defaults. On replace,
  the previous personal defaults follow the updated address; on unlink,
  the defaults are cleared. Smartstore's existing SetDefaultAddress action
  uses the AppUser settings for new sessions.
- Smartstore's address mapping is prevented from updating the legacy
  Customer default-address attributes for AppUser sessions. Its delete
  action is now an anti-forgery-protected POST.
- New isolated tests verify that anonymous requests cannot create/delete/
  replace or set another person's defaults, and nop's tests assert that
  creating an address copies data and links the authenticated AppUser.
- User-accessible buyer-order listing is additionally constrained to the
  active storefront to avoid accidental cross-store disclosure.

### Important remaining checkout integration

The personal default address fields are **not yet wired through every
legacy checkout step**. Old checkout shipping/billing selections and price,
tax, loyalty and payment assumptions may still use the legacy Customer
record even when the final Order.CustomerId is the chosen commercial buyer.
Full checkout and payment migration/reconciliation is mandatory before
releasing the independent-user flow.

No migrations are included in this PR. Build and tests are not executed.


## Continuation: security and ownership boundaries (2026-11-17)

- The legacy sign-in bridge now requires a **signed, immutable legacy login-account ID** in the same cookie identity as the trusted `app_user_guid`. A revoked or reassigned `LegacyIdentityCustomerId` is checked against the active `AppUser` record on each identity lookup; ambiguous claims and duplicate mappings fail closed. This is still **not** an independent AppUser login.
- New Nop sessions issue a stable `ClaimTypes.NameIdentifier` and resolve the legacy account by ID rather than mutable email/name. Existing Nop AppUser cookies issued without that claim require a new login.
- The buyer selected by a previous login is cleared at new password login (and on sign-out). Buyer membership is still checked server-side for every request.
- Return creation now validates access to the order's commercial buyer for AppUser sessions and records the actual requester in `ReturnRequest.AppUserId` / `ReturnCase.AppUserId`. Nop's return-history screen is restricted to the current AppUser for new AppUser sessions, rather than the legacy Customer's return history. Historical rows require verified user ownership backfill.
- Nop recurring-payment listing/cancel/retry now use the selected authorized commercial buyer, including a second check of the recurring payment's initial order owner. The legacy customer-keyed reward balance screen fails closed for an AppUser session until an AppUser-owned loyalty implementation exists.
- Smartstore's synchronous permission check was corrected to use the same trusted legacy-login binding as its asynchronous permission check. It must not authorize a stale AppUser claim after reassignment.
- Regression tests for missing/invalid/ambiguous identity claims were added. CI covers compilation and focused tests only; **it does not validate an end-to-end schema cutover, checkout accounting, or real sign-in.**

### Still blocking production

The primary password, registration, recovery, OTP and external login flows still use Customer-based identity. Smartstore still configures `AddIdentity<Customer, CustomerRole>()`; nop's main authentication interface still returns `Customer`. The many old personal-data, admin and commerce workflows are not fully migrated. Payment/credit/loyalty/shipping/tax and schema backfill must be reviewed before this branch can be considered finished. **Do not merge or deploy as a completed user/customer cutover.** No migrations were generated.

## Follow-up changes: product reviews, profile ownership and checkout safety (2026-11-17)

- Product reviews and helpfulness votes created through a validated AppUser session record `AppUserId` as the real author/voter; same commercial CustomerId is not treated as the same person. Nop account review history and one-review-per-product checks now use AppUser ownership. Smartstore review rewards do **not** credit the old legacy customer account for new AppUser sessions, pending user-owned reward migration.
- `PUT api/app-user/profile` permits changes to authenticated AppUser's username and phone (resetting `PhoneVerified` when it changes). It does **not** accept unverified email changes, roles, memberships or arbitrary user IDs. The old Customer Info POST rejects AppUser sessions to avoid rewriting a commercial Customer as a personal profile; the legacy account UI needs replacement with an AppUser form.
- Identity lookup now checks that the linked legacy login account is active, not deleted or a system account. Smartstore's synchronous permission path checks the same condition.
- **Checkout financial safety:** Until pricing, discount, tax, shipping, payment, refund and credit paths are calculated consistently for the selected *commercial buyer*, AppUser checkout for a buyer different from `ProcessPaymentRequest.CustomerId` returns a specific error **before charging**. This is a deliberate fail-closed temporary restriction; multi-buyer checkout is NOT complete. Never remove this guard before validating full buyer-level calculations.
- The old Customer-based sign-in, registration, password recovery, MFA/OTP, external authentication and admin/back-office flows are still live. This PR is not an independent authentication implementation; `LegacyIdentityCustomerId` remains a temporary security bridge.

### Required exit criteria for a completed cutover

1. Replace the **primary authentication principal** with AppUser, preserving secure cookie/token validation and security-stamp/session revocation. No login path may use Customer credentials, including OTP, external providers and account recovery.
2. Separate all user-owned data (profile, roles, address book, reviews, votes, wishlists, reward/wallet, private messages, subscriptions and user preferences) from commercial Customer. Provide usable storefront UI for the AppUser profile.
3. Finish buyer-level checkout calculation and payment reconciliation (including recurring orders, refunds, taxes, shipping, discounts and account credits). Then remove the temporary cross-buyer checkout block.
4. Ensure background jobs, admin API and export paths explicitly distinguish business buyer from actor and do not inherit shopper context.
5. Owner-created schema migrations and reviewed historical backfill must establish unique indices, verified identity mappings, ownership attribution and foreign keys; never match identities to buyers just on email/phone.
6. Run integration tests against the migrated database with 2 users sharing a buyer, 1 user with multiple buyers, revoke/disable tests and payment/provider regression tests. Focused compilation tests alone are not sufficient.

**Status remains DRAFT. No migrations were generated.**


## Independent AppUser authentication branch — November 17, 2026

### What is wired
The public password-login, registration, password-change, password-recovery,
OTP sign-in and external provider entry points now use **AppUser** credential
storage, not Customer password storage. Each app has a separate cookie scheme
with a signed AppUser GUID and a security stamp verified against the active
AppUser database row on each request. Password changes/resets rotate the stamp.

Endpoints: `GET /api/app-user/auth/antiforgery`, `POST /api/app-user/auth/register`,
`/login`, `/logout`, `/password/change`, `/password/reset/request`,
`/password/reset/complete`, `/otp/request`, `/otp/verify`,
`/phone/verify/request`, `/phone/verify/complete`,
`/email/verify/request`, `/email/verify/complete`;
`GET /api/app-user/auth/external/challenge?provider=...` and
`/external/callback`. For cookie-authenticated JSON requests first retrieve
the anti-forgery token and send it as the RequestVerificationToken header.
State-mutating API methods cannot be called without the CSRF token.

Password hashes use ASP.NET Identity PBKDF2, password reset secrets are
SHA-256-hashed and expire after 15 min, email verification expires after
30 min, and OTP is PBKDF2-hashed, expires after 5 min, allows at most 5
failed attempts and enforces send throttling. Telephone verification
is distinct from OTP login: only an authenticated AppUser can verify a
new unverified phone. All passwords are independent; do **not** copy
`CustomerPassword` or auto-link by matching email/phone.

New external users can be created only from an ASP.NET middleware-validated
external ticket with a verified email claim. Provider plus stable external
subject must be unique. Existing identities can only be reused with an
explicit reviewed matching `ExternalSubject` binding. External provider
plugins may only sign in previously bound AppUsers; they cannot register
or sign in Customers.

### Configuration required before any real sign-in
- Serve over HTTPS (independent auth cookies are Secure=Always).
- Configure a TLS SMTP relay using
  `AppUserAuth:Smtp:Host`, `Port`, `From`, `Username`, `Password`.
- Configure `AppUserAuth:PublicBaseUrl` to an absolute HTTPS origin. Reset
  and activation emails derive links solely from this trusted origin,
  never from incoming HTTP Host.
- Configure a real SMS sender: BonakMarketCommerce reuses `ISmsService`;
  Smartstore's default replaceable adapter sends `POST {phone,message}`
  to an HTTPS SMS gateway configured by `AppUserAuth:Sms:Endpoint`
  and `AppUserAuth:Sms:ApiKey` (X-Api-Key). Replace that adapter for a
  provider-specific protocol, retries, correlation IDs or failover.
- Configure at least one external OAuth/OIDC handler that issues a validated
  external-authentication ticket and includes stable subject / verified email.
  Test state, correlation and callback redirect URIs against that provider.
- No secrets or database credentials should be committed to this branch.

### Blocking issues before release (PR remains DRAFT)
1. **No migration was generated** per repository-owner instruction. Columns,
   normalized unique indexes, existing AppUser identity backfill, and
   external-subject ownership must be reviewed and migrated by the owner.
   The new code cannot operate against an old schema.
2. The old Customer identity infrastructure remains for compatibility with
   admin/plugin/internal services. Smartstore still calls
   `AddIdentity<Customer,CustomerRole>()`; migrating/removing that entire
   legacy dependency is NOT in this iteration. Do not claim global removal.
3. Registration is wired for `Standard` mode. Legacy
   `EmailValidation` and `AdminApproval` registration policies currently
   **fail closed** rather than silently bypassing activation/approval.
   A first-class AppUser approval workflow is required for these modes.
4. Legacy Customer-based 2FA/MFA plugin screens are not AppUser MFA. The
   BonakMarketCommerce legacy MFA entry points are deliberately disabled;
   the new passwordless AppUser SMS OTP is distinct from mandatory second
   factor. Provider-specific MFA enrollment must be migrated explicitly.
5. End-to-end tests on the migrated database, SMTP/SMS transport, browser
   sign-in/sign-out, external callback, concurrent signup/unique constraints,
   and account-lockout/rate-limiting tests have **not** been performed.
   Build + filtered unit tests are insufficient for release.
6. Commerce checkout, financial reconciliation and all historical
   buyer/user records remain scoped to the separate financial cutover;
   the temporary fail-closed cross-buyer checkout safeguard stays active.

Status: implemented independent AppUser auth paths with a compatibility
boundary, but **not a production-complete global Customer-to-AppUser
migration**. Do not merge this Draft as production ready.


### Authentication cutover update: legacy-cookie deauthorization
Following the introduction of a native AppUser cookie, the
`CurrentAppUserContext.GetCurrentUserAsync` resolver no longer authenticates
users from the old `Identity<Customer>` / Customer cookie, even if that
cookie contains an earlier `app_user_guid` bridge claim. Smartstore's
synchronous permissions path also rejects the legacy fallback. Old
sessions must sign out and reauthenticate as AppUser after the identity
data and password reset plan are migrated. The old bridge-claim presence
is still detected to **fail closed** rather than fall back to Customer
permissions. Earlier sections discussing the temporary bridge describe
the historical PR stages, not the current session policy.


### Registration and provider feature gates (security-critical)
- `AppUserAuth:AllowApiRegistration` defaults to **false**. Enable it
  only after adding equivalent CAPTCHA, privacy-consent and abuse controls
  to the JSON registration endpoint. The existing MVC registration route
  performs its own security filters and is not gated by this flag.
- `AppUserAuth:AllowExternalAutoProvision` defaults to **false**:
  first-time external users are not auto-created without explicit
  operator opt-in and the Standard registration policy. Verified,
  pre-linked provider subject sign-in remains supported.
- The AppUser JSON API cannot bypass the configured store registration
  mode. EmailValidation/AdminApproval still need independent policy
  handlers (the current branch refuses those modes).
- Smartstore SMS OTP login additionally requires
  `AppUserAuth:Otp:Enabled=true`; Nop's public OTP path honors
  `OtpSettings.LoginByPhoneEnabled`. All sender credentials are
  deployment secrets, not repository config.

### Native principal claim boundary
Independent cookies use `app_user_id`, `app_user_guid` and
`app_user_security_stamp` from the trusted AppUser issuer. They do **not**
emit `ClaimTypes.NameIdentifier` with an AppUser ID, because old
Customer-aware middleware can interpret that claim as a CustomerId and
accidentally access another person's customer record. Identity managers
must resolve native AppUser claims through `ICurrentAppUserContext`.
