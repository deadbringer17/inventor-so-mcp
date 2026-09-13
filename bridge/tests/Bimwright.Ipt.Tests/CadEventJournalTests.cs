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
    public void RestoringARevisionUndoesTheBumpOfAnUndoneChange()
    {
        var journal = new CadEventJournal();
        journal.Append("document_changed", "doc");
        string planned = journal.Revision("doc");
        journal.Append("document_changed", "doc");   // the preview's own edits
        journal.Append("document_changed", "doc");   // and its rollback
        Assert.NotEqual(planned, journal.Revision("doc"));

        Assert.True(journal.TryRestoreRevision("doc", planned));
        Assert.Equal(planned, journal.Revision("doc"));
        // The journal itself still records everything that happened.
        Assert.Equal(3, ((JArray)journal.Read()["events"]!).Count);
    }

    [Fact]
    public void RestoringNeverMovesARevisionForwardOrCrossesAnEpoch()
    {
        var journal = new CadEventJournal();
        journal.Append("document_changed", "doc");
        string current = journal.Revision("doc");

        Assert.False(journal.TryRestoreRevision("doc", journal.Epoch + ":9"));      // would hide a later change
        Assert.False(journal.TryRestoreRevision("doc", new CadEventJournal().Revision("doc")));  // other epoch
        Assert.False(journal.TryRestoreRevision("doc", "nonsense"));
        Assert.Equal(current, journal.Revision("doc"));
    }

    [Fact]
    public void RestoringOneDocumentLeavesTheOthersAlone()
    {
        var journal = new CadEventJournal();
        journal.Append("document_changed", "a");
        string planned = journal.Revision("a");
        journal.Append("document_changed", "a");
        journal.Append("document_changed", "b");
        string otherBefore = journal.Revision("b");

        Assert.True(journal.TryRestoreRevision("a", planned));
        Assert.Equal(otherBefore, journal.Revision("b"));
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
