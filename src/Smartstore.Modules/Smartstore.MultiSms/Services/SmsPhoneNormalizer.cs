namespace Smartstore.MultiSms.Services;

/// <summary>
/// Enforces one canonical E.164 representation for Iranian mobile numbers.
/// </summary>
public static class SmsPhoneNormalizer
{
    public static string Normalize(string value)
    {
        value = value?.Trim();
        // Users on Persian/Arabic keyboards often enter localized digits.
        // Canonical storage and provider traffic always use ASCII digits.
        if (value is not null)
        {
            value = new string(value.Select(c => c switch
            {
                >= '\u06F0' and <= '\u06F9' => (char)('0' + c - '\u06F0'),
                >= '\u0660' and <= '\u0669' => (char)('0' + c - '\u0660'),
                _ => c
            }).ToArray());
        }
        if (value is null)
        {
            return null;
        }

        if (value.Length == 11 && value.StartsWith("09", StringComparison.Ordinal) &&
            value.All(char.IsAsciiDigit))
        {
            return "+98" + value[1..];
        }

        if (value.Length == 12 && value.StartsWith("989", StringComparison.Ordinal) &&
            value.All(char.IsAsciiDigit))
        {
            return "+" + value;
        }

        if (value.Length == 13 && value.StartsWith("+989", StringComparison.Ordinal) &&
            value.AsSpan(1).ToArray().All(char.IsAsciiDigit))
        {
            return value;
        }

        return null;
    }

    public static string ToProviderPhone(string canonicalPhone)
        => canonicalPhone is { Length: 13 } && canonicalPhone.StartsWith("+989", StringComparison.Ordinal)
            ? "0" + canonicalPhone[3..]
            : canonicalPhone;
}
