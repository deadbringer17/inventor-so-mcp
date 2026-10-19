using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;
using Bimwright.Ipt.Server;
using Bimwright.Ipt.Server.Imports;
using Inventor.So.Mcp.Http;
using Inventor.So.Mcp.Http.Imports;
using Inventor.So.Mcp.Http.Pairing;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Tests;

public sealed class BmpImportContentInspectorTests
{
    // These are actual BMP bytes, not inspector metadata stubs. Distinct BGR colors, row padding,
    // and a nonzero unused X byte exercise the 24/32-bit file layout without platform image APIs.
    private static byte[] Bmp(int width = 3, int height = 2, ushort bits = 24, bool explicitSize = true)
    {
        int stride = ((width * bits + 31) / 32) * 4;
        int pixelBytes = stride * Math.Abs(height);
        var bytes = new byte[54 + pixelBytes];
        W16(bytes, 0, 0x4d42); W32(bytes, 2, (uint)bytes.Length); W32(bytes, 10, 54);
        W32(bytes, 14, 40); W32(bytes, 18, (uint)width); W32(bytes, 22, unchecked((uint)height));
        W16(bytes, 26, 1); W16(bytes, 28, bits); W32(bytes, 34, explicitSize ? (uint)pixelBytes : 0);
        W32(bytes, 38, 3780); W32(bytes, 42, 3780);
        for (int i = 54; i < bytes.Length; i++) bytes[i] = (byte)((i * 13) & 255);
        return bytes;
    }
    private static void W16(byte[] b, int offset, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(offset, 2), value);
    private static void W32(byte[] b, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(offset, 4), value);
    private static BmpImportContentInspector Inspector() => new(10, 10, 100);

    [Theory]
    [InlineData(24, 2, true)] [InlineData(24, -2, false)]
    [InlineData(32, 2, false)] [InlineData(32, -2, true)]
    public async Task InspectsRealRgbFileWithoutMutation(int bits, int height, bool explicitSize)
    {
        byte[] bytes = Bmp(height: height, bits: (ushort)bits, explicitSize: explicitSize);
        byte[] original = (byte[])bytes.Clone();
        JObject metadata = await Inspector().InspectAsync("bmp", bytes, CancellationToken.None);
        Assert.Equal("Image", (string?)metadata["kind"]);
        Assert.Equal(3, (int)metadata["image"]!["pixel_width"]!);
        Assert.Equal(2, (int)metadata["image"]!["pixel_height"]!);
        Assert.Empty((JArray)metadata["dependencies"]!); Assert.Equal(JTokenType.Null, metadata["geometry"]!.Type);
        Assert.Equal(original, bytes);
        metadata["image"]!["pixel_width"] = 999;
        Assert.Equal(3, (int)(await Inspector().InspectAsync("bmp", bytes, CancellationToken.None))["image"]!["pixel_width"]!);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(13)] [InlineData(14)] [InlineData(39)] [InlineData(53)]
    public async Task RejectsIncompleteHeaders(int length)
    { await Assert.ThrowsAsync<ArgumentException>(() => Inspector().InspectAsync("bmp", Bmp().AsMemory(0, length), CancellationToken.None)); }

    [Theory]
    [InlineData(0, 0, 2)] // Signature.
    [InlineData(6, 1, 2)] [InlineData(8, 1, 2)] // Reserved fields.
    [InlineData(2, 54, 4)] // File size.
    [InlineData(10, 53, 4)] [InlineData(10, 55, 4)] [InlineData(10, 4294967295L, 4)]
    [InlineData(14, 12, 4)] [InlineData(14, 108, 4)] [InlineData(14, 124, 4)]
    [InlineData(18, 0, 4)] [InlineData(18, 4294967295L, 4)]
    [InlineData(22, 0, 4)] [InlineData(22, 2147483648L, 4)]
    [InlineData(26, 0, 2)] [InlineData(26, 2, 2)]
    [InlineData(28, 8, 2)] [InlineData(28, 16, 2)]
    [InlineData(30, 1, 4)] [InlineData(30, 3, 4)] [InlineData(30, 6, 4)]
    [InlineData(34, 23, 4)] [InlineData(34, 25, 4)]
    [InlineData(46, 1, 4)] [InlineData(50, 1, 4)]
    public async Task RejectsInvalidOrExplicitlyUnsupportedHeader(int offset, long value, int size)
    {
        byte[] bytes = Bmp();
        if (size == 2) W16(bytes, offset, (ushort)value); else W32(bytes, offset, (uint)value);
        await Assert.ThrowsAsync<ArgumentException>(() => Inspector().InspectAsync("bmp", bytes, CancellationToken.None));
    }

    [Theory]
    [InlineData(-1)] [InlineData(1)] [InlineData(-3)]
    public async Task RejectsMissingPixelOrPaddingBytesAndTrailingDataEvenWithMatchingFileSize(int delta)
    {
        byte[] bytes = Bmp(explicitSize: false); Array.Resize(ref bytes, bytes.Length + delta);
        W32(bytes, 2, (uint)bytes.Length);
        await Assert.ThrowsAsync<ArgumentException>(() => Inspector().InspectAsync("bmp", bytes, CancellationToken.None));
    }

    [Fact]
    public async Task EnforcesEachExplicitBudgetAndAcceptsExactBoundary()
    {
        byte[] bytes = Bmp();
        await new BmpImportContentInspector(3, 2, 6).InspectAsync("bmp", bytes, CancellationToken.None);
        foreach (var policy in new[] { new BmpImportContentInspector(2, 2, 6), new BmpImportContentInspector(3, 1, 6), new BmpImportContentInspector(3, 2, 5) })
            await Assert.ThrowsAsync<ArgumentException>(() => policy.InspectAsync("bmp", bytes, CancellationToken.None));
    }

    [Theory]
    [InlineData(0, 1, 1L)] [InlineData(1, 0, 1L)] [InlineData(1, 1, 0L)]
    [InlineData(-1, 1, 1L)] [InlineData(1, -1, 1L)] [InlineData(1, 1, -1L)]
    public void RequiresPositiveCallerBudgets(int width, int height, long pixels)
    { Assert.Throws<ArgumentOutOfRangeException>(() => new BmpImportContentInspector(width, height, pixels)); }

    [Fact]
    public async Task RejectsHugeArithmeticWithoutAllocatingPixelBufferOrWrappingStride()
    {
        var inspector = new BmpImportContentInspector(int.MaxValue, int.MaxValue, long.MaxValue);
        byte[] bytes = Bmp(bits: 32, explicitSize: false);
        W32(bytes, 18, int.MaxValue); W32(bytes, 22, int.MaxValue);
        await Assert.ThrowsAsync<ArgumentException>(() => inspector.InspectAsync("bmp", bytes, CancellationToken.None));
        // A width whose bit-count multiplication would wrap Int32 must also fail on file range.
        W32(bytes, 18, 134217729); W32(bytes, 22, 1);
        await Assert.ThrowsAsync<ArgumentException>(() => inspector.InspectAsync("bmp", bytes, CancellationToken.None));
    }

    [Theory]
    [InlineData("png")] [InlineData("BMP")] [InlineData("")] [InlineData(null)]
    public async Task RequiresExactDeclaredFormat(string? format)
    { await Assert.ThrowsAsync<ArgumentException>(() => Inspector().InspectAsync(format!, Bmp(), CancellationToken.None)); }

    [Fact]
    public async Task PreCancellationWinsEvenForMalformedBytes()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Inspector().InspectAsync("bmp", Bmp(), cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Inspector().InspectAsync("bmp", ReadOnlyMemory<byte>.Empty, cancellation.Token));
    }

    [Fact]
    public async Task RealInspectorWorksThroughOptInAuthenticatedHttpHost()
    {
        string root = Path.Combine(Path.GetTempPath(), "s1-bmp-http-" + Guid.NewGuid().ToString("N"));
        var config = new InventorMcpConfig { DescriptorDirectory = Path.Combine(root, "targets"), AssetDirectory = Path.Combine(root, "assets"),
            AuditDirectory = Path.Combine(root, "audit"), HttpRateLimitPerMinute = 1000 };
        config.HttpUrls.Clear(); config.HttpUrls.Add("http://127.0.0.1:0");
        var tokens = new TokenRegistry(); string token = TokenRegistry.Generate(); tokens.Add("bmp-owner", token);
        // Fixture-only budgets, not production defaults or performance claims.
        var storePolicy = new ImportResourceStorePolicy(1024, 2048, 2, 2048, 32, TimeSpan.FromMinutes(1), new[] { "bmp" });
        var options = new HostOptions { ImportResources = new ImportHttpPolicy(storePolicy, 1024, 2,
            new[] { new ImportHttpFormat("bmp", "Image") }, new BmpImportContentInspector(3, 2, 6)) };
        var app = HttpHost.Build(Array.Empty<string>(), config, tokens, options);
        try
        {
            await app.StartAsync();
            string address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
            using var client = new HttpClient { BaseAddress = new Uri(address) };
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            byte[] bytes = Bmp();
            using var upload = new ByteArrayContent(bytes); upload.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            using var response = await client.PostAsync("/imports/resources/bmp", upload);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            JObject body = JObject.Parse(await response.Content.ReadAsStringAsync());
            Assert.False((bool)body["cad_import_available"]!);
            Assert.Equal(3, (int)body["resource"]!["image"]!["pixel_width"]!);
            Assert.Equal(bytes, await client.GetByteArrayAsync("/imports/resources/" + (string?)body["resource"]!["asset_id"] + "/content"));
            byte[] invalid = Bmp(); W32(invalid, 30, 3);
            using var bad = new ByteArrayContent(invalid); bad.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            using var rejected = await client.PostAsync("/imports/resources/bmp", bad);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, rejected.StatusCode);
            Assert.Equal("IMPORT_CONTENT_INVALID", (string?)JObject.Parse(await rejected.Content.ReadAsStringAsync())["code"]);
            Assert.Single(app.Services.GetRequiredService<ImportResourceStore>().List("bmp-owner"));
        }
        finally
        {
            await app.StopAsync();
            // Dispose before deleting audit handles, also on an assertion failure.
            await app.DisposeAsync();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
