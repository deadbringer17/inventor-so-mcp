using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Glb;
using Newtonsoft.Json.Linq;

namespace InventorXrSo.Core.Tests.Backend;

public class SheetMetalModeTests
{
    private static readonly DocumentState State = new("doc", "r1", "v1");
    private DateTimeOffset _now = new(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);

    private sealed class Backend : IDesignBackend
    {
        public Func<Task<DesignPreview>> Plan;
        public int Previews, Commits;
        public JArray Received;
        public Task<DesignPreview> PreviewDesignAsync(DocumentState state, JArray operations, CancellationToken ct)
        { Previews++; Received = operations; return Plan(); }
        public Task<DocumentState> CommitDesignAsync(DesignPreview preview, CancellationToken ct)
        { Commits++; return Task.FromResult(new DocumentState("doc", "r2", "v2")); }
    }

    private DesignPreview Preview(string revision = "r1", string plan = "plan_1") => new(plan, "doc", revision, _now.AddMinutes(15),
        new GlbModel("doc", Array.Empty<GlbPrimitive>()));

    private (SheetMetalMode mode, DesignSession session, Backend backend) Setup(bool flat = false, int bodies = 1, bool online = true)
    {
        var backend = new Backend { Plan = () => Task.FromResult(Preview()) };
        var session = new DesignSession(backend, () => _now);
        session.SetContext(State, online, true);
        var mode = new SheetMetalMode();
        mode.Update(State, online, true, SheetMetalContext.Parse(SheetMetalContextTests.Info(flat, bodies), State, State));
        return (mode, session, backend);
    }

    [Fact] // M5-01
    public void SheetMetalPartMakesLamieraPrimaryButOrdinaryPartDoesNot()
    {
        var (mode, session, _) = Setup(); using var cleanup = session;
        Assert.True(mode.IsPrimary); Assert.True(mode.CanWrite); Assert.Equal(SheetMetalAvailability.Ready, mode.Availability);

        var ordinary = new SheetMetalMode();
        ordinary.Update(State, true, true, SheetMetalContext.Parse(JObject.Parse(@"{""is_sheet_metal"":false}"), State));
        Assert.False(ordinary.IsPrimary); Assert.False(ordinary.CanWrite); Assert.Equal(SheetMetalAvailability.NotSheetMetal, ordinary.Availability);
        Assert.False(ordinary.Arm(SheetMetalCommand.Flange, out var reason)); Assert.NotNull(reason);
        // The ordinary part keeps working in Design: the same session is untouched by Lamiera.
        using var design = new DesignSession(new Backend());
        design.SetContext(State, true, true); design.SetDraft(new JArray(DesignOperations.Parameter("W", 1, "mm")));
        Assert.True(design.CanEdit);

        var asm = new SheetMetalMode(); asm.Update(State, true, false, SheetMetalContext.Parse(SheetMetalContextTests.Info(), State));
        Assert.False(asm.IsPrimary); Assert.Equal(SheetMetalAvailability.NotAPart, asm.Availability);
        var none = new SheetMetalMode(); none.Update(State, true, true, null);
        Assert.False(none.IsPrimary); Assert.False(none.CanWrite); Assert.NotNull(none.Reason);
        var unreadable = new SheetMetalMode(); unreadable.Update(State, true, true, SheetMetalContext.Unavailable(State, "boom"));
        Assert.False(unreadable.IsPrimary); Assert.Equal("boom", unreadable.Reason);
    }

    [Fact] // M5-02
    public void ContextFollowsDocumentRevisionAndConnection()
    {
        var (mode, session, _) = Setup(); using var cleanup = session;
        Assert.True(mode.Arm(SheetMetalCommand.Flange, out _));
        var next = new DocumentState("doc", "r2", "v2");
        mode.Update(next, true, true, mode.Context); // old read against the new revision
        Assert.Equal(SheetMetalAvailability.Stale, mode.Availability); Assert.True(mode.IsPrimary);
        Assert.False(mode.CanWrite); Assert.Equal(SheetMetalCommand.None, mode.Armed);
        mode.Update(next, true, true, SheetMetalContext.Parse(SheetMetalContextTests.Info(true), next, next));
        Assert.True(mode.CanWrite); Assert.True(mode.Context.FlatPattern.Exists);
        mode.Update(next, false, true, mode.Context);
        Assert.Equal(SheetMetalAvailability.Offline, mode.Availability); Assert.False(mode.CanWrite);
        mode.Update(new DocumentState("other", "r1", "v1"), true, true, mode.Context);
        Assert.Equal(SheetMetalAvailability.NoRead, mode.Availability); Assert.False(mode.IsPrimary);
    }

    [Fact]
    public void IncompleteReadShowsReasonAndCannotBeArmed()
    {
        var mode = new SheetMetalMode(); var json = SheetMetalContextTests.Info(); json.Remove("rule");
        mode.Update(State, true, true, SheetMetalContext.Parse(json, State));
        Assert.True(mode.IsPrimary); Assert.False(mode.CanWrite); Assert.Equal(SheetMetalAvailability.Unreadable, mode.Availability);
        Assert.False(mode.Arm(SheetMetalCommand.Face, out var reason)); Assert.Contains("incompleta", reason);
        using var session = new DesignSession(new Backend()); session.SetContext(State, true, true);
        Assert.False(mode.SubmitFace(session, "S")); Assert.Contains("incompleta", mode.LastError);
        Assert.Equal(DesignStatus.Empty, session.Status);
    }

    [Fact] // M5-04 core: Face/Cut/rule share the Design session, preview, Apply, Cancel
    public async Task FaceCutAndRuleRunThroughSharedSessionPreviewApplyAndCancel()
    {
        var (mode, session, backend) = Setup(); using var cleanup = session;
        Assert.False(mode.SubmitFace(session, "Sketch1")); Assert.Equal("Arma prima il comando.", mode.LastError);
        Assert.True(mode.Arm(SheetMetalCommand.Face, out _)); Assert.True(mode.SubmitFace(session, "Sketch1"));
        Assert.Equal(DesignStatus.Draft, session.Status);
        await session.PreviewAsync(); Assert.False(session.CanApply); session.ConfirmRendered("plan_1"); Assert.True(session.CanApply);
        Assert.Equal("sheet_metal_face", (string)backend.Received[0]["command"]);
        session.Cancel(); Assert.False(session.CanApply); Assert.Equal(0, backend.Commits);

        mode.Arm(SheetMetalCommand.Cut, out _);
        Assert.True(mode.SubmitCut(session, "S2", "through_all", "positive", false));
        await session.PreviewAsync(); session.ConfirmRendered("plan_1");
        await session.ApplyAsync(); Assert.Equal(1, backend.Commits); Assert.Equal(DesignStatus.RefreshRequired, session.Status);
        Assert.False(mode.SubmitCut(session, "S2", "through_all", "positive", false)); // blocked until refreshed
        session.AcknowledgeRefresh(session.LastCommit);
    }

    [Fact]
    public void RuleMustExistAndThicknessBoundedOtherwiseDraftIsRejectedWithoutWrite()
    {
        var (mode, session, backend) = Setup(); using var cleanup = session;
        mode.Arm(SheetMetalCommand.Rule, out _);
        Assert.False(mode.SubmitRule(session, "Steel_9", null, null)); Assert.Equal("La regola non è presente nel documento.", mode.LastError);
        Assert.Equal(DesignStatus.Error, session.Status); Assert.Equal(mode.LastError, session.Error);
        Assert.False(mode.SubmitRule(session, null, null, 0.01)); Assert.Equal(0, backend.Previews);
        Assert.True(mode.SubmitRule(session, "Alu_1mm", null, 1)); Assert.Equal(DesignStatus.Draft, session.Status);
        Assert.Null(session.Error); Assert.True(session.CanEdit);
    }

    [Fact] // M5-05: distinct outcomes for create-flat-pattern
    public void FlatPatternCreationDistinguishesAllowedExistingAndMultiBody()
    {
        var (mode, session, _) = Setup(); using var cleanup = session;
        Assert.Equal(FlatPatternCreation.Allowed, mode.CheckFlatPattern(out var m0)); Assert.Null(m0);
        Assert.True(mode.Arm(SheetMetalCommand.FlatPattern, out _));
        Assert.True(mode.SubmitFlatPattern(session, "ent_edge", "vertical", true));

        var (existing, s2, _) = Setup(flat: true); using var c2 = s2;
        Assert.Equal(FlatPatternCreation.AlreadyExists, existing.CheckFlatPattern(out var m1)); Assert.Contains("esiste già", m1);
        Assert.False(existing.Arm(SheetMetalCommand.FlatPattern, out var r1)); Assert.Equal(m1, r1);

        var (multi, s3, _) = Setup(bodies: 2); using var c3 = s3;
        Assert.Equal(FlatPatternCreation.MultiBody, multi.CheckFlatPattern(out var m2)); Assert.Contains("un solo body", m2);
        Assert.NotEqual(m1, m2);
        Assert.False(multi.Arm(SheetMetalCommand.FlatPattern, out _));

        var (off, s4, _) = Setup(online: false); using var c4 = s4;
        Assert.Equal(FlatPatternCreation.Unavailable, off.CheckFlatPattern(out _));
    }

    [Fact] // M5-07
    public async Task StaleRevisionOfflineAndLatePreviewNeverEnableApply()
    {
        var late = new TaskCompletionSource<DesignPreview>();
        var (mode, session, backend) = Setup(); using var cleanup = session;
        backend.Plan = () => late.Task;
        mode.Arm(SheetMetalCommand.Face, out _); mode.SubmitFace(session, "S");
        var pending = session.PreviewAsync();
        var next = new DocumentState("doc", "r2", "v2");
        session.SetContext(next, true, true); mode.Update(next, true, true, mode.Context);
        late.SetResult(Preview("r1")); await pending;
        Assert.False(session.CanApply); Assert.Equal(DesignStatus.Empty, session.Status);
        Assert.False(mode.SubmitFace(session, "S")); Assert.Equal(0, backend.Commits);

        // Offline: no draft can be submitted; going offline drops the armed command.
        var (m2, s2, _) = Setup(); using var c2 = s2;
        m2.Arm(SheetMetalCommand.Face, out _);
        s2.SetContext(State, false, true); m2.Update(State, false, true, m2.Context);
        Assert.Equal(SheetMetalCommand.None, m2.Armed); Assert.False(m2.SubmitFace(s2, "S")); Assert.False(s2.CanEdit);
    }
}
