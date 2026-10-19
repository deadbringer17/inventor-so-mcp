using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace Bimwright.Ipt.Shared.Contracts;

/// <summary>Explicit memory and transfer limits; no wire adapter or native capability is implied.</summary>
public sealed class SketchImportStagingPolicy
{
    public SketchImportStagingPolicy(int maxBytesPerTransfer, long maxTotalBytes, int maxTransfers,
        int maxChunkBytes, TimeSpan ttl, int maxIdentityLength)
    {
        if (maxBytesPerTransfer < 1 || maxTotalBytes < maxBytesPerTransfer || maxTransfers < 1
            || maxChunkBytes < 1 || maxChunkBytes > maxBytesPerTransfer || ttl <= TimeSpan.Zero
            || maxIdentityLength < 1)
            throw new ArgumentException("Staging limits and TTL must be explicit and positive.");
        MaxBytesPerTransfer = maxBytesPerTransfer; MaxTotalBytes = maxTotalBytes;
        MaxTransfers = maxTransfers; MaxChunkBytes = maxChunkBytes; Ttl = ttl;
        MaxIdentityLength = maxIdentityLength;
    }
    public int MaxBytesPerTransfer { get; }
    public long MaxTotalBytes { get; }
    public int MaxTransfers { get; }
    public int MaxChunkBytes { get; }
    public TimeSpan Ttl { get; }
    public int MaxIdentityLength { get; }
}

/// <summary>
/// API-agnostic in-memory staging. Adapters must supply authenticated owner and session identities.
/// Reserves the full declared length before accepting bytes, seals by length and SHA256, and never
/// exposes mutable storage. Expiry is fixed at creation and swept lazily or explicitly by the host.
/// Retired records remain charged to both budgets while leased. No files or persistent links exist.
/// </summary>
public sealed class SketchImportStagingStore : IDisposable
{
    private readonly object _gate = new object();
    private readonly SketchImportStagingPolicy _policy;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
    private long _reservedBytes;
    private int _residentTransfers;
    private bool _disposed;

    internal sealed class Entry
    {
        internal Entry(string id, string owner, string session, int length, DateTimeOffset expiry)
        { Id = id; Owner = owner; Session = session; Bytes = new byte[length]; Expiry = expiry; }
        internal readonly string Id, Owner, Session;
        internal readonly byte[] Bytes;
        internal readonly DateTimeOffset Expiry;
        internal int Received, Leases;
        internal bool Sealed, Retired;
        internal string? Hash;
    }

    public SketchImportStagingStore(SketchImportStagingPolicy policy, Func<DateTimeOffset>? clock = null)
    { _policy = policy ?? throw new ArgumentNullException(nameof(policy)); _clock = clock ?? (() => DateTimeOffset.UtcNow); }

    /// <summary>Includes expired/discarded entries still pinned by a lease.</summary>
    public long ReservedBytes { get { lock (_gate) { SweepCore(); return _reservedBytes; } } }
    public int ResidentTransfers { get { lock (_gate) { SweepCore(); return _residentTransfers; } } }

    public string Begin(string owner, string session, int declaredSize)
    {
        ValidateIdentity(owner, session);
        if (declaredSize < 1 || declaredSize > _policy.MaxBytesPerTransfer)
            throw new ArgumentOutOfRangeException(nameof(declaredSize));
        lock (_gate)
        {
            ThrowIfDisposed(); SweepCore();
            if (_residentTransfers >= _policy.MaxTransfers || declaredSize > _policy.MaxTotalBytes - _reservedBytes)
                throw new InvalidOperationException("Staging capacity is exhausted.");
            DateTimeOffset expiry = _clock().Add(_policy.Ttl);
            string id;
            do { id = "st_" + Guid.NewGuid().ToString("N"); } while (_entries.ContainsKey(id));
            var entry = new Entry(id, owner, session, declaredSize, expiry);
            _entries.Add(id, entry); _reservedBytes += declaredSize; _residentTransfers++;
            return id;
        }
    }

    public void Append(string owner, string session, string id, int offset, byte[] chunk)
    {
        ValidateIdentity(owner, session);
        if (chunk == null) throw new ArgumentNullException(nameof(chunk));
        if (chunk.Length < 1 || chunk.Length > _policy.MaxChunkBytes)
            throw new ArgumentOutOfRangeException(nameof(chunk));
        lock (_gate)
        {
            Entry entry = Require(owner, session, id);
            if (entry.Sealed) throw new InvalidOperationException("Transfer is already sealed.");
            if (offset != entry.Received || chunk.Length > entry.Bytes.Length - entry.Received)
                throw new ArgumentException("Chunk must continue the transfer within its declared length.", nameof(offset));
            Buffer.BlockCopy(chunk, 0, entry.Bytes, entry.Received, chunk.Length);
            entry.Received += chunk.Length;
        }
    }

    /// <summary>A wrong digest retires the transfer; a new transfer is required to retry.</summary>
    public void Seal(string owner, string session, string id, string sha256)
    {
        ValidateIdentity(owner, session);
        if (!IsSha256(sha256)) throw new ArgumentException("Declare a SHA256 hexadecimal digest.", nameof(sha256));
        lock (_gate)
        {
            Entry entry = Require(owner, session, id);
            if (entry.Sealed) throw new InvalidOperationException("Transfer is already sealed.");
            if (entry.Received != entry.Bytes.Length) throw new InvalidOperationException("Transfer is incomplete.");
            string actual;
            using (var hash = SHA256.Create())
                actual = BitConverter.ToString(hash.ComputeHash(entry.Bytes)).Replace("-", "").ToLowerInvariant();
            if (!string.Equals(actual, sha256, StringComparison.OrdinalIgnoreCase))
            {
                _entries.Remove(entry.Id); Retire(entry);
                throw new InvalidOperationException("Transfer digest does not match.");
            }
            entry.Hash = actual; entry.Sealed = true;
        }
    }

    /// <summary>Returns null for absent/foreign/expired transfers; unsealed transfers cannot be acquired.</summary>
    public Lease? Acquire(string owner, string session, string id)
    {
        ValidateIdentity(owner, session);
        lock (_gate)
        {
            ThrowIfDisposed(); SweepCore();
            Entry? entry = Find(owner, session, id);
            if (entry == null) return null;
            if (!entry.Sealed) throw new InvalidOperationException("Transfer is not sealed.");
            var lease = new Lease(this, entry);
            entry.Leases++;
            return lease;
        }
    }

    public bool Discard(string owner, string session, string id)
    {
        ValidateIdentity(owner, session);
        lock (_gate)
        {
            ThrowIfDisposed(); SweepCore();
            Entry? entry = Find(owner, session, id);
            if (entry == null) return false;
            _entries.Remove(entry.Id); Retire(entry); return true;
        }
    }

    public void SweepExpired() { lock (_gate) { ThrowIfDisposed(); SweepCore(); } }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (Entry entry in _entries.Values) Retire(entry);
            _entries.Clear();
        }
    }

    private Entry Require(string owner, string session, string id)
    {
        ThrowIfDisposed(); SweepCore();
        return Find(owner, session, id) ?? throw new KeyNotFoundException("Transfer is unavailable.");
    }
    private Entry? Find(string owner, string session, string id)
    {
        if (id == null) return null;
        return _entries.TryGetValue(id, out Entry? entry)
            && string.Equals(entry.Owner, owner, StringComparison.Ordinal)
            && string.Equals(entry.Session, session, StringComparison.Ordinal) ? entry : null;
    }
    private void SweepCore()
    {
        if (_entries.Count == 0) return;
        DateTimeOffset now = _clock();
        var expired = new List<Entry>();
        foreach (Entry entry in _entries.Values) if (now >= entry.Expiry) expired.Add(entry);
        foreach (Entry entry in expired) { _entries.Remove(entry.Id); Retire(entry); }
    }
    private void Retire(Entry entry)
    { entry.Retired = true; if (entry.Leases == 0) Release(entry); }
    private void Release(Entry entry)
    { Array.Clear(entry.Bytes, 0, entry.Bytes.Length); _reservedBytes -= entry.Bytes.Length; _residentTransfers--; }
    private void ValidateIdentity(string owner, string session)
    {
        if (string.IsNullOrWhiteSpace(owner) || owner.Length > _policy.MaxIdentityLength
            || string.IsNullOrWhiteSpace(session) || session.Length > _policy.MaxIdentityLength)
            throw new ArgumentException("Owner and session identities are required within the configured limit.");
    }
    private void ThrowIfDisposed() { if (_disposed) throw new ObjectDisposedException(nameof(SketchImportStagingStore)); }
    private static bool IsSha256(string value)
    {
        if (value == null || value.Length != 64) return false;
        foreach (char c in value) if (!(c >= '0' && c <= '9') && !(c >= 'a' && c <= 'f') && !(c >= 'A' && c <= 'F')) return false;
        return true;
    }

    public sealed class Lease : IDisposable
    {
        private readonly SketchImportStagingStore _store;
        private Entry? _entry;
        internal Lease(SketchImportStagingStore store, Entry entry)
        { _store = store; _entry = entry; Id = entry.Id; Size = entry.Bytes.Length; Sha256 = entry.Hash!; }
        public string Id { get; }
        public int Size { get; }
        public string Sha256 { get; }
        public byte[] ReadBytes()
        {
            lock (_store._gate)
            {
                if (_entry == null) throw new ObjectDisposedException(nameof(Lease));
                return (byte[])_entry.Bytes.Clone();
            }
        }
        /// <summary>Reads a detached bounded range, including after the store retires this lease's entry.</summary>
        public byte[] ReadChunk(int offset, int count)
        {
            lock (_store._gate)
            {
                if (_entry == null) throw new ObjectDisposedException(nameof(Lease));
                if (count < 1 || count > _store._policy.MaxChunkBytes)
                    throw new ArgumentOutOfRangeException(nameof(count));
                // Subtraction avoids overflowing offset + count before checking the range.
                if (offset < 0 || offset > _entry.Bytes.Length || count > _entry.Bytes.Length - offset)
                    throw new ArgumentOutOfRangeException(nameof(offset));
                var chunk = new byte[count];
                Buffer.BlockCopy(_entry.Bytes, offset, chunk, 0, count);
                return chunk;
            }
        }
        public void Dispose()
        {
            lock (_store._gate)
            {
                if (_entry == null) return;
                Entry entry = _entry; _entry = null; entry.Leases--;
                if (entry.Retired && entry.Leases == 0) _store.Release(entry);
            }
        }
    }
}

