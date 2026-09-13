#if INVENTOR2027
using System;
using System.IO;
using Path = System.IO.Path;
using File = System.IO.File;
using Environment = System.Environment;
using System.Linq;
using Inventor;
using Newtonsoft.Json.Linq;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;

namespace Bimwright.Ipt.Shared.Handlers.Core;

public sealed class CheckpointHandler : HandlerBase, IInventorCommand
{
    private readonly string _action;
    public CheckpointHandler(string action) => _action = action;
    public string Name => "checkpoint_" + _action;
    public bool IsReadOnly => _action == "list" || _action == "diff";
    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        if (!IsReadOnly && ctx.ReadOnly) return Fail(ctx, "READ_ONLY", "Checkpoint writes require write permission.");
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "InventorSO");
        var store = new CheckpointStore(Path.Combine(root, "checkpoints"));
        if (_action == "list") return Ok(ctx, store.List((string?)p["document_id"]));
        var app = (Application)ctx.Application!;
        if (_action == "diff")
        {
            if (!ActiveDocumentSupport.TryGetActivePart(ctx, Name, out _, out var active, out var failure)) return failure!;
            string activeId = "doc_" + active.InternalName;
            if ((string?)p["document_id"] != activeId) return Fail(ctx, "INVALID_ARGUMENT", "DOCUMENT_CHANGED");
            string? revision = ctx.Events?.Revision(activeId);
            if (revision == null || (string?)p["expected_revision"] != revision) return Fail(ctx, "INVALID_ARGUMENT", "STALE_REVISION");
            string key = (string?)p["checkpoint_id"] ?? "";
            var before = store.ReadSemantic(key);
            if ((string?)before["document_id"] != activeId) return Fail(ctx, "INVALID_ARGUMENT", "CHECKPOINT_DOCUMENT_MISMATCH");
            var after = PartSemanticSnapshot.Capture(active, ctx.IsDeadlineExceeded);
            if (ctx.Events!.Revision(activeId) != revision || app.ActiveDocument == null || EntityReferences.DocumentId(app.ActiveDocument) != activeId)
                return Fail(ctx, "INVALID_ARGUMENT", "DOCUMENT_CHANGED_DURING_DIFF");
            var result = PartSnapshotDiff.Compare(before, after);
            result["checkpoint_id"] = key; result["revision"] = revision;
            result["database_units"] = after["database_units"];
            return Ok(ctx, result);
        }
        if (_action == "create")
        {
            string label = (string?)p["label"] ?? "";
            CheckpointStore.ValidateLabel(label);
            if (!ActiveDocumentSupport.TryGetActivePart(ctx, Name, out _, out var part, out var failure)) return failure!;
            if (part.ReferencedDocumentDescriptors.Count != 0 || part.ComponentDefinition.ModelStates.Count != 1)
                return Fail(ctx, "INVALID_ARGUMENT", "Checkpoints currently require a standalone, single-model-state part.");
            var semantic = PartSemanticSnapshot.Capture(part, ctx.IsDeadlineExceeded);
            // Reuse the source-state, revision and transaction guards from safe native artifact output.
            var output = new SaveArtifactHandler().Execute(ctx, new JObject { ["document_id"] = p["document_id"],
                ["expected_revision"] = p["expected_revision"], ["format"] = "native" });
            if (!output.Ok) return output;
            if (!JToken.DeepEquals(semantic, PartSemanticSnapshot.Capture(part, ctx.IsDeadlineExceeded)))
                throw new InvalidOperationException("SEMANTIC_STATE_CHANGED_DURING_CHECKPOINT: artifact retained, checkpoint not cataloged.");
            var checkpoint = store.Create((string)p["document_id"]!, (string)p["expected_revision"]!, label,
                destination => File.Copy((string)output.Data!["path"]!, destination, false), ctx.IsDeadlineExceeded, semantic);
            checkpoint["revision"] = ctx.Events?.Revision((string)p["document_id"]!);
            checkpoint["source_saved_in_place"] = false;
            checkpoint["snapshot_artifact_path"] = output.Data!["path"];
            return Ok(ctx, checkpoint);
        }
        if (_action != "restore") throw new InvalidOperationException("Unknown checkpoint action.");
        string checkpointId = (string?)p["checkpoint_id"] ?? "";
        var metadata = store.Read(checkpointId);
        string documentId = (string)metadata["document_id"]!;
        if (app.Documents.Cast<global::Inventor.Document>().Any(d => EntityReferences.DocumentId(d) == documentId))
            return Fail(ctx, "INVALID_ARGUMENT", "SOURCE_STILL_OPEN: close the source safely before opening a recovery copy; no document will be closed automatically.");
        if (app.ActiveDocument != null)
        {
            var probe = app.TransactionManager.StartTransaction((Inventor._Document)app.ActiveDocument, "Inventor SO recovery ownership check");
            bool nested = probe.HasParentTransaction;
            probe.Abort();
            if (nested) return Fail(ctx, "INVALID_ARGUMENT", "TRANSACTION_BUSY");
        }
        var path = store.RecoveryCopy(checkpointId, Path.Combine(root, "recoveries"), ctx.IsDeadlineExceeded);
        if (ctx.IsDeadlineExceeded?.Invoke() == true) throw new TimeoutException("Expired before recovery open; recovery file retained.");
        var recovered = app.Documents.Open(path, true);
        if (EntityReferences.DocumentId(recovered) != documentId || !string.Equals(recovered.FullFileName, path, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("RECOVERY_IDENTITY_MISMATCH: inspect open documents; no document was closed.");
        return Ok(ctx, new JObject { ["status"] = "recovery_copy_opened", ["checkpoint_id"] = checkpointId,
            ["document_id"] = documentId, ["revision"] = ctx.Events?.Revision(documentId), ["path"] = path,
            ["source_overwritten"] = false, ["checkpoint_modified"] = false });
    }
}
#endif
