using System;
using System.Reflection;
using System.Linq;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;
using Bimwright.Ipt.Shared.Plugin;
using Inventor;
using Newtonsoft.Json.Linq;
using Path = System.IO.Path;

internal static class NestedProbe
{
    internal static int Run(global::Inventor.Application app)
    {
        global::Inventor.Document? original = null;
        PartDocument? part = null;
        AssemblyDocument? subAssembly = null;
        AssemblyDocument? topAssembly = null;
        try
        {
            original = app.ActiveDocument;
            var fixtureDirectory = Path.Combine(Path.GetTempPath(), "xrso-m4-nested-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(fixtureDirectory);
            var partPath = Path.Combine(fixtureDirectory, "nested-cylinder.ipt");
            var subPath = Path.Combine(fixtureDirectory, "nested-subassembly.iam");

            part = (PartDocument)app.Documents.Add(DocumentTypeEnum.kPartDocumentObject,
                app.FileManager.GetTemplateFile(DocumentTypeEnum.kPartDocumentObject), true);
            var sketch = part.ComponentDefinition.Sketches.Add(part.ComponentDefinition.WorkPlanes[3]);
            sketch.SketchCircles.AddByCenterRadius(app.TransientGeometry.CreatePoint2d(0, 0), 1);
            var extrusion = part.ComponentDefinition.Features.ExtrudeFeatures.CreateExtrudeDefinition(
                sketch.Profiles.AddForSolid(), PartFeatureOperationEnum.kJoinOperation);
            extrusion.SetDistanceExtent(2, PartFeatureExtentDirectionEnum.kPositiveExtentDirection);
            part.ComponentDefinition.Features.ExtrudeFeatures.Add(extrusion);
            part.SaveAs(partPath, false);

            subAssembly = (AssemblyDocument)app.Documents.Add(DocumentTypeEnum.kAssemblyDocumentObject);
            var subDefinition = subAssembly.ComponentDefinition;
            subDefinition.Occurrences.Add(partPath, app.TransientGeometry.CreateMatrix());
            var childPlacement = app.TransientGeometry.CreateMatrix();
            childPlacement.SetTranslation(app.TransientGeometry.CreateVector(4, 0, 0));
            subDefinition.Occurrences.Add(partPath, childPlacement);
            subAssembly.SaveAs(subPath, false);

            topAssembly = (AssemblyDocument)app.Documents.Add(DocumentTypeEnum.kAssemblyDocumentObject);
            var topDefinition = topAssembly.ComponentDefinition;
            var fixedInstance = topDefinition.Occurrences.Add(subPath, app.TransientGeometry.CreateMatrix());
            fixedInstance.Grounded = true;
            var rotatedPlacement = app.TransientGeometry.CreateMatrix();
            rotatedPlacement.SetToRotation(Math.PI / 2, app.TransientGeometry.CreateVector(0, 0, 1),
                app.TransientGeometry.CreatePoint(0, 0, 0));
            rotatedPlacement.SetTranslation(app.TransientGeometry.CreateVector(20, 10, 0));
            var movingInstance = topDefinition.Occurrences.Add(subPath, rotatedPlacement);
            movingInstance.Grounded = false;

            var trackerType = typeof(InventorCommandRegistry).Assembly
                .GetType("Bimwright.Ipt.Shared.Plugin.CadEventTracker", true)!;
            using var tracker = (IDisposable)Activator.CreateInstance(trackerType, app)!;
            var journal = (CadEventJournal)trackerType.GetProperty("Journal")!.GetValue(tracker)!;
            var commands = InventorCommandRegistry.Build(new PluginOptions(2027, false, false, 0));
            var context = new InventorCommandContext
            {
                Application = app,
                Events = journal,
                Commands = commands,
                AllowExperimental = true,
                InventorYear = 2027,
            };

            JObject Call(string name, JObject arguments)
            {
                var response = commands[name].Execute(context, arguments);
                if (!response.Ok)
                    throw new InvalidOperationException(name + ": " + response.Error?.Code + " " + response.Error?.Message);
                return (JObject)response.Data!;
            }

            string topDocumentId = "doc_" + ((global::Inventor.Document)topAssembly).InternalName;
            JObject TopRequest() => new()
            {
                ["document_id"] = topDocumentId,
                ["expected_revision"] = journal.Revision(topDocumentId),
            };

            var overview = Call("get_assembly_context_xr", TopRequest());
            var topOccurrences = (JArray)overview["occurrences"]!;
            Check(topOccurrences.Count == 2, "Top assembly has exactly two direct repeated subassembly occurrences");
            Check(topOccurrences.All(item => (string?)item["definition_kind"] == "assembly"),
                "Both direct occurrences are reported as subassemblies");
            var movingJson = topOccurrences.Single(item => (string?)item["name"] == movingInstance.Name);
            var movingId = (string)movingJson["occurrence_id"]!;
            Check((bool?)movingJson["grounded"] == false && (bool?)movingJson["dof_complete"] == true,
                "Rotated top-level subassembly is free with complete DOF");
            Console.WriteLine("PASS nested native context: two direct subassemblies, grounded and free.");

            var originalPose = movingInstance.Transformation.Copy();
            string originalRevision = journal.Revision(topDocumentId);
            var operation = new JObject
            {
                ["command"] = "assembly_move",
                ["arguments"] = new JObject
                {
                    ["occurrence_id"] = movingId,
                    ["translation_mm"] = new JArray(10, 0, 0),
                },
            };
            var request = TopRequest();
            request["operations"] = new JArray(operation);
            request["preview"] = true;
            request["include_preview_mesh"] = true;
            request["validate"] = new JArray("rebuild", "constraint_health", "interference");
            var preview = Call("atomic_batch", request);
            var bodies = (JArray)preview["preview_mesh"]!["bodies"]!;
            Check(bodies.Count == 4, "Nested assembly preview tessellates all four cylinder bodies");
            var centers = bodies.Select(BodyCenter).ToArray();
            AssertCenters(centers, new[]
            {
                new[] { 0d, 0d, 1d }, new[] { 4d, 0d, 1d },
                new[] { 21d, 10d, 1d }, new[] { 21d, 14d, 1d },
            }, "preview mesh uses global transforms, including the 90-degree subassembly rotation and tentative move");
            Check(SamePose(movingInstance.Transformation, originalPose), "Nested assembly preview restores the occurrence pose");
            Check(journal.Revision(topDocumentId) == originalRevision, "Nested assembly preview preserves the revision");
            Console.WriteLine("PASS nested preview: four body meshes have expected global centers and clean rollback.");

            var nestedChild = movingInstance.SubOccurrences[1];
            var entityReferences = typeof(InventorCommandRegistry).Assembly
                .GetType("Bimwright.Ipt.Shared.Handlers.Core.EntityReferences", true)!;
            var describe = entityReferences.GetMethod("Describe", BindingFlags.Public | BindingFlags.Static)!;
            var childReference = (JObject)describe.Invoke(null,
                new object[] { (global::Inventor.Document)topAssembly, nestedChild })!;
            Check((bool?)childReference["supported"] == true, "Top-context reference key can identify a nested child");
            var childId = (string)childReference["id"]!;
            var childEdit = TopRequest();
            childEdit["operations"] = new JArray(new JObject
            {
                ["command"] = "assembly_move",
                ["arguments"] = new JObject { ["occurrence_id"] = childId, ["translation_mm"] = new JArray(1, 0, 0) },
            });
            childEdit["preview"] = true;
            childEdit["include_preview_mesh"] = true;
            childEdit["validate"] = new JArray("rebuild", "constraint_health", "interference");
            var childResponse = commands["atomic_batch"].Execute(context, childEdit);
            Check(!childResponse.Ok, "Editing a child occurrence from the top assembly is refused");
            Check(SamePose(movingInstance.Transformation, originalPose), "Refused nested child edit leaves parent pose unchanged");
            Check(journal.Revision(topDocumentId) == originalRevision, "Refused nested child edit preserves parent revision");
            Console.WriteLine("PASS nested child edit is refused in the top assembly.");

            string subDocumentId = "doc_" + ((global::Inventor.Document)subAssembly).InternalName;
            Call("activate_open_document_xr", new JObject { ["document_id"] = subDocumentId });
            // External COM event sink: drain activation notifications before taking
            // the revision snapshot. The production handler runs in Inventor's STA.
            System.Windows.Forms.Application.DoEvents();
            _ = app.ActiveDocument.InternalName;
            System.Windows.Forms.Application.DoEvents();
            var subRequest = new JObject
            {
                ["document_id"] = subDocumentId,
                ["expected_revision"] = journal.Revision(subDocumentId),
            };
            var subContext = Call("get_assembly_context_xr", subRequest);
            var subOccurrences = (JArray)subContext["occurrences"]!;
            Check(subOccurrences.Count == 2 && subOccurrences.All(item => (string?)item["definition_kind"] == "part"),
                "Explicitly activated subassembly exposes its own two part occurrences");
            Check(app.ActiveDocument.InternalName == subAssembly.InternalName,
                "Explicit activation switches the Inventor active document to the subassembly");
            Console.WriteLine("PASS explicit subassembly activation and child assembly context.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Nested Inventor probe failed: " + ex);
            return 1;
        }
        finally
        {
            try { if (topAssembly != null) ((global::Inventor.Document)topAssembly).Close(true); }
            finally
            {
                try { if (subAssembly != null) ((global::Inventor.Document)subAssembly).Close(true); }
                finally
                {
                    try { if (part != null) ((global::Inventor.Document)part).Close(true); }
                    finally { original?.Activate(); }
                }
            }
        }
    }

    private static double[] BodyCenter(JToken token)
    {
        var body = MeshPayload.FromJson((JObject)token);
        if (body.Positions.Length < 3) throw new InvalidOperationException("Nested preview body is empty: " + body.Name);
        var min = new[] { double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity };
        var max = new[] { double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity };
        for (int i = 0; i < body.Positions.Length; i++)
        {
            int axis = i % 3;
            min[axis] = Math.Min(min[axis], body.Positions[i]);
            max[axis] = Math.Max(max[axis], body.Positions[i]);
        }
        return Enumerable.Range(0, 3).Select(axis => (min[axis] + max[axis]) / 2).ToArray();
    }

    private static void AssertCenters(double[][] actual, double[][] expected, string message)
    {
        Check(actual.Length == expected.Length, message + " (body count)");
        var unmatched = actual.ToList();
        foreach (var target in expected)
        {
            int match = unmatched.FindIndex(point => Enumerable.Range(0, 3)
                .All(axis => Math.Abs(point[axis] - target[axis]) <= 0.02));
            Check(match >= 0, message + "; missing center [" + string.Join(", ", target) + "]");
            unmatched.RemoveAt(match);
        }
    }

    private static bool SamePose(Matrix left, Matrix right)
    {
        for (int row = 1; row <= 4; row++)
        for (int column = 1; column <= 4; column++)
            if (Math.Abs(left.Cell[row, column] - right.Cell[row, column]) > 1e-6) return false;
        return true;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Assertion failed: " + message);
    }
}
