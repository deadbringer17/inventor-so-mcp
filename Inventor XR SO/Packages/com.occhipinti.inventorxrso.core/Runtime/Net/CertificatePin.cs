using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace InventorXrSo.Core.Net
{
    /// <summary>SHA-256 fingerprints of DER certificates: how the headset recognises its PC.</summary>
    public static class CertificatePin
    {
        public static string Sha256Hex(byte[] der)
        {
            using (var sha = SHA256.Create())
                return ToHex(sha.ComputeHash(der));
        }

        public static bool IsValid(string hex)
        {
            if (hex == null) return false;
            var clean = Strip(hex);
            return clean.Length == 64 && clean.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'));
        }

        /// <summary>Lowercase hex without separators; throws on anything that is not 64 hex digits.</summary>
        public static string Normalize(string hex)
        {
            if (!IsValid(hex)) throw new ArgumentException("Not a SHA-256 fingerprint.", nameof(hex));
            return Strip(hex);
        }

        public static bool FixedTimeEquals(string a, string b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }

        /// <summary>"ABCD 0123 …", the grouping the PC console prints.</summary>
        public static string Display(string hex)
        {
            var builder = new StringBuilder();
            for (int i = 0; i + 4 <= hex.Length; i += 4)
            {
                if (builder.Length > 0) builder.Append(' ');
                builder.Append(hex.Substring(i, 4).ToUpperInvariant());
            }
            return builder.ToString();
        }

        private static string Strip(string hex) => hex.Replace(":", "").Replace(" ", "").ToLowerInvariant();

        private static string ToHex(byte[] bytes)
        {
            var builder = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes) builder.Append(b.ToString("x2"));
            return builder.ToString();
        }
    }
}
