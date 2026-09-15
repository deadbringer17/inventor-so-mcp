#if INVENTOR2027
using System;
using System.Linq;
using Inventor;
using Newtonsoft.Json.Linq;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Handlers;
using Bimwright.Ipt.Shared.Infrastructure;

namespace Bimwright.Ipt.Shared.Handlers.Core;

/// <summary>
/// <c>ground_component_safe</c> — ground or unground one direct occurrence.
/// A grounded component is the fixed reference everything else resolves against; without one, an
/// assembly solves into an arbitrary position. Grounding does not move anything, so the usual
/// clearance and interference validation would prove nothing and is not claimed here — the rebuild
/// and health of every constraint and joint is still verified.
/// </summary>
public sealed class GroundComponentHandler : HandlerBase, IInventorCommand
{
    public string Name => "ground_component_safe";
    public bool IsReadOnly => false;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        if (ctx.ReadOnly) return Fail(ctx, InventorErrorCodes.READ_ONLY, "Grounding requires write permission.");
        if (!ActiveDocumentSupport.TryGetActiveAssembly(ctx, Name, out var app, out var assembly, out var failure)) return failure!;
        var doc = (global::Inventor.Document)assembly;
        string id = EntityReferences.DocumentId(doc);
        if ((string?)p["document_id"] != id) return Fail(ctx, ConcurrencyFailure.DocumentChanged((string?)p["document_id"], id));
        if (ctx.Events == null || (string?)p["expected_revision"] != ctx.Events.Revision(id))
            return Fail(ctx, ConcurrencyFailure.StaleRevision((string?)p["expected_revision"], ctx.Events?.Revision(id)));
        if (p["grounded"]?.Type != JTokenType.Boolean)
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "grounded must be true or false.");
        bool grounded = (bool)p["grounded"]!;

        var def = assembly.ComponentDefinition;
        ComponentOccurrence occurrence;
        try { occurrence = EntityReferences.ResolveOccurrence(doc, (string?)p["component_id"] ?? ""); }
        catch (ArgumentException ex) { return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, ex.Message); }
        if (!def.Occurrences.Cast<ComponentOccurrence>().Any(o => ReferenceEquals(o, occurrence)))
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "Only direct occurrences of the active assembly are supported.");
        if (occurrence.Suppressed)
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "A suppressed occurrence cannot be grounded.");
        if (occurrence.Grounded == grounded)
            return Ok(ctx, new JObject { ["status"] = "unchanged", ["component_id"] = (string?)p["component_id"],
                ["component_name"] = occurrence.Name, ["grounded"] = grounded,
                ["document_id"] = id, ["revision"] = ctx.Events.Revision(id) });

        Transaction? transaction = null;
        void Owned()
        {
            if (transaction == null || !ReferenceEquals(app.TransactionManager.CurrentTransaction, transaction))
                throw new InvalidOperationException("TRANSACTION_OWNERSHIP_LOST");
        }
        try
        {
            if (ctx.IsDeadlineExceeded?.Invoke() == true) throw new TimeoutException("Expired before grounding.");
            transaction = app.TransactionManager.StartTransaction((Inventor._Document)doc, "Inventor SO ground component");
            if (transaction.HasParentTransaction) throw ConcurrencyFailure.TransactionBusy();
            occurrence.Grounded = grounded;
            if (!doc.Update2()) throw new InvalidOperationException("Assembly rebuild failed.");
            foreach (AssemblyConstraint constraint in def.Constraints)
                if (!constraint.Suppressed && constraint.HealthStatus != HealthStatusEnum.kUpToDateHealth)
                    throw new InvalidOperationException("CONSTRAINT_UNHEALTHY: " + constraint.Name);
            foreach (AssemblyJoint joint in def.Joints)
                if (!joint.Suppressed && joint.HealthStatus != HealthStatusEnum.kUpToDateHealth)
                    throw new InvalidOperationException("JOINT_UNHEALTHY: " + joint.Name);
            if (occurrence.Grounded != grounded)
                throw new InvalidOperationException("GROUND_NOT_APPLIED: Inventor did not keep the requested state.");
            Owned();
            if (app.ActiveDocument == null || EntityReferences.DocumentId(app.ActiveDocument) != id)
                throw ConcurrencyFailure.DocumentChanged(id, app.ActiveDocument == null ? null : EntityReferences.DocumentId(app.ActiveDocument), "the grounding change");
            transaction.End();
            transaction = null;
            return Ok(ctx, new JObject
            {
                ["status"] = "committed",
                ["component_id"] = (string?)p["component_id"],
                ["component_name"] = occurrence.Name,
                ["grounded"] = grounded,
                ["grounded_count"] = def.Occurrences.Cast<ComponentOccurrence>().Count(o => o.Grounded),
                ["document_id"] = id,
                ["revision"] = ctx.Events.Revision(id),
            });
        }
        catch (Exception ex)
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
    }
}
#endif
