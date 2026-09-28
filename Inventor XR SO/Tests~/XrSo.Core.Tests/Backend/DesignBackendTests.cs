using Bimwright.Ipt.Tests;
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Mcp;
using InventorXrSo.Core.Tests.Support;
using Newtonsoft.Json.Linq;

namespace InventorXrSo.Core.Tests.Backend;

public class DesignBackendTests : IClassFixture<BackendFixture>
{
    private readonly BackendFixture _fixture;
    public DesignBackendTests(BackendFixture fixture) { _fixture = fixture; _fixture.AddIn.DesignPartMode = true; }

    [Fact]
    public async Task HistoryCrossesHttpsWithOwnerIsolationStaleRevisionAndConsumedTickets()
    {
        var backend = new InventorBackend(_fixture.Transport(), _fixture.EditorServer, new MemoryAssetCache());
        await backend.ConnectAsync(default);
        var before = await backend.GetDocumentStateAsync(default);
        var plan = await backend.PreviewDesignAsync(before,new JArray(DesignOperations.Parameter("BoltLength",37,"mm")),default);
        var committed = await backend.CommitDesignAsync(plan,default);
        var history = await backend.GetHistoryAsync(committed,default); Assert.True(history.CanUndo);
        string token = Inventor.So.Mcp.Http.TokenRegistry.Generate(); _fixture.Tokens.Add("other-editor",token);
        var other = new InventorBackend(_fixture.Transport(),new InventorXrSo.Core.Pairing.PairedServer(
            _fixture.Base.Host,_fixture.Base.Port,_fixture.CertSha256,"other-editor",token),new MemoryAssetCache());
        await other.ConnectAsync(default);
        Assert.False((await other.GetHistoryAsync(committed,default)).CanUndo);
        var denied = await Assert.ThrowsAsync<McpToolException>(() => other.ApplyHistoryAsync(history,false,default));
        Assert.Equal("HISTORY_CHANGED",denied.Code);
        var undone = await backend.ApplyHistoryAsync(history,false,default);
        Assert.NotEqual(committed.Revision,undone.Revision);
        Assert.Equal("STALE_REVISION",(await Assert.ThrowsAsync<McpToolException>(() => backend.ApplyHistoryAsync(history,false,default))).Code);
        var redo = await backend.GetHistoryAsync(undone,default); Assert.True(redo.CanRedo);
        var redone = await backend.ApplyHistoryAsync(redo,true,default);
        var current = await backend.GetHistoryAsync(redone,default); Assert.True(current.CanUndo);
        _fixture.AddIn.RaiseDocumentChanged(true);
        Assert.Equal("STALE_REVISION",(await Assert.ThrowsAsync<McpToolException>(() => backend.ApplyHistoryAsync(current,false,default))).Code);
        Assert.False((await backend.GetHistoryAsync(await backend.GetDocumentStateAsync(default),default)).CanUndo);
    }

    [Fact]
    public async Task RealHttpsPreviewDownloadsTentativeGlbThenCommitsSamePlanOnce()
    {
        var backend = new InventorBackend(_fixture.Transport(), _fixture.EditorServer, new MemoryAssetCache());
        await backend.ConnectAsync(default);
        var before = await backend.GetDocumentStateAsync(default);
        var plan = await backend.PreviewDesignAsync(before, new JArray(DesignOperations.Parameter("BoltLength", 20, "mm")), default);
        Assert.Equal(FakeAddIn.BoltId, plan.Model.DocumentId);
        Assert.NotEmpty(plan.Model.Primitives);
        Assert.All(plan.Model.Primitives.SelectMany(p => p.Faces), f => Assert.Null(f.FaceId));
        Assert.Equal(before.Revision, (await backend.GetDocumentStateAsync(default)).Revision);
        var committed = await backend.CommitDesignAsync(plan, default);
        Assert.NotEqual(before.Revision, committed.Revision);
        var reused = await Assert.ThrowsAsync<McpToolException>(() => backend.CommitDesignAsync(plan, default));
        Assert.Equal("PLAN_NOT_FOUND", reused.Code);
    }

    [Fact]
    public async Task ContextAndSketchPreviewCrossTheRealTransportWithFrames()
    {
        var backend = new InventorBackend(_fixture.Transport(), _fixture.EditorServer, new MemoryAssetCache());
        await backend.ConnectAsync(default);
        var state = await backend.GetDocumentStateAsync(default);
        var context = await backend.GetDesignContextAsync(state,default);
        Assert.Equal("3",Assert.Single(context.Planes).Reference);
        Assert.Equal(10,Assert.Single(context.Edges).PointsMm[1].X);
        var draft = new SketchDraft("3",context.Planes[0].Frame,"XR_Circle");
        draft.Add(new SketchElement(SketchShape.Circle,new CadPoint(2,4),default,6));
        var preview = await backend.PreviewDesignAsync(state,draft.Operations(),default);
        var sketch = Assert.Single(preview.Sketches);
        Assert.Equal("XR_Circle",sketch.Name); Assert.Equal(6,Assert.Single(sketch.Elements).Radius);
        Assert.Equal(state.Revision,(await backend.GetDocumentStateAsync(default)).Revision);
    }

    [Fact]
    public async Task DesktopChangeRejectsCommitOfOldPreview()
    {
        var backend = new InventorBackend(_fixture.Transport(), _fixture.EditorServer, new MemoryAssetCache());
        await backend.ConnectAsync(default);
        var before = await backend.GetDocumentStateAsync(default);
        var plan = await backend.PreviewDesignAsync(before, new JArray(DesignOperations.Parameter("BoltLength", 25, "mm")), default);
        _fixture.AddIn.RaiseDocumentChanged(true);
        var stale = await Assert.ThrowsAsync<McpToolException>(() => backend.CommitDesignAsync(plan, default));
        Assert.Equal("STALE_REVISION", stale.Code);
    }
}
