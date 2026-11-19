using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Smartstore.Core.Data;
using Smartstore.Core.Messaging;
using Smartstore.MultiSms.Configuration;
using Smartstore.MultiSms.Domain;

namespace Smartstore.MultiSms.Services;

/// <summary>
/// Best-effort operational history around the transport. A database logging failure
/// must never trigger a second SMS or conceal a successful provider acknowledgement.
/// No OTP body is copied to SQL, including on failed delivery.
/// </summary>
public sealed class SmsHistoryStore : ISmsHistoryWriter
{
    private readonly SmartDbContext _db;
    private readonly ILogger<SmsHistoryStore> _logger;
    private static long _lastCleanupUtcTicks;

    public SmsHistoryStore(SmartDbContext db, ILogger<SmsHistoryStore> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// A pure, testable privacy boundary: OTP and manual tests are never
    /// copied to long-lived history, even with ordinary message storage on.
    /// </summary>
    public static SmsMessage CreateMessageMetadata(SmsSendRequest request, SmsGatewaySettings settings)
        => new()
        {
            Phone = request.PhoneNumber,
            Kind = request.Kind.ToString(),
            Status = "Pending",
            Body = request.Kind == SmsMessageKind.General && settings.StoreNonOtpMessageBody
                ? request.Text : null,
            CreatedOnUtc = DateTime.UtcNow
        };

    public async Task<SmsMessage> BeginAsync(
        SmsSendRequest request, SmsGatewaySettings settings, CancellationToken ct)
    {
        if (!settings.HistoryEnabled) return null;

        var message = CreateMessageMetadata(request, settings);

        try
        {
            _db.Set<SmsMessage>().Add(message);
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            try { _db.Entry(message).State = EntityState.Detached; }
            catch (InvalidOperationException) { }
            _logger.LogWarning("SMS history could not be started; provider send will continue.");
            return null;
        }

        await PruneIfDueAsync(settings.HistoryRetentionDays);
        return message;
    }

    public async Task RecordAttemptAsync(
        SmsMessage message, string provider, string status, string messageId,
        string errorCategory, DateTime startedOnUtc, int durationMs, CancellationToken ct)
    {
        if (message is null) return;

        var attempt = new SmsSendAttempt
        {
            SmsMessageId = message.Id,
            Provider = provider,
            Status = status,
            ProviderMessageId = messageId,
            ErrorCategory = errorCategory,
            StartedOnUtc = startedOnUtc,
            DurationMs = Math.Max(0, durationMs)
        };

        var previous = (message.AttemptCount, message.Provider,
            message.ProviderMessageId, message.Status, message.FinishedOnUtc);
        message.AttemptCount++;
        message.Provider = provider;
        message.ProviderMessageId = messageId;
        message.Status = status;
        message.FinishedOnUtc = DateTime.UtcNow;

        try
        {
            _db.Set<SmsSendAttempt>().Add(attempt);
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception)
        {
            // A failed attempt write must not leak modified history entities
            // into the next provider attempt's SaveChanges call.
            try
            {
                _db.Entry(attempt).State = EntityState.Detached;
                (message.AttemptCount, message.Provider, message.ProviderMessageId,
                    message.Status, message.FinishedOnUtc) = previous;
                _db.Entry(message).State = EntityState.Unchanged;
            }
            catch (InvalidOperationException) { }
            _logger.LogWarning("SMS attempt history could not be saved; provider result is unchanged.");
        }
    }

    /// <summary>
    /// Request-triggered, bounded cleanup: no separate scheduler or dependency.
    /// Runs at most once per six hours per process, deleting up to 250 old rows.
    /// </summary>
    private async Task PruneIfDueAsync(int retentionDays)
    {
        var ticks = DateTime.UtcNow.Ticks;
        var previous = Interlocked.Read(ref _lastCleanupUtcTicks);
        if (ticks - previous < TimeSpan.FromHours(6).Ticks ||
            Interlocked.CompareExchange(ref _lastCleanupUtcTicks, ticks, previous) != previous)
        {
            return;
        }

        try
        {
            var cutoff = DateTime.UtcNow.AddDays(-Math.Clamp(retentionDays, 1, 3650));
            var expiredIds = await _db.Set<SmsMessage>().AsNoTracking()
                .Where(x => x.CreatedOnUtc < cutoff)
                .OrderBy(x => x.CreatedOnUtc)
                .Take(250).Select(x => x.Id).ToArrayAsync();
            if (expiredIds.Length == 0) return;

            await _db.Set<SmsSendAttempt>()
                .Where(x => expiredIds.Contains(x.SmsMessageId))
                .ExecuteDeleteAsync();
            await _db.Set<SmsMessage>()
                .Where(x => expiredIds.Contains(x.Id))
                .ExecuteDeleteAsync();
        }
        catch (Exception)
        {
            _logger.LogWarning("Automatic SMS history retention cleanup failed.");
        }
    }

    public async Task<SmsHistoryModelData> SearchAsync(
        string phone, string kind, string status, int page, int pageSize, CancellationToken ct)
    {
        var q = _db.Set<SmsMessage>().AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(phone))
        {
            // OTP calls store canonical +98; manual test calls may store local 09.
            // An exact-match search covers both without scanning message bodies.
            var canonical = SmsPhoneNormalizer.Normalize(phone) ?? phone;
            var local = SmsPhoneNormalizer.ToProviderPhone(canonical);
            q = q.Where(x => x.Phone == canonical || x.Phone == local);
        }
        if (!string.IsNullOrWhiteSpace(kind)) q = q.Where(x => x.Kind == kind);
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(x => x.Status == status);

        var count = await q.CountAsync(ct);
        var items = await q.OrderByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new SmsHistoryModelData(items, count);
    }

    public Task<SmsMessage> FindAsync(int id, CancellationToken ct)
        => _db.Set<SmsMessage>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<SmsSendAttempt>> AttemptsAsync(int messageId, CancellationToken ct)
        => await _db.Set<SmsSendAttempt>().AsNoTracking()
            .Where(x => x.SmsMessageId == messageId).OrderBy(x => x.Id).ToListAsync(ct);
}

public sealed record SmsHistoryModelData(IReadOnlyList<SmsMessage> Items, int Total);
