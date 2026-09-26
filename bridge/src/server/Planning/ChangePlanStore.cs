using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Bimwright.Ipt.Shared.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Server.Planning;

/// <summary>
/// Change plans (plan §18): the operations an agent proposed, the revision they were previewed
/// against and a hash of their content. A plan is committed exactly as previewed or not at all:
/// commit re-checks owner, expiry, revision and hash. Plans live in memory only.
/// </summary>
public sealed class ChangePlanStore
{
    public sealed class Plan
    {
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
    private readonly Dictionary<string, Plan> _plans = new(StringComparer.Ordinal);
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

    public Plan Create(string owner, string documentId, string revision, JArray operations, JToken? validate, string? intent)
    {
        var now = _clock();
        var plan = new Plan
        {
            Id = "plan_" + Guid.NewGuid().ToString("N"),
            Owner = owner,
            DocumentId = documentId,
            Revision = revision,
            Operations = (JArray)operations.DeepClone(),
            Validate = validate?.DeepClone(),
            Intent = intent,
            Hash = HashOf(operations, validate),
            CreatedUtc = now,
            ExpiresUtc = now + _ttl,
        };
        lock (_gate)
        {
            Sweep(now);
            var mine = _plans.Values.Where(p => p.Owner == owner).OrderBy(p => p.CreatedUtc).ToList();
            while (mine.Count >= MaxPlansPerOwner) { _plans.Remove(mine[0].Id); mine.RemoveAt(0); }
            _plans[plan.Id] = plan;
        }
        return plan;
    }

    public void Discard(string id)
    {
        lock (_gate) _plans.Remove(id);
    }

    /// <summary>
    /// Take a plan for commit. The plan is removed whatever happens next: a failed commit has to be
    /// planned again against the new state rather than retried blindly.
    /// </summary>
    public Plan Take(string planId, string owner, string documentId, string expectedRevision)
    {
        lock (_gate)
        {
            Sweep(_clock());
            if (string.IsNullOrWhiteSpace(planId) || !_plans.TryGetValue(planId, out var plan) || plan.Owner != owner)
                throw new PlanException(InventorErrorCodes.PLAN_NOT_FOUND, "Unknown, expired or foreign plan id; plan the change again.");
            _plans.Remove(planId);
            if (plan.DocumentId != documentId)
                throw new PlanException(InventorErrorCodes.PLAN_MISMATCH, "The plan was made for " + plan.DocumentId + ", not " + documentId + ".");
            if (plan.Revision != expectedRevision)
                throw new PlanException(InventorErrorCodes.PLAN_MISMATCH, "The plan was previewed at revision " + plan.Revision +
                    "; expected_revision " + expectedRevision + " differs. Plan the change again.");
            if (HashOf(plan.Operations, plan.Validate) != plan.Hash)
                throw new PlanException(InventorErrorCodes.PLAN_MISMATCH, "Plan content no longer matches its hash.");
            return plan;
        }
    }

    public IReadOnlyList<Plan> List(string owner)
    {
        lock (_gate)
        {
            Sweep(_clock());
            return _plans.Values.Where(p => p.Owner == owner).OrderByDescending(p => p.CreatedUtc).ToArray();
        }
    }

    private void Sweep(DateTimeOffset now)
    {
        foreach (var id in _plans.Values.Where(p => p.ExpiresUtc <= now).Select(p => p.Id).ToArray()) _plans.Remove(id);
    }
}

public sealed class PlanException : Exception
{
    public PlanException(string code, string message) : base(message) { Code = code; }
    public string Code { get; }
}
