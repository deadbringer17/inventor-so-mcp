using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Contracts;

/// <summary>Bounded per-add-in event journal and revision tokens. Thread-safe for readers.</summary>
public sealed class CadEventJournal
{
    private readonly object _gate = new();
    private readonly Queue<JObject> _events = new();
    private readonly Dictionary<string, long> _revisions = new(StringComparer.Ordinal);
    private readonly int _capacity;
    private long _sequence;
    public string Epoch { get; } = Guid.NewGuid().ToString("N");

    public CadEventJournal(int capacity = 512)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
    }

    public string Revision(string documentId)
    {
        lock (_gate) return Epoch + ":" + (_revisions.TryGetValue(documentId, out var revision) ? revision : 0);
    }

    public void Append(string type, string? documentId)
    {
        lock (_gate)
        {
            long sequence = ++_sequence;
            if (documentId != null) _revisions[documentId] = sequence;
            _events.Enqueue(new JObject { ["sequence"] = sequence, ["type"] = type,
                ["document_id"] = documentId, ["utc"] = DateTimeOffset.UtcNow.ToString("O") });
            while (_events.Count > _capacity) _events.Dequeue();
        }
    }

    public JObject Read(long after = 0, string? epoch = null)
    {
        if (after < 0) throw new ArgumentOutOfRangeException(nameof(after));
        lock (_gate)
        {
            long oldest = _events.Count > 0 ? (long)_events.Peek()["sequence"]! : _sequence + 1;
            bool reset = (epoch != null && epoch != Epoch) || after < oldest - 1 || after > _sequence;
            return new JObject { ["epoch"] = Epoch, ["cursor"] = _sequence, ["resync_required"] = reset,
                ["events"] = new JArray(_events.Where(e => reset || (long)e["sequence"]! > after).Select(e => e.DeepClone())) };
        }
    }
}
