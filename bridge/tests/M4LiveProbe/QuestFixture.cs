using Inventor;
using Newtonsoft.Json.Linq;
using Path = System.IO.Path;

internal static class QuestFixture
{
    internal static int Inventory(global::Inventor.Application app)
    {
        foreach (var document in app.Documents.Cast<Document>())
            Console.WriteLine(new JObject { ["name"] = document.DisplayName,
                ["path"] = document.FullFileName, ["dirty"] = document.Dirty }.ToString(Newtonsoft.Json.Formatting.None));
        return 0;
    }

    internal static int Show(global::Inventor.Application app)
    {
        app.Visible = true;
        Console.WriteLine("Inventor visible with active document: " + app.ActiveDocument?.DisplayName);
        return 0;
    }

    internal static int CloseBatchPart(global::Inventor.Application app)
    {
        var path = Path.Combine(System.Environment.CurrentDirectory, "artifacts", "m4-verification", "quest-fixture-batch.json");
        var fixture = JObject.Parse(System.IO.File.ReadAllText(path));
        var expected = (string?)fixture["part"] ?? throw new InvalidDataException("Batch fixture part missing.");
        var document = app.Documents.Cast<Document>().SingleOrDefault(item =>
            string.Equals(item.FullFileName, expected, StringComparison.OrdinalIgnoreCase));
        if (document == null) { Console.WriteLine("Batch part already closed."); return 0; }
        if (document.Dirty || ReferenceEquals(app.ActiveDocument, document))
            throw new InvalidOperationException("Batch part has unsaved changes or is active; refusing to close.");
        document.Close(true);
        Console.WriteLine("Closed saved batch-only part: " + expected);
        return 0;
    }

    internal static int Save(global::Inventor.Application app)
    {
        var path = Path.Combine(System.Environment.CurrentDirectory, "artifacts", "m4-verification", "quest-fixture.json");
        var fixture = JObject.Parse(System.IO.File.ReadAllText(path));
        var expected = (string?)fixture["assembly"] ?? throw new InvalidDataException("Fixture path missing.");
        if (app.ActiveDocument is not AssemblyDocument assembly ||
            !string.Equals(assembly.FullFileName, expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The dedicated Quest fixture is not active; refusing to save another document.");
        assembly.Save();
        Console.WriteLine("Saved dedicated Quest fixture: " + expected);
        return Inspect(app);
    }

    internal static int Open(global::Inventor.Application app)
    {
        var path = Path.Combine(System.Environment.CurrentDirectory, "artifacts", "m4-verification", "quest-fixture.json");
        var fixture = JObject.Parse(System.IO.File.ReadAllText(path));
        var expected = (string?)fixture["assembly"] ?? throw new InvalidDataException("Fixture path missing.");
        if (!System.IO.File.Exists(expected)) throw new FileNotFoundException("Quest fixture missing.", expected);
        var opened = app.Documents.Cast<Document>().FirstOrDefault(document =>
            string.Equals(document.FullFileName, expected, StringComparison.OrdinalIgnoreCase));
        if (opened != null) opened.Activate(); else app.Documents.Open(expected, true);
        return Inspect(app);
    }

    internal static int QuitSaved(global::Inventor.Application app)
    {
        var path = Path.Combine(System.Environment.CurrentDirectory, "artifacts", "m4-verification", "quest-fixture.json");
        var fixture = JObject.Parse(System.IO.File.ReadAllText(path));
        var assemblyPath = (string?)fixture["assembly"] ?? throw new InvalidDataException("Fixture path missing.");
        var partPath = (string?)fixture["part"] ?? throw new InvalidDataException("Fixture part missing.");
        if (app.ActiveDocument is not AssemblyDocument active ||
            !string.Equals(active.FullFileName, assemblyPath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The saved Quest fixture is not active; refusing to quit Inventor.");
        var open = app.Documents.Cast<Document>().ToArray();
        if (open.Any(document => document.Dirty ||
                (!string.Equals(document.FullFileName, assemblyPath, StringComparison.OrdinalIgnoreCase) &&
                 !string.Equals(document.FullFileName, partPath, StringComparison.OrdinalIgnoreCase))))
            throw new InvalidOperationException("Inventor has other or unsaved documents; refusing to quit.");
        Console.WriteLine("Quitting Inventor with only " + open.Length + " saved dedicated fixture document(s).");
        app.Quit();
        return 0;
    }

    internal static int Restore(global::Inventor.Application app)
    {
        var path = Path.Combine(System.Environment.CurrentDirectory, "artifacts", "m4-verification", "quest-fixture.json");
        var fixture = JObject.Parse(System.IO.File.ReadAllText(path));
        var assemblyPath = (string?)fixture["assembly"] ?? throw new InvalidDataException("Fixture path missing.");
        var partPath = (string?)fixture["part"] ?? throw new InvalidDataException("Fixture part path missing.");
        var previousPath = (string?)fixture["previous_document"];
        if (app.ActiveDocument is not AssemblyDocument active ||
            !string.Equals(active.FullFileName, assemblyPath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The batch Quest fixture is not active; refusing to close another document.");
        var previous = app.Documents.Cast<Document>().FirstOrDefault(document =>
            string.Equals(document.FullFileName, previousPath, StringComparison.OrdinalIgnoreCase));
        if (previousPath != null && previous == null)
            throw new InvalidOperationException("The previous Inventor document is no longer open.");
        var part = app.Documents.Cast<Document>().FirstOrDefault(document =>
            string.Equals(document.FullFileName, partPath, StringComparison.OrdinalIgnoreCase));
        active.Close(true);
        if (part != null && !string.Equals(partPath, previousPath, StringComparison.OrdinalIgnoreCase)) part.Close(true);
        previous?.Activate();
        Console.WriteLine("Quest batch fixture closed without saving; previous document restored: " + previousPath);
        return 0;
    }

    internal static int Inspect(global::Inventor.Application app)
    {
        var path = Path.Combine(System.Environment.CurrentDirectory, "artifacts", "m4-verification", "quest-fixture.json");
        var fixture = JObject.Parse(System.IO.File.ReadAllText(path));
        var expected = (string?)fixture["assembly"] ?? throw new InvalidDataException("Fixture path missing.");
        var moving = (string?)fixture["moving"] ?? throw new InvalidDataException("Fixture occurrence missing.");
        if (app.ActiveDocument is not AssemblyDocument assembly ||
            !string.Equals(assembly.FullFileName, expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The dedicated Quest fixture is not active in Inventor.");
        var occurrence = assembly.ComponentDefinition.Occurrences.Cast<ComponentOccurrence>()
            .Single(item => item.Name == moving);
        var transform = occurrence.Transformation;
        var result = new JObject
        {
            ["assembly"] = assembly.FullFileName,
            ["moving"] = moving,
            ["x_mm"] = transform.Cell[1, 4] * 10,
            ["y_mm"] = transform.Cell[2, 4] * 10,
            ["z_mm"] = transform.Cell[3, 4] * 10,
            ["rotation_matrix"] = new JArray(
                transform.Cell[1, 1], transform.Cell[1, 2], transform.Cell[1, 3],
                transform.Cell[2, 1], transform.Cell[2, 2], transform.Cell[2, 3],
                transform.Cell[3, 1], transform.Cell[3, 2], transform.Cell[3, 3]),
            ["constraint_count"] = assembly.ComponentDefinition.Constraints.Count,
            ["joint_count"] = assembly.ComponentDefinition.Joints.Count,
            ["dirty"] = assembly.Dirty,
        };
        Console.WriteLine(result.ToString(Newtonsoft.Json.Formatting.None));
        return 0;
    }

    internal static int Prepare(global::Inventor.Application app, bool wide = false)
    {
        var directory = Path.Combine(Path.GetTempPath(), "xrso-m4-quest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        PartDocument? part = null; AssemblyDocument? assembly = null;
        var original = app.ActiveDocument;
        try
        {
            part = (PartDocument)app.Documents.Add(DocumentTypeEnum.kPartDocumentObject);
            var sketch = part.ComponentDefinition.Sketches.Add(part.ComponentDefinition.WorkPlanes[3]);
            sketch.SketchCircles.AddByCenterRadius(app.TransientGeometry.CreatePoint2d(0, 0), wide ? 10 : 1);
            var extrusion = part.ComponentDefinition.Features.ExtrudeFeatures.CreateExtrudeDefinition(sketch.Profiles.AddForSolid(), PartFeatureOperationEnum.kJoinOperation);
            extrusion.SetDistanceExtent(wide ? 20 : 2, PartFeatureExtentDirectionEnum.kPositiveExtentDirection);
            part.ComponentDefinition.Features.ExtrudeFeatures.Add(extrusion);
            var partPath = Path.Combine(directory, "Cylinder.ipt"); part.SaveAs(partPath, false);
            assembly = (AssemblyDocument)app.Documents.Add(DocumentTypeEnum.kAssemblyDocumentObject);
            var fixedPart = assembly.ComponentDefinition.Occurrences.Add(partPath, app.TransientGeometry.CreateMatrix());
            fixedPart.Grounded = true;
            var pose = app.TransientGeometry.CreateMatrix(); pose.SetTranslation(app.TransientGeometry.CreateVector(wide ? 50 : 5, 0, 0));
            var moving = assembly.ComponentDefinition.Occurrences.Add(partPath, pose); moving.Grounded = false;
            var assemblyPath = Path.Combine(directory, "XR_M4_Quest_Acceptance.iam"); assembly.SaveAs(assemblyPath, false);
            var manifest = new JObject { ["assembly"] = assemblyPath, ["part"] = partPath,
                ["document_id"] = "doc_" + assembly.InternalName, ["moving"] = moving.Name, ["initial_x_mm"] = wide ? 500 : 50,
                ["wide"] = wide,
                ["previous_document"] = original?.FullFileName };
            var output = Path.Combine(System.Environment.CurrentDirectory, "artifacts", "m4-verification"); Directory.CreateDirectory(output);
            System.IO.File.WriteAllText(Path.Combine(output, "quest-fixture.json"), manifest.ToString());
            Console.WriteLine("Quest fixture prepared and left open: " + assemblyPath);
            return 0;
        }
        catch
        {
            try { assembly?.Close(true); } finally { part?.Close(true); original?.Activate(); }
            throw;
        }
    }
}
