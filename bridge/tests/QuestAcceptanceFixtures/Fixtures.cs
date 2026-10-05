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
                "m9n" => PrepareM9Nested(app, directory, created),
                "m9f" => PrepareM9Flex(app, directory, created, hiddenDefinitions: false),
                "m9h" => PrepareM9Flex(app, directory, created, hiddenDefinitions: true),
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
    /// M9 nested: three assemblies. Assieme1 holds PartA (40 x 30 x 10 mm centred block, sketch Blocco extruded 10 mm, as the M6 block)
    /// and PartC (30 x 20 x 10 mm, 60 mm in X, so the ghost of Assieme1 is not empty when PartA is entered), Assieme2 holds PartB
    /// (20 x 20 x 10 mm centred block). Assieme3 holds Assieme1 (grounded at the origin) and Assieme2 (grounded,
    /// 120 mm in X) so rays reach both. Every document is saved on disk and kept open (definitions can be activated); every name
    /// starts with XR_M9N_Quest_Acceptance, the guard of the nested M9 runner.
    /// </summary>
    private static JObject PrepareM9Nested(global::Inventor.Application app, string directory, List<object> created)
    {
        const string prefix = "XR_M9N_Quest_Acceptance_";
        var partA = (PartDocument)app.Documents.Add(DocumentTypeEnum.kPartDocumentObject);
        created.Add(partA);
        CreateBlock(app, partA, 40, 30, 10, true, "Blocco");
        var partAPath = Path.Combine(directory, prefix + "PartA.ipt");
        partA.SaveAs(partAPath, false);
        var partB = (PartDocument)app.Documents.Add(DocumentTypeEnum.kPartDocumentObject);
        created.Add(partB);
        CreateBlock(app, partB, 20, 20, 10, true, "Blocco");
        var partBPath = Path.Combine(directory, prefix + "PartB.ipt");
        partB.SaveAs(partBPath, false);

        var partC = (PartDocument)app.Documents.Add(DocumentTypeEnum.kPartDocumentObject);
        created.Add(partC);
        CreateBlock(app, partC, 30, 20, 10, true, "Blocco");
        var partCPath = Path.Combine(directory, prefix + "PartC.ipt");
        partC.SaveAs(partCPath, false);

        var tg = app.TransientGeometry;
        AssemblyDocument BuildAssembly(string fileName, params (string path, double xCm)[] items)
        {
            var doc = (AssemblyDocument)app.Documents.Add(DocumentTypeEnum.kAssemblyDocumentObject);
            created.Add(doc);
            foreach (var (path, xCm) in items)
            {
                var pose = tg.CreateMatrix();
                pose.SetTranslation(tg.CreateVector(xCm, 0, 0));
                var occurrence = doc.ComponentDefinition.Occurrences.Add(path, pose);
                occurrence.Grounded = true;
            }
            doc.SaveAs(Path.Combine(directory, fileName), false);
            return doc;
        }
        var assembly1 = BuildAssembly(prefix + "Assieme1.iam", (partAPath, 0), (partCPath, 6));
        var assembly2 = BuildAssembly(prefix + "Assieme2.iam", (partBPath, 0));
        var assembly1Path = assembly1.FullFileName;
        var assembly2Path = assembly2.FullFileName;
        var assembly3 = BuildAssembly(prefix + "Assieme3.iam", (assembly1Path, 0), (assembly2Path, 12));
        var assembly3Path = assembly3.FullFileName;
        assembly3.Activate();
        var expected = new JObject
        {
            ["occurrences"] = 2, ["part_a_volume_mm3"] = 12000, ["part_b_volume_mm3"] = 4000, ["extrude_mm"] = 10,
            ["sub_assemblies"] = new JArray("Assieme1", "Assieme2"), ["assieme1_parts"] = new JArray("PartA", "PartC"), ["assieme2_parts"] = new JArray("PartB"), ["fixture_documents"] = 6,
        };
        return new JObject
        {
            ["assembly"] = assembly3Path, ["assembly1"] = assembly1Path, ["assembly2"] = assembly2Path,
            ["part_a"] = partAPath, ["part_b"] = partBPath, ["part_c"] = partCPath,
            ["documents"] = new JArray(assembly3Path, assembly1Path, assembly2Path, partAPath, partBPath, partCPath), ["expected"] = expected,
        };
    }

    /// <summary>
    /// M9 flex: the structure of the user's robot at small scale. Top assembly Robot holds PartL1 (loose, ungrounded, -80 mm in X),
    /// AsmFixed (grounded, NOT flexible, at the origin: a normal sub-assembly AsmInner with PartI, plus PartF at +60 mm) and AsmFlex
    /// (UNGROUNDED, ComponentOccurrence.Flexible = True, 5 m away at z = -5000 mm: a FLEXIBLE sub-assembly AsmFlexInner, also Flexible and
    /// ungrounded, with PartX (40 x 30 x 10 mm, as m9n PartA, so the feature edit can be repeated) and PartY, plus PartG at +60 mm).
    /// Flexible is settable only on assembly occurrences: it is set after placing and READ BACK; the preparation fails when it did not
    /// stick (the runner depends on it). No assembly constraint is added to AsmFlex: a flush or mate to a work plane proxy of a flexible
    /// sub-assembly cannot be verified without a live Inventor and its sign/offset could move the 5 m placement, so AsmFlex is simply
    /// ungrounded and unconstrained. Every document is saved and kept open (definitions can be activated); every name starts with
    /// XR_M9F_Quest_Acceptance, the guard of the flex M9 runner.
    /// </summary>
    /// <remarks>
    /// With <paramref name="hiddenDefinitions"/> (fixture m9h, guard prefix XR_M9H_Quest_Acceptance) the same structure is built and saved,
    /// then EVERYTHING is closed and reloaded the way the user's real assembly looks: every definition with Documents.Open(path, false)
    /// (loaded, no window) and only the top assembly Robot with Documents.Open(path, true). The preparation reads Documents.VisibleDocuments
    /// back and fails when a definition still has a window (the scenario depends on it).
    /// </remarks>
    private static JObject PrepareM9Flex(global::Inventor.Application app, string directory, List<object> created, bool hiddenDefinitions)
    {
        string prefix = hiddenDefinitions ? "XR_M9H_Quest_Acceptance_" : "XR_M9F_Quest_Acceptance_";
        string SavePart(string name, double w, double d, double h)
        {
            var part = (PartDocument)app.Documents.Add(DocumentTypeEnum.kPartDocumentObject);
            created.Add(part);
            CreateBlock(app, part, w, d, h, true, "Blocco");
            var path = Path.Combine(directory, prefix + name + ".ipt");
            part.SaveAs(path, false);
            return path;
        }
        var partL1 = SavePart("PartL1", 30, 30, 10);
        var partI = SavePart("PartI", 40, 30, 10);
        var partF = SavePart("PartF", 30, 20, 10);
        var partX = SavePart("PartX", 40, 30, 10);
        var partY = SavePart("PartY", 30, 20, 10);
        var partG = SavePart("PartG", 20, 20, 10);

        var tg = app.TransientGeometry;
        ComponentOccurrence Place(AssemblyDocument doc, string path, double xCm, double zCm, bool grounded)
        {
            var pose = tg.CreateMatrix();
            pose.SetTranslation(tg.CreateVector(xCm, 0, zCm));
            var occurrence = doc.ComponentDefinition.Occurrences.Add(path, pose);
            occurrence.Grounded = grounded;
            return occurrence;
        }
        AssemblyDocument NewAssembly(string name)
        {
            var doc = (AssemblyDocument)app.Documents.Add(DocumentTypeEnum.kAssemblyDocumentObject);
            created.Add(doc);
            doc.SaveAs(Path.Combine(directory, prefix + name + ".iam"), false);
            return doc;
        }
        void MakeFlexible(ComponentOccurrence occurrence, string what)
        {
            occurrence.Flexible = true;
            if (!occurrence.Flexible) throw new InvalidOperationException(what + ": ComponentOccurrence.Flexible did not stick after setting it to True.");
        }

        // Normal sub-assembly of the fixed branch.
        var asmInner = NewAssembly("AsmInner");
        Place(asmInner, partI, 0, 0, true);
        asmInner.Save();
        // Fixed branch: AsmInner (normal) and PartF.
        var asmFixed = NewAssembly("AsmFixed");
        var innerOccurrence = Place(asmFixed, asmInner.FullFileName, 0, 0, true);
        Place(asmFixed, partF, 6, 0, true);
        if (innerOccurrence.Flexible) throw new InvalidOperationException("AsmInner must stay a normal (non flexible) sub-assembly.");
        asmFixed.Save();
        // Flexible branch: AsmFlexInner (flexible, ungrounded) with PartX and PartY, ungrounded as well.
        var asmFlexInner = NewAssembly("AsmFlexInner");
        Place(asmFlexInner, partX, 0, 0, false);
        Place(asmFlexInner, partY, 6, 0, false);
        asmFlexInner.Save();
        var asmFlex = NewAssembly("AsmFlex");
        var flexInnerOccurrence = Place(asmFlex, asmFlexInner.FullFileName, 0, 0, false);
        MakeFlexible(flexInnerOccurrence, "AsmFlexInner inside AsmFlex");
        Place(asmFlex, partG, 6, 0, false);
        asmFlex.Save();
        // Top assembly.
        var robot = NewAssembly("Robot");
        Place(robot, partL1, -8, 0, false);
        Place(robot, asmFixed.FullFileName, 0, 0, true);
        var flexOccurrence = Place(robot, asmFlex.FullFileName, 0, -500, false);   // cm: 5 m from AsmFixed
        MakeFlexible(flexOccurrence, "AsmFlex inside Robot");
        robot.Save();
        var robotPath = robot.FullFileName;
        var documents = new JArray(robotPath, asmFixed.FullFileName, asmInner.FullFileName, asmFlex.FullFileName, asmFlexInner.FullFileName,
            partL1, partI, partF, partX, partY, partG);
        if (hiddenDefinitions) ReopenDefinitionsWithoutWindow(app, created, documents, robotPath);
        else robot.Activate();
        var expected = new JObject
        {
            ["occurrences"] = 3, ["root_occurrences"] = new JArray("PartL1", "AsmFixed", "AsmFlex"),
            ["asm_fixed_grounded"] = true, ["asm_fixed_flexible"] = false, ["asm_flex_grounded"] = false, ["asm_flex_flexible"] = true,
            ["asm_flex_z_mm"] = -5000, ["asm_flex_inner_flexible"] = true, ["asm_flex_inner_grounded"] = false,
            ["asm_fixed_children"] = new JArray("AsmInner", "PartF"), ["asm_flex_children"] = new JArray("AsmFlexInner", "PartG"),
            ["asm_flex_inner_children"] = new JArray("PartX", "PartY"), ["part_x_volume_mm3"] = 12000, ["extrude_mm"] = 10,
            ["asm_flex_constraints"] = 0, ["fixture_documents"] = documents.Count,
        };
        return new JObject
        {
            ["assembly"] = robotPath, ["asm_fixed"] = asmFixed.FullFileName, ["asm_inner"] = asmInner.FullFileName,
            ["asm_flex"] = asmFlex.FullFileName, ["asm_flex_inner"] = asmFlexInner.FullFileName,
            ["part_l1"] = partL1, ["part_i"] = partI, ["part_f"] = partF, ["part_x"] = partX, ["part_y"] = partY, ["part_g"] = partG,
            ["documents"] = documents, ["expected"] = expected,
        };
    }

    /// <summary>
    /// m9h: closes every saved fixture document (top assembly first) and loads them again: definitions with OpenVisible = false (no window),
    /// the top assembly with OpenVisible = true and activated. Fails when a definition ends up with a window or the top has none.
    /// </summary>
    private static void ReopenDefinitionsWithoutWindow(global::Inventor.Application app, List<object> created, JArray documents, string robotPath)
    {
        var paths = documents.Select(t => (string)t!).ToArray();
        // Top first, then the other assemblies, then the parts: nothing is closed while an open document still owns it.
        foreach (var path in paths.OrderBy(p => SamePath(p, robotPath) ? 0 : p.EndsWith(".iam", StringComparison.OrdinalIgnoreCase) ? 1 : 2))
        {
            var doc = FindOpen(app, path);
            if (doc != null) doc.Close(true);   // everything was saved: nothing is discarded
        }
        created.Clear();
        foreach (var path in paths.Where(p => !SamePath(p, robotPath)))
            created.Add(app.Documents.Open(path, false));
        var robot = app.Documents.Open(robotPath, true);
        created.Add(robot);
        robot.Activate();
        var visible = VisiblePaths(app);
        var withWindow = paths.Where(p => !SamePath(p, robotPath) && visible.Contains(p, StringComparer.OrdinalIgnoreCase)).ToArray();
        if (withWindow.Length > 0)
            throw new InvalidOperationException("m9h: definitions still have a window after Documents.Open(path, false): " + string.Join(", ", withWindow.Select(Path.GetFileName)));
        if (!visible.Contains(robotPath, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("m9h: the top assembly Robot has no window after Documents.Open(path, true).");
        if (paths.Any(p => FindOpen(app, p) == null))
            throw new InvalidOperationException("m9h: a fixture document is not loaded after reopening it.");
    }

    /// <summary>Full file names of the documents that have a window (Documents.VisibleDocuments).</summary>
    private static string[] VisiblePaths(global::Inventor.Application app) =>
        app.Documents.VisibleDocuments.Cast<Document>().Select(d => d.FullFileName).ToArray();

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
        cube.PropertySets["Design Tracking Properties"]["Description"].Value = "";   // get_assembly_bom reports a blank part number as the file name: the BOM finding is a missing description
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
            ["bom_finding"] = "DESCRIPTION_MISSING", ["fixture_documents"] = 2,
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
            if (m == "m9n" || m == "m9f" || m == "m9h") AddNestedInspection(app, manifest, result);
            if (m == "m9f" || m == "m9h") AddFlexInspection(assembly, result);
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

    /// <summary>m9n: dirty flag and (for parts) volume of every fixture document, so a run can be checked from outside Inventor.</summary>
    private static void AddNestedInspection(global::Inventor.Application app, JObject manifest, JObject result)
    {
        var documents = new JArray();
        var visible = VisiblePaths(app);
        result["visible_documents"] = visible.Length;
        foreach (var path in ManifestDocuments(manifest))
        {
            var doc = FindOpen(app, path);
            // has_window: the document is in Documents.VisibleDocuments (a loaded document without a window cannot be activated until it is shown).
            var item = new JObject { ["name"] = Path.GetFileName(path), ["open"] = doc != null, ["has_window"] = doc != null && visible.Contains(path, StringComparer.OrdinalIgnoreCase), ["dirty"] = doc?.Dirty };
            if (doc is PartDocument part) item["volume_mm3"] = VolumeMm3(part.ComponentDefinition);
            documents.Add(item);
        }
        result["documents_state"] = documents;
    }

    /// <summary>
    /// m9f: Grounded and Flexible read back from Inventor for the top assembly's direct occurrences and for the sub-assemblies' own
    /// occurrences (read from the open definitions), the number of assembly constraints of each assembly and the z position in mm.
    /// </summary>
    private static void AddFlexInspection(AssemblyDocument robot, JObject result)
    {
        JObject Describe(ComponentOccurrence occurrence)
        {
            var item = new JObject
            {
                ["name"] = occurrence.Name, ["grounded"] = occurrence.Grounded, ["z_mm"] = occurrence.Transformation.Cell[3, 4] * 10,
                ["is_assembly"] = occurrence.DefinitionDocumentType == DocumentTypeEnum.kAssemblyDocumentObject,
            };
            // Flexible exists on assembly occurrences only.
            if (occurrence.DefinitionDocumentType == DocumentTypeEnum.kAssemblyDocumentObject)
            {
                try { item["flexible"] = occurrence.Flexible; } catch (Exception ex) { item["flexible_error"] = ex.Message; }
                try
                {
                    var definition = (AssemblyComponentDefinition)occurrence.Definition;
                    item["constraints"] = definition.Constraints.Count;
                    item["children"] = new JArray(definition.Occurrences.Cast<ComponentOccurrence>().Select(Describe));
                }
                catch (Exception ex) { item["children_error"] = ex.Message; }
            }
            return item;
        }
        var top = robot.ComponentDefinition;
        result["robot_constraints"] = top.Constraints.Count;
        result["flex_tree"] = new JArray(top.Occurrences.Cast<ComponentOccurrence>().Select(Describe));
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
        var assemblyPath = (string?)manifest["assembly"];
        var ordered = ManifestDocuments(manifest).OrderBy(p => p.EndsWith(".iam", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(p => SamePath(p, assemblyPath) ? 0 : 1);
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
