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

    private sealed class CountingCache : IAssetCache
    {
        private readonly MemoryAssetCache _inner = new MemoryAssetCache();
        public int Puts { get; private set; }
        public bool TryGet(string assetId, out byte[] bytes) => _inner.TryGet(assetId, out bytes);
        public void Put(string assetId, byte[] bytes) { Puts++; _inner.Put(assetId, bytes); }
    }
}
