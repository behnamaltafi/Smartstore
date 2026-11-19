using Smartstore.MultiSms.Domain;
using Smartstore.MultiSms.Services;

namespace Smartstore.MultiSms.Models;

public sealed class SmsHistoryModel
{
    public IReadOnlyList<SmsMessage> Messages { get; set; } = Array.Empty<SmsMessage>();
    public int Page { get; set; } = 1;
    public int Total { get; set; }
    public int PageSize { get; set; } = 30;
    public string Phone { get; set; }
    public string Kind { get; set; }
    public string Status { get; set; }

    public int PageCount => Math.Max(1, (int)Math.Ceiling(Total / (double)PageSize));
}

public sealed class SmsHistoryDetailsModel
{
    public SmsMessage Message { get; set; }
    public IReadOnlyList<SmsSendAttempt> Attempts { get; set; } = Array.Empty<SmsSendAttempt>();
}
