using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Server.Imports;

/// <summary>Explicit application limits. Formats are declarations, not verified native capabilities.</summary>
public sealed class ImportResourceStorePolicy
{
    private readonly HashSet<string> _formats;
    public ImportResourceStorePolicy(long maxBytesPerResource, long maxTotalBytes, int maxRecords,
        int maxMetadataBytes, int maxFormatLength, TimeSpan ttl, IEnumerable<string> allowedFormats)
    {
        if (maxBytesPerResource < 1 || maxTotalBytes < maxBytesPerResource || maxRecords < 1
            || maxMetadataBytes < 2 || maxFormatLength < 1 || ttl <= TimeSpan.Zero)
            throw new ArgumentException("Import store limits and TTL must be explicit and positive.");
        MaxBytesPerResource = maxBytesPerResource; MaxTotalBytes = maxTotalBytes; MaxRecords = maxRecords;
        MaxMetadataBytes = maxMetadataBytes; MaxFormatLength = maxFormatLength; Ttl = ttl;
        if (allowedFormats == null) throw new ArgumentNullException(nameof(allowedFormats));
        _formats = new HashSet<string>(StringComparer.Ordinal);
        foreach (string format in allowedFormats)
            if (!Token(format, maxFormatLength) || !_formats.Add(format))
                throw new ArgumentException("Declare distinct opaque format tokens.", nameof(allowedFormats));
        if (_formats.Count == 0) throw new ArgumentException("Declare at least one format.", nameof(allowedFormats));
    }
    public long MaxBytesPerResource { get; }
    public long MaxTotalBytes { get; }
    public int MaxRecords { get; }
    public int MaxMetadataBytes { get; }
    public int MaxFormatLength { get; }
    public TimeSpan Ttl { get; }
    internal bool Allows(string format) => format != null && _formats.Contains(format);
    /// <summary>Read-only policy query for host adapters; does not expose the mutable allow-list.</summary>
    public bool IsFormatAllowed(string format) => Allows(format);
    private static bool Token(string value, int maximum) => !string.IsNullOrEmpty(value) && value.Length <= maximum
        && value.All(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_' or '-');
}

/// <summary>
/// Detached owner-specific metadata normalized through JSON serialization/parsing at registration.
/// CLR values such as byte arrays become JSON values (base64 strings), never shared mutable references.
/// </summary>
public sealed class ImportResourceRecord
{
    private readonly JObject _metadata;
    internal ImportResourceRecord(string id, string sha256, string format, long size, DateTimeOffset expires, JObject metadata)
    { Id = id; Sha256 = sha256; Format = format; Size = size; ExpiresUtc = expires; _metadata = (JObject)metadata.DeepClone(); }
    public string Id { get; }
    public string Sha256 { get; }
    public string Format { get; }
    public long Size { get; }
    public DateTimeOffset ExpiresUtc { get; }
    public JObject Metadata => (JObject)_metadata.DeepClone();
}

/// <summary>
/// In-memory import catalog. Caller supplies the authenticated identity; this class does not authenticate it.
/// Leases pin immutable bytes, not a CAD document or a persistent linked-file lifetime. Nothing survives restart.
/// </summary>
public sealed class ImportResourceStore
{
    internal sealed class Blob
    {
        internal readonly string Id, Sha256;
        internal readonly byte[] Bytes;
        internal long Access;
        internal int Pins;
        internal bool Invalidated;
        internal Blob(string id, string sha, byte[] bytes) { Id = id; Sha256 = sha; Bytes = bytes; }
    }
    private sealed class OwnerRecord
    {
        internal readonly Blob Blob;
        internal readonly string Format;
        internal readonly JObject Metadata;
        internal readonly DateTimeOffset Expires;
        internal OwnerRecord(Blob blob, string format, JObject metadata, DateTimeOffset expires)
        { Blob = blob; Format = format; Metadata = metadata; Expires = expires; }
        internal ImportResourceRecord Snapshot() => new(Blob.Id, Blob.Sha256, Format, Blob.Bytes.LongLength, Expires, Metadata);
    }
    private static readonly Regex IdPattern = new("\\Aa_[0-9a-f]{64}\\z", RegexOptions.CultureInvariant);
    private readonly object _gate = new();
    private readonly Dictionary<string, Blob> _blobs = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Owner, string Id), OwnerRecord> _records = new();
    private readonly ImportResourceStorePolicy _policy;
    private readonly Func<DateTimeOffset> _clock;
    private long _stored, _access;

    public ImportResourceStore(ImportResourceStorePolicy policy, Func<DateTimeOffset>? clock = null)
    { _policy = policy ?? throw new ArgumentNullException(nameof(policy)); _clock = clock ?? (() => DateTimeOffset.UtcNow); }
    public static bool IsValidId(string? id) => id != null && IdPattern.IsMatch(id);
    public long StoredBytes { get { lock (_gate) { Sweep(_clock()); return _stored; } } }

    public ImportResourceRecord Put(string owner, byte[] bytes, string format, JObject? metadata = null)
    {
        Owner(owner);
        if (bytes == null || bytes.Length == 0 || bytes.LongLength > _policy.MaxBytesPerResource)
            throw new ArgumentException("Import content is empty or exceeds its byte policy.", nameof(bytes));
        if (!_policy.Allows(format)) throw new ArgumentException("Import format is not allowed by policy.", nameof(format));
        // DeepClone alone retains CLR byte[] references held by JValue. Round-trip through JSON
        // before storing to freeze the wire representation and discard all caller-owned CLR values.
        string metadataJson = (metadata ?? new JObject()).ToString(Formatting.None);
        if (Encoding.UTF8.GetByteCount(metadataJson) > _policy.MaxMetadataBytes)
            throw new ArgumentException("Import metadata exceeds policy.", nameof(metadata));
        var frozen = JObject.Parse(metadataJson);
        byte[] content = (byte[])bytes.Clone();
        string sha = Hash(content), id = "a_" + sha;
        lock (_gate)
        {
            DateTimeOffset now = _clock(), expires = now + _policy.Ttl;
            Sweep(now);
            _blobs.TryGetValue(id, out var blob);
            if (blob != null && !Healthy(blob))
            {
                Invalidate(blob);
                if (blob.Pins > 0) throw new InvalidOperationException("Import content is unavailable while a damaged lease remains active.");
                blob = null;
            }
            long incoming = blob == null ? content.LongLength : 0;
            int addedRecord = _records.ContainsKey((owner, id)) ? 0 : 1;
            MakeRoom(id, incoming, addedRecord);
            if (blob == null)
            {
                blob = new Blob(id, sha, content); _blobs.Add(id, blob); _stored += content.LongLength;
            }
            blob.Access = ++_access;
            var record = new OwnerRecord(blob, format, frozen, expires);
            _records[(owner, id)] = record;
            return record.Snapshot();
        }
    }

    public ImportResourceRecord? Describe(string owner, string? id)
    {
        Owner(owner); if (!IsValidId(id)) return null;
        lock (_gate)
        {
            Sweep(_clock());
            if (!_records.TryGetValue((owner, id!), out var record)) return null;
            if (!Healthy(record.Blob)) { Invalidate(record.Blob); return null; }
            record.Blob.Access = ++_access;
            return record.Snapshot();
        }
    }

    public IReadOnlyList<ImportResourceRecord> List(string owner)
    {
        Owner(owner);
        lock (_gate)
        {
            Sweep(_clock());
            foreach (var blob in _blobs.Values.ToArray()) if (!Healthy(blob)) Invalidate(blob);
            return Array.AsReadOnly(_records.Where(pair => pair.Key.Owner == owner)
                .OrderBy(pair => pair.Key.Id, StringComparer.Ordinal).Select(pair => pair.Value.Snapshot()).ToArray());
        }
    }

    /// <summary>Atomically pins all listed resources or returns null; foreign and missing IDs are indistinguishable.</summary>
    public Lease? Acquire(string owner, IEnumerable<string> ids)
    {
        Owner(owner); if (ids == null) throw new ArgumentNullException(nameof(ids));
        var unique = new HashSet<string>(StringComparer.Ordinal);
        var requested = new List<string>();
        foreach (string id in ids)
        {
            if (!IsValidId(id) || !unique.Add(id) || requested.Count >= _policy.MaxRecords)
                throw new ArgumentException("Lease requires distinct valid IDs within the resource policy.", nameof(ids));
            requested.Add(id);
        }
        if (requested.Count == 0) throw new ArgumentException("Lease needs at least one resource.", nameof(ids));
        lock (_gate)
        {
            Sweep(_clock());
            var records = new List<OwnerRecord>();
            foreach (string id in requested)
            {
                if (!_records.TryGetValue((owner, id), out var record)) return null;
                if (!Healthy(record.Blob)) { Invalidate(record.Blob); return null; }
                records.Add(record);
            }
            var blobs = records.Select(record => record.Blob).ToArray();
            if (blobs.Any(blob => blob.Pins == int.MaxValue)) throw new InvalidOperationException("Import lease count exceeds supported capacity.");
            foreach (var blob in blobs) { blob.Pins++; blob.Access = ++_access; }
            return new Lease(this, blobs, records.Select(record => record.Snapshot()).ToArray());
        }
    }

    public bool TryRead(string owner, string? id, out byte[]? bytes)
    {
        bytes = null; Owner(owner); if (!IsValidId(id)) return false;
        using var lease = Acquire(owner, new[] { id! });
        if (lease == null) return false;
        bytes = lease.Read(id!); return true;
    }

    private static void Owner(string owner)
    { if (string.IsNullOrWhiteSpace(owner)) throw new ArgumentException("Pass the authenticated import owner.", nameof(owner)); }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static bool Healthy(Blob blob) => !blob.Invalidated && Hash(blob.Bytes) == blob.Sha256;
    private void Invalidate(Blob blob)
    {
        blob.Invalidated = true;
        foreach (var key in _records.Where(pair => ReferenceEquals(pair.Value.Blob, blob)).Select(pair => pair.Key).ToArray()) _records.Remove(key);
        if (blob.Pins == 0) Remove(blob);
    }
    private void Sweep(DateTimeOffset now)
    {
        foreach (var key in _records.Where(pair => pair.Value.Expires <= now).Select(pair => pair.Key).ToArray()) _records.Remove(key);
        foreach (var blob in _blobs.Values.ToArray())
            if (blob.Pins == 0 && !_records.Values.Any(record => ReferenceEquals(record.Blob, blob))) Remove(blob);
    }
    private void MakeRoom(string preservedId, long incoming, int addedRecord)
    {
        long projectedBytes = _stored;
        long projectedRecords = (long)_records.Count + addedRecord;
        var victims = new List<Blob>();
        foreach (var blob in _blobs.Values.Where(blob => blob.Id != preservedId && blob.Pins == 0).OrderBy(blob => blob.Access))
        {
            if (incoming <= _policy.MaxTotalBytes - projectedBytes && projectedRecords <= _policy.MaxRecords) break;
            victims.Add(blob); projectedBytes -= blob.Bytes.LongLength;
            projectedRecords -= _records.Values.Count(record => ReferenceEquals(record.Blob, blob));
        }
        if (incoming > _policy.MaxTotalBytes - projectedBytes || projectedRecords > _policy.MaxRecords)
            throw new InvalidOperationException("Import capacity is exhausted by resources that cannot be evicted.");
        // Decide the entire eviction before changing live records: refused puts do not partially evict a catalog.
        foreach (var blob in victims) Remove(blob);
    }
    private void Remove(Blob blob)
    {
        if (blob.Pins != 0) throw new InvalidOperationException("Cannot evict leased import bytes.");
        foreach (var key in _records.Where(pair => ReferenceEquals(pair.Value.Blob, blob)).Select(pair => pair.Key).ToArray()) _records.Remove(key);
        if (_blobs.Remove(blob.Id)) _stored -= blob.Bytes.LongLength;
    }

    public sealed class Lease : IDisposable
    {
        private readonly ImportResourceStore _store;
        private readonly Blob[] _blobs;
        private bool _disposed;
        internal Lease(ImportResourceStore store, Blob[] blobs, ImportResourceRecord[] records)
        { _store = store; _blobs = blobs; Records = Array.AsReadOnly(records); }
        public IReadOnlyList<ImportResourceRecord> Records { get; }
        public byte[] Read(string id)
        {
            lock (_store._gate)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(Lease));
                var blob = _blobs.FirstOrDefault(candidate => candidate.Id == id);
                if (blob == null) throw new ArgumentException("Resource is outside this lease.", nameof(id));
                if (!Healthy(blob)) { _store.Invalidate(blob); throw new InvalidOperationException("Leased import content failed its integrity check."); }
                return (byte[])blob.Bytes.Clone();
            }
        }
        public void Dispose()
        {
            lock (_store._gate)
            {
                if (_disposed) return;
                _disposed = true;
                foreach (var blob in _blobs) blob.Pins--;
                Array.Clear(_blobs, 0, _blobs.Length);
                _store.Sweep(_store._clock());
            }
        }
    }
}
