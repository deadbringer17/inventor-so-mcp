using InventorXrSo.Core.Mcp;
using InventorXrSo.Core.Selection;
using InventorXrSo.Core.Tests.Support;

namespace InventorXrSo.Core.Tests.Selection;

public class SelectionTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    [Fact]
    public void ResolveFollowsTheInventorDefaults()
    {
        var none = InventorXrSo.Core.Selection.Selection.None;
        Assert.Equal(SelectionKind.Face, SelectionService.Resolve("part", none, null));
        Assert.Equal(SelectionKind.Occurrence, SelectionService.Resolve("assembly", none, "ent_1"));
        var occ = new InventorXrSo.Core.Selection.Selection(SelectionKind.Occurrence, "ent_1", null, "ent_1");
        Assert.Equal(SelectionKind.Face, SelectionService.Resolve("assembly", occ, "ent_1"));
        Assert.Equal(SelectionKind.Occurrence, SelectionService.Resolve("assembly", occ, "ent_2"));
        var face = new InventorXrSo.Core.Selection.Selection(SelectionKind.Face, "ent_1", "f", "proxy");
        Assert.Equal(SelectionKind.Face, SelectionService.Resolve("assembly", face, "ent_1"));
    }

    [Fact]
    public async Task AssemblyClickSelectsTheOccurrenceThenItsFace()
    {
        var backend = new FakeBackend();
        var service = new SelectionService(backend);
        var seen = new List<InventorXrSo.Core.Selection.Selection>();
        service.Changed += seen.Add;

        await service.SelectAsync("assembly", "ent_1", "f3", None);
        Assert.Equal(SelectionKind.Occurrence, service.Current.Kind);
        Assert.Equal("ent_1", service.Current.EntityId);
        Assert.Contains("highlight:ent_1", backend.Calls);

        await service.SelectAsync("assembly", "ent_1", "f3", None);
        Assert.Equal(SelectionKind.Face, service.Current.Kind);
        Assert.Equal("proxy:ent_1:f3", service.Current.EntityId);
        Assert.Equal(new[] { "highlight:ent_1", "clear", "pick:ent_1:f3", "highlight:proxy:ent_1:f3" }, backend.Calls);
        Assert.Equal(2, seen.Count);
    }

    [Fact]
    public async Task PartFacesNeedNoPick()
    {
        var backend = new FakeBackend();
        var service = new SelectionService(backend);
        await service.SelectAsync("part", null, "ent_face_7", None);
        Assert.Equal("ent_face_7", service.Current.EntityId);
        Assert.Equal(new[] { "highlight:ent_face_7" }, backend.Calls);
    }

    [Fact]
    public async Task AStalePickClearsTheSelectionAndRethrows()
    {
        var backend = new FakeBackend();
        var service = new SelectionService(backend);
        await service.SelectAsync("assembly", "ent_1", "f3", None);
        backend.Pick = (_, _) => throw new McpToolException("inventor_pick_entity", "INVALID_ARGUMENT", "face gone", null);
        await Assert.ThrowsAsync<McpToolException>(() => service.SelectAsync("assembly", "ent_1", "f3", None));
        Assert.Equal(SelectionKind.None, service.Current.Kind);
    }

    [Fact]
    public async Task ClickingNothingClears()
    {
        var backend = new FakeBackend();
        var service = new SelectionService(backend);
        await service.SelectAsync("assembly", "ent_1", "f3", None);
        await service.ClearAsync(None);
        Assert.Equal(SelectionKind.None, service.Current.Kind);
        Assert.Equal("clear", backend.Calls.Last());
    }
}
