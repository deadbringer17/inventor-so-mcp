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

    /// <summary>
    /// Put one document's revision token back to <paramref name="revision"/> after a change was undone
    /// in full, so an operation that leaves the model untouched also leaves the caller's plan valid.
    /// Refuses a token from another epoch and never moves a revision forward, so it cannot be used to
    /// hide a change that is still on the model. The event journal itself keeps every entry.
    /// </summary>
    public bool TryRestoreRevision(string documentId, string revision)
    {
        if (documentId == null || revision == null) return false;
        int separator = revision.LastIndexOf(':');
        if (separator <= 0 || revision.Substring(0, separator) != Epoch) return false;
        if (!long.TryParse(revision.Substring(separator + 1), out long target) || target < 0) return false;
        lock (_gate)
        {
            long current = _revisions.TryGetValue(documentId, out var value) ? value : 0;
            if (target > current) return false;
            if (target == 0) _revisions.Remove(documentId);
            else _revisions[documentId] = target;
            return true;
        }
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
