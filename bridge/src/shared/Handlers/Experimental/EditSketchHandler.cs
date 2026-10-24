#if INVENTOR2027 && SO_EXPERIMENTAL
using System;
using System.Linq;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;
using Bimwright.Ipt.Shared.Handlers.Core;
using Inventor;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Handlers.Experimental;

/// <summary>S1: one semantic command on an existing native sketch, exclusively in the owned batch transaction.</summary>
public sealed class EditSketchHandler : ExperimentalHandler
{
    internal static readonly string[] SupportedVariants = {
        "line","line_midpoint","rectangle_two_points","rectangle_three_points","rectangle_center_two_points","rectangle_center_three_points","polygon",
        "circle_center","point","arc_three_points","arc_center","arc_tangent","ellipse","spline_fit","spline_control","spline_point_update","spline_tension_update",
        "slot_center_to_center","slot_overall","slot_center","slot_three_point_arc","slot_center_arc",
        "format_construction","format_centerline","format_hole_center","transform_move","transform_rotate","transform_stretch","offset_distance","offset_point",
        "pattern_rectangular","pattern_circular","pattern_update","pattern_delete",
        "constraint_coincident","constraint_parallel","constraint_collinear","constraint_perpendicular","constraint_concentric",
        "constraint_tangent","constraint_smooth","constraint_equal","constraint_ground","constraint_horizontal","constraint_vertical","constraint_symmetry","constraint_delete",
        "dimension_aligned","dimension_horizontal","dimension_vertical","dimension_angle_two_lines","dimension_angle_three_points",
        "dimension_radius","dimension_diameter","dimension_arc_length","dimension_ellipse_major","dimension_ellipse_minor",
        "dimension_offset","dimension_tangent_distance","dimension_offset_spline","dimension_update","dimension_delete" };
    public override string Name => "edit_sketch";
    public override bool IsReadOnly => false;

    protected override JToken Run(InventorCommandContext ctx, Application app, JObject p)
    {
        if (ctx.ReadOnly) throw new CodedFailureException(InventorErrorCodes.READ_ONLY, "Sketch changes require write permission.");
        if (ctx.OwnedBatchTransaction == null || !ReferenceEquals(ctx.OwnedBatchTransaction, app.TransactionManager.CurrentTransaction))
            throw new CodedFailureException(InventorErrorCodes.ATOMIC_REQUIRED, "Sketch editing requires the owned atomic batch transaction.");
        var part = X.ActivePart(app, Name);
        var sketch = SketchReferences.Resolve((global::Inventor.Document)part, X.Str(p, "sketch_id"), "sketch") as PlanarSketch
            ?? throw new ArgumentException("Select a native 2D sketch.");
        if (sketch.SketchEntities.Count > 2000) throw new ArgumentException("Sketch entity budget exceeded (2000).");
        string variant = X.Str(p, "variant");
        // Image insertion, text mutation and copy foundations remain undisclosed until their
        // native rendering/client preview and complete XR editors are integrated.
        if(!SupportedVariants.Contains(variant,StringComparer.Ordinal) && variant!="insert_image"
            && variant!="text_create" && variant!="text_update" && variant!="text_delete" && variant!="transform_copy") throw new ArgumentException("Unsupported native sketch operation.");
        var options = p["options"] as JObject ?? throw new ArgumentException("options must be an object.");
        var g = app.TransientGeometry;
        Point2d Point(JToken? token)
        {
            if (token is not JArray a || a.Count != 2 || a.Any(v => v.Type is not (JTokenType.Float or JTokenType.Integer)))
                throw new ArgumentException("Sketch points must contain two finite millimetre coordinates.");
            double x = (double)a[0], y = (double)a[1];
            if (double.IsNaN(x) || double.IsInfinity(x) || double.IsNaN(y) || double.IsInfinity(y)) throw new ArgumentException("Non-finite sketch point.");
            return g.CreatePoint2d(UnitConvert.MmToCm(x), UnitConvert.MmToCm(y));
        }
        JArray Points(int count)
        {
            if (options["points_mm"] is not JArray data || data.Count != count) throw new ArgumentException("Wrong number of command points.");
            return data;
        }
        double Length(string name) => UnitConvert.MmToCm(X.Positive(options, name));
        int before = sketch.SketchEntities.Count;
        int constraintsBefore=sketch.GeometricConstraints.Count;
        if(variant=="insert_image")
            NativeSketchImages.Apply(ctx,app,part,sketch,options);
        else if(variant.StartsWith("text_",StringComparison.Ordinal))
            NativeSketchTexts.Apply(ctx,app,part,sketch,X.Str(p,"sketch_id"),variant.Substring(5),options);
        else if(variant=="arc_tangent")
            NativeSketchTangentArcs.Apply(ctx,app,(global::Inventor.Document)part,sketch,X.Str(p,"sketch_id"),options);
        else if(variant=="spline_point_update")
            NativeSketchSplinePointEdits98.Apply(ctx,app,(global::Inventor.Document)part,sketch,X.Str(p,"sketch_id"),options);
        else if(variant=="spline_tension_update")
            NativeSketchSplineTensionEdits99.Apply(ctx,app,(global::Inventor.Document)part,sketch,X.Str(p,"sketch_id"),options);
        else if(variant.StartsWith("pattern_",StringComparison.Ordinal))
            NativeSketchPatterns.Apply(ctx,app,(global::Inventor.Document)part,sketch,X.Str(p,"sketch_id"),variant.Substring(8),options);
        else if(variant=="offset_distance" || variant=="offset_point")
            NativeSketchOffsets.Apply(ctx,app,(global::Inventor.Document)part,sketch,X.Str(p,"sketch_id"),variant=="offset_point",options);
        else if(variant=="transform_copy")
            NativeSketchCopies.Apply(ctx,app,(global::Inventor.Document)part,sketch,X.Str(p,"sketch_id"),options);
        else if(variant.StartsWith("transform_",StringComparison.Ordinal))
            NativeSketchTransforms.Apply(ctx,app,(global::Inventor.Document)part,sketch,X.Str(p,"sketch_id"),variant.Substring(10),options);
        else if(variant=="dimension_update" || variant=="dimension_delete")
            NativeSketchDimensions.Edit(ctx,(global::Inventor.Document)part,sketch,X.Str(p,"sketch_id"),variant.Substring(10),options,g);
        else if(variant.StartsWith("dimension_",StringComparison.Ordinal))
            NativeSketchDimensions.Add(ctx,(global::Inventor.Document)part,sketch,X.Str(p,"sketch_id"),variant.Substring(10),options,g);
        else if(variant=="constraint_delete")
            NativeSketchConstraints.Delete(ctx,(global::Inventor.Document)part,sketch,X.Str(p,"sketch_id"),options);
        else if(variant.StartsWith("constraint_",StringComparison.Ordinal))
        {
            if(constraintsBefore>=2000) throw new ArgumentException("Native geometric constraint budget exceeded (2000).");
            NativeSketchConstraints.Add(ctx,(global::Inventor.Document)part,sketch,X.Str(p,"sketch_id"),variant.Substring(11),options,g);
        }
        else
        switch (variant)
        {
            case "format_construction":
            case "format_centerline":
            case "format_hole_center":
            {
                if(options["value"]?.Type!=JTokenType.Boolean) throw new ArgumentException("Format value must be an explicit boolean.");
                if(options["references"] is not JArray selected || selected.Count<1 || selected.Count>2000
                    || selected.Any(r=>r is not JObject)) throw new ArgumentException("Select 1–2000 native sketch references.");
                var document=(global::Inventor.Document)part;
                string documentId=EntityReferences.DocumentId(document), revision=ctx.Events?.Revision(documentId) ?? "";
                var seen=new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
                var entities=new System.Collections.Generic.List<SketchEntity>();
                foreach(JObject reference in selected)
                {
                    X.Deadline(ctx,"validating the selected native sketch entities");
                    if(X.Str(reference,"document_id")!=documentId || X.Str(reference,"sketch_id")!=X.Str(p,"sketch_id"))
                        throw new ArgumentException("Selected reference belongs to a different document or sketch.");
                    if(X.Str(reference,"revision")!=revision) throw ConcurrencyFailure.StaleRevision((string?)reference["revision"],revision);
                    string id=X.Str(reference,"entity_id"), subpart=X.Str(reference,"subpart");
                    if(!seen.Add(id)) throw new ArgumentException("Select each native entity only once.");
                    var entity=SketchReferences.Resolve(document,id,"sketch_entity") as SketchEntity
                        ?? throw new ArgumentException("Format requires native sketch geometry.");
                    if(X.Str(reference,"type")!=SketchEditorXrHandler.Kind(entity)) throw new ArgumentException("Native reference type does not match the selected entity.");
                    if(!SketchReferences.SameComObject(entity.Parent,sketch)) throw new ArgumentException("Native entity is not owned by the selected sketch.");
                    if(subpart!="Entity" && !(variant=="format_hole_center" && entity is SketchPoint && subpart=="Point"))
                        throw new ArgumentException("Format requires the whole native geometry, not an endpoint or center.");
                    if(variant=="format_centerline" && entity is not SketchLine) throw new ArgumentException("Centerline format requires sketch lines.");
                    if(variant=="format_hole_center" && entity is not SketchPoint) throw new ArgumentException("Hole-center format requires sketch points.");
                    entities.Add(entity);
                }
                bool value=(bool)options["value"]!;
                foreach(var entity in entities)
                {
                    X.Deadline(ctx,"updating native sketch format");
                    if(variant=="format_construction") entity.Construction=value;
                    else if(variant=="format_centerline") ((SketchLine)entity).Centerline=value;
                    else ((SketchPoint)entity).HoleCenter=value;
                }
                break;
            }
            case "line_midpoint":
            {
                if(options.Properties().Any(p=>p.Name!="points_mm" && p.Name!="references" && p.Name!="reference_roles"))
                    throw new ArgumentException("Unknown midpoint line option.");
                var points=Points(2);object center=Point(points[0]),end=Point(points[1]);
                if(options["references"]!=null || options["reference_roles"]!=null)
                {
                    if(options["reference_roles"] is not JArray roles || roles.Count<1 || roles.Count>2
                        || roles.Any(r=>r.Type!=JTokenType.String || ((string?)r!="midpoint" && (string?)r!="end"))
                        || roles.Select(r=>(string)r!).Distinct(StringComparer.Ordinal).Count()!=roles.Count)
                        throw new ArgumentException("Select distinct midpoint/end roles for the native points.");
                    var selected=NativeSketchConstraints.ResolveSelections(ctx,(global::Inventor.Document)part,sketch,X.Str(p,"sketch_id"),options["references"]);
                    if(selected.Length!=roles.Count || selected.Any(e=>e is not SketchPoint)) throw new ArgumentException("Midpoint line references must resolve to native sketch points.");
                    for(int i=0;i<roles.Count;i++) if((string?)roles[i]=="midpoint") center=selected[i];else end=selected[i];
                }
                Point2d Position(object value)=>value is SketchPoint point ? point.Geometry : (Point2d)value;
                if(Position(center).DistanceTo(Position(end))<1e-9) throw new ArgumentException("Midpoint and endpoint must differ.");
                sketch.SketchLines.AddByMidEndPoints(center,end);break;
            }
            case "line":
            {
                var points = Points(2); var a = Point(points[0]); var b = Point(points[1]);
                if (a.DistanceTo(b) < 1e-9) throw new ArgumentException("Line endpoints must differ.");
                sketch.SketchLines.AddByTwoPoints(a, b); break;
            }
            case "rectangle_two_points":
            {
                var points = Points(2); sketch.SketchLines.AddAsTwoPointRectangle(Point(points[0]), Point(points[1])); break;
            }
            case "rectangle_three_points":
            { var points=Points(3); sketch.SketchLines.AddAsThreePointRectangle(Point(points[0]),Point(points[1]),Point(points[2])); break; }
            case "rectangle_center_two_points":
            { var points=Points(2); sketch.SketchLines.AddAsTwoPointCenteredRectangle(Point(points[0]),Point(points[1])); break; }
            case "rectangle_center_three_points":
            { var points=Points(3); sketch.SketchLines.AddAsThreePointCenteredRectangle(Point(points[0]),Point(points[1]),Point(points[2])); break; }
            case "polygon":
            {
                if (options["sides"]?.Type!=JTokenType.Integer || (int)options["sides"]! < 3 || (int)options["sides"]! > 120)
                    throw new ArgumentException("Polygon requires 3–120 integer sides.");
                var points=Points(2); sketch.SketchLines.AddAsPolygon((int)options["sides"]!,Point(points[0]),Point(points[1]),X.Bool(options,"inscribed",true)); break;
            }
            case "circle_center": sketch.SketchCircles.AddByCenterRadius(Point(Points(1)[0]), Length("radius_mm")); break;
            case "point": sketch.SketchPoints.Add(Point(Points(1)[0]), X.Bool(options, "hole_center", false)); break;
            case "arc_three_points":
            {
                // Desktop order is start/end/through; the API takes start/mid/end.
                var points = Points(3); sketch.SketchArcs.AddByThreePoints(Point(points[0]), Point(points[2]), Point(points[1])); break;
            }
            case "arc_center":
            {
                double sweep = X.Num(options, "sweep_deg");
                if (Math.Abs(sweep) < 1e-9 || Math.Abs(sweep) >= 360) throw new ArgumentException("Arc sweep must be nonzero and below one revolution.");
                sketch.SketchArcs.AddByCenterStartSweepAngle(Point(Points(1)[0]), Length("radius_mm"), UnitConvert.DegToRad(X.Num(options, "start_deg")), UnitConvert.DegToRad(sweep)); break;
            }
            case "ellipse":
            {
                double major = Length("major_radius_mm"), minor = Length("minor_radius_mm");
                // Inventor preserves the first/second axis order even when the
                // second radius is larger (desktop ellipse and native API oracle).
                double angle = UnitConvert.DegToRad(X.Num(options, "rotation_deg", 0));
                sketch.SketchEllipses.Add(Point(Points(1)[0]), g.CreateUnitVector2d(Math.Cos(angle), Math.Sin(angle)), major, minor); break;
            }
            case "spline_fit":
            case "spline_control":
            {
                bool control=variant=="spline_control";
                var request=NativeSplineCreationPolicy96.Parse(options,control);
                var points = app.TransientObjects.CreateObjectCollection();
                foreach (var point in request.PointsMm) points.Add(g.CreatePoint2d(UnitConvert.MmToCm(point.X),UnitConvert.MmToCm(point.Y)));
                if (variant == "spline_fit")
                {
                    // The client freezes the snapshot preference in the operation,
                    // so changing application options cannot alter a rendered plan.
                    int method = request.FitMethod!.Value;
                    if (!Enum.IsDefined(typeof(SplineFitMethodEnum), method))
                        throw new ArgumentException("Spline fit method must be an explicit native enum value.");
                    var spline=sketch.SketchSplines.Add(points, (SplineFitMethodEnum)method);
                    if(request.ClosureSpecified)spline.Closed=request.Closed;
                    if(request.ClosureSpecified&&spline.Closed!=request.Closed)throw new ArgumentException("Native spline closure differs from the requested creator profile.");
                }
                else
                {
                    var spline=sketch.SketchControlPointSplines.Add(points);
                    if(request.ClosureSpecified&&spline.IsClosed!=request.Closed)throw new ArgumentException("Native control spline closure differs from the requested creator profile.");
                }
                break;
            }
            case "slot_center_to_center":
            case "slot_overall":
            case "slot_center":
            {
                var points = Points(2); var a = Point(points[0]); var b = Point(points[1]); double width = Length("width_mm");
                if (variant == "slot_center_to_center") sketch.AddStraightSlotByCenterToCenter(a, b, width);
                else if (variant == "slot_overall") sketch.AddStraightSlotByOverall(a, b, width);
                else sketch.AddStraightSlotBySlotCenter(a, b, width);
                break;
            }
            case "slot_three_point_arc":
            {
                // Desktop collects start/end/through; the API takes StartPoint/MidPoint/EndPoint.
                var points = Points(3); sketch.AddArcSlotByThreePointArc(Point(points[0]), Point(points[2]), Point(points[1]), Length("width_mm")); break;
            }
            case "slot_center_arc":
            {
                var points = Points(2); double sweep = UnitConvert.DegToRad(X.Num(options, "sweep_deg"));
                if (Math.Abs(sweep) < 1e-9 || Math.Abs(sweep) >= 2 * Math.PI) throw new ArgumentException("Invalid slot sweep.");
                sketch.AddArcSlotByCenterPointArc(Point(points[0]), Point(points[1]), sweep, Length("width_mm")); break;
            }
            default: throw new ArgumentException("Unsupported S1 native sketch variant: " + variant);
        }
        X.Deadline(ctx, "after the S1 semantic sketch command");
        if (sketch.SketchEntities.Count > 2000) throw new ArgumentException("Command exceeds the sketch entity budget (2000); rollback required.");
        if(sketch.GeometricConstraints.Count>2000) throw new ArgumentException("Command exceeds the geometric constraint budget (2000); rollback required.");
        sketch.Solve();
        if(sketch.ConstraintStatus==ConstraintStatusEnum.kOverConstrainedConstraintStatus)
            throw new ArgumentException("Native sketch solver reports conflicting constraints.");
        // IDs for tentative entities would outlive Abort. Refresh the authoritative snapshot only after a confirmed commit.
        return new JObject { ["sketch_name"] = sketch.Name, ["variant"] = variant, ["added_native_entities"] = sketch.SketchEntities.Count - before,
            ["added_native_constraints"]=sketch.GeometricConstraints.Count-constraintsBefore,
            ["constraint_status"] = sketch.ConstraintStatus.ToString(), ["refresh_snapshot_after_commit"] = true };
    }
}
#endif
