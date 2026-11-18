using Microsoft.AspNetCore.DataProtection;

namespace Smartstore.MultiSms.Services;

/// <summary>
/// Protects credentials at rest. All application nodes must share persistent Data Protection keys.
/// </summary>
public sealed class SmsCredentialProtection
{
    private const string Prefix = "dp:v1:";
    private readonly IDataProtector _protector;

    public SmsCredentialProtection(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("Smartstore.MultiSms.Credentials.v1");
    }

    public string Protect(string plaintext)
        => string.IsNullOrWhiteSpace(plaintext) ? null : Prefix + _protector.Protect(plaintext);

    public string Unprotect(string ciphertext)
    {
        if (string.IsNullOrWhiteSpace(ciphertext))
        {
            throw new SmsProviderException("SMS provider credentials are not configured.", safeToFailover: true);
        }

        if (!ciphertext.StartsWith(Prefix, StringComparison.Ordinal))
        {
            throw new SmsProviderException("SMS provider credentials must be encrypted.", safeToFailover: true);
        }

        try
        {
            return _protector.Unprotect(ciphertext[Prefix.Length..]);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            throw new SmsProviderException("SMS provider credentials cannot be decrypted.", safeToFailover: true);
        }
    }
}
