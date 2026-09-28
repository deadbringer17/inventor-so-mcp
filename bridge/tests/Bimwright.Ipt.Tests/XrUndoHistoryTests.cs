using Bimwright.Ipt.Shared.Infrastructure;

namespace Bimwright.Ipt.Tests;

public class XrUndoHistoryTests
{
    private sealed class Backend : IXrHistoryBackend
    {
        public string DocumentId { get; set; } = "part";
        public string Revision { get; set; } = "r2";
        public string? UndoTransactionId => Done.LastOrDefault();
        public string? RedoTransactionId => Undone.LastOrDefault();
        public bool Busy { get; set; }
        public bool ThrowAfter { get; set; }
        public bool SkipOperation { get; set; }
        public int Calls;
        public List<string> Done = new() { "desktop", "xr1", "xr2" }, Undone = new();
        public void Undo() => Move(Done,Undone);
        public void Redo() => Move(Undone,Done);
        private void Move(List<string> from,List<string> to)
        {
            Calls++;
            if (!SkipOperation) { to.Add(from.Last()); from.RemoveAt(from.Count-1); Revision += "x"; }
            if (ThrowAfter) throw new IOException("COM response lost after mutation");
        }
    }
    private static (XrUndoHistory history, Backend backend, string ticket) Setup()
    {
        var h = new XrUndoHistory(); var b = new Backend();
        h.Record("quest","part","r0","r1","xr1");
        return (h,b,h.Record("quest","part","r1","r2","xr2"));
    }
    [Fact]
    public void MultipleUndoRedoStopBeforeDesktopAndConsumeTickets()
    {
        var (h,b,ticket) = Setup(); var old = ticket;
        ticket = h.Apply("quest","part",b.Revision,ticket,false,b);
        Assert.Equal("HISTORY_CHANGED",Assert.Throws<XrHistoryException>(() => h.Apply("quest","part",b.Revision,old,false,b)).Code);
        ticket = h.Apply("quest","part",b.Revision,ticket,false,b);
        Assert.False(h.Available("quest",b).Undo); Assert.True(h.Available("quest",b).Redo);
        Assert.Equal(new[] { "desktop" },b.Done);
        ticket = h.Apply("quest","part",b.Revision,ticket,true,b);
        h.Apply("quest","part",b.Revision,ticket,true,b);
        Assert.Equal(new[] { "desktop","xr1","xr2" },b.Done); Assert.Equal(4,b.Calls);
    }
    [Theory]
    [InlineData("owner")][InlineData("document")][InlineData("revision")][InlineData("stack")][InlineData("busy")]
    public void RefusalNeverCallsNativeUndo(string change)
    {
        var (h,b,ticket) = Setup();
        if (change == "document") b.DocumentId = "other";
        if (change == "revision") b.Revision = "desktop-edit";
        if (change == "stack") b.Done.Add("desktop-edit-without-event");
        if (change == "busy") b.Busy = true;
        Assert.Throws<XrHistoryException>(() => h.Apply(change == "owner" ? "other" : "quest","part","r2",ticket,false,b));
        Assert.Equal(0,b.Calls);
    }
    [Fact]
    public void InterveningRevisionInvalidatesChainEvenIfNativeStackWasRestored()
    {
        var (h,b,ticket) = Setup(); b.Revision = "desktop";
        Assert.Null(h.Available("quest",b).Ticket);
        b.Revision = "r2";
        Assert.Throws<XrHistoryException>(() => h.Apply("quest","part","r2",ticket,false,b));
        Assert.Equal(0,b.Calls);
    }
    [Theory][InlineData(true)][InlineData(false)]
    public void UnknownNativeOutcomeCannotBeReplayed(bool throws)
    {
        var (h,b,ticket) = Setup(); b.ThrowAfter = throws; b.SkipOperation = !throws;
        Assert.ThrowsAny<Exception>(() => h.Apply("quest","part","r2",ticket,false,b));
        Assert.Null(h.Available("quest",b).Ticket);
        Assert.Throws<XrHistoryException>(() => h.Apply("quest","part",b.Revision,ticket,false,b));
        Assert.Equal(1,b.Calls);
    }
    [Fact]
    public void NewCommitAfterUndoDropsRedoAndKeepsEarlierXrUndo()
    {
        var (h,b,ticket) = Setup(); h.Apply("quest","part",b.Revision,ticket,false,b);
        string before = b.Revision; b.Done.Add("xr3"); b.Undone.Clear(); b.Revision = "r3";
        ticket = h.Record("quest","part",before,b.Revision,"xr3");
        ticket = h.Apply("quest","part",b.Revision,ticket,false,b);
        h.Apply("quest","part",b.Revision,ticket,false,b);
        Assert.Equal(new[] { "desktop" },b.Done);
    }
    [Fact]
    public void CommitFollowingDesktopEditCannotBridgeToOlderXrHistory()
    {
        var (h,b,_) = Setup(); b.Done.Add("desktop2"); b.Done.Add("xr3"); b.Revision = "r4";
        var ticket = h.Record("quest","part","r3","r4","xr3");
        h.Apply("quest","part",b.Revision,ticket,false,b);
        Assert.False(h.Available("quest",b).Undo); Assert.Equal("desktop2",b.UndoTransactionId);
    }
    [Fact]
    public void AnotherClientsCommitCannotBeSkippedToReachOwnHistory()
    {
        var (h,b,_) = Setup(); b.Done.Add("other-xr"); b.Revision = "r3";
        var ticket = h.Record("other","part","r2","r3","other-xr");
        Assert.Null(h.Available("quest",b).Ticket);
        Assert.Throws<XrHistoryException>(() => h.Apply("quest","part","r3",ticket,false,b));
        Assert.Equal(0,b.Calls);
        h.Apply("other","part","r3",ticket,false,b);
        Assert.True(h.Available("quest",b).Undo);
        Assert.False(h.Available("quest",b).Redo);
    }
    [Fact]
    public void HistoryLimitKeepsLatest64TransactionsAndNeverUndoesEvictedOnes()
    {
        var h = new XrUndoHistory(); var b = new Backend { Revision = "r0", Done = new() { "desktop" } };
        string ticket = "";
        for (int i=1;i<=70;i++)
        {
            var before = b.Revision; b.Revision = "r"+i; b.Done.Add("xr"+i);
            ticket = h.Record("quest","part",before,b.Revision,"xr"+i);
        }
        for (int i=0;i<64;i++) ticket = h.Apply("quest","part",b.Revision,ticket,false,b);
        Assert.False(h.Available("quest",b).Undo);
        Assert.Equal("xr6",b.UndoTransactionId);
        Assert.Equal(64,b.Calls);
    }
}
