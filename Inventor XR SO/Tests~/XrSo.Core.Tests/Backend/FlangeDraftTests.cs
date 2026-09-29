using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Glb;
using Newtonsoft.Json.Linq;

namespace InventorXrSo.Core.Tests.Backend;

public class FlangeDraftTests
{
    private static readonly DocumentState State = new("doc", "r1", "v1");
    private DateTimeOffset _now = new(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);

    private sealed class Backend : IDesignBackend
    {
        public int Previews, Commits; public JArray Received;
        public TaskCompletionSource<DesignPreview> Gate;
        public DateTimeOffset Expires;
        public async Task<DesignPreview> PreviewDesignAsync(DocumentState state, JArray operations, CancellationToken ct)
        {
            Previews++; Received = operations;
            if (Gate != null) return await Gate.Task;
            return new DesignPreview("plan_" + Previews, "doc", "r1", Expires, new GlbModel("doc", Array.Empty<GlbPrimitive>()));
        }
        public Task<DocumentState> CommitDesignAsync(DesignPreview preview, CancellationToken ct) { Commits++; return Task.FromResult(new DocumentState("doc", "r2", "v2")); }
    }

    private (SheetMetalMode mode, DesignSession session, Backend backend, FlangeDraft draft, IDisposable bind) Setup()
    {
        var backend = new Backend { Expires = _now.AddMinutes(15) };
        var session = new DesignSession(backend, () => _now); session.SetContext(State, true, true);
        var mode = new SheetMetalMode();
        mode.Update(State, true, true, SheetMetalContext.Parse(SheetMetalContextTests.Info(), State, State));
        mode.Arm(SheetMetalCommand.Flange, out _);
        var draft = new FlangeDraft();
        return (mode, session, backend, draft, mode.BindFlange(session, draft));
    }

    [Fact]
    public void ManipulatorAndFieldEditTheSameDraftAndBumpVersionOnlyOnRealChange()
    {
        var d = new FlangeDraft(); int changes = 0; d.Changed += () => changes++;
        Assert.True(d.SetEdges(new[] { "ent_1", "ent_2" })); Assert.Equal(1, d.Version);
        Assert.False(d.SetEdges(new[] { "ent_1", "ent_2" })); Assert.Equal(1, d.Version);
        Assert.True(d.ApplyManipulatorHeight(25)); Assert.Equal(25, d.HeightMm);
        Assert.True(d.SetHeight(30)); Assert.Equal(30, d.HeightMm);
        Assert.False(d.SetHeight(30));
        Assert.True(d.SetAngle(45)); Assert.True(d.SetDatum("inner")); Assert.False(d.SetDatum("inner"));
        Assert.Equal(5, d.Version); Assert.Equal(5, changes);
        Assert.True(d.ToggleEdge("ent_2")); Assert.Equal(new[] { "ent_1" }, d.EdgeIds);
        Assert.True(d.TryBuild(out var op, out var error)); Assert.Null(error);
        Assert.Equal("sheet_metal_flange", (string)op["command"]);
        Assert.Equal(30, (double)op["arguments"]["height_mm"]); Assert.Equal(45, (double)op["arguments"]["angle_degrees"]);
        Assert.Equal("inner", (string)op["arguments"]["height_datum"]);
    }

    [Fact]
    public void DraggingNeverInfersTheBendAngle()
    {
        var d = new FlangeDraft(); d.SetEdges(new[] { "ent_1" });
        for (double h = 5; h < 200; h += 7.5) d.ApplyManipulatorHeight(h);
        Assert.Equal(90, d.AngleDegrees);
        Assert.Equal(90, (double)(d.TryBuild(out var op, out _) ? op["arguments"]["angle_degrees"] : null));
    }

    [Fact]
    public void InvalidValuesAreStoredButNotBuildable()
    {
        var d = new FlangeDraft(); d.SetEdges(new[] { "ent_1" });
        int v = d.Version; d.SetHeight(double.NaN); Assert.Equal(v + 1, d.Version);
        d.SetHeight(double.NaN); Assert.Equal(v + 1, d.Version);
        Assert.False(d.TryBuild(out var op, out var error)); Assert.Null(op); Assert.False(string.IsNullOrEmpty(error));
        d.SetHeight(10); d.SetEdges(new[] { "mesh_face" }); Assert.False(d.TryBuild(out _, out var e2)); Assert.Contains("riferimento", e2);
        d.SetEdges(Array.Empty<string>()); Assert.False(d.TryBuild(out _, out _));
    }

    [Fact]
    public async Task EditingAfterPreviewInvalidatesApplyAndReleaseAsksForPreviewNotCommit()
    {
        var (mode, session, backend, draft, bind) = Setup(); using var cleanup = session; using var b = bind;
        draft.SetEdges(new[] { "ent_1" }); draft.ApplyManipulatorHeight(20);
        Assert.Equal(DesignStatus.Draft, session.Status);
        draft.ReleaseManipulator();
        Assert.Equal(0, backend.Previews); Assert.Equal(0, backend.Commits);
        Assert.True(await mode.PreviewPendingAsync(session, draft)); Assert.False(draft.PreviewRequested);
        Assert.Equal(1, backend.Previews); Assert.Equal(0, backend.Commits);
        Assert.Equal(20, (double)backend.Received[0]["arguments"]["height_mm"]);
        session.ConfirmRendered("plan_1"); Assert.True(session.CanApply);
        Assert.False(await mode.PreviewPendingAsync(session, draft)); // nothing pending

        draft.SetHeight(21); // numeric edit
        Assert.False(session.CanApply); Assert.Equal(DesignStatus.Draft, session.Status);
        await session.PreviewAsync(); session.ConfirmRendered("plan_2"); Assert.True(session.CanApply);
        draft.SetDatum("tangent"); Assert.False(session.CanApply);
        await session.PreviewAsync(); session.ConfirmRendered("plan_3"); Assert.True(session.CanApply);
        draft.ToggleEdge("ent_9"); Assert.False(session.CanApply);
        draft.SetAngle(0); // invalid -> rejected, ghost kept, cannot apply
        Assert.Equal(DesignStatus.Error, session.Status); Assert.False(session.CanApply);
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.ApplyAsync());
        Assert.Equal(0, backend.Commits);
    }

    [Fact]
    public async Task EditDuringPreviewCancelsIt()
    {
        var (mode, session, backend, draft, bind) = Setup(); using var cleanup = session; using var b = bind;
        draft.SetEdges(new[] { "ent_1" });
        backend.Gate = new TaskCompletionSource<DesignPreview>();
        var pending = session.PreviewAsync();
        draft.SetHeight(44);
        backend.Gate.SetResult(new DesignPreview("late", "doc", "r1", _now.AddMinutes(5), new GlbModel("doc", Array.Empty<GlbPrimitive>())));
        await pending;
        Assert.Equal(DesignStatus.Draft, session.Status); Assert.Null(session.Preview); Assert.False(session.CanApply);
    }

    [Fact]
    public async Task ReleaseIsIgnoredWhenTheDraftIsNotPreviewable()
    {
        var (mode, session, backend, draft, bind) = Setup(); using var cleanup = session; using var b = bind;
        draft.ReleaseManipulator(); // no edges: builder rejects, session in Error
        Assert.False(await mode.PreviewPendingAsync(session, draft)); Assert.Equal(0, backend.Previews);
        draft.SetEdges(new[] { "ent_1" }); draft.ReleaseManipulator(); session.Cancel();
        Assert.False(await mode.PreviewPendingAsync(session, draft)); Assert.Equal(0, backend.Previews);
    }

    [Fact]
    public void DocumentOrRevisionChangeClearsEdgesAndUnbindStopsSync()
    {
        var (mode, session, backend, draft, bind) = Setup(); using var cleanup = session;
        draft.SetEdges(new[] { "ent_1" }); Assert.Equal(DesignStatus.Draft, session.Status);
        var next = new DocumentState("doc", "r2", "v2");
        session.SetContext(next, true, true); mode.Update(next, true, true, mode.Context);
        Assert.Empty(draft.EdgeIds); Assert.Equal(SheetMetalCommand.None, mode.Armed); Assert.Equal(DesignStatus.Empty, session.Status);
        bind.Dispose();
        mode.Update(next, true, true, SheetMetalContext.Parse(SheetMetalContextTests.Info(), next, next)); mode.Arm(SheetMetalCommand.Flange, out _);
        draft.SetEdges(new[] { "ent_1" }); Assert.Equal(DesignStatus.Empty, session.Status);
    }

    [Fact]
    public void UnarmedFlangeDoesNotWriteADraft()
    {
        var (mode, session, backend, draft, bind) = Setup(); using var cleanup = session; using var b = bind;
        mode.Disarm(); draft.SetEdges(new[] { "ent_1" });
        Assert.Equal(DesignStatus.Empty, session.Status); Assert.False(mode.SubmitFlange(session, draft));
    }
}
