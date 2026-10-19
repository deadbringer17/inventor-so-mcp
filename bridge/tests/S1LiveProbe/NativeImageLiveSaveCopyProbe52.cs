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

internal static partial class Program
{
    private static void RunImageLiveSaveCopy52(Inventor.Application app, PartDocument doc, JObject report)
    {
        void GuardOriginal()
        {
            if (app.Documents.Count != 1 || !TopologySame(app.ActiveDocument, doc)
                || doc.DisplayName != "S1_ArcInput_Oracle.ipt" || !string.IsNullOrEmpty(doc.FullFileName)
                || doc.ComponentDefinition.Sketches.Count != 1)
                throw new InvalidOperationException("Exact single owned unsaved save-copy52 fixture required.");
        }
        GuardOriginal();
        var sketch = doc.ComponentDefinition.Sketches[1];
        if (sketch.SketchEntities.Count != 0 || sketch.SketchImages.Count != 0 || doc.ReferencedOLEFileDescriptors.Count != 0)
            throw new InvalidOperationException("Save-copy52 fixture must start empty.");
        if (app.ActiveEditObject is PlanarSketch editing) editing.ExitEdit();
        var pipe = new NativeImagePipe52(app);
        var rows = new JArray(); report["save_copy_cases"] = rows;
        report["kind"] = doc.ComponentDefinition is SheetMetalComponentDefinition ? "sheetmetal" : "part";
        report["intended_case_count"] = 3;
        report["evidence"] = "Actual loaded add-in native bitmap plus independent frame theory on legacy embedded images, committed outside research transaction, then SaveCopyAs and active reopen in the owned process. No XR/physical proof.";
        string directory = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "s1-live-savecopy52-" + Guid.NewGuid().ToString("N")));
        if (Directory.Exists(directory) || File.Exists(directory)) throw new InvalidOperationException("Owned save-copy52 scratch already exists.");
        Directory.CreateDirectory(directory);
        var created = new List<string>(); PartDocument? copy = null; string? copyPath = null;
        bool copyOwned = false, copyClosed = true;
        JObject OriginalState() => new() { ["id"] = doc.InternalName, ["path"] = doc.FullFileName,
            ["revision"] = doc.DatabaseRevisionId, ["dirty"] = doc.Dirty,
            ["images"] = new JArray(sketch.SketchImages.Cast<SketchImage>().Select(BmpProbeImageReadback)),
            ["ole"] = new JArray(doc.ReferencedOLEFileDescriptors.Cast<ReferencedOLEFileDescriptor>().Select(TopologyKey)) };
        try
        {
            foreach (string operation in new[] { "rotate_left", "mirror_horizontal", "geometry37_mirrorH" })
            {
                GuardOriginal();
                var row = new JObject { ["operation"] = operation, ["bits"] = 32, ["top_down"] = true };
                rows.Add(row);
                string source = Path.Combine(directory, "source-" + rows.Count + ".bmp");
                BmpProbeWriteFile(source, 32, true, created);
                var tx = app.TransactionManager.StartTransaction((_Document)doc, "S1 legacy image save-copy52 fixture");
                bool ended = false; SketchImage image; double[][] raw;
                try
                {
                    image = sketch.SketchImages.Add(source, app.TransientGeometry.CreatePoint2d(1.3, 2.1), false);
                    raw = RawImageCorners51(image);
                    if (operation == "rotate_left") image.RotateLeft();
                    else if (operation == "mirror_horizontal") image.MirrorHorizontal();
                    else
                    {
                        var objects = app.TransientObjects.CreateObjectCollection(); objects.Add(image);
                        sketch.RotateSketchObjects(objects, app.TransientGeometry.CreatePoint2d(1.3, 2.1), 37 * Math.PI / 180, false, false);
                        image.MirrorHorizontal();
                    }
                    if (!doc.Update2(true) || image.LinkedToFile || image.Transparent)
                        throw new InvalidOperationException("Legacy embedded image fixture update/state differs.");
                    if (!TopologySame(app.TransactionManager.CurrentTransaction, tx)) throw new InvalidOperationException("Owned fixture transaction changed.");
                    tx.End(); ended = true;
                }
                finally
                {
                    if (!ended)
                    {
                        if (!TopologySame(app.TransactionManager.CurrentTransaction, tx)) throw new InvalidOperationException("Cannot abort an unowned fixture transaction.");
                        tx.Abort();
                    }
                }
                if (TopologySame(app.TransactionManager.CurrentTransaction, tx)) throw new InvalidOperationException("Save-copy cannot run inside the owned research transaction.");
                File.Delete(source);
                if (File.Exists(source)) throw new InvalidOperationException("Legacy source remains before bitmap capture.");
                row["source_deleted_before_capture"] = true;
                row["owned_transaction_ended_before_save"] = true;
                JObject Read(PartDocument activeDoc, SketchImage nativeImage, JObject evidence)
                {
                    if (!TopologySame(app.ActiveDocument, activeDoc) || nativeImage.LinkedToFile
                        || nativeImage.AttributeSets.NameIsUsed["InventorSO_SketchImagePayload"]
                        || nativeImage.AttributeSets.NameIsUsed["InventorSO_SketchImageFrame"])
                        throw new InvalidOperationException("Save-copy52 requires active legacy image without managed attributes.");
                    bool duplicated = app.Documents.Cast<Document>().Count(candidate => candidate.InternalName == activeDoc.InternalName) > 1;
                    string id = DocumentIdComposer.Compose(activeDoc.InternalName, activeDoc.FullFileName, duplicated);
                    string Reference(object entity, string kind)
                    {
                        byte[] key = Array.Empty<byte>(); ((dynamic)entity).GetReferenceKey(ref key, 0);
                        return new PersistentEntityReference { DocumentId = id, EntityType = kind, Key = key, Context = Array.Empty<byte>() }.Encode();
                    }
                    var revision = pipe.Call("get_visual_revision", new JObject { ["document_id"] = id });
                    var common = new JObject { ["owner"] = "s1-live-savecopy52", ["session"] = Guid.NewGuid().ToString("N"),
                        ["document_id"] = id, ["expected_revision"] = revision["revision"],
                        ["sketch_id"] = Reference(nativeImage.Parent, "sketch"), ["image_id"] = Reference(nativeImage, "sketch_image"),
                        ["source_mode"] = "native_bitmap" };
                    string? transfer = null; byte[]? bytes = null;
                    JObject Args(string action)
                    { var p = (JObject)common.DeepClone(); p["action"] = action; if (transfer != null) p["transfer_id"] = transfer; return p; }
                    try
                    {
                        var begin = pipe.Call("get_sketch_image_payload", Args("begin")); transfer = (string)begin["transfer_id"]!;
                        VerifyLiveImageFrame51(begin, raw, operation, nativeImage.Visible, evidence);
                        int length = (int)begin["byte_length"]!;
                        if (length != 78) throw new InvalidOperationException("Save-copy52 bitmap is not the six-pixel bounded fixture.");
                        var chunkArgs = Args("chunk"); chunkArgs["offset"] = 0; chunkArgs["count"] = length;
                        var chunk = pipe.Call("get_sketch_image_payload", chunkArgs);
                        bytes = Convert.FromBase64String((string)chunk["data_base64"]!);
                        if (bytes.Length != length || Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() != (string?)begin["sha256"]
                            || BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(18, 4)) != 3
                            || BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(22, 4)) != -2)
                            throw new InvalidOperationException("Saved legacy bitmap layout/hash differs.");
                        var colors = new JArray(); var expected = new JArray();
                        for (int i = 54; i < bytes.Length; i += 4) colors.Add(new JArray(bytes[i], bytes[i + 1], bytes[i + 2]));
                        for (int y = 0; y < 2; y++) for (int x = 0; x < 3; x++) expected.Add(new JArray(y == 0 ? 255 : 0, x * 100, y == 1 ? 255 : 0));
                        if (!JToken.DeepEquals(colors, expected)) throw new InvalidOperationException("Saved legacy bitmap colors differ from generated source.");
                        evidence["bitmap_colors_exact"] = true;
                        evidence["manifest"] = begin.DeepClone();
                        if ((bool?)pipe.Call("get_sketch_image_payload", Args("discard"))["discarded"] != true)
                            throw new InvalidOperationException("Save-copy52 transfer cleanup failed.");
                        transfer = null;
                        if (!JToken.DeepEquals(revision, pipe.Call("get_visual_revision", new JObject { ["document_id"] = id })))
                            throw new InvalidOperationException("Save-copy52 read changed backend revision.");
                        evidence["read_revision_exact"] = true;
                        var signature = (JObject)begin.DeepClone(); signature.Remove("transfer_id"); return signature;
                    }
                    finally
                    {
                        if (bytes != null) Array.Clear(bytes, 0, bytes.Length);
                        if (transfer != null) pipe.Call("get_sketch_image_payload", Args("discard"));
                    }
                }
                var beforeEvidence = new JObject(); row["before_save"] = beforeEvidence;
                var signature = Read(doc, image, beforeEvidence);
                GuardOriginal(); var original = OriginalState(); row["original_before_save"] = original.DeepClone();
                copyPath = Path.GetFullPath(Path.Combine(directory, "copy-" + rows.Count + ".ipt"));
                if (Path.GetDirectoryName(copyPath) != directory || File.Exists(copyPath)) throw new InvalidOperationException("Save-copy52 destination is not exclusively owned.");
                created.Add(copyPath); doc.SaveAs(copyPath, true);
                if (!File.Exists(copyPath) || !JToken.DeepEquals(original, OriginalState())) throw new InvalidOperationException("Save-copy52 changed original or produced no file.");
                // Open(false) produces a hidden document which 2027 refuses to Activate (E_FAIL).
                // The read-only wire query intentionally requires this copy to be the active doc.
                copyClosed = false; copy = app.Documents.Open(copyPath, true) as PartDocument;
                copyOwned = copy != null && !TopologySame(copy, doc) && string.Equals(Path.GetFullPath(copy.FullFileName), copyPath, StringComparison.OrdinalIgnoreCase);
                if (!copyOwned || app.Documents.Count != 2 || copy!.ComponentDefinition.Sketches.Count != 1
                    || copy.ComponentDefinition.Sketches[1].SketchImages.Count != rows.Count)
                    throw new InvalidOperationException("Reopened save-copy52 ownership/cardinality differs.");
                copy.Activate(); var afterEvidence = new JObject(); row["after_reopen"] = afterEvidence;
                var copiedSignature = Read(copy, copy.ComponentDefinition.Sketches[1].SketchImages[rows.Count], afterEvidence);
                if (!JToken.DeepEquals(signature, copiedSignature)) throw new InvalidOperationException("Saved native bitmap/frame manifest differs after reopen.");
                copy.Close(true); copy = null; copyOwned = false; copyClosed = true; doc.Activate(); GuardOriginal();
                if (!JToken.DeepEquals(original, OriginalState())) throw new InvalidOperationException("Original fixture changed after copy closure.");
                row["bitmap_and_frame_persisted_exact"] = true; row["original_preserved_exact"] = true;
                row["copy_closed"] = true; row["outcome"] = "PASS";
            }
        }
        catch (Exception error) { if (rows.Last is JObject row) { row["outcome"] = "FAIL"; row["error"] = error.ToString(); } throw; }
        finally
        {
            var errors = new JArray();
            try
            {
                if (copy != null && copyOwned)
                {
                    if (TopologySame(copy, doc) || !string.Equals(Path.GetFullPath(copy.FullFileName), copyPath, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Copy ownership changed before cleanup.");
                    copy.Close(true); copy = null; copyClosed = true;
                }
                if (copy == null && copyClosed) { doc.Activate(); GuardOriginal(); }
            }
            catch (Exception error) { errors.Add(new JObject { ["document_cleanup_error"] = error.ToString() }); }
            if (copy == null && copyClosed && errors.Count == 0)
            {
                foreach (string path in created)
                {
                    if (!string.Equals(Path.GetDirectoryName(Path.GetFullPath(path)), directory, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Save-copy52 cleanup escaped owned scratch.");
                    try { File.Delete(path); } catch (Exception error) { errors.Add(new JObject { ["file_cleanup_error"] = error.ToString() }); }
                }
                if (errors.Count == 0) try { Directory.Delete(directory, false); } catch (Exception error) { errors.Add(new JObject { ["directory_cleanup_error"] = error.ToString() }); }
            }
            else errors.Add(new JObject { ["preserved_directory"] = directory, ["reason"] = "Copy ownership or closure unverified." });
            report["case_count"] = rows.Count; report["completed_cases"] = rows.OfType<JObject>().Count(row => (string?)row["outcome"] == "PASS");
            report["failed_cases"] = rows.OfType<JObject>().Count(row => (string?)row["outcome"] != "PASS");
            report["temp_cleanup_errors"] = errors; report["temp_cleanup_complete"] = errors.Count == 0;
            if (errors.Count != 0) throw new InvalidOperationException("Save-copy52 cleanup failed; unverified copies/files preserved.");
        }
    }

    private sealed class NativeImagePipe52
    {
        private readonly TargetDescriptor _target;
        internal NativeImagePipe52(Inventor.Application app)
        {
            if (GetWindowThreadProcessId(new IntPtr(unchecked((long)(uint)app.MainFrameHWND)), out uint pid) == 0 || pid == 0)
                throw new InvalidOperationException("Cannot identify owned native save-copy52 process.");
            string path = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                "InventorSO", "inventor-so-mcp", $"inventor-2027-{pid}.json");
            var wait = System.Diagnostics.Stopwatch.StartNew();
            while (wait.Elapsed < TimeSpan.FromSeconds(30))
            {
                if (File.Exists(path))
                {
                    var target = JsonConvert.DeserializeObject<TargetDescriptor>(File.ReadAllText(path));
                    if (target?.ProcessId == pid && target.Transport == "pipe" && !string.IsNullOrEmpty(target.AuthToken)) { _target = target; return; }
                }
                Thread.Sleep(100);
            }
            throw new InvalidOperationException("Owned save-copy52 add-in did not publish its authenticated pipe.");
        }
        internal JObject Call(string command, JObject args)
        {
            using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var pipe = new NamedPipeClientStream(".", _target.PipeName!, PipeDirection.InOut, PipeOptions.Asynchronous);
            pipe.ConnectAsync(cancel.Token).GetAwaiter().GetResult();
            using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true) { AutoFlush = true };
            using var reader = new StreamReader(pipe, new UTF8Encoding(false), false, 4096, true);
            var request = new InventorCommandEnvelope { Id = Guid.NewGuid(), Command = command, Params = args,
                AuthToken = _target.AuthToken, ReadOnly = true, TimeoutMs = 30000 };
            writer.WriteLine(JsonConvert.SerializeObject(request));
            string line = reader.ReadLineAsync(cancel.Token).GetAwaiter().GetResult() ?? throw new InvalidOperationException("Save-copy52 pipe closed before reply.");
            var reply = JsonConvert.DeserializeObject<InventorCommandResult>(line)!;
            if (reply.Id != request.Id || !reply.Ok) throw new InvalidOperationException("Save-copy52 native query refused: " + reply.Error?.Code
                + " details=" + reply.Error?.Details?.ToString(Formatting.None));
            return (JObject)reply.Data!;
        }
    }
}
