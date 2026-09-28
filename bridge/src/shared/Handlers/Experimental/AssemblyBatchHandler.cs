#if INVENTOR2027 && SO_EXPERIMENTAL
using System;
using System.Linq;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Handlers.Core;
using Bimwright.Ipt.Shared.Infrastructure;
using Inventor;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Handlers.Experimental;

/// <summary>Assembly mutations executed only inside the transaction owned by atomic_batch.</summary>
public sealed class AssemblyMoveHandler : AssemblyBatchHandler { public override string Name => "assembly_move"; }
public sealed class AssemblyConstraintHandler : AssemblyBatchHandler { public override string Name => "assembly_constraint"; }
public sealed class AssemblyJointHandler : AssemblyBatchHandler { public override string Name => "assembly_joint"; }

public abstract class AssemblyBatchHandler : ExperimentalHandler
{
    public override bool IsReadOnly => false;

    protected override JToken Run(InventorCommandContext ctx, Application app, JObject p)
    {
        if (ctx.ReadOnly) throw new CodedFailureException(InventorErrorCodes.READ_ONLY, "Assembly changes require write permission.");
        if (ctx.OwnedBatchTransaction == null || !ReferenceEquals(ctx.OwnedBatchTransaction, app.TransactionManager.CurrentTransaction))
            throw new ArgumentException("Assembly operations require the owned atomic batch transaction.");
        var assembly = X.ActiveAssembly(app, Name);
        var doc = (global::Inventor.Document)assembly;
        var def = assembly.ComponentDefinition;
        void Direct(ComponentOccurrence occurrence)
        {
            if (!def.Occurrences.Cast<ComponentOccurrence>().Any(o => ReferenceEquals(o, occurrence))
                || occurrence.Suppressed || occurrence.Adaptive
                || (occurrence.DefinitionDocumentType == DocumentTypeEnum.kAssemblyDocumentObject && occurrence.Flexible)
                || occurrence.Definition is VirtualComponentDefinition)
                throw new ArgumentException("Select a resolved, nonadaptive, nonflexible direct occurrence of the active assembly.");
        }
        void Pair(ComponentOccurrence a, ComponentOccurrence b)
        {
            Direct(a); Direct(b);
            if (ReferenceEquals(a, b)) throw new ArgumentException("Select geometry on two different occurrences.");
        }
        X.Deadline(ctx, "before assembly mutation");
        if (Name == "assembly_move")
        {
            var occurrence = EntityReferences.ResolveOccurrence(doc, X.Str(p, "occurrence_id"));
            Direct(occurrence);
            if (occurrence.Grounded) throw new ArgumentException("Grounded components cannot be moved.");
            var args = (JObject)p.DeepClone(); args["minimum_clearance_mm"] = 0;
            var move = ComponentMoveRequest.Parse(args);
            var original = occurrence.Transformation;
            var values = new double[4, 4];
            for (int r = 0; r < 4; r++) for (int c = 0; c < 4; c++) values[r, c] = original.Cell[r + 1, c + 1];
            var proposed = ComponentPoseMath.Apply(values, move);
            var target = original.Copy();
            for (int r = 0; r < 4; r++) for (int c = 0; c < 4; c++) target.Cell[r + 1, c + 1] = proposed[r, c];
            // Transformation invokes Inventor's constraint solver. Never use SetTransformWithoutConstraints.
            occurrence.Transformation = target;
            if (!doc.Update2()) throw new InvalidOperationException("Assembly rebuild failed.");
            var actual = occurrence.Transformation;
            double distanceSquared = 0, trace = 0;
            for (int r = 1; r <= 3; r++)
            {
                double delta = actual.Cell[r, 4] - target.Cell[r, 4]; distanceSquared += delta * delta;
                for (int c = 1; c <= 3; c++) trace += actual.Cell[r, c] * target.Cell[r, c];
            }
            double degrees = Math.Acos(Math.Max(-1, Math.Min(1, (trace - 1) / 2))) * 180 / Math.PI;
            if (Math.Sqrt(distanceSquared) * 10 > 0.01 || degrees > 0.01)
                throw new ArgumentException("DOF_MOVE_REJECTED: constraints prevented the requested pose.");
            return new JObject { ["occurrence_id"] = p["occurrence_id"], ["matrix_rowmajor_cm"] = X.RowMajor(actual) };
        }
        if (Name == "assembly_constraint")
        {
            var args = (JObject)p.DeepClone(); args["minimum_clearance_mm"] = 0;
            if ((string?)args["type"] != "angle" && args["offset_mm"] == null) args["offset_mm"] = 0;
            var request = ConstraintCreateRequest.Parse(args);
            var (a, oa) = CreateConstraintHandler.ResolveEntity(doc, request, request.FaceA);
            var (b, ob) = CreateConstraintHandler.ResolveEntity(doc, request, request.FaceB);
            Pair(oa, ob);
            AssemblyConstraint created = request.Type switch
            {
                "mate" or "mate_axis" => (AssemblyConstraint)def.Constraints.AddMateConstraint(a, b, UnitConvert.MmToCm(request.OffsetMm)),
                "flush" => (AssemblyConstraint)def.Constraints.AddFlushConstraint(a, b, UnitConvert.MmToCm(request.OffsetMm)),
                "insert" => (AssemblyConstraint)def.Constraints.AddInsertConstraint(a, b, request.AxesOpposed, UnitConvert.MmToCm(request.OffsetMm)),
                "angle" => (AssemblyConstraint)def.Constraints.AddAngleConstraint(a, b, UnitConvert.DegToRad(request.AngleDegrees)),
                "tangent" => (AssemblyConstraint)def.Constraints.AddTangentConstraint(a, b, request.InsideTangency, UnitConvert.MmToCm(request.OffsetMm)),
                _ => throw new ArgumentException("Unsupported M4 constraint type.")
            };
            return new JObject { ["name"] = created.Name, ["type"] = request.Type };
        }
        if (Name == "assembly_joint")
        {
            var type = X.Str(p, "joint_type") switch
            {
                "rigid" => AssemblyJointTypeEnum.kRigidJointType,
                "rotational" => AssemblyJointTypeEnum.kRotationalJointType,
                "slide" => AssemblyJointTypeEnum.kSlideJointType,
                "cylindrical" => AssemblyJointTypeEnum.kCylindricalJointType,
                "planar" => AssemblyJointTypeEnum.kPlanarJointType,
                "ball" => AssemblyJointTypeEnum.kBallJointType,
                _ => throw new ArgumentException("Unsupported joint type.")
            };
            var (a, oa) = CreateJointHandler.Resolve(doc, X.Str(p, "origin_a_id"));
            var (b, ob) = CreateJointHandler.Resolve(doc, X.Str(p, "origin_b_id"));
            Pair(oa, ob);
            var definition = def.Joints.CreateAssemblyJointDefinition(type,
                CreateJointHandler.CreateOriginIntent(def, a, type),
                CreateJointHandler.CreateOriginIntent(def, b, type));
            if (X.Bool(p, "flip_origin", false)) definition.FlipOriginDirection = true;
            if (X.Bool(p, "flip_alignment", false)) definition.FlipAlignmentDirection = true;
            if (p["gap_mm"] != null)
            {
                double gap = X.Num(p, "gap_mm");
                try
                {
                    // Cylindrical joints retain translation along their axis; Inventor exposes
                    // its initial offset as LinearPosition, and rejects the Gap setter.
                    if (type == AssemblyJointTypeEnum.kCylindricalJointType) definition.LinearPosition = UnitConvert.MmToCm(gap);
                    else if (type == AssemblyJointTypeEnum.kBallJointType)
                    { if (gap != 0) throw new ArgumentException("A Ball joint joins coincident origins and requires zero gap."); }
                    else definition.Gap = UnitConvert.MmToCm(gap);
                }
                catch (Exception ex) { throw new ArgumentException("JOINT_GAP_REJECTED: the selected joint does not accept this gap. " + ex.Message, ex); }
            }
            AssemblyJoint joint;
            try { joint = def.Joints.Add(definition); }
            catch (Exception ex) { throw new ArgumentException("JOINT_ORIGIN_REJECTED: Inventor could not create this joint from the selected origins. " + ex.Message, ex); }
            return new JObject { ["name"] = joint.Name, ["joint_type"] = p["joint_type"] };
        }
        throw new ArgumentException("Unknown assembly command.");
    }
}
#endif
