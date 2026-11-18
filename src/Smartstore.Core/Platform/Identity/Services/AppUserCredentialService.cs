using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Smartstore.Core.Data;

namespace Smartstore.Core.Identity;

/// <summary>
/// Authenticates independent AppUser credentials. No method reads Customer passwords
/// or creates AppUserCustomer memberships by matching email or phone.
/// </summary>
public sealed class AppUserCredentialService
{
    private readonly SmartDbContext _db;
    private readonly Microsoft.AspNetCore.Identity.PasswordHasher<AppUser> _hasher = new();

    public AppUserCredentialService(SmartDbContext db) => _db = db;

    private static bool ValidPassword(string value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length >= 12 && value.Length <= 256;
    private static string Normalize(string value) => value?.Trim().ToUpperInvariant();

    public async Task<AppUser> RegisterAsync(string username, string email, string password,
        string phone = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(username) || username.Length > 500 ||
            string.IsNullOrWhiteSpace(email) || email.Length > 500 ||
            !email.Contains('@') || !ValidPassword(password))
            return null;

        var name = Normalize(username);
        var address = Normalize(email);
        phone = phone?.Trim();
        if (!string.IsNullOrEmpty(phone) &&
            (phone.Length > 40 || phone[0] != '+' || !phone.Skip(1).All(char.IsDigit)))
            return null;
        if (await _db.AppUsers.AsNoTracking().AnyAsync(x =>
            x.Username.ToUpper() == name || x.Email.ToUpper() == address ||
            (!string.IsNullOrEmpty(phone) && x.PhoneNumber == phone), ct))
            return null;

        var user = new AppUser
        {
            Username = username.Trim(),
            Email = email.Trim(),
            PhoneNumber = phone,
            PhoneVerified = false,
            UserGuid = Guid.NewGuid(),
            SecurityStamp = Guid.NewGuid().ToString("N"),
            EmailVerified = false,
            Active = true,
            CreatedOnUtc = DateTime.UtcNow
        };
        user.PasswordHash = _hasher.HashPassword(user, password);
        _db.AppUsers.Add(user);
        await _db.SaveChangesAsync(ct);
        return user;
    }

    public async Task<AppUser> AuthenticatePasswordAsync(string login, string password,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(login) || string.IsNullOrEmpty(password))
            return null;

        var normalized = Normalize(login);
        var matches = await _db.AppUsers
            .Where(x => x.Username.ToUpper() == normalized || x.Email.ToUpper() == normalized)
            .Take(2).ToArrayAsync(ct);
        if (matches.Length != 1)
            return null;

        var user = matches[0];
        if (!user.Active || string.IsNullOrEmpty(user.PasswordHash) ||
            user.LockoutEndUtc > DateTime.UtcNow)
            return null;

        PasswordVerificationResult result;
        try
        {
            result = _hasher.VerifyHashedPassword(user, user.PasswordHash, password);
        }
        catch (FormatException)
        {
            result = PasswordVerificationResult.Failed;
        }

        if (result == PasswordVerificationResult.Failed)
        {
            user.FailedLoginAttempts++;
            if (user.FailedLoginAttempts >= 5)
            {
                user.LockoutEndUtc = DateTime.UtcNow.AddMinutes(15);
                user.FailedLoginAttempts = 0;
            }
            await _db.SaveChangesAsync(ct);
            return null;
        }

        user.FailedLoginAttempts = 0;
        user.LockoutEndUtc = null;
        if (result == PasswordVerificationResult.SuccessRehashNeeded)
            user.PasswordHash = _hasher.HashPassword(user, password);
        await _db.SaveChangesAsync(ct);
        return user;
    }

    public async Task<bool> ChangePasswordAsync(AppUser authenticated, string oldPassword,
        string newPassword, CancellationToken ct = default)
    {
        if (authenticated is null || !ValidPassword(newPassword) || string.IsNullOrEmpty(oldPassword))
            return false;

        var user = await _db.AppUsers.FirstOrDefaultAsync(x =>
            x.Id == authenticated.Id && x.Active, ct);
        if (user is null || user.UserGuid != authenticated.UserGuid ||
            user.SecurityStamp != authenticated.SecurityStamp ||
            string.IsNullOrWhiteSpace(user.PasswordHash) ||
            _hasher.VerifyHashedPassword(user, user.PasswordHash, oldPassword) ==
                PasswordVerificationResult.Failed)
            return false;

        user.PasswordHash = _hasher.HashPassword(user, newPassword);
        RevokeSessions(user);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Issue an email confirmation secret only to the active authenticated AppUser.</summary>
    public async Task<string> BeginEmailVerificationAsync(AppUser authenticated,
        CancellationToken ct = default)
    {
        if (authenticated is null || !authenticated.Active)
            return null;

        var user = await _db.AppUsers.FirstOrDefaultAsync(x =>
            x.Id == authenticated.Id && x.Active, ct);
        if (user is null || user.UserGuid != authenticated.UserGuid ||
            user.SecurityStamp != authenticated.SecurityStamp || user.EmailVerified ||
            string.IsNullOrWhiteSpace(user.Email) ||
            user.EmailVerificationLastSentUtc > DateTime.UtcNow.AddMinutes(-2))
            return null;

        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        user.EmailVerificationTokenHash = Hash(token);
        user.EmailVerificationExpiresUtc = DateTime.UtcNow.AddMinutes(30);
        user.EmailVerificationLastSentUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return token;
    }

    public async Task<bool> CompleteEmailVerificationAsync(string email, string token,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(token))
            return false;

        var normalized = Normalize(email);
        var matches = await _db.AppUsers.Where(x => x.Active &&
            x.Email.ToUpper() == normalized).Take(2).ToArrayAsync(ct);
        if (matches.Length != 1)
            return false;

        var user = matches[0];
        if (user.EmailVerificationExpiresUtc <= DateTime.UtcNow ||
            !SecureHashEquals(user.EmailVerificationTokenHash, Hash(token)))
            return false;

        user.EmailVerified = true;
        user.EmailVerificationTokenHash = null;
        user.EmailVerificationExpiresUtc = null;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Return secret to server-side email sender only; do not serialize it to the client.</summary>
    public async Task<string> BeginPasswordResetAsync(string email, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(email))
            return null;
        var address = Normalize(email);
        var matches = await _db.AppUsers.Where(x => x.Active &&
            x.Email.ToUpper() == address).Take(2).ToArrayAsync(ct);
        if (matches.Length != 1)
            return null;

        var user = matches[0];
        if (user.PasswordResetLastSentUtc > DateTime.UtcNow.AddMinutes(-2))
            return null;

        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        user.PasswordResetTokenHash = Hash(token);
        user.PasswordResetExpiresUtc = DateTime.UtcNow.AddMinutes(15);
        user.PasswordResetLastSentUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return token;
    }

    public async Task<bool> CompletePasswordResetAsync(string email, string token,
        string password, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(token) ||
            !ValidPassword(password))
            return false;

        var address = Normalize(email);
        var matches = await _db.AppUsers.Where(x => x.Active &&
            x.Email.ToUpper() == address).Take(2).ToArrayAsync(ct);
        if (matches.Length != 1)
            return false;

        var user = matches[0];
        if (user.PasswordResetExpiresUtc <= DateTime.UtcNow ||
            !SecureHashEquals(user.PasswordResetTokenHash, Hash(token)))
            return false;

        user.PasswordHash = _hasher.HashPassword(user, password);
        user.EmailVerified = true;
        user.PasswordResetTokenHash = null;
        user.PasswordResetExpiresUtc = null;
        RevokeSessions(user);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>One-time code returned exclusively to a server-side SMS sender.</summary>
    public async Task<string> BeginOtpAsync(string phone, AppUser authenticated = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(phone) || phone.Length > 40)
            return null;

        AppUser user;
        if (authenticated is not null)
        {
            user = await _db.AppUsers.FirstOrDefaultAsync(x =>
                x.Id == authenticated.Id && x.Active &&
                x.UserGuid == authenticated.UserGuid &&
                x.SecurityStamp == authenticated.SecurityStamp &&
                x.PhoneNumber == phone, ct);
        }
        else
        {
            var matches = await _db.AppUsers.Where(x => x.Active &&
                x.PhoneVerified && x.PhoneNumber == phone).Take(2).ToArrayAsync(ct);
            user = matches.Length == 1 ? matches[0] : null;
        }
        if (user is null || user.LockoutEndUtc > DateTime.UtcNow ||
            user.OtpLastSentUtc > DateTime.UtcNow.AddMinutes(-1))
            return null;

        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        user.OtpHash = _hasher.HashPassword(user, code);
        user.OtpExpiresUtc = DateTime.UtcNow.AddMinutes(5);
        user.OtpLastSentUtc = DateTime.UtcNow;
        user.OtpFailedAttempts = 0;
        user.OtpPurpose = authenticated is null ? "login" : "verify-phone";
        await _db.SaveChangesAsync(ct);
        return code;
    }

    public async Task<AppUser> VerifyOtpAsync(string phone, string code,
        AppUser authenticated = null, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(phone) || string.IsNullOrEmpty(code) || code.Length != 6)
            return null;

        AppUser user;
        if (authenticated is null)
        {
            var matches = await _db.AppUsers.Where(x =>
                x.Active && x.PhoneVerified && x.PhoneNumber == phone)
                .Take(2).ToArrayAsync(ct);
            user = matches.Length == 1 ? matches[0] : null;
        }
        else
        {
            user = await _db.AppUsers.FirstOrDefaultAsync(x =>
                x.Id == authenticated.Id && x.Active &&
                x.UserGuid == authenticated.UserGuid &&
                x.SecurityStamp == authenticated.SecurityStamp &&
                x.PhoneNumber == phone, ct);
        }

        if (user is null ||
            user.OtpPurpose != (authenticated is null ? "login" : "verify-phone") ||
            user.OtpExpiresUtc <= DateTime.UtcNow ||
            user.LockoutEndUtc > DateTime.UtcNow ||
            string.IsNullOrEmpty(user.OtpHash) || user.OtpFailedAttempts >= 5)
            return null;

        if (_hasher.VerifyHashedPassword(user, user.OtpHash, code) ==
            PasswordVerificationResult.Failed)
        {
            user.OtpFailedAttempts++;
            if (user.OtpFailedAttempts >= 5)
                user.LockoutEndUtc = DateTime.UtcNow.AddMinutes(15);
            await _db.SaveChangesAsync(ct);
            return null;
        }

        user.OtpHash = null;
        user.OtpExpiresUtc = null;
        user.OtpPurpose = null;
        user.OtpFailedAttempts = 0;
        if (authenticated is not null)
            user.PhoneVerified = true;
        await _db.SaveChangesAsync(ct);
        return user;
    }

    /// <summary>Only the verified external middleware ticket may supply provider and subject.</summary>
    public async Task<AppUser> AuthenticateExternalAsync(
        string provider, string subject, string verifiedEmail, bool allowProvisioning = false,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(subject) ||
            provider.Length > 100 || subject.Length > 2048)
            return null;

        var identityKey = provider + ":" + Hash(subject);
        var matches = await _db.AppUsers.Where(x => x.ExternalSubject == identityKey)
            .Take(2).ToArrayAsync(ct);
        if (matches.Length > 1)
            return null;
        if (matches.Length == 1)
            return matches[0].Active ? matches[0] : null;

        // A disabled or approval-based registration policy must not be
        // bypassed by the external-login callback.
        if (!allowProvisioning)
            return null;

        // No automatic email/phone identity linkage.
        if (string.IsNullOrWhiteSpace(verifiedEmail) || verifiedEmail.Length > 500 ||
            !verifiedEmail.Contains('@'))
            return null;
        var address = Normalize(verifiedEmail);
        if (await _db.AppUsers.AnyAsync(x => x.Email.ToUpper() == address, ct))
            return null;

        var created = new AppUser
        {
            UserGuid = Guid.NewGuid(),
            SecurityStamp = Guid.NewGuid().ToString("N"),
            ExternalSubject = identityKey,
            Username = "external-" + Guid.NewGuid().ToString("N"),
            Email = verifiedEmail.Trim(),
            EmailVerified = true,
            Active = true,
            CreatedOnUtc = DateTime.UtcNow
        };
        _db.AppUsers.Add(created);
        await _db.SaveChangesAsync(ct);
        return created;
    }

    private static void RevokeSessions(AppUser user)
    {
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        user.FailedLoginAttempts = 0;
        user.LockoutEndUtc = null;
        user.OtpHash = null;
        user.OtpPurpose = null;
        user.OtpExpiresUtc = null;
    }

    private static string Hash(string secret) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));

    private static bool SecureHashEquals(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return false;
        var a = Encoding.ASCII.GetBytes(left);
        var b = Encoding.ASCII.GetBytes(right);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }
}
