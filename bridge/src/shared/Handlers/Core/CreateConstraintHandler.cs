#if INVENTOR2027
using System;
using System.Linq;
using Inventor;
using Newtonsoft.Json.Linq;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;

namespace Bimwright.Ipt.Shared.Handlers.Core;

public sealed class CreateConstraintHandler : HandlerBase, IInventorCommand
{
    public string Name => "create_constraint_safe";
    public bool IsReadOnly => false;
    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        if (ctx.ReadOnly) return Fail(ctx, "READ_ONLY", "Constraint creation requires write permission.");
        if (!ActiveDocumentSupport.TryGetActiveAssembly(ctx, Name, out var app, out var assembly, out var failure)) return failure!;
        var doc = (global::Inventor.Document)assembly;
        string id = EntityReferences.DocumentId(doc);
        if ((string?)p["document_id"] != id) return Fail(ctx, "INVALID_ARGUMENT", "DOCUMENT_CHANGED");
        if (ctx.Events == null || (string?)p["expected_revision"] != ctx.Events.Revision(id)) return Fail(ctx, "INVALID_ARGUMENT", "STALE_REVISION");
        var request = ConstraintCreateRequest.Parse(p);
        var a = EntityReferences.ResolvePlanarAssemblyFace(doc, request.FaceA);
        var b = EntityReferences.ResolvePlanarAssemblyFace(doc, request.FaceB);
        var def = assembly.ComponentDefinition;
        var direct = def.Occurrences.Cast<ComponentOccurrence>().ToArray();
        foreach (var face in new[] { a, b })
        {
            var occurrence = face.ContainingOccurrence;
            if (!direct.Any(o => ReferenceEquals(o, occurrence)) || occurrence.Suppressed || occurrence.Adaptive ||
                occurrence.DefinitionDocumentType != DocumentTypeEnum.kPartDocumentObject)
                return Fail(ctx, "INVALID_ARGUMENT", "Only unsuppressed nonadaptive direct part occurrences are supported.");
        }
        if (ReferenceEquals(a.ContainingOccurrence, b.ContainingOccurrence))
            return Fail(ctx, "INVALID_ARGUMENT", "Faces must belong to different occurrences.");
        bool priorUi = app.UserInterfaceManager.UserInteractionDisabled;
        Transaction? transaction = null;
        void Owned()
        {
            if (transaction == null || !ReferenceEquals(app.TransactionManager.CurrentTransaction, transaction))
                throw new InvalidOperationException("TRANSACTION_OWNERSHIP_LOST");
        }
        try
        {
            if (ctx.IsDeadlineExceeded?.Invoke() == true) throw new TimeoutException("Expired before constraint creation.");
            app.UserInterfaceManager.UserInteractionDisabled = true;
            transaction = app.TransactionManager.StartTransaction((Inventor._Document)doc, "Inventor SO create constraint");
            if (transaction.HasParentTransaction) throw new InvalidOperationException("TRANSACTION_BUSY");
            AssemblyConstraint created = request.Type == "flush"
                ? (AssemblyConstraint)def.Constraints.AddFlushConstraint(a, b, request.OffsetMm / 10)
                : (AssemblyConstraint)def.Constraints.AddMateConstraint(a, b, request.OffsetMm / 10);
            if (!doc.Update2()) throw new InvalidOperationException("Assembly rebuild failed.");
            foreach (AssemblyConstraint c in def.Constraints)
                if (!c.Suppressed && c.HealthStatus != HealthStatusEnum.kUpToDateHealth)
                    throw new InvalidOperationException("CONSTRAINT_UNHEALTHY: " + c.Name);
            var components = def.Occurrences.Cast<ComponentOccurrence>().Where(o => !o.Suppressed).ToArray();
            int checks = 0;
            for (int i = 0; i < components.Length; i++) for (int j = i + 1; j < components.Length; j++)
            {
                if (ctx.IsDeadlineExceeded?.Invoke() == true) throw new TimeoutException("Expired during assembly validation.");
                var pair = app.TransientObjects.CreateObjectCollection(); pair.Add(components[i]); pair.Add(components[j]);
                if (def.AnalyzeInterference(pair).Count > 0) throw new InvalidOperationException("INTERFERENCE");
                double distance = app.MeasureTools.GetMinimumDistance(components[i], components[j]) * 10;
                if (double.IsNaN(distance) || double.IsInfinity(distance) || distance < request.ClearanceMm)
                    throw new InvalidOperationException("CLEARANCE_FAILED: " + distance);
                checks++;
            }
            var result = new JObject { ["type"] = request.Type, ["offset_mm"] = request.OffsetMm, ["pairs_checked"] = checks,
                ["constraint_id"] = request.Preview ? null : EntityReferences.Describe(doc, created)["id"] };
            Owned();
            if (ctx.IsDeadlineExceeded?.Invoke() == true) throw new TimeoutException("Expired before commit.");
            if (app.ActiveDocument == null || EntityReferences.DocumentId(app.ActiveDocument) != id) throw new InvalidOperationException("DOCUMENT_CHANGED");
            if (request.Preview) transaction.Abort(); else transaction.End();
            transaction = null;
            result["status"] = request.Preview ? "preview_rolled_back" : "committed";
            result["revision"] = ctx.Events.Revision(id);
            return Ok(ctx, result);
        }
        catch (Exception ex)
        {
            if (transaction != null)
            {
                try { Owned(); transaction.Abort(); }
                catch (Exception rollback) { throw new InvalidOperationException("ROLLBACK_FAILED: " + rollback.Message, ex); }
                throw new InvalidOperationException("ROLLED_BACK: " + ex.Message, ex);
            }
            throw;
        }
        finally { app.UserInterfaceManager.UserInteractionDisabled = priorUi; }
    }
}
#endif
