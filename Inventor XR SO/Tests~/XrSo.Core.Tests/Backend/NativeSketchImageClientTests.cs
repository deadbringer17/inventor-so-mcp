using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Mcp;
using InventorXrSo.Core.Net;
using InventorXrSo.Core.Pairing;
using Newtonsoft.Json.Linq;

namespace InventorXrSo.Core.Tests.Backend;

public sealed class NativeSketchImageClientTests
{
    private static readonly DocumentState State = new("doc_native", "rev_current", "visual_current");
    private static readonly PairedServer Server = new("127.0.0.1", 7443, new string('b', 64), "quest", "secret");
    private const string Sketch = "ent_sketch", Image = "ent_image";
    private sealed class Transport : IHttpTransport
    {
        internal readonly List<TransportRequest> Requests = new();
        internal Func<TransportRequest, CancellationToken, Task<TransportResponse>> Handler;
        public Task<TransportResponse> SendAsync(TransportRequest request, CancellationToken ct)
        { Requests.Add(request); return Handler(request, ct); }
        public Task<int> StreamLinesAsync(TransportRequest request, Action<string> onLine, CancellationToken ct) => throw new NotSupportedException();
    }
    private static byte[] Png(int width = 1, int height = 1)
    {
        using var output = new MemoryStream(); output.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        void Chunk(string type, byte[] data)
        {
            byte[] size = new byte[4]; BinaryPrimitives.WriteUInt32BigEndian(size, (uint)data.Length); output.Write(size);
            byte[] name = Encoding.ASCII.GetBytes(type); output.Write(name); output.Write(data);
            uint crc = uint.MaxValue;
            foreach (byte value in name.Concat(data)) { crc ^= value; for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0u : 0xedb88320u); }
            BinaryPrimitives.WriteUInt32BigEndian(size, crc ^ uint.MaxValue); output.Write(size);
        }
        byte[] header = new byte[13]; BinaryPrimitives.WriteUInt32BigEndian(header, (uint)width); BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), (uint)height);
        header[8] = 8; header[9] = 6; Chunk("IHDR", header);
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.NoCompression, true))
        {
            // Real RGBA scanlines with no-compression zlib make the large case exercise actual byte budgets,
            // rather than inflating a small PNG with unrelated padding or malformed IDAT data.
            byte[] row = new byte[width * 4 + 1];
            for (int x = 0; x < width; x++) { row[x * 4 + 1] = 255; row[x * 4 + 4] = 255; }
            for (int y = 0; y < height; y++) zlib.Write(row);
        }
        Chunk("IDAT", compressed.ToArray()); Chunk("IEND", Array.Empty<byte>()); return output.ToArray();
    }
    private static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static JObject Metadata(byte[] png)
    {
        string sha = Sha(png), id = "a_" + sha;
        return new JObject {
            ["document_id"] = State.DocumentId, ["revision"] = State.Revision, ["sketch_id"] = Sketch, ["image_id"] = Image,
            ["source_kind"] = "native_bitmap", ["pixel_width"] = 1, ["pixel_height"] = 1,
            ["width_mm"] = 30, ["height_mm"] = 20, ["visible"] = true, ["transparent"] = false,
            ["image_frame"] = new JObject { ["schema"] = 1, ["provenance"] = "native_boundary_roles",
                ["corner_order"] = new JArray("top_left", "top_right", "bottom_right", "bottom_left"),
                ["corners_mm"] = new JArray(new JArray(13, 21), new JArray(43, 21), new JArray(43, 1), new JArray(13, 1)) },
            ["asset"] = new JObject { ["asset_id"] = id, ["sha256"] = sha, ["mime_type"] = "image/png", ["size_bytes"] = png.Length, ["asset_url"] = "/assets/" + id } };
    }
    private static Transport Replies(byte[] png, int status = 200, string mime = "image/png") => new() {
        Handler = (_, _) => Task.FromResult(new TransportResponse(status, new Dictionary<string, string> { ["Content-Type"] = mime }, (byte[])png.Clone())) };
    private static Task<NativeSketchImageAsset> Download(Transport t, JObject metadata, CancellationToken ct = default)
        => new NativeSketchImageClient(t, Server).DownloadAsync(metadata, State, Sketch, Image, ct);

    [Fact]
    public async Task DownloadsBoundedPairedAssetAndReturnsIndependentImmutableCallerOwnedResult()
    {
        byte[] png = Png(); var metadata = Metadata(png); var t = Replies(png);
        using var result = await Download(t, metadata);
        var request = Assert.Single(t.Requests);
        Assert.Equal("https://127.0.0.1:7443/assets/" + result.AssetId, request.Url);
        Assert.Equal(png.Length, request.MaxResponseBytes); Assert.Equal("Bearer secret", request.Headers["Authorization"]);
        Assert.Equal(png, result.CopyPngBytes()); Assert.Equal("native_bitmap", result.SourceKind); Assert.Equal("native_boundary_roles", result.Provenance);
        metadata["image_frame"]!["corners_mm"]![0]![0] = 999;
        Assert.Equal(13, result.CornersMm[0].X);
        Assert.Throws<NotSupportedException>(() => ((IList<CadPoint>)result.CornersMm)[0] = new CadPoint(999, 999));
        var first = result.CopyPngBytes(); first[0] = 0; Assert.Equal(137, result.CopyPngBytes()[0]);
        Assert.True(result.Matches(State, Sketch, Image)); Assert.False(result.Matches(new DocumentState(State.DocumentId, "new", "v"), Sketch, Image));
        result.Dispose(); result.Dispose(); Assert.Throws<ObjectDisposedException>(() => result.CopyPngBytes());
    }

    [Theory]
    [InlineData("document")] [InlineData("revision")] [InlineData("sketch")] [InlineData("image")]
    [InlineData("source")] [InlineData("transparent")] [InlineData("mime")] [InlineData("hash")]
    [InlineData("id")] [InlineData("size")] [InlineData("oversize")] [InlineData("pixels")] [InlineData("dimension")]
    [InlineData("order")] [InlineData("frame")] [InlineData("skew")] [InlineData("width")] [InlineData("nan")]
    public async Task RejectsUnboundOrMalformedMetadataBeforeAuthenticatedDownload(string fault)
    {
        byte[] png = Png(); var m = Metadata(png); var t = Replies(png);
        switch (fault)
        {
            case "document": m["document_id"] = "other"; break;
            case "revision": m["revision"] = "old"; break;
            case "sketch": m["sketch_id"] = "ent_other"; break;
            case "image": m["image_id"] = "ent_other"; break;
            case "source": m["source_kind"] = "managed_original"; break;
            case "transparent": m["transparent"] = true; break;
            case "mime": m["asset"]!["mime_type"] = "image/jpeg"; break;
            case "hash": m["asset"]!["sha256"] = new string('A', 64); break;
            case "id": m["asset"]!["asset_id"] = "a_" + new string('a', 64); break;
            case "size": m["asset"]!["size_bytes"] = 0; break;
            case "oversize": m["asset"]!["size_bytes"] = NativeSketchImageClient.MaxImageBytes + 1; break;
            case "pixels": m["pixel_width"] = 8192; m["pixel_height"] = 8192; break;
            case "dimension": m["pixel_width"] = 8193; break;
            case "order": m["image_frame"]!["corner_order"]![0] = "bottom_left"; break;
            case "frame": m["image_frame"]!["provenance"] = "guessed"; break;
            case "skew": m["image_frame"]!["corners_mm"]![2]![0] = 42; break;
            case "width": m["width_mm"] = 0; break;
            case "nan": m["width_mm"] = double.NaN; break;
        }
        await Assert.ThrowsAsync<FormatException>(() => Download(t, m)); Assert.Empty(t.Requests);
    }

    [Theory]
    [InlineData("https://evil.test:7443")] [InlineData("https://127.0.0.1:7444")] [InlineData("http://127.0.0.1:7443")]
    [InlineData("//127.0.0.1:7443")] [InlineData("https://user@127.0.0.1:7443")]
    [InlineData("query")] [InlineData("fragment")] [InlineData("escape")]
    public async Task RejectsForeignOrNoncanonicalUrlsWithoutForwardingBearer(string prefix)
    {
        byte[] png = Png(); var m = Metadata(png); string path = (string)m["asset"]!["asset_url"]; var t = Replies(png);
        m["asset"]!["asset_url"] = prefix == "query" ? path + "?token=x" : prefix == "fragment" ? path + "#x"
            : prefix == "escape" ? path.Replace("/assets/", "/assets/../assets/") : prefix + path;
        await Assert.ThrowsAsync<FormatException>(() => Download(t, m)); Assert.Empty(t.Requests);
    }

    [Fact]
    public async Task AllowsExactSameOriginAbsoluteUrl()
    {
        byte[] png = Png(); var m = Metadata(png); m["asset"]!["asset_url"] = "https://127.0.0.1:7443" + (string)m["asset"]!["asset_url"];
        using var result = await Download(Replies(png), m); Assert.Equal(png, result.CopyPngBytes());
    }

    [Fact]
    public async Task RejectsHashMismatchAndClearsDownloadedResponseBuffer()
    {
        byte[] png = Png(), body = (byte[])png.Clone(); body[^1] ^= 1;
        var t = new Transport { Handler = (_, _) => Task.FromResult(new TransportResponse(200, new Dictionary<string, string> { ["Content-Type"] = "image/png" }, body)) };
        var error = await Assert.ThrowsAsync<McpException>(() => Download(t, Metadata(png))); Assert.Equal("ASSET_CORRUPT", error.Code);
        Assert.All(body, b => Assert.Equal(0, b));
    }

    [Fact]
    public async Task ChecksPngDimensionsAndCrcEvenWhenContentHashIsValid()
    {
        byte[] png = Png(); png[^1] ^= 1; var m = Metadata(png);
        await Assert.ThrowsAsync<FormatException>(() => Download(Replies(png), m));
        png = Png(); m = Metadata(png); m["pixel_width"] = 2;
        await Assert.ThrowsAsync<FormatException>(() => Download(Replies(png), m));
    }

    [Fact]
    public async Task RejectsOversizedBodyEvenWhenTransportIgnoresLimit()
    {
        byte[] png = Png(); var m = Metadata(png);
        await Assert.ThrowsAsync<FormatException>(() => Download(Replies(png.Concat(new byte[] { 0 }).ToArray()), m));
    }

    [Fact]
    public async Task CancellationBeforeAndAfterTransportNeverPublishesAssetAndClearsBody()
    {
        byte[] png = Png(); var t = Replies(png); using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Download(t, Metadata(png), cts.Token)); Assert.Empty(t.Requests);
        using var later = new CancellationTokenSource(); byte[] body = (byte[])png.Clone();
        t.Handler = (_, _) => { later.Cancel(); return Task.FromResult(new TransportResponse(200, new Dictionary<string, string> { ["Content-Type"] = "image/png" }, body)); };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Download(t, Metadata(png), later.Token)); Assert.All(body, b => Assert.Equal(0, b));
    }

    private sealed class CallerContext : SynchronizationContext
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<Action> _callbacks = new();
        public override void Post(SendOrPostCallback callback, object state) => _callbacks.Enqueue(() => callback(state));
        internal void PumpUntil(Task task)
        {
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (!task.IsCompleted && DateTime.UtcNow < deadline)
            {
                if (_callbacks.TryDequeue(out var callback)) callback();
                else Thread.Sleep(1);
            }
            Assert.True(task.IsCompleted, "Caller-context operation did not finish.");
        }
    }

    // A bounded, explicitly pumped caller context is the behavior under test here.
    // Awaiting these tests normally would remove the caller-thread cancellation/response proof.
#pragma warning disable xUnit1031
    [Fact]
    public void LargeValidPngLetsCallerHandleWorkBeforeCompletionAndResumesOnCallerContext()
    {
        byte[] png = Png(1024, 2048); Assert.True(png.Length > 8 * 1024 * 1024);
        var metadata = Metadata(png); metadata["pixel_width"] = 1024; metadata["pixel_height"] = 2048;
        var transport = Replies(png); var previous = SynchronizationContext.Current; var context = new CallerContext();
        Task<NativeSketchImageAsset> operation = null;
        try
        {
            SynchronizationContext.SetSynchronizationContext(context);
            operation = Download(transport, metadata);
            // With an already-completed HTTP reply, callers must still regain control while validating
            // the large bitmap. In Unity this is where another frame/input callback can run.
            Assert.False(operation.IsCompleted, "Large bitmap validation blocked the caller through completion.");
            context.PumpUntil(operation);
            using var asset = operation.GetAwaiter().GetResult();
            Assert.Equal(Sha(png), asset.Sha256); Assert.Equal(png, asset.CopyPngBytes());
        }
        finally
        {
            // Always drain ownership even if the responsiveness assertion fails.
            if (operation != null && !operation.IsCompleted) context.PumpUntil(operation);
            if (operation?.Status == TaskStatus.RanToCompletion) operation.Result.Dispose();
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    [Fact]
    public void CallerCanCancelLargeValidationWithoutWaitingForPngScanAndResponseIsCleared()
    {
        byte[] png = Png(1024, 2048), body = (byte[])png.Clone();
        var metadata = Metadata(png); metadata["pixel_width"] = 1024; metadata["pixel_height"] = 2048;
        var transport = new Transport { Handler = (_, _) => Task.FromResult(new TransportResponse(200,
            new Dictionary<string, string> { ["Content-Type"] = "image/png" }, body)) };
        using var cancel = new CancellationTokenSource();
        var previous = SynchronizationContext.Current; var context = new CallerContext(); Task<NativeSketchImageAsset> operation = null;
        try
        {
            SynchronizationContext.SetSynchronizationContext(context);
            operation = Download(transport, metadata, cancel.Token);
            Assert.False(operation.IsCompleted, "Caller could not cancel until validation had already finished.");
            cancel.Cancel(); context.PumpUntil(operation);
            Assert.ThrowsAny<OperationCanceledException>(() => operation.GetAwaiter().GetResult());
            Assert.True(body.All(b => b == 0), "Cancelled validation retained the transport body.");
        }
        finally
        {
            cancel.Cancel();
            if (operation != null && !operation.IsCompleted) context.PumpUntil(operation);
            if (operation?.Status == TaskStatus.RanToCompletion) operation.Result.Dispose();
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }
#pragma warning restore xUnit1031
}
