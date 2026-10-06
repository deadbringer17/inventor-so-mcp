using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using InventorXrSo.Core.Net;

namespace InventorXrSo.Core.Backend
{
    public interface IAssetCache
    {
        bool TryGet(string assetId, out byte[] bytes);
        void Put(string assetId, byte[] bytes);
    }

    public static class AssetIds
    {
        private static readonly Regex Pattern = new Regex("^a_[0-9a-f]{64}$");

        public static bool IsValid(string assetId) => assetId != null && Pattern.IsMatch(assetId);

        /// <summary>Asset ids are "a_" + SHA-256 of the bytes: a download is checked, not trusted.</summary>
        public static bool Matches(string assetId, byte[] bytes)
        {
            if (!IsValid(assetId)) return false;
            string hash;
            using (var sha = SHA256.Create())
            {
                var builder = new StringBuilder(64);
                foreach (var b in sha.ComputeHash(bytes)) builder.Append(b.ToString("x2"));
                hash = builder.ToString();
            }
            return CertificatePin.FixedTimeEquals(assetId.Substring(2), hash);
        }
    }

    /// <summary>In-memory cache; with a cap it evicts the least recently used entries (0 = unbounded).</summary>
    public sealed class MemoryAssetCache : IAssetCache
    {
        private readonly object _gate = new object();
        private readonly Dictionary<string, LinkedListNode<KeyValuePair<string, byte[]>>> _items =
            new Dictionary<string, LinkedListNode<KeyValuePair<string, byte[]>>>();
        // Front = most recently used.
        private readonly LinkedList<KeyValuePair<string, byte[]>> _order = new LinkedList<KeyValuePair<string, byte[]>>();
        private readonly int _maxEntries;
        private readonly long _maxBytes;
        private long _bytes;

        public MemoryAssetCache(int maxEntries = 0, long maxBytes = 0)
        {
            _maxEntries = maxEntries;
            _maxBytes = maxBytes;
        }

        public int Count { get { lock (_gate) return _items.Count; } }
        public long TotalBytes { get { lock (_gate) return _bytes; } }

        public bool TryGet(string assetId, out byte[] bytes)
        {
            bytes = null;
            if (assetId == null) return false;
            lock (_gate)
            {
                if (!_items.TryGetValue(assetId, out var node)) return false;
                _order.Remove(node);
                _order.AddFirst(node);
                bytes = node.Value.Value;
                return true;
            }
        }

        public void Put(string assetId, byte[] bytes)
        {
            if (assetId == null || bytes == null) return;
            lock (_gate)
            {
                if (_items.TryGetValue(assetId, out var existing))
                {
                    _bytes -= existing.Value.Value.Length;
                    _order.Remove(existing);
                    _items.Remove(assetId);
                }
                var node = new LinkedListNode<KeyValuePair<string, byte[]>>(new KeyValuePair<string, byte[]>(assetId, bytes));
                _order.AddFirst(node);
                _items[assetId] = node;
                _bytes += bytes.Length;
                // The entry just added is never evicted, even when it alone exceeds the byte cap.
                while (_order.Count > 1 && ((_maxEntries > 0 && _order.Count > _maxEntries) || (_maxBytes > 0 && _bytes > _maxBytes)))
                {
                    var last = _order.Last;
                    _order.RemoveLast();
                    _items.Remove(last.Value.Key);
                    _bytes -= last.Value.Value.Length;
                }
            }
        }
    }

    /// <summary>
    /// Content-addressed disk cache: an id never changes meaning, so entries never need invalidating, only
    /// trimming. Total size is capped; the least recently used files (by last-write time, touched on read)
    /// are deleted first.
    /// </summary>
    public sealed class FileAssetCache : IAssetCache
    {
        public const long DefaultMaxBytes = 512L * 1024 * 1024;

        private readonly string _directory;
        private readonly long _maxBytes;
        private readonly object _evictGate = new object();

        public FileAssetCache(string directory, long maxBytes = DefaultMaxBytes)
        {
            _directory = directory;
            _maxBytes = maxBytes;
            Directory.CreateDirectory(directory);
        }

        private string PathFor(string assetId) => Path.Combine(_directory, assetId + ".glb");

        public bool TryGet(string assetId, out byte[] bytes)
        {
            bytes = null;
            if (!AssetIds.IsValid(assetId)) return false;
            var path = PathFor(assetId);
            try
            {
                if (!File.Exists(path)) return false;
                bytes = File.ReadAllBytes(path);
                Touch(path);
                return true;
            }
            catch (IOException) { bytes = null; return false; }          // evicted or replaced under us: a miss
            catch (UnauthorizedAccessException) { bytes = null; return false; }
        }

        public void Put(string assetId, byte[] bytes)
        {
            if (!AssetIds.IsValid(assetId) || bytes == null) return;
            var path = PathFor(assetId);
            try
            {
                if (File.Exists(path)) { Touch(path); return; }           // same id = same bytes
                var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllBytes(temp, bytes);
                try { File.Move(temp, path); }
                catch (IOException)
                {
                    // A concurrent Put of the same id won the race: the content is identical, drop ours.
                    try { File.Delete(temp); } catch (IOException) { }
                    if (!File.Exists(path)) throw;
                }
            }
            catch (UnauthorizedAccessException) { return; }
            Evict(path);
        }

        private static void Touch(string path)
        {
            try { File.SetLastWriteTimeUtc(path, DateTime.UtcNow); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private void Evict(string keepPath)
        {
            if (_maxBytes <= 0) return;
            lock (_evictGate)
            {
                var files = new List<FileInfo>();
                long total = 0;
                foreach (var file in new DirectoryInfo(_directory).GetFiles("*.glb"))
                {
                    files.Add(file);
                    total += file.Length;
                }
                if (total <= _maxBytes) return;
                files.Sort((a, b) => a.LastWriteTimeUtc.CompareTo(b.LastWriteTimeUtc));
                foreach (var file in files)
                {
                    if (total <= _maxBytes) break;
                    if (string.Equals(file.FullName, keepPath, StringComparison.OrdinalIgnoreCase)) continue;
                    try { var length = file.Length; file.Delete(); total -= length; }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
        }
    }
}
