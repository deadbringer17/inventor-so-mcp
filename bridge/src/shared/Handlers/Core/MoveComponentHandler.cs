#if INVENTOR2027
using System;
using System.Linq;
using Inventor;
using Newtonsoft.Json.Linq;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;

namespace Bimwright.Ipt.Shared.Handlers.Core;

/// <summary>Single owned assembly translation with mandatory all-neighbor clearance validation.</summary>
public sealed class MoveComponentHandler : HandlerBase, IInventorCommand
{
    private readonly bool _insert;
    public MoveComponentHandler(bool insert = false) => _insert = insert;
    public string Name => _insert ? "insert_component_safe" : "move_component_safe";
    public bool IsReadOnly => false;
    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        if (ctx.ReadOnly) return Fail(ctx, InventorErrorCodes.READ_ONLY, "Component movement requires write permission.");
        if (!ActiveDocumentSupport.TryGetActiveAssembly(ctx, Name, out var app, out var assembly, out var failure)) return failure!;
        var doc = (global::Inventor.Document)assembly;
        string id = EntityReferences.DocumentId(doc);
        if ((string?)p["document_id"] != id) return Fail(ctx, "INVALID_ARGUMENT", "DOCUMENT_CHANGED");
        if (ctx.Events == null || (string?)p["expected_revision"] != ctx.Events.Revision(id)) return Fail(ctx, "INVALID_ARGUMENT", "STALE_REVISION");
        var move = ComponentMoveRequest.Parse(p);
        var def = assembly.ComponentDefinition;
        ComponentOccurrence? occurrence = null;
        PartDocument? source = null;
        string? sourcePath = null;
        if (_insert)
        {
            var sources = app.Documents.Cast<global::Inventor.Document>().Where(d => EntityReferences.DocumentId(d) == (string?)p["source_document_id"]).ToArray();
            if (sources.Length != 1 || sources[0] is not PartDocument part || part.Dirty || string.IsNullOrWhiteSpace(part.FullFileName) || !System.IO.File.Exists(part.FullFileName))
                return Fail(ctx, "INVALID_ARGUMENT", "SOURCE_NOT_READY: a unique already-open, saved, clean part document is required.");
            if (part.ComponentDefinition.ModelStates.Count != 1)
                return Fail(ctx, "INVALID_ARGUMENT", "Multiple model states are not supported for insertion yet.");
            source = part;
            sourcePath = part.FullFileName;
        }
        else
        {
            occurrence = EntityReferences.ResolveOccurrence(doc, (string?)p["component_id"] ?? "");
            if (!def.Occurrences.Cast<ComponentOccurrence>().Any(o => ReferenceEquals(o, occurrence)))
                return Fail(ctx, "INVALID_ARGUMENT", "Only direct assembly occurrences are supported for translation.");
            if (occurrence.Grounded || occurrence.Suppressed || occurrence.Constraints.Count > 0 || occurrence.Joints.Count > 0)
                return Fail(ctx, "INVALID_ARGUMENT", "Component is grounded, suppressed or constrained; edit its constraints instead of forcing movement.");
        }
        bool priorUi = app.UserInterfaceManager.UserInteractionDisabled;
        Transaction? transaction = null;
        void EnsureOwned()
        {
            if (transaction == null || !ReferenceEquals(app.TransactionManager.CurrentTransaction, transaction))
                throw new InvalidOperationException("TRANSACTION_OWNERSHIP_LOST");
        }
        try
        {
            if (ctx.IsDeadlineExceeded?.Invoke() == true) throw new TimeoutException("Expired before movement");
            app.UserInterfaceManager.UserInteractionDisabled = true;
            transaction = app.TransactionManager.StartTransaction((Inventor._Document)doc, "Inventor SO component translation");
            if (transaction.HasParentTransaction) throw new InvalidOperationException("TRANSACTION_BUSY");
            var original = _insert ? app.TransientGeometry.CreateMatrix() : occurrence!.Transformation;
            var target = original.Copy();
            var matrix = new double[4,4];
            for (int row=0;row<4;row++) for (int col=0;col<4;col++) matrix[row,col]=original.Cell[row+1,col+1];
            var transformed = ComponentPoseMath.Apply(matrix, move);
            for (int row=0;row<4;row++) for (int col=0;col<4;col++) target.Cell[row+1,col+1]=transformed[row,col];
            if (_insert)
            {
                occurrence = def.Occurrences.Add(sourcePath!, target);
                occurrence.Grounded = false;
                if (occurrence.Adaptive) throw new InvalidOperationException("Adaptive insertion is not supported.");
            }
            else occurrence!.Transformation = target;
            if (!doc.Update2()) throw new InvalidOperationException("Assembly rebuild failed.");
            var actual = occurrence!.Transformation;
            for (int row=1;row<=3;row++) for (int col=1;col<=4;col++)
                if (Math.Abs(actual.Cell[row,col] - target.Cell[row,col]) > 1e-7)
                    throw new InvalidOperationException("Constraints prevented requested position/orientation.");
            var checks = new JArray();
            foreach (ComponentOccurrence other in def.Occurrences)
            {
                if (ReferenceEquals(other, occurrence) || other.Suppressed) continue;
                if (ctx.IsDeadlineExceeded?.Invoke() == true) throw new TimeoutException("Deadline during clearance checks.");
                var pair = app.TransientObjects.CreateObjectCollection(); pair.Add(occurrence); pair.Add(other);
                if (def.AnalyzeInterference(pair).Count > 0) throw new InvalidOperationException("INTERFERENCE: " + other.Name);
                double distance = app.MeasureTools.GetMinimumDistance(occurrence, other) * 10;
                if (double.IsNaN(distance) || double.IsInfinity(distance) || distance < move.Clearance)
                    throw new InvalidOperationException("CLEARANCE_FAILED: " + other.Name + "; distance_mm=" + distance);
                checks.Add(new JObject { ["component_id"] = EntityReferences.Describe(doc, other)["id"], ["distance_mm"] = distance });
            }
            EnsureOwned();
            if (_insert && (source!.Dirty || source.FullFileName != sourcePath)) throw new InvalidOperationException("SOURCE_CHANGED");
            if (ctx.IsDeadlineExceeded?.Invoke() == true) throw new TimeoutException("Expired before commit.");
            if (app.ActiveDocument == null || EntityReferences.DocumentId(app.ActiveDocument) != id) throw new InvalidOperationException("DOCUMENT_CHANGED");
            JToken? resultId = _insert ? (move.Preview ? null : EntityReferences.Describe(doc, occurrence)["id"]) : p["component_id"];
            if (move.Preview) transaction.Abort(); else transaction.End();
            transaction = null;
            return Ok(ctx, new JObject { ["status"] = move.Preview ? "preview_rolled_back" : "committed",
                ["document_id"] = id, ["revision"] = ctx.Events.Revision(id), ["component_id"] = resultId,
                ["translation_mm"] = new JArray(move.X, move.Y, move.Z), ["minimum_clearance_mm"] = move.Clearance,
                ["rotation_degrees"] = move.RotationDegrees, ["rotation_axis"] = move.RotationAxis == null ? null : new JArray(move.RotationAxis),
                ["rotation_center_mm"] = move.RotationCenterMm == null ? null : new JArray(move.RotationCenterMm),
                ["checks_at_proposed_position"] = checks });
        }
        catch (Exception ex)
        {
            if (transaction != null)
            {
                try { EnsureOwned(); transaction.Abort(); }
                catch (Exception rollback) { throw new InvalidOperationException("ROLLBACK_FAILED: " + rollback.Message, ex); }
                throw new InvalidOperationException("ROLLED_BACK: " + ex.Message, ex);
            }
            throw;
        }
        finally { app.UserInterfaceManager.UserInteractionDisabled = priorUi; }
    }
}
#endif
