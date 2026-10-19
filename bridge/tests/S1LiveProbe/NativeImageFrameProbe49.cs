using System.Reflection;
using Bimwright.Ipt.Shared.Plugin;
using Inventor;
using Newtonsoft.Json.Linq;
using File = System.IO.File;
using Path = System.IO.Path;

internal static partial class Program
{
    private static void VerifyImageFrame49(Inventor.Application app, PartDocument doc, PlanarSketch sketch,
        JObject row, int turns, string sha, int length, JObject? snapshot)
    {
        if (snapshot == null || snapshot["entities"] is not JArray entities) throw new InvalidOperationException("Native image snapshot missing.");
        var item = entities.OfType<JObject>().Single(e => (string?)e["type"] == "image");
        if (item["image_frame"] == null)
        {
            var frameType = typeof(InventorCommandRegistry).Assembly.GetType("Bimwright.Ipt.Shared.Handlers.Experimental.NativeSketchImageFrame", true)!;
            // Preserve the native binding failure stack rather than the snapshot's sanitized summary.
            frameType.GetMethod("Read", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object[] { doc, sketch.SketchImages[1] });
        }
        if ((bool?)snapshot["partial"] != true || (bool?)item["supported"] != false
            || (string?)item["representation_state"] != "geometry_and_original_source")
            throw new InvalidOperationException("Image source/frame must not claim a complete texture representation.");
        var source = (JObject)item["image_source"]!;
        if ((string?)source["sha256"] != sha || (string?)source["asset_id"] != "a_" + sha
            || (int?)source["byte_length"] != length || (string?)source["kind"] != "managed_original")
            throw new InvalidOperationException("Native image metadata differs from committed source.");
        var image = sketch.SketchImages[1];
        var type = typeof(InventorCommandRegistry).Assembly.GetType("Bimwright.Ipt.Shared.Handlers.Experimental.NativeSketchImageFrame", true)!;
        var method = type.GetMethod("Read", BindingFlags.Static | BindingFlags.NonPublic)!;
        JObject Read(PartDocument document, SketchImage target) => (JObject)method.Invoke(null, new object[] { document, target })!;
        void Compare(JArray actual, JArray expected)
        {
            if (actual.Count != 4 || expected.Count != 4) throw new InvalidOperationException("Four bound corners required.");
            for (int i = 0; i < 4; i++) for (int axis = 0; axis < 2; axis++) ImageNear45((double)actual[i][axis]!, (double)expected[i][axis]!);
        }
        var original = Read(doc, image);
        if (!JToken.DeepEquals(original, item["image_frame"])) throw new InvalidOperationException("Snapshot and native frame differ.");
        double w = image.Width * 10, h = image.Height * 10, a = turns * Math.PI / 2;
        var expected = new JArray();
        foreach (var p in new[] { (0d, 0d), (w, 0d), (w, -h), (0d, -h) })
            expected.Add(new JArray(13 + p.Item1 * Math.Cos(a) - p.Item2 * Math.Sin(a), 21 + p.Item1 * Math.Sin(a) + p.Item2 * Math.Cos(a)));
        Compare((JArray)original["corners_mm"]!, expected);
        row["frame_theoretical_quarter_turn_match"] = true;
        var tx = app.TransactionManager.StartTransaction((_Document)doc, "S1 image corner transform49");
        try
        {
            var objects = app.TransientObjects.CreateObjectCollection(); objects.Add(image);
            sketch.MoveSketchObjects(objects, app.TransientGeometry.CreateVector2d(0.4, -0.3), false, false);
            if (!doc.Update2(true)) throw new InvalidOperationException("Image move update failed.");
            var moved = new JArray(expected.OfType<JArray>().Select(p => new JArray((double)p[0] + 4, (double)p[1] - 3)));
            Compare((JArray)Read(doc, image)["corners_mm"]!, moved);
            double angle = 37 * Math.PI / 180;
            sketch.RotateSketchObjects(objects, app.TransientGeometry.CreatePoint2d(0, 0), angle, false, false);
            if (!doc.Update2(true)) throw new InvalidOperationException("Image rotation update failed.");
            var rotated = new JArray(moved.OfType<JArray>().Select(p => new JArray((double)p[0] * Math.Cos(angle) - (double)p[1] * Math.Sin(angle),
                (double)p[0] * Math.Sin(angle) + (double)p[1] * Math.Cos(angle))));
            var current = Read(doc, image); Compare((JArray)current["corners_mm"]!, rotated);
            row["frame_after_move_and_rotation"] = current; row["frame_transform_match"] = true;
        }
        finally
        {
            if (!TopologySame(app.TransactionManager.CurrentTransaction, tx)) throw new InvalidOperationException("Owned frame transform transaction changed.");
            tx.Abort();
        }
        if (!JToken.DeepEquals(original, Read(doc, image))) throw new InvalidOperationException("Frame transform Abort did not restore exact coordinates.");
        row["frame_abort_exact"] = true;
        // Persisted keys must survive a saved copy. Use the last case, with all earlier images present.
        if ((int?)row["bits_per_pixel"] != 32 || (bool?)row["top_down"] != true || turns != 3) return;
        if (app.Documents.Count != 1 || !TopologySame(app.ActiveDocument, doc) || !string.IsNullOrEmpty(doc.FullFileName))
            throw new InvalidOperationException("Saved frame copy requires the exact single owned unsaved fixture.");
        string directory = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "s1-frame-copy49-" + Guid.NewGuid().ToString("N")));
        string path = Path.Combine(directory, "owned-copy.ipt");
        if (Directory.Exists(directory) || File.Exists(directory)) throw new InvalidOperationException("Frame-copy scratch already exists.");
        Directory.CreateDirectory(directory);
        PartDocument? copy = null; bool copyOwned = false, closed = false, attempted = false;
        string revision = doc.DatabaseRevisionId, identity = doc.InternalName; bool dirty = doc.Dirty;
        int count = doc.ComponentDefinition.Sketches.Count;
        try
        {
            attempted = true; doc.SaveAs(path, true);
            copy = app.Documents.Open(path, false) as PartDocument ?? throw new InvalidOperationException("Saved frame copy did not reopen.");
            copyOwned = !TopologySame(copy, doc) && string.Equals(Path.GetFullPath(copy.FullFileName), path, StringComparison.OrdinalIgnoreCase);
            if (!copyOwned || app.Documents.Count != 2 || copy.ComponentDefinition.Sketches.Count != count)
                throw new InvalidOperationException("Saved frame copy ownership/count differs.");
            var copiedImage = copy.ComponentDefinition.Sketches[count].SketchImages[1];
            var frame = Read(copy, copiedImage); Compare((JArray)frame["corners_mm"]!, (JArray)original["corners_mm"]!);
            row["frame_after_saved_copy_reopen"] = frame; row["frame_saved_copy_match"] = true;
            copy.Close(true); copy = null; closed = true; doc.Activate();
            if (doc.InternalName != identity || doc.DatabaseRevisionId != revision || doc.Dirty != dirty
                || !string.IsNullOrEmpty(doc.FullFileName) || app.Documents.Count != 1 || !TopologySame(app.ActiveDocument, doc))
                throw new InvalidOperationException("Frame-copy test changed the original identity/revision/Dirty state.");
            row["frame_copy_original_unchanged"] = true;
        }
        finally
        {
            if (copy != null && copyOwned)
            {
                if (TopologySame(copy, doc) || !string.Equals(Path.GetFullPath(copy.FullFileName), path, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Frame copy ownership changed; preserving it.");
                copy.Close(true); copy = null; closed = true; doc.Activate();
            }
            if (copy == null && (!attempted || closed))
            {
                if (!string.Equals(Path.GetDirectoryName(Path.GetFullPath(path)), directory, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Frame-copy cleanup escaped owned directory.");
                File.Delete(path); Directory.Delete(directory, false);
                row["frame_copy_cleanup_complete"] = true;
            }
            else throw new InvalidOperationException("Frame-copy closure uncertain; preserving scratch files.");
        }
    }
}
