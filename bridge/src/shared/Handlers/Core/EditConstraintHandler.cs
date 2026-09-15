#if INVENTOR2027
using System;
using System.Linq;
using Inventor;
using Newtonsoft.Json.Linq;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;
namespace Bimwright.Ipt.Shared.Handlers.Core;

public sealed class EditConstraintHandler : HandlerBase, IInventorCommand
{
    public string Name => "edit_constraint_safe";
    public bool IsReadOnly => false;
    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        if (ctx.ReadOnly) return Fail(ctx, "READ_ONLY", "Constraint editing requires write permission.");
        if (!ActiveDocumentSupport.TryGetActiveAssembly(ctx, Name, out var app, out var assembly, out var failure)) return failure!;
        var doc = (global::Inventor.Document)assembly;
        string id = EntityReferences.DocumentId(doc);
        if ((string?)p["document_id"] != id) return Fail(ctx, ConcurrencyFailure.DocumentChanged((string?)p["document_id"], id));
        if (ctx.Events == null || (string?)p["expected_revision"] != ctx.Events.Revision(id)) return Fail(ctx, ConcurrencyFailure.StaleRevision((string?)p["expected_revision"], ctx.Events?.Revision(id)));
        double Number(string key)
        {
            var token = p[key];
            if (token == null || (token.Type != JTokenType.Float && token.Type != JTokenType.Integer)) throw new ArgumentException(key + " must be numeric.");
            double value = (double)token;
            if (double.IsNaN(value) || double.IsInfinity(value)) throw new ArgumentException(key + " must be finite.");
            return value;
        }
        double value = Number("value"), clearance = Number("minimum_clearance_mm");
        if (clearance < 0) throw new ArgumentException("Clearance cannot be negative.");
        bool preview = (bool?)p["preview"] ?? true;
        var constraint = EntityReferences.ResolveAssemblyConstraint(doc, (string?)p["constraint_id"] ?? "");
        var def = assembly.ComponentDefinition;
        if (!def.Constraints.Cast<AssemblyConstraint>().Any(c => ReferenceEquals(c, constraint)) || constraint.Suppressed)
            return Fail(ctx, "INVALID_ARGUMENT", "Constraint must be unsuppressed and belong to the active assembly.");
        Parameter parameter = constraint switch { MateConstraint m => m.Offset, FlushConstraint f => f.Offset,
            InsertConstraint i => i.Distance, AngleConstraint a => a.Angle,
            _ => throw new ArgumentException("Only mate, flush, insert and angle driving dimensions are supported.") };
        string unit = constraint is AngleConstraint ? "deg" : "mm";
        if ((string?)p["units"] != unit) throw new ArgumentException("This constraint requires units=" + unit);
        double target = unit == "deg" ? value * Math.PI / 180 : value / 10;
        if (double.IsInfinity(target)) throw new ArgumentException("Value overflow.");
        bool priorUi = app.UserInterfaceManager.UserInteractionDisabled;
        Transaction? transaction = null;
        void Owned()
        {
            if (transaction == null || !ReferenceEquals(app.TransactionManager.CurrentTransaction, transaction))
                throw new InvalidOperationException("TRANSACTION_OWNERSHIP_LOST");
        }
        try
        {
            if (ctx.IsDeadlineExceeded?.Invoke() == true) throw new TimeoutException("Expired before constraint edit.");
            app.UserInterfaceManager.UserInteractionDisabled = true;
            transaction = app.TransactionManager.StartTransaction((Inventor._Document)doc, "Inventor SO constraint edit");
            if (transaction.HasParentTransaction) throw ConcurrencyFailure.TransactionBusy();
            parameter.Value = target;
            if (!doc.Update2()) throw new InvalidOperationException("Assembly rebuild failed.");
            foreach (AssemblyConstraint c in def.Constraints)
                if (!c.Suppressed && c.HealthStatus != HealthStatusEnum.kUpToDateHealth) throw new InvalidOperationException("CONSTRAINT_UNHEALTHY: " + c.Name);
            if (Math.Abs(Convert.ToDouble(parameter.Value)-target)>1e-8) throw new InvalidOperationException("Constraint did not reach requested value.");
            var components = def.Occurrences.Cast<ComponentOccurrence>().Where(o=>!o.Suppressed).ToArray();
            int checks = 0;
            for (int i=0;i<components.Length;i++) for (int j=i+1;j<components.Length;j++)
            {
                if (ctx.IsDeadlineExceeded?.Invoke() == true) throw new TimeoutException("Expired during assembly validation.");
                var pair = app.TransientObjects.CreateObjectCollection(); pair.Add(components[i]); pair.Add(components[j]);
                if (def.AnalyzeInterference(pair).Count>0) throw new InvalidOperationException("INTERFERENCE: " + components[i].Name + "/" + components[j].Name);
                double distance = app.MeasureTools.GetMinimumDistance(components[i], components[j])*10;
                if (double.IsNaN(distance) || double.IsInfinity(distance) || distance<clearance) throw new InvalidOperationException("CLEARANCE_FAILED: " + distance);
                checks++;
            }
            Owned();
            if (ctx.IsDeadlineExceeded?.Invoke() == true) throw new TimeoutException("Expired before commit.");
            if (app.ActiveDocument == null || EntityReferences.DocumentId(app.ActiveDocument)!=id)
                throw ConcurrencyFailure.DocumentChanged(id, app.ActiveDocument == null ? null : EntityReferences.DocumentId(app.ActiveDocument), "the constraint edit");
            if (preview) transaction.Abort(); else transaction.End();
            transaction=null;
            return Ok(ctx, new JObject { ["status"] = preview ? "preview_rolled_back" : "committed", ["constraint_id"] = p["constraint_id"],
                ["value"] = value, ["units"] = unit, ["pairs_checked"] = checks, ["revision"] = ctx.Events.Revision(id) });
        }
        catch(Exception ex)
        {
            if (transaction != null)
            {
                try { Owned(); transaction.Abort(); }
                catch (Exception rollback)
                {
                    // The rollback is the part that failed, so CAD is in an unknown state: say so in
                    // the code, and keep what actually went wrong in the details.
                    throw new CodedFailureException(InventorErrorCodes.ROLLBACK_FAILED,
                        "Rollback failed; inspect the model before continuing. Failure: " + ex.Message +
                        "; rollback: " + rollback.Message, CodedFailureException.ReasonOf(ex), ex);
                }
                throw new CodedFailureException(InventorErrorCodes.ROLLED_BACK,
                    "Rolled back; nothing was changed. " + ex.Message, CodedFailureException.ReasonOf(ex), ex);
            }
            throw;
        }
        finally { app.UserInterfaceManager.UserInteractionDisabled = priorUi; }
    }
}
#endif
