using System.Net;
using System.Net.Http.Headers;
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

public sealed class ImportResourceEndpointTests : IAsyncLifetime
{
    private sealed class FakeInspector : IImportContentInspector
    {
        internal int Calls;
        internal Func<string, ReadOnlyMemory<byte>, CancellationToken, Task<JObject>> Handler = (_, content, _) => Task.FromResult(new JObject
        { ["kind"] = "Image", ["dependencies"] = new JArray(), ["image"] = new JObject { ["pixel_width"] = content.Length, ["pixel_height"] = 1 }, ["geometry"] = null });
        public Task<JObject> InspectAsync(string format, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
        { Interlocked.Increment(ref Calls); return Handler(format, content, cancellationToken); }
    }
    private readonly string _root = Path.Combine(Path.GetTempPath(), "s1-import-http-" + Guid.NewGuid().ToString("N"));
    private readonly string _questToken = TokenRegistry.Generate(), _otherToken = TokenRegistry.Generate();
    private readonly FakeInspector _inspector = new();
    private WebApplication? _app;
    private string _base = "";
    private DateTimeOffset _now = DateTimeOffset.Parse("2026-10-11T00:00:00Z");
    private ImportResourceStore _store = null!;
    private async Task Start(bool enabled = true, int limit = 16, int records = 10, int rate = 1000, int listLimit = 2)
    {
        var config = new InventorMcpConfig { DescriptorDirectory = Path.Combine(_root, "targets"), AssetDirectory = Path.Combine(_root, "assets"),
            AuditDirectory = Path.Combine(_root, "audit"), HttpRateLimitPerMinute = rate };
        config.HttpUrls.Clear(); config.HttpUrls.Add("http://127.0.0.1:0");
        var tokens = new TokenRegistry(); tokens.Add("quest", _questToken); tokens.Add("other", _otherToken);
        var policy = new ImportResourceStorePolicy(limit, limit, records, 2048, 32, TimeSpan.FromMinutes(1), new[] { "png" });
        _store = new ImportResourceStore(policy, () => _now);
        var options = new HostOptions { ImportResources = enabled ? new ImportHttpPolicy(policy, limit, listLimit,
            new[] { new ImportHttpFormat("png", "Image") }, _inspector) : null,
            ConfigureServices = services => services.AddSingleton(_store) };
        _app = HttpHost.Build(Array.Empty<string>(), config, tokens, options); await _app.StartAsync();
        _base = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
    }
    private HttpClient Client(string? token)
    {
        var client = new HttpClient { BaseAddress = new Uri(_base) };
        if (token != null) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
    private static ByteArrayContent Content(params byte[] bytes)
    { var content = new ByteArrayContent(bytes); content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream"); return content; }
    private static async Task<JObject> Body(HttpResponseMessage response) => JObject.Parse(await response.Content.ReadAsStringAsync());
    private static async Task<string> Upload(HttpClient client, params byte[] bytes)
    {
        using var response = await client.PostAsync("/imports/resources/png", Content(bytes));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (string)(await Body(response))["resource"]!["asset_id"]!;
    }
    public Task InitializeAsync() => Task.CompletedTask;
    [Fact]
    public void HttpPolicyRejectsFormatsExcludedByTheStoreBeforeMappingRoutes()
    {
        var storePolicy = new ImportResourceStorePolicy(16, 32, 10, 2048, 32, TimeSpan.FromMinutes(1), new[] { "png" });
        Assert.True(storePolicy.IsFormatAllowed("png")); Assert.False(storePolicy.IsFormatAllowed("dwg"));
        Assert.Throws<ArgumentException>(() => new ImportHttpPolicy(storePolicy, 16, 2,
            new[] { new ImportHttpFormat("dwg", "Acad") }, _inspector));
        var compatible = new ImportHttpPolicy(storePolicy, 16, 2, new[] { new ImportHttpFormat("png", "Image") }, _inspector);
        Assert.Same(storePolicy, compatible.StorePolicy); Assert.Same(_inspector, compatible.Inspector);
    }
    public async Task DisposeAsync()
    {
        if (_app != null) { await _app.StopAsync(); await _app.DisposeAsync(); }
        try { Directory.Delete(_root, true); } catch { }
    }

    [Fact]
    public async Task NoPolicyMeansNoImportRoutes()
    {
        await Start(enabled: false); using var client = Client(_questToken);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/imports/resources")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync("/imports/resources/png", Content(1))).StatusCode);
    }
    [Theory]
    [InlineData(null)] [InlineData("invalid-token")]
    public async Task AllRoutesRequireBearerBeforeReadingOrInspection(string? token)
    {
        await Start(); using var client = Client(token);
        foreach (string route in new[] { "/imports/resources", "/imports/resources/a_bad", "/imports/resources/a_bad/content" })
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(route)).StatusCode);
        var response = await client.PostAsync("/imports/resources/png", Content(1));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("UNAUTHORIZED", (string?)(await Body(response))["error"]!["code"]); Assert.Equal(0, _inspector.Calls);
    }
    [Fact]
    public async Task UploadListDescribeAndContentUseOwnerAndCoreCompatibleMetadata()
    {
        await Start(); using var quest = Client(_questToken); using var other = Client(_otherToken);
        string id = await Upload(quest, 1, 2, 3);
        var description = await Body(await quest.GetAsync("/imports/resources/" + id));
        Assert.Equal("memory", (string?)description["storage"]); Assert.False((bool)description["cad_import_available"]!);
        Assert.Equal("verified_metadata", (string?)description["inspection_state"]);
        Assert.Equal("Image", (string?)description["resource"]!["kind"]); Assert.Equal(3, (int)description["resource"]!["byte_length"]!);
        Assert.Equal(3, (int)description["resource"]!["image"]!["pixel_width"]!);
        Assert.Empty((JArray)description["resource"]!["dependencies"]!);
        Assert.Single((JArray)(await Body(await quest.GetAsync("/imports/resources")))["resources"]!);
        Assert.Empty((JArray)(await Body(await other.GetAsync("/imports/resources")))["resources"]!);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync("/imports/resources/" + id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync("/imports/resources/" + id + "/content")).StatusCode);
        var downloaded = await quest.GetAsync("/imports/resources/" + id + "/content");
        Assert.Equal(new byte[] { 1, 2, 3 }, await downloaded.Content.ReadAsByteArrayAsync());
        Assert.Equal(id[2..], downloaded.Headers.GetValues("X-Content-SHA256").Single());
        Assert.True(downloaded.Headers.CacheControl!.NoStore);
    }
    [Fact]
    public async Task UploadReplayDeduplicatesBytesAndForeignSessionHeadersDoNotGrantAccess()
    {
        await Start(); using var quest = Client(_questToken); using var other = Client(_otherToken);
        string id = await Upload(quest, 1, 2); Assert.Equal(id, await Upload(quest, 1, 2)); Assert.Equal(2, _store.StoredBytes);
        other.DefaultRequestHeaders.Add("Mcp-Session-Id", "copied-quest-session");
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync("/imports/resources/" + id)).StatusCode);
        Assert.Equal(id, await Upload(other, 1, 2)); Assert.Equal(2, _store.StoredBytes);
        Assert.Single(_store.List("quest")); Assert.Single(_store.List("other"));
    }
    [Fact]
    public async Task RejectsDeclaredAndChunkedOversizeBeforeRegistration()
    {
        await Start(limit: 4); using var quest = Client(_questToken);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await quest.PostAsync("/imports/resources/png", Content(1, 2, 3, 4, 5))).StatusCode);
        var chunked = new StreamContent(new MemoryStream(new byte[] { 1, 2, 3, 4, 5 }));
        chunked.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        var response = await quest.PostAsync("/imports/resources/png", chunked);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal("IMPORT_TOO_LARGE", (string?)(await Body(response))["code"]); Assert.Equal(0, _inspector.Calls); Assert.Empty(_store.List("quest"));
        await Upload(quest, 1, 2, 3, 4);
    }
    [Fact]
    public async Task ExplicitImportLimitCanExceedTheGlobalMcpBodyLimit()
    {
        int length = checked((int)HttpHost.MaxRequestBodyBytes + 1);
        await Start(limit: length); using var quest = Client(_questToken);
        await Upload(quest, new byte[length]); Assert.Equal(length, _store.StoredBytes);
    }
    [Fact]
    public async Task EmptyWrongFormatAndQueryOwnerAreRefused()
    {
        await Start(); using var quest = Client(_questToken);
        Assert.Equal(HttpStatusCode.BadRequest, (await quest.PostAsync("/imports/resources/png", Content())).StatusCode);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await quest.PostAsync("/imports/resources/dwg", Content(1))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await quest.PostAsync("/imports/resources/png?owner=other", Content(1))).StatusCode);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await quest.PostAsync("/imports/resources/png", new StringContent("x"))).StatusCode);
        Assert.Equal(0, _inspector.Calls); Assert.Empty(_store.List("quest"));
    }
    [Fact]
    public async Task InspectorRejectionDoesNotRegisterAnyContentOrLeakInternalErrors()
    {
        await Start(); using var quest = Client(_questToken);
        _inspector.Handler = (_, _, _) => throw new ArgumentException("secret-path");
        var response = await quest.PostAsync("/imports/resources/png", Content(1));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.DoesNotContain("secret-path", await response.Content.ReadAsStringAsync()); Assert.Empty(_store.List("quest"));
        _inspector.Handler = (_, _, _) => throw new Exception("private-detail");
        response = await quest.PostAsync("/imports/resources/png", Content(1));
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.DoesNotContain("private-detail", await response.Content.ReadAsStringAsync());
    }
    [Theory]
    [InlineData("kind")] [InlineData("dimensions")] [InlineData("dependency")] [InlineData("path")]
    public async Task InvalidInspectedShapeCannotBecomeAVerifiedResource(string failure)
    {
        await Start(); using var quest = Client(_questToken);
        var normal = _inspector.Handler;
        _inspector.Handler = async (format, bytes, ct) =>
        {
            var metadata = await normal(format, bytes, ct);
            if (failure == "kind") metadata["kind"] = "Acad";
            if (failure == "dimensions") metadata["image"]!["pixel_width"] = 0;
            if (failure == "dependency") metadata["dependencies"] = new JArray("a_" + new string('0', 64));
            if (failure == "path") metadata["path"] = "C:\\secret";
            return metadata;
        };
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await quest.PostAsync("/imports/resources/png", Content(1))).StatusCode);
        Assert.Empty(_store.List("quest"));
    }
    [Fact]
    public async Task ListLimitAndOffsetAreExplicitAndExpiryRemainsOwnerScoped()
    {
        await Start(); using var quest = Client(_questToken);
        string id = await Upload(quest, 1); await Upload(quest, 2); await Upload(quest, 3);
        var first = await Body(await quest.GetAsync("/imports/resources")); Assert.Equal(2, ((JArray)first["resources"]!).Count);
        Assert.Equal(2, (int)first["next_offset"]!);
        var second = await Body(await quest.GetAsync("/imports/resources?offset=2")); Assert.Single((JArray)second["resources"]!);
        Assert.Equal(JTokenType.Null, second["next_offset"]!.Type);
        Assert.Equal(HttpStatusCode.BadRequest, (await quest.GetAsync("/imports/resources?offset=-1")).StatusCode);
        _now = _now.AddMinutes(1);
        Assert.Equal(HttpStatusCode.NotFound, (await quest.GetAsync("/imports/resources/" + id)).StatusCode);
        Assert.Empty((JArray)(await Body(await quest.GetAsync("/imports/resources")))["resources"]!);
    }
    [Fact]
    public async Task CapacityExhaustionIsStructuredAndRateLimitIsSharedWithHost()
    {
        await Start(limit: 2, rate: 4); using var quest = Client(_questToken);
        string id = await Upload(quest, 1, 2); using var lease = _store.Acquire("quest", new[] { id });
        var response = await quest.PostAsync("/imports/resources/png", Content(3, 4));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode); Assert.Equal("IMPORT_CAPACITY_EXHAUSTED", (string?)(await Body(response))["code"]);
        await quest.GetAsync("/imports/resources"); await quest.GetAsync("/imports/resources");
        response = await quest.GetAsync("/imports/resources"); Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal("RATE_LIMITED", (string?)(await Body(response))["error"]!["code"]);
    }
    [Fact]
    public async Task CanceledInspectionLeavesNoPartiallyRegisteredResource()
    {
        await Start(); using var quest = Client(_questToken);
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _inspector.Handler = async (_, _, ct) => { entered.TrySetResult(true); await Task.Delay(Timeout.Infinite, ct); return new JObject(); };
        using var cancellation = new CancellationTokenSource();
        var request = quest.PostAsync("/imports/resources/png", Content(1), cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await request);
        Assert.Empty(_store.List("quest")); Assert.Equal(0, _store.StoredBytes);
    }
}
