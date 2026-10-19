using System.Buffers.Binary;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using Bimwright.Ipt.Shared.Contracts;
using Inventor;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using File = System.IO.File;
using Path = System.IO.Path;
using Environment = System.Environment;

internal static partial class Program
{
    private static void RunImagePixelsInProcess50(Inventor.Application app, PartDocument doc, JObject report, bool verifyFrame = false)
    {
        if (app.Documents.Count != 1 || doc.ComponentDefinition.Sketches.Count != 1 || !TopologySame(app.ActiveDocument, doc)
            || doc.DisplayName != "S1_ArcInput_Oracle.ipt" || !string.IsNullOrEmpty(doc.FullFileName))
            throw new InvalidOperationException("Exact single owned unsaved pixel research fixture required.");
        var sketch = doc.ComponentDefinition.Sketches[1];
        if (sketch.SketchEntities.Count != 0 || doc.ReferencedOLEFileDescriptors.Count != 0) throw new InvalidOperationException("Pixel research baseline must be empty.");
        if (app.ActiveEditObject is PlanarSketch editing) editing.ExitEdit();
        if (GetWindowThreadProcessId(new IntPtr(unchecked((long)(uint)app.MainFrameHWND)), out uint pid) == 0 || pid == 0)
            throw new InvalidOperationException("Cannot identify owned native pixel process.");
        string descriptorPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "InventorSO", "inventor-so-mcp", $"inventor-2027-{pid}.json");
        TargetDescriptor? target = null; var wait = System.Diagnostics.Stopwatch.StartNew();
        while (wait.Elapsed < TimeSpan.FromSeconds(30))
        {
            if (File.Exists(descriptorPath))
            {
                target = JsonConvert.DeserializeObject<TargetDescriptor>(File.ReadAllText(descriptorPath));
                if (target?.ProcessId == pid && target.Transport == "pipe" && !string.IsNullOrEmpty(target.AuthToken)) break;
            }
            Thread.Sleep(100);
        }
        if (target?.ProcessId != pid || target.Transport != "pipe" || string.IsNullOrEmpty(target.AuthToken))
            throw new InvalidOperationException("Owned process did not publish an authenticated pipe descriptor.");
        // The descriptor and authentication token never enter the evidence report.
        JObject Call(string command, JObject args)
        {
            using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var pipe = new NamedPipeClientStream(".", target.PipeName!, PipeDirection.InOut, PipeOptions.Asynchronous);
            pipe.ConnectAsync(cancel.Token).GetAwaiter().GetResult();
            using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true) { AutoFlush = true };
            using var reader = new StreamReader(pipe, new UTF8Encoding(false), false, 4096, true);
            var request = new InventorCommandEnvelope { Id = Guid.NewGuid(), Command = command, Params = args,
                AuthToken = target.AuthToken, ReadOnly = true, TimeoutMs = 30000 };
            writer.WriteLine(JsonConvert.SerializeObject(request));
            string line = reader.ReadLineAsync(cancel.Token).GetAwaiter().GetResult() ?? throw new InvalidOperationException("Native pipe closed before reply.");
            var reply = JsonConvert.DeserializeObject<InventorCommandResult>(line)!;
            if (reply.Id != request.Id) throw new InvalidOperationException("Native pipe response id differs.");
            if (!reply.Ok) throw new InvalidOperationException("Native pixel query refused: " + reply.Error?.Code + " details=" + reply.Error?.Details?.ToString(Formatting.None));
            return (JObject)reply.Data!;
        }
        string documentId = "doc_" + doc.InternalName;
        string Reference(object entity, string kind)
        {
            byte[] key = Array.Empty<byte>(); ((dynamic)entity).GetReferenceKey(ref key, 0);
            return new PersistentEntityReference { DocumentId = documentId, EntityType = kind, Key = key, Context = Array.Empty<byte>() }.Encode();
        }
        string sketchId = Reference(sketch, "sketch");
        JObject NativeState() => new() { ["revision"] = doc.DatabaseRevisionId, ["dirty"] = doc.Dirty,
            ["images"] = new JArray(sketch.SketchImages.Cast<SketchImage>().Select(BmpProbeImageReadback)),
            ["entities"] = sketch.SketchEntities.Count, ["constraints"] = sketch.GeometricConstraints.Count,
            ["ole"] = new JArray(doc.ReferencedOLEFileDescriptors.Cast<ReferencedOLEFileDescriptor>().Select(d => TopologyKey(d))) };
        var baseline = NativeState(); var rows = new JArray(); report["pixel_cases"] = rows;
        report["kind"] = doc.ComponentDefinition is SheetMetalComponentDefinition ? "sheetmetal" : "part";
        report["evidence"] = "Authenticated pipe into the actual loaded add-in in owned Inventor; six generated fixture pixels only. Native bitmap transformation observations, no XR rendering proof.";
        report["intended_case_count"] = verifyFrame ? 32 : 20;
        if (verifyFrame)
        {
            report["frame_evidence"] = "Independent raw Position/Width/Height rectangle transformed by operation theory; current boundary-role frame checked with unchanged source bitmap colors. No XR or physical display proof.";
            report["save_copy_state"] = "NOT COVERED: fixture is inside an owned active transaction; this probe does not save it.";
        }
        string directory = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "s1-pixels50-" + Guid.NewGuid().ToString("N")));
        if (Directory.Exists(directory) || File.Exists(directory)) throw new InvalidOperationException("Owned pixel scratch exists.");
        Directory.CreateDirectory(directory); var created = new List<string>();
        try
        {
            string[] operations = verifyFrame
                ? new[] { "raw", "mirror_horizontal", "mirror_vertical", "rotate_left", "geometry_rotate37", "mirror_hv", "rotate_left_twice", "geometry37_mirrorH" }
                : new[] { "raw", "mirror_horizontal", "mirror_vertical", "rotate_left", "geometry_rotate37" };
            foreach (int bits in new[] { 24, 32 }) foreach (bool topDown in new[] { false, true })
            foreach (string operation in operations)
            {
                if (app.Documents.Count != 1 || !TopologySame(app.ActiveDocument, doc) || sketch.SketchImages.Count != 0 || doc.ReferencedOLEFileDescriptors.Count != 0)
                    throw new InvalidOperationException("Owned pixel baseline changed before case.");
                string path = Path.Combine(directory, $"source-{rows.Count}.bmp"); BmpProbeWriteFile(path, bits, topDown, created);
                var row = new JObject { ["bits"] = bits, ["top_down"] = topDown, ["operation"] = operation }; rows.Add(row);
                var tx = app.TransactionManager.StartTransaction((_Document)doc, "S1 in-process pixels50 research");
                var ownedKeys = new HashSet<string>(StringComparer.Ordinal);
                try
                {
                    var image = sketch.SketchImages.Add(path, app.TransientGeometry.CreatePoint2d(1.3, 2.1), false);
                    double[][] rawCorners = verifyFrame ? RawImageCorners51(image) : Array.Empty<double[]>();
                    foreach (ReferencedOLEFileDescriptor ole in doc.ReferencedOLEFileDescriptors) ownedKeys.Add(TopologyKey(ole));
                    if (ownedKeys.Count != 1) throw new InvalidOperationException("Expected one isolated OLE descriptor.");
                    File.Delete(path); if (File.Exists(path)) throw new InvalidOperationException("Original source remains before pixel query.");
                    if (operation == "mirror_horizontal") image.MirrorHorizontal();
                    else if (operation == "mirror_vertical") image.MirrorVertical();
                    else if (operation == "rotate_left") image.RotateLeft();
                    else if (operation == "mirror_hv") { image.MirrorHorizontal(); image.MirrorVertical(); }
                    else if (operation == "rotate_left_twice") { image.RotateLeft(); image.RotateLeft(); }
                    else if (operation is "geometry_rotate37" or "geometry37_mirrorH")
                    { var collection = app.TransientObjects.CreateObjectCollection(); collection.Add(image);
                        sketch.RotateSketchObjects(collection, app.TransientGeometry.CreatePoint2d(1.3, 2.1), 37 * Math.PI / 180, false, false);
                        if (operation == "geometry37_mirrorH") image.MirrorHorizontal(); }
                    if (!doc.Update2(true)) throw new InvalidOperationException("Pixel research update failed.");
                    string imageId = Reference(image, "sketch_image");
                    var revision = Call("get_visual_revision", new JObject { ["document_id"] = documentId });
                    var state = NativeState();
                    var common = new JObject { ["document_id"] = documentId, ["expected_revision"] = revision["revision"],
                        ["sketch_id"] = sketchId, ["image_id"] = imageId, ["owner"] = "s1-native-pixels50",
                        ["session"] = Guid.NewGuid().ToString("N"), ["source_mode"] = "native_bitmap" };
                    string? transfer = null;
                    JObject Args(string action) { var p = (JObject)common.DeepClone(); p["action"] = action; if (transfer != null) p["transfer_id"] = transfer; return p; }
                    byte[]? bytes = null;
                    try
                    {
                        var begin = Call("get_sketch_image_payload", Args("begin")); transfer = (string)begin["transfer_id"]!; row["manifest"] = begin;
                        if (verifyFrame) VerifyLiveImageFrame51(begin, rawCorners, operation, image.Visible, row);
                        int length = (int)begin["byte_length"]!; if (length < 54 || length > 4096) throw new InvalidOperationException("Six-pixel research output budget exceeded.");
                        var request = Args("chunk"); request["offset"] = 0; request["count"] = length;
                        var chunk = Call("get_sketch_image_payload", request); bytes = Convert.FromBase64String((string)chunk["data_base64"]!);
                        if (bytes.Length != length || Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() != (string?)begin["sha256"])
                            throw new InvalidOperationException("Native pixel wire length/hash mismatch.");
                        int width = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(18, 4));
                        int height = -BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(22, 4));
                        if (width * height != 6 || height <= 0 || BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(28, 2)) != 32)
                            throw new InvalidOperationException("Native pixel output dimensions/layout differ.");
                        var colors = new JArray(); for (int i = 54; i < bytes.Length; i += 4) colors.Add(new JArray(bytes[i], bytes[i + 1], bytes[i + 2]));
                        row["top_down_fixture_bgr"] = colors; row["native_geometry"] = BmpProbeImageReadback(image);
                        if (operation == "raw" || verifyFrame)
                        {
                            var expected = new JArray(); for (int y = 0; y < 2; y++) for (int x = 0; x < 3; x++)
                            { int sourceRow = topDown ? y : 1 - y; expected.Add(new JArray(sourceRow == 0 ? 255 : 0, x * 100, sourceRow == 1 ? 255 : 0)); }
                            bool exact = width == 3 && height == 2 && JToken.DeepEquals(colors, expected);
                            if (operation == "raw") row["raw_colors_exact"] = exact;
                            if (verifyFrame) row["bitmap_colors_exact"] = exact;
                            if (!exact) throw new InvalidOperationException("Native bitmap colors differ from the unchanged source fixture.");
                        }
                        Call("get_sketch_image_payload", Args("discard")); transfer = null;
                        if (!JToken.DeepEquals(state, NativeState())) throw new InvalidOperationException("Native pixel read modified CAD state.");
                        var after = Call("get_visual_revision", new JObject { ["document_id"] = documentId });
                        if (!JToken.DeepEquals(revision, after)) throw new InvalidOperationException("Native pixel read changed backend revision.");
                        row["read_only_exact"] = true; row["outcome"] = "OBSERVATION";
                    }
                    finally { if (bytes != null) Array.Clear(bytes, 0, bytes.Length); if (transfer != null) Call("get_sketch_image_payload", Args("discard")); }
                }
                catch (Exception error) { row["outcome"] = "FAIL"; row["error"] = error.ToString(); throw; }
                finally
                {
                    if (!TopologySame(app.TransactionManager.CurrentTransaction, tx)) throw new InvalidOperationException("Owned pixel transaction changed.");
                    tx.Abort();
                    var remove = new List<ReferencedOLEFileDescriptor>();
                    foreach (ReferencedOLEFileDescriptor ole in doc.ReferencedOLEFileDescriptors)
                    {
                        if (!ownedKeys.Contains(TopologyKey(ole)) || (!string.IsNullOrEmpty(ole.FullFileName)
                            && !string.Equals(Path.GetFullPath(ole.FullFileName), path, StringComparison.OrdinalIgnoreCase)))
                            throw new InvalidOperationException("Pixel repair encountered an OLE descriptor outside exact ownership.");
                        remove.Add(ole);
                    }
                    foreach (var ole in remove) ole.Delete();
                    if (sketch.SketchImages.Count != 0 || sketch.SketchEntities.Count != 0 || doc.ReferencedOLEFileDescriptors.Count != 0)
                        throw new InvalidOperationException("Pixel research did not restore empty native baseline.");
                    row["empty_restoration"] = true;
                }
            }
        }
        finally
        {
            report["case_count"] = rows.Count; report["completed_observations"] = rows.OfType<JObject>().Count(r => (string?)r["outcome"] == "OBSERVATION");
            report["failed_cases"] = rows.OfType<JObject>().Count(r => (string?)r["outcome"] == "FAIL");
            if (verifyFrame)
            {
                report["frame_theory_match_count"] = rows.OfType<JObject>().Count(r => (bool?)r["frame_theory_match"] == true);
                report["bitmap_colors_exact_count"] = rows.OfType<JObject>().Count(r => (bool?)r["bitmap_colors_exact"] == true);
            }
            foreach (string path in created) { if (Path.GetDirectoryName(Path.GetFullPath(path)) != directory) throw new InvalidOperationException("Pixel cleanup escaped scratch."); File.Delete(path); }
            Directory.Delete(directory, false); report["temp_cleanup_complete"] = true;
        }
    }
}
