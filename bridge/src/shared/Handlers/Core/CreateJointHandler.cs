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
/// <c>create_joint_safe</c> — an assembly joint between two component origins.
/// A joint states the degrees of freedom that remain (rigid, rotational, slide, cylindrical, planar,
/// ball) instead of the several constraints that would add up to the same thing, which is what makes
/// a mechanism readable. Origins are portable proxy ids from <c>inventor_list_topology</c>.
///
/// Validation matches the constraint path: owned transaction, rebuild, health of every constraint and
/// joint, and interference plus minimum clearance across all unsuppressed top-level pairs. A joint
/// leaves motion available by design, so these checks describe the assembled position only — never a
/// swept path.
/// </summary>
public sealed class CreateJointHandler : HandlerBase, IInventorCommand
{
    public string Name => "create_joint_safe";
    public bool IsReadOnly => false;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        if (ctx.ReadOnly) return Fail(ctx, InventorErrorCodes.READ_ONLY, "Joint creation requires write permission.");
        if (!ActiveDocumentSupport.TryGetActiveAssembly(ctx, Name, out var app, out var assembly, out var failure)) return failure!;
        var doc = (global::Inventor.Document)assembly;
        string id = EntityReferences.DocumentId(doc);
        if ((string?)p["document_id"] != id) return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "DOCUMENT_CHANGED");
        if (ctx.Events == null || (string?)p["expected_revision"] != ctx.Events.Revision(id))
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "STALE_REVISION");

        var type = ((string?)p["joint_type"] ?? "").Trim().ToLowerInvariant() switch
        {
            "rigid" => AssemblyJointTypeEnum.kRigidJointType,
            "rotational" => AssemblyJointTypeEnum.kRotationalJointType,
            "slide" => AssemblyJointTypeEnum.kSlideJointType,
            "cylindrical" => AssemblyJointTypeEnum.kCylindricalJointType,
            "planar" => AssemblyJointTypeEnum.kPlanarJointType,
            "ball" => AssemblyJointTypeEnum.kBallJointType,
            _ => (AssemblyJointTypeEnum)0
        };
        if (type == 0)
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT,
                "joint_type must be rigid, rotational, slide, cylindrical, planar or ball.");
        string originA = (string?)p["origin_a_id"] ?? "";
        string originB = (string?)p["origin_b_id"] ?? "";
        if (string.IsNullOrWhiteSpace(originA) || string.IsNullOrWhiteSpace(originB))
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT,
                "origin_a_id and origin_b_id are required: portable proxy ids from inventor_list_topology.");
        if (originA == originB)
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "The two origins must be different entities.");
        double clearance = p["minimum_clearance_mm"] == null ? 0 : p.Value<double>("minimum_clearance_mm");
        if (double.IsNaN(clearance) || clearance < 0)
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "minimum_clearance_mm cannot be negative.");
        double? gap = p["gap_mm"]?.Type is JTokenType.Float or JTokenType.Integer ? p.Value<double>("gap_mm") : null;
        bool preview = p["preview"]?.Type != JTokenType.Boolean || (bool)p["preview"]!;
        bool flipOrigin = p["flip_origin"]?.Type == JTokenType.Boolean && (bool)p["flip_origin"]!;
        bool flipAlignment = p["flip_alignment"]?.Type == JTokenType.Boolean && (bool)p["flip_alignment"]!;

        var def = assembly.ComponentDefinition;
        object entityA, entityB;
        ComponentOccurrence occurrenceA, occurrenceB;
        try
        {
            (entityA, occurrenceA) = Resolve(doc, originA);
            (entityB, occurrenceB) = Resolve(doc, originB);
        }
        catch (ArgumentException ex) { return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, ex.Message); }

        var direct = def.Occurrences.Cast<ComponentOccurrence>().ToArray();
        foreach (var occurrence in new[] { occurrenceA, occurrenceB })
            if (!direct.Any(o => ReferenceEquals(o, occurrence)) || occurrence.Suppressed || occurrence.Adaptive ||
                occurrence.DefinitionDocumentType != DocumentTypeEnum.kPartDocumentObject)
                return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT,
                    "Only unsuppressed nonadaptive direct part occurrences are supported.");
        if (ReferenceEquals(occurrenceA, occurrenceB))
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "The two origins must belong to different occurrences.");

        bool priorUi = app.UserInterfaceManager.UserInteractionDisabled;
        Transaction? transaction = null;
        void Owned()
        {
            if (transaction == null || !ReferenceEquals(app.TransactionManager.CurrentTransaction, transaction))
                throw new InvalidOperationException("TRANSACTION_OWNERSHIP_LOST");
        }
        try
        {
            if (ctx.IsDeadlineExceeded?.Invoke() == true) throw new TimeoutException("Expired before joint creation.");
            app.UserInterfaceManager.UserInteractionDisabled = true;
            transaction = app.TransactionManager.StartTransaction((Inventor._Document)doc, "Inventor SO create joint");
            if (transaction.HasParentTransaction) throw new InvalidOperationException("TRANSACTION_BUSY");

            var intentA = def.CreateGeometryIntent(entityA);
            var intentB = def.CreateGeometryIntent(entityB);
            AssemblyJointDefinition definition;
            AssemblyJoint joint;
            try
            {
                definition = def.Joints.CreateAssemblyJointDefinition(type, intentA, intentB);
                if (flipOrigin) definition.FlipOriginDirection = true;
                if (flipAlignment) definition.FlipAlignmentDirection = true;
                if (gap != null) definition.Gap = UnitConvert.MmToCm(gap.Value);
                joint = def.Joints.Add(definition);
            }
            catch (Exception ex)
            {
                // Verified on 2027: a rotational joint takes the CIRCULAR EDGES whose centres define the
                // axis; handing it the cylindrical face instead fails with a bare E_FAIL.
                throw new InvalidOperationException("JOINT_ORIGIN_REJECTED: Inventor refused these origins for a " +
                    (string?)p["joint_type"] + " joint. Rotational and cylindrical joints take circular edges, not the " +
                    "cylindrical face; list edges with inventor_list_topology and filter geometry=Circle. " +
                    "Inventor reported: " + ex.Message);
            }

            if (!doc.Update2()) throw new InvalidOperationException("Assembly rebuild failed.");
            foreach (AssemblyConstraint constraint in def.Constraints)
                if (!constraint.Suppressed && constraint.HealthStatus != HealthStatusEnum.kUpToDateHealth)
                    throw new InvalidOperationException("CONSTRAINT_UNHEALTHY: " + constraint.Name);
            foreach (AssemblyJoint existing in def.Joints)
                if (!existing.Suppressed && existing.HealthStatus != HealthStatusEnum.kUpToDateHealth)
                    throw new InvalidOperationException("JOINT_UNHEALTHY: " + existing.Name);

            var components = def.Occurrences.Cast<ComponentOccurrence>().Where(o => !o.Suppressed).ToArray();
            int checks = 0;
            for (int i = 0; i < components.Length; i++) for (int j = i + 1; j < components.Length; j++)
            {
                if (ctx.IsDeadlineExceeded?.Invoke() == true) throw new TimeoutException("Expired during assembly validation.");
                var pair = app.TransientObjects.CreateObjectCollection();
                pair.Add(components[i]); pair.Add(components[j]);
                if (def.AnalyzeInterference(pair).Count > 0) throw new InvalidOperationException("INTERFERENCE");
                double distance = UnitConvert.CmToMm(app.MeasureTools.GetMinimumDistance(components[i], components[j]));
                if (double.IsNaN(distance) || double.IsInfinity(distance) || distance < clearance)
                    throw new InvalidOperationException("CLEARANCE_FAILED: " + distance);
                checks++;
            }

            var result = new JObject
            {
                ["joint_type"] = (string?)p["joint_type"],
                ["gap_mm"] = gap,
                ["flip_origin"] = flipOrigin,
                ["flip_alignment"] = flipAlignment,
                ["pairs_checked"] = checks,
                ["joint_name"] = preview ? null : joint.Name,
                ["degrees_of_freedom_remain"] = type != AssemblyJointTypeEnum.kRigidJointType,
                ["motion_path_validated"] = false,
            };
            Owned();
            if (ctx.IsDeadlineExceeded?.Invoke() == true) throw new TimeoutException("Expired before commit.");
            if (app.ActiveDocument == null || EntityReferences.DocumentId(app.ActiveDocument) != id)
                throw new InvalidOperationException("DOCUMENT_CHANGED");
            if (preview) transaction.Abort(); else transaction.End();
            transaction = null;
            result["status"] = preview ? "preview_rolled_back" : "committed";
            result["document_id"] = id;
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

    /// <summary>A joint origin may be a face, an edge or a vertex of a component.</summary>
    private static (object Entity, ComponentOccurrence Occurrence) Resolve(global::Inventor.Document doc, string id)
    {
        var reference = PersistentEntityReference.Decode(id);
        switch (reference.EntityType)
        {
            case "face_proxy":
            {
                var face = EntityReferences.ResolveAssemblyFace(doc, id);
                return (face, face.ContainingOccurrence);
            }
            case "edge_proxy":
            {
                var edge = EntityReferences.ResolveAssemblyEdge(doc, id);
                return (edge, edge.ContainingOccurrence);
            }
            case "vertex_proxy":
            {
                var vertex = EntityReferences.ResolveAssemblyVertex(doc, id);
                return (vertex, vertex.ContainingOccurrence);
            }
            default:
                throw new ArgumentException("JOINT_ORIGIN_TYPE: a joint origin must be a face, edge or vertex proxy of a " +
                    "component; this id is a " + reference.EntityType + ".");
        }
    }
}
#endif
