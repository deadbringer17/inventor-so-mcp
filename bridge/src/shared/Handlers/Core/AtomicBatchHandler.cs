#if INVENTOR2027
using System;
using System.Collections.Generic;
using System.Linq;
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
        try
        {
            using var backend = new InventorBatchBackend(ctx);
            var options = new CadBatchOptions
            {
                DocumentKind = backend.DocumentKind,
                // Host-owned: the add-in's own environment decides, never the request.
                AllowExperimental = ctx.AllowExperimental,
                IsRegistered = command => ctx.Commands != null && ctx.Commands.ContainsKey(command),
                SupportsCheck = InventorBatchBackend.SupportsCheck,
                Validate = p["validate"],
                IncludePreviewMesh = (bool?)p["include_preview_mesh"] ?? false,
            };
            var data = AtomicCadBatch.Run(backend, (string?)p["document_id"] ?? "", (string?)p["expected_revision"] ?? "",
                operations, (bool?)p["preview"] ?? false, ctx.IsDeadlineExceeded, options);
#if SO_EXPERIMENTAL
            if (ctx.AllowExperimental && (string?)data["status"] == "committed" && backend.CommittedTransactionId != null
                && (backend.DocumentKind == CadDocumentKinds.Part || backend.DocumentKind == CadDocumentKinds.Assembly)
                && p["history_owner"]?.Type == JTokenType.String)
            {
                // Recording must never turn a successful CAD commit into a reported rollback.
                try { data["history_ticket"] = Experimental.XrHistoryState.For(ctx).History.Record(
                    (string)p["history_owner"]!, (string)data["document_id"]!, (string)p["expected_revision"]!,
                    (string)data["revision"]!, backend.CommittedTransactionId); }
                catch { data["history_unavailable"] = true; }
            }
#endif
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
            // TRANSACTION_BUSY from Begin, NO_DOCUMENT/WRONG_DOCUMENT_TYPE from the backend: refusals
            // raised before any operation ran, so they belong to no step. Report them under their
            // own code rather than letting them reach the dispatcher's catch-all as an API_ERROR.
            return InventorCommandResult.Fail(Guid.Empty, failure.Code, failure.Message, failure.Details, new());
        }
    }
}

internal sealed class InventorBatchBackend : ICadBatchBackend, ICadBatchValidatingBackend, ICadBatchPreviewBackend, IDisposable
{
    /// <summary>Pairwise interference/clearance is O(n²) COM calls; beyond this the check is refused, not truncated.</summary>
    private const int MaxClearanceOccurrences = 60;

    private readonly InventorCommandContext _ctx;
    private readonly Application _app;
    private readonly global::Inventor.Document _doc;
    private Transaction? _transaction;
    public string? CommittedTransactionId { get; private set; }
    private bool? _priorInteractionDisabled;
    public InventorBatchBackend(InventorCommandContext ctx)
    {
        _ctx = ctx;
        _app = (Application)ctx.Application!;
        _doc = _app.ActiveDocument ?? throw new CodedFailureException(InventorErrorCodes.NO_DOCUMENT, "No active Inventor document.");
        DocumentKind = _doc.DocumentType switch
        {
            DocumentTypeEnum.kPartDocumentObject => CadDocumentKinds.Part,
            DocumentTypeEnum.kAssemblyDocumentObject => CadDocumentKinds.Assembly,
            DocumentTypeEnum.kDrawingDocumentObject => CadDocumentKinds.Drawing,
            _ => throw new CodedFailureException(InventorErrorCodes.WRONG_DOCUMENT_TYPE,
                "Atomic batches run in part, assembly or drawing documents."),
        };
        if (ctx.Events == null) throw new InvalidOperationException("EVENTS_UNAVAILABLE: cannot verify concurrency.");
    }

    public string DocumentKind { get; }
    public string DocumentId => _app.ActiveDocument == null ? "" : EntityReferences.DocumentId(_app.ActiveDocument);
    public string Revision => _ctx.Events!.Revision(EntityReferences.DocumentId(_doc));

    public JObject CapturePreview()
    {
        EnsureOwned();
#if SO_EXPERIMENTAL
        if (_doc is AssemblyDocument assembly) return Experimental.AssemblyPreviewMesh.Capture(_ctx, assembly);
        // Preview topology disappears at Abort: never export portable IDs for it.
        var result = new Experimental.GetDisplayMeshHandler().Execute(_ctx, new JObject
        {
            ["document_id"] = EntityReferences.DocumentId(_doc),
            ["include_face_ids"] = false, ["tolerance_mm"] = 0.1, ["max_triangles"] = 500_000,
        });
        if (!result.Ok) throw new CodedFailureException(result.Error?.Code ?? InventorErrorCodes.API_ERROR,
            result.Error?.Message ?? "Preview tessellation failed.", result.Error?.Details);
        var payload = (JObject)result.Data!;
        var sketches = new JArray();
        var part = (PartDocument)_doc;
        int entities = 0;
        foreach (PlanarSketch sketch in part.ComponentDefinition.Sketches)
        {
            if (sketches.Count >= 256 || (entities += sketch.SketchEntities.Count) > 20000)
                throw new ArgumentException("Preview sketch geometry exceeds the supported limit.");
            var snapshot = new Experimental.GetSketchInfoHandler().Execute(_ctx, new JObject
            { ["sketch_name"] = sketch.Name, ["max_entities"] = 20000 });
            if (!snapshot.Ok) throw new CodedFailureException(snapshot.Error?.Code ?? InventorErrorCodes.API_ERROR,
                snapshot.Error?.Message ?? "Sketch preview failed.", snapshot.Error?.Details);
            var data = (JObject)snapshot.Data!;
            data["frame"] = Experimental.DesignContextXrHandler.SketchFrame(_app, sketch);
            data["visible"] = sketch.Visible;
            sketches.Add(data);
        }
        payload["sketches"] = sketches;
        return payload;
#else
        throw new CodedFailureException(InventorErrorCodes.EXPERIMENTAL_DISABLED, "Preview meshes require an experimental build.");
#endif
    }

    /// <summary>Checks this build can run. Sketch and drawing checks ride on the experimental build.</summary>
    public static bool SupportsCheck(string name) => name switch
    {
        ValidationSpec.Rebuild or ValidationSpec.FeatureHealth or ValidationSpec.ConstraintHealth
            or ValidationSpec.Interference or ValidationSpec.MinClearance => true,
#if SO_EXPERIMENTAL
        ValidationSpec.SketchFullyConstrained or ValidationSpec.DrawingReferences => true,
#endif
        _ => false,
    };

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
        InventorCommandResult result;
        var previous = _ctx.OwnedBatchTransaction;
        _ctx.OwnedBatchTransaction = _transaction;
        try { result = handler.Execute(_ctx, arguments); }
        finally { _ctx.OwnedBatchTransaction = previous; }
        // Carry the failing handler's own code structurally; AtomicCadBatch lifts it into the
        // batch result's details.step_code rather than re-parsing it out of a sentence.
        if (!result.Ok) throw new CodedFailureException(result.Error?.Code ?? InventorErrorCodes.API_ERROR,
            result.Error?.Message ?? "The step failed without a message.", result.Error?.Details);
        return new JObject { ["command"] = command, ["data"] = result.Data };
    }

    /// <summary>The checks every batch of this document kind runs, whatever the caller asked.</summary>
    public void Validate()
    {
        EnsureOwned();
        if (!_doc.Update2()) throw new InvalidOperationException("Rebuild failed.");
        if (_doc is PartDocument part)
        {
            foreach (PartFeature feature in part.ComponentDefinition.Features)
                if (!feature.Suppressed && feature.HealthStatus != HealthStatusEnum.kUpToDateHealth)
                    throw new InvalidOperationException("Feature is not healthy: " + feature.Name + " (" + feature.HealthStatus + ")");
        }
        else if (_doc is AssemblyDocument assembly)
        {
            var def = assembly.ComponentDefinition;
            foreach (AssemblyConstraint constraint in def.Constraints)
                if (!constraint.Suppressed && constraint.HealthStatus != HealthStatusEnum.kUpToDateHealth)
                    throw Failed(ValidationSpec.ConstraintHealth, "Constraint is not healthy: " + constraint.Name + " (" + constraint.HealthStatus + ")");
            foreach (AssemblyJoint joint in def.Joints)
                if (!joint.Suppressed && joint.HealthStatus != HealthStatusEnum.kUpToDateHealth)
                    throw Failed(ValidationSpec.ConstraintHealth, "Joint is not healthy: " + joint.Name + " (" + joint.HealthStatus + ")");
        }
    }

    /// <summary>The caller's extra checks, after the defaults passed.</summary>
    public void Validate(ValidationSpec spec)
    {
        EnsureOwned();
        foreach (var rule in spec.Rules)
        {
            if (_ctx.IsDeadlineExceeded?.Invoke() == true) throw new TimeoutException("Expired during validation.");
            switch (rule.Name)
            {
                case ValidationSpec.Rebuild:
                case ValidationSpec.FeatureHealth:
                case ValidationSpec.ConstraintHealth:
                    break; // Covered by Validate() for this document kind.
                case ValidationSpec.Interference:
                    CheckPairs(null);
                    break;
                case ValidationSpec.MinClearance:
                    CheckPairs(rule.ValueMm ?? 0);
                    break;
#if SO_EXPERIMENTAL
                case ValidationSpec.SketchFullyConstrained:
                case ValidationSpec.DrawingReferences:
                    Bimwright.Ipt.Shared.Handlers.Experimental.ExperimentalValidators.Run(_doc, rule.Name);
                    break;
#endif
                default:
                    throw Failed(rule.Name, "This add-in build cannot run '" + rule.Name + "'.");
            }
        }
    }

    /// <summary>Interference (clearanceMm null) or minimum clearance between every pair of unsuppressed occurrences.</summary>
    private void CheckPairs(double? clearanceMm)
    {
        if (_doc is not AssemblyDocument assembly) return;
        var def = assembly.ComponentDefinition;
        var components = def.Occurrences.Cast<ComponentOccurrence>().Where(o => !o.Suppressed).ToArray();
        if (components.Length > MaxClearanceOccurrences)
            throw Failed(clearanceMm == null ? ValidationSpec.Interference : ValidationSpec.MinClearance,
                "Refusing a pairwise check over " + components.Length + " top-level occurrences (limit " + MaxClearanceOccurrences + ").");
        for (int i = 0; i < components.Length; i++)
            for (int j = i + 1; j < components.Length; j++)
            {
                if (_ctx.IsDeadlineExceeded?.Invoke() == true) throw new TimeoutException("Expired during assembly validation.");
                var pair = _app.TransientObjects.CreateObjectCollection();
                pair.Add(components[i]);
                pair.Add(components[j]);
                if (def.AnalyzeInterference(pair).Count > 0)
                    throw Failed(ValidationSpec.Interference, "Interference: " + components[i].Name + "/" + components[j].Name,
                        new JObject { ["pair"] = new JArray(components[i].Name, components[j].Name) });
                if (clearanceMm is { } required)
                {
                    double distance = UnitConvert.CmToMm(_app.MeasureTools.GetMinimumDistance(components[i], components[j]));
                    if (double.IsNaN(distance) || double.IsInfinity(distance) || distance < required)
                        throw Failed(ValidationSpec.MinClearance, "Clearance " + distance.ToString("0.###") + " mm < " + required + " mm: " +
                            components[i].Name + "/" + components[j].Name,
                            new JObject { ["pair"] = new JArray(components[i].Name, components[j].Name), ["distance_mm"] = distance, ["required_mm"] = required });
                }
            }
    }

    private static CodedFailureException Failed(string check, string message, JObject? extra = null)
    {
        var details = extra ?? new JObject();
        details["check"] = check;
        return new CodedFailureException(InventorErrorCodes.VALIDATION_FAILED, message, details);
    }

    private void EnsureOwned()
    {
        if (_transaction == null || !ReferenceEquals(_app.TransactionManager.CurrentTransaction, _transaction))
            throw new InvalidOperationException("TRANSACTION_OWNERSHIP_LOST: refusing to end another transaction.");
    }
    public void Commit()
    {
        EnsureOwned();
        _transaction!.MergeWithPrevious = false;
        string id = _transaction.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _transaction.End(); _transaction = null; CommittedTransactionId = id;
        ObserveOwnedEnd();
    }
    public void RestoreRevision(string revision) => _ctx.Events!.TryRestoreRevision(EntityReferences.DocumentId(_doc), revision);
    public void Rollback()
    {
        if (_transaction == null) return;
        EnsureOwned(); bool root = !_transaction.HasParentTransaction;
        _transaction.Abort(); _transaction = null;
        if (root) ObserveOwnedEnd();
    }
    private void ObserveOwnedEnd()
    {
#if SO_EXPERIMENTAL
        // An observational failure after End must not misreport a committed transaction.
        try { Experimental.XrHistoryState.For(_ctx).ObserveOwnedEnd(_app); } catch { }
#endif
    }
    public void Dispose()
    {
        // The runner owns rollback; restore user interaction even if rollback itself failed.
        if (_priorInteractionDisabled.HasValue) _app.UserInterfaceManager.UserInteractionDisabled = _priorInteractionDisabled.Value;
    }
}
#endif
