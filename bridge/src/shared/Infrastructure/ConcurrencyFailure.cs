using Newtonsoft.Json.Linq;
using Bimwright.Ipt.Shared.Contracts;

namespace Bimwright.Ipt.Shared.Infrastructure;

/// <summary>
/// The three optimistic-concurrency refusals every safe write shares, built once so each handler
/// reports the same code, the same guidance and the same machine-readable fields. Handlers that
/// refuse before touching the model turn these into a result (<c>HandlerBase.Fail</c>); handlers
/// that discover the change mid-transaction throw them.
/// </summary>
public static class ConcurrencyFailure
{
    /// <summary>
    /// The request names a document other than the active one. <paramref name="phase"/> names the
    /// step during which the change was noticed, for the guards that re-check after doing work.
    /// </summary>
    public static CodedFailureException DocumentChanged(string? expectedDocumentId, string? activeDocumentId, string? phase = null)
    {
        var details = new JObject();
        if (expectedDocumentId != null) details["expected_document_id"] = expectedDocumentId;
        if (activeDocumentId != null) details["active_document_id"] = activeDocumentId;
        if (phase != null) details["phase"] = phase;
        return new CodedFailureException(InventorErrorCodes.DOCUMENT_CHANGED,
            phase == null
                ? "The active document is not the one this request targets. Read the active document again and retry."
                : "The active document changed during " + phase + "; nothing was applied. Read it again and retry.",
            details.Count == 0 ? null : details);
    }

    /// <summary>The document moved on since <c>expected_revision</c> was read.</summary>
    public static CodedFailureException StaleRevision(string? expectedRevision, string? actualRevision)
    {
        var details = new JObject();
        if (expectedRevision != null) details["expected_revision"] = expectedRevision;
        if (actualRevision != null) details["actual_revision"] = actualRevision;
        return new CodedFailureException(InventorErrorCodes.STALE_REVISION,
            "The document changed since expected_revision was read. Read it again and replan.",
            details.Count == 0 ? null : details);
    }

    /// <summary>
    /// A document anywhere in the dependency set moved, so the plan just built no longer describes
    /// what is on disk. <paramref name="changedDocumentId"/> names the one that moved.
    /// </summary>
    public static CodedFailureException DependencyChanged(string? changedDocumentId, string phase) =>
        new(InventorErrorCodes.DOCUMENT_CHANGED,
            "A document in the dependency set changed during " + phase + "; nothing was applied. Read the model again and replan.",
            new JObject { ["phase"] = phase, ["changed_document_id"] = changedDocumentId });

    /// <summary>
    /// Another transaction already owns the document, so this command cannot own one of its own and
    /// refuses rather than committing work it could not roll back.
    /// </summary>
    public static CodedFailureException TransactionBusy() =>
        new(InventorErrorCodes.TRANSACTION_BUSY,
            "Another Inventor transaction is active. Finish or cancel it in Inventor, then retry; nothing was changed.");
}
