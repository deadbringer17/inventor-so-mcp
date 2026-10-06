using Newtonsoft.Json.Linq;
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Mcp;
using InventorXrSo.Core.Session;
using InventorXrSo.Core.Tests.Glb;
using InventorXrSo.Core.Tests.Support;

namespace InventorXrSo.Core.Tests.Session;

public class SceneLoaderTests
{
    private static FakeBackend Backend()
    {
        var backend = new FakeBackend { Scene = () => FakeBackend.Assembly() };
        backend.Meshes["p"] = GlbModelTests.BoltGlb();
        backend.Meshes["q"] = GlbModelTests.BoltGlb();
        return backend;
    }

    [Fact]
    public async Task LoadsEveryDefinitionOnce()
    {
        var scene = await new SceneLoader(Backend()).LoadAsync(CancellationToken.None);
        Assert.Equal(new[] { "p", "q" }, scene.Models.Keys.OrderBy(k => k));
        Assert.Equal("a_p", scene.AssetIds["p"]);
        Assert.Empty(scene.Omitted);
    }

    [Fact]
    public async Task ATooLargeMeshIsOmittedNotFatal()
    {
        var backend = Backend();
        backend.TooLarge.Add("q");
        var scene = await new SceneLoader(backend).LoadAsync(CancellationToken.None);
        Assert.Equal(new[] { "p" }, scene.Models.Keys);
        Assert.Equal(new[] { "q" }, scene.Omitted);
    }

    [Fact]
    public async Task ACorruptMeshIsOmittedNotFatal()
    {
        var backend = Backend();
        backend.Meshes["q"] = new byte[30];
        var scene = await new SceneLoader(backend).LoadAsync(CancellationToken.None);
        Assert.Equal(new[] { "p" }, scene.Models.Keys);
        Assert.Equal(new[] { "q" }, scene.Omitted);
    }

    [Fact]
    public async Task OtherToolErrorsPropagate()
    {
        var backend = Backend();
        backend.FailNext = new McpToolException("inventor_get_scene_graph", "NO_DOCUMENT", "none", null);
        await Assert.ThrowsAsync<McpToolException>(() => new SceneLoader(backend).LoadAsync(CancellationToken.None));
    }

    private static FakeBackend Many(int count)
    {
        var ids = Enumerable.Range(0, count).Select(i => "d" + i).ToArray();
        var backend = new FakeBackend
        {
            Scene = () => SceneGraph.FromJson(JObject.Parse(
                @"{""document_id"":""doc"",""kind"":""assembly"",""revision"":""r1"",""visual_revision"":""v1"",""definition_document_ids"":["
                + string.Join(",", ids.Select(i => "\"" + i + "\"")) + @"],""root"":{""name"":""Top"",""definition_kind"":""assembly"",""children"":[]}}"))
        };
        foreach (var id in ids) backend.Meshes[id] = GlbModelTests.BoltGlb();
        return backend;
    }

    [Fact]
    public async Task MeshesAreFetchedConcurrentlyUpToTheBound()
    {
        var backend = Many(24);
        backend.MeshGate = async (_, ct) => await Task.Delay(40, ct);
        var scene = await new SceneLoader(backend).LoadAsync(CancellationToken.None);
        Assert.Equal(24, scene.Models.Count);
        Assert.True(backend.MaxConcurrentMeshCalls > 1, "fetches must overlap");
        Assert.True(backend.MaxConcurrentMeshCalls <= SceneLoader.MaxParallelFetches, "concurrency " + backend.MaxConcurrentMeshCalls);
    }

    [Fact]
    public async Task ResultOrderFollowsTheSceneNotTheCompletionOrder()
    {
        var backend = Many(12);
        // Later definitions answer first.
        backend.MeshGate = async (id, ct) => await Task.Delay((12 - int.Parse(id.Substring(1))) * 5, ct);
        backend.TooLarge.Add("d3"); backend.TooLarge.Add("d9");
        var scene = await new SceneLoader(backend).LoadAsync(CancellationToken.None);
        Assert.Equal(Enumerable.Range(0, 12).Where(i => i != 3 && i != 9).Select(i => "d" + i), scene.Models.Keys);
        Assert.Equal(new[] { "d3", "d9" }, scene.Omitted);
        Assert.Equal("a_d5", scene.AssetIds["d5"]);
    }

    [Fact]
    public async Task OmittedDefinitionsDoNotUseUpTheDefinitionCap()
    {
        int total = SceneLoader.MaxDefinitions + 6;
        var backend = Many(total);
        backend.TooLarge.Add("d1"); backend.TooLarge.Add("d2");
        var scene = await new SceneLoader(backend).LoadAsync(CancellationToken.None);
        Assert.Equal(SceneLoader.MaxDefinitions, scene.Models.Count);
        Assert.Equal(new[] { "d1", "d2" }.Concat(Enumerable.Range(SceneLoader.MaxDefinitions + 2, 4).Select(i => "d" + i)), scene.Omitted);
        Assert.Equal("d0", scene.Models.Keys.First());
    }

    [Fact]
    public async Task CancellationStopsLaunchingWorkAndPropagates()
    {
        var backend = Many(40);
        using var cts = new CancellationTokenSource();
        int started = 0;
        backend.MeshGate = async (_, ct) =>
        {
            if (Interlocked.Increment(ref started) == 3) cts.Cancel();
            await Task.Delay(Timeout.Infinite, ct);
        };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new SceneLoader(backend).LoadAsync(cts.Token));
        Assert.True(backend.Calls.Count(c => c.StartsWith("mesh:")) <= SceneLoader.MaxParallelFetches + 1, "no new fetches after cancel");
    }

    [Fact]
    public async Task AFatalErrorOnOneDefinitionFailsTheLoadAndCancelsTheSiblings()
    {
        var backend = Many(30);
        int cancelledSiblings = 0;
        backend.MeshGate = async (id, ct) =>
        {
            if (id == "d1") { await Task.Delay(20, ct); throw new McpToolException("inventor_get_display_mesh", "NO_DOCUMENT", "gone", null); }
            try { await Task.Delay(Timeout.Infinite, ct); }
            catch (OperationCanceledException) { Interlocked.Increment(ref cancelledSiblings); throw; }
        };
        var ex = await Assert.ThrowsAsync<McpToolException>(() => new SceneLoader(backend).LoadAsync(CancellationToken.None));
        Assert.Equal("NO_DOCUMENT", ex.Code);
        Assert.True(cancelledSiblings >= 1, "in-flight siblings are cancelled");
        Assert.True(backend.Calls.Count(c => c.StartsWith("mesh:")) < 30, "queued definitions are never started");
    }
}
