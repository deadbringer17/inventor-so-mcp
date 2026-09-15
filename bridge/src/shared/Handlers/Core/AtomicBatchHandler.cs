#if INVENTOR2027
using System;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;
using Inventor;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Handlers.Core;

public sealed class AtomicBatchHandler : IInventorCommand
{
    public string Name => "atomic_batch";
    public bool IsReadOnly => false;
    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        if (ctx.ReadOnly) return InventorCommandResult.Fail(Guid.Empty, InventorErrorCodes.READ_ONLY, "Batch requires write permission", new());
        if (p["operations"] is not JArray operations) return InventorCommandResult.Fail(Guid.Empty, InventorErrorCodes.INVALID_ARGUMENT, "operations must be an array", new());
        using var backend = new InventorBatchBackend(ctx);
        try
        {
            var data = AtomicCadBatch.Run(backend, (string?)p["document_id"] ?? "", (string?)p["expected_revision"] ?? "",
                operations, (bool?)p["preview"] ?? false, ctx.IsDeadlineExceeded);
            return InventorCommandResult.Success(Guid.Empty, data, new());
        }
        catch (CadBatchException failure)
        {
            // The batch already knows its own code, failing step and command; reporting that as a
            // sanitized API_ERROR string would force the caller to parse the sentence back apart.
            return InventorCommandResult.Fail(Guid.Empty, failure.Code, failure.Message, failure.Details(), new());
        }
        catch (CodedFailureException failure)
        {
            // TRANSACTION_BUSY from Begin: a refusal raised before any operation ran, so it belongs
            // to no step. Report it under its own code rather than letting it reach the dispatcher's
            // catch-all as an API_ERROR whose message happens to start with the code.
            return InventorCommandResult.Fail(Guid.Empty, failure.Code, failure.Message, failure.Details, new());
        }
    }
}

internal sealed class InventorBatchBackend : ICadBatchBackend, IDisposable
{
    private readonly InventorCommandContext _ctx;
    private readonly Application _app;
    private readonly global::Inventor.Document _doc;
    private Transaction? _transaction;
    private bool? _priorInteractionDisabled;
    public InventorBatchBackend(InventorCommandContext ctx)
    {
        _ctx = ctx;
        _app = (Application)ctx.Application!;
        _doc = _app.ActiveDocument ?? throw new CodedFailureException(InventorErrorCodes.NO_DOCUMENT, "No active Inventor document.");
        if (_doc.DocumentType != DocumentTypeEnum.kPartDocumentObject) throw new InvalidOperationException("Atomic modeling batch currently requires a part document.");
        if (ctx.Events == null) throw new InvalidOperationException("EVENTS_UNAVAILABLE: cannot verify concurrency.");
    }
    public string DocumentId => _app.ActiveDocument == null ? "" : EntityReferences.DocumentId(_app.ActiveDocument);
    public string Revision => _ctx.Events!.Revision(EntityReferences.DocumentId(_doc));
    public void Begin(string name)
    {
        _priorInteractionDisabled = _app.UserInterfaceManager.UserInteractionDisabled;
        _app.UserInterfaceManager.UserInteractionDisabled = true;
        _transaction = _app.TransactionManager.StartTransaction((_Document)_doc, name);
        // Inventor returns an unidentified transaction even when idle. Only a newly
        // started identified transaction can reliably report whether it is nested.
        // Abort only our empty transaction; never end or abort the existing parent.
        try
        {
            if (_transaction.HasParentTransaction)
                throw ConcurrencyFailure.TransactionBusy();
        }
        catch
        {
            Rollback();
            throw;
        }
    }
    public JObject Execute(string command, JObject arguments)
    {
        EnsureOwned();
        if (_ctx.Commands == null || !_ctx.Commands.TryGetValue(command, out var handler)) throw new ArgumentException("Unregistered batch command " + command);
        var result = handler.Execute(_ctx, arguments);
        // Carry the failing handler's own code structurally; AtomicCadBatch lifts it into the
        // batch result's details.step_code rather than re-parsing it out of a sentence.
        if (!result.Ok) throw new CodedFailureException(result.Error?.Code ?? InventorErrorCodes.API_ERROR,
            result.Error?.Message ?? "The step failed without a message.", result.Error?.Details);
        return new JObject { ["command"] = command, ["data"] = result.Data };
    }
    public void Validate()
    {
        EnsureOwned();
        if (!_doc.Update2()) throw new InvalidOperationException("Rebuild failed.");
        var part = (PartDocument)_doc;
        foreach (PartFeature feature in part.ComponentDefinition.Features)
            if (!feature.Suppressed && feature.HealthStatus != HealthStatusEnum.kUpToDateHealth)
                throw new InvalidOperationException("Feature is not healthy: " + feature.Name + " (" + feature.HealthStatus + ")");
    }
    private void EnsureOwned()
    {
        if (_transaction == null || !ReferenceEquals(_app.TransactionManager.CurrentTransaction, _transaction))
            throw new InvalidOperationException("TRANSACTION_OWNERSHIP_LOST: refusing to end another transaction.");
    }
    public void Commit() { EnsureOwned(); _transaction!.End(); _transaction = null; }
    public void RestoreRevision(string revision) => _ctx.Events!.TryRestoreRevision(EntityReferences.DocumentId(_doc), revision);
    public void Rollback() { if (_transaction != null) { EnsureOwned(); _transaction.Abort(); _transaction = null; } }
    public void Dispose()
    {
        // The runner owns rollback; restore user interaction even if rollback itself failed.
        if (_priorInteractionDisabled.HasValue) _app.UserInterfaceManager.UserInteractionDisabled = _priorInteractionDisabled.Value;
    }
}
#endif
