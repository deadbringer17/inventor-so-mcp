using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Server.Assets;

/// <summary>
/// Tessellations keyed by document and visual revision, so an unchanged definition yields the same
/// GLB bytes and therefore the same asset id. Re-tessellating is not enough for that: the portable
/// face ids in the GLB extras carry a fresh key context on every call (verified live on Inventor
/// 2027), which changes the bytes although the geometry is identical. The visual revision advances
/// on every document change and never on save, activation or view events, and it embeds the
/// add-in's epoch, so a hit can never serve geometry from another state or another add-in session.
/// Bounded LRU (entry count and an estimated byte budget of the decoded meshes); the stored JSON is the
/// add-in's answer minus <c>bodies</c> (the decoded <see cref="GlbBuilder.MeshSource"/> already holds them, so
/// keeping the base64 too would store every mesh twice). Shared by every client of this server process
/// (asset ownership stays per client in <see cref="AssetStore"/>).
/// </summary>
public sealed class MeshCache
{
    public const int DefaultCapacity = 64;
    /// <summary>Estimated memory the cached meshes may use together (256 MB); least recently used entries go first.</summary>
    public const long DefaultMaxBytes = 256L * 1024 * 1024;
    private const long FaceRangeBytes = 160;
    private const long EntryOverheadBytes = 1024;

    private readonly object _gate = new();
    private readonly int _capacity;
    private readonly long _maxBytes;
    private readonly Dictionary<string, (JObject Data, GlbBuilder.MeshSource Source, long Used, long Bytes)> _entries = new(StringComparer.Ordinal);
    private long _clock;
    private long _bytes;

    public MeshCache() : this(DefaultCapacity) { }

    public MeshCache(int capacity) : this(capacity, DefaultMaxBytes) { }

    public MeshCache(int capacity, long maxBytes)
    {
        if (capacity < 1) throw new ArgumentException("capacity must be positive.");
        if (maxBytes < 1) throw new ArgumentException("maxBytes must be positive.");
        _capacity = capacity;
        _maxBytes = maxBytes;
    }

    /// <summary>Estimated bytes currently held (decoded arrays plus a small per-entry overhead).</summary>
    public long EstimatedBytes { get { lock (_gate) return _bytes; } }

    /// <summary>Memory estimate of one decoded mesh: float32 positions and normals, uint32 indices, face ranges.</summary>
    public static long EstimateBytes(GlbBuilder.MeshSource source)
    {
        long total = EntryOverheadBytes;
        foreach (var body in source.Bodies)
            total += ((long)body.Positions.Length + body.Normals.Length + body.Indices.Length) * 4 + body.Faces.Count * FaceRangeBytes;
        return total;
    }

    public static string Key(string documentId, string visualRevision, double toleranceMm, bool includeFaceIds)
        => documentId + "|" + visualRevision + "|" + toleranceMm.ToString("R", CultureInfo.InvariantCulture) + "|" + (includeFaceIds ? "ids" : "noids");

    public bool TryGet(string key, out JObject data, out GlbBuilder.MeshSource source)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(key, out var entry))
            {
                _entries[key] = (entry.Data, entry.Source, ++_clock, entry.Bytes);
                data = (JObject)entry.Data.DeepClone();
                source = entry.Source;
                return true;
            }
        }
        data = null!;
        source = null!;
        return false;
    }

    public void Put(string key, JObject data, GlbBuilder.MeshSource source)
    {
        // The decoded source carries the geometry; keep only the small metadata (revision, names, flags).
        var meta = new JObject(data.Properties().Where(p => p.Name != "bodies").Select(p => new JProperty(p.Name, p.Value.DeepClone())));
        var bytes = EstimateBytes(source);
        lock (_gate)
        {
            if (_entries.Remove(key, out var old)) _bytes -= old.Bytes;
            if (bytes > _maxBytes) return;   // never cacheable: do not flush everything else for it
            _entries[key] = (meta, source, ++_clock, bytes);
            _bytes += bytes;
            while (_entries.Count > _capacity || _bytes > _maxBytes)
            {
                var oldest = _entries.OrderBy(e => e.Value.Used).First().Key;
                _bytes -= _entries[oldest].Bytes;
                _entries.Remove(oldest);
            }
        }
    }

    public int Count { get { lock (_gate) return _entries.Count; } }
}
