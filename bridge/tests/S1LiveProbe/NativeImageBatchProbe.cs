using System.Security.Cryptography;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;
using Bimwright.Ipt.Shared.Plugin;
using Inventor;
using Newtonsoft.Json.Linq;
using File = System.IO.File;
using Path = System.IO.Path;

internal static partial class Program
{
    // Native product-handler evidence only. The wrapper owns and closes the whole
    // unsaved fixture afterwards; committed images intentionally form later baselines.
    private static void RunImageBatchProbe(Inventor.Application app, PartDocument doc, JObject report)
    {
        var sketches = doc.ComponentDefinition.Sketches;
        if (sketches.Count != 1) throw new InvalidOperationException("Image batch probe requires one original owned sketch.");
        var original = sketches[1];
        void Guard()
        {
            if (app.Documents.Count != 1 || app.ActiveDocument is not PartDocument active || !TopologySame(active, doc)
                || doc.DisplayName != "S1_ArcInput_Oracle.ipt" || !string.IsNullOrEmpty(doc.FullFileName)
                || doc.DocumentType != DocumentTypeEnum.kPartDocumentObject || sketches.Count < 1 || !TopologySame(sketches[1], original))
                throw new InvalidOperationException("Exact single unsaved owned image fixture is required.");
            if (app.ActiveEditObject is object edit && !TopologySame(edit, original)
                && !TopologySame(edit, doc) && !TopologySame(edit, doc.ComponentDefinition))
                throw new InvalidOperationException("Another native edit context is active.");
        }
        Guard();
        if (original.SketchEntities.Count != 0 || original.GeometricConstraints.Count != 0
            || original.DimensionConstraints.Count != 0 || original.TextBoxes.Count != 0
            || original.SketchImages.Count != 0 || doc.ReferencedOLEFileDescriptors.Count != 0)
            throw new InvalidOperationException("Image batch fixture must start completely empty, including OLE descriptors.");
        if (app.ActiveEditObject is PlanarSketch editing) editing.ExitEdit();
        Guard();
        var trackerType = typeof(InventorCommandRegistry).Assembly.GetType("Bimwright.Ipt.Shared.Plugin.CadEventTracker", true)!;
        using var tracker = (IDisposable)Activator.CreateInstance(trackerType, app)!;
        var journal = (CadEventJournal)trackerType.GetProperty("Journal")!.GetValue(tracker)!;
        using var staging = new SketchImportStagingStore(new SketchImportStagingPolicy(
            1024 * 1024, 2 * 1024 * 1024, 4, StageSketchImportHandler.MaxChunkBytes, TimeSpan.FromMinutes(10), 128));
        var commands = InventorCommandRegistry.Build(new PluginOptions(2027, false, false, 0));
        var context = new InventorCommandContext { Application = app, Events = journal, Commands = commands,
            AllowExperimental = true, InventorYear = 2027, SketchImports = staging };
        string documentId = "doc_" + doc.InternalName;
        const string owner = "s1-native-image-batch";
        string session = Guid.NewGuid().ToString("N");
        var rows = new JArray(); report["image_batch_cases"] = rows;
        report["evidence"] = "Native Inventor API product handlers: stage_sketch_import begin/chunk/seal then atomic_batch insert_image. No HTTP, desktop equivalence, Quest, physical, alpha or durable linked-file evidence.";
        report["kind"] = doc.ComponentDefinition is SheetMetalComponentDefinition ? "sheetmetal" : "part";
        report["intended_case_count"] = 48;
        JObject State() => new() { ["document_id"] = documentId, ["expected_revision"] = journal.Revision(documentId) };
        JObject Call(string command, JObject args)
        {
            var result = commands[command].Execute(context, args);
            if (!result.Ok) throw new InvalidOperationException(command + ": " + result.Error?.Code + " " + result.Error?.Message);
            return (JObject)result.Data!;
        }
        JObject StageArgs(string action) => new() { ["action"] = action, ["owner"] = owner, ["session"] = session };
        JObject Snapshot()
        {
            Guard(); var all = new JArray(); var ole = new JArray(); int imageCount = 0;
            var imageKeys = new HashSet<string>(StringComparer.Ordinal); var oleKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (PlanarSketch sketch in sketches)
            {
                var images = new JArray();
                foreach (SketchImage image in sketch.SketchImages)
                {
                    var readback = BmpProbeImageReadback(image); string key = (string?)readback["reference_key"] ?? "";
                    if (key.Length == 0 || !imageKeys.Add(key)) throw new InvalidOperationException("Image baseline identity is missing or duplicated.");
                    images.Add(readback); imageCount++;
                }
                all.Add(new JObject { ["key"] = TopologyKey(sketch), ["name"] = sketch.Name,
                    ["entities"] = sketch.SketchEntities.Count, ["constraints"] = sketch.GeometricConstraints.Count,
                    ["dimensions"] = sketch.DimensionConstraints.Count, ["textboxes"] = sketch.TextBoxes.Count, ["images"] = images });
            }
            foreach (ReferencedOLEFileDescriptor descriptor in doc.ReferencedOLEFileDescriptors)
            {
                string key = TopologyKey(descriptor);
                if (key.Length == 0 || !oleKeys.Add(key)) throw new InvalidOperationException("OLE baseline identity is missing or duplicated.");
                ole.Add(new JObject { ["key"] = key, ["filename"] = descriptor.FullFileName });
            }
            return new JObject { ["document_id"] = documentId, ["sketches"] = all, ["ole"] = ole,
                ["sketch_count"] = sketches.Count, ["image_count"] = imageCount, ["ole_count"] = ole.Count };
        }
        report["initial_state"] = Snapshot();
        string directory = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "s1-image-product-" + Guid.NewGuid().ToString("N")));
        if (Directory.Exists(directory) || File.Exists(directory)) throw new InvalidOperationException("Image probe scratch directory already exists.");
        Directory.CreateDirectory(directory); var created = new List<string>();
        try
        {
            foreach (int bits in new[] { 24, 32 })
            foreach (bool topDown in new[] { false, true })
            {
                string path = Path.GetFullPath(Path.Combine(directory, $"fixture-{bits}-{topDown}.bmp"));
                if (Path.GetDirectoryName(path) != directory) throw new InvalidOperationException("Image probe scratch path escaped.");
                BmpProbeWriteFile(path, bits, topDown, created);
                byte[] bytes = File.ReadAllBytes(path);
                try
                {
                    string hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(); string asset = "a_" + hash;
                    foreach (int turns in new[] { 0, 1, 2, 3 })
                    foreach (string action in new[] { "preview", "commit", "failure_after_add" })
                    {
                        Guard();
                        var sketch = sketches.Add(doc.ComponentDefinition.WorkPlanes[3]); sketch.Name = "S1_ImageBatch_" + sketches.Count;
                        if (sketch.SketchEntities.Count != 0 || sketch.SketchImages.Count != 0 || sketch.TextBoxes.Count != 0
                            || sketch.GeometricConstraints.Count != 0 || sketch.DimensionConstraints.Count != 0)
                            throw new InvalidOperationException("Fresh image batch sketch is not completely empty.");
                        string sketchId = (string)((JArray)Call("get_design_context_xr", State())["sketches"]!)
                            .Single(s => (string?)s["name"] == sketch.Name)["sketch_id"]!;
                        string revision = journal.Revision(documentId); var before = Snapshot();
                        var row = new JObject { ["bits_per_pixel"] = bits, ["top_down"] = topDown, ["quarter_turns"] = turns,
                            ["action"] = action, ["sketch_id"] = sketchId, ["source_sha256"] = hash,
                            ["revision_before"] = revision, ["before"] = before.DeepClone() };
                        rows.Add(row); string? transfer = null;
                        try
                        {
                            var begin = StageArgs("begin"); begin["byte_length"] = bytes.Length;
                            transfer = (string)Call("stage_sketch_import", begin)["transfer_id"]!;
                            var chunk = StageArgs("chunk"); chunk["transfer_id"] = transfer; chunk["offset"] = 0;
                            chunk["data_base64"] = Convert.ToBase64String(bytes); Call("stage_sketch_import", chunk);
                            var seal = StageArgs("seal"); seal["transfer_id"] = transfer; seal["sha256"] = hash;
                            if ((bool?)Call("stage_sketch_import", seal)["sealed"] != true) throw new InvalidOperationException("Image staging was not sealed.");
                            var options = new JObject { ["asset_id"] = asset, ["position_mm"] = new JArray(13, 21),
                                ["width_mm"] = 30, ["quarter_turns"] = turns, ["link"] = false };
                            var operations = new JArray(new JObject { ["command"] = "edit_sketch", ["arguments"] = new JObject {
                                ["sketch_id"] = sketchId, ["variant"] = "insert_image", ["options"] = options } });
                            if (action == "failure_after_add") operations.Add(new JObject { ["command"] = "edit_sketch",
                                ["arguments"] = new JObject { ["sketch_id"] = sketchId, ["variant"] = "line", ["options"] = new JObject() } });
                            var batch = State(); batch["preview"] = action == "preview"; batch["operations"] = operations;
                            batch["import_attachments"] = new JObject { ["owner"] = owner, ["session"] = session,
                                ["resources"] = new JArray(new JObject { ["asset_id"] = asset, ["transfer_id"] = transfer,
                                    ["sha256"] = hash, ["byte_length"] = bytes.Length, ["format"] = "bmp", ["metadata"] = new JObject {
                                        ["kind"] = "Image", ["dependencies"] = new JArray(), ["image"] = new JObject {
                                            ["pixel_width"] = 3, ["pixel_height"] = 2 } } }) };
                            var result = commands["atomic_batch"].Execute(context, batch);
                            row["batch_result"] = JObject.FromObject(result);
                            if (action == "failure_after_add")
                            {
                                if (result.Ok || result.Error?.Code != InventorErrorCodes.ROLLED_BACK
                                    || (int?)result.Error.Details?["step_index"] != 1 || (string?)result.Error.Details?["command"] != "edit_sketch")
                                    throw new InvalidOperationException("Expected second-step failure after successful image insertion and exact owned rollback.");
                            }
                            else if (!result.Ok || (string?)result.Data?["status"] != (action == "preview" ? "preview_rolled_back" : "committed"))
                                throw new InvalidOperationException("Image product batch did not reach its expected outcome.");
                            Guard(); var after = Snapshot(); row["after"] = after.DeepClone(); row["revision_after"] = journal.Revision(documentId);
                            if (action != "commit")
                            {
                                row["exact_restoration"] = JToken.DeepEquals(before, after) && journal.Revision(documentId) == revision;
                                if ((bool?)row["exact_restoration"] != true) throw new InvalidOperationException("Image batch did not restore all pre-existing images, OLE descriptors and revision.");
                            }
                            else
                            {
                                if (journal.Revision(documentId) == revision || sketch.SketchImages.Count != 1
                                    || (int)after["image_count"]! != (int)before["image_count"]! + 1
                                    || (int)after["ole_count"]! != (int)before["ole_count"]! + 1)
                                    throw new InvalidOperationException("Committed image cardinality or revision did not advance as expected.");
                                var prior = (JArray)before["sketches"]!; var current = (JArray)after["sketches"]!;
                                for (int n = 0; n < prior.Count - 1; n++)
                                    if (!JToken.DeepEquals(prior[n], current[n])) throw new InvalidOperationException("Commit changed a pre-existing sketch/image baseline.");
                                foreach (JObject ole in (JArray)before["ole"]!)
                                    if (!((JArray)after["ole"]!).Any(v => JToken.DeepEquals(v, ole))) throw new InvalidOperationException("Commit changed a pre-existing OLE descriptor.");
                                var image = sketch.SketchImages[1];
                                if (image.LinkedToFile || !TopologySame(image.Parent, sketch)) throw new InvalidOperationException("Image is not embedded in the selected native sketch.");
                                ImageNear45(image.Width * 10, 30); ImageNear45(image.Height * 10, 20);
                                var readback = BmpProbeImageReadback(image); row["committed_image"] = readback;
                                var boundary = (JArray)readback["boundary_mm"]!;
                                if (boundary.Count != 4) throw new InvalidOperationException("Image boundary is not a native four-edge rectangle.");
                                var xs = boundary.OfType<JArray>().SelectMany(e => new[] { (double)e[0], (double)e[2] }).ToArray();
                                var ys = boundary.OfType<JArray>().SelectMany(e => new[] { (double)e[1], (double)e[3] }).ToArray();
                                ImageNear45(xs.Max() - xs.Min(), turns % 2 == 0 ? 30 : 20);
                                ImageNear45(ys.Max() - ys.Min(), turns % 2 == 0 ? 20 : 30);
                                if (turns == 0) { ImageNear45(image.Position.X * 10, 13); ImageNear45(image.Position.Y * 10, 21); }
                                row["orientation_evidence"] = "Native boundary recorded; axis spans checked for requested quarter turn. Pixel orientation/alpha and desktop visual equivalence are not covered.";
                            }
                            row["outcome"] = "PASS native product handler assertions";
                        }
                        catch (Exception ex)
                        { row["outcome"] = "FAIL"; row["error"] = ex.ToString(); throw; }
                        finally
                        {
                            if (transfer != null)
                            {
                                var discard = StageArgs("discard"); discard["transfer_id"] = transfer;
                                row["staging_cleanup"] = Call("stage_sketch_import", discard);
                            }
                            row["staging_reserved_bytes_after"] = staging.ReservedBytes;
                            if (staging.ReservedBytes != 0 || staging.ResidentTransfers != 0)
                                throw new InvalidOperationException("Image batch leaked a staging lease or transfer.");
                        }
                    }
                }
                finally { Array.Clear(bytes, 0, bytes.Length); }
            }
            report["final_state"] = Snapshot();
        }
        finally
        {
            report["case_count"] = rows.Count;
            report["completed_cases"] = rows.OfType<JObject>().Count(r => (string?)r["outcome"] == "PASS native product handler assertions");
            report["failed_cases"] = rows.OfType<JObject>().Count(r => (string?)r["outcome"] == "FAIL");
            report["not_run_cases"] = 48 - rows.Count;
            var errors = new JArray();
            foreach (string path in created)
                try { File.Delete(path); } catch (Exception ex) { errors.Add(new JObject { ["file"] = path, ["error"] = ex.ToString() }); }
            try { Directory.Delete(directory, false); } catch (Exception ex) { errors.Add(new JObject { ["directory"] = directory, ["error"] = ex.ToString() }); }
            report["temp_cleanup_errors"] = errors; report["temp_cleanup_complete"] = errors.Count == 0;
            report["fixture_cleanup"] = "NOT PERFORMED HERE: root wrapper must close only this owned unsaved fixture, skip save, and restore previous document.";
            if (errors.Count != 0) throw new InvalidOperationException("Owned image probe scratch cleanup failed.");
        }
    }
    private static void ImageNear45(double actualMm, double expectedMm)
    {
        if (!double.IsFinite(actualMm) || Math.Abs(actualMm - expectedMm) > 1e-5)
            throw new InvalidOperationException("Native image property differs beyond 0.00001 mm.");
    }
}
