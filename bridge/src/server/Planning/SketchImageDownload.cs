using System;
using System.Buffers.Binary;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Bimwright.Ipt.Shared.Contracts;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Server.Planning;

/// <summary>Internal bounded native payload reader. Does not expose a tool or enable CAD import.</summary>
internal static class SketchImageDownload
{
    internal const int MaxImageBytes = 32 * 1024 * 1024;
    internal const int MaxChunkBytes = 192 * 1024;
    internal const int MaxEncodedCharacters = MaxChunkBytes / 3 * 4;

    internal static Task<Record> DownloadAsync(PluginClient client, string owner, string documentId,
        string revision, string sketchId, string imageId, CancellationToken ct = default)
        => DownloadCoreAsync(client, owner, documentId, revision, sketchId, imageId, "managed_original", ct);

    internal static Task<Record> DownloadNativeAsync(PluginClient client, string owner, string documentId,
        string revision, string sketchId, string imageId, CancellationToken ct = default)
        => DownloadCoreAsync(client, owner, documentId, revision, sketchId, imageId, "native_bitmap", ct);

    private static async Task<Record> DownloadCoreAsync(PluginClient client, string owner, string documentId,
        string revision, string sketchId, string imageId, string sourceMode, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(client);
        foreach (string value in new[] { owner, documentId, revision, sketchId, imageId })
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Image download identity is required.");
        ct.ThrowIfCancellationRequested();
        var target = client.CapturePinnedTarget();
        string session = Guid.NewGuid().ToString("N");
        string? transfer = null;
        byte[]? buffer = null;
        async Task<JObject> Send(string action, JObject arguments, CancellationToken token)
        {
            arguments["action"] = action; arguments["owner"] = owner; arguments["session"] = session;
            arguments["document_id"] = documentId; arguments["expected_revision"] = revision;
            arguments["sketch_id"] = sketchId; arguments["image_id"] = imageId;
            if (sourceMode == "native_bitmap") arguments["source_mode"] = sourceMode;
            return await client.SendPinnedAsync(target, "get_sketch_image_payload", arguments, token).ConfigureAwait(false) as JObject ?? throw Protocol();
        }
        try
        {
            JObject begin = await Send("begin", new JObject
            {
                ["document_id"] = documentId, ["expected_revision"] = revision,
                ["sketch_id"] = sketchId, ["image_id"] = imageId,
            }, ct).ConfigureAwait(false);
            transfer = TransferId(begin); // Journal before checking metadata; later failures must discard.
            // Older managed-original responders omit this optional discriminator. Native bitmap never does.
            if ((sourceMode == "native_bitmap" || begin["source_mode"] != null)
                && Text(begin, "source_mode") != sourceMode) throw Protocol();
            int length = Integer(begin, "byte_length", 54, MaxImageBytes);
            int width = Integer(begin, "pixel_width", 1, int.MaxValue);
            int height = Integer(begin, "pixel_height", 1, int.MaxValue);
            int chunkLimit = Integer(begin, "max_chunk_bytes", 1, MaxChunkBytes);
            string sha = Hash(begin);
            if (Text(begin, "format") != "bmp" || (((long)width * 24 + 31) / 32) * 4 > (length - 54L) / height) throw Protocol();
            JObject? frame = null;
            double widthMm = 0, heightMm = 0;
            bool visible = false, transparent = false;
            if (sourceMode == "native_bitmap")
            {
                widthMm = Positive(begin, "width_mm"); heightMm = Positive(begin, "height_mm");
                if (begin["visible"]?.Type != JTokenType.Boolean || begin["transparent"]?.Type != JTokenType.Boolean) throw Protocol();
                visible = (bool)begin["visible"]!; transparent = (bool)begin["transparent"]!;
                // The current native bitmap contract is opaque BGRX; no mask semantics are inferred.
                if (transparent) throw Protocol();
                frame = ValidateFrame(begin["image_frame"], widthMm, heightMm);
            }
            ct.ThrowIfCancellationRequested();
            buffer = new byte[length];
            for (int offset = 0; offset < length;)
            {
                ct.ThrowIfCancellationRequested();
                int count = Math.Min(chunkLimit, length - offset);
                JObject reply = await Send("chunk", new JObject
                { ["transfer_id"] = transfer, ["offset"] = offset, ["count"] = count }, ct).ConfigureAwait(false);
                if (TransferId(reply) != transfer || Hash(reply) != sha
                    || Integer(reply, "byte_length", 54, MaxImageBytes) != length
                    || Integer(reply, "offset", 0, MaxImageBytes) != offset) throw Protocol();
                string encoded = Text(reply, "data_base64");
                int expectedEncoded = ((count + 2) / 3) * 4;
                if (encoded.Length > MaxEncodedCharacters || encoded.Length != expectedEncoded) throw Protocol();
                byte[]? decoded = null;
                try
                {
                    try { decoded = Convert.FromBase64String(encoded); }
                    catch (FormatException) { throw Protocol(); }
                    if (decoded.Length != count || Convert.ToBase64String(decoded) != encoded) throw Protocol();
                    ct.ThrowIfCancellationRequested();
                    Buffer.BlockCopy(decoded, 0, buffer, offset, count);
                }
                finally { if (decoded != null) CryptographicOperations.ZeroMemory(decoded); }
                offset += count;
            }
            ct.ThrowIfCancellationRequested();
            if (Convert.ToHexString(SHA256.HashData(buffer)).ToLowerInvariant() != sha) throw Protocol();
            ValidateBmp(buffer, width, height);
            ct.ThrowIfCancellationRequested();
            var result = new Record(buffer, sha, width, height, sourceMode, target, frame, widthMm, heightMm, visible, transparent);
            buffer = null; // Transfer the sole full-buffer ownership to the result.
            return result;
        }
        finally
        {
            if (buffer != null) CryptographicOperations.ZeroMemory(buffer);
            if (transfer != null)
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                try { await Send("discard", new JObject { ["transfer_id"] = transfer }, cleanup.Token).ConfigureAwait(false); }
                catch (Exception) { /* Pinned cleanup is bounded; native TTL is the fallback. */ }
            }
        }
    }

    private static void ValidateBmp(byte[] bytes, int width, int height)
    {
        uint U32(int position) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(position, 4));
        ushort U16(int position) => BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(position, 2));
        int signedHeight = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(22, 4));
        int bits = U16(28);
        long stride = (((long)width * bits + 31) / 32) * 4;
        if (U16(0) != 0x4d42 || U32(2) != bytes.Length || U32(6) != 0 || U32(10) != 54 || U32(14) != 40
            || BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(18, 4)) != width
            || signedHeight == int.MinValue || Math.Abs(signedHeight) != height || U16(26) != 1 || bits is not (24 or 32)
            || U32(30) != 0 || U32(46) != 0 || U32(50) != 0 || stride > (bytes.Length - 54L) / height
            || stride * height != bytes.Length - 54L || (U32(34) != 0 && U32(34) != stride * height)) throw Protocol();
    }
    private static JObject ValidateFrame(JToken? token, double width, double height)
    {
        if (token is not JObject frame || frame.Count != 4 || Integer(frame, "schema", 1, 1) != 1
            || Text(frame, "provenance") != "native_boundary_roles"
            || frame["corner_order"] is not JArray order || order.Count != 4 || order.Any(p => p.Type != JTokenType.String)
            || !order.Values<string>().SequenceEqual(new[] { "top_left", "top_right", "bottom_right", "bottom_left" }, StringComparer.Ordinal)
            || frame["corners_mm"] is not JArray corners || corners.Count != 4) throw Protocol();
        var x = new double[4]; var y = new double[4];
        for (int i = 0; i < 4; i++)
        {
            if (corners[i] is not JArray point || point.Count != 2) throw Protocol();
            x[i] = Finite(point[0]); y[i] = Finite(point[1]);
        }
        double Length(double dx, double dy) => Math.Sqrt(dx * dx + dy * dy);
        bool Near(double actual, double expected) => double.IsFinite(actual) && Math.Abs(actual - expected) <= 1e-5;
        double tx = x[1] - x[0], ty = y[1] - y[0], lx = x[3] - x[0], ly = y[3] - y[0];
        if (!(Length(tx, ty) > 0) || !(Length(lx, ly) > 0) || !Near(Length(tx, ty), width) || !Near(Length(lx, ly), height)
            || !Near(Length(x[2] - x[3], y[2] - y[3]), width) || !Near(Length(x[2] - x[1], y[2] - y[1]), height)
            || !Near(Length(x[2] - x[1] - lx, y[2] - y[1] - ly), 0) || !Near((tx * lx + ty * ly) / width, 0)) throw Protocol();
        return (JObject)frame.DeepClone();
    }
    private static double Finite(JToken? token)
    {
        if (token == null || token.Type is not (JTokenType.Integer or JTokenType.Float)
            || !double.TryParse(token.ToString(Newtonsoft.Json.Formatting.None), NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            || !double.IsFinite(value)) throw Protocol();
        return value;
    }
    private static double Positive(JObject reply, string name)
    { double value = Finite(reply[name]); if (value <= 0) throw Protocol(); return value; }
    private static string Text(JObject reply, string field)
    { if (reply[field]?.Type != JTokenType.String) throw Protocol(); return (string)reply[field]!; }
    private static string TransferId(JObject reply)
    {
        string id = Text(reply, "transfer_id");
        if (id.Length is < 1 or > 128 || id.Any(c => !(c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_' or '-'))) throw Protocol();
        return id;
    }
    private static string Hash(JObject reply)
    {
        string hash = Text(reply, "sha256");
        if (hash.Length != 64 || hash.Any(c => !(c is >= '0' and <= '9' or >= 'a' and <= 'f'))) throw Protocol();
        return hash;
    }
    private static int Integer(JObject reply, string field, int minimum, int maximum)
    {
        JToken? value = reply[field];
        if (value?.Type != JTokenType.Integer || !int.TryParse(value.ToString(), NumberStyles.Integer,
            CultureInfo.InvariantCulture, out int number) || number < minimum || number > maximum) throw Protocol();
        return number;
    }
    private static InventorGatewayException Protocol() => new(InventorErrorCodes.API_ERROR, "Invalid native image payload response.");

    internal sealed class Record : IDisposable
    {
        private byte[]? _bytes;
        private readonly JObject? _frame;
        internal Record(byte[] bytes, string sha, int width, int height, string sourceKind, PluginClient.PinnedTarget target,
            JObject? frame, double widthMm, double heightMm, bool visible, bool transparent)
        {
            _bytes = bytes; Sha256 = sha; PixelWidth = width; PixelHeight = height; ByteLength = bytes.Length; SourceKind = sourceKind;
            Target = target; _frame = (JObject?)frame?.DeepClone(); WidthMm = widthMm; HeightMm = heightMm; Visible = visible; Transparent = transparent;
        }
        /// <summary>Owned bytes; use only while this result is alive. Dispose clears this array.</summary>
        internal byte[] Bytes => Volatile.Read(ref _bytes) ?? throw new ObjectDisposedException(nameof(Record));
        internal int ByteLength { get; }
        internal string Sha256 { get; }
        /// <summary>Requested and confirmed payload origin; does not describe alpha or transparency.</summary>
        internal string SourceKind { get; }
        internal PluginClient.PinnedTarget Target { get; }
        internal JObject? Frame => (JObject?)_frame?.DeepClone();
        // Physical dimensions and flags are meaningful for native bitmap results with a frame.
        internal double WidthMm { get; }
        internal double HeightMm { get; }
        internal bool Visible { get; }
        internal bool Transparent { get; }
        internal string AssetId => "a_" + Sha256;
        internal int PixelWidth { get; }
        internal int PixelHeight { get; }
        internal async Task RecheckRevisionAsync(PluginClient client, string documentId, string revision, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(client);
            if (string.IsNullOrWhiteSpace(documentId) || string.IsNullOrWhiteSpace(revision)) throw new ArgumentException("Document revision is required.");
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _bytes) == null, this);
            ct.ThrowIfCancellationRequested();
            var reply = await client.SendPinnedAsync(Target, "get_visual_revision", new JObject { ["document_id"] = documentId }, ct).ConfigureAwait(false) as JObject ?? throw Protocol();
            if (Text(reply, "document_id") != documentId) throw new InventorGatewayException(InventorErrorCodes.DOCUMENT_CHANGED, "Image document changed during encoding.");
            if (Text(reply, "revision") != revision) throw new InventorGatewayException(InventorErrorCodes.STALE_REVISION, "Image revision changed during encoding.");
            ct.ThrowIfCancellationRequested();
        }
        public void Dispose()
        { byte[]? bytes = Interlocked.Exchange(ref _bytes, null); if (bytes != null) CryptographicOperations.ZeroMemory(bytes); }
    }
}
