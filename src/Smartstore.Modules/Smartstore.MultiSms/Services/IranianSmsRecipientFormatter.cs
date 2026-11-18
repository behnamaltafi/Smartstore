namespace Smartstore.MultiSms.Services;

/// <summary>
/// Converts canonical Iranian E.164 mobile numbers to the domestic mobile
/// representation accepted by Iranian SMS providers. Other numbers are
/// preserved for provider-level validation.
/// </summary>
internal static class IranianSmsRecipientFormatter
{
    public static string Normalize(string phone)
    {
        if (phone is { Length: 13 } && phone.StartsWith("+989", StringComparison.Ordinal) &&
            phone.AsSpan(1).ToArray().All(char.IsAsciiDigit))
        {
            return "0" + phone[3..];
        }

        return phone;
    }
}
