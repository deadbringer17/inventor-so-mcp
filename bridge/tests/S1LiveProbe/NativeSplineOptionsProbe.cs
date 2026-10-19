using Inventor;
using Newtonsoft.Json.Linq;

internal static partial class Program
{
    private static void RunSplineOptions(Inventor.Application app, PartDocument doc, JObject report)
    {
        if (app.ActiveEditObject is PlanarSketch editing) editing.ExitEdit();
        report["evidence"] = "Native API creation observations only; not desktop equivalence, product, Quest or physical PASS. Every transaction aborted.";
        report["kind"] = doc.ComponentDefinition is SheetMetalComponentDefinition ? "sheetmetal" : "part";
        var rows = new JArray(); report["option_cases"] = rows;
        int baselineSketches = doc.ComponentDefinition.Sketches.Count;
        foreach (string variant in new[] { "fit", "control" })
        foreach (int count in new[] { 2, 3, 5 })
        foreach (bool closed in new[] { false, true })
        foreach (int method in variant == "fit" ? new[] { 26369, 26370, 26371 } : new[] { 0 })
        {
            var row = new JObject { ["variant"] = variant, ["input_point_count"] = count,
                ["requested_closed"] = closed, ["fit_method"] = method,
                ["closure_strategy"] = variant == "fit" ? "Closed setter" : "repeat first control point" };
            rows.Add(row);
            var transaction = app.TransactionManager.StartTransaction((_Document)doc, "S1 spline options observation, aborted");
            string phase = "create";
            try
            {
                var sketch = doc.ComponentDefinition.Sketches.Add(doc.ComponentDefinition.WorkPlanes[3]);
                var points = app.TransientObjects.CreateObjectCollection();
                for (int i = 0; i < count; i++) points.Add(app.TransientGeometry.CreatePoint2d(i, Math.Sin(i * .7)));
                if (variant == "fit")
                {
                    var spline = sketch.SketchSplines.Add(points, (SplineFitMethodEnum)method);
                    phase = "close";
                    if (closed) spline.Closed = true;
                    phase = "solve"; sketch.Solve(); doc.Update2(true);
                    phase = "read";
                    row["actual_closed"] = spline.Closed;
                    row["actual_fit_method"] = (int)spline.FitMethod;
                    row["actual_point_count"] = spline.FitPointCount;
                    var actual = new JArray(); row["actual_points_mm"] = actual;
                    for (int i = 1; i <= spline.FitPointCount; i++)
                    { var p = spline.FitPoint[i].Geometry; actual.Add(new JArray(p.X * 10, p.Y * 10)); }
                }
                else
                {
                    if (closed) points.Add(app.TransientGeometry.CreatePoint2d(0, 0));
                    var spline = sketch.SketchControlPointSplines.Add(points);
                    phase = "solve"; sketch.Solve(); doc.Update2(true);
                    phase = "read";
                    row["actual_closed"] = spline.IsClosed;
                    row["actual_point_count"] = spline.ControlPointCount;
                    var actual = new JArray(); row["actual_points_mm"] = actual;
                    for (int i = 1; i <= spline.ControlPointCount; i++)
                    { var p = spline.ControlPoint[i].Geometry; actual.Add(new JArray(p.X * 10, p.Y * 10)); }
                }
                row["constraint_count"] = sketch.GeometricConstraints.Count;
                row["observation"] = "created and read";
            }
            catch (Exception ex)
            {
                row["observation"] = "native exception"; row["phase"] = phase;
                row["hresult"] = "0x" + ex.HResult.ToString("X8"); row["error"] = ex.ToString();
            }
            finally { transaction.Abort(); }
            if (doc.ComponentDefinition.Sketches.Count != baselineSketches)
                throw new InvalidOperationException("Spline option observation rollback changed sketch count.");
            row["rollback_sketch_count_restored"] = true;
        }
        report["observation_outcome"] = "PASS 24 observations collected and aborted; native exceptions remain observations, not successful creations.";
    }
}
