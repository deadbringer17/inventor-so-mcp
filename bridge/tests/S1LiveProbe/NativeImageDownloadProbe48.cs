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
    private static void RunImageDownloadProbe48(Inventor.Application app, PartDocument doc, JObject report, bool verifyFrame = false)
    {
        var sketches = doc.ComponentDefinition.Sketches;
        void Guard()
        {
            if (app.Documents.Count != 1 || app.ActiveDocument is not PartDocument active || !TopologySame(active, doc)
                || doc.DisplayName != "S1_ArcInput_Oracle.ipt" || !string.IsNullOrEmpty(doc.FullFileName) || sketches.Count < 1)
                throw new InvalidOperationException("Exact single unsaved owned image download fixture required.");
        }
        Guard(); var original = sketches[1];
        if (sketches.Count != 1 || original.SketchEntities.Count != 0 || original.SketchImages.Count != 0
            || original.GeometricConstraints.Count != 0 || original.DimensionConstraints.Count != 0
            || original.TextBoxes.Count != 0 || doc.ReferencedOLEFileDescriptors.Count != 0)
            throw new InvalidOperationException("Image download fixture must begin empty, including OLE.");
        if (app.ActiveEditObject is PlanarSketch editing)
        { if (!TopologySame(editing, original)) throw new InvalidOperationException("Another sketch is being edited."); editing.ExitEdit(); }
        else if (app.ActiveEditObject is object edit && !TopologySame(edit, doc) && !TopologySame(edit, doc.ComponentDefinition))
            throw new InvalidOperationException("Another native edit context is active.");
        var trackerType = typeof(InventorCommandRegistry).Assembly.GetType("Bimwright.Ipt.Shared.Plugin.CadEventTracker", true)!;
        using var tracker = (IDisposable)Activator.CreateInstance(trackerType, app)!;
        var journal = (CadEventJournal)trackerType.GetProperty("Journal")!.GetValue(tracker)!;
        var policy = new SketchImportStagingPolicy(1024 * 1024, 2 * 1024 * 1024, 4, 192 * 1024, TimeSpan.FromMinutes(10), 128);
        using var staging = new SketchImportStagingStore(policy);
        using var downloads = new SketchImportStagingStore(policy);
        var commands = InventorCommandRegistry.Build(new PluginOptions(2027, false, false, 0));
        var ctx = new InventorCommandContext { Application = app, Events = journal, Commands = commands,
            AllowExperimental = true, InventorYear = 2027, SketchImports = staging, SketchImageDownloads = downloads };
        string documentId = "doc_" + doc.InternalName;
        const string owner = "s1-native-image-download48";
        string session = Guid.NewGuid().ToString("N");
        JObject State() => new() { ["document_id"] = documentId, ["expected_revision"] = journal.Revision(documentId) };
        JObject Stage(string action) => new() { ["action"] = action, ["owner"] = owner, ["session"] = session };
        JObject Call(string command, JObject args)
        {
            var result = commands[command].Execute(ctx, args);
            if (!result.Ok) throw new InvalidOperationException(command + ": " + result.Error?.Code + " " + result.Error?.Message);
            return (JObject)result.Data!;
        }
        string Reference(object native, string kind)
        {
            byte[] key = Array.Empty<byte>(); ((dynamic)native).GetReferenceKey(ref key, 0);
            return new PersistentEntityReference { DocumentId = documentId, EntityType = kind, Key = key, Context = Array.Empty<byte>() }.Encode();
        }
        JObject Snapshot()
        {
            Guard();
            return new JObject { ["document_id"] = documentId, ["revision"] = journal.Revision(documentId),
                ["database_revision"] = doc.DatabaseRevisionId, ["dirty"] = doc.Dirty,
                ["sketches"] = new JArray(sketches.Cast<PlanarSketch>().Select(s => new JObject {
                    ["key"] = TopologyKey(s), ["entities"] = s.SketchEntities.Count, ["constraints"] = s.GeometricConstraints.Count,
                    ["dimensions"] = s.DimensionConstraints.Count, ["textboxes"] = s.TextBoxes.Count,
                    ["images"] = new JArray(s.SketchImages.Cast<SketchImage>().Select(BmpProbeImageReadback)) })),
                ["ole"] = new JArray(doc.ReferencedOLEFileDescriptors.Cast<ReferencedOLEFileDescriptor>().Select(d => new JObject {
                    ["key"] = TopologyKey(d), ["filename"] = d.FullFileName })) };
        }
        var rows = new JArray(); report["image_download_cases"] = rows; report["intended_case_count"] = 16;
        report["kind"] = doc.ComponentDefinition is SheetMetalComponentDefinition ? "sheetmetal" : "part";
        report["evidence"] = "Native staged atomic embedded BMP followed by get_sketch_image_payload begin/chunk/discard after original/private sources and upload staging are gone. Byte reconstruction/hash and native read-only invariants; no HTTP, rendering, desktop, linked or Quest evidence.";
        report["not_covered"] = new JArray("Expiry and disposed download-store behavior require pure store tests; no wall-clock sleeps in native matrix.");
        string directory = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "s1-image-download48-" + Guid.NewGuid().ToString("N")));
        if (Directory.Exists(directory) || File.Exists(directory)) throw new InvalidOperationException("Scratch directory already exists.");
        Directory.CreateDirectory(directory); var created = new List<string>();
        try
        {
            foreach (int bits in new[] { 24, 32 })
            foreach (bool topDown in new[] { false, true })
            foreach (int turns in new[] { 0, 1, 2, 3 })
            {
                Guard(); var sketch = sketches.Add(doc.ComponentDefinition.WorkPlanes[3]); sketch.Name = "S1_ImageDownload48_" + sketches.Count;
                string sketchId = Reference(sketch, "sketch");
                var row = new JObject { ["bits_per_pixel"] = bits, ["top_down"] = topDown, ["quarter_turns"] = turns };
                rows.Add(row); string? upload = null, download = null; byte[]? bytes = null; JObject? request = null;
                try
                {
                    string source = Path.GetFullPath(Path.Combine(directory, $"source-{bits}-{topDown}-{turns}.bmp"));
                    if (Path.GetDirectoryName(source) != directory) throw new InvalidOperationException("Scratch source escaped directory.");
                    BmpProbeWriteFile(source, bits, topDown, created); bytes = File.ReadAllBytes(source);
                    int length = bytes.Length; string hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), asset = "a_" + hash;
                    row["source_sha256"] = hash; row["source_byte_length"] = length;
                    var beginUpload = Stage("begin"); beginUpload["byte_length"] = length;
                    upload = (string)Call("stage_sketch_import", beginUpload)["transfer_id"]!;
                    var chunkUpload = Stage("chunk"); chunkUpload["transfer_id"] = upload; chunkUpload["offset"] = 0;
                    chunkUpload["data_base64"] = Convert.ToBase64String(bytes); Call("stage_sketch_import", chunkUpload);
                    var seal = Stage("seal"); seal["transfer_id"] = upload; seal["sha256"] = hash;
                    if ((bool?)Call("stage_sketch_import", seal)["sealed"] != true) throw new InvalidOperationException("Upload not sealed.");
                    File.Delete(source); if (File.Exists(source)) throw new InvalidOperationException("Original source remains before commit.");
                    row["original_source_deleted_before_commit"] = true;
                    var batch = State(); batch["preview"] = false; batch["history_owner"] = owner;
                    batch["operations"] = new JArray(new JObject { ["command"] = "edit_sketch", ["arguments"] = new JObject {
                        ["sketch_id"] = sketchId, ["variant"] = "insert_image", ["options"] = new JObject {
                            ["asset_id"] = asset, ["position_mm"] = new JArray(13, 21), ["width_mm"] = 30, ["quarter_turns"] = turns, ["link"] = false } } });
                    batch["import_attachments"] = new JObject { ["owner"] = owner, ["session"] = session,
                        ["resources"] = new JArray(new JObject { ["asset_id"] = asset, ["transfer_id"] = upload, ["sha256"] = hash,
                            ["byte_length"] = length, ["format"] = "bmp", ["metadata"] = new JObject { ["kind"] = "Image",
                                ["dependencies"] = new JArray(), ["image"] = new JObject { ["pixel_width"] = 3, ["pixel_height"] = 2 } } }) };
                    var privateBefore = Directory.GetDirectories(Path.GetTempPath(), "inventor-so-image-*").ToHashSet(StringComparer.OrdinalIgnoreCase);
                    row["commit"] = Call("atomic_batch", batch);
                    if ((string?)row["commit"]!["status"] != "committed" || sketch.SketchImages.Count != 1) throw new InvalidOperationException("Image was not committed.");
                    var remaining = Directory.GetDirectories(Path.GetTempPath(), "inventor-so-image-*").Where(p => !privateBefore.Contains(p)).ToArray();
                    row["private_sources_after_commit"] = new JArray(remaining);
                    if (remaining.Length != 0) throw new InvalidOperationException("Private image source remains before download.");
                    var discardUpload = Stage("discard"); discardUpload["transfer_id"] = upload; Call("stage_sketch_import", discardUpload); upload = null;
                    if (staging.ReservedBytes != 0 || staging.ResidentTransfers != 0) throw new InvalidOperationException("Upload staging remains before download.");
                    row["upload_staging_empty_before_download"] = true;
                    Array.Clear(bytes, 0, bytes.Length); bytes = null;
                    var readerArgs = State(); readerArgs["sketch_id"] = sketchId;
                    var reader = commands["get_sketch_editor_xr"].Execute(ctx, readerArgs);
                    row["sketch_editor_after_commit"] = JObject.FromObject(reader);
                    if (verifyFrame) VerifyImageFrame49(app, doc, sketch, row, turns, hash, length, reader.Data as JObject);
                    row["sketch_editor_assessment"] = "Diagnostic snapshot only: unsupported image/native type or internal frame constraints do not establish image rendering support and do not fail the independent payload download assertions.";
                    request = State(); request["sketch_id"] = sketchId; request["image_id"] = Reference(sketch.SketchImages[1], "sketch_image");
                    request["owner"] = owner; request["session"] = session;
                    JObject Args(string action) { var a = (JObject)request.DeepClone(); a["action"] = action; if (download != null) a["transfer_id"] = download; return a; }
                    var baseline = Snapshot(); row["before_download"] = baseline.DeepClone();
                    void Unchanged()
                    { if (!JToken.DeepEquals(baseline, Snapshot())) throw new InvalidOperationException("Image payload read/refusal mutated native state."); }
                    var begin = Call("get_sketch_image_payload", Args("begin"));
                    download = (string?)begin["transfer_id"] ?? throw new InvalidOperationException("Download transfer identity missing.");
                    row["begin"] = begin.DeepClone();
                    if ((string?)begin["sha256"] != hash || (int?)begin["byte_length"] != length || (string?)begin["format"] != "bmp"
                        || (int?)begin["pixel_width"] != 3 || (int?)begin["pixel_height"] != 2 || (int?)begin["max_chunk_bytes"] != 192 * 1024
                        || begin["data_base64"] != null) throw new InvalidOperationException("Download manifest differs from source or includes bytes.");
                    Unchanged(); var refusals = new JArray(); row["refusals"] = refusals;
                    void Refuse(JObject a, string code, string label)
                    {
                        var result = commands["get_sketch_image_payload"].Execute(ctx, a);
                        refusals.Add(new JObject { ["label"] = label, ["ok"] = result.Ok, ["code"] = result.Error?.Code });
                        if (result.Ok || result.Error?.Code != code) throw new InvalidOperationException("Unexpected download refusal: " + label);
                        Unchanged();
                    }
                    JObject Chunk(int offset, int count) { var a = Args("chunk"); a["offset"] = offset; a["count"] = count; return a; }
                    var foreign = Chunk(0, 1); foreign["owner"] = owner + "-foreign"; Refuse(foreign, InventorErrorCodes.ASSET_NOT_FOUND, "foreign_owner");
                    foreign = Chunk(0, 1); foreign["session"] = session + "-foreign"; Refuse(foreign, InventorErrorCodes.ASSET_NOT_FOUND, "foreign_session");
                    var stale = Chunk(0, 1); stale["expected_revision"] = "stale-image-download-revision"; Refuse(stale, InventorErrorCodes.STALE_REVISION, "stale_revision");
                    var wrongRef = Chunk(0, 1); wrongRef["image_id"] = sketchId; Refuse(wrongRef, InventorErrorCodes.INVALID_ARGUMENT, "wrong_reference_type");
                    wrongRef = Chunk(0, 1); wrongRef["sketch_id"] = Reference(original, "sketch"); Refuse(wrongRef, InventorErrorCodes.INVALID_ARGUMENT, "foreign_image_parent");
                    Refuse(Chunk(0, 192 * 1024 + 1), InventorErrorCodes.INVALID_ARGUMENT, "oversized_count");
                    Refuse(Chunk(int.MaxValue, int.MaxValue), InventorErrorCodes.INVALID_ARGUMENT, "overflow_range");
                    Refuse(Chunk(-1, 1), InventorErrorCodes.INVALID_ARGUMENT, "negative_offset");
                    Refuse(Chunk(length, 1), InventorErrorCodes.INVALID_ARGUMENT, "past_end");
                    var reconstructed = new byte[length]; var chunks = new JArray(); row["chunks"] = chunks;
                    try
                    {
                        for (int offset = 0; offset < length;)
                        {
                            int count = Math.Min(17, length - offset); var part = Call("get_sketch_image_payload", Chunk(offset, count));
                            byte[] piece = Convert.FromBase64String((string?)part["data_base64"] ?? throw new InvalidOperationException("Chunk bytes missing."));
                            try
                            {
                                if ((string?)part["transfer_id"] != download || (int?)part["offset"] != offset || (int?)part["byte_length"] != length
                                    || (string?)part["sha256"] != hash || piece.Length != count) throw new InvalidOperationException("Chunk identity/range differs.");
                                Buffer.BlockCopy(piece, 0, reconstructed, offset, count);
                                chunks.Add(new JObject { ["offset"] = offset, ["count"] = count, ["sha256"] = Convert.ToHexString(SHA256.HashData(piece)).ToLowerInvariant() });
                            }
                            finally { Array.Clear(piece, 0, piece.Length); }
                            offset += count; Unchanged();
                        }
                        string actual = Convert.ToHexString(SHA256.HashData(reconstructed)).ToLowerInvariant();
                        row["reconstructed_sha256"] = actual;
                        if (actual != hash) throw new InvalidOperationException("Reconstructed download hash differs from original source.");
                    }
                    finally { Array.Clear(reconstructed, 0, reconstructed.Length); }
                    var discard = Args("discard");
                    if ((bool?)Call("get_sketch_image_payload", discard)["discarded"] != true) throw new InvalidOperationException("Download discard did not release transfer.");
                    Refuse(Chunk(0, 1), InventorErrorCodes.ASSET_NOT_FOUND, "discarded_transfer"); download = null;
                    if (downloads.ReservedBytes != 0 || downloads.ResidentTransfers != 0) throw new InvalidOperationException("Download store leaked bytes/transfer.");
                    Unchanged(); row["read_only_exact"] = true; row["outcome"] = "PASS";
                }
                catch (Exception ex) { row["outcome"] = "FAIL"; row["error"] = ex.ToString(); throw; }
                finally
                {
                    if (bytes != null) Array.Clear(bytes, 0, bytes.Length);
                    try
                    {
                        if (upload != null) { var a = Stage("discard"); a["transfer_id"] = upload; Call("stage_sketch_import", a); }
                        if (download != null && request != null) { var a = (JObject)request.DeepClone(); a["action"] = "discard"; a["transfer_id"] = download; Call("get_sketch_image_payload", a); }
                        row["upload_reserved_bytes_after"] = staging.ReservedBytes; row["download_reserved_bytes_after"] = downloads.ReservedBytes;
                        if (staging.ReservedBytes != 0 || staging.ResidentTransfers != 0 || downloads.ReservedBytes != 0 || downloads.ResidentTransfers != 0)
                            throw new InvalidOperationException("Image download case retained upload/download staging resources.");
                    }
                    catch (Exception ex) { row["outcome"] = "FAIL"; row["staging_cleanup_error"] = ex.ToString(); throw; }
                }
            }
        }
        finally
        {
            report["case_count"] = rows.Count; report["completed_cases"] = rows.OfType<JObject>().Count(r => (string?)r["outcome"] == "PASS");
            report["failed_cases"] = rows.OfType<JObject>().Count(r => (string?)r["outcome"] == "FAIL"); report["not_run_cases"] = 16 - rows.Count;
            var errors = new JArray();
            foreach (string path in created)
                try
                {
                    if (!string.Equals(Path.GetDirectoryName(Path.GetFullPath(path)), directory, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Scratch cleanup path escaped owned directory.");
                    File.Delete(path);
                }
                catch (Exception ex) { errors.Add(new JObject { ["file"] = path, ["error"] = ex.ToString() }); }
            if (errors.Count == 0) try { Directory.Delete(directory, false); } catch (Exception ex) { errors.Add(new JObject { ["directory"] = directory, ["error"] = ex.ToString() }); }
            report["temp_cleanup_errors"] = errors; report["temp_cleanup_complete"] = errors.Count == 0;
            if (errors.Count != 0) throw new InvalidOperationException("Owned image download scratch cleanup failed.");
        }
    }
}
