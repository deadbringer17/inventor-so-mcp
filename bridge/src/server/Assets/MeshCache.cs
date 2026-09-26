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
/// Bounded LRU; shared by every client of this server process (asset ownership stays per client in
/// <see cref="AssetStore"/>).
/// </summary>
public sealed class MeshCache
{
    public const int DefaultCapacity = 64;

    private readonly object _gate = new();
    private readonly int _capacity;
    private readonly Dictionary<string, (JObject Data, GlbBuilder.MeshSource Source, long Used)> _entries = new(StringComparer.Ordinal);
    private long _clock;

    public MeshCache() : this(DefaultCapacity) { }

    public MeshCache(int capacity)
    {
        if (capacity < 1) throw new ArgumentException("capacity must be positive.");
        _capacity = capacity;
    }

    public static string Key(string documentId, string visualRevision, double toleranceMm, bool includeFaceIds)
        => documentId + "|" + visualRevision + "|" + toleranceMm.ToString("R", CultureInfo.InvariantCulture) + "|" + (includeFaceIds ? "ids" : "noids");

    public bool TryGet(string key, out JObject data, out GlbBuilder.MeshSource source)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(key, out var entry))
            {
                _entries[key] = (entry.Data, entry.Source, ++_clock);
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
        lock (_gate)
        {
            _entries[key] = ((JObject)data.DeepClone(), source, ++_clock);
            while (_entries.Count > _capacity)
                _entries.Remove(_entries.OrderBy(e => e.Value.Used).First().Key);
        }
    }

    public int Count { get { lock (_gate) return _entries.Count; } }
}
