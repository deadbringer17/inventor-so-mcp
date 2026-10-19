using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using InventorXrSo.Core.Mcp;
using InventorXrSo.Core.Net;
using InventorXrSo.Core.Pairing;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace InventorXrSo.Core.Backend
{
    public interface IImportResourceClient
    {
        Task<ImportResourcePage> ListAsync(int offset, CancellationToken ct);
        Task<ImportResourceDescriptor> DescribeAsync(string assetId, CancellationToken ct);
        Task<ImportResourcePreview> DownloadPreviewAsync(ImportResourceDescriptor resource, CancellationToken ct);
    }

    /// <summary>Explicit client budgets; metadata allow-lists are supplied separately, never inferred from a catalog.</summary>
    public sealed class ImportResourceClientPolicy
    {
        public ImportResourceClientPolicy(SketchImportResourcePolicy metadata, long maxJsonBytes, long maxPreviewBytes,
            int maxPageEntries, int maxJsonDepth, TimeSpan requestTimeout, TimeSpan maxRemainingTtl)
        {
            Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            if (maxJsonBytes < 1 || maxJsonBytes > int.MaxValue || maxPreviewBytes < 1 || maxPreviewBytes > int.MaxValue || maxPageEntries < 1 || maxJsonDepth < 1
                || requestTimeout <= TimeSpan.Zero || maxRemainingTtl <= TimeSpan.Zero)
                throw new ArgumentException("Import client limits must be explicit and positive.");
            MaxJsonBytes = maxJsonBytes; MaxPreviewBytes = maxPreviewBytes; MaxPageEntries = maxPageEntries;
            MaxJsonDepth = maxJsonDepth; RequestTimeout = requestTimeout; MaxRemainingTtl = maxRemainingTtl;
        }
        public SketchImportResourcePolicy Metadata { get; }
        public long MaxJsonBytes { get; }
        public long MaxPreviewBytes { get; }
        public int MaxPageEntries { get; }
        public int MaxJsonDepth { get; }
        public TimeSpan RequestTimeout { get; }
        public TimeSpan MaxRemainingTtl { get; }
    }

    public sealed class ImportResourceDescriptor
    {
        internal ImportResourceDescriptor(SketchImportResource resource, string sha, DateTimeOffset expires, bool preview)
        { Resource = resource; SourceSha256 = sha; ExpiresUtc = expires; PreviewAvailable = preview; }
        public SketchImportResource Resource { get; }
        public string SourceSha256 { get; }
        public DateTimeOffset ExpiresUtc { get; }
        public bool PreviewAvailable { get; }
        public bool CadImportAvailable => false;
    }
    public sealed class ImportResourcePage
    {
        internal ImportResourcePage(IEnumerable<ImportResourceDescriptor> resources, int? next)
        { Resources = Array.AsReadOnly(resources.ToArray()); NextOffset = next; }
        public IReadOnlyList<ImportResourceDescriptor> Resources { get; }
        /// <summary>Navigation through a changing in-memory catalog; not a stable snapshot or an authorization token.</summary>
        public int? NextOffset { get; }
        public bool CadImportAvailable => false;
    }
    public sealed class ImportResourcePreview
    {
        private readonly byte[] _png;
        internal ImportResourcePreview(byte[] png, string sha, string source)
        { _png = (byte[])png.Clone(); PngSha256 = sha; SourceSha256 = source; }
        public byte[] PngBytes => (byte[])_png.Clone();
        public string PngSha256 { get; }
        public string SourceSha256 { get; }
    }

    /// <summary>
    /// Reads only the paired server's resource routes. No upload, CAD, cache or server-supplied URL is used.
    /// PNG validation covers framing, chunk CRC, RGBA8 IHDR and dimensions, not deflate decoding/rendering.
    /// Metadata is locally policy-validated; this is not a proof of source-file or native CAD semantics.
    /// </summary>
    public sealed class ImportResourceClient : IImportResourceClient
    {
        private readonly IHttpTransport _transport;
        private readonly PairedServer _server;
        private readonly ImportResourceClientPolicy _policy;
        private readonly Func<DateTimeOffset> _clock;
        public ImportResourceClient(IHttpTransport transport, PairedServer server, ImportResourceClientPolicy policy, Func<DateTimeOffset> clock)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _server = server ?? throw new ArgumentNullException(nameof(server));
            _policy = policy ?? throw new ArgumentNullException(nameof(policy));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }
        public async Task<ImportResourcePage> ListAsync(int offset, CancellationToken ct)
        {
            if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
            var json = Json(await Get("?offset=" + offset.ToString(CultureInfo.InvariantCulture), _policy.MaxJsonBytes, ct));
            Envelope(json); Fields(json, "ok", "storage", "cad_import_available", "resources", "next_offset");
            if (!(json["resources"] is JArray array) || array.Count > _policy.MaxPageEntries || json.Property("next_offset") == null) throw Invalid("Invalid resource page.");
            var resources = new List<ImportResourceDescriptor>(); var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in array)
            {
                ct.ThrowIfCancellationRequested();
                if (!(item is JObject entry)) throw Invalid("Invalid catalog entry.");
                var resource = Descriptor(entry);
                if (!ids.Add(resource.Resource.AssetId)) throw Invalid("Duplicate resource id in page.");
                resources.Add(resource);
            }
            long? continuation = Null(json["next_offset"]) ? (long?)null : Integer(json["next_offset"]);
            if (continuation.HasValue && (continuation.Value < 0 || continuation.Value > int.MaxValue)) throw Invalid("Page continuation is out of range.");
            int? next = continuation.HasValue ? (int?)continuation.Value : null;
            if (next.HasValue && (array.Count == 0 || next.Value != (long)offset + array.Count)) throw Invalid("Invalid page continuation.");
            ct.ThrowIfCancellationRequested();
            return new ImportResourcePage(resources, next);
        }
        public async Task<ImportResourceDescriptor> DescribeAsync(string assetId, CancellationToken ct)
        {
            RequireId(assetId);
            var descriptor = Descriptor(Json(await Get("/" + assetId, _policy.MaxJsonBytes, ct)));
            if (descriptor.Resource.AssetId != assetId) throw Invalid("Descriptor belongs to another resource.");
            ct.ThrowIfCancellationRequested(); return descriptor;
        }
        public async Task<ImportResourcePreview> DownloadPreviewAsync(ImportResourceDescriptor resource, CancellationToken ct)
        {
            if (resource == null) throw new ArgumentNullException(nameof(resource));
            RequireId(resource.Resource.AssetId); Fresh(resource.ExpiresUtc);
            if (!resource.PreviewAvailable || resource.Resource.Kind != SketchImportResourceKind.Image)
                throw new McpException("IMPORT_PREVIEW_UNAVAILABLE", "This resource does not offer a preview.");
            var response = await Get("/" + resource.Resource.AssetId + "/preview", _policy.MaxPreviewBytes, ct);
            if (MediaType(response.Header("Content-Type")) != "image/png") throw Invalid("Preview is not a PNG response.");
            string pngSha = response.Header("X-Content-SHA256"), sourceSha = response.Header("X-Source-SHA256");
            if (!Hash(pngSha) || !Hash(sourceSha) || sourceSha != resource.SourceSha256 || Sha(response.Body) != pngSha)
                throw new McpException("IMPORT_PREVIEW_CORRUPT", "Preview or source hash does not match.");
            ValidatePng(response.Body, resource.Resource.Image, ct); Fresh(resource.ExpiresUtc); ct.ThrowIfCancellationRequested();
            return new ImportResourcePreview(response.Body, pngSha, sourceSha);
        }

        private async Task<TransportResponse> Get(string suffix, long maxBytes, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var request = new TransportRequest("GET", _server.BaseUrl.TrimEnd('/') + "/imports/resources" + suffix)
                { Timeout = _policy.RequestTimeout, MaxResponseBytes = maxBytes };
            request.Headers["Authorization"] = "Bearer " + _server.Token;
            var response = await _transport.SendAsync(request, ct); ct.ThrowIfCancellationRequested();
            if (response.Status == 401) throw new McpUnauthorizedException();
            if (response.Body.LongLength > maxBytes) throw Invalid("Import response exceeds client policy.");
            if (response.Status != 200)
            {
                string code = "IMPORT_HTTP_" + response.Status.ToString(CultureInfo.InvariantCulture);
                if (response.Body.LongLength <= _policy.MaxJsonBytes && MediaType(response.Header("Content-Type")) == "application/json")
                {
                    try
                    {
                        var error = Json(response);
                        if (error["ok"]?.Type == JTokenType.Boolean && !(bool)error["ok"] && error["error"] is JObject detail
                            && detail["code"]?.Type == JTokenType.String && error["code"]?.Type == JTokenType.String && (string)detail["code"] == (string)error["code"]
                            && SketchImportResourceValidator.Token((string)detail["code"], _policy.Metadata.MaxTokenLength)) code = (string)detail["code"];
                    }
                    catch (Exception ex) when (ex is FormatException || ex is JsonException || ex is ArgumentException) { }
                }
                throw new McpException(code, "Import resource request failed (HTTP " + response.Status.ToString(CultureInfo.InvariantCulture) + ").");
            }
            return response;
        }
        private JObject Json(TransportResponse response)
        {
            if (response.Body.LongLength > _policy.MaxJsonBytes || MediaType(response.Header("Content-Type")) != "application/json")
                throw Invalid("Invalid JSON response type or size.");
            try
            {
                using (var text = new StringReader(new UTF8Encoding(false, true).GetString(response.Body)))
                using (var reader = new JsonTextReader(text) { DateParseHandling = DateParseHandling.None, MaxDepth = _policy.MaxJsonDepth })
                {
                    var json = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                    if (reader.Read()) throw Invalid("Trailing JSON data.");
                    return json;
                }
            }
            catch (Exception ex) when (ex is JsonException || ex is DecoderFallbackException) { throw new FormatException("Malformed import JSON.", ex); }
        }
        private ImportResourceDescriptor Descriptor(JObject json)
        {
            Envelope(json); Fields(json, "ok", "storage", "cad_import_available", "inspection_state", "resource", "sha256", "expires_utc", "preview_available");
            if (String(json["inspection_state"]) != "verified_metadata" || !(json["resource"] is JObject r)) throw Invalid("Unverified import metadata.");
            Fields(r, "asset_id", "kind", "format", "byte_length", "dependencies", "image", "geometry");
            string id = String(r["asset_id"]), sha = String(json["sha256"]); RequireId(id);
            if (!Hash(sha) || id.Substring(2) != sha) throw Invalid("Source hash and id differ.");
            if (!DateTimeOffset.TryParseExact(String(json["expires_utc"]), "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out var expires)
                || expires.Offset != TimeSpan.Zero) throw Invalid("Invalid resource expiry.");
            Fresh(expires);
            string kindName = String(r["kind"]);
            if (kindName != "Image" && kindName != "Points" && kindName != "Acad") throw Invalid("Invalid resource kind.");
            var kind = (SketchImportResourceKind)Enum.Parse(typeof(SketchImportResourceKind), kindName);
            if (!(r["dependencies"] is JArray dependencies) || dependencies.Count != 0)
                throw Invalid("This catalog requires standalone resources; unresolved dependencies are unsupported.");
            SketchImportImageMetadata image = null; SketchImportGeometryMetadata geometry = null;
            if (!Null(r["image"]))
            {
                if (!(r["image"] is JObject i)) throw Invalid("Invalid image metadata.");
                Fields(i, "pixel_width", "pixel_height");
                long width = Integer(i["pixel_width"]), height = Integer(i["pixel_height"]);
                if (width < 1 || height < 1 || width > int.MaxValue || height > int.MaxValue) throw Invalid("Image dimensions are out of range.");
                image = new SketchImportImageMetadata((int)width, (int)height);
            }
            if (!Null(r["geometry"]))
            {
                if (!(r["geometry"] is JObject g)) throw Invalid("Invalid geometry metadata.");
                Fields(g, "unit", "millimeters_per_unit", "point_count", "bounds");
                SketchImportBounds bounds = null;
                if (!Null(g["bounds"]))
                {
                    if (!(g["bounds"] is JObject b)) throw Invalid("Invalid geometry bounds.");
                    Fields(b, "min_x", "min_y", "max_x", "max_y");
                    bounds = new SketchImportBounds(Number(b["min_x"]), Number(b["min_y"]), Number(b["max_x"]), Number(b["max_y"]));
                }
                geometry = new SketchImportGeometryMetadata(String(g["unit"]), Number(g["millimeters_per_unit"]), Null(g["point_count"]) ? (long?)null : Integer(g["point_count"]), bounds);
            }
            var resource = new SketchImportResource(id, kind, String(r["format"]), Integer(r["byte_length"]), image: image, geometry: geometry);
            try { SketchImportResourceValidator.Validate(new SketchImportResourceBundle(id, new[] { resource }), _policy.Metadata); }
            catch (ArgumentException ex) { throw new FormatException("Import metadata violates policy.", ex); }
            bool preview = Boolean(json["preview_available"]);
            if (preview && (kind != SketchImportResourceKind.Image || resource.Format != "bmp")) throw Invalid("Unsupported preview capability.");
            return new ImportResourceDescriptor(resource, sha, expires, preview);
        }
        private void Fresh(DateTimeOffset expires)
        { var remaining = expires - _clock(); if (remaining <= TimeSpan.Zero || remaining > _policy.MaxRemainingTtl) throw Invalid("Import resource is expired or its lifetime exceeds policy."); }
        private static void Envelope(JObject json)
        { if (!Boolean(json["ok"]) || String(json["storage"]) != "memory" || Boolean(json["cad_import_available"])) throw Invalid("Unsupported import envelope or CAD capability."); }
        private static bool Null(JToken t) => t == null || t.Type == JTokenType.Null;
        private static string String(JToken t) => t?.Type == JTokenType.String ? (string)t : throw Invalid("Expected string.");
        private static bool Boolean(JToken t) => t?.Type == JTokenType.Boolean ? (bool)t : throw Invalid("Expected boolean.");
        private static long Integer(JToken t)
        { if (t?.Type != JTokenType.Integer || !long.TryParse(t.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)) throw Invalid("Expected representable integer."); return number; }
        private static double Number(JToken t) => t?.Type == JTokenType.Float || t?.Type == JTokenType.Integer ? (double)t : throw Invalid("Expected number.");
        private static void Fields(JObject json, params string[] names)
        { if (json.Properties().Any(p => !names.Contains(p.Name, StringComparer.Ordinal))) throw Invalid("Unexpected import metadata field."); }
        private static string MediaType(string value) => value?.Split(';')[0].Trim().ToLowerInvariant();
        private static bool Hash(string value) => value != null && value.Length == 64 && value.All(c => c >= '0' && c <= '9' || c >= 'a' && c <= 'f');
        private static void RequireId(string id)
        { if (id == null || id.Length != 66 || !id.StartsWith("a_", StringComparison.Ordinal) || !Hash(id.Substring(2))) throw Invalid("Invalid import resource id."); }
        private static string Sha(byte[] bytes)
        { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
        private static FormatException Invalid(string message) => new FormatException(message);

        private static void ValidatePng(byte[] bytes, SketchImportImageMetadata image, CancellationToken ct)
        {
            byte[] signature = { 137, 80, 78, 71, 13, 10, 26, 10 };
            if (bytes.Length < 8 || !signature.SequenceEqual(bytes.Take(8))) throw Invalid("Invalid PNG signature.");
            int at = 8; bool header = false, data = false, end = false;
            while (at < bytes.Length)
            {
                ct.ThrowIfCancellationRequested();
                if (end || bytes.Length - at < 12) throw Invalid("Invalid PNG framing.");
                uint size = Big(bytes, at); if (size > (long)bytes.Length - at - 12) throw Invalid("Invalid PNG chunk range.");
                int count = (int)size; string kind = Encoding.ASCII.GetString(bytes, at + 4, 4);
                uint crc = uint.MaxValue;
                for (int i = at + 4; i < at + 8 + count; i++)
                { if ((i & 4095) == 0) ct.ThrowIfCancellationRequested(); crc ^= bytes[i]; for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0u : 0xedb88320u); }
                if ((crc ^ uint.MaxValue) != Big(bytes, at + 8 + count)) throw Invalid("PNG CRC mismatch.");
                if (kind == "IHDR")
                {
                    if (header || at != 8 || count != 13 || image == null || Big(bytes, at + 8) != image.PixelWidth || Big(bytes, at + 12) != image.PixelHeight
                        || bytes[at + 16] != 8 || bytes[at + 17] != 6 || bytes[at + 18] != 0 || bytes[at + 19] != 0 || bytes[at + 20] != 0) throw Invalid("PNG dimensions or encoding do not match metadata.");
                    header = true;
                }
                else if (kind == "IDAT") { if (!header || count == 0) throw Invalid("Invalid PNG data chunk."); data = true; }
                else if (kind == "IEND") { if (!header || !data || count != 0) throw Invalid("Invalid PNG end."); end = true; }
                else throw Invalid("Unsupported PNG chunk.");
                at += count + 12;
            }
            if (!end) throw Invalid("Incomplete PNG.");
        }
        private static uint Big(byte[] b, int at) => ((uint)b[at] << 24) | ((uint)b[at + 1] << 16) | ((uint)b[at + 2] << 8) | b[at + 3];
    }
}
