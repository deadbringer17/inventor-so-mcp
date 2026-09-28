#if INVENTOR2027 && SO_EXPERIMENTAL
using System;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Handlers.Core;
using Bimwright.Ipt.Shared.Handlers.Parameters;
using Bimwright.Ipt.Shared.Infrastructure;
using Inventor;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Handlers.Experimental;

/// <summary>Revision-bound authoring references. No activation, transactions or document edits.</summary>
public sealed class DesignContextXrHandler : ExperimentalHandler
{
    public override string Name => "get_design_context_xr";
    public override bool IsReadOnly => true;
    protected override JToken Run(InventorCommandContext ctx, Application app, JObject p)
    {
        var doc = X.ActivePart(app, Name);
        var document = (global::Inventor.Document)doc;
        string id = EntityReferences.DocumentId(document), revision = X.Str(p, "expected_revision");
        if (X.Str(p, "document_id") != id) throw ConcurrencyFailure.DocumentChanged((string?)p["document_id"], id);
        if (ctx.Events == null || ctx.Events.Revision(id) != revision)
            throw ConcurrencyFailure.StaleRevision(revision, ctx.Events?.Revision(id));
        const int limit = 2000, maxPoints = 100000;
        int pointCount = 0;
        bool truncated = false;
        var def = doc.ComponentDefinition;
        var planes = new JArray(); var sketches = new JArray(); var faces = new JArray();
        var edges = new JArray(); var parameters = new JArray(); var warnings = new JArray();
        var sketchSnapshots = new JArray(); int sketchEntities = 0;
        for (int i = 1; i <= def.WorkPlanes.Count; i++)
        {
            X.Deadline(ctx, "reading Design planes");
            if (planes.Count >= limit) { truncated = true; break; }
            var plane = def.WorkPlanes[i];
            try
            {
                plane.GetPosition(out Point origin, out UnitVector x, out UnitVector y);
                planes.Add(new JObject { ["reference"] = i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["name"] = plane.Name, ["origin_mm"] = PointMm(origin),
                    ["x_axis"] = new JArray(x.X, x.Y, x.Z), ["y_axis"] = new JArray(y.X, y.Y, y.Z) });
            }
            catch (Exception ex) { warnings.Add("Work plane " + i + ": " + ex.Message); }
        }
        foreach (PlanarSketch sketch in def.Sketches)
        {
            X.Deadline(ctx, "reading Design sketches");
            if (sketches.Count >= limit) { truncated = true; break; }
            var frame = SketchFrame(app, sketch);
            frame["name"] = sketch.Name; frame["visible"] = sketch.Visible;
            frame["entity_count"] = sketch.SketchEntities.Count;
            frame["constraint_status"] = sketch.ConstraintStatus.ToString();
            sketches.Add(frame);
            int count=sketch.SketchEntities.Count;
            if (sketchSnapshots.Count<256 && sketchEntities+count<=20000)
            {
                var snapshot=new GetSketchInfoHandler().Execute(ctx,new JObject { ["sketch_name"]=sketch.Name,["max_entities"]=20000 });
                if(snapshot.Ok && snapshot.Data is JObject data)
                {
                    data["frame"]=SketchFrame(app,sketch); data["visible"]=sketch.Visible;
                    sketchSnapshots.Add(data); sketchEntities+=count;
                }
                else { truncated=true; warnings.Add("Sketch geometry unavailable: "+sketch.Name); }
            }
            else { truncated=true; warnings.Add("Sketch geometry limit: "+sketch.Name); }
        }
        int bodyIndex = 0;
        foreach (SurfaceBody body in def.SurfaceBodies)
        {
            bodyIndex++;
            int faceOrdinal = 0;
            foreach (Face face in body.Faces)
            {
                faceOrdinal++;
                X.Deadline(ctx, "reading Design faces");
                if (faces.Count >= limit) { truncated = true; break; }
                if (face.SurfaceType != SurfaceTypeEnum.kPlaneSurface) continue;
                var plane = (Plane)face.Geometry;
                double sign = face.IsParamReversed ? -1 : 1;
                faces.Add(new JObject { ["id"] = X.Describe(document, face), ["body"] = body.Name,
                    ["body_index"] = bodyIndex, ["face_ordinal"] = faceOrdinal,
                    ["point_mm"] = PointMm(face.PointOnFace),
                    ["normal"] = new JArray(plane.Normal.X * sign, plane.Normal.Y * sign, plane.Normal.Z * sign) });
            }
            foreach (Edge edge in body.Edges)
            {
                X.Deadline(ctx, "reading Design edges");
                if (edges.Count >= limit || pointCount >= maxPoints) { truncated = true; break; }
                var item = new JObject { ["id"] = X.Describe(document, edge), ["body"] = body.Name,
                    ["kind"] = edge.GeometryType.ToString() };
                try
                {
                    var evaluator = edge.Evaluator;
                    evaluator.GetParamExtents(out double min, out double max);
                    // COM SAFEARRAY handling follows the tessellator's verified late-bound pattern.
                    dynamic source = evaluator;
                    int count = 0; double[] coordinates = new double[0];
                    source.GetStrokes(min, max, 0.01, out count, out coordinates);
                    if (count < 2 || coordinates.Length != count * 3 || count > 8192 || pointCount + count > maxPoints)
                        throw new InvalidOperationException("Edge stroke limit exceeded or invalid geometry.");
                    pointCount += count;
                    var points = new JArray();
                    foreach (double coordinate in coordinates)
                    {
                        if (double.IsNaN(coordinate) || double.IsInfinity(coordinate)) throw new InvalidOperationException("Non-finite edge coordinate.");
                        points.Add(UnitConvert.CmToMm(coordinate));
                    }
                    item["points_mm"] = points;
                    evaluator.GetLengthAtParam(min, max, out double length);
                    item["length_mm"] = UnitConvert.CmToMm(length);
                }
                catch (Exception ex) { item["points_mm"] = null; item["unavailable"] = ex.Message; }
                edges.Add(item);
            }
        }
        foreach (Parameter parameter in def.Parameters)
        {
            X.Deadline(ctx, "reading Design parameters");
            if (parameters.Count >= limit) { truncated = true; break; }
            parameters.Add(ParameterValueDto.From(parameter));
        }
        if (ctx.Events.Revision(id) != revision) throw ConcurrencyFailure.StaleRevision(revision, ctx.Events.Revision(id));
        return new JObject { ["document_id"] = id, ["revision"] = revision, ["kind"] = "part",
            ["planes"] = planes, ["sketches"] = sketches, ["faces"] = faces, ["edges"] = edges,
            ["parameters"] = parameters, ["sketch_snapshots"] = sketchSnapshots, ["truncated"] = truncated, ["warnings"] = warnings };
    }

    internal static JObject SketchFrame(Application app, PlanarSketch sketch)
    {
        var tg = app.TransientGeometry;
        var origin = sketch.SketchToModelSpace(tg.CreatePoint2d(0, 0));
        var x = sketch.SketchToModelSpace(tg.CreatePoint2d(1, 0));
        var y = sketch.SketchToModelSpace(tg.CreatePoint2d(0, 1));
        return new JObject { ["origin_mm"] = PointMm(origin),
            ["x_axis"] = new JArray(x.X-origin.X, x.Y-origin.Y, x.Z-origin.Z),
            ["y_axis"] = new JArray(y.X-origin.X, y.Y-origin.Y, y.Z-origin.Z) };
    }
    private static JArray PointMm(Point p) => new(UnitConvert.CmToMm(p.X), UnitConvert.CmToMm(p.Y), UnitConvert.CmToMm(p.Z));
}
#endif
