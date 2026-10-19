using System.Buffers.Binary;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Mcp;
using InventorXrSo.Core.Net;
using InventorXrSo.Core.Pairing;
using Newtonsoft.Json.Linq;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace InventorXrSo.Core.Tests.Backend;

public sealed class ImportResourceClientTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-12T00:00:00Z");
    private static readonly string SourceHash = new('a', 64), Id = "a_" + SourceHash;
    private static readonly PairedServer Server = new("127.0.0.1", 7443, new string('b', 64), "quest", "secret-bearer");
    private sealed class FakeTransport : IHttpTransport
    {
        internal readonly List<TransportRequest> Requests = new();
        internal Func<TransportRequest, CancellationToken, Task<TransportResponse>> Handler;
        public Task<TransportResponse> SendAsync(TransportRequest request, CancellationToken ct)
        { Requests.Add(request); return Handler(request, ct); }
        public Task<int> StreamLinesAsync(TransportRequest request, Action<string> onLine, CancellationToken ct) => throw new NotSupportedException();
    }
    private static ImportResourceClientPolicy Policy(long json = 4096, long png = 4096, int page = 2, int depth = 16)
        => new(new SketchImportResourcePolicy(4, 0, 66, 8192, 32768, 100, 100, 1000,
            new[] { new SketchImportFormat(SketchImportResourceKind.Image, "bmp"), new SketchImportFormat(SketchImportResourceKind.Points, "xyz"), new SketchImportFormat(SketchImportResourceKind.Acad, "dwg") },
            new[] { new KeyValuePair<string, double>("mm", 1) }), json, png, page, depth, TimeSpan.FromSeconds(4), TimeSpan.FromMinutes(2));
    private static JObject Envelope() => new()
    {
        ["ok"] = true, ["storage"] = "memory", ["cad_import_available"] = false, ["inspection_state"] = "verified_metadata",
        ["sha256"] = SourceHash, ["expires_utc"] = Now.AddMinutes(1).ToString("O"), ["preview_available"] = true,
        ["resource"] = new JObject { ["asset_id"] = Id, ["kind"] = "Image", ["format"] = "bmp", ["byte_length"] = 62,
            ["dependencies"] = new JArray(), ["image"] = new JObject { ["pixel_width"] = 1, ["pixel_height"] = 2 }, ["geometry"] = null }
    };
    private static TransportResponse Json(JObject json, int status = 200)
        => new(status, new Dictionary<string, string> { ["Content-Type"] = "application/json; charset=utf-8" }, Encoding.UTF8.GetBytes(json.ToString()));
    private static ImportResourceClient Client(FakeTransport transport, ImportResourceClientPolicy policy = null, Func<DateTimeOffset> clock = null)
        => new(transport, Server, policy ?? Policy(), clock ?? (() => Now));
    private static FakeTransport Replies(params TransportResponse[] responses)
    { var queue = new Queue<TransportResponse>(responses); return new FakeTransport { Handler = (_, _) => Task.FromResult(queue.Dequeue()) }; }
    private static JObject Page(params JObject[] rows) => new() { ["ok"] = true, ["storage"] = "memory", ["cad_import_available"] = false, ["resources"] = new JArray(rows), ["next_offset"] = null };

    [Fact]
    public async Task BackendFactoryListsAndDescribesUsingOnlyPairedRoutesAndExplicitBudgets()
    {
        var page = Page(Envelope()); page["next_offset"] = 5;
        var transport = Replies(Json(page), Json(Envelope()));
        var cache = new MemoryAssetCache(); var backend = new InventorBackend(transport, Server, cache);
        var client = backend.CreateImportResourceClient(Policy(), () => Now);
        var listed = await client.ListAsync(4, default); Assert.Equal(5, listed.NextOffset);
        var descriptor = await client.DescribeAsync(Id, default);
        Assert.Equal(Id, Assert.Single(listed.Resources).Resource.AssetId);
        Assert.Equal(SourceHash, descriptor.SourceSha256); Assert.Equal(Now.AddMinutes(1), descriptor.ExpiresUtc);
        Assert.True(descriptor.PreviewAvailable); Assert.False(descriptor.CadImportAvailable); Assert.False(listed.CadImportAvailable);
        Assert.Equal("https://127.0.0.1:7443/imports/resources?offset=4", transport.Requests[0].Url);
        Assert.Equal("https://127.0.0.1:7443/imports/resources/" + Id, transport.Requests[1].Url);
        Assert.All(transport.Requests, r => { Assert.Equal("GET", r.Method); Assert.Equal("Bearer secret-bearer", r.Headers["Authorization"]); Assert.Equal(4096, r.MaxResponseBytes); Assert.Equal(TimeSpan.FromSeconds(4), r.Timeout); });
        Assert.False(cache.TryGet(Id, out _));
    }

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("a_123")] [InlineData("https://evil.test/x")]
    [InlineData("../x")] [InlineData("a_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\n")]
    [InlineData("a_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public async Task RejectsMalformedIdsBeforeAnyAuthenticatedRequest(string id)
    {
        var transport = Replies(); var client = Client(transport);
        await Assert.ThrowsAsync<FormatException>(() => client.DescribeAsync(id, default)); Assert.Empty(transport.Requests);
    }

    [Theory]
    [InlineData("cad")] [InlineData("storage")] [InlineData("inspect")] [InlineData("hash")]
    [InlineData("kind")] [InlineData("format")] [InlineData("width")] [InlineData("pixels")]
    [InlineData("bytes")] [InlineData("geometry")] [InlineData("dependencies")] [InlineData("expired")]
    [InlineData("future")] [InlineData("boolean")] [InlineData("url")] [InlineData("id")]
    public async Task RejectsUntrustedOrInconsistentEnvelopes(string fault)
    {
        var e = Envelope(); var r = (JObject)e["resource"];
        switch (fault)
        {
            case "cad": e["cad_import_available"] = true; break;
            case "storage": e["storage"] = "disk"; break;
            case "inspect": e["inspection_state"] = "claimed"; break;
            case "hash": e["sha256"] = new string('b', 64); break;
            case "kind": r["kind"] = "image"; break;
            case "format": r["format"] = "png"; break;
            case "width": r["image"]["pixel_width"] = 0; break;
            case "pixels": r["image"]["pixel_width"] = 101; break;
            case "bytes": r["byte_length"] = 0; break;
            case "geometry": r["geometry"] = new JObject { ["unit"] = "mm", ["millimeters_per_unit"] = 1 }; break;
            case "dependencies": r["dependencies"] = new JArray(Id); break;
            case "expired": e["expires_utc"] = Now.ToString("O"); break;
            case "future": e["expires_utc"] = Now.AddHours(1).ToString("O"); break;
            case "boolean": e["preview_available"] = "true"; break;
            case "url": e["preview_url"] = "https://evil.test/steal"; break;
            case "id": r["asset_id"] = "a_" + new string('b', 64); e["sha256"] = new string('b', 64); break;
        }
        var transport = Replies(Json(e)); await Assert.ThrowsAsync<FormatException>(() => Client(transport).DescribeAsync(Id, default));
        Assert.Single(transport.Requests);
    }

    [Theory]
    [InlineData("Points", "xyz", true)] [InlineData("Acad", "dwg", false)]
    public async Task ParsesPolicyApprovedGeometryWithoutSuggestingCadOrPreview(string kind, string format, bool points)
    {
        var e = Envelope(); var r = (JObject)e["resource"]; e["preview_available"] = false;
        r["kind"] = kind; r["format"] = format; r["image"] = null;
        r["geometry"] = new JObject { ["unit"] = "mm", ["millimeters_per_unit"] = 1, ["point_count"] = points ? 3 : JValue.CreateNull(),
            ["bounds"] = new JObject { ["min_x"] = 0, ["min_y"] = 1, ["max_x"] = 2, ["max_y"] = 3 } };
        var transport = Replies(Json(e)); var client = Client(transport); var d = await client.DescribeAsync(Id, default);
        Assert.False(d.PreviewAvailable); Assert.False(d.CadImportAvailable); Assert.Equal("mm", d.Resource.Geometry.Unit);
        Assert.Equal("IMPORT_PREVIEW_UNAVAILABLE", (await Assert.ThrowsAsync<McpException>(() => client.DownloadPreviewAsync(d, default))).Code);
        Assert.Single(transport.Requests);
    }

    [Theory]
    [InlineData("duplicate")] [InlineData("overpage")] [InlineData("continuation")] [InlineData("empty-next")] [InlineData("missing-next")]
    public async Task RejectsInvalidPagination(string fault)
    {
        var page = Page(Envelope());
        if (fault == "duplicate") page["resources"] = new JArray(Envelope(), Envelope());
        if (fault == "overpage") page["resources"] = new JArray(Envelope(), Envelope(), Envelope());
        if (fault == "continuation") page["next_offset"] = 9;
        if (fault == "empty-next") { page["resources"] = new JArray(); page["next_offset"] = 1; }
        if (fault == "missing-next") page.Remove("next_offset");
        await Assert.ThrowsAsync<FormatException>(() => Client(Replies(Json(page))).ListAsync(0, default));
    }

    [Fact]
    public async Task EmptyCatalogAndInvalidOffsetAreHandledLocally()
    {
        var transport = Replies(Json(Page())); var client = Client(transport);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.ListAsync(-1, default)); Assert.Empty(transport.Requests);
        var page = await client.ListAsync(0, default); Assert.Empty(page.Resources); Assert.Null(page.NextOffset);
    }

    [Theory]
    [InlineData("duplicate-json")] [InlineData("utf8")] [InlineData("body-limit")] [InlineData("depth")]
    public async Task BoundsAndParsesJsonStrictlyEvenIfTransportIgnoresLimit(string fault)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(Envelope().ToString()); var policy = Policy();
        if (fault == "duplicate-json") bytes = Encoding.UTF8.GetBytes(Envelope().ToString().Replace("\"ok\": true", "\"ok\": true, \"ok\": true"));
        if (fault == "utf8") bytes[0] = 0xff;
        if (fault == "body-limit") policy = Policy(json: bytes.Length - 1);
        if (fault == "depth") policy = Policy(depth: 1);
        var response = new TransportResponse(200, new Dictionary<string, string> { ["Content-Type"] = "application/json" }, bytes);
        await Assert.ThrowsAsync<FormatException>(() => Client(Replies(response), policy).DescribeAsync(Id, default));
    }

    [Fact]
    public async Task DistinguishesAuthenticationStructuredErrorsAndCancellationWithoutReplay()
    {
        var denied = Replies(new TransportResponse(401, null, Array.Empty<byte>()));
        await Assert.ThrowsAsync<McpUnauthorizedException>(() => Client(denied).DescribeAsync(Id, default)); Assert.Single(denied.Requests);
        var error = new JObject { ["ok"] = false, ["code"] = "ASSET_NOT_FOUND", ["error"] = new JObject { ["code"] = "ASSET_NOT_FOUND", ["message"] = "private path secret" } };
        var ex = await Assert.ThrowsAsync<McpException>(() => Client(Replies(Json(error, 404))).DescribeAsync(Id, default));
        Assert.Equal("ASSET_NOT_FOUND", ex.Code); Assert.DoesNotContain("secret", ex.Message);
        using var cts = new CancellationTokenSource(); cts.Cancel(); var canceled = Replies();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Client(canceled).ListAsync(0, cts.Token)); Assert.Empty(canceled.Requests);
        var during = new FakeTransport { Handler = (_, token) => Task.FromCanceled<TransportResponse>(new CancellationToken(true)) };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Client(during).DescribeAsync(Id, default)); Assert.Single(during.Requests);
    }

    private static byte[] Png(int width = 1, int height = 2)
    {
        using var png = new MemoryStream(); png.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        void Chunk(string type, byte[] bytes)
        {
            byte[] t = Encoding.ASCII.GetBytes(type), field = new byte[4]; BinaryPrimitives.WriteUInt32BigEndian(field, (uint)bytes.Length); png.Write(field); png.Write(t); png.Write(bytes);
            uint crc = uint.MaxValue; foreach (byte b in t.Concat(bytes)) { crc ^= b; for (int bit = 0; bit < 8; bit++) crc = (crc & 1) == 0 ? crc >> 1 : (crc >> 1) ^ 0xedb88320u; }
            BinaryPrimitives.WriteUInt32BigEndian(field, crc ^ uint.MaxValue); png.Write(field);
        }
        byte[] header = new byte[13]; BinaryPrimitives.WriteInt32BigEndian(header, width); BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height); header[8] = 8; header[9] = 6; Chunk("IHDR", header);
        using var compressed = new MemoryStream(); using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, true)) zlib.Write(new byte[(width * 4 + 1) * height]);
        Chunk("IDAT", compressed.ToArray()); Chunk("IEND", Array.Empty<byte>()); return png.ToArray();
    }
    private static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static TransportResponse PngResponse(byte[] png, string source = null, string hash = null)
        => new(200, new Dictionary<string, string> { ["Content-Type"] = "image/png", ["X-Content-SHA256"] = hash ?? Sha(png), ["X-Source-SHA256"] = source ?? SourceHash }, png);

    [Fact]
    public async Task DownloadsPreviewWithDistinctHashesAndDefensiveBytesWithoutCache()
    {
        byte[] png = Png(); var transport = Replies(Json(Envelope()), PngResponse(png)); var client = Client(transport);
        var resource = await client.DescribeAsync(Id, default); var preview = await client.DownloadPreviewAsync(resource, default);
        Assert.Equal(png, preview.PngBytes); Assert.Equal(Sha(png), preview.PngSha256); Assert.Equal(SourceHash, preview.SourceSha256); Assert.NotEqual(preview.PngSha256, preview.SourceSha256);
        var first = preview.PngBytes; first[0] = 0; png[0] = 0; Assert.Equal(137, preview.PngBytes[0]);
        Assert.EndsWith("/" + Id + "/preview", transport.Requests[1].Url); Assert.Equal(4096, transport.Requests[1].MaxResponseBytes);
    }

    [Theory]
    [InlineData("hash")] [InlineData("source")] [InlineData("crc")] [InlineData("dimensions")]
    [InlineData("signature")] [InlineData("truncated")] [InlineData("trailer")] [InlineData("limit")] [InlineData("type")]
    public async Task RejectsTamperedOrUnboundedPngBeforeAnyUnityDecode(string fault)
    {
        byte[] png = Png(); var policy = Policy();
        if (fault == "crc") png[29] ^= 1;
        if (fault == "dimensions") png = Png(2, 2);
        if (fault == "signature") png[0] = 0;
        if (fault == "truncated") png = png.Take(png.Length - 1).ToArray();
        if (fault == "trailer") png = png.Concat(new byte[] { 0 }).ToArray();
        if (fault == "limit") policy = Policy(png: png.Length - 1);
        var response = PngResponse(png, fault == "source" ? new string('b', 64) : null, fault == "hash" ? new string('b', 64) : null);
        if (fault == "type") response = new TransportResponse(200, new Dictionary<string, string> { ["Content-Type"] = "image/jpeg" }, png);
        var client = Client(Replies(Json(Envelope()), response), policy); var descriptor = await client.DescribeAsync(Id, default);
        if (fault == "hash" || fault == "source") Assert.Equal("IMPORT_PREVIEW_CORRUPT", (await Assert.ThrowsAsync<McpException>(() => client.DownloadPreviewAsync(descriptor, default))).Code);
        else await Assert.ThrowsAsync<FormatException>(() => client.DownloadPreviewAsync(descriptor, default));
    }

    [Fact]
    public async Task ExpiredDescriptorDoesNotTriggerPreviewAndPolicyHasNoUnboundedCap()
    {
        var now = Now; var transport = Replies(Json(Envelope())); var client = Client(transport, clock: () => now);
        var descriptor = await client.DescribeAsync(Id, default); now = Now.AddMinutes(1);
        await Assert.ThrowsAsync<FormatException>(() => client.DownloadPreviewAsync(descriptor, default)); Assert.Single(transport.Requests);
        Assert.Throws<ArgumentException>(() => Policy(json: 0)); Assert.Throws<ArgumentException>(() => Policy(png: 0));
        Assert.Throws<ArgumentException>(() => Policy(json: (long)int.MaxValue + 1)); Assert.Throws<ArgumentException>(() => Policy(page: 0));
        Assert.Throws<ArgumentException>(() => Policy(depth: 0));
    }

    private sealed class LoopbackTransport : IHttpTransport, IDisposable
    {
        private readonly HttpClient _http = new();
        public async Task<TransportResponse> SendAsync(TransportRequest request, CancellationToken ct)
        {
            // This test-only adapter points the paired HTTPS model at the controlled HTTP loopback host.
            // Production pinning is owned by UnityHttpTransport and is not exercised by this test.
            var uri = new UriBuilder(request.Url) { Scheme = "http" }.Uri;
            using var message = new HttpRequestMessage(new HttpMethod(request.Method), uri);
            foreach (var header in request.Headers) message.Headers.TryAddWithoutValidation(header.Key, header.Value);
            using var response = await _http.SendAsync(message, ct);
            byte[] bytes = await response.Content.ReadAsByteArrayAsync(ct);
            Assert.True(bytes.LongLength <= request.MaxResponseBytes);
            var headers = response.Headers.Concat(response.Content.Headers).ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
            return new TransportResponse((int)response.StatusCode, headers, bytes);
        }
        public Task<int> StreamLinesAsync(TransportRequest request, Action<string> onLine, CancellationToken ct) => throw new NotSupportedException();
        public void Dispose() => _http.Dispose();
    }

    [Fact]
    public async Task RealHostCatalogDescriptorPreviewAndExpiryMatchTheCoreContract()
    {
        string root = Path.Combine(Path.GetTempPath(), "s1-core-import-client-" + Guid.NewGuid().ToString("N"));
        var now = Now;
        var config = new Bimwright.Ipt.Server.InventorMcpConfig { DescriptorDirectory = Path.Combine(root, "targets"), AssetDirectory = Path.Combine(root, "assets"), AuditDirectory = Path.Combine(root, "audit"), HttpRateLimitPerMinute = 1000 };
        config.HttpUrls.Clear(); config.HttpUrls.Add("http://127.0.0.1:0");
        var tokens = new Inventor.So.Mcp.Http.TokenRegistry(); string token = Inventor.So.Mcp.Http.TokenRegistry.Generate(); tokens.Add("owner", token);
        var storePolicy = new Bimwright.Ipt.Server.Imports.ImportResourceStorePolicy(128, 256, 2, 2048, 32, TimeSpan.FromMinutes(1), new[] { "bmp" });
        var store = new Bimwright.Ipt.Server.Imports.ImportResourceStore(storePolicy, () => now);
        var inspector = new Inventor.So.Mcp.Http.Imports.BmpImportContentInspector(1, 2, 2);
        var policy = new Inventor.So.Mcp.Http.Imports.ImportHttpPolicy(storePolicy, 128, 2,
            new[] { new Inventor.So.Mcp.Http.Imports.ImportHttpFormat("bmp", "Image") }, inspector,
            new Inventor.So.Mcp.Http.Imports.BmpImportPreviewRenderer(inspector, 4096, 16384));
        var options = new Inventor.So.Mcp.Http.Pairing.HostOptions { ImportResources = policy, ConfigureServices = services => services.AddSingleton(store) };
        var app = Inventor.So.Mcp.Http.HttpHost.Build(Array.Empty<string>(), config, tokens, options);
        try
        {
            await app.StartAsync();
            var address = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First());
            byte[] bmp = new byte[62];
            BinaryPrimitives.WriteUInt16LittleEndian(bmp, 0x4d42); BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(2), bmp.Length);
            BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(10), 54); BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(14), 40);
            BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(18), 1); BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(22), 2);
            BinaryPrimitives.WriteUInt16LittleEndian(bmp.AsSpan(26), 1); BinaryPrimitives.WriteUInt16LittleEndian(bmp.AsSpan(28), 24);
            bmp[54] = 255; bmp[60] = 255;
            using var http = new HttpClient { BaseAddress = address }; http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var content = new ByteArrayContent(bmp); content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            using var upload = await http.PostAsync("/imports/resources/bmp", content); Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
            string id = "a_" + Sha(bmp);
            using var transport = new LoopbackTransport();
            var paired = new PairedServer(address.Host, address.Port, new string('b', 64), "owner", token);
            var client = new InventorBackend(transport, paired, new MemoryAssetCache()).CreateImportResourceClient(Policy(), () => now);
            var page = await client.ListAsync(0, default); Assert.Equal(id, Assert.Single(page.Resources).Resource.AssetId);
            var descriptor = await client.DescribeAsync(id, default); Assert.Equal(Sha(bmp), descriptor.SourceSha256); Assert.False(descriptor.CadImportAvailable);
            var preview = await client.DownloadPreviewAsync(descriptor, default); Assert.Equal(Sha(preview.PngBytes), preview.PngSha256); Assert.Equal(Sha(bmp), preview.SourceSha256);
            Assert.Equal(1, BinaryPrimitives.ReadInt32BigEndian(preview.PngBytes.AsSpan(16, 4))); Assert.Equal(2, BinaryPrimitives.ReadInt32BigEndian(preview.PngBytes.AsSpan(20, 4)));
            now = now.AddMinutes(1);
            var expired = await Assert.ThrowsAsync<McpException>(() => client.DescribeAsync(id, default)); Assert.Equal("ASSET_NOT_FOUND", expired.Code);
            Assert.Empty((await client.ListAsync(0, default)).Resources);
        }
        finally
        {
            await app.StopAsync(); await app.DisposeAsync();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
