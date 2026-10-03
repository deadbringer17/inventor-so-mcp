using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Mcp;
using InventorXrSo.Core.Verify;
using Newtonsoft.Json.Linq;

namespace InventorXrSo.Core.Tests.Verify;

public class VerifyJobTests
{
    private static DistanceReport Report(string revision) => DistanceReport.FromJson(new JObject { ["revision"] = revision, ["distance_mm"] = 30 });
    private static DocumentState State(string revision) => new DocumentState("doc", revision, "v");

    [Fact]
    public async Task RunMovesFromRunningToDoneAndKeepsTheRevision()
    {
        var session = new VerifySession();
        var pending = new TaskCompletionSource<DistanceReport>();
        var run = session.Distance.RunAsync(_ => pending.Task, default);
        Assert.Equal(VerifyStatus.Running, session.Distance.Status);
        Assert.True(session.Gate.Busy); Assert.Same(session.Distance, session.Running);
        pending.SetResult(Report("r1"));
        Assert.True(await run);
        Assert.Equal(VerifyStatus.Done, session.Distance.Status);
        Assert.Equal("r1", session.Distance.Revision);
        Assert.False(session.Gate.Busy);
    }

    [Fact]
    public async Task ASecondVerificationIsRefusedWhileOneRuns()
    {
        var session = new VerifySession();
        var pending = new TaskCompletionSource<DistanceReport>();
        _ = session.Distance.RunAsync(_ => pending.Task, default);
        Assert.False(await session.Health.RunAsync(_ => throw new InvalidOperationException("must not start"), default));
        Assert.Equal(VerifyStatus.Idle, session.Health.Status);
        Assert.Equal(VerifyMessages.Busy, session.Gate.Reason);
        pending.SetResult(Report("r1"));
    }

    [Fact]
    public async Task IgnoreDiscardsTheAnswerButKeepsTheGateBusyUntilItArrives()
    {
        var session = new VerifySession();
        var pending = new TaskCompletionSource<DistanceReport>();
        var run = session.Distance.RunAsync(_ => pending.Task, default);
        session.Distance.Ignore();
        Assert.Equal(VerifyStatus.Idle, session.Distance.Status);
        Assert.True(session.Gate.Busy, "Inventor is still computing the ignored request");
        pending.SetResult(Report("r1"));
        await run;
        Assert.Equal(VerifyStatus.Idle, session.Distance.Status);
        Assert.Null(session.Distance.Result);
        Assert.False(session.Gate.Busy);
    }

    [Fact]
    public async Task ADifferentRevisionMakesADoneResultStale()
    {
        var session = new VerifySession();
        await session.Distance.RunAsync(_ => Task.FromResult(Report("r1")), default);
        session.OnDocumentState(State("r1"));
        Assert.Equal(VerifyStatus.Done, session.Distance.Status);
        session.OnDocumentState(State("r2"));
        Assert.Equal(VerifyStatus.Stale, session.Distance.Status);
        Assert.NotNull(session.Distance.Result);
    }

    [Fact]
    public async Task ATimeoutFailsAndBlocksNewVerificationsForThirtySeconds()
    {
        var now = new DateTime(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc);
        var session = new VerifySession(() => now);
        await session.Health.RunAsync(_ => Task.FromException<HealthReport>(new McpToolException("t", "TIMEOUT", "late", null)), default);
        Assert.Equal(VerifyStatus.Failed, session.Health.Status);
        Assert.Equal(VerifyMessages.Timeout, session.Health.ErrorMessage);
        Assert.False(session.Gate.CanStart);
        Assert.Equal(VerifyMessages.Timeout, session.Gate.Reason);
        now = now.AddSeconds(31);
        Assert.True(session.Gate.CanStart);
    }

    [Fact]
    public async Task StaleRevisionErrorsBecomeAShortItalianMessage()
    {
        var session = new VerifySession();
        await session.Distance.RunAsync(_ => Task.FromException<DistanceReport>(new McpToolException("t", "STALE_REVISION", "x", null)), default);
        Assert.Equal(VerifyStatus.Failed, session.Distance.Status);
        Assert.Equal("STALE_REVISION", session.Distance.ErrorCode);
        Assert.Equal("Il modello è cambiato, rilancia.", session.Distance.ErrorMessage);
    }

    [Fact]
    public async Task ResetForgetsResultsAndAnInFlightAnswer()
    {
        var session = new VerifySession();
        await session.Distance.RunAsync(_ => Task.FromResult(Report("r1")), default);
        var pending = new TaskCompletionSource<HealthReport>();
        var run = session.Health.RunAsync(_ => pending.Task, default);
        session.Reset();
        pending.SetResult(HealthReport.FromJson(new JObject { ["revision"] = "r1" }));
        await run;
        Assert.All(session.Jobs, j => Assert.Equal(VerifyStatus.Idle, j.Status));
        Assert.Null(session.Distance.Result); Assert.Null(session.Health.Result);
    }
}
