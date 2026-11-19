namespace Smartstore.MultiSms.Services;

public static class SmsHistoryFormatter
{
    public static string MaskPhone(string phone)
    {
        if (string.IsNullOrEmpty(phone)) return "";
        return phone.Length > 7
            ? phone[..4] + new string('*', phone.Length - 7) + phone[^3..]
            : "***";
    }

    public static string SafeCategory(Exception exception) => exception switch
    {
        OperationCanceledException => "Cancelled",
        TimeoutException => "Timeout",
        HttpRequestException => "Transport",
        SmsProviderException ex when ex.SafeToFailover => "ProviderRejected",
        SmsProviderException => "ProviderUnconfirmed",
        InvalidOperationException => "Configuration",
        _ => "Unknown"
    };

    public static string FailedStatus(Exception exception)
        => exception is SmsProviderException { SafeToFailover: true } ? "Rejected" : "Unknown";
}
