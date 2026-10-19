using Bimwright.Ipt.Shared.Contracts;
using Inventor;
using Newtonsoft.Json.Linq;

internal static partial class Program
{
    private sealed record SupplementFixture83(PartDocument Document, PlanarSketch Sketch, SketchEntity Source,
        string Family, string Endpoint, int Side, string AnchorMode, SketchPoint? Target, JObject Analytic);
    private static readonly List<SupplementFixture83> SupplementFixtures83 = new();

    // DRAFT: invoke only while CreateFixture73 owns the new unsaved document, before SaveAs.
    private static void CreateSupplementFixtures83(PartDocument doc)
    {
        GuardDocuments73(); var app = Application!; var g = app.TransientGeometry;
        var sketch = doc.ComponentDefinition.Sketches.Add(doc.ComponentDefinition.WorkPlanes[3]);
        sketch.Name = "S1_TangentSources";
        int ordinal = 0;
        foreach (string family in new[] { "elliptical_arc", "spline", "control_point_spline", "arc_other_endpoint" })
        foreach (string endpoint in new[] { "Start", "End" })
        foreach (int side in family == "arc_other_endpoint" ? new[] { 1 } : new[] { -1, 1 })
        foreach (string mode in family == "arc_other_endpoint" ? new[] { "other_source_endpoint" } : new[] { "coordinates", "native_point" })
        {
            GuardDocuments73();
            double offset = ordinal++ * 6d;
            SketchEntity source;
            if (family == "elliptical_arc") source = (SketchEntity)sketch.SketchEllipticalArcs.Add(g.CreatePoint2d(5 + offset, 5), g.CreateUnitVector2d(1, 0), 2, 1, 0, Math.PI / 2);
            else if (family == "arc_other_endpoint") source = (SketchEntity)sketch.SketchArcs.AddByCenterStartSweepAngle(g.CreatePoint2d(5 + offset, 5), 1, 0, Math.PI / 2);
            else
            {
                var points = app.TransientObjects.CreateObjectCollection();
                foreach (var xy in new[] { new[] { 3d, 4d }, new[] { 4d, 6d }, new[] { 6d, 6d }, new[] { 7d, 4d } })
                    points.Add(g.CreatePoint2d(xy[0] + offset, xy[1]));
                source = family == "spline" ? (SketchEntity)sketch.SketchSplines.Add(points, (SplineFitMethodEnum)26369)
                    : (SketchEntity)sketch.SketchControlPointSplines.Add(points);
            }
            var sourceStart = SupplementEndpoint83(source, true); var sourceEnd = SupplementEndpoint83(source, false);
            var p = endpoint == "Start" ? sourceStart.Geometry : sourceEnd.Geometry;
            var analytic = new JObject { ["tangent_oracle"] = "analytic_from_native_definition_no_evaluator" };
            double tx, ty;
            if (family is "spline" or "control_point_spline")
            {
                BSplineCurve2d geometry = family == "spline" ? ((SketchSpline)source).Geometry : ((SketchControlPointSpline)source).Geometry;
                geometry.GetBSplineInfo(out int order, out int polesCount, out int knotsCount, out bool rational, out bool periodic, out bool closed);
                double[] poles = Array.Empty<double>(), knots = Array.Empty<double>(), weights = Array.Empty<double>();
                geometry.GetBSplineData(ref poles, ref knots, ref weights);
                // Inventor returns no weights for a non-rational curve. Preserve that
                // absence as an empty array rather than serializing JArray(null) as [null].
                weights ??= Array.Empty<double>();
                if (periodic || closed || order < 2 || polesCount < order || poles.Length != polesCount * 2 || knotsCount != polesCount + order
                    || knots.Length != knotsCount || poles.Any(v => !double.IsFinite(v)) || knots.Any(v => !double.IsFinite(v))
                    || !knots.Take(order).All(v => v == knots[0]) || !knots.TakeLast(order).All(v => v == knots[^1])
                    || knots[order] <= knots[0] || knots[knots.Length - order - 1] >= knots[^1]
                    || rational && (weights.Length != polesCount || weights.Any(v => !double.IsFinite(v) || v <= 0)))
                    throw new HostFailure73("Supplement spline is not a finite open clamped NURBS with positive weights.");
                if (Math.Abs(sourceStart.Geometry.X - poles[0]) > 1e-6 || Math.Abs(sourceStart.Geometry.Y - poles[1]) > 1e-6
                    || Math.Abs(sourceEnd.Geometry.X - poles[^2]) > 1e-6 || Math.Abs(sourceEnd.Geometry.Y - poles[^1]) > 1e-6)
                    throw new HostFailure73("Spline endpoint identity does not match the analytic terminal poles.");
                tx = endpoint == "Start" ? poles[0] - poles[2] : poles[^2] - poles[^4];
                ty = endpoint == "Start" ? poles[1] - poles[3] : poles[^1] - poles[^3];
                analytic["order"] = order; analytic["poles_mm"] = new JArray(poles.Select(v => v * 10));
                analytic["knots"] = new JArray(knots); analytic["weights"] = new JArray(weights); analytic["rational"] = rational;
                analytic["fit_method"] = family == "spline" ? new JValue(26369) : JValue.CreateNull();
            }
            else
            {
                // Axis-aligned ellipse at0/90: derivatives (0,b),(-a,0), with outward reversal at Start.
                tx = endpoint == "Start" ? 0 : -1; ty = endpoint == "Start" ? -1 : 0;
                analytic["center_mm"] = new JArray((5 + offset) * 10, 50);
                analytic["radii_mm"] = family == "elliptical_arc" ? new JArray(20, 10) : new JArray(10, 10);
            }
            double norm = Math.Sqrt(tx * tx + ty * ty);
            if (!double.IsFinite(norm) || norm <= 1e-9) throw new HostFailure73("Supplement endpoint tangent is degenerate.");
            tx /= norm; ty /= norm;
            analytic["source_start_mm"] = new JArray(sourceStart.Geometry.X * 10, sourceStart.Geometry.Y * 10);
            analytic["source_end_mm"] = new JArray(sourceEnd.Geometry.X * 10, sourceEnd.Geometry.Y * 10);
            analytic["outgoing_tangent"] = new JArray(tx, ty);
            SketchPoint? target = null;
            if (mode == "native_point") target = sketch.SketchPoints.Add(g.CreatePoint2d(p.X + tx - ty * side, p.Y + ty + tx * side), false);
            if (mode == "other_source_endpoint") target = endpoint == "Start" ? sourceEnd : sourceStart;
            SupplementFixtures83.Add(new SupplementFixture83(doc, sketch, source, family, endpoint, side, mode, target, analytic));
        }
        if (!doc.Rebuild2(false)) throw new HostFailure73("Supplement fixture rebuild failed.");
    }

    private static SketchPoint SupplementEndpoint83(SketchEntity entity, bool start) => entity switch
    {
        SketchArc arc => start ? arc.StartSketchPoint : arc.EndSketchPoint,
        SketchEllipticalArc arc => start ? arc.StartSketchPoint : arc.EndSketchPoint,
        SketchSpline spline => start ? spline.StartSketchPoint : spline.EndSketchPoint,
        SketchControlPointSpline spline => start ? spline.StartSketchPoint : spline.EndSketchPoint,
        _ => throw new HostFailure73("Supplement source endpoint API is unavailable.")
    };

    private static bool SupplementEntity83(SketchEntity entity, JObject row)
    {
        if (entity is not SketchEllipticalArc && entity is not SketchSpline && entity is not SketchControlPointSpline) return false;
        var start = SupplementEndpoint83(entity, true); var end = SupplementEndpoint83(entity, false);
        row["start_key"] = Key73(start); row["end_key"] = Key73(end);
        row["start_mm"] = new JArray(start.Geometry.X * 10, start.Geometry.Y * 10);
        row["end_mm"] = new JArray(end.Geometry.X * 10, end.Geometry.Y * 10);
        if (entity is SketchEllipticalArc arc)
        {
            row["type"] = "elliptical_arc"; row["center_key"] = Key73(arc.CenterSketchPoint);
            row["center_mm"] = new JArray(arc.CenterSketchPoint.Geometry.X * 10, arc.CenterSketchPoint.Geometry.Y * 10);
            row["major_radius_mm"] = arc.MajorRadius * 10; row["minor_radius_mm"] = arc.MinorRadius * 10;
        }
        else
        {
            row["type"] = entity is SketchSpline ? "spline" : "control_point_spline";
            BSplineCurve2d geometry = entity is SketchSpline spline ? spline.Geometry : ((SketchControlPointSpline)entity).Geometry;
            geometry.GetBSplineInfo(out int order, out int polesCount, out int knotsCount, out bool rational, out bool periodic, out bool closed);
            double[] poles = Array.Empty<double>(), knots = Array.Empty<double>(), weights = Array.Empty<double>();
            geometry.GetBSplineData(ref poles, ref knots, ref weights);
            weights ??= Array.Empty<double>();
            row["order"] = order; row["poles_mm"] = new JArray(poles.Select(p => p * 10)); row["knots"] = new JArray(knots);
            row["weights"] = new JArray(weights); row["rational"] = rational; row["periodic"] = periodic; row["closed"] = closed;
            row["pole_count"] = polesCount; row["knot_count"] = knotsCount;
        }
        return true;
    }

    private static void WriteSupplementOracle83()
    {
        GuardDocuments73(); var rows = new JArray();
        foreach (var fixture in SupplementFixtures83)
        {
            var owned = Documents.Single(d => d.InternalName == fixture.Document.InternalName);
            if (owned.SavedPath == null) throw new HostFailure73("Supplement oracle requires owned saved identities.");
            string Encode(object entity, string kind) => new PersistentEntityReference { DocumentId = owned.DocumentId,
                EntityType = kind, Key = Convert.FromBase64String(Key73(entity)), Context = Array.Empty<byte>() }.Encode();
            rows.Add(new JObject { ["document_id"] = owned.DocumentId, ["context"] = owned.Sheet ? "Sheet" : "Part",
                ["sketch_name"] = fixture.Sketch.Name, ["sketch_id"] = Encode(fixture.Sketch, "sketch"),
                ["source_id"] = Encode(fixture.Source, "sketch_entity"), ["family"] = fixture.Family, ["endpoint"] = fixture.Endpoint,
                ["side"] = fixture.Side, ["anchor_mode"] = fixture.AnchorMode, ["analytic"] = fixture.Analytic,
                ["source_start_key"] = Key73(SupplementEndpoint83(fixture.Source, true)),
                ["source_end_key"] = Key73(SupplementEndpoint83(fixture.Source, false)),
                ["target_id"] = fixture.Target == null ? null : Encode(fixture.Target, "sketch_entity"),
                ["target_key"] = fixture.Target == null ? null : Key73(fixture.Target) });
        }
        if (rows.Count != 52) throw new HostFailure73("Supplement fixture matrix cardinality differs.");
        WritePublic73("supplement-fixtures83.json", new JObject { ["schema"] = 1, ["run_id"] = Settings.RunId,
            ["target_id"] = TargetId, ["cases"] = rows, ["evidence"] = "DRAFT native construction/definition data; no tangent evaluator, no client or desktop PASS." });
    }
}
