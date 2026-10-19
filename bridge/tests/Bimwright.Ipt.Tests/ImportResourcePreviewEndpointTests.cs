using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Bimwright.Ipt.Server;
using Bimwright.Ipt.Server.Imports;
using Inventor.So.Mcp.Http;
using Inventor.So.Mcp.Http.Imports;
using Inventor.So.Mcp.Http.Pairing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Tests;

public sealed class ImportResourcePreviewEndpointTests : IAsyncLifetime
{
    private readonly string _root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "s1-preview-" + Guid.NewGuid().ToString("N")));
    private readonly string _token = TokenRegistry.Generate(), _foreign = TokenRegistry.Generate();
    private WebApplication? _app;
    private string _base = "";
    private DateTimeOffset _now = DateTimeOffset.Parse("2026-10-12T00:00:00Z");
    private ImportResourceStore _store = null!;
    private async Task Start(bool renderer = true, int outputBudget = 1024)
    {
        var config = new InventorMcpConfig { DescriptorDirectory = Path.Combine(_root, "targets"),
            AssetDirectory = Path.Combine(_root, "assets"), AuditDirectory = Path.Combine(_root, "audit"), HttpRateLimitPerMinute = 1000 };
        config.HttpUrls.Clear(); config.HttpUrls.Add("http://127.0.0.1:0");
        var tokens = new TokenRegistry(); tokens.Add("quest", _token); tokens.Add("foreign", _foreign);
        var storePolicy = new ImportResourceStorePolicy(1024, 4096, 8, 2048, 8, TimeSpan.FromMinutes(1), new[] { "bmp" });
        _store = new ImportResourceStore(storePolicy, () => _now);
        var inspector = new BmpImportContentInspector(10, 10, 100);
        var policy = new ImportHttpPolicy(storePolicy, 1024, 8, new[] { new ImportHttpFormat("bmp", "Image") }, inspector,
            renderer ? new BmpImportPreviewRenderer(inspector, outputBudget, 16384) : null);
        _app = HttpHost.Build(Array.Empty<string>(), config, tokens, new HostOptions {
            ImportResources = policy, ConfigureServices = services => services.AddSingleton(_store) });
        await _app.StartAsync();
        _base = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
    }
    private HttpClient Client(string? token)
    {
        var client = new HttpClient { BaseAddress = new Uri(_base) };
        if (token != null) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
    private static byte[] Bmp()
    {
        byte[] bytes = new byte[70];
        void U16(int offset, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset, 2), value);
        void U32(int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset, 4), value);
        U16(0, 0x4d42); U32(2, 70); U32(10, 54); U32(14, 40); U32(18, 2); U32(22, 2); U16(26, 1); U16(28, 24); U32(34, 16);
        for (int i = 54; i < bytes.Length; i++) bytes[i] = (byte)(i * 3);
        return bytes;
    }
    private static Task<JObject> Body(HttpResponseMessage response) => Parse(response);
    private static async Task<JObject> Parse(HttpResponseMessage response) => JObject.Parse(await response.Content.ReadAsStringAsync());
    private static async Task<string> Upload(HttpClient client)
    {
        using var content = new ByteArrayContent(Bmp()); content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using var response = await client.PostAsync("/imports/resources/bmp", content);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (string)(await Body(response))["resource"]!["asset_id"]!;
    }
    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync()
    {
        if (_app != null) { await _app.StopAsync(); await _app.DisposeAsync(); }
        string temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!_root.StartsWith(temp, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(_root).StartsWith("s1-preview-", StringComparison.Ordinal))
            throw new InvalidOperationException("Refusing cleanup outside the owned preview fixture directory.");
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
    [Theory]
    [InlineData(null)] [InlineData("invalid")]
    public async Task PreviewRequiresAuthentication(string? token)
    {
        await Start(); using var client = Client(token);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/imports/resources/a_bad/preview")).StatusCode);
    }
    [Fact]
    public async Task PreviewIsPngWithSeparateHashesAndLeavesOriginalUnchanged()
    {
        await Start(); using var client = Client(_token); string id = await Upload(client);
        var descriptor = await Body(await client.GetAsync("/imports/resources/" + id));
        Assert.True((bool)descriptor["preview_available"]!); Assert.False((bool)descriptor["cad_import_available"]!);
        var catalog = await Body(await client.GetAsync("/imports/resources"));
        Assert.True((bool)catalog["resources"]![0]!["preview_available"]!);
        using var response = await client.GetAsync("/imports/resources/" + id + "/preview");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.Equal("image/png", response.Content.Headers.ContentType!.MediaType);
        Assert.True(response.Headers.CacheControl!.NoStore);
        byte[] png = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(new byte[] {137,80,78,71,13,10,26,10}, png.Take(8));
        Assert.Equal(Convert.ToHexString(SHA256.HashData(png)).ToLowerInvariant(), response.Headers.GetValues("X-Content-SHA256").Single());
        Assert.Equal(id[2..], response.Headers.GetValues("X-Source-SHA256").Single());
        Assert.Equal(Bmp(), await client.GetByteArrayAsync("/imports/resources/" + id + "/content"));
        using var again = await client.GetAsync("/imports/resources/" + id + "/preview");
        Assert.Equal(png, await again.Content.ReadAsByteArrayAsync());
    }
    [Fact]
    public async Task UnknownForeignAndExpiredAreIndistinguishable()
    {
        await Start(); using var owner = Client(_token); using var foreign = Client(_foreign); string id = await Upload(owner);
        var denied = await Body(await foreign.GetAsync("/imports/resources/" + id + "/preview"));
        using var missing = await owner.GetAsync("/imports/resources/a_" + new string('0',64) + "/preview");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode); Assert.True(JToken.DeepEquals(denied, await Body(missing)));
        _now += TimeSpan.FromMinutes(2);
        using var expired = await owner.GetAsync("/imports/resources/" + id + "/preview");
        Assert.Equal(HttpStatusCode.NotFound, expired.StatusCode); Assert.True(JToken.DeepEquals(denied, await Body(expired)));
    }
    [Fact]
    public async Task MissingRendererIsExplicitAndDoesNotDisableOriginalContent()
    {
        await Start(renderer: false); using var client = Client(_token); string id = await Upload(client);
        Assert.False((bool)(await Body(await client.GetAsync("/imports/resources/" + id)))["preview_available"]!);
        using var response = await client.GetAsync("/imports/resources/" + id + "/preview");
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Equal("IMPORT_PREVIEW_UNAVAILABLE", (string?)(await Body(response))["code"]);
        Assert.Equal(Bmp(), await client.GetByteArrayAsync("/imports/resources/" + id + "/content"));
    }
    [Fact]
    public async Task OutputBudgetFailurePreservesResourceAndDoesNotLeakPartialPng()
    {
        await Start(outputBudget: 60); using var client = Client(_token); string id = await Upload(client);
        using var response = await client.GetAsync("/imports/resources/" + id + "/preview");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("IMPORT_PREVIEW_INVALID", (string?)(await Body(response))["code"]);
        Assert.Equal(Bmp(), await client.GetByteArrayAsync("/imports/resources/" + id + "/content"));
    }
    [Fact]
    public async Task QueryCannotSelectOwnerOrAlternativePath()
    {
        await Start(); using var client = Client(_token); string id = await Upload(client);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/imports/resources/" + id + "/preview?owner=foreign")).StatusCode);
    }
    [Fact]
    public void PreviewPolicyMustUseSameInspectorAndImageFormat()
    {
        var store = new ImportResourceStorePolicy(1024,4096,8,2048,8,TimeSpan.FromMinutes(1),new[]{"bmp"});
        var one = new BmpImportContentInspector(10,10,100); var two = new BmpImportContentInspector(10,10,100);
        var renderer = new BmpImportPreviewRenderer(one,1024,16384);
        Assert.Throws<ArgumentException>(() => new ImportHttpPolicy(store,1024,8,new[]{new ImportHttpFormat("bmp","Image")},two,renderer));
        Assert.Throws<ArgumentException>(() => new ImportHttpPolicy(store,1024,8,new[]{new ImportHttpFormat("bmp","Points")},one,renderer));
    }
}
