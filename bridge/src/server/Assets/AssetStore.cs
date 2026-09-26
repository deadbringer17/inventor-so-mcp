using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Server.Assets;

/// <summary>A stored asset's metadata. The bytes live on disk under the store's own directory.</summary>
public sealed class AssetRecord
{
    public string Id { get; init; } = "";
    public string Sha256 { get; init; } = "";
    public string MimeType { get; init; } = "";
    public long Size { get; init; }
    public DateTimeOffset CreatedUtc { get; init; }
    public DateTimeOffset ExpiresUtc { get; set; }
    public DateTimeOffset LastAccessUtc { get; set; }
    public HashSet<string> Owners { get; } = new(StringComparer.Ordinal);
    public JObject Metadata { get; set; } = new();

    public JObject ToJson(string? publicBaseUrl) => new()
    {
        ["asset_id"] = Id,
        ["sha256"] = Sha256,
        ["mime_type"] = MimeType,
        ["size_bytes"] = Size,
        ["expires_utc"] = ExpiresUtc.ToString("O"),
        ["resource_uri"] = "inventor://assets/" + Id,
        ["asset_url"] = string.IsNullOrWhiteSpace(publicBaseUrl) ? null : publicBaseUrl!.TrimEnd('/') + "/assets/" + Id,
        ["metadata"] = Metadata.DeepClone(),
    };
}

/// <summary>
/// Content-addressed, bounded, expiring store for meshes, images and reports handed to remote clients.
/// <list type="bullet">
/// <item>Ids are <c>a_</c> + SHA-256 of the bytes: never a path, validated by regex before any lookup.</item>
/// <item>MIME allowlist, per-asset and total size limits, LRU eviction, TTL renewed on reuse.</item>
/// <item>Ownership: an asset is readable only by the clients (token names) that produced it; a
/// foreign or unknown id is reported as not found, so existence never leaks.</item>
/// <item>Bytes read back from disk are re-hashed; a tampered file is deleted, not served.</item>
/// </list>
/// Nothing survives a restart: the index is in memory and files left by a previous run are removed
/// at start-up, so a stale file can never be addressed.
/// </summary>
public sealed class AssetStore
{
    public static readonly IReadOnlyDictionary<string, string> AllowedMimeTypes = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["model/gltf-binary"] = ".glb",
        ["image/png"] = ".png",
        ["application/pdf"] = ".pdf",
        ["text/csv"] = ".csv",
        ["application/json"] = ".json",
    };

    private static readonly Regex IdPattern = new("^a_[0-9a-f]{64}$", RegexOptions.CultureInvariant);

    private readonly object _gate = new();
    private readonly Dictionary<string, AssetRecord> _records = new(StringComparer.Ordinal);
    private readonly string _directory;
    private readonly TimeSpan _ttl;
    private readonly long _maxBytes;
    private readonly long _totalBytes;
    private readonly Func<DateTimeOffset> _clock;
    private long _stored;

    public AssetStore(InventorMcpConfig config) : this(config.AssetDirectory, TimeSpan.FromMinutes(Math.Max(1, config.AssetTtlMinutes)),
        config.AssetMaxBytes, config.AssetTotalBytes) { }

    public AssetStore(string directory, TimeSpan ttl, long maxBytes, long totalBytes, Func<DateTimeOffset>? clock = null)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Path.IsPathFullyQualified(directory))
            throw new ArgumentException("Asset directory must be an absolute path.");
        if (maxBytes <= 0 || totalBytes < maxBytes) throw new ArgumentException("Asset limits must be positive and total >= per-asset.");
        _directory = Path.GetFullPath(directory);
        _ttl = ttl;
        _maxBytes = maxBytes;
        _totalBytes = totalBytes;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        Directory.CreateDirectory(_directory);
        foreach (var stale in Directory.EnumerateFiles(_directory, "a_*"))
            try { File.Delete(stale); } catch { /* locked by another process: it stays unaddressable */ }
    }

    public long MaxBytes => _maxBytes;

    public static bool IsValidId(string? id) => id != null && IdPattern.IsMatch(id);

    /// <summary>Store bytes (or renew an identical asset) for <paramref name="owner"/>.</summary>
    public AssetRecord Put(byte[] bytes, string mimeType, string owner, JObject? metadata = null)
    {
        if (bytes == null || bytes.Length == 0) throw new ArgumentException("An asset needs content.");
        if (!AllowedMimeTypes.ContainsKey(mimeType)) throw new ArgumentException("MIME type '" + mimeType + "' is not allowed.");
        if (bytes.Length > _maxBytes) throw new ArgumentException("Asset of " + bytes.Length + " bytes exceeds the " + _maxBytes + "-byte limit.");
        if (string.IsNullOrWhiteSpace(owner)) throw new ArgumentException("An asset needs an owner.");
        string sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        string id = "a_" + sha;
        var now = _clock();
        lock (_gate)
        {
            Sweep(now);
            if (_records.TryGetValue(id, out var existing) && existing.MimeType == mimeType)
            {
                existing.Owners.Add(owner);
                existing.ExpiresUtc = now + _ttl;
                existing.LastAccessUtc = now;
                if (metadata != null) existing.Metadata = metadata;
                return existing;
            }
            EvictFor(bytes.Length);
            var path = PathOf(id);
            var temp = path + ".tmp";
            File.WriteAllBytes(temp, bytes);
            File.Move(temp, path, overwrite: true);
            var record = new AssetRecord
            {
                Id = id, Sha256 = sha, MimeType = mimeType, Size = bytes.Length, CreatedUtc = now,
                ExpiresUtc = now + _ttl, LastAccessUtc = now, Metadata = metadata ?? new JObject(),
            };
            record.Owners.Add(owner);
            _records[id] = record;
            _stored += bytes.Length;
            return record;
        }
    }

    /// <summary>Metadata only, subject to the same ownership and expiry rules as the bytes.</summary>
    public AssetRecord? Describe(string? id, string requester)
    {
        if (!IsValidId(id)) return null;
        lock (_gate)
        {
            Sweep(_clock());
            return _records.TryGetValue(id!, out var record) && record.Owners.Contains(requester) ? record : null;
        }
    }

    /// <summary>Read an asset's bytes, verifying ownership, expiry and the content hash.</summary>
    public bool TryRead(string? id, string requester, out AssetRecord? record, out byte[]? bytes)
    {
        record = null;
        bytes = null;
        if (!IsValidId(id)) return false;
        lock (_gate)
        {
            var now = _clock();
            Sweep(now);
            if (!_records.TryGetValue(id!, out var found) || !found.Owners.Contains(requester)) return false;
            byte[] data;
            try { data = File.ReadAllBytes(PathOf(id!)); }
            catch (IOException) { Remove(id!); return false; }
            if (Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant() != found.Sha256)
            {
                Remove(id!);
                return false;
            }
            found.LastAccessUtc = now;
            found.ExpiresUtc = now + _ttl;
            record = found;
            bytes = data;
            return true;
        }
    }

    public IReadOnlyList<AssetRecord> List(string requester)
    {
        lock (_gate)
        {
            Sweep(_clock());
            return _records.Values.Where(r => r.Owners.Contains(requester)).OrderByDescending(r => r.LastAccessUtc).ToArray();
        }
    }

    public long StoredBytes { get { lock (_gate) return _stored; } }

    private string PathOf(string id) => Path.Combine(_directory, id + ".bin");

    private void Sweep(DateTimeOffset now)
    {
        foreach (var expired in _records.Values.Where(r => r.ExpiresUtc <= now).Select(r => r.Id).ToArray())
            Remove(expired);
    }

    private void EvictFor(long incoming)
    {
        foreach (var victim in _records.Values.OrderBy(r => r.LastAccessUtc).Select(r => r.Id).ToArray())
        {
            if (_stored + incoming <= _totalBytes) break;
            Remove(victim);
        }
    }

    private void Remove(string id)
    {
        if (_records.Remove(id, out var record)) _stored -= record.Size;
        try { File.Delete(PathOf(id)); } catch { /* best effort; the id is no longer addressable */ }
    }
}
