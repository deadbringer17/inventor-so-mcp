using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using InventorXrSo.Core.Mcp;
using InventorXrSo.Core.Net;
using InventorXrSo.Core.Pairing;
using Newtonsoft.Json.Linq;

namespace InventorXrSo.Core.Backend
{
    public interface INativeSketchImageBackend
    {
        Task<NativeSketchImageAsset> GetNativeSketchImageAsync(DocumentState state, string sketchId, string imageId, CancellationToken ct);
    }

    /// <summary>Caller-owned verified native bitmap. Dispose clears its private PNG; no cache or scene references.</summary>
    public sealed class NativeSketchImageAsset : IDisposable
    {
        private readonly object _sync = new object();
        private byte[] _png;
        internal NativeSketchImageAsset(DocumentState state, string sketchId, string imageId, CadPoint[] corners,
            int pixelWidth, int pixelHeight, double width, double height, bool visible, string assetId, string sha, byte[] png)
        {
            DocumentId = state.DocumentId; Revision = state.Revision; SketchId = sketchId; ImageId = imageId;
            CornersMm = Array.AsReadOnly((CadPoint[])corners.Clone()); PixelWidth = pixelWidth; PixelHeight = pixelHeight;
            WidthMm = width; HeightMm = height; Visible = visible; AssetId = assetId; Sha256 = sha; _png = png;
        }
        public string DocumentId { get; }
        public string Revision { get; }
        public string SketchId { get; }
        public string ImageId { get; }
        public IReadOnlyList<CadPoint> CornersMm { get; }
        public int PixelWidth { get; }
        public int PixelHeight { get; }
        public double WidthMm { get; }
        public double HeightMm { get; }
        public bool Visible { get; }
        public bool Transparent => false;
        public string SourceKind => "native_bitmap";
        public string Provenance => "native_boundary_roles";
        public string AssetId { get; }
        public string Sha256 { get; }
        public byte[] CopyPngBytes()
        { lock (_sync) { if (_png == null) throw new ObjectDisposedException(nameof(NativeSketchImageAsset)); return (byte[])_png.Clone(); } }
        public bool Matches(DocumentState state, string sketchId, string imageId) => state != null && state.DocumentId == DocumentId
            && state.Revision == Revision && sketchId == SketchId && imageId == ImageId;
        public void Dispose()
        { lock (_sync) { if (_png == null) return; Array.Clear(_png, 0, _png.Length); _png = null; } }
    }

    /// <summary>Strict native-image metadata boundary and bounded authenticated download through the paired TLS transport.</summary>
    public sealed class NativeSketchImageClient
    {
        public const int MaxImageBytes = 32 * 1024 * 1024;
        private readonly IHttpTransport _transport;
        private readonly PairedServer _server;
        public NativeSketchImageClient(IHttpTransport transport, PairedServer server)
        { _transport = transport ?? throw new ArgumentNullException(nameof(transport)); _server = server ?? throw new ArgumentNullException(nameof(server)); }

        public async Task<NativeSketchImageAsset> DownloadAsync(JObject json, DocumentState state, string sketchId, string imageId, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            RequireReferences(state, sketchId, imageId);
            if (json == null || Text(json, "document_id") != state.DocumentId || Text(json, "revision") != state.Revision
                || Text(json, "sketch_id") != sketchId || Text(json, "image_id") != imageId) throw Invalid();
            if (Text(json, "source_kind") != "native_bitmap" || Boolean(json, "transparent")) throw Invalid();
            bool visible = Boolean(json, "visible");
            int pixelWidth = Integer(json, "pixel_width", 1, 8192), pixelHeight = Integer(json, "pixel_height", 1, 8192);
            if ((long)pixelWidth * pixelHeight > 8 * 1024 * 1024) throw Invalid();
            double width = Number(json["width_mm"]), height = Number(json["height_mm"]);
            if (width <= 0 || height <= 0) throw Invalid();
            if (!(json["image_frame"] is JObject frame) || frame.Count != 4 || Integer(frame, "schema", 1, 1) != 1
                || Text(frame, "provenance") != "native_boundary_roles" || !(frame["corner_order"] is JArray order)
                || order.Count != 4 || order.Any(v => v.Type != JTokenType.String)
                || !order.Values<string>().SequenceEqual(new[] { "top_left", "top_right", "bottom_right", "bottom_left" }, StringComparer.Ordinal)
                || !(frame["corners_mm"] is JArray points) || points.Count != 4) throw Invalid();
            var corners = new CadPoint[4];
            for (int i = 0; i < 4; i++)
            { if (!(points[i] is JArray p) || p.Count != 2) throw Invalid(); corners[i] = new CadPoint(Number(p[0]), Number(p[1])); }
            double Length(double x, double y) => Math.Sqrt(x * x + y * y);
            double tx = corners[1].X - corners[0].X, ty = corners[1].Y - corners[0].Y;
            double lx = corners[3].X - corners[0].X, ly = corners[3].Y - corners[0].Y;
            if (!(Length(tx, ty) > 0) || !(Length(lx, ly) > 0) || !Near(Length(tx, ty), width) || !Near(Length(lx, ly), height)
                || !Near(Length(corners[2].X - corners[3].X, corners[2].Y - corners[3].Y), width)
                || !Near(Length(corners[2].X - corners[1].X, corners[2].Y - corners[1].Y), height)
                || !Near(Length(corners[2].X - corners[1].X - lx, corners[2].Y - corners[1].Y - ly), 0)
                || !Near((tx * lx + ty * ly) / width, 0)) throw Invalid();
            if (!(json["asset"] is JObject asset)) throw Invalid();
            string sha = Text(asset, "sha256"), id = Text(asset, "asset_id");
            if (!Hash(sha) || id != "a_" + sha || Text(asset, "mime_type") != "image/png") throw Invalid();
            int size = Integer(asset, "size_bytes", 33, MaxImageBytes);
            string url = ResolveAssetUrl(Text(asset, "asset_url"), id);
            var request = new TransportRequest("GET", url) { MaxResponseBytes = size, Timeout = TimeSpan.FromSeconds(30) };
            request.Headers["Authorization"] = "Bearer " + _server.Token;
            var response = await _transport.SendAsync(request, ct);
            byte[] owned = null;
            try
            {
                ct.ThrowIfCancellationRequested();
                if (response.Status == 401) throw new McpUnauthorizedException();
                if (response.Status != 200) throw new McpException("NATIVE_IMAGE_HTTP", "Native image could not be downloaded.");
                if (response.Body == null || response.Body.Length != size || response.Header("Content-Type")?.Split(';')[0].Trim().ToLowerInvariant() != "image/png") throw Invalid();
                // SHA/CRC can scan tens of MiB. Keep this pure byte work off the Unity caller thread;
                // the await resumes on its captured context before ownership is handed to the renderer.
                owned = await Task.Run(() => ValidateAndCopy(response.Body, sha, pixelWidth, pixelHeight, ct), ct);
                ct.ThrowIfCancellationRequested();
                var result = new NativeSketchImageAsset(state, sketchId, imageId, corners, pixelWidth, pixelHeight, width, height, visible, id, sha, owned);
                owned = null; return result;
            }
            finally
            {
                if (owned != null) Array.Clear(owned, 0, owned.Length);
                if (response.Body != null) Array.Clear(response.Body, 0, response.Body.Length);
            }
        }

        private static byte[] ValidateAndCopy(byte[] bytes, string sha, int pixelWidth, int pixelHeight, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            using (var digest = SHA256.Create())
            {
                const int block = 256 * 1024;
                for (int at = 0; at < bytes.Length; at += block)
                {
                    ct.ThrowIfCancellationRequested();
                    digest.TransformBlock(bytes, at, Math.Min(block, bytes.Length - at), null, 0);
                }
                digest.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                ct.ThrowIfCancellationRequested();
                if (BitConverter.ToString(digest.Hash).Replace("-", "").ToLowerInvariant() != sha)
                    throw new McpException("ASSET_CORRUPT", "Native image content hash differs.");
            }
            ValidatePng(bytes, pixelWidth, pixelHeight, ct);
            ct.ThrowIfCancellationRequested();
            byte[] owned = (byte[])bytes.Clone();
            try { ct.ThrowIfCancellationRequested(); return owned; }
            catch { Array.Clear(owned, 0, owned.Length); throw; }
        }

        internal static void RequireReferences(DocumentState state, string sketchId, string imageId)
        { if (state == null || string.IsNullOrWhiteSpace(state.DocumentId) || string.IsNullOrWhiteSpace(state.Revision)
                || !SketchEntityRef.Opaque(sketchId) || !SketchEntityRef.Opaque(imageId)) throw Invalid(); }
        private string ResolveAssetUrl(string value, string id)
        {
            string path = "/assets/" + id;
            if (!Uri.TryCreate(_server.BaseUrl, UriKind.Absolute, out var paired) || paired.Scheme != Uri.UriSchemeHttps) throw Invalid();
            if (value == path) return paired.GetLeftPart(UriPartial.Authority) + path;
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length != 0
                || !string.Equals(uri.Host, paired.Host, StringComparison.OrdinalIgnoreCase) || uri.Port != paired.Port
                || uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.AbsolutePath != path
                || value != uri.GetLeftPart(UriPartial.Authority) + path) throw Invalid();
            return paired.GetLeftPart(UriPartial.Authority) + path;
        }
        private static string Text(JObject obj, string field)
        { if (obj[field]?.Type != JTokenType.String) throw Invalid(); return (string)obj[field]; }
        private static bool Boolean(JObject obj, string field)
        { if (obj[field]?.Type != JTokenType.Boolean) throw Invalid(); return (bool)obj[field]; }
        private static int Integer(JObject obj, string field, int min, int max)
        { if (obj[field]?.Type != JTokenType.Integer || !int.TryParse(obj[field].ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) || n < min || n > max) throw Invalid(); return n; }
        private static double Number(JToken t)
        { if (t == null || t.Type != JTokenType.Integer && t.Type != JTokenType.Float || !double.TryParse(t.ToString(Newtonsoft.Json.Formatting.None), NumberStyles.Float, CultureInfo.InvariantCulture, out double n) || double.IsNaN(n) || double.IsInfinity(n)) throw Invalid(); return n; }
        private static bool Near(double a, double b) => !double.IsNaN(a) && !double.IsInfinity(a) && Math.Abs(a - b) <= 1e-5;
        private static bool Hash(string s) => s != null && s.Length == 64 && s.All(c => c >= '0' && c <= '9' || c >= 'a' && c <= 'f');
        private static FormatException Invalid() => new FormatException("Invalid native image response.");

        // Framing, CRC and dimensions are checked here; actual pixel decoding belongs to the renderer.
        private static void ValidatePng(byte[] bytes, int width, int height, CancellationToken ct)
        {
            byte[] signature = { 137, 80, 78, 71, 13, 10, 26, 10 };
            if (!bytes.Take(8).SequenceEqual(signature)) throw Invalid();
            int at = 8; bool header = false, data = false, end = false;
            while (at < bytes.Length)
            {
                ct.ThrowIfCancellationRequested();
                if (end || bytes.Length - at < 12) throw Invalid();
                uint size = Big(bytes, at); if (size > bytes.Length - (long)at - 12) throw Invalid();
                int count = (int)size; string kind = System.Text.Encoding.ASCII.GetString(bytes, at + 4, 4);
                uint crc = uint.MaxValue;
                for (int i = at + 4; i < at + 8 + count; i++)
                { if ((i & 4095) == 0) ct.ThrowIfCancellationRequested(); crc ^= bytes[i]; for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0u : 0xedb88320u); }
                if ((crc ^ uint.MaxValue) != Big(bytes, at + 8 + count)) throw Invalid();
                if (kind == "IHDR")
                {
                    if (header || at != 8 || count != 13 || Big(bytes, at + 8) != width || Big(bytes, at + 12) != height
                        || bytes[at + 16] != 8 || bytes[at + 17] != 6 || bytes[at + 18] != 0 || bytes[at + 19] != 0 || bytes[at + 20] != 0) throw Invalid();
                    header = true;
                }
                else if (kind == "IDAT") { if (!header || count == 0) throw Invalid(); data = true; }
                else if (kind == "IEND") { if (!header || !data || count != 0) throw Invalid(); end = true; }
                else throw Invalid();
                at += count + 12;
            }
            if (!end) throw Invalid();
        }
        private static uint Big(byte[] b, int at) => ((uint)b[at] << 24) | ((uint)b[at + 1] << 16) | ((uint)b[at + 2] << 8) | b[at + 3];
    }
}
