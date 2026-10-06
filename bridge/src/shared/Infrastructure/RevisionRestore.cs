using System;
using Bimwright.Ipt.Shared.Contracts;

namespace Bimwright.Ipt.Shared.Infrastructure;

/// <summary>
/// Puts a document's revision token back after a transaction that was aborted in full. Inventor still
/// raises change events when a transaction is aborted (verified live on 2027), so without this a
/// preview or a rolled-back refusal would advance the revision and invalidate the caller's plan even
/// though the model is untouched. <c>AtomicCadBatch</c> does the same for batches.
/// </summary>
internal static class RevisionRestore
{
    /// <summary>
    /// Call only after the abort succeeded. Never throws: the preview or refusal already stands, and
    /// <see cref="CadEventJournal.TryRestoreRevision"/> refuses a token from another epoch and never
    /// moves a revision forward, so a failed restore only costs the caller a re-read.
    /// </summary>
    public static void AfterAbort(InventorCommandContext ctx, string documentId, string? observedRevision)
    {
        if (ctx.Events == null || documentId == null || observedRevision == null) return;
        try { ctx.Events.TryRestoreRevision(documentId, observedRevision); }
        catch (Exception) { /* the preview or refusal already stands */ }
    }
}
