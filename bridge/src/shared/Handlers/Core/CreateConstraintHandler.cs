#if INVENTOR2027
using System;
using System.Linq;
using Inventor;
using Newtonsoft.Json.Linq;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;
using Bimwright.Ipt.Shared.Handlers;

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
        ConstraintCreateRequest request;
        object a, b;
        ComponentOccurrence occurrenceA, occurrenceB;
        try
        {
            request = ConstraintCreateRequest.Parse(p);
            (a, occurrenceA) = ResolveEntity(doc, request, request.FaceA);
            (b, occurrenceB) = ResolveEntity(doc, request, request.FaceB);
        }
        catch (ArgumentException ex) { return Fail(ctx, "INVALID_ARGUMENT", ex.Message); }

        var def = assembly.ComponentDefinition;
        var direct = def.Occurrences.Cast<ComponentOccurrence>().ToArray();
        foreach (var occurrence in new[] { occurrenceA, occurrenceB })
        {
            if (!direct.Any(o => ReferenceEquals(o, occurrence)) || occurrence.Suppressed || occurrence.Adaptive ||
                occurrence.DefinitionDocumentType != DocumentTypeEnum.kPartDocumentObject)
                return Fail(ctx, "INVALID_ARGUMENT", "Only unsuppressed nonadaptive direct part occurrences are supported.");
        }
        if (ReferenceEquals(occurrenceA, occurrenceB))
            return Fail(ctx, "INVALID_ARGUMENT", "The two references must belong to different occurrences.");
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
            var constraints = def.Constraints;
            AssemblyConstraint created = request.Type switch
            {
                "flush" => (AssemblyConstraint)constraints.AddFlushConstraint(a, b, UnitConvert.MmToCm(request.OffsetMm)),
                // A cylinder passed to AddMateConstraint mates the axes, which is the intent of mate_axis.
                "mate" or "mate_axis" => (AssemblyConstraint)constraints.AddMateConstraint(a, b, UnitConvert.MmToCm(request.OffsetMm)),
                "insert" => (AssemblyConstraint)constraints.AddInsertConstraint(a, b, request.AxesOpposed, UnitConvert.MmToCm(request.OffsetMm)),
                "angle" => (AssemblyConstraint)constraints.AddAngleConstraint(a, b, UnitConvert.DegToRad(request.AngleDegrees)),
                "tangent" => (AssemblyConstraint)constraints.AddTangentConstraint(a, b, request.InsideTangency, UnitConvert.MmToCm(request.OffsetMm)),
                "symmetry" => (AssemblyConstraint)constraints.AddSymmetryConstraint(a, b,
                    EntityReferences.ResolvePlanarAssemblyFace(doc, request.SymmetryPlane)),
                "transitional" => (AssemblyConstraint)constraints.AddTransitionalConstraint(
                    (Face)a, (Face)b),
                _ => throw new ArgumentException("Unsupported constraint type.")
            };
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
            var result = new JObject { ["type"] = request.Type,
                ["offset_mm"] = request.Type == "angle" ? null : (JToken)request.OffsetMm,
                ["angle_degrees"] = request.Type == "angle" ? (JToken)request.AngleDegrees : null,
                ["axes_opposed"] = request.Type == "insert" ? (JToken)request.AxesOpposed : null,
                ["inside_tangency"] = request.Type == "tangent" ? (JToken)request.InsideTangency : null,
                ["pairs_checked"] = checks,
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

    /// <summary>
    /// Resolves one reference and checks it is the kind of geometry the constraint actually needs.
    /// Inventor would otherwise infer an axis from a plane, or a plane from a cylinder, and build a
    /// constraint the caller never asked for.
    /// </summary>
    private static (object Entity, ComponentOccurrence Occurrence) ResolveEntity(
        global::Inventor.Document doc, ConstraintCreateRequest request, string id)
    {
        if (request.NeedsEdges)
        {
            var edge = EntityReferences.ResolveAssemblyEdge(doc, id);
            if (edge.GeometryType != CurveTypeEnum.kCircleCurve && edge.GeometryType != CurveTypeEnum.kCircularArcCurve)
                throw new ArgumentException("INSERT_NEEDS_CIRCULAR_EDGE: an insert constraint joins circular edges; " +
                    "this one is " + edge.GeometryType + ". List edges with inventor_list_topology on the assembly.");
            return (edge, edge.ContainingOccurrence);
        }
        var face = EntityReferences.ResolveAssemblyFace(doc, id);
        if (request.NeedsPlanarFaces && face.SurfaceType != SurfaceTypeEnum.kPlaneSurface)
            throw new ArgumentException("MATE_NEEDS_PLANAR_FACE: " + request.Type + " joins planar faces; this one is " +
                face.SurfaceType + ". Use mate_axis for cylinders.");
        if (request.Type == "mate_axis" && face.SurfaceType != SurfaceTypeEnum.kCylinderSurface &&
            face.SurfaceType != SurfaceTypeEnum.kConeSurface)
            throw new ArgumentException("MATE_AXIS_NEEDS_CYLINDER: mate_axis joins the axes of cylindrical or conical " +
                "faces; this one is " + face.SurfaceType + ".");
        return (face, face.ContainingOccurrence);
    }
}
#endif
