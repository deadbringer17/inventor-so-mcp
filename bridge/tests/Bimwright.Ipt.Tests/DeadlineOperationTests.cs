using Bimwright.Ipt.Shared.Infrastructure;

namespace Bimwright.Ipt.Tests;

public sealed class DeadlineOperationTests
{
    [Fact]
    public async Task ExpiredQueuedWorkNeverMutates()
    {
        int writes = 0;
        var op = new DeadlineOperation<int>(() => ++writes, 100);
        op.Expire();
        op.Execute();
        var ex = await Assert.ThrowsAsync<TimeoutException>(() => op.Task);
        Assert.Contains("no CAD operation", ex.Message);
        Assert.Equal(0, writes);
    }

    [Fact]
    public async Task DelayedTimerCannotAllowLateExecution()
    {
        int writes = 0;
        long elapsed = 0;
        var op = new DeadlineOperation<int>(() => ++writes, 100, () => elapsed);
        elapsed = 100;
        op.Execute();
        await Assert.ThrowsAsync<TimeoutException>(() => op.Task);
        Assert.Equal(0, writes);
    }

    [Fact]
    public async Task RunningWorkReportsUnknownOutcomeInsteadOfPretendingRollback()
    {
        using var started = new ManualResetEventSlim();
        using var finish = new ManualResetEventSlim();
        var op = new DeadlineOperation<int>(() => { started.Set(); finish.Wait(); return 7; }, 30000);
        var worker = Task.Run(op.Execute);
        try
        {
            Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
            op.Expire();
            var ex = await Assert.ThrowsAsync<TimeoutException>(() => op.Task);
            Assert.Contains("Outcome unknown", ex.Message);
        }
        finally { finish.Set(); await worker; }
    }

    [Fact]
    public async Task SuccessfulWorkRunsOnceAndCannotBeExpiredAfterCompletion()
    {
        int writes = 0;
        var op = new DeadlineOperation<int>(() => ++writes, 1000);
        op.Execute();
        op.Execute();
        op.Expire();
        Assert.Equal(1, await op.Task);
        Assert.Equal(1, writes);
    }
}
