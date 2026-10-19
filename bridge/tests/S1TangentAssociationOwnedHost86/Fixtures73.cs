using System.Globalization;
using System.Security.Cryptography;
using Bimwright.Ipt.Shared.Contracts;
using Inventor;
using Newtonsoft.Json.Linq;
using File = System.IO.File;
using Path = System.IO.Path;

internal static partial class Program
{
    private static JObject Inspect73()
    {
        Phase = "readonly_inspection"; GuardDocuments73();
        var before = Documents.ToDictionary(d => d.InternalName, d => (revision: d.Document.DatabaseRevisionId, dirty: d.Document.Dirty));
        var documents = new JArray();
        foreach (var record in Documents)
        {
            var doc = record.Document;
            if (record.SavedPath == null || DocumentIdComposer.Compose(doc.InternalName, doc.FullFileName, false) != record.DocumentId)
                throw new HostFailure73("Inspection requires the final owned saved fixture identities.");
            string sha = HashFile73(record.SavedPath);
            var sketches = new JArray();
            foreach (PlanarSketch sketch in doc.ComponentDefinition.Sketches)
            {
                var texts = new JArray(); foreach (TextBox text in sketch.TextBoxes) texts.Add(Text73(text, sketch));
                sketches.Add(new JObject { ["name"] = sketch.Name, ["key"] = Key73(sketch), ["visible"] = sketch.Visible,
                    ["entities"] = new JArray(sketch.SketchEntities.Cast<SketchEntity>().Select(e => Entity73(e))),
                    ["dimensions"] = new JArray(sketch.DimensionConstraints.Cast<DimensionConstraint>().Select(d => new JObject
                    { ["key"] = Key73(d), ["type"] = d.Type.ToString(), ["name"] = d.Parameter.Name, ["expression"] = d.Parameter.Expression,
                        ["value_internal"] = JToken.FromObject(d.Parameter.Value), ["driven"] = d.Driven })),
                    ["constraints"] = new JArray(sketch.GeometricConstraints.Cast<GeometricConstraint>().Select(Constraint83)),
                    ["texts"] = texts, ["image_count"] = sketch.SketchImages.Count });
            }
            documents.Add(new JObject { ["context"] = record.Sheet ? "sheetmetal" : "part", ["document_id"] = record.DocumentId,
                ["internal_name"] = record.InternalName, ["path"] = record.SavedPath, ["name"] = doc.DisplayName,
                ["database_revision_id"] = doc.DatabaseRevisionId, ["dirty"] = doc.Dirty,
                ["file_sha256"] = sha, ["baseline_file_unchanged"] = sha == record.BaselineSha,
                ["body_count"] = doc.ComponentDefinition.SurfaceBodies.Count, ["sketches"] = sketches,
                ["parameters"] = new JArray(doc.ComponentDefinition.Parameters.Cast<Parameter>().Select(p => new JObject
                { ["key"] = Key73(p), ["name"] = p.Name, ["expression"] = p.Expression, ["unit"] = p.get_Units(),
                    ["kind"] = p.ParameterType.ToString(), ["native_value"] = JToken.FromObject(p.Value) })),
                ["ole_count"] = doc.ReferencedOLEFileDescriptors.Count });
        }
        GuardDocuments73();
        if (Documents.Any(d => d.Document.DatabaseRevisionId != before[d.InternalName].revision || d.Document.Dirty != before[d.InternalName].dirty))
            throw new HostFailure73("Read-only inspection changed native revision or Dirty.");
        string? active = Application!.ActiveDocument?.InternalName;
        if (active != null && !Documents.Any(d => d.InternalName == active)) throw new HostFailure73("Inspection encountered an unowned active document.");
        return new JObject { ["schema"] = 1, ["run_id"] = Settings.RunId, ["captured_utc"] = DateTimeOffset.UtcNow.ToString("O"),
            ["owned_pid"] = OwnedPid, ["active_document_id"] = Documents.SingleOrDefault(d => d.InternalName == active)?.DocumentId,
            ["readonly_native_revision_dirty_preserved"] = true, ["documents"] = documents,
            ["open_s1_gates"] = 18, ["quest_proof"] = false };
    }

    private static string Key73(object entity)
    {
        byte[] key = Array.Empty<byte>(); ((dynamic)entity).GetReferenceKey(ref key, 0);
        if (key.Length == 0) throw new HostFailure73("Native fixture object has no persistent key.");
        return Convert.ToBase64String(key);
    }
    private static JObject Entity73(SketchEntity entity)
    {
        var row = new JObject { ["key"] = Key73(entity), ["native_type"] = entity.Type.ToString(), ["construction"] = entity.Construction };
        if (entity is SketchLine line)
        {
            row["type"] = "line"; row["start_key"] = Key73(line.StartSketchPoint); row["end_key"] = Key73(line.EndSketchPoint);
            row["start_mm"] = new JArray(line.StartSketchPoint.Geometry.X * 10, line.StartSketchPoint.Geometry.Y * 10);
            row["end_mm"] = new JArray(line.EndSketchPoint.Geometry.X * 10, line.EndSketchPoint.Geometry.Y * 10);
            row["centerline"] = line.Centerline;
        }
        else if(entity is SketchArc arc)
        {
            row["type"]="arc";row["start_key"]=Key73(arc.StartSketchPoint);row["end_key"]=Key73(arc.EndSketchPoint);
            row["center_key"]=Key73(arc.CenterSketchPoint);
            row["start_mm"]=new JArray(arc.StartSketchPoint.Geometry.X*10,arc.StartSketchPoint.Geometry.Y*10);
            row["end_mm"]=new JArray(arc.EndSketchPoint.Geometry.X*10,arc.EndSketchPoint.Geometry.Y*10);
            row["center_mm"]=new JArray(arc.CenterSketchPoint.Geometry.X*10,arc.CenterSketchPoint.Geometry.Y*10);
            row["radius_mm"]=arc.Radius*10;
        }
        else if (entity is SketchPoint point)
        { row["type"] = "point"; row["point_mm"] = new JArray(point.Geometry.X * 10, point.Geometry.Y * 10); row["hole_center"] = point.HoleCenter; }
        else row["type"] = "other_native_entity"; // Do not pretend to have audited unlisted geometry.
        return row;
    }
    private static JObject Constraint83(GeometricConstraint constraint)
    {
        var row=new JObject {["key"]=Key73(constraint),["type"]=constraint.Type.ToString()};
        if(constraint is TangentSketchConstraint tangent)
        {
            row["entity_one_key"]=Key73(tangent.EntityOne);row["entity_two_key"]=Key73(tangent.EntityTwo);
            row["shared_endpoint_keys"]=new JArray(EndpointKeys83(tangent.EntityOne)
                .Intersect(EndpointKeys83(tangent.EntityTwo),StringComparer.Ordinal));
        }
        return row;
    }
    private static string[] EndpointKeys83(SketchEntity entity)=>entity switch
    {
        SketchLine line=>new[]{Key73(line.StartSketchPoint),Key73(line.EndSketchPoint)},
        SketchArc arc=>new[]{Key73(arc.StartSketchPoint),Key73(arc.EndSketchPoint)},
        SketchEllipticalArc arc=>new[]{Key73(arc.StartSketchPoint),Key73(arc.EndSketchPoint)},
        SketchSpline spline=>new[]{spline.StartSketchPoint,spline.EndSketchPoint}.Where(p=>p!=null).Select(p=>Key73(p)).ToArray(),
        SketchControlPointSpline spline=>new[]{spline.StartSketchPoint,spline.EndSketchPoint}.Where(p=>p!=null).Select(p=>Key73(p)).ToArray(),
        _=>Array.Empty<string>()
    };
    private static JObject Text73(TextBox text, PlanarSketch sketch)
    {
        int vertices = 0, faces = 0; double[] coordinates = Array.Empty<double>(); int[] indices = Array.Empty<int>();
        text.GetFacetsInfo(out vertices, out faces, out coordinates, out indices);
        if (vertices < 0 || vertices > 8192 || faces < 0 || faces > 32768 || coordinates.Length != vertices * 3
            || indices.Length != faces * 3 || coordinates.Any(v => !double.IsFinite(v)) || indices.Any(i => i < 1 || i > vertices))
            throw new HostFailure73("Native text glyph arrays exceed the fixture budget or are malformed.");
        var minimum = new double[3]; var maximum = new double[3];
        for (int axis = 0; axis < 3; axis++)
        {
            minimum[axis] = vertices == 0 ? 0 : Enumerable.Range(0, vertices).Min(i => coordinates[i * 3 + axis] * 10);
            maximum[axis] = vertices == 0 ? 0 : Enumerable.Range(0, vertices).Max(i => coordinates[i * 3 + axis] * 10);
        }
        var range = text.RangeBox; var origin = text.Origin; double c = Math.Cos(text.Rotation), s = Math.Sin(text.Rotation);
        double[] xs = { range.MinPoint.X, range.MaxPoint.X, range.MaxPoint.X, range.MinPoint.X };
        double[] ys = { range.MinPoint.Y, range.MinPoint.Y, range.MaxPoint.Y, range.MaxPoint.Y };
        var bounds = new JArray();
        for (int i = 0; i < 4; i++)
        {
            double x = xs[i] - origin.X, y = ys[i] - origin.Y;
            bounds.Add(new JArray((origin.X + c * x - s * y) * 10, (origin.Y + s * x + c * y) * 10));
        }
        byte red = 0, green = 0, blue = 0; text.GetColor(out red, out green, out blue);
        return new JObject { ["key"] = Key73(text), ["native_type"] = text.Type.ToString(), ["text"] = text.Text,
            ["formatted_text"] = text.FormattedText, ["fitted"] = text.Fitted, ["style"] = text.Style.Name,
            ["origin_mm"] = new JArray(origin.X * 10, origin.Y * 10), ["rotation_rad"] = text.Rotation, ["width_scale"] = text.WidthScale,
            ["bounds_sketch_mm"] = bounds, ["glyph_coordinate_space"] = "model_mm", ["glyph_vertices"] = vertices, ["glyph_faces"] = faces,
            ["glyph_min_model_mm"] = new JArray(minimum), ["glyph_max_model_mm"] = new JArray(maximum),
            ["glyph_coordinates_sha256"] = HashText73(new JArray(coordinates).ToString(Newtonsoft.Json.Formatting.None)),
            ["glyph_indices_sha256"] = HashText73(new JArray(indices).ToString(Newtonsoft.Json.Formatting.None)),
            ["color_rgb"] = new JArray((int)red, (int)green, (int)blue) };
    }
    private static string HashText73(string text) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)));
}
