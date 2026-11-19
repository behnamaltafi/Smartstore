using Microsoft.Extensions.Hosting;

namespace Smartstore.MultiSms.Services;

/// <summary>
/// Ephemeral, bounded, single-process inbox. No file/SQL/log persistence.
/// Can only be populated and read in ASPNETCORE_ENVIRONMENT=Development.
/// </summary>
public sealed class DevelopmentSmsInbox
{
    private readonly IHostEnvironment _environment;
    private readonly object _sync = new();
    private readonly List<DevelopmentSmsEnvelope> _items = new();

    public DevelopmentSmsInbox(IHostEnvironment environment)
    {
        _environment = environment;
    }

    public bool IsAvailable => _environment.IsDevelopment();

    public string Receive(string phone, string message)
    {
        if (!IsAvailable)
        {
            throw new InvalidOperationException("Development SMS inbox is unavailable outside Development.");
        }

        lock (_sync)
        {
            Prune();
            var id = Guid.NewGuid().ToString("N");
            _items.Insert(0, new DevelopmentSmsEnvelope(id, phone, message, DateTime.UtcNow));
            if (_items.Count > 100)
            {
                _items.RemoveRange(100, _items.Count - 100);
            }
            return id;
        }
    }

    public IReadOnlyList<DevelopmentSmsEnvelope> Snapshot()
    {
        if (!IsAvailable)
        {
            return Array.Empty<DevelopmentSmsEnvelope>();
        }

        lock (_sync)
        {
            Prune();
            return _items.ToArray();
        }
    }

    private void Prune()
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-5);
        _items.RemoveAll(x => x.CreatedOnUtc < cutoff);
    }
}

public sealed record DevelopmentSmsEnvelope(string Id, string Phone, string Message, DateTime CreatedOnUtc);
