using InventorXrSo.Core.Mcp;
using Newtonsoft.Json.Linq;
using Bimwright.Ipt.Tests;
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Glb;
using InventorXrSo.Core.Tests.Support;

namespace InventorXrSo.Core.Tests.Backend;

public class InventorBackendTests : IClassFixture<BackendFixture>
{
    private readonly BackendFixture _f;
    public InventorBackendTests(BackendFixture fixture) { _f = fixture; }

    private async Task<InventorBackend> Connected(IAssetCache cache = null)
    {
        var backend = new InventorBackend(_f.Transport(), _f.EditorServer, cache ?? new MemoryAssetCache());
        await backend.ConnectAsync(CancellationToken.None);
        return backend;
    }

    [Fact]
    public async Task M1FlowAgainstTheFakeAddIn()
    {
        var ct = CancellationToken.None;
        var backend = await Connected();
        Assert.True((await backend.GetCapabilitiesAsync(ct)).IsXrReady);

        var state = await backend.GetDocumentStateAsync(ct);
        Assert.Equal(FakeAddIn.AssemblyId, state.DocumentId);

        var scene = await backend.GetSceneGraphAsync(ct);
        Assert.Equal("assembly", scene.Kind);
        Assert.Equal(new[] { FakeAddIn.BoltId, FakeAddIn.PlateId }.OrderBy(x => x), scene.DefinitionIds.OrderBy(x => x));
        Assert.Equal(3, scene.PlacedParts().Count());

        var mesh = await backend.GetDefinitionMeshAsync(FakeAddIn.BoltId, ct);
        var model = GlbModel.Parse(await backend.GetAssetAsync(mesh, ct));
        var face = model.Primitives[0].FaceMap.FaceAtTriangle(4).FaceId;

        var entity = await backend.PickFaceAsync("ent_occ_2", face, ct);
        Assert.Equal("ent_proxy_" + face, entity);
        await backend.HighlightAsync(new[] { entity }, ct);
        await backend.ClearHighlightAsync(ct);
        Assert.Contains("highlight_entity", _f.AddIn.Commands);
    }

    [Fact]
    public async Task ACachedAssetIsNotDownloadedAgain()
    {
        var cache = new CountingCache();
        var backend = await Connected(cache);
        var mesh = await backend.GetDefinitionMeshAsync(FakeAddIn.PlateId, CancellationToken.None);
        var first = await backend.GetAssetAsync(mesh, CancellationToken.None);
        var second = await backend.GetAssetAsync(mesh, CancellationToken.None);
        Assert.Equal(first, second);
        Assert.Equal(1, cache.Puts);
    }

    [Fact]
    public void ADefinitionMeshWithoutAnAssetIsRefused()
    {
        foreach (var json in new[] { new JObject(), new JObject { ["asset"] = new JObject { ["asset_id"] = "a_" + new string('0', 64) } }, new JObject { ["asset"] = new JObject { ["asset_url"] = "/assets/x" } } })
            Assert.Equal("ASSET_MISSING", Assert.Throws<McpException>(() => DefinitionMesh.FromJson("d", json)).Code);
    }

    [Theory]
    [InlineData("not-an-id", "/assets/not-an-id")]
    [InlineData("a_0000000000000000000000000000000000000000000000000000000000000000", "http://evil.example/assets/x")]
    [InlineData("a_0000000000000000000000000000000000000000000000000000000000000000", "//evil.example/assets/x")]
    public async Task AnInvalidAssetIdOrForeignUrlIsRefusedBeforeAnyRequest(string id, string url)
    {
        var backend = await Connected(new CountingCache());
        var mesh = DefinitionMesh.FromJson("d", new JObject { ["asset"] = new JObject { ["asset_id"] = id, ["asset_url"] = url } });
        var ex = await Assert.ThrowsAsync<McpException>(() => backend.GetAssetAsync(mesh, CancellationToken.None));
        Assert.Equal("ASSET_MISSING", ex.Code);
    }

    [Fact]
    public async Task APublicUrlOnAnotherHostIsFetchedFromThePairedHost()
    {
        var backend = await Connected(new CountingCache());
        var mesh = await backend.GetDefinitionMeshAsync(FakeAddIn.PlateId, CancellationToken.None);
        var path = mesh.AssetUrl.StartsWith("https://") ? new Uri(mesh.AssetUrl).AbsolutePath : "/" + mesh.AssetUrl.TrimStart('/');
        var foreign = DefinitionMesh.FromJson("d", new JObject { ["asset"] = new JObject { ["asset_id"] = mesh.AssetId, ["asset_url"] = "https://evil.example:9999" + path } });
        var expected = await backend.GetAssetAsync(mesh, CancellationToken.None);
        var cold = await Connected(new CountingCache());   // empty cache: the foreign URL is really fetched
        Assert.Equal(expected, await cold.GetAssetAsync(foreign, CancellationToken.None));
    }

    private sealed class CountingCache : IAssetCache
    {
        private readonly MemoryAssetCache _inner = new MemoryAssetCache();
        public int Puts { get; private set; }
        public bool TryGet(string assetId, out byte[] bytes) => _inner.TryGet(assetId, out bytes);
        public void Put(string assetId, byte[] bytes) { Puts++; _inner.Put(assetId, bytes); }
    }
}
