using System.Collections.Concurrent;
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

    public sealed class MemoryAssetCache : IAssetCache
    {
        private readonly ConcurrentDictionary<string, byte[]> _items = new ConcurrentDictionary<string, byte[]>();
        public bool TryGet(string assetId, out byte[] bytes) => _items.TryGetValue(assetId, out bytes);
        public void Put(string assetId, byte[] bytes) => _items[assetId] = bytes;
    }

    /// <summary>Content-addressed disk cache: an id never changes meaning, so entries never need invalidating.</summary>
    public sealed class FileAssetCache : IAssetCache
    {
        private readonly string _directory;

        public FileAssetCache(string directory)
        {
            _directory = directory;
            Directory.CreateDirectory(directory);
        }

        public bool TryGet(string assetId, out byte[] bytes)
        {
            bytes = null;
            if (!AssetIds.IsValid(assetId)) return false;
            var path = Path.Combine(_directory, assetId + ".glb");
            if (!File.Exists(path)) return false;
            bytes = File.ReadAllBytes(path);
            return true;
        }

        public void Put(string assetId, byte[] bytes)
        {
            if (!AssetIds.IsValid(assetId)) return;
            var path = Path.Combine(_directory, assetId + ".glb");
            var temp = path + ".tmp";
            File.WriteAllBytes(temp, bytes);
            if (File.Exists(path)) File.Delete(temp);
            else File.Move(temp, path);
        }
    }
}
