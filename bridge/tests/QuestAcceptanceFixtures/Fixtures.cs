using Inventor;
using Newtonsoft.Json.Linq;
using Path = System.IO.Path;

internal static class Fixtures
{
    private const string SheetMetalSubType = "{9C464203-9BAE-11D3-8BAD-0060B0CE6BB4}";

    private static string ManifestPath(string m) =>
        Path.Combine(System.Environment.CurrentDirectory, "artifacts", m + "-verification", "quest-fixture.json");

    private static bool SamePath(string? a, string? b) =>
        a != null && b != null && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static Document? FindOpen(global::Inventor.Application app, string? path) =>
        path == null ? null : app.Documents.Cast<Document>().FirstOrDefault(d => SamePath(d.FullFileName, path));

    private static string[] ManifestDocuments(JObject manifest) =>
        ((JArray?)manifest["documents"] ?? new JArray()).Select(t => (string)t!).ToArray();

    // ---------------------------------------------------------------- prepare

    internal static int Prepare(global::Inventor.Application app, string m)
    {
        var manifestPath = ManifestPath(m);
        if (System.IO.File.Exists(manifestPath))
        {
            var old = JObject.Parse(System.IO.File.ReadAllText(manifestPath));
            var stillOpen = ManifestDocuments(old).Where(p => FindOpen(app, p) != null).ToArray();
            if (stillOpen.Length > 0)
                throw new InvalidOperationException("A previous " + m + " fixture is still open (" + string.Join(", ", stillOpen) +
                    "); run --restore-quest " + m + " first.");
        }

        var directory = Path.Combine(Path.GetTempPath(), "xrso-" + m + "-quest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var original = app.ActiveDocument;
        var created = new List<object>();
        try
        {
            JObject manifest = m switch
            {
                "m1" or "m2" => PrepareAssembly(app, m, directory, created),
                "m3" => PrepareM3(app, directory, created),
                "m5" => PrepareM5(app, directory, created),
                "m6" => PrepareM6(app, directory, created),
                "m7" => PrepareM7(app, directory, created),
                _ => throw new ArgumentException("Unknown milestone " + m),
            };
            var active = app.ActiveDocument ?? throw new InvalidOperationException("No active document after preparing the fixture.");
            manifest["milestone"] = m;
            manifest["directory"] = directory;
            manifest["previous_document"] = original?.FullFileName;
            manifest["document_id"] = "doc_" + active.InternalName;
            manifest["active_document"] = active.FullFileName;
            Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
            System.IO.File.WriteAllText(manifestPath, manifest.ToString());
            Console.WriteLine("Quest fixture " + m + " prepared and left open: " + active.FullFileName);
            return 0;
        }
        catch
        {
            foreach (var doc in Enumerable.Reverse(created))
                try { ((dynamic)doc).Close(true); } catch { /* best effort */ }
            try { original?.Activate(); } catch { /* best effort */ }
            throw;
        }
    }

    private static void CreateBlock(global::Inventor.Application app, PartDocument part, double widthMm, double depthMm, double heightMm,
        bool centered, string sketchName)
    {
        var def = part.ComponentDefinition;
        var sketch = def.Sketches.Add(def.WorkPlanes[3]);
        sketch.Name = sketchName;
        var tg = app.TransientGeometry;
        var w = widthMm / 10; var d = depthMm / 10;
        if (centered) sketch.SketchLines.AddAsTwoPointRectangle(tg.CreatePoint2d(-w / 2, -d / 2), tg.CreatePoint2d(w / 2, d / 2));
        else sketch.SketchLines.AddAsTwoPointRectangle(tg.CreatePoint2d(0, 0), tg.CreatePoint2d(w, d));
        var extrude = def.Features.ExtrudeFeatures.CreateExtrudeDefinition(sketch.Profiles.AddForSolid(), PartFeatureOperationEnum.kJoinOperation);
        extrude.SetDistanceExtent(heightMm / 10, PartFeatureExtentDirectionEnum.kPositiveExtentDirection);
        def.Features.ExtrudeFeatures.Add(extrude);
    }

    private static JObject PrepareAssembly(global::Inventor.Application app, string m, string directory, List<object> created)
    {
        var upper = m.ToUpperInvariant();
        var partName = m == "m1" ? "XR_M1_Quest_Block.ipt" : "XR_M2_Quest_Acceptance_Block.ipt";
        var assemblyName = "XR_" + upper + "_Quest_Acceptance.iam";
        var part = (PartDocument)app.Documents.Add(DocumentTypeEnum.kPartDocumentObject);
        created.Add(part);
        CreateBlock(app, part, 100, 60, 20, false, "Blocco");
        var partPath = Path.Combine(directory, partName);
        part.SaveAs(partPath, false);
        if (m == "m1")
        {
            part.Close(true);
            created.Remove(part);
        }
        var assembly = (AssemblyDocument)app.Documents.Add(DocumentTypeEnum.kAssemblyDocumentObject);
        created.Add(assembly);
        var tg = app.TransientGeometry;
        var first = assembly.ComponentDefinition.Occurrences.Add(partPath, tg.CreateMatrix());
        first.Grounded = true;
        var pose = tg.CreateMatrix();
        pose.SetTranslation(tg.CreateVector(15, 0, 0));
        var second = assembly.ComponentDefinition.Occurrences.Add(partPath, pose);
        second.Grounded = false;
        var assemblyPath = Path.Combine(directory, assemblyName);
        assembly.SaveAs(assemblyPath, false);
        var documents = new JArray(assemblyPath, partPath);
        var expected = new JObject { ["occurrences"] = 2, ["occurrence_centers_distance_mm"] = 150 };
        if (m == "m1") { expected["block_long_side_mm"] = 100; expected["gap_mm"] = 50; }
        else
        {
            var opened = app.Documents.Open(partPath, true);
            created.Add(opened);
            expected["volume_mm3"] = 120000; expected["fixture_documents"] = 2;
        }
        assembly.Activate();
        return new JObject { ["assembly"] = assemblyPath, ["part"] = partPath, ["documents"] = documents, ["expected"] = expected };
    }

    private static Face? TopPlanarFace(dynamic def, out Box? range)
    {
        Face? best = null; range = null; var bestZ = double.MinValue;
        foreach (Face face in def.SurfaceBodies[1].Faces)
        {
            if (face.SurfaceType != SurfaceTypeEnum.kPlaneSurface) continue;
            var box = face.Evaluator.RangeBox;
            var flat = Math.Abs(box.MaxPoint.Z - box.MinPoint.Z) < 1e-6;
            var normal = ((Plane)face.Geometry).Normal;
            if (!flat || Math.Abs(normal.Z) < 0.999) continue;
            if (box.MaxPoint.Z > bestZ) { bestZ = box.MaxPoint.Z; best = face; range = box; }
        }
        return best;
    }

    private static PlanarSketch SketchOnTopFace(global::Inventor.Application app, dynamic def, string name,
        Action<PlanarSketch, Point2d> draw)
    {
        Box? box;
        var face = TopPlanarFace(def, out box) ?? throw new InvalidOperationException("Top planar face not found.");
        var sketch = def.Sketches.Add(face);
        sketch.Name = name;
        var centre = app.TransientGeometry.CreatePoint((box!.MinPoint.X + box.MaxPoint.X) / 2,
            (box.MinPoint.Y + box.MaxPoint.Y) / 2, box.MaxPoint.Z);
        draw(sketch, sketch.ModelToSketchSpace(centre));
        return sketch;
    }

    private static JObject PrepareM3(global::Inventor.Application app, string directory, List<object> created)
    {
        var part = (PartDocument)app.Documents.Add(DocumentTypeEnum.kPartDocumentObject);
        created.Add(part);
        CreateBlock(app, part, 40, 30, 10, true, "Blocco");
        var def = part.ComponentDefinition;
        var top = TopPlanarFace(def, out var box);
        if (top == null || Math.Abs(box!.MaxPoint.Z - 1) > 1e-3)
            throw new InvalidOperationException("The +Z face at z = 10 mm was not found.");
        SketchOnTopFace(app, def, "Base_M3", (sketch, c) => sketch.SketchCircles.AddByCenterRadius(c, 0.5));
        var path = Path.Combine(directory, "XR_M3_Quest_Acceptance.ipt");
        part.SaveAs(path, false);
        var expected = new JObject
        {
            ["volume_mm3"] = 12000, ["base_sketch"] = "Base_M3", ["base_circle_radius_mm"] = 5,
            ["extrude_join_20mm_volume_mm3"] = 12000 + Math.PI * 25 * 20,
        };
        return new JObject { ["part"] = path, ["documents"] = new JArray(path), ["expected"] = expected };
    }

    /// <summary>Sheet-metal part: a 100 x 60 mm Face (sketch <paramref name="baseName"/>) and an unconsumed 20 x 10 mm cut sketch on its top face.</summary>
    private static PartDocument BuildSheetMetalPart(global::Inventor.Application app, string path, List<object> created,
        string baseName, string cutName)
    {
        var template = app.FileManager.GetTemplateFile(DocumentTypeEnum.kPartDocumentObject, SystemOfMeasureEnum.kDefaultSystemOfMeasure,
            DraftingStandardEnum.kDefault_DraftingStandard, SheetMetalSubType);
        var part = (PartDocument)app.Documents.Add(DocumentTypeEnum.kPartDocumentObject, template, true);
        created.Add(part);
        if (part.ComponentDefinition is not SheetMetalComponentDefinition def)
            throw new InvalidOperationException("The sheet-metal template did not produce a sheet-metal part.");
        var tg = app.TransientGeometry;
        var baseSketch = def.Sketches.Add(def.WorkPlanes[3]);
        baseSketch.Name = baseName;
        baseSketch.SketchLines.AddAsTwoPointRectangle(tg.CreatePoint2d(0, 0), tg.CreatePoint2d(10, 6));
        var faces = ((SheetMetalFeatures)def.Features).FaceFeatures;
        faces.Add(faces.CreateFaceFeatureDefinition(baseSketch.Profiles.AddForSolid()));
        SketchOnTopFace(app, def, cutName, (sketch, c) =>
            sketch.SketchLines.AddAsTwoPointRectangle(tg.CreatePoint2d(c.X - 1, c.Y - 0.5), tg.CreatePoint2d(c.X + 1, c.Y + 0.5)));
        part.SaveAs(path, false);
        return part;
    }

    private static JObject PrepareM5(global::Inventor.Application app, string directory, List<object> created)
    {
        var path = Path.Combine(directory, "XR_M5_Quest_Acceptance.ipt");
        var part = BuildSheetMetalPart(app, path, created, "Base_M5", "Taglio_M5");
        var def = (SheetMetalComponentDefinition)part.ComponentDefinition;
        var expected = new JObject
        {
            ["is_sheet_metal"] = true, ["thickness_mm"] = ThicknessMm(def), ["bends"] = 0,
            ["has_flat_pattern"] = false, ["cut_sketch"] = "Taglio_M5",
        };
        return new JObject { ["part"] = path, ["documents"] = new JArray(path), ["expected"] = expected };
    }

    /// <summary>
    /// M6: one assembly with two components, both parts kept open so "Apri in Progettazione / Lamiera" can activate them.
    /// The block (40 x 30 x 10 mm, as m3, unconsumed sketch Base_M6) serves Progettazione, the sheet (as m5, unconsumed sketch
    /// Taglio_M6) serves Lamiera, the assembly serves Ispeziona and Assieme. Every document name starts with XR_M6_Quest_Acceptance.
    /// </summary>
    private static JObject PrepareM6(global::Inventor.Application app, string directory, List<object> created)
    {
        var block = (PartDocument)app.Documents.Add(DocumentTypeEnum.kPartDocumentObject);
        created.Add(block);
        CreateBlock(app, block, 40, 30, 10, true, "Blocco");
        var blockDef = block.ComponentDefinition;
        var top = TopPlanarFace(blockDef, out var box);
        if (top == null || Math.Abs(box!.MaxPoint.Z - 1) > 1e-3)
            throw new InvalidOperationException("The +Z face at z = 10 mm was not found.");
        SketchOnTopFace(app, blockDef, "Base_M6", (sketch, c) => sketch.SketchCircles.AddByCenterRadius(c, 0.5));
        var blockPath = Path.Combine(directory, "XR_M6_Quest_Acceptance_Block.ipt");
        block.SaveAs(blockPath, false);

        var sheetPath = Path.Combine(directory, "XR_M6_Quest_Acceptance_Sheet.ipt");
        var sheet = BuildSheetMetalPart(app, sheetPath, created, "Base_M6", "Taglio_M6");

        var assembly = (AssemblyDocument)app.Documents.Add(DocumentTypeEnum.kAssemblyDocumentObject);
        created.Add(assembly);
        var tg = app.TransientGeometry;
        var first = assembly.ComponentDefinition.Occurrences.Add(blockPath, tg.CreateMatrix());
        first.Grounded = true;
        var pose = tg.CreateMatrix();
        pose.SetTranslation(tg.CreateVector(6, 0, 0));   // cm: the sheet (0..100 mm in X) sits beside the block (-20..20 mm)
        var second = assembly.ComponentDefinition.Occurrences.Add(sheetPath, pose);
        second.Grounded = false;
        var assemblyPath = Path.Combine(directory, "XR_M6_Quest_Acceptance.iam");
        assembly.SaveAs(assemblyPath, false);
        assembly.Activate();
        var expected = new JObject
        {
            ["occurrences"] = 2, ["block_volume_mm3"] = 12000, ["block_sketch"] = "Base_M6", ["sheet_cut_sketch"] = "Taglio_M6",
            ["sheet_thickness_mm"] = ThicknessMm((SheetMetalComponentDefinition)sheet.ComponentDefinition),
            ["fixture_documents"] = 3,
        };
        return new JObject
        {
            ["assembly"] = assemblyPath, ["block_part"] = blockPath, ["sheet_part"] = sheetPath,
            ["documents"] = new JArray(assemblyPath, blockPath, sheetPath), ["expected"] = expected,
        };
    }

    /// <summary>
    /// M7: four 20 mm cubes of one part (blank part number). M7_A grounded at the origin; M7_B grounded and overlapping M7_A by
    /// 5 mm in X (2000 mm3); M7_C grounded 30 mm from M7_A along Y; M7_D free and unconstrained at X = 100 mm. M7_Sick is a flush
    /// constraint between the M7_Ref work planes of the two grounded cubes M7_B and M7_C with a 5 mm offset they cannot satisfy.
    /// </summary>
    private static JObject PrepareM7(global::Inventor.Application app, string directory, List<object> created)
    {
        var cube = (PartDocument)app.Documents.Add(DocumentTypeEnum.kPartDocumentObject);
        created.Add(cube);
        CreateBlock(app, cube, 20, 20, 20, false, "Cubo");   // x, y in [0, 20] mm, z in [0, 20] mm
        var def = cube.ComponentDefinition;
        var reference = def.WorkPlanes.AddByPlaneAndOffset(def.WorkPlanes[3], 1.0, false);   // z = 10 mm
        reference.Name = "M7_Ref";
        cube.PropertySets["Design Tracking Properties"]["Part Number"].Value = "";
        var cubePath = Path.Combine(directory, "XR_M7_Quest_Acceptance_Cube.ipt");
        cube.SaveAs(cubePath, false);

        var assembly = (AssemblyDocument)app.Documents.Add(DocumentTypeEnum.kAssemblyDocumentObject);
        created.Add(assembly);
        var tg = app.TransientGeometry;
        var occurrences = assembly.ComponentDefinition.Occurrences;
        ComponentOccurrence Place(string name, double xCm, double yCm, bool grounded)
        {
            var pose = tg.CreateMatrix();
            pose.SetTranslation(tg.CreateVector(xCm, yCm, 0));
            var occurrence = occurrences.Add(cubePath, pose);
            occurrence.Name = name;
            occurrence.Grounded = grounded;
            return occurrence;
        }
        Place("M7_A", 0, 0, true);
        var b = Place("M7_B", 1.5, 0, true);
        var c = Place("M7_C", 0, 5, true);
        Place("M7_D", 10, 0, false);

        object RefProxy(ComponentOccurrence occurrence)
        {
            occurrence.CreateGeometryProxy(((PartComponentDefinition)occurrence.Definition).WorkPlanes["M7_Ref"], out object proxy);
            return proxy;
        }
        var constraints = assembly.ComponentDefinition.Constraints;
        var flush = constraints.AddFlushConstraint(RefProxy(b), RefProxy(c), 0.5);
        flush.Name = "M7_Sick";
        assembly.Update();
        if (flush.HealthStatus == HealthStatusEnum.kUpToDateHealth)
            throw new InvalidOperationException("M7_Sick is healthy: the fixture needs a failing constraint. Record this in the probe log.");

        var assemblyPath = Path.Combine(directory, "XR_M7_Quest_Acceptance.iam");
        assembly.SaveAs(assemblyPath, false);
        assembly.Activate();
        var expected = new JObject
        {
            ["occurrences"] = 4, ["interference_pairs"] = 1, ["interference_volume_mm3"] = 2000,
            ["distance_a_c_mm"] = 30, ["unconstrained"] = new JArray("M7_D"), ["failing_constraint"] = "M7_Sick",
            ["bom_finding"] = "PART_NUMBER_MISSING", ["fixture_documents"] = 2,
        };
        return new JObject
        {
            ["assembly"] = assemblyPath, ["part"] = cubePath, ["documents"] = new JArray(assemblyPath, cubePath), ["expected"] = expected,
        };
    }

    private static double? ThicknessMm(SheetMetalComponentDefinition def)
    {
        try { return Convert.ToDouble(((dynamic)def).Thickness.Value) * 10; } catch { return null; }
    }

    // ---------------------------------------------------------------- inspect

    private static JObject LoadManifest(string m)
    {
        var path = ManifestPath(m);
        if (!System.IO.File.Exists(path)) throw new FileNotFoundException("Fixture manifest missing; run --prepare-quest " + m + ".", path);
        return JObject.Parse(System.IO.File.ReadAllText(path));
    }

    private static Document RequireActive(global::Inventor.Application app, JObject manifest, string m)
    {
        var expected = (string?)manifest["active_document"] ?? throw new InvalidDataException("active_document missing in manifest.");
        var active = app.ActiveDocument;
        if (active == null || !SamePath(active.FullFileName, expected))
            throw new InvalidOperationException("The " + m + " Quest fixture is not the active document; refusing to continue.");
        return active;
    }

    private static double VolumeMm3(PartComponentDefinition def) => def.MassProperties.Volume * 1000;

    internal static int Inspect(global::Inventor.Application app, string m)
    {
        var manifest = LoadManifest(m);
        var active = RequireActive(app, manifest, m);
        var result = new JObject { ["milestone"] = m, ["document"] = active.FullFileName, ["dirty"] = active.Dirty };
        if (active is AssemblyDocument assembly)
        {
            var occurrences = assembly.ComponentDefinition.Occurrences.Cast<ComponentOccurrence>().ToArray();
            var list = new JArray();
            foreach (var occ in occurrences)
                list.Add(new JObject
                {
                    ["name"] = occ.Name,
                    ["x_mm"] = occ.Transformation.Cell[1, 4] * 10, ["y_mm"] = occ.Transformation.Cell[2, 4] * 10,
                    ["z_mm"] = occ.Transformation.Cell[3, 4] * 10, ["grounded"] = occ.Grounded,
                });
            result["occurrences"] = list;
            if (occurrences.Length == 2)
            {
                double Axis(int row) => (occurrences[1].Transformation.Cell[row, 4] - occurrences[0].Transformation.Cell[row, 4]) * 10;
                result["occurrence_centers_distance_mm"] = Math.Sqrt(Axis(1) * Axis(1) + Axis(2) * Axis(2) + Axis(3) * Axis(3));
            }
            if (occurrences.Length > 0 && occurrences[0].Definition is PartComponentDefinition partDef)
                result["volume_mm3"] = VolumeMm3(partDef);
            result["fixture_documents"] = ManifestDocuments(manifest).Count(p => FindOpen(app, p) != null);
            if (m == "m7") ProbeM7.AddInspection(app, assembly, result);
        }
        else if (active is PartDocument part)
        {
            var def = part.ComponentDefinition;
            result["volume_mm3"] = VolumeMm3(def);
            result["sketches"] = new JArray(def.Sketches.Cast<PlanarSketch>().Select(s => (JToken)s.Name));
            if (m == "m3")
            {
                var sketch = def.Sketches.Cast<PlanarSketch>().FirstOrDefault(s => s.Name == "Base_M3");
                result["base_sketch"] = sketch?.Name;
                if (sketch != null && sketch.SketchCircles.Count > 0)
                    result["base_circle_radius_mm"] = sketch.SketchCircles[1].Radius * 10;
            }
            if (m == "m5")
            {
                var sheet = def as SheetMetalComponentDefinition;
                result["is_sheet_metal"] = sheet != null;
                if (sheet != null)
                {
                    result["thickness_mm"] = ThicknessMm(sheet);
                    try { result["bends"] = sheet.Bends.Count; } catch { result["bends"] = null; }
                    result["has_flat_pattern"] = sheet.HasFlatPattern;
                }
                result["cut_sketch"] = def.Sketches.Cast<PlanarSketch>().FirstOrDefault(s => s.Name == "Taglio_M5")?.Name;
            }
        }
        Console.WriteLine(result.ToString(Newtonsoft.Json.Formatting.None));
        return 0;
    }

    // ---------------------------------------------------------------- restore

    internal static int Restore(global::Inventor.Application app, string m)
    {
        var manifest = LoadManifest(m);
        RequireActive(app, manifest, m);
        var previousPath = (string?)manifest["previous_document"];
        var previous = FindOpen(app, previousPath);
        if (previousPath != null && previous == null)
            throw new InvalidOperationException("The previous Inventor document is no longer open: " + previousPath);
        // Assembly first, so the parts are no longer referenced when they are closed.
        var ordered = ManifestDocuments(manifest).OrderBy(p => p.EndsWith(".iam", StringComparison.OrdinalIgnoreCase) ? 0 : 1);
        foreach (var path in ordered)
        {
            var doc = FindOpen(app, path);
            if (doc == null) { Console.WriteLine("Already closed: " + path); continue; }
            doc.Close(true);
            Console.WriteLine("Closed without saving: " + path);
        }
        previous?.Activate();
        Console.WriteLine("Quest fixture " + m + " closed; previous document " + (previousPath ?? "(none)") + (previous != null ? " reactivated." : "."));
        return 0;
    }
}
