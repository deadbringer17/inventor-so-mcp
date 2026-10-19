#if INVENTOR2027 && SO_EXPERIMENTAL
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;
using Inventor;
using Newtonsoft.Json.Linq;
using File = System.IO.File;
using Path = System.IO.Path;

namespace Bimwright.Ipt.Shared.Handlers.Experimental;

/// <summary>STA-only image insertion from batch-owned sealed bytes. Public paths are never accepted.</summary>
internal static class NativeSketchImages
{
    internal static JObject Apply(InventorCommandContext ctx, Application app, PartDocument doc,
        PlanarSketch sketch, JObject options)
    {
        if (ctx.ReadOnly) throw new CodedFailureException(InventorErrorCodes.READ_ONLY, "Image insertion requires write permission.");
        if (!ctx.AllowExperimental) throw new CodedFailureException(InventorErrorCodes.EXPERIMENTAL_DISABLED, "Experimental image insertion is disabled.");
        if (ctx.OwnedBatchTransaction == null || !ReferenceEquals(ctx.OwnedBatchTransaction, app.TransactionManager.CurrentTransaction))
            throw new CodedFailureException(InventorErrorCodes.ATOMIC_REQUIRED, "Image insertion requires the owned atomic batch transaction.");
        if (ctx.OwnedSketchImageJournal is not NativeSketchImageJournal journal || ctx.OwnedSketchImports == null)
            throw new CodedFailureException(InventorErrorCodes.ATOMIC_REQUIRED, "Image insertion requires batch-owned import resources and a native journal.");
        if (options.Properties().Any(p => !new[] { "asset_id", "position_mm", "width_mm", "quarter_turns", "link" }.Contains(p.Name, StringComparer.Ordinal))
            || options["asset_id"]?.Type != JTokenType.String || options["link"]?.Type != JTokenType.Boolean
            || options["quarter_turns"]?.Type != JTokenType.Integer
            || !int.TryParse(options["quarter_turns"]!.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int turns)
            || turns < 0 || turns > 3 || options["position_mm"] is not JArray position || position.Count != 2)
            throw Invalid();
        // Linked files need document-owned durable storage and independent reference repair.
        // The same-source native OLE failure is unresolved; refuse before creating any file.
        if ((bool)options["link"]!) throw new CodedFailureException(InventorErrorCodes.INVALID_ARGUMENT, "Linked image insertion requires durable document storage and is not yet supported.");
        double x = Number(position[0]), y = Number(position[1]), width = Number(options["width_mm"]);
        if (width <= 0) throw Invalid();
        string asset = (string)options["asset_id"]!;
        if (!ctx.OwnedSketchImports.Resources.TryGetValue(asset, out var resource))
            throw new CodedFailureException(InventorErrorCodes.ASSET_NOT_FOUND, "The image is not attached to this batch.");
        journal.GuardForAdd(ctx.OwnedBatchTransaction, app, doc, sketch);
        X.Deadline(ctx, "validating the image bytes");
        byte[] bytes = ctx.OwnedSketchImports.Read(asset);
        string source;
        int pixelsWide, pixelsHigh;
        try
        {
            if (bytes.Length != resource.ByteLength || resource.Format != "bmp"
                || Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() != resource.Sha256
                || asset != "a_" + resource.Sha256) throw Invalid();
            (pixelsWide, pixelsHigh) = ValidateBmp(bytes, resource.Metadata);
            NativeSketchImagePayload.AssertCapacity(doc, bytes.Length);
            double height = width * pixelsHigh / pixelsWide;
            if (!double.IsFinite(height) || height <= 0) throw Invalid();
            X.Deadline(ctx, "writing the owned image source");
            source = journal.WriteSource(bytes);
        }
        finally { Array.Clear(bytes, 0, bytes.Length); }
        journal.GuardForAdd(ctx.OwnedBatchTransaction, app, doc, sketch);
        X.Deadline(ctx, "inserting the native image");
        var before = journal.OleKeys();
        int count = sketch.SketchImages.Count;
        SketchImage image;
        try { image = sketch.SketchImages.Add(source, app.TransientGeometry.CreatePoint2d(x / 10, y / 10), false); }
        finally { journal.CaptureAdd(before, source); }
        journal.GuardForAdd(ctx.OwnedBatchTransaction, app, doc, sketch);
        if (sketch.SketchImages.Count != count + 1 || !SketchReferences.SameComObject(image.Parent, sketch) || image.LinkedToFile)
            throw new InvalidOperationException("Native image ownership, cardinality or embedded state diverged.");
        if (journal.OleKeys().Count(k => !before.Contains(k)) != 1)
            throw new InvalidOperationException("Native image insertion did not produce exactly one OLE descriptor.");
        byte[] payload = ctx.OwnedSketchImports.Read(asset);
        try { NativeSketchImagePayload.Write(image, payload, resource.Sha256, pixelsWide, pixelsHigh); }
        finally { Array.Clear(payload, 0, payload.Length); }
        image.Width = width / 10;
        image.Height = width * pixelsHigh / pixelsWide / 10;
        image.Position = app.TransientGeometry.CreatePoint2d(x / 10, y / 10);
        Near("width_mm", image.Width * 10, width); Near("height_mm", image.Height * 10, width * pixelsHigh / pixelsWide);
        Near("position_x_mm", image.Position.X * 10, x); Near("position_y_mm", image.Position.Y * 10, y);
        NativeSketchImageFrame.Write(image);
        if (turns != 0)
        {
            // RotateLeft changes image properties without rotating its sketch boundary on 2027.
            // Rotate the native object about its insertion point, preserving its local dimensions.
            var objects = app.TransientObjects.CreateObjectCollection(); objects.Add(image);
            sketch.RotateSketchObjects(objects, app.TransientGeometry.CreatePoint2d(x / 10, y / 10),
                turns * Math.PI / 2, false, false);
        }
        X.Deadline(ctx, "updating the native image");
        if (!doc.Update2(true)) throw new InvalidOperationException("Native image update failed.");
        journal.GuardForAdd(ctx.OwnedBatchTransaction, app, doc, sketch);
        if (image.LinkedToFile || !SketchReferences.SameComObject(image.Parent, sketch))
            throw new InvalidOperationException("Native image state diverged after update.");
        Near("width_mm", image.Width * 10, width); Near("height_mm", image.Height * 10, width * pixelsHigh / pixelsWide);
        Near("position_x_mm", image.Position.X * 10, x); Near("position_y_mm", image.Position.Y * 10, y);
        return NativeSketchImageJournal.ImageReadback(image);
    }

    private static double Number(JToken? value)
    {
        if (value?.Type is not (JTokenType.Integer or JTokenType.Float)
            || !double.TryParse(value.ToString(Newtonsoft.Json.Formatting.None), NumberStyles.Float, CultureInfo.InvariantCulture, out double number)
            || !double.IsFinite(number)) throw Invalid();
        return number;
    }
    private static void Near(string property, double actual, double expected)
    {
        // Native image dimensions quantize (30 mm reads 29.99999718 mm on 2027).
        // Use the existing S1 native-property tolerance; identity/Abort comparisons stay exact.
        if (!double.IsFinite(actual) || Math.Abs(actual - expected) > 1e-5)
            throw new CodedFailureException(InventorErrorCodes.VALIDATION_FAILED,
                "Native image geometry diverged from the requested dimensions or position.",
                new JObject { ["property"] = property, ["actual_mm"] = actual, ["expected_mm"] = expected });
    }
    private static (int Width, int Height) ValidateBmp(byte[] bytes, JObject metadata)
    {
        var b = bytes.AsSpan();
        if (b.Length < 54 || BinaryPrimitives.ReadUInt16LittleEndian(b) != 0x4d42
            || U32(b, 2) != b.Length || U32(b, 6) != 0 || U32(b, 10) != 54 || U32(b, 14) != 40) throw Invalid();
        int w = BinaryPrimitives.ReadInt32LittleEndian(b.Slice(18, 4)), sh = BinaryPrimitives.ReadInt32LittleEndian(b.Slice(22, 4));
        if (w <= 0 || sh == 0 || sh == int.MinValue) throw Invalid();
        int h = Math.Abs(sh), bits = BinaryPrimitives.ReadUInt16LittleEndian(b.Slice(28, 2));
        long stride = (((long)w * bits + 31) / 32) * 4;
        if (BinaryPrimitives.ReadUInt16LittleEndian(b.Slice(26, 2)) != 1 || bits is not (24 or 32)
            || U32(b, 30) != 0 || U32(b, 46) != 0 || U32(b, 50) != 0
            || stride > (b.Length - 54L) / h || stride * h != b.Length - 54L
            || (U32(b, 34) != 0 && U32(b, 34) != stride * h)
            || metadata["kind"]?.Type != JTokenType.String || (string?)metadata["kind"] != "Image"
            || metadata["dependencies"] is not JArray dependencies || dependencies.Count != 0
            || metadata["image"] is not JObject image || image["pixel_width"]?.Type != JTokenType.Integer
            || image["pixel_height"]?.Type != JTokenType.Integer
            || (long)image["pixel_width"]! != w || (long)image["pixel_height"]! != h) throw Invalid();
        return (w, h);
    }
    private static uint U32(ReadOnlySpan<byte> b, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(b.Slice(offset, 4));
    private static CodedFailureException Invalid() => new CodedFailureException(InventorErrorCodes.INVALID_ARGUMENT, "Image bytes, metadata or insertion arguments are invalid.");
}

/// <summary>One atomic batch journal. Abort repair owns only identities captured immediately around Add.</summary>
internal sealed class NativeSketchImageJournal : IDisposable
{
    private readonly Application _app;
    private readonly PartDocument _doc;
    private readonly JObject _baseline;
    private readonly Dictionary<string, string> _baselineOle;
    private readonly HashSet<string> _ownedOle = new(StringComparer.Ordinal);
    private readonly List<string> _files = new();
    private string? _directory;
    private object? _transaction;
    private bool _committed, _repaired, _disposed, _captureIncomplete, _repairFailed;

    internal NativeSketchImageJournal(Application app, PartDocument doc)
    {
        _app = app; _doc = doc;
        GuardDocument();
        _baselineOle = OleSnapshot();
        _baseline = Snapshot();
    }
    internal void GuardForAdd(object transaction, Application app, PartDocument doc, PlanarSketch sketch)
    {
        if (_disposed || _committed || _repaired || _repairFailed || _captureIncomplete) throw new InvalidOperationException("Image journal is not writable.");
        if (!ReferenceEquals(app, _app) || !SketchReferences.SameComObject(doc, _doc)) throw new InvalidOperationException("Image journal belongs to another document.");
        GuardDocument();
        if (!ReferenceEquals(transaction, app.TransactionManager.CurrentTransaction)
            || (_transaction != null && !ReferenceEquals(transaction, _transaction)))
            throw new CodedFailureException(InventorErrorCodes.ATOMIC_REQUIRED, "Image insertion requires the journal's exact owned transaction.");
        _transaction = transaction;
        bool found = false;
        foreach (PlanarSketch candidate in _doc.ComponentDefinition.Sketches)
            if (SketchReferences.SameComObject(candidate, sketch)) found = true;
        if (!found) throw new InvalidOperationException("Selected sketch does not belong to the journal document.");
        object? edit = app.ActiveEditObject;
        if (edit != null && !SketchReferences.SameComObject(edit, sketch) && !SketchReferences.SameComObject(edit, doc)
            && !SketchReferences.SameComObject(edit, doc.ComponentDefinition))
            throw new InvalidOperationException("Another native edit context is active.");
    }
    internal string WriteSource(byte[] bytes)
    {
        if (_directory == null)
        {
            string directory = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "inventor-so-image-" + Guid.NewGuid().ToString("N")));
            if (Directory.Exists(directory) || File.Exists(directory)) throw new IOException("Image scratch directory already exists.");
            Directory.CreateDirectory(directory); _directory = directory;
        }
        CheckDirectory();
        string path = Path.GetFullPath(Path.Combine(_directory!, Guid.NewGuid().ToString("N") + ".bmp"));
        if (!string.Equals(Path.GetDirectoryName(path), _directory, StringComparison.OrdinalIgnoreCase)) throw new IOException("Image scratch path escaped its owned directory.");
        using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        { _files.Add(path); stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
        return path;
    }
    internal HashSet<string> OleKeys() => new(OleSnapshot().Keys, StringComparer.Ordinal);
    internal void CaptureAdd(HashSet<string> before, string source)
    {
        try
        {
            GuardDocument();
            int added = 0;
            foreach (var entry in OleSnapshot())
            {
                if (before.Contains(entry.Key)) continue;
                _ownedOle.Add(entry.Key); added++;
                // Empty filename is normal for embedded OLE. Identity comes from isolated Add.
                if (entry.Value.Length != 0 && (!Path.IsPathRooted(entry.Value)
                    || !string.Equals(Path.GetFullPath(entry.Value), source, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException("Added OLE source cannot be attributed to this isolated insertion.");
            }
            // A failing Add may mutate nothing; zero is then valid and Abort can still be proven.
            // Successful Add requires exactly one descriptor in Apply, after this finally capture.
            if (added > 1) throw new InvalidOperationException("Isolated image insertion yielded multiple OLE descriptors.");
        }
        catch { _captureIncomplete = true; throw; }
    }
    internal void RepairAfterAbort()
    {
        if (_repairFailed) throw RollbackFailure();
        if (_repaired) return;
        try
        {
            if (_committed || _disposed || _captureIncomplete) throw new InvalidOperationException("Image journal cannot prove Abort repair ownership.");
            GuardDocument();
            if (_transaction != null && ReferenceEquals(_transaction, _app.TransactionManager.CurrentTransaction))
                throw new InvalidOperationException("Image repair requires the owned transaction to have been aborted first.");
            var current = OleSnapshot();
            var protectedFiles = new HashSet<string>(_baselineOle.Values.Where(f => f.Length != 0).Select(NormalizeFile), StringComparer.OrdinalIgnoreCase);
            var candidates = new List<ReferencedOLEFileDescriptor>();
            foreach (ReferencedOLEFileDescriptor descriptor in _doc.ReferencedOLEFileDescriptors)
            {
                string key = Key(descriptor), filename = current[key];
                if (_baselineOle.ContainsKey(key))
                { if (_baselineOle[key] != filename) throw new InvalidOperationException("Protected OLE descriptor changed."); continue; }
                if (!_ownedOle.Contains(key) || (filename.Length != 0 && !_files.Contains(NormalizeFile(filename), StringComparer.OrdinalIgnoreCase)))
                    throw new InvalidOperationException("Image repair encountered an uncaptured OLE descriptor.");
                if (filename.Length != 0 && protectedFiles.Contains(NormalizeFile(filename)))
                    throw new InvalidOperationException("New OLE descriptor shares a protected linked source.");
                candidates.Add(descriptor);
            }
            // Validate every candidate and baseline before the first destructive action.
            if (_baselineOle.Keys.Any(k => !current.ContainsKey(k))) throw new InvalidOperationException("Protected OLE descriptor is missing.");
            foreach (var descriptor in candidates) descriptor.Delete();
            if (!JToken.DeepEquals(_baseline, Snapshot())) throw new InvalidOperationException("Native images or OLE baseline was not exactly restored.");
            _repaired = true;
            Cleanup();
        }
        catch (Exception error)
        { _repairFailed = true; throw new CodedFailureException(InventorErrorCodes.ROLLBACK_FAILED, "Native image and OLE restoration could not be verified.", inner: error); }
    }
    // Backend calls this only after it has verified End. Cleanup never changes a committed result.
    internal void Commit() { _committed = true; Cleanup(); }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_committed || _repaired) Cleanup();
        // Preserve diagnostic source files if restoration is unproven or incomplete.
    }
    private void Cleanup()
    {
        if (_directory == null) return;
        try
        {
            CheckDirectory();
            foreach (string file in _files)
                try
                {
                    if (Path.GetDirectoryName(Path.GetFullPath(file)) != _directory) continue;
                    if (File.Exists(file) && (File.GetAttributes(file) & FileAttributes.ReparsePoint) == 0) File.Delete(file);
                }
                catch { /* Cleanup cannot mask CAD commit or verified restoration. */ }
            if (!Directory.EnumerateFileSystemEntries(_directory).Any()) Directory.Delete(_directory, false);
        }
        catch { /* Keep owned scratch on cleanup failure; never recurse. */ }
    }
    private void CheckDirectory()
    {
        if (_directory == null || !Directory.Exists(_directory)
            || (File.GetAttributes(_directory) & FileAttributes.ReparsePoint) != 0) throw new IOException("Image scratch directory is unavailable or redirected.");
    }
    private void GuardDocument()
    {
        if (_app.ActiveDocument is not PartDocument active || !SketchReferences.SameComObject(active, _doc))
            throw new CodedFailureException(InventorErrorCodes.DOCUMENT_CHANGED, "The active image document changed.");
    }
    private Dictionary<string, string> OleSnapshot()
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (ReferencedOLEFileDescriptor descriptor in _doc.ReferencedOLEFileDescriptors)
            if (!result.TryAdd(Key(descriptor), descriptor.FullFileName ?? "")) throw new InvalidOperationException("Duplicate OLE descriptor identity.");
        return result;
    }
    private JObject Snapshot()
    {
        var sketches = new List<JObject>();
        foreach (PlanarSketch sketch in _doc.ComponentDefinition.Sketches)
            sketches.Add(new JObject { ["key"] = Key(sketch), ["images"] = new JArray(sketch.SketchImages.Cast<SketchImage>()
                .Select(ImageReadback).OrderBy(i => (string?)i["reference_key"], StringComparer.Ordinal)) });
        return new JObject { ["sketches"] = new JArray(sketches.OrderBy(s => (string?)s["key"], StringComparer.Ordinal)),
            ["ole"] = new JArray(OleSnapshot().OrderBy(p => p.Key, StringComparer.Ordinal)
                .Select(p => new JObject { ["key"] = p.Key, ["filename"] = p.Value })) };
    }
    internal static JObject ImageReadback(SketchImage image)
    {
        var boundary = new JArray();
        foreach (SketchEntity entity in image.BoundaryGeometry)
        {
            if (entity is not SketchLine line) throw new InvalidOperationException("Unexpected native image boundary type.");
            boundary.Add(new JArray(line.StartSketchPoint.Geometry.X * 10, line.StartSketchPoint.Geometry.Y * 10,
                line.EndSketchPoint.Geometry.X * 10, line.EndSketchPoint.Geometry.Y * 10));
        }
        return new JObject { ["reference_key"] = Key(image), ["name"] = image.Name, ["type"] = image.Type.ToString(),
            ["position_mm"] = new JArray(image.Position.X * 10, image.Position.Y * 10), ["width_mm"] = image.Width * 10,
            ["height_mm"] = image.Height * 10, ["linked"] = image.LinkedToFile,
            ["referenced_filename"] = image.LinkedToFile ? image.ReferencedFileDescriptor.FullFileName : null, ["boundary_mm"] = boundary };
    }
    private static string Key(object value)
    {
        byte[] bytes = Array.Empty<byte>(); ((dynamic)value).GetReferenceKey(ref bytes, 0);
        if (bytes.Length == 0) throw new InvalidOperationException("Native image journal identity is empty.");
        return Convert.ToBase64String(bytes);
    }
    private static string NormalizeFile(string filename)
    { if (!Path.IsPathRooted(filename)) throw new InvalidOperationException("Native OLE filename is not absolute."); return Path.GetFullPath(filename); }
    private static CodedFailureException RollbackFailure() => new(InventorErrorCodes.ROLLBACK_FAILED, "A previous native image repair failed; restoration remains unverified.");
}
#endif
