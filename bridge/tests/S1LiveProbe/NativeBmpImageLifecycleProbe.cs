using Inventor;
using Newtonsoft.Json.Linq;
using File = System.IO.File;
using Path = System.IO.Path;

internal static partial class Program
{
    // API-only research. The baseline image belongs to an outer transaction;
    // each simulated preview/failure owns an explicitly nested transaction.
    // Never end a desktop transaction or delete descriptors by filename alone.
    private static void RunBmpImageLifecycleProbe(Inventor.Application app, PartDocument doc, JObject report)
    {
        if (doc.ComponentDefinition.Sketches.Count != 1)
            throw new InvalidOperationException("BMP lifecycle requires the single original owned sketch.");
        var sketch = doc.ComponentDefinition.Sketches[1];
        string sketchName = sketch.Name;
        void Guard(bool empty)
        {
            if (app.Documents.Count != 1 || app.ActiveDocument is not PartDocument active || !TopologySame(active, doc)
                || doc.DisplayName != "S1_ArcInput_Oracle.ipt" || !string.IsNullOrEmpty(doc.FullFileName)
                || doc.ComponentDefinition.Sketches.Count != 1 || !TopologySame(doc.ComponentDefinition.Sketches[1], sketch)
                || sketch.Name != sketchName)
                throw new InvalidOperationException("Exact single unsaved owned arc fixture is required.");
            if (app.ActiveEditObject is object edit && !TopologySame(edit, sketch) && !TopologySame(edit, doc)
                && !TopologySame(edit, doc.ComponentDefinition))
                throw new InvalidOperationException("BMP lifecycle cannot change another edit context.");
            if (empty && (sketch.SketchEntities.Count != 0 || sketch.GeometricConstraints.Count != 0
                || sketch.DimensionConstraints.Count != 0 || sketch.TextBoxes.Count != 0 || sketch.SketchImages.Count != 0))
                throw new InvalidOperationException("BMP lifecycle requires the original completely empty sketch.");
        }
        Guard(true);
        if (app.ActiveEditObject is PlanarSketch ownedEdit) ownedEdit.ExitEdit();
        Guard(true);
        if (doc.ReferencedOLEFileDescriptors.Count != 0)
            throw new InvalidOperationException("Outer fixture must have zero OLE descriptors; this probe creates its own pre-existing baseline.");

        JObject Snapshot()
        {
            Guard(false);
            var state = TopologyReadback(doc, sketch);
            state["images"] = new JArray(sketch.SketchImages.Cast<SketchImage>().Select(BmpProbeImageReadback)
                .OrderBy(i => (string?)i["reference_key"], StringComparer.Ordinal));
            var ole = new List<JObject>();
            foreach (ReferencedOLEFileDescriptor descriptor in doc.ReferencedOLEFileDescriptors)
                ole.Add(new JObject { ["reference_key"] = TopologyKey(descriptor), ["filename"] = descriptor.FullFileName });
            if (ole.Any(d => string.IsNullOrEmpty((string?)d["reference_key"]))
                || ole.Select(d => (string)d["reference_key"]!).Distinct(StringComparer.Ordinal).Count() != ole.Count)
                throw new InvalidOperationException("OLE readback has empty or duplicated identities.");
            state["ole_descriptors"] = new JArray(ole.OrderBy(d => (string)d["reference_key"]!, StringComparer.Ordinal));
            state["image_count"] = sketch.SketchImages.Count;
            state["referenced_ole_count"] = doc.ReferencedOLEFileDescriptors.Count;
            return state;
        }
        HashSet<string> OleKeys() => doc.ReferencedOLEFileDescriptors.Cast<ReferencedOLEFileDescriptor>()
            .Select(TopologyKey).ToHashSet(StringComparer.Ordinal);

        report["evidence"] = "API RESEARCH ONLY: preservation of an owned pre-existing image/OLE baseline across nested Abort and explicit captured-identity repair. No product preview, desktop, Quest or physical PASS.";
        report["kind"] = doc.ComponentDefinition is SheetMetalComponentDefinition ? "sheetmetal" : "part";
        report["not_covered"] = new JArray("Undo/Redo: no desktop history navigation while the outer baseline transaction is owned", "saved documents and durable linked-file lifetime", "non-BMP formats", "product transport, permissions, preview/commit", "pre-existing OLE of other applications");
        var emptyBaseline = Snapshot(); report["baseline"] = emptyBaseline.DeepClone();
        var rows = new JArray(); report["bmp_lifecycle_cases"] = rows;
        string directory = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "s1-bmp-lifecycle-" + Guid.NewGuid().ToString("N")));
        if (Directory.Exists(directory) || File.Exists(directory)) throw new InvalidOperationException("Unique scratch directory already exists.");
        Directory.CreateDirectory(directory);
        var files = new List<string>();
        bool restoredEmpty = false;
        try
        {
            var paths = new List<string>();
            for (int n = 0; n < 4; n++)
            {
                string path = Path.GetFullPath(Path.Combine(directory, "owned-" + n + ".bmp"));
                if (Path.GetDirectoryName(path) != directory) throw new InvalidOperationException("Scratch path escapes owned directory.");
                BmpProbeWriteFile(path, n % 2 == 0 ? 24 : 32, n % 2 == 1, files); paths.Add(path);
            }

            // Capture only descriptor deltas from an isolated Add, immediately
            // in finally, including a possible native exception after mutation.
            void CaptureAdded(HashSet<string> before, HashSet<string> owned, JArray captures, string source, bool linked)
            {
                Guard(false);
                foreach (ReferencedOLEFileDescriptor descriptor in doc.ReferencedOLEFileDescriptors)
                {
                    string key = TopologyKey(descriptor);
                    if (string.IsNullOrEmpty(key)) throw new InvalidOperationException("OLE identity cannot be captured.");
                    if (before.Contains(key)) continue;
                    string filename = descriptor.FullFileName;
                    // Embedded OLE already has an empty FullFileName immediately after Add.
                    // Ownership comes from the isolated Add delta, never from matching a filename.
                    if ((linked && string.IsNullOrEmpty(filename)) || (!string.IsNullOrEmpty(filename)
                        && (!Path.IsPathRooted(filename) || !string.Equals(source, Path.GetFullPath(filename), StringComparison.OrdinalIgnoreCase))))
                        throw new InvalidOperationException("New descriptor cannot be attributed to an owned source file immediately after Add.");
                    owned.Add(key);
                    captures.Add(new JObject { ["reference_key"] = key, ["filename"] = filename,
                        ["isolated_add_source_file"] = source, ["requested_link"] = linked });
                }
            }
            JArray Repair(HashSet<string> protectedKeys, HashSet<string> owned)
            {
                Guard(false);
                var deleted = new JArray();
                var candidates = new List<ReferencedOLEFileDescriptor>();
                var protectedFiles = doc.ReferencedOLEFileDescriptors.Cast<ReferencedOLEFileDescriptor>()
                    .Where(d=>protectedKeys.Contains(TopologyKey(d))).Select(d=>d.FullFileName)
                    .Where(f=>!string.IsNullOrEmpty(f)).ToHashSet(StringComparer.OrdinalIgnoreCase);
                for (int i = doc.ReferencedOLEFileDescriptors.Count; i >= 1; i--)
                {
                    var descriptor = doc.ReferencedOLEFileDescriptors[i]; string key = TopologyKey(descriptor);
                    if (protectedKeys.Contains(key)) continue;
                    string filename = descriptor.FullFileName;
                    if (!owned.Contains(key) || (!string.IsNullOrEmpty(filename)
                        && (!Path.IsPathRooted(filename) || !files.Any(f => string.Equals(f, Path.GetFullPath(filename), StringComparison.OrdinalIgnoreCase)))))
                        throw new InvalidOperationException("Repair encountered an OLE descriptor outside captured Add ownership.");
                    // Distinct OLE keys do not prove independent underlying linked-file state.
                    // Native research shows Delete of a same-file new descriptor breaks the old image.
                    if(!string.IsNullOrEmpty(filename) && protectedFiles.Contains(filename))
                        throw new InvalidOperationException("Captured descriptor shares a linked source with protected baseline OLE; refusing deletion before any repair mutation.");
                    candidates.Add(descriptor);
                }
                foreach(var descriptor in candidates)
                {
                    deleted.Add(new JObject { ["reference_key"] = TopologyKey(descriptor), ["filename"] = descriptor.FullFileName }); descriptor.Delete();
                }
                return deleted;
            }
            foreach (bool baselineLinked in new[] { false, true })
            {
                Guard(true);
                if (!JToken.DeepEquals(emptyBaseline, Snapshot())) throw new InvalidOperationException("Outer fixture baseline changed.");
                restoredEmpty = false;
                var outerReport = new JObject { ["baseline_linked"] = baselineLinked };
                if (report["outer_baselines"] is not JArray outerRows) report["outer_baselines"] = outerRows = new JArray();
                outerRows.Add(outerReport);
                var allOwned = new HashSet<string>(StringComparer.Ordinal);
                var outer = app.TransactionManager.StartTransaction((_Document)doc, "S1 BMP lifecycle owned baseline, always aborted");
                try
                {
                    if (outer.HasParentTransaction) throw new InvalidOperationException("Outer baseline must not have a parent transaction.");
                    var before = OleKeys(); var captures = new JArray(); outerReport["captured_baseline_ole"] = captures;
                    SketchImage baselineImage;
                    try { baselineImage = sketch.SketchImages.Add(paths[0], app.TransientGeometry.CreatePoint2d(-2, -3), baselineLinked); }
                    finally { CaptureAdded(before, allOwned, captures, paths[0], baselineLinked); }
                    baselineImage.Width = baselineImage.Width * 1.25; baselineImage.Height = baselineImage.Height * 1.25;
                    baselineImage.RotateLeft(); doc.Update2(true);
                    var protectedKeys = OleKeys(); var imageBaseline = Snapshot(); outerReport["image_baseline"] = imageBaseline.DeepClone();
                    if (sketch.SketchImages.Count != 1 || protectedKeys.Count == 0 || baselineImage.LinkedToFile != baselineLinked)
                        throw new InvalidOperationException("Owned pre-existing image/OLE baseline was not created.");

                    foreach (bool addedLinked in new[] { false, true })
                    foreach (bool reuseBaselineSource in new[] { false, true })
                    foreach (var scenario in new[] { (name: "one-add-abort", count: 1, fault: 0), (name: "three-add-abort", count: 3, fault: 0),
                        (name: "fault-after-first-add", count: 3, fault: 1), (name: "fault-after-second-add", count: 3, fault: 2) })
                    {
                        if (!TopologySame(app.TransactionManager.CurrentTransaction, outer) || !JToken.DeepEquals(imageBaseline, Snapshot()))
                            throw new InvalidOperationException("Owned pre-existing image/OLE baseline or outer transaction changed before case.");
                        var row = new JObject { ["scenario"] = scenario.name, ["baseline_linked"] = baselineLinked,
                            ["added_linked"] = addedLinked, ["reuse_baseline_source_file"] = reuseBaselineSource,
                            ["expected_injected_failure_after_add"] = scenario.fault,
                            ["before"] = imageBaseline.DeepClone() }; rows.Add(row);
                        var innerOwned = new HashSet<string>(StringComparer.Ordinal); var capturesInner = new JArray(); row["captured_added_ole"] = capturesInner;
                        int added = 0; bool bodyVerified = false, injected = false;
                        var inner = app.TransactionManager.StartTransaction((_Document)doc, "S1 BMP lifecycle nested additions, always aborted");
                        try
                        {
                            if (!inner.HasParentTransaction) throw new InvalidOperationException("Inner image transaction must be nested inside our owned outer transaction.");
                            for (int n = 1; n <= scenario.count; n++)
                            {
                                var keysBeforeAdd = OleKeys(); SketchImage image;
                                try { image = sketch.SketchImages.Add(paths[reuseBaselineSource ? 0 : n], app.TransientGeometry.CreatePoint2d(n, n + 1), addedLinked); }
                                finally
                                {
                                    try { CaptureAdded(keysBeforeAdd, innerOwned, capturesInner, paths[reuseBaselineSource ? 0 : n], addedLinked); }
                                    finally { allOwned.UnionWith(innerOwned); }
                                }
                                added++;
                                // Fault immediately after capture, before Update/readback.
                                if (scenario.fault == n) throw new BmpLifecycleInjectedFailure();
                                doc.Update2(true);
                                if (!TopologySame(image.Parent, sketch) || image.LinkedToFile != addedLinked
                                    || sketch.SketchImages.Count != n + 1) throw new InvalidOperationException("Added image count, owner or link state differs.");
                            }
                            row["after_additions"] = Snapshot();
                            bodyVerified = scenario.fault == 0 && added == scenario.count;
                        }
                        catch (BmpLifecycleInjectedFailure)
                        {
                            injected = true; bodyVerified = added == scenario.fault;
                            row["expected_failure_injected"] = true; row["at_injected_failure"] = Snapshot();
                        }
                        catch (Exception ex)
                        {
                            row["native_error"] = ex.ToString(); row["hresult"] = "0x" + ex.HResult.ToString("X8");
                        }
                        finally
                        {
                            if (!TopologySame(app.TransactionManager.CurrentTransaction, inner))
                                throw new InvalidOperationException("Inner transaction is no longer current; refusing to touch another transaction.");
                            inner.Abort();
                        }
                        if (!TopologySame(app.TransactionManager.CurrentTransaction, outer))
                            throw new InvalidOperationException("Inner Abort did not return to the owned outer baseline transaction.");
                        row["successful_add_count"] = added; row["injected_failure_observed"] = injected;
                        var afterAbort = Snapshot(); row["after_abort"] = afterAbort;
                        row["exact_abort"] = JToken.DeepEquals(imageBaseline, afterAbort);
                        try { row["explicit_owned_ole_cleanup"] = Repair(protectedKeys, innerOwned); }
                        catch(Exception ex)
                        {
                            row["repair_refusal"] = ex.Message;
                            row["observation"] = "NATIVE EXCEPTION OR LIFECYCLE DIVERGENCE";
                            row["state_at_repair_refusal"] = Snapshot();
                            throw;
                        }
                        var repaired = Snapshot(); row["after_repair"] = repaired;
                        bool exact = JToken.DeepEquals(imageBaseline, repaired); row["exact_preexisting_image_and_ole_restoration"] = exact;
                        row["observation"] = bodyVerified && exact ? "API LIFECYCLE OBSERVATION COMPLETED; not product PASS" : "NATIVE EXCEPTION OR LIFECYCLE DIVERGENCE";
                        if (!exact) throw new InvalidOperationException("Nested Abort and captured-identity repair failed to restore the exact pre-existing image/OLE baseline.");
                    }
                }
                finally
                {
                    if (!TopologySame(app.TransactionManager.CurrentTransaction, outer))
                        throw new InvalidOperationException("Outer transaction is no longer current; refusing to touch another transaction.");
                    outer.Abort();
                    try { outerReport["after_outer_abort"] = Snapshot(); }
                    finally { outerReport["outer_owned_ole_cleanup"] = Repair(new HashSet<string>(StringComparer.Ordinal), allOwned); }
                    var restored = Snapshot(); outerReport["after_outer_repair"] = restored;
                    restoredEmpty = JToken.DeepEquals(emptyBaseline, restored); outerReport["exact_empty_fixture_restoration"] = restoredEmpty;
                    if (!restoredEmpty) throw new InvalidOperationException("Outer Abort and owned OLE repair failed to restore the exact empty fixture.");
                    Guard(true);
                }
            }
            report["case_count"] = rows.Count;
            report["completed_observations"] = rows.OfType<JObject>().Count(r => (string?)r["observation"] == "API LIFECYCLE OBSERVATION COMPLETED; not product PASS");
            report["native_exceptions_or_divergences"] = rows.Count - (int)report["completed_observations"]!;
            report["exact_nested_abort_count"] = rows.OfType<JObject>().Count(r => (bool?)r["exact_abort"] == true);
            report["exact_preexisting_restoration_count"] = rows.OfType<JObject>().Count(r => (bool?)r["exact_preexisting_image_and_ole_restoration"] == true);
            report["final_state"] = Snapshot();
        }
        finally
        {
            report["case_count"] = rows.Count;
            report["completed_observations"] = rows.OfType<JObject>().Count(r => (string?)r["observation"] == "API LIFECYCLE OBSERVATION COMPLETED; not product PASS");
            report["native_exceptions_or_divergences"] = rows.Count - (int)report["completed_observations"]!;
            report["exact_nested_abort_count"] = rows.OfType<JObject>().Count(r => (bool?)r["exact_abort"] == true);
            report["exact_preexisting_restoration_count"] = rows.OfType<JObject>().Count(r => (bool?)r["exact_preexisting_image_and_ole_restoration"] == true);
            // If restoration failed, preserve source files for diagnosis rather
            // than leave any remaining native linked descriptor dangling.
            var errors = new JArray(); report["temp_cleanup_errors"] = errors;
            report["temp_files_retained_for_failed_restoration"] = !restoredEmpty;
            if (restoredEmpty)
            {
                foreach (string file in files)
                    try { File.Delete(file); } catch (Exception ex) { errors.Add(new JObject { ["file"] = file, ["error"] = ex.ToString() }); }
                try { Directory.Delete(directory, false); } catch (Exception ex) { errors.Add(new JObject { ["directory"] = directory, ["error"] = ex.ToString() }); }
            }
            report["temp_cleanup_complete"] = restoredEmpty && errors.Count == 0;
            if (errors.Count != 0) throw new InvalidOperationException("Owned BMP lifecycle scratch cleanup was incomplete.");
        }
    }

    private sealed class BmpLifecycleInjectedFailure : Exception { }
}
