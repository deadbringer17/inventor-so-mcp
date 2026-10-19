using System.Globalization;
using System.Security.Cryptography;
using Bimwright.Ipt.Shared.Contracts;
using Inventor;
using Newtonsoft.Json.Linq;
using File = System.IO.File;
using Path = System.IO.Path;

internal static partial class Program
{
    private static void CreateFixture73(bool sheet)
    {
        Phase = sheet ? "prepare_sheet_fixture" : "prepare_part_fixture";
        GuardDocuments73();
        var app = Application!;
        string template = sheet ? app.FileManager.GetTemplateFile(DocumentTypeEnum.kPartDocumentObject,
            SystemOfMeasureEnum.kDefaultSystemOfMeasure, DraftingStandardEnum.kDefault_DraftingStandard,
            "{9C464203-9BAE-11D3-8BAD-0060B0CE6BB4}") : app.FileManager.GetTemplateFile(DocumentTypeEnum.kPartDocumentObject);
        string name = "XR_S1_Quest_Acceptance_" + (sheet ? "Sheet" : "Part") + ".ipt";
        string path = Path.GetFullPath(Path.Combine(FixtureDirectory, name));
        if (Path.GetDirectoryName(path) != Path.GetFullPath(FixtureDirectory) || File.Exists(path))
            throw new HostFailure73("Fixture destination is not a new exact owned file.");
        var doc = (PartDocument)app.Documents.Add(DocumentTypeEnum.kPartDocumentObject, template, true);
        // Persist the returned native identity immediately, before any other application
        // mutation, including DisplayName. Recovery never relies on a filename prefix.
        var record = new OwnedDocument73(doc, sheet, doc.InternalName, path);
        Documents.Add(record); PersistOwnership73();
        GuardDocuments73();
        if ((doc.ComponentDefinition is SheetMetalComponentDefinition) != sheet)
            throw new HostFailure73("Fixture template subtype differs.");
        doc.DisplayName = name;
        var def = doc.ComponentDefinition; var geometry = app.TransientGeometry;
        var baseSketch = def.Sketches.Add(def.WorkPlanes[3]); baseSketch.Name = "S1_Base";
        baseSketch.SketchLines.AddAsTwoPointRectangle(geometry.CreatePoint2d(0, 0), geometry.CreatePoint2d(10, 6));
        var profile = baseSketch.Profiles.AddForSolid();
        if (sheet)
        {
            var faces = ((SheetMetalFeatures)def.Features).FaceFeatures;
            faces.Add(faces.CreateFaceFeatureDefinition(profile));
        }
        else
        {
            var extrusion = def.Features.ExtrudeFeatures.CreateExtrudeDefinition(profile, PartFeatureOperationEnum.kJoinOperation);
            extrusion.SetDistanceExtent(1, PartFeatureExtentDirectionEnum.kPositiveExtentDirection);
            def.Features.ExtrudeFeatures.Add(extrusion);
        }
        baseSketch.Visible = false;
        var create = def.Sketches.Add(def.WorkPlanes[3]); create.Name = "S1_Create"; create.Visible = true;
        if (create.SketchEntities.Count != 0 || create.GeometricConstraints.Count != 0 || create.DimensionConstraints.Count != 0
            || create.TextBoxes.Count != 0 || create.SketchImages.Count != 0)
            throw new HostFailure73("S1_Create is not empty; no geometry is deleted to force the baseline.");
        def.Parameters.UserParameters.AddByExpression("S1QuestLength73", "10 mm", "mm");
        def.Parameters.UserParameters.AddByValue("S1QuestFlag73", false, UnitsTypeEnum.kBooleanUnits);
        def.Parameters.UserParameters.AddByValue("S1QuestLabel73", "Quest73", UnitsTypeEnum.kTextUnits);
        var read = def.Sketches.Add(def.WorkPlanes[3]); read.Name = "S1_TextRead"; read.Visible = true;
        read.TextBoxes.AddFitted(geometry.CreatePoint2d(1, 2),
            "L=<Parameter Name='S1QuestLength73' Precision='0'>S1QuestLength73</Parameter>", Type.Missing);
        read.TextBoxes.AddByRectangle(geometry.CreatePoint2d(1, 5), geometry.CreatePoint2d(8, 3), "Quest73", Type.Missing);
        if (!doc.Rebuild2(false) || def.SurfaceBodies.Count != 1 || read.TextBoxes.Count != 2 || read.TextBoxes[1].Text != "L=10")
            throw new HostFailure73("Native body/text fixture initialization differs.");
        foreach (TextBox text in read.TextBoxes) _ = Text73(text, read);
        CreateSupplementFixtures83(doc);
        GuardDocuments73(); doc.SaveAs(path, false);
        // SaveAs is the one allowed transition from unsaved to this intended path.
        // Validate directly before changing the in-memory ownership record.
        if (doc.InternalName != record.InternalName || !SamePath73(doc.FullFileName, path))
            throw new HostFailure73("Saved fixture identity/path differs from the owned destination.");
        record.SavedPath = path; record.DocumentId = DocumentIdComposer.Compose(doc.InternalName, doc.FullFileName, false);
        if (record.DocumentId.Length > 256 || !record.DocumentId.StartsWith("doc_", StringComparison.Ordinal))
            throw new HostFailure73("Fixture document id is invalid.");
        record.BaselineSha = HashFile73(path); PersistOwnership73();
        GuardDocuments73();
    }

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
        var row = new JObject { ["key"] = Key73(entity), ["native_type"] = entity.Type.ToString(), ["construction"] = entity.Construction,
            ["constraint_status"] = entity.ConstraintStatus.ToString() };
        // SketchSplineHandle derives from SketchLine. Preserve the actual native
        // role before any base-interface classification; no production helper used.
        if (entity is SketchSplineHandle handle)
        {
            row["type"] = "spline_handle";
            var ownership = HandleOwnership83(handle);
            foreach (var property in ownership.Properties()) row[property.Name] = property.Value.DeepClone();
            row["start_key"] = Key73(handle.StartSketchPoint); row["end_key"] = Key73(handle.EndSketchPoint);
            row["start_mm"] = new JArray(handle.StartSketchPoint.Geometry.X * 10, handle.StartSketchPoint.Geometry.Y * 10);
            row["end_mm"] = new JArray(handle.EndSketchPoint.Geometry.X * 10, handle.EndSketchPoint.Geometry.Y * 10);
        }
        else if (entity is SketchLine line)
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
        else if (!SupplementEntity83(entity, row)) row["type"] = "other_native_entity";
        return row;
    }
    private static JObject Constraint83(GeometricConstraint constraint)
    {
        var row=new JObject {["key"]=Key73(constraint),["type"]=constraint.Type.ToString()};
        if(constraint is TangentSketchConstraint tangent)
        {
            row["entity_one_key"]=Key73(tangent.EntityOne);row["entity_two_key"]=Key73(tangent.EntityTwo);
            row["entity_one_type"]=tangent.EntityOne.Type.ToString();row["entity_two_type"]=tangent.EntityTwo.Type.ToString();
            row["entity_one_member"]=NativeMember83(tangent.EntityOne);
            row["entity_two_member"]=NativeMember83(tangent.EntityTwo);
            row["shared_endpoint_keys"]=new JArray(EndpointKeys83(tangent.EntityOne)
                .Intersect(EndpointKeys83(tangent.EntityTwo),StringComparer.Ordinal));
            // A handle's FitPoint normally lies inside its line, not at either
            // handle endpoint. Record this owner relationship separately from
            // the actual EntityOne/EntityTwo endpoint intersection above.
            var fitPoints = new List<string>();
            if (tangent.EntityOne is SketchSplineHandle one)
            {
                row["entity_one_handle_owner"] = HandleOwnership83(one);
                string key = Key73(one.FitPoint);
                if (EndpointKeys83(tangent.EntityTwo).Contains(key, StringComparer.Ordinal)) fitPoints.Add(key);
            }
            if (tangent.EntityTwo is SketchSplineHandle two)
            {
                row["entity_two_handle_owner"] = HandleOwnership83(two);
                string key = Key73(two.FitPoint);
                if (EndpointKeys83(tangent.EntityOne).Contains(key, StringComparer.Ordinal)) fitPoints.Add(key);
            }
            row["handle_fit_point_shared_with_other_member_keys"] = new JArray(fitPoints.Distinct(StringComparer.Ordinal));
        }
        return row;
    }
    private static JObject NativeMember83(SketchEntity entity)
    {
        var row = new JObject { ["key"] = Key73(entity), ["native_type"] = entity.Type.ToString(),
            ["constraint_status"] = entity.ConstraintStatus.ToString(),
            ["type"] = entity is SketchSplineHandle ? "spline_handle" : entity is SketchLine ? "line"
                : entity is SketchArc ? "arc" : entity is SketchSpline ? "spline"
                : entity is SketchControlPointSpline ? "control_point_spline" : entity is SketchEllipticalArc ? "elliptical_arc"
                : entity is SketchPoint ? "point" : "other_native_entity" };
        if (entity is SketchSplineHandle handle) row["handle_owner"] = HandleOwnership83(handle);
        return row;
    }
    private static JObject HandleOwnership83(SketchSplineHandle handle)
    {
        var spline = handle.Spline; var fitPoint = handle.FitPoint;
        var parent = handle.Parent; var start = handle.StartSketchPoint; var end = handle.EndSketchPoint;
        if (spline == null || fitPoint == null || parent == null || start == null || end == null
            || !SameCom73(spline.Parent, parent) || !SameCom73(fitPoint.Parent, parent)
            || !SameCom73(start.Parent, parent) || !SameCom73(end.Parent, parent))
            throw new HostFailure73("Independent handle owner/sketch identity differs.");
        int count = spline.FitPointCount, matches = 0;
        if (count < 1 || count > 2000) throw new HostFailure73("Independent handle fit-point count exceeds budget.");
        for (int i = 1; i <= count; i++) if (SameCom73(spline.get_FitPoint(i), fitPoint)) matches++;
        if (matches != 1) throw new HostFailure73("Independent handle fit-point membership is ambiguous.");
        bool first = spline.StartSketchPoint != null && SameCom73(spline.StartSketchPoint, fitPoint);
        bool last = spline.EndSketchPoint != null && SameCom73(spline.EndSketchPoint, fitPoint);
        if (first && last) throw new HostFailure73("Independent closed spline handle endpoint role is ambiguous.");
        return new JObject { ["owner_spline_key"] = Key73(spline), ["fit_point_key"] = Key73(fitPoint),
            ["fit_point_role"] = first ? "Start" : last ? "End" : "Interior", ["sketch_key"] = Key73(parent),
            ["owner_spline_constraint_status"] = spline.ConstraintStatus.ToString(),
            ["start_key"] = Key73(start), ["end_key"] = Key73(end),
            ["fit_point_distinct_from_handle_endpoints"] = !SameCom73(fitPoint, start) && !SameCom73(fitPoint, end) };
    }
    private static string[] EndpointKeys83(SketchEntity entity)=>entity switch
    {
        SketchSplineHandle handle=>new[]{Key73(handle.StartSketchPoint),Key73(handle.EndSketchPoint)},
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
