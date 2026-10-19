using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Bimwright.Ipt.Shared.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Server.Planning;

/// <summary>
/// Change plans (plan §18): the operations an agent proposed, the revision they were previewed
/// against and a hash of their content. A plan is committed exactly as previewed or not at all:
/// commit re-checks owner, expiry, revision and hash. Plans live in memory only.
/// </summary>
public sealed class ChangePlanStore : IDisposable
{
    public sealed class Plan : IDisposable
    {
        private IDisposable? _attachment;

        // Only Take returns a resource-bearing plan; metadata snapshots cannot read bytes.
        internal T? Attachment<T>() where T : class, IDisposable => Volatile.Read(ref _attachment) as T;

        // Only the detached Plan returned by Take owns resources. Metadata views never do.
        public void Dispose()
        {
            var attachment = Interlocked.Exchange(ref _attachment, null);
            if (attachment != null) DisposeAll(new List<IDisposable> { attachment });
        }

        internal Plan Snapshot(IDisposable? attachment = null) => new()
        {
            Id = Id, Owner = Owner, DocumentId = DocumentId, Revision = Revision,
            Operations = (JArray)Operations.DeepClone(), Validate = Validate?.DeepClone(),
            Intent = Intent, Hash = Hash, CreatedUtc = CreatedUtc, ExpiresUtc = ExpiresUtc,
            Preview = (JObject)Preview.DeepClone(), Impact = (JObject)Impact.DeepClone(),
            _attachment = attachment,
        };
        public string Id { get; init; } = "";
        public string Owner { get; init; } = "";
        public string DocumentId { get; init; } = "";
        public string Revision { get; init; } = "";
        public JArray Operations { get; init; } = new();
        public JToken? Validate { get; init; }
        public string? Intent { get; init; }
        public string Hash { get; init; } = "";
        public DateTimeOffset CreatedUtc { get; init; }
        public DateTimeOffset ExpiresUtc { get; init; }
        public JObject Preview { get; set; } = new();
        public JObject Impact { get; set; } = new();

        public JObject ToJson() => new()
        {
            ["plan_id"] = Id,
            ["document_id"] = DocumentId,
            ["document_revision"] = Revision,
            ["intent"] = Intent,
            ["operations_sha256"] = Hash,
            ["operations"] = Operations.DeepClone(),
            ["validate"] = Validate?.DeepClone(),
            ["created_utc"] = CreatedUtc.ToString("O"),
            ["expires_utc"] = ExpiresUtc.ToString("O"),
            ["preview"] = Preview.DeepClone(),
            ["impact"] = Impact.DeepClone(),
        };
    }

    public const int MaxPlansPerOwner = 64;
    private readonly object _gate = new();
    private sealed record Entry(Plan Plan, IDisposable? Attachment);
    private readonly Dictionary<string, Entry> _plans = new(StringComparer.Ordinal);
    private bool _disposed;
    private readonly TimeSpan _ttl;
    private readonly Func<DateTimeOffset> _clock;

    public ChangePlanStore() : this(TimeSpan.FromMinutes(15)) { }

    public ChangePlanStore(TimeSpan ttl, Func<DateTimeOffset>? clock = null)
    {
        _ttl = ttl;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>SHA-256 of the canonical (sorted-key, compact) JSON of the operations and checks.</summary>
    public static string HashOf(JArray operations, JToken? validate)
    {
        var canonical = new JObject { ["operations"] = Canonical(operations), ["validate"] = validate == null ? JValue.CreateNull() : Canonical(validate) };
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString(Formatting.None)))).ToLowerInvariant();
    }

    private static JToken Canonical(JToken token) => token switch
    {
        JObject o => new JObject(o.Properties().OrderBy(p => p.Name, StringComparer.Ordinal).Select(p => new JProperty(p.Name, Canonical(p.Value)))),
        JArray a => new JArray(a.Select(Canonical)),
        _ => token.DeepClone(),
    };

    /// <summary>Transfers attachment ownership on entry, including when creation fails. Attachments never enter JSON.</summary>
    public Plan Create(string owner, string documentId, string revision, JArray operations, JToken? validate, string? intent,
        IDisposable? attachment = null)
    {
        var released = new List<IDisposable>();
        bool transferred = false;
        try
        {
            var now = _clock();
            var plan = new Plan
            {
                Id = "plan_" + Guid.NewGuid().ToString("N"), Owner = owner,
                DocumentId = documentId, Revision = revision,
                Operations = (JArray)operations.DeepClone(), Validate = validate?.DeepClone(),
                Intent = intent, Hash = HashOf(operations, validate),
                CreatedUtc = now, ExpiresUtc = now + _ttl,
            };
            lock (_gate)
            {
                ThrowIfDisposed();
                Sweep(now, released);
                var mine = _plans.Values.Where(e => e.Plan.Owner == owner).OrderBy(e => e.Plan.CreatedUtc).ToList();
                while (mine.Count >= MaxPlansPerOwner)
                {
                    Remove(mine[0].Plan.Id, released);
                    mine.RemoveAt(0);
                }
                _plans.Add(plan.Id, new Entry(plan, attachment));
                transferred = true;
            }
            return plan;
        }
        finally
        {
            if (!transferred && attachment != null) released.Add(attachment);
            DisposeAll(released);
        }
    }

    public void Discard(string id)
    {
        var released = new List<IDisposable>();
        lock (_gate) Remove(id, released);
        DisposeAll(released);
    }

    /// <summary>
    /// Consumes a plan for commit. The caller owns the returned plan and must dispose it in finally,
    /// including failed or cancelled commits. Foreign attempts do not consume another owner's plan.
    /// </summary>
    public Plan Take(string planId, string owner, string documentId, string expectedRevision)
    {
        var released = new List<IDisposable>();
        try
        {
            lock (_gate)
            {
                ThrowIfDisposed();
                Sweep(_clock(), released);
                if (string.IsNullOrWhiteSpace(planId) || !_plans.TryGetValue(planId, out var entry) || entry.Plan.Owner != owner)
                    throw new PlanException(InventorErrorCodes.PLAN_NOT_FOUND, "Unknown, expired or foreign plan id; plan the change again.");
                _plans.Remove(planId);
                bool transferred = false;
                try
                {
                    var plan = entry.Plan;
                    if (plan.DocumentId != documentId)
                        throw new PlanException(InventorErrorCodes.PLAN_MISMATCH, "The plan was made for " + plan.DocumentId + ", not " + documentId + ".");
                    if (plan.Revision != expectedRevision)
                        throw new PlanException(InventorErrorCodes.PLAN_MISMATCH, "The plan was previewed at revision " + plan.Revision +
                            "; expected_revision " + expectedRevision + " differs. Plan the change again.");
                    if (HashOf(plan.Operations, plan.Validate) != plan.Hash)
                        throw new PlanException(InventorErrorCodes.PLAN_MISMATCH, "Plan content no longer matches its hash.");
                    var owned = plan.Snapshot(entry.Attachment);
                    transferred = true;
                    return owned;
                }
                finally
                {
                    if (!transferred && entry.Attachment != null) released.Add(entry.Attachment);
                }
            }
        }
        finally { DisposeAll(released); }
    }

    /// <summary>Detached metadata snapshots, without resource ownership.</summary>
    public IReadOnlyList<Plan> List(string owner)
    {
        var released = new List<IDisposable>();
        try
        {
            lock (_gate)
            {
                ThrowIfDisposed();
                Sweep(_clock(), released);
                return _plans.Values.Where(e => e.Plan.Owner == owner).OrderByDescending(e => e.Plan.CreatedUtc)
                    .Select(e => e.Plan.Snapshot()).ToArray();
            }
        }
        finally { DisposeAll(released); }
    }

    public void Dispose()
    {
        var released = new List<IDisposable>();
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var id in _plans.Keys.ToArray()) Remove(id, released);
        }
        DisposeAll(released);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(ChangePlanStore));
    }

    private void Sweep(DateTimeOffset now, List<IDisposable> released)
    {
        foreach (var id in _plans.Values.Where(e => e.Plan.ExpiresUtc <= now).Select(e => e.Plan.Id).ToArray()) Remove(id, released);
    }

    private void Remove(string id, List<IDisposable> released)
    {
        if (_plans.Remove(id, out var entry) && entry.Attachment != null) released.Add(entry.Attachment);
    }

    private static void DisposeAll(List<IDisposable> released)
    {
        foreach (var attachment in released)
        {
            try { attachment.Dispose(); }
            // Cleanup must release every attachment without hiding a commit/validation failure.
            // Do not log exception text: an attachment may contain private source paths.
            catch (Exception) { Trace.TraceError("Change plan attachment cleanup failed."); }
        }
    }

}

public sealed class PlanException : Exception
{
    public PlanException(string code, string message) : base(message) { Code = code; }
    public string Code { get; }
}
