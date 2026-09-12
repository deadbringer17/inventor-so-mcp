using Bimwright.Ipt.Shared.Contracts;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Tests;

public sealed class CadEventJournalTests
{
    [Fact]
    public void OverflowRequestsResyncAndKeepsBoundedRecentEvents()
    {
        var journal = new CadEventJournal(2);
        journal.Append("document_changed", "a");
        journal.Append("document_changed", "a");
        journal.Append("document_saved", "a");
        var result = journal.Read(0, journal.Epoch);
        Assert.True((bool)result["resync_required"]!);
        Assert.Equal(2, ((JArray)result["events"]!).Count);
        Assert.Empty((JArray)journal.Read(3, journal.Epoch)["events"]!);
    }

    [Fact]
    public void RestartCannotReuseStaleRevisionOrCursor()
    {
        var old = new CadEventJournal();
        var current = new CadEventJournal();
        Assert.NotEqual(old.Revision("doc"), current.Revision("doc"));
        Assert.True((bool)current.Read(0, old.Epoch)["resync_required"]!);
    }

    [Fact]
    public void DocumentRevisionIsIndependentAndReadReturnsCopies()
    {
        var journal = new CadEventJournal();
        string before = journal.Revision("b");
        journal.Append("document_changed", "a");
        Assert.Equal(before, journal.Revision("b"));
        var events = (JArray)journal.Read()["events"]!;
        events[0]["type"] = "tampered";
        Assert.Equal("document_changed", (string?)journal.Read()["events"]![0]!["type"]);
    }
}
