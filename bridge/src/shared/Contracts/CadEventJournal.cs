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
    private readonly Dictionary<string, long> _visual = new(StringComparer.Ordinal);
    // Recent visual revisions per document, so undoing a change can also put the visual token back.
    private readonly Dictionary<string, List<long>> _visualHistory = new(StringComparer.Ordinal);
    private readonly HashSet<string> _visualHistoryTruncated = new(StringComparer.Ordinal);
    private const int VisualHistoryLength = 64;
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
    /// The visual revision goes back to the last geometric change at or before the restored point:
    /// otherwise every preview made an XR client download unchanged geometry again (seen live in the
    /// viewer). When that point has left the bounded history the visual token stays where it is,
    /// which costs a refetch but never hides a change.
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
            if (_visualHistory.TryGetValue(documentId, out var history))
            {
                history.RemoveAll(sequence => sequence > target);
                if (history.Count > 0) _visual[documentId] = history[history.Count - 1];
                else if (!_visualHistoryTruncated.Contains(documentId)) _visual.Remove(documentId);
            }
            return true;
        }
    }

    /// <summary>
    /// Token that advances only when a document's geometry or structure may have changed
    /// (<c>document_changed</c>), never on save, activation or selection. An XR client compares it to
    /// decide whether to fetch meshes again; <see cref="Revision"/> is the stricter plan token.
    /// </summary>
    public string VisualRevision(string documentId)
    {
        lock (_gate) return Epoch + ":v" + (_visual.TryGetValue(documentId, out var revision) ? revision : 0);
    }

    /// <summary>Event types that describe view state only; they never advance a document revision.</summary>
    public static bool IsViewEvent(string type) => type == "selection_changed" || type == "camera_changed";

    public void Append(string type, string? documentId) => Append(type, documentId, null);

    /// <summary>
    /// <c>data</c> flag of a <c>document_changed</c> that Inventor reported as leaving geometry and
    /// structure alone (a save, an iProperty edit): the revision advances, the visual revision does not.
    /// </summary>
    public const string GeometryFlag = "geometry";

    /// <summary>
    /// Record an event. Document events advance the document's revision (plans made before it are
    /// stale); <c>document_changed</c> also advances its visual revision unless its data carries
    /// <c>geometry: false</c>. View events (selection, camera) are journalled for subscribers but
    /// advance neither: moving the camera must never invalidate somebody's change plan.
    /// </summary>
    public void Append(string type, string? documentId, JObject? data)
    {
        lock (_gate)
        {
            long sequence = ++_sequence;
            if (documentId != null && !IsViewEvent(type))
            {
                _revisions[documentId] = sequence;
                bool nonGeometric = data?[GeometryFlag]?.Type == JTokenType.Boolean && !(bool)data[GeometryFlag]!;
                if (type == "document_changed" && !nonGeometric)
                {
                    _visual[documentId] = sequence;
                    if (!_visualHistory.TryGetValue(documentId, out var history)) _visualHistory[documentId] = history = new List<long>();
                    history.Add(sequence);
                    if (history.Count > VisualHistoryLength)
                    {
                        history.RemoveAt(0);
                        _visualHistoryTruncated.Add(documentId);
                    }
                }
            }
            var entry = new JObject { ["sequence"] = sequence, ["type"] = type,
                ["document_id"] = documentId, ["utc"] = DateTimeOffset.UtcNow.ToString("O") };
            if (data != null) entry["data"] = data;
            _events.Enqueue(entry);
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
