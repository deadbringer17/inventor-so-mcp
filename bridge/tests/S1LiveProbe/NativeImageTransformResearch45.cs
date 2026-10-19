using Inventor;
using Newtonsoft.Json.Linq;
using File = System.IO.File;
using Path = System.IO.Path;

internal static partial class Program
{
    // Observations of API setter/rotation order, not a product acceptance verdict.
    private static void RunImageTransformResearch45(Inventor.Application app, PartDocument doc, JObject report)
    {
        if (doc.ComponentDefinition.Sketches.Count != 1)
            throw new InvalidOperationException("Image transform research requires one original owned sketch.");
        var sketch = doc.ComponentDefinition.Sketches[1]; string sketchName = sketch.Name;
        void Guard(bool empty)
        {
            if (app.Documents.Count != 1 || app.ActiveDocument is not PartDocument active || !TopologySame(active, doc)
                || doc.DisplayName != "S1_ArcInput_Oracle.ipt" || !string.IsNullOrEmpty(doc.FullFileName)
                || doc.ComponentDefinition.Sketches.Count != 1 || !TopologySame(doc.ComponentDefinition.Sketches[1], sketch)
                || sketch.Name != sketchName)
                throw new InvalidOperationException("Exact single unsaved owned image transform fixture required.");
            if (app.ActiveEditObject is object edit && !TopologySame(edit, sketch) && !TopologySame(edit, doc)
                && !TopologySame(edit, doc.ComponentDefinition)) throw new InvalidOperationException("Another native edit context is active.");
            if (empty && (sketch.SketchEntities.Count != 0 || sketch.GeometricConstraints.Count != 0
                || sketch.DimensionConstraints.Count != 0 || sketch.TextBoxes.Count != 0 || sketch.SketchImages.Count != 0))
                throw new InvalidOperationException("Image transform research requires a completely empty sketch.");
        }
        Guard(true);
        if (app.ActiveEditObject is PlanarSketch editing) editing.ExitEdit();
        Guard(true);
        if (doc.ReferencedOLEFileDescriptors.Count != 0) throw new InvalidOperationException("Research baseline must have zero OLE descriptors.");
        JObject Snapshot()
        {
            Guard(false); var state = TopologyReadback(doc, sketch);
            state["images"] = new JArray(sketch.SketchImages.Cast<SketchImage>().Select(BmpProbeImageReadback));
            state["ole"] = new JArray(doc.ReferencedOLEFileDescriptors.Cast<ReferencedOLEFileDescriptor>()
                .Select(d => new JObject { ["key"] = TopologyKey(d), ["filename"] = d.FullFileName }));
            state["image_count"] = sketch.SketchImages.Count;
            state["ole_count"] = doc.ReferencedOLEFileDescriptors.Count; return state;
        }
        var baseline = Snapshot(); var rows = new JArray(); report["image_transform_cases"] = rows;
        report["evidence"] = "API RESEARCH ONLY: embedded BMP Width/Height/Position setter order versus RotateLeft; full phase boundary readback. No product, desktop, pixel orientation, Quest or physical PASS.";
        report["kind"] = doc.ComponentDefinition is SheetMetalComponentDefinition ? "sheetmetal" : "part";
        report["baseline"] = baseline.DeepClone(); report["intended_case_count"] = 24;
        string directory = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "s1-image-transform45-" + Guid.NewGuid().ToString("N")));
        if (Directory.Exists(directory) || File.Exists(directory)) throw new InvalidOperationException("Owned research scratch directory already exists.");
        Directory.CreateDirectory(directory); var created = new List<string>();
        try
        {
            foreach (int turns in new[] { 0, 1, 2, 3 })
            foreach (string order in new[] { "beforeRotate", "afterRotate", "geometryRotate" })
            foreach (string policy in new[] { "source30x20", "oddSwapped20x30" })
            {
                Guard(true);
                if (doc.ReferencedOLEFileDescriptors.Count != 0 || !JToken.DeepEquals(baseline, Snapshot()))
                    throw new InvalidOperationException("Native research baseline was not restored before the next case.");
                string source = Path.GetFullPath(Path.Combine(directory, "owned-" + rows.Count + ".bmp"));
                if (Path.GetDirectoryName(source) != directory) throw new InvalidOperationException("Research source escaped owned directory.");
                BmpProbeWriteFile(source, 24, false, created);
                bool swap = policy == "oddSwapped20x30" && turns % 2 != 0;
                double width = swap ? 20 : 30, height = swap ? 30 : 20;
                var phases = new JArray(); var row = new JObject { ["quarter_turns"] = turns, ["resize_order"] = order,
                    ["target_policy"] = policy, ["requested_width_mm"] = width, ["requested_height_mm"] = height,
                    ["requested_position_mm"] = new JArray(13, 21), ["setter_order"] = "Width, Height, Position",
                    ["source"] = source, ["phases"] = phases };
                rows.Add(row); var ownedKeys = new HashSet<string>(StringComparer.Ordinal);
                var transaction = app.TransactionManager.StartTransaction((_Document)doc, "S1 image transform API research, always aborted");
                try
                {
                    if (transaction.HasParentTransaction) throw new InvalidOperationException("Research transaction must be a root transaction.");
                    Guard(true); SketchImage image;
                    try { image = sketch.SketchImages.Add(source, app.TransientGeometry.CreatePoint2d(1.3, 2.1), false); }
                    finally
                    {
                        // Baseline is empty and only this isolated Add has run. Capture
                        // native identities before Abort can clear descriptor filenames.
                        foreach (ReferencedOLEFileDescriptor descriptor in doc.ReferencedOLEFileDescriptors)
                        {
                            string key = TopologyKey(descriptor), filename = descriptor.FullFileName;
                            if (key.Length == 0 || !ownedKeys.Add(key) || (!string.IsNullOrEmpty(filename)
                                && (!Path.IsPathRooted(filename) || !string.Equals(Path.GetFullPath(filename), source, StringComparison.OrdinalIgnoreCase))))
                                throw new InvalidOperationException("Cannot attribute the isolated image Add OLE descriptor to this research case.");
                        }
                    }
                    void Read(string phase)
                    {
                        Guard(false);
                        if (image.LinkedToFile || !TopologySame(image.Parent, sketch) || sketch.SketchImages.Count != 1)
                            throw new InvalidOperationException("Research image identity, owner or embedded state changed.");
                        phases.Add(new JObject { ["phase"] = phase, ["image"] = BmpProbeImageReadback(image) });
                    }
                    void Configure()
                    {
                        image.Width = width / 10; image.Height = height / 10;
                        image.Position = app.TransientGeometry.CreatePoint2d(1.3, 2.1);
                        Read("configured_before_update");
                        if (!doc.Update2(true)) throw new InvalidOperationException("Native update failed after configuration.");
                        Read("configured");
                    }
                    Read("rawAdd");
                    if (ownedKeys.Count != 1) throw new InvalidOperationException("Isolated embedded Add did not yield exactly one OLE descriptor.");
                    if (order == "beforeRotate" || order == "geometryRotate") Configure();
                    for (int n = 0; n < turns; n++)
                    {
                        if (order == "geometryRotate")
                        {
                            var objects = app.TransientObjects.CreateObjectCollection(); objects.Add(image);
                            sketch.RotateSketchObjects(objects, app.TransientGeometry.CreatePoint2d(1.3, 2.1), Math.PI / 2, false, false);
                        }
                        else image.RotateLeft();
                        string phase = (order == "geometryRotate" ? "rotate_geometry_" : "rotate_left_") + (n + 1);
                        Read(phase + "_before_update");
                        if (!doc.Update2(true)) throw new InvalidOperationException("Native update failed after rotation.");
                        Read(phase);
                    }
                    if (order == "afterRotate") Configure();
                    if (!doc.Update2(true)) throw new InvalidOperationException("Final native image update failed.");
                    Read("final");
                    row["observation"] = "OBSERVATIONS COLLECTED; no product PASS";
                }
                catch (Exception ex) { row["observation"] = "NATIVE EXCEPTION"; row["error"] = ex.ToString(); }
                finally
                {
                    if (!TopologySame(app.TransactionManager.CurrentTransaction, transaction))
                        throw new InvalidOperationException("Owned research transaction is no longer current; refusing to touch another transaction.");
                    transaction.Abort(); Guard(true);
                    row["after_abort"] = Snapshot();
                    var candidates = new List<ReferencedOLEFileDescriptor>();
                    foreach (ReferencedOLEFileDescriptor descriptor in doc.ReferencedOLEFileDescriptors)
                    {
                        string filename = descriptor.FullFileName;
                        if (!ownedKeys.Contains(TopologyKey(descriptor)) || (!string.IsNullOrEmpty(filename)
                            && (!Path.IsPathRooted(filename) || !string.Equals(Path.GetFullPath(filename), source, StringComparison.OrdinalIgnoreCase))))
                            throw new InvalidOperationException("OLE repair encountered a descriptor outside isolated Add ownership.");
                        candidates.Add(descriptor);
                    }
                    var removed = new JArray(); row["explicit_ole_cleanup"] = removed;
                    foreach (var descriptor in candidates)
                    { removed.Add(TopologyKey(descriptor)); descriptor.Delete(); }
                    Guard(true); var restored = Snapshot(); row["restored"] = restored;
                    row["exact_empty_restoration"] = sketch.SketchImages.Count == 0 && doc.ReferencedOLEFileDescriptors.Count == 0
                        && JToken.DeepEquals(baseline, restored);
                    if ((bool?)row["exact_empty_restoration"] != true)
                        throw new InvalidOperationException("Image transform Abort and explicit owned repair did not restore the exact empty baseline.");
                }
            }
            report["final_state"] = Snapshot();
        }
        finally
        {
            report["case_count"] = rows.Count;
            report["completed_observations"] = rows.OfType<JObject>().Count(r => (string?)r["observation"] == "OBSERVATIONS COLLECTED; no product PASS");
            report["native_exception_count"] = rows.OfType<JObject>().Count(r => (string?)r["observation"] == "NATIVE EXCEPTION");
            report["exact_empty_restoration_count"] = rows.OfType<JObject>().Count(r => (bool?)r["exact_empty_restoration"] == true);
            var errors = new JArray();
            foreach (string path in created)
                try { File.Delete(path); } catch (Exception ex) { errors.Add(new JObject { ["path"] = path, ["error"] = ex.ToString() }); }
            try { Directory.Delete(directory, false); } catch (Exception ex) { errors.Add(new JObject { ["path"] = directory, ["error"] = ex.ToString() }); }
            report["temp_cleanup_errors"] = errors; report["temp_cleanup_complete"] = errors.Count == 0;
            if (errors.Count != 0) throw new InvalidOperationException("Owned image transform research scratch cleanup failed.");
        }
    }
}
