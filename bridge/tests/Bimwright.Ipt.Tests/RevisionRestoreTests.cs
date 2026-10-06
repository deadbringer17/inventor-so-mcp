using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;

namespace Bimwright.Ipt.Tests;

/// <summary>
/// The helper the assembly handlers call after Abort(). The handlers themselves need Inventor and are
/// not covered here; the journal behaviour they rely on is.
/// </summary>
public sealed class RevisionRestoreTests
{
    [Fact]
    public void AbortedTransactionLeavesTheObservedRevision()
    {
        var journal = new CadEventJournal();
        var ctx = new InventorCommandContext { Events = journal };
        journal.Append("document_changed", "doc");
        string observed = journal.Revision("doc");

        // Inventor raises change events even though the transaction was aborted.
        journal.Append("document_changed", "doc");
        journal.Append("document_changed", "doc");
        Assert.NotEqual(observed, journal.Revision("doc"));

        RevisionRestore.AfterAbort(ctx, "doc", observed);

        Assert.Equal(observed, journal.Revision("doc"));
    }

    [Fact]
    public void RevisionFromAnotherEpochIsIgnored()
    {
        var journal = new CadEventJournal();
        var ctx = new InventorCommandContext { Events = journal };
        journal.Append("document_changed", "doc");
        string current = journal.Revision("doc");

        RevisionRestore.AfterAbort(ctx, "doc", new CadEventJournal().Revision("doc"));

        Assert.Equal(current, journal.Revision("doc"));
    }

    [Fact]
    public void MissingJournalOrRevisionIsANoOp()
    {
        RevisionRestore.AfterAbort(new InventorCommandContext(), "doc", "x:1");
        var journal = new CadEventJournal();
        RevisionRestore.AfterAbort(new InventorCommandContext { Events = journal }, "doc", null);
        Assert.Equal(journal.Epoch + ":0", journal.Revision("doc"));
    }
}
