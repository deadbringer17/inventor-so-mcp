using Inventor;
using Newtonsoft.Json.Linq;

internal static partial class Program
{
    // OBSERVATION ONLY. Identities and native memberships, never coordinate matching.
    private static JObject NativeSplineIdentity87(SketchSpline source)
    {
        var sketch = (PlanarSketch)source.Parent;
        var failures = new JArray();
        var entities = new JArray();
        var constraints = new JArray();
        foreach (SketchEntity entity in sketch.SketchEntities)
            entities.Add(IdentityEntity87(entity, failures));
        // Include points explicitly: SketchEntities enumeration membership is not assumed.
        var points = new JArray();
        foreach (SketchPoint point in sketch.SketchPoints)
            points.Add(IdentityEntity87((SketchEntity)point, failures));
        foreach (GeometricConstraint constraint in sketch.GeometricConstraints)
        {
            var row = new JObject { ["key"] = Key73(constraint), ["type"] = constraint.Type.ToString(),
                ["sketch_key"] = Key73(constraint.Parent) };
            var members = new JArray(); row["members"] = members;
            void Member(string role, Func<SketchEntity?> read)
            {
                try
                {
                    var entity = read();
                    members.Add(new JObject { ["role"] = role, ["present"] = entity != null,
                        ["entity"] = entity == null ? null : IdentityEntity87(entity, failures) });
                }
                catch (Exception error)
                {
                    var failure = ApiError85("identity87." + constraint.Type + "." + role, error);
                    failure["constraint_key"] = row["key"]!.DeepClone(); failures.Add(failure);
                }
            }
            switch (constraint)
            {
                case SplineFitPointConstraint fit:
                    Member("Spline", () => (SketchEntity)fit.Spline);
                    // Preserve an unreadable native member as explicit missing
                    // evidence. Custom raw IDispatch reading crashed the research
                    // worker in native-r4 and is not used again.
                    Member("Point", () => (SketchEntity)fit.Point);
                    break;
                case CoincidentConstraint coincident:
                    Member("EntityOne", () => coincident.EntityOne);
                    Member("EntityTwo", () => coincident.EntityTwo);
                    break;
                case ConcentricConstraint concentric:
                    Member("EntityOne", () => concentric.EntityOne);
                    Member("EntityTwo", () => concentric.EntityTwo);
                    break;
                case TangentSketchConstraint tangent:
                    Member("EntityOne", () => tangent.EntityOne);
                    Member("EntityTwo", () => tangent.EntityTwo);
                    break;
                case GroundConstraint ground:
                    Member("Entity", () => ground.Entity);
                    break;
                case MidpointConstraint midpoint:
                    Member("Point", () => (SketchEntity)midpoint.Point);
                    Member("Line", () => (SketchEntity?)midpoint.Line);
                    Member("Arc", () => (SketchEntity?)midpoint.Arc);
                    break;
                case OffsetConstraint offset:
                    Member("EntityOne", () => offset.EntityOne);
                    Member("EntityTwo", () => offset.EntityTwo);
                    Member("EntityThree", () => offset.EntityThree);
                    Member("EntityFour", () => offset.EntityFour);
                    break;
                default:
                    row["member_capture"] = "NOT_COVERED: native type has no verified role adapter in this research probe";
                    break;
            }
            constraints.Add(row);
        }
        return new JObject { ["evidence"] = "OBSERVATION_ONLY: exact native key and membership graph; no product identity policy",
            ["source_key"] = Key73(source), ["sketch_key"] = Key73(sketch),
            ["entities"] = entities, ["points"] = points, ["constraints"] = constraints,
            ["member_read_failures"] = failures, ["all_attempted_member_reads_returned"] = failures.Count == 0 };
    }

    private static JObject IdentityEntity87(SketchEntity entity, JArray failures)
    {
        var row = new JObject { ["key"] = Key73(entity), ["type"] = entity.Type.ToString(),
            ["sketch_key"] = Key73(entity.Parent) };
        try
        {
            var nativeOwners=entity.OwnedBy;
            var owners = nativeOwners==null ? Array.Empty<SketchEntity>() : nativeOwners.Cast<SketchEntity>().ToArray();
            row["owned_by_state"]=nativeOwners==null ? "native_null" : "enumerated";
            row["owned_by_keys"] = new JArray(owners.Select(Key73));
            row["owned_by_types"] = new JArray(owners.Select(e => e.Type.ToString()));
            row["owned_by"] = new JArray(owners.Select(e =>
                new JObject { ["key"] = Key73(e), ["type"] = e.Type.ToString(), ["sketch_key"] = Key73(e.Parent) }));
        }
        catch (Exception error) { var failure = ApiError85("identity87.OwnedBy", error); failure["entity_key"] = row["key"]!.DeepClone(); failures.Add(failure); }
        try { row["constraint_keys"] = new JArray((entity is SketchPoint point ? point.Constraints : entity.Constraints).Cast<GeometricConstraint>().Select(Key73)); }
        catch (Exception error) { var failure = ApiError85("identity87.Constraints", error); failure["entity_key"] = row["key"]!.DeepClone(); failures.Add(failure); }
        if (entity is SketchSplineHandle handle) row["handle_owner"] = HandleState85(handle);
        row["construction"] = entity.Construction;
        if (entity is SketchArc arc)
        {
            // Structural ownership is observed, never inferred from Construction=true.
            row["start_point"] = IdentityEntity87((SketchEntity)arc.StartSketchPoint, failures);
            row["end_point"] = IdentityEntity87((SketchEntity)arc.EndSketchPoint, failures);
            row["center_point"] = IdentityEntity87((SketchEntity)arc.CenterSketchPoint, failures);
        }
        if (entity is SketchLine line)
        {
            row["start_point"] = IdentityEntity87((SketchEntity)line.StartSketchPoint, failures);
            row["end_point"] = IdentityEntity87((SketchEntity)line.EndSketchPoint, failures);
        }
        return row;
    }

}
