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
    // Save a copy of the wrapper's exact unsaved fixture; never close the original document.
    private static void RunImageSaveCopyProbe46(Inventor.Application app, PartDocument doc, JObject report)
    {
        var sketches = doc.ComponentDefinition.Sketches;
        void GuardOriginal()
        {
            if (app.Documents.Count != 1 || app.ActiveDocument is not PartDocument active || !TopologySame(active, doc)
                || doc.DisplayName != "S1_ArcInput_Oracle.ipt" || !string.IsNullOrEmpty(doc.FullFileName)
                || doc.DocumentType != DocumentTypeEnum.kPartDocumentObject || sketches.Count != 1)
                throw new InvalidOperationException("Exact single unsaved owned image save-copy fixture is required.");
        }
        GuardOriginal(); var sketch = sketches[1];
        if (sketch.SketchEntities.Count != 0 || sketch.GeometricConstraints.Count != 0 || sketch.DimensionConstraints.Count != 0
            || sketch.TextBoxes.Count != 0 || sketch.SketchImages.Count != 0 || doc.ReferencedOLEFileDescriptors.Count != 0)
            throw new InvalidOperationException("Image save-copy fixture must start empty, including OLE.");
        if (app.ActiveEditObject is PlanarSketch editing)
        {
            if (!TopologySame(editing, sketch)) throw new InvalidOperationException("Another sketch is being edited.");
            editing.ExitEdit();
        }
        else if (app.ActiveEditObject is object edit && !TopologySame(edit, doc) && !TopologySame(edit, doc.ComponentDefinition))
            throw new InvalidOperationException("Another native edit context is active.");
        var trackerType = typeof(InventorCommandRegistry).Assembly.GetType("Bimwright.Ipt.Shared.Plugin.CadEventTracker", true)!;
        using var tracker = (IDisposable)Activator.CreateInstance(trackerType, app)!;
        var journal = (CadEventJournal)trackerType.GetProperty("Journal")!.GetValue(tracker)!;
        using var staging = new SketchImportStagingStore(new SketchImportStagingPolicy(
            1024 * 1024, 2 * 1024 * 1024, 4, StageSketchImportHandler.MaxChunkBytes, TimeSpan.FromMinutes(10), 128));
        var commands = InventorCommandRegistry.Build(new PluginOptions(2027, false, false, 0));
        var context = new InventorCommandContext { Application = app, Events = journal, Commands = commands,
            AllowExperimental = true, InventorYear = 2027, SketchImports = staging };
        string documentId = "doc_" + doc.InternalName;
        const string owner = "s1-native-image-save-copy46";
        string session = Guid.NewGuid().ToString("N");
        JObject State() => new() { ["document_id"] = documentId, ["expected_revision"] = journal.Revision(documentId) };
        JObject Call(string command, JObject args)
        {
            var result = commands[command].Execute(context, args);
            if (!result.Ok) throw new InvalidOperationException(command + ": " + result.Error?.Code + " " + result.Error?.Message);
            return (JObject)result.Data!;
        }
        JObject StageArgs(string action) => new() { ["action"] = action, ["owner"] = owner, ["session"] = session };
        JObject OriginalState() => new() { ["document_id"] = "doc_" + doc.InternalName, ["full_filename"] = doc.FullFileName,
            ["database_revision"] = doc.DatabaseRevisionId, ["dirty"] = doc.Dirty };
        var row = new JObject { ["bits_per_pixel"] = 32, ["top_down"] = true, ["quarter_turns"] = 1 };
        report["image_save_copy_cases"] = new JArray(row);
        report["kind"] = doc.ComponentDefinition is SheetMetalComponentDefinition ? "sheetmetal" : "part";
        report["intended_case_count"] = 1;
        report["evidence"] = "Native stage_sketch_import and atomic_batch embedded BMP; raw payload attributes and geometry survive SaveAs(copy=true) and reopening the copy. No HTTP, query-handler, rendering, linked, desktop or Quest proof.";
        string directory = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "s1-image-save-copy46-" + Guid.NewGuid().ToString("N")));
        if (Directory.Exists(directory) || File.Exists(directory)) throw new InvalidOperationException("Image save-copy scratch directory already exists.");
        Directory.CreateDirectory(directory);
        var created = new List<string>(); string? transfer = null; byte[]? bytes = null;
        PartDocument? copy = null; bool copyOwned = false, copyClosed = false, saveAttempted = false;
        string copyPath = Path.GetFullPath(Path.Combine(directory, "saved-copy.ipt"));
        if (Path.GetDirectoryName(copyPath) != directory) throw new InvalidOperationException("Save-copy path escaped scratch directory.");
        try
        {
            string source = Path.Combine(directory, "source.bmp"); BmpProbeWriteFile(source, 32, true, created);
            bytes = File.ReadAllBytes(source); int byteLength = bytes.Length;
            string hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), asset = "a_" + hash;
            row["source_sha256"] = hash; row["source_byte_length"] = byteLength;
            JObject Payload(SketchImage image)
            {
                const string setName = "InventorSO_SketchImagePayload";
                if (!image.AttributeSets.NameIsUsed[setName]) throw new InvalidOperationException("Native payload attribute set missing.");
                var set = image.AttributeSets[setName];
                if (set.Count != 8 || set.Transient) throw new InvalidOperationException("Payload fields missing or transient.");
                foreach (string field in new[] { "schema", "bytes", "sha256", "asset_id", "byte_length", "format", "pixel_width", "pixel_height" })
                    if (!set.NameIsUsed[field]) throw new InvalidOperationException("Payload field missing: " + field);
                if (set["bytes"].ValueType != ValueTypeEnum.kByteArrayType || set["bytes"].Value is not byte[] raw)
                    throw new InvalidOperationException("Payload does not contain native byte-array data.");
                string actualHash = Convert.ToHexString(SHA256.HashData(raw)).ToLowerInvariant();
                if (Convert.ToInt32(set["schema"].Value) != 1 || raw.Length != byteLength || actualHash != hash
                    || (string)set["sha256"].Value != hash || (string)set["asset_id"].Value != asset
                    || Convert.ToInt32(set["byte_length"].Value) != byteLength || (string)set["format"].Value != "bmp"
                    || Convert.ToInt32(set["pixel_width"].Value) != 3 || Convert.ToInt32(set["pixel_height"].Value) != 2)
                    throw new InvalidOperationException("Native persisted payload metadata/hash differs from sealed source.");
                return new JObject { ["schema"] = 1, ["byte_length"] = raw.Length, ["sha256"] = actualHash,
                    ["asset_id"] = asset, ["format"] = "bmp", ["pixel_width"] = 3, ["pixel_height"] = 2, ["transient"] = false };
            }
            JObject Geometry(SketchImage image)
            {
                var value = BmpProbeImageReadback(image); value.Remove("reference_key"); value.Remove("name"); return value;
            }
            var begin = StageArgs("begin"); begin["byte_length"] = byteLength;
            transfer = (string)Call("stage_sketch_import", begin)["transfer_id"]!;
            var chunk = StageArgs("chunk"); chunk["transfer_id"] = transfer; chunk["offset"] = 0;
            chunk["data_base64"] = Convert.ToBase64String(bytes); Call("stage_sketch_import", chunk);
            var seal = StageArgs("seal"); seal["transfer_id"] = transfer; seal["sha256"] = hash;
            if ((bool?)Call("stage_sketch_import", seal)["sealed"] != true) throw new InvalidOperationException("Save-copy image staging not sealed.");
            File.Delete(source); row["original_source_deleted_before_commit"] = !File.Exists(source);
            if (File.Exists(source)) throw new InvalidOperationException("Original source remains before commit.");
            string sketchId = (string)((JArray)Call("get_design_context_xr", State())["sketches"]!).Single()["sketch_id"]!;
            var batch = State(); batch["preview"] = false; batch["history_owner"] = owner;
            batch["operations"] = new JArray(new JObject { ["command"] = "edit_sketch", ["arguments"] = new JObject {
                ["sketch_id"] = sketchId, ["variant"] = "insert_image", ["options"] = new JObject {
                    ["asset_id"] = asset, ["position_mm"] = new JArray(13, 21), ["width_mm"] = 30, ["quarter_turns"] = 1, ["link"] = false } } });
            batch["import_attachments"] = new JObject { ["owner"] = owner, ["session"] = session,
                ["resources"] = new JArray(new JObject { ["asset_id"] = asset, ["transfer_id"] = transfer,
                    ["sha256"] = hash, ["byte_length"] = byteLength, ["format"] = "bmp", ["metadata"] = new JObject {
                        ["kind"] = "Image", ["dependencies"] = new JArray(), ["image"] = new JObject { ["pixel_width"] = 3, ["pixel_height"] = 2 } } }) };
            var privateBefore = Directory.GetDirectories(Path.GetTempPath(), "inventor-so-image-*").ToHashSet(StringComparer.OrdinalIgnoreCase);
            row["commit"] = Call("atomic_batch", batch);
            if ((string?)row["commit"]!["status"] != "committed") throw new InvalidOperationException("Image batch was not committed.");
            var privateAfter = Directory.GetDirectories(Path.GetTempPath(), "inventor-so-image-*").Where(p => !privateBefore.Contains(p)).ToArray();
            row["private_sources_after_commit"] = new JArray(privateAfter);
            if (privateAfter.Length != 0) throw new InvalidOperationException("Private image source remains before save-copy.");
            var discard = StageArgs("discard"); discard["transfer_id"] = transfer;
            row["staging_cleanup_before_save"] = Call("stage_sketch_import", discard); transfer = null;
            if (staging.ReservedBytes != 0 || staging.ResidentTransfers != 0) throw new InvalidOperationException("Image staging remains before save-copy.");
            Array.Clear(bytes, 0, bytes.Length); bytes = null;
            if (sketch.SketchImages.Count != 1) throw new InvalidOperationException("Expected one committed image.");
            var image = sketch.SketchImages[1]; row["payload_before_save"] = Payload(image);
            var geometry = Geometry(image); row["geometry_before_save"] = geometry;
            if (image.LinkedToFile) throw new InvalidOperationException("Save-copy test requires an embedded image.");
            ImageNear45(image.Width * 10, 30); ImageNear45(image.Height * 10, 20);
            var boundary = (JArray)geometry["boundary_mm"]!;
            var xs = boundary.OfType<JArray>().SelectMany(v => new[] { (double)v[0], (double)v[2] }).ToArray();
            var ys = boundary.OfType<JArray>().SelectMany(v => new[] { (double)v[1], (double)v[3] }).ToArray();
            if (boundary.Count != 4) throw new InvalidOperationException("Image boundary cardinality differs.");
            ImageNear45(xs.Max() - xs.Min(), 20); ImageNear45(ys.Max() - ys.Min(), 30);
            GuardOriginal(); var before = OriginalState(); row["original_before_save_copy"] = before;
            if (File.Exists(copyPath)) throw new InvalidOperationException("Save-copy destination unexpectedly exists.");
            saveAttempted = true; doc.SaveAs(copyPath, true);
            if (!File.Exists(copyPath)) throw new InvalidOperationException("Native SaveCopyAs created no file.");
            created.Add(copyPath); row["saved_copy_bytes"] = new FileInfo(copyPath).Length;
            row["original_after_save_copy"] = OriginalState();
            copy = app.Documents.Open(copyPath, false) as PartDocument ?? throw new InvalidOperationException("Reopened copy is not a PartDocument.");
            copyOwned = !TopologySame(copy, doc) && string.Equals(Path.GetFullPath(copy.FullFileName), copyPath, StringComparison.OrdinalIgnoreCase);
            if (!copyOwned || app.Documents.Count != 2) throw new InvalidOperationException("Reopened copy ownership/count could not be verified.");
            if (copy.ComponentDefinition.Sketches.Count != 1 || copy.ComponentDefinition.Sketches[1].SketchImages.Count != 1)
                throw new InvalidOperationException("Reopened copy image cardinality differs.");
            var copiedImage = copy.ComponentDefinition.Sketches[1].SketchImages[1];
            row["payload_after_reopen"] = Payload(copiedImage); row["geometry_after_reopen"] = Geometry(copiedImage);
            if (!JToken.DeepEquals(row["payload_before_save"], row["payload_after_reopen"])) throw new InvalidOperationException("Saved-copy native payload differs.");
            var copiedGeometry = (JObject)row["geometry_after_reopen"]!;
            foreach (string field in new[] { "position_mm", "boundary_mm" })
            {
                var a = ((JArray)geometry[field]!).DescendantsAndSelf().OfType<JValue>().Select(v => (double)v).ToArray();
                var b = ((JArray)copiedGeometry[field]!).DescendantsAndSelf().OfType<JValue>().Select(v => (double)v).ToArray();
                if (a.Length != b.Length) throw new InvalidOperationException("Saved-copy image geometry cardinality differs.");
                for (int n = 0; n < a.Length; n++) ImageNear45(a[n], b[n]);
            }
            foreach (string field in new[] { "width_mm", "height_mm" }) ImageNear45((double)geometry[field]!, (double)copiedGeometry[field]!);
            if ((bool?)copiedGeometry["linked"] != false || (string?)geometry["type"] != (string?)copiedGeometry["type"])
                throw new InvalidOperationException("Saved-copy image type/link state differs.");
            row["payload_persisted_exact"] = true; row["geometry_persisted_exact"] = true;
            copy.Close(true); copyClosed = true; copy = null; doc.Activate(); GuardOriginal();
            var after = OriginalState(); row["original_after_copy_close"] = after;
            if ((string?)before["document_id"] != (string?)after["document_id"] || (string?)after["full_filename"] != ""
                || (bool?)before["dirty"] != (bool?)after["dirty"])
                throw new InvalidOperationException("Save-copy changed original document identity/path/dirty state.");
            row["original_revision_changed_by_save_copy"] = !JToken.DeepEquals(before["database_revision"], after["database_revision"]);
            row["original_preserved"] = true; row["copy_closed"] = true; row["outcome"] = "PASS";
        }
        catch (Exception ex) { row["outcome"] = "FAIL"; row["error"] = ex.ToString(); throw; }
        finally
        {
            if (bytes != null) Array.Clear(bytes, 0, bytes.Length);
            var errors = new JArray();
            try { if (transfer != null) { var discard = StageArgs("discard"); discard["transfer_id"] = transfer; Call("stage_sketch_import", discard); } }
            catch (Exception ex) { errors.Add(new JObject { ["staging_error"] = ex.ToString() }); }
            try
            {
                if (copy != null && copyOwned)
                {
                    if (TopologySame(copy, doc) || !string.Equals(Path.GetFullPath(copy.FullFileName), copyPath, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Copy ownership changed before close; preserving it.");
                    copy.Close(true); copyClosed = true; copy = null;
                }
                if (copy == null) { doc.Activate(); GuardOriginal(); }
            }
            catch (Exception ex) { errors.Add(new JObject { ["document_cleanup_error"] = ex.ToString() }); }
            bool safe = copy == null && (!saveAttempted || copyClosed) && errors.Count == 0;
            if (safe)
            {
                foreach (string path in created)
                    try
                    {
                        if (!string.Equals(Path.GetDirectoryName(Path.GetFullPath(path)), directory, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidOperationException("Owned cleanup path escaped scratch directory.");
                        File.Delete(path);
                    }
                    catch (Exception ex) { errors.Add(new JObject { ["file"] = path, ["error"] = ex.ToString() }); }
                if (errors.Count == 0)
                    try { Directory.Delete(directory, false); } catch (Exception ex) { errors.Add(new JObject { ["directory"] = directory, ["error"] = ex.ToString() }); }
            }
            else errors.Add(new JObject { ["preserved_directory"] = directory, ["reason"] = "Copy close or ownership unverified; no scratch files removed." });
            report["case_count"] = 1; report["completed_cases"] = (string?)row["outcome"] == "PASS" && errors.Count == 0 ? 1 : 0;
            report["failed_cases"] = 1 - (int)report["completed_cases"]!;
            report["temp_cleanup_errors"] = errors; report["temp_cleanup_complete"] = errors.Count == 0;
            if (errors.Count != 0) { row["outcome"] = "FAIL"; throw new InvalidOperationException("Owned image save-copy cleanup failed; files retained when ownership was unverified."); }
        }
    }
}
