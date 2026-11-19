using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Smartstore.Events;
using Smartstore.Core.Configuration;
using Smartstore.Core.Data;
using Smartstore.Core.Identity;
using Smartstore.Core.Messaging;
using Smartstore.MultiSms.Configuration;
using Smartstore.MultiSms.Domain;

namespace Smartstore.MultiSms.Services;

/// <summary>
/// Unifies mobile-number login and post-verification Customer registration.
/// Never trusts an unverified Customer.GenericAttributes.Phone value.
/// </summary>
public sealed class SmsOtpLoginService : ISmsOtpLoginService
{
    private readonly SmartDbContext _db;
    private readonly ISmsSender _sms;
    private readonly ISettingFactory _settings;
    private readonly IDataProtector _codeProtector;
    private readonly UserManager<Customer> _userManager;
    private readonly SignInManager<Customer> _signInManager;
    private readonly IEventPublisher _events;

    public SmsOtpLoginService(
        SmartDbContext db,
        ISmsSender sms,
        ISettingFactory settings,
        IDataProtectionProvider protection,
        UserManager<Customer> userManager,
        SignInManager<Customer> signInManager,
        IEventPublisher events)
    {
        _db = db;
        _sms = sms;
        _settings = settings;
        _codeProtector = protection.CreateProtector("Smartstore.MultiSms.Otp.v1");
        _userManager = userManager;
        _signInManager = signInManager;
        _events = events;
    }

    public async Task<bool> IsEnabledAsync()
    {
        var settings = await _settings.LoadSettingsAsync<SmsGatewaySettings>();
        return settings.Enabled && settings.OtpLoginEnabled;
    }

    public async Task<string> GetVerifiedPhoneAsync(int customerId, CancellationToken ct = default)
        => await _db.Set<SmsLoginPhone>().AsNoTracking()
            .Where(x => x.CustomerId == customerId)
            .Select(x => x.Phone)
            .FirstOrDefaultAsync(ct);

    public async Task<bool> RequestEnrollmentCodeAsync(Customer customer, string phone, CancellationToken ct = default)
    {
        var canonical = SmsPhoneNormalizer.Normalize(phone);
        if (canonical is null || customer is null || !customer.Active || customer.Deleted ||
            !customer.IsRegistered() || !await IsEnabledAsync())
        {
            return false;
        }

        if (await _db.Set<SmsLoginPhone>().AsNoTracking().AnyAsync(
            x => x.Phone == canonical && x.CustomerId != customer.Id, ct))
        {
            return false;
        }

        if (!await ConsumeEnrollmentQuotaAsync(customer.Id, ct))
        {
            return false;
        }

        return await IssueCodeAsync(canonical, "enroll:" + customer.Id, ct);
    }

    public async Task RequestLoginCodeAsync(string phone, CancellationToken ct = default)
    {
        var canonical = SmsPhoneNormalizer.Normalize(phone);
        if (canonical is null || !await IsEnabledAsync())
        {
            return;
        }

        // Same public response for known/unknown numbers. Unknown numbers receive an OTP
        // so they can register only AFTER proving possession of that mobile number.
        var binding = await _db.Set<SmsLoginPhone>().AsNoTracking()
            .FirstOrDefaultAsync(x => x.Phone == canonical, ct);
        if (binding is null)
        {
            var customerSettings = await _settings.LoadSettingsAsync<CustomerSettings>();
            if (customerSettings.UserRegistrationType != UserRegistrationType.Standard)
            {
                return;
            }
        }
        else
        {
            var customer = await _db.Customers.IncludeCustomerRoles().FirstOrDefaultAsync(
                x => x.Id == binding.CustomerId, ct);
            if (customer is null || !customer.Active || customer.Deleted || !customer.IsRegistered() ||
                !await _signInManager.CanSignInAsync(customer) ||
                await _userManager.IsLockedOutAsync(customer) ||
                await _userManager.GetTwoFactorEnabledAsync(customer))
            {
                return;
            }
        }

        // A consumed login challenge no longer stores its own cooldown.
        // Keep per-phone quota separately to prevent unlimited resend cycles
        // after repeated successful logins or registrations.
        if (!await ConsumeQuotaAsync("login-phone:" + canonical, TimeSpan.FromMinutes(2), ct))
        {
            return;
        }

        await IssueCodeAsync(canonical, "login", ct);
    }

    public async Task<bool> ConfirmEnrollmentAsync(Customer customer, string phone, string code,
        CancellationToken ct = default)
    {
        var canonical = SmsPhoneNormalizer.Normalize(phone);
        if (canonical is null || customer is null || !customer.Active || customer.Deleted ||
            !customer.IsRegistered() || !await IsEnabledAsync())
        {
            return false;
        }

        var challenge = await ValidateCodeAsync(canonical, "enroll:" + customer.Id, code, ct);
        if (challenge is null)
        {
            return false;
        }

        if (await _db.Set<SmsLoginPhone>().AsNoTracking().AnyAsync(
            x => x.Phone == canonical && x.CustomerId != customer.Id, ct))
        {
            return false;
        }

        var binding = await _db.Set<SmsLoginPhone>().FirstOrDefaultAsync(
            x => x.CustomerId == customer.Id, ct);
        if (binding is null)
        {
            _db.Set<SmsLoginPhone>().Add(new SmsLoginPhone
            {
                CustomerId = customer.Id,
                Phone = canonical,
                VerifiedOnUtc = DateTime.UtcNow,
                ProfileCompletedOnUtc = DateTime.UtcNow
            });
        }
        else
        {
            binding.Phone = canonical;
            binding.VerifiedOnUtc = DateTime.UtcNow;
            binding.ProfileCompletedOnUtc ??= DateTime.UtcNow;
        }

        // Profile contact phone follows the verified authentication number.
        // A profile form submission alone never changes the SMS login binding.
        customer.GenericAttributes.Phone = SmsPhoneNormalizer.ToProviderPhone(canonical);
        _db.Set<SmsOtpChallenge>().Remove(challenge);
        try
        {
            await _db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
            return false;
        }
    }

    /// <summary>
    /// One OTP works for BOTH sign-in and sign-up. Unbound numbers create a native
    /// Customer after code verification, never before it. Existing accounts are
    /// matched exclusively through the verified phone binding (no untrusted profile-phone lookup).
    /// </summary>
    public async Task<Customer> ConfirmLoginAsync(string phone, string code,
        CancellationToken ct = default)
    {
        var canonical = SmsPhoneNormalizer.Normalize(phone);
        if (canonical is null || !await IsEnabledAsync())
        {
            return null;
        }

        var binding = await _db.Set<SmsLoginPhone>().AsNoTracking()
            .FirstOrDefaultAsync(x => x.Phone == canonical, ct);

        Customer customer = null;
        CustomerSettings customerSettings = null;
        CustomerRole registeredRole = null;
        string username = null;
        string internalEmail = null;

        if (binding is not null)
        {
            customer = await _db.Customers.IncludeCustomerRoles()
                .FirstOrDefaultAsync(x => x.Id == binding.CustomerId, ct);
            if (customer is null || customer.Deleted || !customer.Active || !customer.IsRegistered() ||
                !await _signInManager.CanSignInAsync(customer) ||
                await _userManager.IsLockedOutAsync(customer) ||
                await _userManager.GetTwoFactorEnabledAsync(customer))
            {
                return null;
            }
        }
        else
        {
            customerSettings = await _settings.LoadSettingsAsync<CustomerSettings>();
            // Respect native shop registration policy. Never bypass email validation,
            // disabled registrations or administrator approval through the OTP path.
            if (customerSettings.UserRegistrationType != UserRegistrationType.Standard)
            {
                return null;
            }

            username = SmsPhoneNormalizer.ToProviderPhone(canonical);
            // Smartstore's native IUserValidator requires Email, even for passwordless
            // users. Reserved .invalid is never a deliverable email address.
            internalEmail = "sms-" + canonical[1..] + "@accounts.invalid";

            // Do not hijack legacy customers merely because their username/phone matches.
            // They must explicitly bind this mobile while signed in to their existing account.
            if (await _db.Customers.IgnoreQueryFilters().AnyAsync(
                x => x.Username == username || x.Email == internalEmail, ct))
            {
                return null;
            }

            registeredRole = await _db.CustomerRoles.FirstOrDefaultAsync(
                x => x.SystemName == SystemCustomerRoleNames.Registered, ct);
            if (registeredRole is null)
            {
                return null;
            }
        }

        var challenge = await ValidateCodeAsync(canonical, "login", code, ct);
        if (challenge is null)
        {
            return null;
        }

        // Consume the code atomically before adding the new Customer and phone binding.
        // The transaction rolls everything back if account creation fails.
        await using var transaction = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            var deleted = await _db.Set<SmsOtpChallenge>()
                .Where(x => x.Id == challenge.Id && x.Version == challenge.Version &&
                    x.ExpiresOnUtc > DateTime.UtcNow)
                .ExecuteDeleteAsync(ct);
            if (deleted != 1)
            {
                await transaction.RollbackAsync(ct);
                return null;
            }

            _db.Entry(challenge).State = EntityState.Detached;
            if (binding is null)
            {
                var now = DateTime.UtcNow;
                customer = new Customer
                {
                    Username = username,
                    Email = internalEmail,
                    Active = true,
                    CreatedOnUtc = now,
                    LastActivityDateUtc = now
                };

                customer.GenericAttributes.Phone = username;

                customer.CustomerRoleMappings.Add(new CustomerRoleMapping
                {
                    CustomerRoleId = registeredRole.Id,
                    CustomerRole = registeredRole
                });

                var extraRoleId = customerSettings.RegisterCustomerRoleId ?? 0;
                if (extraRoleId != 0 && extraRoleId != registeredRole.Id)
                {
                    var extraRole = await _db.CustomerRoles.FirstOrDefaultAsync(
                        x => x.Id == extraRoleId && x.Active, ct);
                    if (extraRole is not null)
                    {
                        customer.CustomerRoleMappings.Add(new CustomerRoleMapping
                        {
                            CustomerRoleId = extraRole.Id,
                            CustomerRole = extraRole
                        });
                    }
                }

                var result = await _userManager.CreateAsync(customer);
                if (!result.Succeeded)
                {
                    await transaction.RollbackAsync(ct);
                    return null;
                }

                _db.Set<SmsLoginPhone>().Add(new SmsLoginPhone
                {
                    CustomerId = customer.Id,
                    Phone = canonical,
                    VerifiedOnUtc = now
                    // ProfileCompletedOnUtc remains null until the new user confirms details.
                });
                await _db.SaveChangesAsync(ct);
            }

            await transaction.CommitAsync(ct);
            if (binding is null)
            {
                await _events.PublishAsync(new CustomerRegisteredEvent { Customer = customer });
            }
            return customer;
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(ct);
            _db.ChangeTracker.Clear();
            return null;
        }
    }

    public Task<bool> IsProfileIncompleteAsync(int customerId, CancellationToken ct = default)
        => _db.Set<SmsLoginPhone>().AsNoTracking()
            .AnyAsync(x => x.CustomerId == customerId && x.ProfileCompletedOnUtc == null, ct);

    public async Task<bool> MarkProfileCompletedAsync(int customerId, CancellationToken ct = default)
    {
        var row = await _db.Set<SmsLoginPhone>().FirstOrDefaultAsync(
            x => x.CustomerId == customerId, ct);
        if (row is null)
        {
            return false;
        }

        row.ProfileCompletedOnUtc ??= DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// Keep mobile identity independent from the public username. This also works
    /// for later profile edits, and never accepts a client-supplied customer ID.
    /// A blank email is allowed for phone-first users; Smartstore's core validator
    /// requires the reserved internal placeholder to remain in the database.
    /// </summary>
    public async Task<IdentityResult> CompleteProfileAsync(
        Customer customer, string username, string firstName, string lastName, string email,
        CancellationToken ct = default)
    {
        if (customer is null || !customer.Active || customer.Deleted || !customer.IsRegistered() ||
            !await IsEnabledAsync())
        {
            return IdentityResult.Failed(new IdentityError { Description = "The account is not eligible." });
        }

        var phone = await GetVerifiedPhoneAsync(customer.Id, ct);
        if (phone is null)
        {
            return IdentityResult.Failed(new IdentityError { Description = "A verified phone number is required." });
        }

        username = username?.Trim();
        if (string.IsNullOrEmpty(username) || username.Length > 100)
        {
            return IdentityResult.Failed(new IdentityError { Description = "Enter a valid username." });
        }

        firstName = firstName?.Trim();
        lastName = lastName?.Trim();
        email = email?.Trim();
        if (firstName?.Length > 225 || lastName?.Length > 225 || email?.Length > 500)
        {
            return IdentityResult.Failed(new IdentityError { Description = "A profile field is too long." });
        }

        var previous = (customer.Username, customer.Email, customer.FirstName,
            customer.LastName, customer.FullName);
        customer.Username = username;
        customer.Email = string.IsNullOrEmpty(email) ?
            "sms-" + phone[1..] + "@accounts.invalid" : email;
        customer.FirstName = firstName;
        customer.LastName = lastName;
        customer.FullName = (firstName + " " + lastName).Trim();

        var result = await _userManager.UpdateAsync(customer);
        if (!result.Succeeded)
        {
            (customer.Username, customer.Email, customer.FirstName,
                customer.LastName, customer.FullName) = previous;
            return result;
        }

        await MarkProfileCompletedAsync(customer.Id, ct);
        return result;
    }

    private Task<bool> ConsumeEnrollmentQuotaAsync(int customerId, CancellationToken ct)
        => ConsumeQuotaAsync("enroll-account:" + customerId, TimeSpan.FromMinutes(1), ct);

    private async Task<bool> ConsumeQuotaAsync(string key, TimeSpan cooldown, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var quotas = _db.Set<SmsOtpQuota>();
        var row = await quotas.FirstOrDefaultAsync(x => x.QuotaKey == key, ct);

        if (row is not null)
        {
            if (row.LastSendUtc > now.Subtract(cooldown))
            {
                return false;
            }

            if (row.WindowStartUtc <= now.AddDays(-1))
            {
                row.WindowStartUtc = now;
                row.SentCount = 0;
            }

            if (row.SentCount >= 10)
            {
                return false;
            }
        }

        var quota = row ?? new SmsOtpQuota
        {
            QuotaKey = key,
            WindowStartUtc = now
        };

        quota.LastSendUtc = now;
        quota.SentCount++;
        quota.Version++;

        if (row is null)
        {
            quotas.Add(quota);
        }

        try
        {
            await _db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
            return false;
        }
    }

    private async Task<bool> IssueCodeAsync(string phone, string purpose, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var challenges = _db.Set<SmsOtpChallenge>();
        var current = await challenges.FirstOrDefaultAsync(
            x => x.Phone == phone && x.Purpose == purpose, ct);

        if (current is not null)
        {
            // Two minutes between sends, maximum 10 sends in a rolling 24h.
            if (current.LastSentOnUtc > now.AddMinutes(-2))
            {
                return false;
            }

            if (current.WindowStartUtc <= now.AddDays(-1))
            {
                current.WindowStartUtc = now;
                current.SentInWindow = 0;
            }

            if (current.SentInWindow >= 10)
            {
                return false;
            }
        }

        string code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        var record = current ?? new SmsOtpChallenge
        {
            Phone = phone,
            Purpose = purpose,
            WindowStartUtc = now
        };

        record.ProtectedCode = _codeProtector.Protect(code);
        record.ExpiresOnUtc = now.AddMinutes(5);
        record.LastSentOnUtc = now;
        record.FailedAttempts = 0;
        record.SentInWindow++;
        record.Version++;

        if (current is null)
        {
            challenges.Add(record);
        }

        // The concurrency token and unique (phone,purpose) index stop parallel
        // requests from issuing several valid codes for the same destination.
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
            return false;
        }

        try
        {
            await _sms.SendAsync(new SmsSendRequest(phone,
                $"Your Smartstore verification code is {code}. Valid for 5 minutes.", SmsMessageKind.Otp), ct);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Never automatically retry an unknown provider outcome: duplicate
            // authentication SMS may be sent. A failed send retains its cooldown.
            return false;
        }
    }

    private async Task<SmsOtpChallenge> ValidateCodeAsync(
        string phone, string purpose, string code, CancellationToken ct)
    {
        if (code is not { Length: 6 } || !code.All(char.IsAsciiDigit))
        {
            return null;
        }

        var record = await _db.Set<SmsOtpChallenge>().FirstOrDefaultAsync(
            x => x.Phone == phone && x.Purpose == purpose, ct);
        if (record is null || record.ExpiresOnUtc <= DateTime.UtcNow ||
            record.FailedAttempts >= 5 || string.IsNullOrWhiteSpace(record.ProtectedCode))
        {
            return null;
        }

        bool valid = false;
        try
        {
            var expected = _codeProtector.Unprotect(record.ProtectedCode);
            valid = CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(code));
        }
        catch (CryptographicException)
        {
            // A rotated/lost key ring fails closed.
        }

        if (!valid)
        {
            record.FailedAttempts++;
            record.Version++;
            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                _db.ChangeTracker.Clear();
            }
            return null;
        }

        return record;
    }
}
