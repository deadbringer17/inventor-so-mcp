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
    public async Task OtherToolErrorsPropagate()
    {
        var backend = Backend();
        backend.FailNext = new McpToolException("inventor_get_scene_graph", "NO_DOCUMENT", "none", null);
        await Assert.ThrowsAsync<McpToolException>(() => new SceneLoader(backend).LoadAsync(CancellationToken.None));
    }
}
