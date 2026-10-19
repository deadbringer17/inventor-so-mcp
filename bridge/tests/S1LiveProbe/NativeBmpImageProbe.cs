using System.Buffers.Binary;
using Inventor;
using Newtonsoft.Json.Linq;
using File = System.IO.File;
using Path = System.IO.Path;

internal static partial class Program
{
    // API research only. Offline signatures confirmed against installed interop 31.0.19200.0:
    // SketchImages.Add(string, Point2d, bool); Count; SketchImage.Delete(); Position/Width/Height
    // get+set; LinkedToFile; ReferencedFileDescriptor.FullFileName; RotateLeft()/RotateRight().
    // There is no numeric Rotation property; do not infer support for arbitrary rotation.
    // https://help.autodesk.com/cloudhelp/2027/ENU/Inventor-API/files/SketchImages_Add.htm
    // https://help.autodesk.com/cloudhelp/2027/ENU/Inventor-API/files/SketchImage.htm
    private static void RunBmpImageProbe(Inventor.Application app, PartDocument doc, JObject report)
    {
        report["evidence"] = "API RESEARCH BMP images on an owned temporary fixture; no Computer Use, desktop equivalence, product or Quest PASS.";
        if (doc.ComponentDefinition.Sketches.Count != 1)
            throw new InvalidOperationException("BMP research requires the single original owned sketch.");
        var sketch = doc.ComponentDefinition.Sketches[1];
        string originalName = sketch.Name;
        void Guard(bool empty)
        {
            if (app.Documents.Count != 1 || app.ActiveDocument is not PartDocument active || !TopologySame(active, doc)
                || doc.DisplayName != "S1_ArcInput_Oracle.ipt" || !string.IsNullOrEmpty(doc.FullFileName)
                || doc.DocumentType != DocumentTypeEnum.kPartDocumentObject || doc.ComponentDefinition.Sketches.Count != 1
                || !TopologySame(doc.ComponentDefinition.Sketches[1], sketch) || sketch.Name != originalName)
                throw new InvalidOperationException("Exact single unsaved owned arc fixture and original sketch required.");
            if (app.ActiveEditObject is object editing && !TopologySame(editing, sketch)
                && !TopologySame(editing, doc) && !TopologySame(editing, doc.ComponentDefinition))
                throw new InvalidOperationException("BMP research cannot change another edit context.");
            if (empty && (sketch.SketchEntities.Count != 0 || sketch.GeometricConstraints.Count != 0
                || sketch.DimensionConstraints.Count != 0 || sketch.TextBoxes.Count != 0 || sketch.SketchImages.Count != 0))
                throw new InvalidOperationException("BMP research requires the original completely empty sketch.");
        }
        Guard(true);
        // Preparation opens the owned sketch's edit command. Finish that empty
        // command before researching image insertion; never close another edit object.
        if (app.ActiveEditObject is PlanarSketch ownedEdit) ownedEdit.ExitEdit();
        Guard(true);
        var initialTransaction = app.TransactionManager.CurrentTransaction;
        report["initial_transaction"] = initialTransaction == null ? JValue.CreateNull() : new JObject
        {
            ["id"] = initialTransaction.Id, ["state"] = initialTransaction.State.ToString(),
            ["name"] = initialTransaction.DisplayName
        };
        // As in AtomicBatchHandler, Inventor exposes an unidentified transaction
        // even while idle. HasParentTransaction on our newly started transaction
        // is the authoritative nesting check; never end/abort the initial object.
        JObject BaselineReadback()
        {
            var state = TopologyReadback(doc, sketch);
            state["image_count"] = sketch.SketchImages.Count;
            state["referenced_ole_count"] = doc.ReferencedOLEFileDescriptors.Count;
            return state;
        }
        var baseline = BaselineReadback(); var rows = new JArray(); report["bmp_image_cases"] = rows;
        if (doc.ReferencedOLEFileDescriptors.Count != 0)
            throw new InvalidOperationException("BMP research requires no pre-existing OLE descriptors.");
        report["evidence"] = "API RESEARCH ONLY: native SketchImages.Add BMP variants and link, position, uniform dimensions, quarter-turn/inverse, Delete; Abort readback and explicit owned OLE cleanup are reported separately. NOT desktop command equivalence, production import, Quest or physical PASS.";
        report["kind"] = doc.ComponentDefinition is SheetMetalComponentDefinition ? "sheetmetal" : "part";
        report["baseline"] = baseline.DeepClone();
        report["unsupported_options"] = new JArray("arbitrary numeric rotation", "alpha interpretation", "persistent linked-file retention", "external source updates", "other image formats");
        string directory = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "s1-native-bmp-" + Guid.NewGuid().ToString("N")));
        if (Directory.Exists(directory) || File.Exists(directory)) throw new InvalidOperationException("Unique BMP scratch directory already exists.");
        Directory.CreateDirectory(directory);
        var created = new List<string>();
        try
        {
            foreach (int bits in new[] { 24, 32 })
            foreach (bool topDown in new[] { false, true })
            {
                string path = Path.GetFullPath(Path.Combine(directory, $"fixture-{bits}-{(topDown ? "top" : "bottom")}.bmp"));
                if (Path.GetDirectoryName(path) != directory) throw new InvalidOperationException("BMP file escapes owned scratch directory.");
                BmpProbeWriteFile(path, bits, topDown, created);
                foreach (bool linked in new[] { true, false })
                {
                    Guard(true);
                    if (!JToken.DeepEquals(baseline, BaselineReadback()))
                        throw new InvalidOperationException("BMP baseline or current transaction changed before a case.");
                    var row = new JObject { ["bits_per_pixel"] = bits, ["top_down"] = topDown, ["link_requested"] = linked,
                        ["source_file"] = path, ["source_sha256"] = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant() };
                    rows.Add(row);
                    var ownedOleKeys = new HashSet<string>(StringComparer.Ordinal);
                    var transaction = app.TransactionManager.StartTransaction((_Document)doc, "S1 BMP API research, always aborted");
                    try
                    {
                        if (transaction.HasParentTransaction) throw new InvalidOperationException("BMP research cannot have a parent transaction.");
                        Guard(true); row["phase"] = "Add";
                        var image = sketch.SketchImages.Add(path, app.TransientGeometry.CreatePoint2d(1.3, 2.1), linked);
                        Guard(false); doc.Update2(true);
                        // Baseline has zero OLE descriptors and this isolated call
                        // creates one image. Capture the resulting native identities
                        // before Delete/Abort: Inventor can clear FullFileName on Abort.
                        var addedOle = new JArray(); row["ole_after_add"] = addedOle;
                        foreach (ReferencedOLEFileDescriptor descriptor in doc.ReferencedOLEFileDescriptors)
                        {
                            string key = TopologyKey(descriptor);
                            if (string.IsNullOrEmpty(key) || !ownedOleKeys.Add(key))
                                throw new InvalidOperationException("Added OLE descriptor identity is empty or duplicated.");
                            addedOle.Add(new JObject { ["reference_key"] = key, ["filename"] = descriptor.FullFileName });
                        }
                        row["added"] = BmpProbeImageReadback(image);
                        if (sketch.SketchImages.Count != 1 || !TopologySame(image.Parent, sketch) || image.LinkedToFile != linked)
                            throw new InvalidOperationException("Native image count, owner or link state differs from requested input.");
                        BmpProbeNear(image.Position.X, 1.3); BmpProbeNear(image.Position.Y, 2.1);
                        if (linked && !string.Equals(Path.GetFullPath(image.ReferencedFileDescriptor.FullFileName), path, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidOperationException("Linked image does not reference the owned BMP file.");
                        double width = image.Width, height = image.Height;
                        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0)
                            throw new InvalidOperationException("Native imported image dimensions are not finite positive lengths.");
                        row["phase"] = "position and uniform dimensions";
                        image.Position = app.TransientGeometry.CreatePoint2d(2.3, 3.1);
                        image.Width = width * 1.5; image.Height = height * 1.5; doc.Update2(true);
                        BmpProbeNear(image.Position.X, 2.3); BmpProbeNear(image.Position.Y, 3.1);
                        BmpProbeNear(image.Width, width * 1.5); BmpProbeNear(image.Height, height * 1.5);
                        var scaled = BmpProbeImageReadback(image); row["scaled_and_positioned"] = scaled;
                        row["phase"] = "quarter-turn and inverse";
                        image.RotateLeft(); doc.Update2(true); row["rotated_left"] = BmpProbeImageReadback(image);
                        image.RotateRight(); doc.Update2(true);
                        var inverse = BmpProbeImageReadback(image); row["inverse_rotation"] = inverse;
                        BmpProbeRequireSameImageGeometry(scaled, inverse);
                        row["native_pixel_research"] = BmpImagePixelResearch37(image, topDown);
                        row["phase"] = "Delete"; image.Delete(); doc.Update2(true);
                        row["count_after_delete"] = sketch.SketchImages.Count;
                        if (sketch.SketchImages.Count != 0) throw new InvalidOperationException("Native image Delete did not remove the added image.");
                        row["observation"] = "API CHECKS COMPLETED; not a desktop or product capability PASS";
                    }
                    catch (Exception ex)
                    {
                        row["observation"] = "NATIVE EXCEPTION OR API DIVERGENCE";
                        row["hresult"] = "0x" + ex.HResult.ToString("X8"); row["error"] = ex.ToString();
                    }
                    finally
                    {
                        if (!TopologySame(app.TransactionManager.CurrentTransaction, transaction))
                            throw new InvalidOperationException("Owned BMP research transaction is no longer current; refusing to touch another transaction.");
                        transaction.Abort();
                    }
                    Guard(true); var restored = BaselineReadback(); row["rollback"] = restored;
                    row["exact_rollback"] = JToken.DeepEquals(baseline, restored);
                    if (!(bool)row["exact_rollback"]!)
                    {
                        // SketchImages.Add can retain an OLE descriptor beyond Abort.
                        // This is separate repair, not a claim that Abort was exact.
                        // Only identities captured immediately after this Add may
                        // be deleted; an extant filename must also be an owned file.
                        var removed = new JArray(); row["explicit_ole_cleanup"] = removed;
                        for(int index = doc.ReferencedOLEFileDescriptors.Count; index >= 1; index--)
                        {
                            var descriptor = doc.ReferencedOLEFileDescriptors[index];
                            string source = descriptor.FullFileName;
                            string key = TopologyKey(descriptor);
                            row["ole_cleanup_candidate"] = new JObject { ["filename"] = source, ["reference_key"] = key };
                            if(!ownedOleKeys.Contains(key) || (!string.IsNullOrEmpty(source)
                                && (!Path.IsPathRooted(source) || !created.Any(owned=>string.Equals(owned,Path.GetFullPath(source),StringComparison.OrdinalIgnoreCase)))))
                                throw new InvalidOperationException("OLE cleanup encountered a descriptor outside the exact identities created by this image Add.");
                            removed.Add(new JObject { ["reference_key"] = key, ["filename"] = source });
                            descriptor.Delete();
                        }
                        Guard(true);
                    }
                    var afterCleanup = BaselineReadback(); row["after_explicit_cleanup"] = afterCleanup;
                    row["exact_restoration_after_cleanup"] = JToken.DeepEquals(baseline,afterCleanup);
                    if (!(bool)row["exact_restoration_after_cleanup"]!)
                        throw new InvalidOperationException("BMP Abort and explicit owned OLE cleanup did not restore the exact empty baseline.");
                }
            }
            report["case_count"] = rows.Count;
            report["completed_api_checks"] = rows.OfType<JObject>().Count(r => (string?)r["observation"] == "API CHECKS COMPLETED; not a desktop or product capability PASS");
            report["native_exceptions_or_divergences"] = rows.Count - (int)report["completed_api_checks"]!;
            report["exact_abort_count"] = rows.OfType<JObject>().Count(r=>(bool?)r["exact_rollback"] == true);
            report["explicit_ole_cleanup_count"] = rows.OfType<JObject>().Count(r=>r["explicit_ole_cleanup"] is JArray removed && removed.Count > 0);
            report["research_outcome"] = "OBSERVATIONS COLLECTED; owned transactions aborted and baseline restored with explicit OLE cleanup when needed. Exact Abort and repair are distinct; no production preview guarantee.";
            report["final_state"] = BaselineReadback(); Guard(true);
        }
        finally
        {
            // No recursive deletion or enumeration of arbitrary files. Delete only files this call created.
            var cleanupErrors = new JArray();
            foreach (string path in created)
                try { File.Delete(path); } catch (Exception ex) { cleanupErrors.Add(new JObject { ["file"] = path, ["error"] = ex.ToString() }); }
            try { Directory.Delete(directory, false); } catch (Exception ex) { cleanupErrors.Add(new JObject { ["directory"] = directory, ["error"] = ex.ToString() }); }
            report["temp_cleanup_errors"] = cleanupErrors; report["temp_cleanup_complete"] = cleanupErrors.Count == 0;
            if (cleanupErrors.Count != 0) throw new InvalidOperationException("Owned BMP scratch cleanup was incomplete; see research report.");
        }
    }

    private static JObject BmpProbeImageReadback(SketchImage image)
    {
        var boundary = new JArray();
        foreach (SketchEntity entity in image.BoundaryGeometry)
        {
            if (entity is not SketchLine line) throw new InvalidOperationException("Image boundary contains an unexpected non-line native object.");
            boundary.Add(new JArray(line.StartSketchPoint.Geometry.X * 10, line.StartSketchPoint.Geometry.Y * 10,
                line.EndSketchPoint.Geometry.X * 10, line.EndSketchPoint.Geometry.Y * 10));
        }
        return new JObject { ["type"] = image.Type.ToString(), ["name"] = image.Name, ["reference_key"] = TopologyKey(image),
            ["position_mm"] = new JArray(image.Position.X * 10, image.Position.Y * 10), ["width_mm"] = image.Width * 10,
            ["height_mm"] = image.Height * 10, ["linked"] = image.LinkedToFile,
            ["referenced_filename"] = image.LinkedToFile ? image.ReferencedFileDescriptor.FullFileName : null, ["boundary_mm"] = boundary };
    }

    private static void BmpProbeNear(double actual, double expected)
    { if (!double.IsFinite(actual) || Math.Abs(actual - expected) > 1e-7) throw new InvalidOperationException("Native BMP geometry diverged from the requested value."); }

    private static void BmpProbeRequireSameImageGeometry(JObject first, JObject second)
    {
        foreach (string key in new[] { "position_mm", "boundary_mm" })
        {
            var a = ((JArray)first[key]!).DescendantsAndSelf().OfType<JValue>().Select(v => (double)v).ToArray();
            var b = ((JArray)second[key]!).DescendantsAndSelf().OfType<JValue>().Select(v => (double)v).ToArray();
            if (a.Length != b.Length) throw new InvalidOperationException("Inverse rotation changed boundary cardinality.");
            for (int i = 0; i < a.Length; i++) BmpProbeNear(a[i], b[i]);
        }
        BmpProbeNear((double)first["width_mm"]!, (double)second["width_mm"]!);
        BmpProbeNear((double)first["height_mm"]!, (double)second["height_mm"]!);
        if ((string?)first["reference_key"] != (string?)second["reference_key"] || (bool)first["linked"]! != (bool)second["linked"]!)
            throw new InvalidOperationException("Inverse rotation changed image identity or link state.");
    }

    private static void BmpProbeWriteFile(string path, int bits, bool topDown, List<string> created)
    {
        const int width = 3, height = 2;
        int stride = ((width * bits + 31) / 32) * 4;
        byte[] bytes = new byte[54 + stride * height];
        void U16(int offset, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset, 2), value);
        void U32(int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset, 4), value);
        U16(0, 0x4d42); U32(2, (uint)bytes.Length); U32(10, 54); U32(14, 40);
        U32(18, width); U32(22, unchecked((uint)(topDown ? -height : height))); U16(26, 1); U16(28, (ushort)bits);
        U32(34, (uint)(stride * height)); U32(38, 3780); U32(42, 3780);
        for (int row = 0; row < height; row++)
        for (int x = 0; x < width; x++)
        {
            int offset = 54 + row * stride + x * (bits / 8);
            bytes[offset] = (byte)(row == 0 ? 255 : 0); bytes[offset + 1] = (byte)(x * 100); bytes[offset + 2] = (byte)(row == 1 ? 255 : 0);
            if (bits == 32) bytes[offset + 3] = 0; // BI_RGB unused X byte; no alpha claim.
        }
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        created.Add(path); stream.Write(bytes);
    }
}
