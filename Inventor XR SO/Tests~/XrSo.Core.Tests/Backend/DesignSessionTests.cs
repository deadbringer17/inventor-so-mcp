using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Glb;
using InventorXrSo.Core.Mcp;
using Newtonsoft.Json.Linq;

namespace InventorXrSo.Core.Tests.Backend;

public class DesignSessionTests
{
    [Fact]
    public void RestoredReviewGuardSurvivesContextChangesAndRequiresExplicitReview()
    {
        var backend=new Backend(); using var session=new DesignSession(backend);
        session.RequireCadReview(); session.SetContext(State,true,true);
        Assert.False(session.CanEdit); Assert.True(session.CommitOutcomeUnknown);
        session.SetContext(new("other","r","v"),true,true); Assert.False(session.CanEdit);
        Assert.Throws<InvalidOperationException>(()=>session.Cancel());
        Assert.Throws<InvalidOperationException>(()=>session.AcknowledgeRefresh(State));
        session.AcknowledgeRefresh(State,true); Assert.True(session.CanEdit);
    }
    private static readonly DocumentState State = new("doc", "r1", "v1");
    private static JArray Draft(double n = 10) => new(DesignOperations.Parameter("Width", n, "mm"));
    private DateTimeOffset _now = new(2026, 9, 27, 12, 0, 0, TimeSpan.Zero);
    private DesignPreview Preview(string revision = "r1") => new("plan_1", "doc", revision, _now.AddMinutes(15),
        new GlbModel("doc", Array.Empty<GlbPrimitive>()));

    private sealed class Backend : IDesignBackend, IDesignHistoryBackend
    {
        public Func<Task<DesignPreview>> Plan;
        public Func<Task<DocumentState>> Commit = () => Task.FromResult(new DocumentState("doc", "r2", "v2"));
        public int Commits;
        public int HistoryCalls;
        public Task<DesignHistory> GetHistoryAsync(DocumentState state, CancellationToken ct) => Task.FromResult(new DesignHistory(state,true,true,"ticket"));
        public Task<DocumentState> ApplyHistoryAsync(DesignHistory history, bool redo, CancellationToken ct) { HistoryCalls++; return Commit(); }
        public JArray Received;
        public Task<DesignPreview> PreviewDesignAsync(DocumentState state, JArray operations, CancellationToken ct)
        { Received = operations; return Plan(); }
        public Task<DocumentState> CommitDesignAsync(DesignPreview preview, CancellationToken ct) { Commits++; return Commit(); }
    }
    private (DesignSession session, Backend backend) Setup()
    {
        var backend = new Backend { Plan = () => Task.FromResult(Preview()) };
        var session = new DesignSession(backend, () => _now);
        session.SetContext(State, true, true); session.SetDraft(Draft());
        return (session, backend);
    }

    [Fact]
    public async Task HistoryRequiresEmptyDraftAndMatchingRevision()
    {
        var (session,backend) = Setup(); using var cleanup = session;
        var history = new DesignHistory(State,true,true,"ticket");
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.ApplyHistoryAsync(history,false));
        session.Cancel();
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.ApplyHistoryAsync(new DesignHistory(new("doc","old","v"),true,false,"old"),false));
        Assert.Equal(0,backend.HistoryCalls);
        await session.ApplyHistoryAsync(history,false);
        Assert.Equal(1,backend.HistoryCalls); Assert.Equal(DesignStatus.RefreshRequired,session.Status);
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.ApplyHistoryAsync(history,true));
    }

    [Fact]
    public async Task LostHistoryResponseRequiresExplicitReviewAndCannotBeRetried()
    {
        var (session,backend) = Setup(); using var cleanup = session; session.Cancel();
        backend.Commit = () => throw new IOException("Lost response after Undo");
        var history = new DesignHistory(State,true,false,"ticket");
        await session.ApplyHistoryAsync(history,false);
        Assert.True(session.CommitOutcomeUnknown);
        Assert.Throws<InvalidOperationException>(() => session.AcknowledgeRefresh(State));
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.ApplyHistoryAsync(history,false));
        Assert.Equal(1,backend.HistoryCalls);
        session.AcknowledgeRefresh(State,true); Assert.True(session.CanEdit);
    }

    [Fact]
    public async Task HistoryInFlightBlocksEditsEvenAfterContextChanges()
    {
        var (session,backend) = Setup(); using var cleanup = session; session.Cancel();
        var response = new TaskCompletionSource<DocumentState>(); backend.Commit = () => response.Task;
        var pending = session.ApplyHistoryAsync(new DesignHistory(State,true,false,"ticket"),false);
        session.SetContext(new("other","r","v"),true,true);
        Assert.False(session.CanEdit); Assert.Throws<InvalidOperationException>(() => session.Cancel());
        response.SetResult(new("doc","r2","v2")); await pending;
        Assert.Equal(DesignStatus.RefreshRequired,session.Status); Assert.Equal(1,backend.HistoryCalls);
    }

    [Fact]
    public async Task ApplyNeedsRenderedPreviewAndConsumesItOnce()
    {
        var (session, backend) = Setup(); using var cleanup = session;
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.ApplyAsync());
        await session.PreviewAsync(); Assert.False(session.CanApply);
        session.ConfirmRendered("other"); Assert.False(session.CanApply);
        session.ConfirmRendered("plan_1"); Assert.True(session.CanApply);
        await session.ApplyAsync(); Assert.Equal(1, backend.Commits);
        Assert.Equal(DesignStatus.RefreshRequired, session.Status);
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.ApplyAsync());
        session.AcknowledgeRefresh(session.LastCommit); Assert.True(session.CanEdit);
    }

    [Fact]
    public async Task LocalValidationKeepsGhostButCannotReusePreviousDraft()
    {
        var (session,backend) = Setup(); using var cleanup = session;
        await session.PreviewAsync(); session.ConfirmRendered("plan_1");
        var ghost = session.Preview;
        session.RejectDraft("Dimension must be positive.");
        Assert.Same(ghost,session.Preview); Assert.False(session.CanApply); Assert.True(session.CanEdit);
        Assert.Equal("Dimension must be positive.",session.Error);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>session.PreviewAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(()=>session.ApplyAsync());
        Assert.Equal(0,backend.Commits);
        session.SetDraft(Draft(15)); await session.PreviewAsync(); session.ConfirmRendered("plan_1");
        Assert.True(session.CanApply); Assert.Null(session.Error);
        Assert.Equal("15 mm",(string)backend.Received[0]!["arguments"]!["value"]!);
    }

    [Fact]
    public async Task LatePreviewCannotOverrideLocalValidationFailure()
    {
        var (session,backend) = Setup(); using var cleanup = session;
        await session.PreviewAsync(); var ghost = session.Preview;
        var response = new TaskCompletionSource<DesignPreview>(); backend.Plan=()=>response.Task;
        var pending=session.PreviewAsync(); session.RejectDraft("Invalid edge selection");
        response.SetResult(Preview()); await pending;
        Assert.Same(ghost,session.Preview); Assert.Equal(DesignStatus.Error,session.Status);
        Assert.Equal("Invalid edge selection",session.Error); Assert.False(session.CanApply);
    }

    [Fact]
    public async Task ExpiredPreviewCannotApplyEvenWithoutAnUpdateTick()
    {
        var (session, backend) = Setup(); using var cleanup = session;
        await session.PreviewAsync(); session.ConfirmRendered("plan_1"); _now = _now.AddMinutes(16);
        Assert.False(session.CanApply);
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.ApplyAsync()); Assert.Equal(0, backend.Commits);
    }

    [Fact]
    public async Task EditingInvalidatesPreviewAndClonesOperations()
    {
        var (session, backend) = Setup(); using var cleanup = session;
        await session.PreviewAsync(); session.ConfirmRendered("plan_1");
        var draft = Draft(20); session.SetDraft(draft); draft[0]!["arguments"]!["value"] = "99 mm";
        Assert.False(session.CanApply); Assert.NotNull(session.Preview);
        await session.PreviewAsync(); Assert.Equal("20 mm", (string)backend.Received[0]!["arguments"]!["value"]);
    }

    [Theory]
    [InlineData("revision")]
    [InlineData("document")]
    [InlineData("offline")]
    [InlineData("cancel")]
    [InlineData("edit")]
    [InlineData("dispose")]
    public async Task LatePreviewCannotResurrectInvalidatedDraft(string change)
    {
        var (session, backend) = Setup(); using var cleanup = session;
        var pending = new TaskCompletionSource<DesignPreview>(); backend.Plan = () => pending.Task;
        var call = session.PreviewAsync();
        switch (change)
        {
            case "revision": session.SetContext(new DocumentState("doc", "r2", "v2"), true, true); break;
            case "document": session.SetContext(new DocumentState("other", "r1", "v1"), true, true); break;
            case "offline": session.SetContext(State, false, true); break;
            case "cancel": session.Cancel(); break;
            case "edit": session.SetDraft(Draft(50)); break;
            case "dispose": session.Dispose(); break;
        }
        pending.SetResult(Preview()); await call;
        Assert.False(session.CanApply); Assert.Null(session.Preview); Assert.Equal(0, backend.Commits);
    }

    [Fact]
    public async Task FailedValidationRetainsGhostAndAllowsCorrection()
    {
        var (session, backend) = Setup(); using var cleanup = session;
        await session.PreviewAsync(); var ghost = session.Preview;
        session.SetDraft(Draft(100));
        backend.Plan = () => throw new McpToolException("plan", "ROLLED_BACK", "Feature unhealthy", null);
        await session.PreviewAsync(); Assert.Same(ghost, session.Preview);
        Assert.Equal(DesignStatus.Error, session.Status); Assert.False(session.CanApply); Assert.True(session.CanEdit);
        session.SetDraft(Draft(10)); Assert.Null(session.Error);
    }

    [Fact]
    public async Task LostCommitResponseRequiresRefreshAndUserReviewWithoutRetry()
    {
        var (session, backend) = Setup(); using var cleanup = session;
        backend.Commit = () => throw new IOException("Connection lost after send");
        await session.PreviewAsync(); session.ConfirmRendered("plan_1"); await session.ApplyAsync();
        Assert.True(session.CommitOutcomeUnknown); Assert.False(session.CanEdit); Assert.Equal(1, backend.Commits);
        Assert.Throws<InvalidOperationException>(() => session.Cancel());
        Assert.Throws<InvalidOperationException>(() => session.SetDraft(Draft()));
        session.SetContext(State, false, true); session.SetContext(State, true, true);
        Assert.False(session.CanEdit);
        Assert.Throws<InvalidOperationException>(() => session.AcknowledgeRefresh(State));
        session.AcknowledgeRefresh(new DocumentState("doc", "r2", "v2"), true);
        Assert.True(session.CanEdit); Assert.Equal(1, backend.Commits);
    }

    [Fact]
    public async Task RevisionEventDuringCommitCannotStartAnotherCommand()
    {
        var (session, backend) = Setup(); using var cleanup = session;
        var pending = new TaskCompletionSource<DocumentState>(); backend.Commit = () => pending.Task;
        await session.PreviewAsync(); session.ConfirmRendered("plan_1"); var commit = session.ApplyAsync();
        var next = new DocumentState("doc", "r2", "v2"); session.SetContext(next, true, true);
        Assert.False(session.CanEdit); Assert.Throws<InvalidOperationException>(() => session.AcknowledgeRefresh(next));
        pending.SetResult(next); await commit;
        Assert.Same(next, session.LastCommit); Assert.Equal(DesignStatus.RefreshRequired, session.Status);
    }

    [Fact]
    public async Task AssemblyAndOfflineContextsCannotAuthor()
    {
        var (session, _) = Setup(); using var cleanup = session;
        session.SetContext(State, true, false); Assert.False(session.CanEdit);
        Assert.Throws<InvalidOperationException>(() => session.SetDraft(Draft()));
        session.SetContext(State, false, true);
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.PreviewAsync());
    }
}
