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
    // The isolated wrapper owns the unsaved document and closes it without saving.
    // No manual OLE repair is permitted here: history must restore the native baseline itself.
    private static void RunImageHistoryProbe46(Inventor.Application app, PartDocument doc, JObject report)
    {
        var sketches = doc.ComponentDefinition.Sketches;
        if (sketches.Count != 1) throw new InvalidOperationException("Image history requires one original owned sketch.");
        var original = sketches[1];
        void Guard()
        {
            if (app.Documents.Count != 1 || app.ActiveDocument is not PartDocument active || !TopologySame(active, doc)
                || doc.DisplayName != "S1_ArcInput_Oracle.ipt" || !string.IsNullOrEmpty(doc.FullFileName)
                || doc.DocumentType != DocumentTypeEnum.kPartDocumentObject || sketches.Count < 1 || !TopologySame(sketches[1], original))
                throw new InvalidOperationException("Exact single unsaved owned image history fixture is required.");
            if (app.ActiveEditObject is object edit && !TopologySame(edit, original)
                && !TopologySame(edit, doc) && !TopologySame(edit, doc.ComponentDefinition))
                throw new InvalidOperationException("Another native edit context is active.");
        }
        Guard();
        if (original.SketchEntities.Count != 0 || original.GeometricConstraints.Count != 0
            || original.DimensionConstraints.Count != 0 || original.TextBoxes.Count != 0
            || original.SketchImages.Count != 0 || doc.ReferencedOLEFileDescriptors.Count != 0)
            throw new InvalidOperationException("Image history fixture must start completely empty, including OLE.");
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
        const string owner = "s1-native-image-history46";
        string session = Guid.NewGuid().ToString("N");
        var rows = new JArray(); report["image_history_cases"] = rows;
        report["evidence"] = "Native stage_sketch_import and atomic_batch embedded image followed by history_xr status/undo/redo. Original source deleted before commit; staging discarded before history. No HTTP, rendering, pixel, desktop, linked or Quest proof.";
        report["kind"] = doc.ComponentDefinition is SheetMetalComponentDefinition ? "sheetmetal" : "part";
        report["intended_case_count"] = 16;
        JObject State() => new() { ["document_id"] = documentId, ["expected_revision"] = journal.Revision(documentId) };
        JObject Call(string command, JObject args)
        {
            var result = commands[command].Execute(context, args);
            if (!result.Ok) throw new InvalidOperationException(command + ": " + result.Error?.Code + " " + result.Error?.Message);
            return (JObject)result.Data!;
        }
        JObject StageArgs(string action) => new() { ["action"] = action, ["owner"] = owner, ["session"] = session };
        JObject HistoryArgs(string action, string? ticket = null)
        { var args = State(); args["owner"] = owner; args["action"] = action; if (ticket != null) args["ticket"] = ticket; return args; }
        JObject Snapshot()
        {
            Guard(); var all = new JArray(); var ole = new JArray(); int count = 0;
            var imageKeys = new HashSet<string>(StringComparer.Ordinal); var oleKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (PlanarSketch sketch in sketches)
            {
                var images = new JArray();
                foreach (SketchImage image in sketch.SketchImages)
                {
                    var readback = BmpProbeImageReadback(image); string key = (string?)readback["reference_key"] ?? "";
                    if (key.Length == 0 || !imageKeys.Add(key)) throw new InvalidOperationException("Image history baseline identity missing or duplicated.");
                    images.Add(readback); count++;
                }
                all.Add(new JObject { ["key"] = TopologyKey(sketch), ["name"] = sketch.Name,
                    ["entities"] = sketch.SketchEntities.Count, ["constraints"] = sketch.GeometricConstraints.Count,
                    ["dimensions"] = sketch.DimensionConstraints.Count, ["textboxes"] = sketch.TextBoxes.Count, ["images"] = images });
            }
            foreach (ReferencedOLEFileDescriptor descriptor in doc.ReferencedOLEFileDescriptors)
            {
                string key = TopologyKey(descriptor);
                if (key.Length == 0 || !oleKeys.Add(key)) throw new InvalidOperationException("OLE history baseline identity missing or duplicated.");
                ole.Add(new JObject { ["key"] = key, ["filename"] = descriptor.FullFileName });
            }
            return new JObject { ["document_id"] = documentId, ["sketches"] = all, ["ole"] = ole,
                ["sketch_count"] = sketches.Count, ["image_count"] = count, ["ole_count"] = ole.Count };
        }
        report["initial_state"] = Snapshot();
        string directory = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "s1-image-history46-" + Guid.NewGuid().ToString("N")));
        if (Directory.Exists(directory) || File.Exists(directory)) throw new InvalidOperationException("Image history scratch directory already exists.");
        Directory.CreateDirectory(directory); var created = new List<string>();
        try
        {
            foreach (int bits in new[] { 24, 32 })
            foreach (bool topDown in new[] { false, true })
            foreach (int turns in new[] { 0, 1, 2, 3 })
            {
                Guard();
                var sketch = sketches.Add(doc.ComponentDefinition.WorkPlanes[3]); sketch.Name = "S1_ImageHistory46_" + sketches.Count;
                if (sketch.SketchEntities.Count != 0 || sketch.SketchImages.Count != 0 || sketch.GeometricConstraints.Count != 0
                    || sketch.DimensionConstraints.Count != 0 || sketch.TextBoxes.Count != 0)
                    throw new InvalidOperationException("New owned history sketch is not empty.");
                string sketchId = (string)((JArray)Call("get_design_context_xr", State())["sketches"]!)
                    .Single(s => (string?)s["name"] == sketch.Name)["sketch_id"]!;
                var before = Snapshot(); string beforeRevision = journal.Revision(documentId);
                var row = new JObject { ["bits_per_pixel"] = bits, ["top_down"] = topDown, ["quarter_turns"] = turns,
                    ["sketch_id"] = sketchId, ["before"] = before.DeepClone(), ["revision_before"] = beforeRevision };
                rows.Add(row); string? transfer = null; byte[]? bytes = null;
                try
                {
                    string source = Path.GetFullPath(Path.Combine(directory, $"fixture-{bits}-{topDown}-{turns}.bmp"));
                    if (Path.GetDirectoryName(source) != directory) throw new InvalidOperationException("Image history scratch path escaped.");
                    BmpProbeWriteFile(source, bits, topDown, created); bytes = File.ReadAllBytes(source);
                    string hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), asset = "a_" + hash;
                    int byteLength = bytes.Length;
                    row["source_sha256"] = hash; row["source_byte_length"] = byteLength;
                    JObject Payload(SketchImage nativeImage)
                    {
                        const string setName = "InventorSO_SketchImagePayload";
                        if (!nativeImage.AttributeSets.NameIsUsed[setName]) throw new InvalidOperationException("Native image payload attributes missing.");
                        var set = nativeImage.AttributeSets[setName];
                        if (set.Count != 8) throw new InvalidOperationException("Native image payload field cardinality differs.");
                        foreach (string field in new[] { "schema", "bytes", "sha256", "asset_id", "byte_length", "format", "pixel_width", "pixel_height" })
                            if (!set.NameIsUsed[field]) throw new InvalidOperationException("Native image payload field missing: " + field);
                        if (set["bytes"].Value is not byte[] payload) throw new InvalidOperationException("Native image payload is not a byte array.");
                        string payloadHash = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
                        if (Convert.ToInt32(set["schema"].Value) != 1 || payload.Length != byteLength || payloadHash != hash
                            || (string)set["sha256"].Value != hash || (string)set["asset_id"].Value != asset
                            || Convert.ToInt32(set["byte_length"].Value) != byteLength || (string)set["format"].Value != "bmp"
                            || Convert.ToInt32(set["pixel_width"].Value) != 3 || Convert.ToInt32(set["pixel_height"].Value) != 2)
                            throw new InvalidOperationException("Persisted native image payload metadata/hash differs from sealed source.");
                        return new JObject { ["schema"] = 1, ["byte_length"] = payload.Length, ["sha256"] = payloadHash,
                            ["asset_id"] = asset, ["format"] = "bmp", ["pixel_width"] = 3, ["pixel_height"] = 2 };
                    }
                    var begin = StageArgs("begin"); begin["byte_length"] = bytes.Length;
                    transfer = (string)Call("stage_sketch_import", begin)["transfer_id"]!;
                    var chunk = StageArgs("chunk"); chunk["transfer_id"] = transfer; chunk["offset"] = 0;
                    chunk["data_base64"] = Convert.ToBase64String(bytes); Call("stage_sketch_import", chunk);
                    var seal = StageArgs("seal"); seal["transfer_id"] = transfer; seal["sha256"] = hash;
                    if ((bool?)Call("stage_sketch_import", seal)["sealed"] != true) throw new InvalidOperationException("Image history staging not sealed.");
                    File.Delete(source); row["original_source_deleted_before_commit"] = !File.Exists(source);
                    if (File.Exists(source)) throw new InvalidOperationException("Original image source still exists before commit.");
                    var batch = State(); batch["preview"] = false; batch["history_owner"] = owner;
                    batch["operations"] = new JArray(new JObject { ["command"] = "edit_sketch", ["arguments"] = new JObject {
                        ["sketch_id"] = sketchId, ["variant"] = "insert_image", ["options"] = new JObject {
                            ["asset_id"] = asset, ["position_mm"] = new JArray(13, 21), ["width_mm"] = 30,
                            ["quarter_turns"] = turns, ["link"] = false } } });
                    batch["import_attachments"] = new JObject { ["owner"] = owner, ["session"] = session,
                        ["resources"] = new JArray(new JObject { ["asset_id"] = asset, ["transfer_id"] = transfer,
                            ["sha256"] = hash, ["byte_length"] = bytes.Length, ["format"] = "bmp", ["metadata"] = new JObject {
                                ["kind"] = "Image", ["dependencies"] = new JArray(), ["image"] = new JObject {
                                    ["pixel_width"] = 3, ["pixel_height"] = 2 } } }) };
                    var priorPrivateSources = Directory.GetDirectories(Path.GetTempPath(), "inventor-so-image-*").ToHashSet(StringComparer.OrdinalIgnoreCase);
                    row["phase"] = "commit"; var commit = Call("atomic_batch", batch); row["commit"] = commit;
                    var remainingPrivateSources = Directory.GetDirectories(Path.GetTempPath(), "inventor-so-image-*")
                        .Where(path => !priorPrivateSources.Contains(path)).ToArray();
                    row["private_sources_after_commit"] = new JArray(remainingPrivateSources);
                    if (remainingPrivateSources.Length != 0) throw new InvalidOperationException("Commit retained a private image source directory before history.");
                    string ticket = (string?)commit["history_ticket"] ?? throw new InvalidOperationException("Image commit has no history ticket.");
                    if ((string?)commit["status"] != "committed" || journal.Revision(documentId) == beforeRevision)
                        throw new InvalidOperationException("Image history commit did not advance revision.");
                    var discard = StageArgs("discard"); discard["transfer_id"] = transfer;
                    row["staging_cleanup_before_history"] = Call("stage_sketch_import", discard); transfer = null;
                    if (staging.ReservedBytes != 0 || staging.ResidentTransfers != 0) throw new InvalidOperationException("History still has staged image bytes.");
                    Array.Clear(bytes, 0, bytes.Length); bytes = null;
                    var committed = Snapshot(); row["after_commit"] = committed.DeepClone();
                    if (sketch.SketchImages.Count != 1 || (int)committed["image_count"]! != (int)before["image_count"]! + 1
                        || (int)committed["ole_count"]! != (int)before["ole_count"]! + 1)
                        throw new InvalidOperationException("Image history commit cardinality differs.");
                    var prior = (JArray)before["sketches"]!; var current = (JArray)committed["sketches"]!;
                    for (int n = 0; n < prior.Count - 1; n++)
                        if (!JToken.DeepEquals(prior[n], current[n])) throw new InvalidOperationException("Image commit changed prior sketch baseline.");
                    foreach (var ole in (JArray)before["ole"]!)
                        if (!((JArray)committed["ole"]!).Any(v => JToken.DeepEquals(v, ole))) throw new InvalidOperationException("Image commit changed prior OLE baseline.");
                    var image = sketch.SketchImages[1];
                    row["payload_after_commit"] = Payload(image);
                    if (image.LinkedToFile || !TopologySame(image.Parent, sketch)) throw new InvalidOperationException("History image is not embedded in selected sketch.");
                    ImageNear45(image.Width * 10, 30); ImageNear45(image.Height * 10, 20);
                    var boundary = (JArray)BmpProbeImageReadback(image)["boundary_mm"]!;
                    if (boundary.Count != 4) throw new InvalidOperationException("History image boundary cardinality differs.");
                    var xs = boundary.OfType<JArray>().SelectMany(e => new[] { (double)e[0], (double)e[2] }).ToArray();
                    var ys = boundary.OfType<JArray>().SelectMany(e => new[] { (double)e[1], (double)e[3] }).ToArray();
                    ImageNear45(xs.Max() - xs.Min(), turns % 2 == 0 ? 30 : 20);
                    ImageNear45(ys.Max() - ys.Min(), turns % 2 == 0 ? 20 : 30);
                    void Refusal(JObject args, string code, string label)
                    {
                        string revision = journal.Revision(documentId); var state = Snapshot();
                        var result = commands["history_xr"].Execute(context, args); row[label] = JObject.FromObject(result);
                        if (result.Ok || result.Error?.Code != code || journal.Revision(documentId) != revision || !JToken.DeepEquals(state, Snapshot()))
                            throw new InvalidOperationException("History refusal mutated native state or returned unexpected code: " + label);
                    }
                    row["phase"] = "history refusals";
                    var foreign = HistoryArgs("undo", ticket); foreign["owner"] = owner + "-foreign";
                    Refusal(foreign, "HISTORY_CHANGED", "foreign_owner_refusal");
                    var stale = HistoryArgs("undo", ticket); stale["expected_revision"] = beforeRevision;
                    Refusal(stale, InventorErrorCodes.STALE_REVISION, "stale_revision_refusal");
                    var status = Call("history_xr", HistoryArgs("status")); row["status_before_undo"] = status;
                    if ((bool?)status["can_undo"] != true || (string?)status["ticket"] != ticket)
                        throw new InvalidOperationException("Image history does not offer owned Undo.");
                    row["phase"] = "undo"; var undone = Call("history_xr", HistoryArgs("undo", ticket)); row["undo"] = undone;
                    var afterUndo = Snapshot(); row["after_undo"] = afterUndo.DeepClone();
                    row["exact_undo_baseline"] = JToken.DeepEquals(before, afterUndo);
                    var modelBefore = (JObject)before.DeepClone(); modelBefore.Remove("ole"); modelBefore.Remove("ole_count");
                    var modelUndo = (JObject)afterUndo.DeepClone(); modelUndo.Remove("ole"); modelUndo.Remove("ole_count");
                    row["exact_undo_model_baseline"] = JToken.DeepEquals(modelBefore, modelUndo);
                    row["undo_retained_commit_ole"] = JToken.DeepEquals(committed["ole"], afterUndo["ole"]);
                    if (!(bool)row["exact_undo_model_baseline"]! || !(bool)row["undo_retained_commit_ole"]!)
                        throw new InvalidOperationException("Undo changed model baseline or retained unexpected OLE identities; no repair attempted.");
                    if ((bool?)undone["can_redo"] != true || (string?)undone["ticket"] == ticket || (string?)undone["revision"] == (string?)commit["revision"])
                        throw new InvalidOperationException("Undo did not rotate revision/ticket and offer Redo.");
                    row["phase"] = "redo"; var redone = Call("history_xr", HistoryArgs("redo", (string?)undone["ticket"])); row["redo"] = redone;
                    var afterRedo = Snapshot(); row["after_redo"] = afterRedo.DeepClone();
                    row["payload_after_redo"] = Payload(sketch.SketchImages[1]);
                    row["exact_redo_committed_state"] = JToken.DeepEquals(committed, afterRedo);
                    if (!(bool)row["exact_redo_committed_state"]!) throw new InvalidOperationException("Native Redo changed image/OLE identity, geometry or baseline.");
                    if ((bool?)redone["can_undo"] != true || (string?)redone["ticket"] == (string?)undone["ticket"]
                        || (string?)redone["revision"] == (string?)undone["revision"])
                        throw new InvalidOperationException("Redo did not rotate revision/ticket and offer Undo.");
                    var cycles = new JArray(); row["repeated_history_cycles"] = cycles;
                    for (int cycle = 0; cycle < 2; cycle++)
                    {
                        var cycleUndo = Call("history_xr", HistoryArgs("undo", (string?)redone["ticket"]));
                        var cycleUndoState = Snapshot();
                        var cycleModel = (JObject)cycleUndoState.DeepClone(); cycleModel.Remove("ole"); cycleModel.Remove("ole_count");
                        if (!JToken.DeepEquals(modelBefore, cycleModel) || !JToken.DeepEquals(committed["ole"], cycleUndoState["ole"]))
                            throw new InvalidOperationException("Repeated Undo changed model or grew retained OLE.");
                        redone = Call("history_xr", HistoryArgs("redo", (string?)cycleUndo["ticket"]));
                        if (!JToken.DeepEquals(committed, Snapshot())) throw new InvalidOperationException("Repeated Redo changed committed state.");
                        cycles.Add(new JObject { ["model_undo_exact"] = true, ["ole_growth"] = 0,
                            ["redo_exact"] = true, ["payload"] = Payload(sketch.SketchImages[1]) });
                    }
                    var finalStatus = Call("history_xr", HistoryArgs("status")); row["status_after_redo"] = finalStatus;
                    if ((bool?)finalStatus["can_undo"] != true || (bool?)finalStatus["can_redo"] != false
                        || (string?)finalStatus["ticket"] != (string?)redone["ticket"] || (string?)finalStatus["revision"] != journal.Revision(documentId))
                        throw new InvalidOperationException("Final image history status differs from Redo receipt.");
                    row["ole_undo_assessment"] = "Exact OLE Undo baseline failed; native history retains commit descriptors. No deletion attempted. Model Undo and complete Redo verified separately.";
                    row["outcome"] = "PASS native model history and payload assertions; exact OLE Undo remains unresolved";
                }
                catch (Exception ex) { row["outcome"] = "FAIL"; row["error"] = ex.ToString(); throw; }
                finally
                {
                    if (bytes != null) Array.Clear(bytes, 0, bytes.Length);
                    try
                    {
                        if (transfer != null) { var discard = StageArgs("discard"); discard["transfer_id"] = transfer; row["staging_cleanup"] = Call("stage_sketch_import", discard); }
                        row["staging_reserved_bytes_after"] = staging.ReservedBytes;
                        if (staging.ReservedBytes != 0 || staging.ResidentTransfers != 0) throw new InvalidOperationException("Image history leaked staging resources.");
                    }
                    catch (Exception ex) { row["outcome"] = "FAIL"; row["staging_cleanup_error"] = ex.ToString(); throw; }
                }
            }
            report["final_state"] = Snapshot();
        }
        finally
        {
            report["case_count"] = rows.Count;
            report["completed_cases"] = rows.OfType<JObject>().Count(r => ((string?)r["outcome"] ?? "").StartsWith("PASS native model history", StringComparison.Ordinal));
            report["failed_cases"] = rows.OfType<JObject>().Count(r => (string?)r["outcome"] == "FAIL");
            report["exact_undo_count"] = rows.OfType<JObject>().Count(r => (bool?)r["exact_undo_baseline"] == true);
            report["exact_model_undo_count"] = rows.OfType<JObject>().Count(r => (bool?)r["exact_undo_model_baseline"] == true);
            report["retained_ole_undo_count"] = rows.OfType<JObject>().Count(r => (bool?)r["undo_retained_commit_ole"] == true);
            report["history_assessment"] = "PARTIAL: model Undo and complete Redo verified separately; exact OLE Undo baseline remains failed.";
            report["exact_redo_count"] = rows.OfType<JObject>().Count(r => (bool?)r["exact_redo_committed_state"] == true);
            report["not_run_cases"] = 16 - rows.Count;
            var errors = new JArray();
            foreach (string path in created)
                try { File.Delete(path); } catch (Exception ex) { errors.Add(new JObject { ["file"] = path, ["error"] = ex.ToString() }); }
            try { Directory.Delete(directory, false); } catch (Exception ex) { errors.Add(new JObject { ["directory"] = directory, ["error"] = ex.ToString() }); }
            report["temp_cleanup_errors"] = errors; report["temp_cleanup_complete"] = errors.Count == 0;
            report["fixture_cleanup"] = "Root isolated wrapper closes only owned fixture/process; no image or OLE history repair performed by probe.";
            if (errors.Count != 0) throw new InvalidOperationException("Owned image history scratch cleanup failed.");
        }
    }
}
