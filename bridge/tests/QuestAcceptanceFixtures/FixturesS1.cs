using Inventor;
using Newtonsoft.Json.Linq;
using File = System.IO.File;
using Path = System.IO.Path;

/// <summary>Dedicated S1 native fixture lifecycle. Call only from the existing STA entry point.</summary>
internal static class S1Fixtures
{
    private const string Prefix = "XR_S1_Quest_Acceptance_";
    private const string PartName = Prefix + "Part.ipt", SheetName = Prefix + "Sheet.ipt";
    private const string SketchName = "S1_Create";
    private const string SheetSubtype = "{9C464203-9BAE-11D3-8BAD-0060B0CE6BB4}";
    private static string ManifestPath => Path.Combine(System.Environment.CurrentDirectory, "artifacts", "s1-verification", "quest-fixture.json");
    private static string Id(Document document) => "doc_" + document.InternalName;
    private static bool SamePath(string a, string b) => !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b)
        && string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
    private static Document[] Open(global::Inventor.Application app) => app.Documents.Cast<Document>().ToArray();
    private static JObject State(Document document) => new()
    {
        ["document_id"] = Id(document), ["internal_name"] = document.InternalName,
        ["path"] = document.FullFileName, ["name"] = document.DisplayName,
        ["database_revision_id"] = document.DatabaseRevisionId, ["dirty"] = document.Dirty
    };

    private static void Write(JObject manifest)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ManifestPath)!);
        File.WriteAllText(ManifestPath, manifest.ToString());
    }

    internal static int Prepare(global::Inventor.Application app)
    {
        if (File.Exists(ManifestPath))
        {
            var old = Load();
            if (((JArray)old["owned_documents"]!).OfType<JObject>().Any(row => Open(app).Any(d =>
                Id(d) == (string?)row["document_id"] || SamePath(d.FullFileName, (string)row["path"]!))))
                throw new InvalidOperationException("Previous S1 fixture is still open; restore it before preparing another run.");
        }
        if (Open(app).Any(d => d.DisplayName.StartsWith(Prefix, StringComparison.Ordinal)))
            throw new InvalidOperationException("An unowned S1-named document is open; refusing duplicate fixture names.");

        var original = app.ActiveDocument;
        var previous = Open(app).Select(d => (JToken)State(d)).ToArray();
        string directory = Path.Combine(Path.GetTempPath(), "xrso-s1-quest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var created = new List<PartDocument>();
        var manifest = new JObject
        {
            ["milestone"] = "s1", ["schema_version"] = 1, ["status"] = "preparing",
            ["created_utc"] = DateTime.UtcNow.ToString("O"), ["directory"] = directory,
            ["previous_document"] = original?.FullFileName,
            ["previous_document_id"] = original == null ? null : Id(original),
            ["previous_documents"] = new JArray(previous), ["documents"] = new JArray(),
            ["owned_documents"] = new JArray(), ["inventor_version"] = app.SoftwareVersion.DisplayVersion,
            ["reader_evidence"] = "Direct COM SketchEntities enumeration; gateway reader/revision tokens not exercised here."
        };
        Write(manifest);
        try
        {
            foreach (bool sheet in new[] { false, true })
            {
                string template = sheet
                    ? app.FileManager.GetTemplateFile(DocumentTypeEnum.kPartDocumentObject,
                        SystemOfMeasureEnum.kDefaultSystemOfMeasure, DraftingStandardEnum.kDefault_DraftingStandard, SheetSubtype)
                    : app.FileManager.GetTemplateFile(DocumentTypeEnum.kPartDocumentObject);
                var part = (PartDocument)app.Documents.Add(DocumentTypeEnum.kPartDocumentObject, template, true);
                created.Add(part);
                if ((part.ComponentDefinition is SheetMetalComponentDefinition) != sheet)
                    throw new InvalidOperationException("Template did not create the requested Part/Sheet context.");
                // A real body lets the standard scene/context pipeline load; the editable sketch is separate.
                BaseBody(app, part, sheet);
                var def = part.ComponentDefinition;
                var sketch = def.Sketches.Add(def.WorkPlanes[3]);
                sketch.Name = SketchName;
                sketch.Visible = true;
                AssertEmpty(sketch);
                string path = Path.Combine(directory, sheet ? SheetName : PartName);
                part.SaveAs(path, false);
                var row = State((Document)part);
                row["context"] = sheet ? "sheetmetal" : "part";
                row["baseline"] = ReadSketch(part);
                ((JArray)manifest["owned_documents"]!).Add(row);
                ((JArray)manifest["documents"]!).Add(path);
                manifest[sheet ? "sheet" : "part"] = path;
                Write(manifest); // Persist ownership before creating the next document.
            }
            created[0].Activate();
            manifest["document_id"] = Id((Document)created[0]);
            manifest["active_document"] = created[0].FullFileName;
            manifest["expected"] = new JObject
            {
                ["fixture_documents"] = 2, ["sketch_name"] = SketchName,
                ["baseline_entities"] = 0, ["baseline_dimensions"] = 0, ["baseline_constraints"] = 0,
                ["line_start_mm"] = new JArray(0, 0), ["line_end_mm"] = new JArray(20, 0)
            };
            manifest["status"] = "prepared";
            Write(manifest);
            Console.WriteLine(manifest.ToString(Newtonsoft.Json.Formatting.None));
            return 0;
        }
        catch (Exception failure)
        {
            var cleanupErrors = new JArray();
            foreach (var part in Enumerable.Reverse(created))
                try { part.Close(true); } catch (Exception ex) { cleanupErrors.Add(ex.Message); }
            try { original?.Activate(); } catch (Exception ex) { cleanupErrors.Add(ex.Message); }
            manifest["status"] = "prepare_failed";
            manifest["failure"] = failure.Message;
            manifest["cleanup_errors"] = cleanupErrors;
            manifest["remaining_created_documents"] = new JArray(Open(app)
                .Where(d => created.Any(p => ReferenceEquals(p, d))).Select(d => (JToken)State(d)));
            Write(manifest);
            if (cleanupErrors.Count > 0) throw new AggregateException("S1 preparation and cleanup failed; inspect manifest before retry.", failure);
            throw;
        }
    }

    private static void BaseBody(global::Inventor.Application app, PartDocument part, bool sheet)
    {
        var def = part.ComponentDefinition;
        var sketch = def.Sketches.Add(def.WorkPlanes[3]);
        sketch.Name = "S1_Base";
        var tg = app.TransientGeometry;
        sketch.SketchLines.AddAsTwoPointRectangle(tg.CreatePoint2d(0, 0), tg.CreatePoint2d(10, 6));
        var profile = sketch.Profiles.AddForSolid();
        if (sheet)
        {
            var faces = ((SheetMetalFeatures)def.Features).FaceFeatures;
            faces.Add(faces.CreateFaceFeatureDefinition(profile));
        }
        else
        {
            var extrude = def.Features.ExtrudeFeatures.CreateExtrudeDefinition(profile, PartFeatureOperationEnum.kJoinOperation);
            extrude.SetDistanceExtent(1, PartFeatureExtentDirectionEnum.kPositiveExtentDirection);
            def.Features.ExtrudeFeatures.Add(extrude);
        }
        sketch.Visible = false;
    }

    private static void AssertEmpty(PlanarSketch sketch)
    {
        if (sketch.SketchEntities.Count != 0 || sketch.DimensionConstraints.Count != 0 || sketch.GeometricConstraints.Count != 0
            || sketch.TextBoxes.Count != 0 || sketch.SketchImages.Count != 0)
            throw new InvalidOperationException("New S1_Create is not empty in native SketchEntities/constraints/text/images; no geometry will be deleted to force the baseline.");
    }

    private static JObject ReadSketch(PartDocument part)
    {
        var matches = part.ComponentDefinition.Sketches.Cast<PlanarSketch>().Where(s => s.Name == SketchName).ToArray();
        if (matches.Length != 1) throw new InvalidOperationException("Expected one exact S1_Create sketch.");
        var sketch = matches[0];
        var entities = new JArray();
        foreach (SketchEntity entity in sketch.SketchEntities)
        {
            var row = new JObject { ["native_type"] = entity.Type.ToString(), ["construction"] = entity.Construction };
            if (entity is SketchLine line)
            {
                row["type"] = "line";
                row["start_mm"] = Mm(line.StartSketchPoint.Geometry);
                row["end_mm"] = Mm(line.EndSketchPoint.Geometry);
            }
            else if (entity is SketchPoint point) { row["type"] = "point"; row["point_mm"] = Mm(point.Geometry); }
            else row["type"] = "unexpected";
            entities.Add(row);
        }
        return new JObject
        {
            ["sketch_name"] = sketch.Name, ["visible"] = sketch.Visible,
            ["native_entity_count"] = sketch.SketchEntities.Count, ["entities"] = entities,
            ["dimension_count"] = sketch.DimensionConstraints.Count, ["constraint_count"] = sketch.GeometricConstraints.Count,
            ["text_count"] = sketch.TextBoxes.Count, ["image_count"] = sketch.SketchImages.Count,
            ["constraint_status"] = sketch.ConstraintStatus.ToString()
        };
    }

    private static JArray Mm(Point2d point) => new(point.X * 10, point.Y * 10);

    private static JObject Load()
    {
        var manifest = JObject.Parse(File.ReadAllText(ManifestPath));
        if ((string?)manifest["milestone"] != "s1" || (int?)manifest["schema_version"] != 1
            || manifest["owned_documents"] is not JArray rows || rows.Count > 2)
            throw new InvalidDataException("Invalid S1 ownership manifest.");
        string directory = Path.GetFullPath((string?)manifest["directory"] ?? throw new InvalidDataException("Missing fixture directory."));
        string temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string basename = Path.GetFileName(directory);
        if (!directory.StartsWith(temp, StringComparison.OrdinalIgnoreCase) || !basename.StartsWith("xrso-s1-quest-", StringComparison.Ordinal)
            || !Guid.TryParseExact(basename.Substring("xrso-s1-quest-".Length), "N", out _))
            throw new InvalidDataException("Fixture directory is not a dedicated S1 temporary directory.");
        foreach (var row in rows.OfType<JObject>())
        {
            string path = (string?)row["path"] ?? throw new InvalidDataException("Missing owned path.");
            string name = (string?)row["name"] ?? "";
            if (name is not (PartName or SheetName) || !SamePath(path, Path.Combine(directory, name))
                || string.IsNullOrWhiteSpace((string?)row["internal_name"]) || (string?)row["document_id"] != "doc_" + (string?)row["internal_name"])
                throw new InvalidDataException("Owned identity/path/name is inconsistent.");
        }
        if (rows.OfType<JObject>().Count() != rows.Count || rows.OfType<JObject>().Select(r => (string?)r["document_id"]).Distinct().Count() != rows.Count)
            throw new InvalidDataException("Duplicate or malformed owned records.");
        return manifest;
    }

    private static Document? FindOwned(global::Inventor.Application app, JObject row)
    {
        var matches = Open(app).Where(d => Id(d) == (string?)row["document_id"]
            || SamePath(d.FullFileName, (string)row["path"]!)).ToArray();
        if (matches.Length == 0) return null;
        if (matches.Length != 1 || Id(matches[0]) != (string?)row["document_id"]
            || !SamePath(matches[0].FullFileName, (string)row["path"]!) || matches[0].DisplayName != (string?)row["name"])
            throw new InvalidOperationException("Owned document identity/path changed; refusing native operation.");
        return matches[0];
    }

    internal static int Inspect(global::Inventor.Application app)
    {
        var manifest = Load();
        var result = new JObject { ["milestone"] = "s1", ["captured_utc"] = DateTime.UtcNow.ToString("O"),
            ["active_document"] = app.ActiveDocument == null ? null : Id(app.ActiveDocument),
            ["evidence"] = "Native COM readback only; no activation, solve, mutation or gateway revision token." };
        var documents = new JArray();
        foreach (var row in ((JArray)manifest["owned_documents"]!).OfType<JObject>())
        {
            var document = FindOwned(app, row) ?? throw new InvalidOperationException("Owned S1 fixture document is not open.");
            if (document is not PartDocument part) throw new InvalidOperationException("Owned fixture is no longer a part document.");
            var state = State(document);
            state["native_sketch"] = ReadSketch(part);
            documents.Add(state);
        }
        result["documents"] = documents;
        Console.WriteLine(result.ToString(Newtonsoft.Json.Formatting.None));
        return 0; // Inspection success is not a test verdict; caller must compare the reported native content.
    }

    internal static int Restore(global::Inventor.Application app)
    {
        var manifest = Load();
        if (manifest["previous_documents"] is not JArray previousRows)
            throw new InvalidDataException("Previous document baseline is missing.");
        // Validate every ownership record and previous document before the first close.
        var owned = ((JArray)manifest["owned_documents"]!).OfType<JObject>().Select(r => FindOwned(app, r)).Where(d => d != null).Cast<Document>().ToArray();
        string? previousId = (string?)manifest["previous_document_id"];
        var previous = previousId == null ? null : Open(app).SingleOrDefault(d => Id(d) == previousId);
        if (previousId != null && previous == null) throw new InvalidOperationException("Previous document is no longer open; refusing partial restore.");
        foreach (var row in previousRows.OfType<JObject>())
        {
            var document = Open(app).SingleOrDefault(d => Id(d) == (string?)row["document_id"])
                ?? throw new InvalidOperationException("A preexisting document is no longer open; refusing partial restore.");
            string path = (string?)row["path"] ?? "";
            if (!(string.IsNullOrWhiteSpace(path) && string.IsNullOrWhiteSpace(document.FullFileName)) && !SamePath(document.FullFileName, path))
                throw new InvalidOperationException("Preexisting document path changed; refusing partial restore.");
        }
        foreach (var document in Enumerable.Reverse(owned)) document.Close(true); // SkipSave=true.
        previous?.Activate();
        if (((JArray)manifest["owned_documents"]!).OfType<JObject>().Any(r => FindOwned(app, r) != null))
            throw new InvalidOperationException("Owned fixture remains open after restore.");
        if (previousId != null && (app.ActiveDocument == null || Id(app.ActiveDocument) != previousId))
            throw new InvalidOperationException("Previous active document was not restored.");
        if (previousId == null && app.ActiveDocument != null)
            throw new InvalidOperationException("Expected no previous active document; restore did not return to an empty active state.");
        var expectedIds = previousRows.OfType<JObject>().Select(r => (string)r["document_id"]!).OrderBy(id => id, StringComparer.Ordinal).ToArray();
        if (!Open(app).Select(Id).OrderBy(id => id, StringComparer.Ordinal).SequenceEqual(expectedIds, StringComparer.Ordinal))
            throw new InvalidOperationException("Open document set differs from the baseline after owned fixture closure.");
        foreach (var row in previousRows.OfType<JObject>())
        {
            var current = Open(app).Single(d => Id(d) == (string?)row["document_id"]);
            if (!JToken.DeepEquals(State(current), row))
                throw new InvalidOperationException("Preexisting document state changed; owned fixtures closed, no unrelated edits will be reversed.");
        }
        manifest["status"] = "restored";
        manifest["restored_utc"] = DateTime.UtcNow.ToString("O");
        Write(manifest);
        Console.WriteLine("S1 fixture restored without saving; temporary files retained for evidence.");
        return 0;
    }
}
