using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Bimwright.Ipt.Server.Assets;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;
using Bimwright.Ipt.Shared.Plugin;
using Inventor;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using File = System.IO.File;
using Path = System.IO.Path;
using Environment = System.Environment;

/// <summary>
/// M5 live probe: closes technical decisions 1 (real flat pattern geometry as a separate mesh,
/// without leaving Inventor in Flat Pattern Edit) and 4 (interaction budget) of the M5 spec against a
/// running Inventor 2027. Windows only. It builds its own temporary sheet-metal fixtures, never touches
/// another document, and closes them unsaved unless --keep is given.
/// </summary>
internal static class Program
{
    [DllImport("oleaut32.dll", PreserveSig = false)]
    private static extern void GetActiveObject(ref Guid clsid, IntPtr reserved,
        [MarshalAs(UnmanagedType.IUnknown)] out object app);

    [STAThread]
    private static int Main(string[] args)
    {
        string artifacts = ArgValue(args, "--artifacts-path") ?? Path.Combine("artifacts", "m5-live-probe");
        int repetitions = int.TryParse(ArgValue(args, "--repetitions"), out var n) && n >= 1 && n <= 50 ? n : 5;
        var report = new Report();
        Probe.Declare(report);
        try
        {
            var clsid = Type.GetTypeFromProgID("Inventor.Application")!.GUID;
            GetActiveObject(ref clsid, IntPtr.Zero, out var running);
            var app = (global::Inventor.Application)running;
            new Probe(app, report, repetitions, args.Contains("--keep")).Run();
        }
        catch (Exception ex)
        {
            report.Findings["fatal"] = ex.ToString();
            report.NotRunAll(Probe.AllIds, "probe could not start: " + ex.Message);
            Console.Error.WriteLine(ex);
        }
        Directory.CreateDirectory(artifacts);
        var header = new JObject
        {
            ["utc"] = DateTime.UtcNow.ToString("o"), ["repetitions"] = repetitions, ["keep"] = args.Contains("--keep"),
            ["dimension_tolerance_mm"] = Probe.DimensionToleranceMm,
        };
        File.WriteAllText(Path.Combine(artifacts, "m5-live-probe-report.json"), report.ToJson(header).ToString(Formatting.Indented));
        File.WriteAllText(Path.Combine(artifacts, "m5-live-probe-summary.txt"), report.ToText(header));
        Console.WriteLine();
        Console.WriteLine(report.ToText(header));
        Console.WriteLine("Report: " + Path.GetFullPath(artifacts));
        // Nonzero for a failure and for anything left unexercised: NOT_RUN is not acceptance evidence.
        return report.Count("FAIL") > 0 ? 1 : report.Count("NOT_RUN") > 0 ? 2 : 0;
    }

    private static string? ArgValue(string[] args, string name)
    {
        int index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}

internal sealed class Probe
{
    public const double DimensionToleranceMm = 0.05;
    private const string SheetMetalSubType = "{9C464203-9BAE-11D3-8BAD-0060B0CE6BB4}";
    private const double FlangeHeightMm = 23.7;      // unique value: the parameter behind it is found by value
    private const double EditedFlangeHeightMm = 30.0;

    public static readonly string[] AllIds =
    {
        "FIXTURE", "D1A_REFUSED_WITHOUT_FLAT_PATTERN", "D1B_MESH_PRESENT", "D1B_GEOMETRY_SOURCE", "D1B_EDIT_STATE",
        "D1B_FOLDED_MODEL_UNCHANGED", "D1B_BBOX_VS_INVENTOR", "D1B_THICKNESS", "D1B_ORIENTATION_REPORTED", "D1B_UNITS_AND_GLB",
        "D1C_IDENTITY_STABLE", "D1C_MESH_HASH_STABLE", "D1D_IDENTITY_CHANGES_AFTER_EDIT", "D1E_MULTI_BODY_REFUSED",
        "D1E_NON_SHEET_METAL_REFUSED", "D4_FLANGE_PREVIEW_TIMING", "D4_DISPLAY_MESH_TIMING", "D4_CREATE_FLAT_PATTERN_TIMING",
        "D4_FLAT_MESH_TIMING", "D4_SIZES",
    };

    public static void Declare(Report report)
    {
        report.Declare("FIXTURE", "Own sheet-metal fixture built (base face + one flange on a real edge)");
        report.Declare("D1A_REFUSED_WITHOUT_FLAT_PATTERN", "D1a get_flat_pattern_mesh before any flat pattern is refused and creates nothing");
        report.Declare("D1B_MESH_PRESENT", "D1b flat pattern mesh returned after create_flat_pattern");
        report.Declare("D1B_GEOMETRY_SOURCE", "D1b geometry source (SurfaceBodies / Body) is reported");
        report.Declare("D1B_EDIT_STATE", "D1b the read leaves no Flat Pattern Edit (handler and independent read)");
        report.Declare("D1B_FOLDED_MODEL_UNCHANGED", "D1b folded model body count, volume, features, range box and revisions unchanged by the read");
        report.Declare("D1B_BBOX_VS_INVENTOR", "D1b mesh bounding box matches Inventor's flat length and width");
        report.Declare("D1B_THICKNESS", "D1b mesh thin dimension matches the sheet thickness");
        report.Declare("D1B_ORIENTATION_REPORTED", "D1b flat plane, thickness side and origin of the mesh are determinable");
        report.Declare("D1B_UNITS_AND_GLB", "D1b payload is centimetres and the server GLB is metres with the same extents");
        report.Declare("D1C_IDENTITY_STABLE", "D1c a second read gives an identical flat_pattern_identity");
        report.Declare("D1C_MESH_HASH_STABLE", "D1c a second read gives an identical mesh hash and GLB");
        report.Declare("D1D_IDENTITY_CHANGES_AFTER_EDIT", "D1d identity changes after a committed flange height edit");
        report.Declare("D1E_MULTI_BODY_REFUSED", "D1e multi-body sheet-metal part is refused");
        report.Declare("D1E_NON_SHEET_METAL_REFUSED", "D1e ordinary part is refused with WRONG_DOCUMENT_TYPE");
        report.Declare("D4_FLANGE_PREVIEW_TIMING", "D4 flange preview (atomic batch preview, rollback, preview mesh) timing");
        report.Declare("D4_DISPLAY_MESH_TIMING", "D4 folded get_display_mesh timing at 0.1 and 0.5 mm");
        report.Declare("D4_CREATE_FLAT_PATTERN_TIMING", "D4 create_flat_pattern timing");
        report.Declare("D4_FLAT_MESH_TIMING", "D4 get_flat_pattern_mesh timing at 0.1 and 0.5 mm");
        report.Declare("D4_SIZES", "D4 triangle counts and payload/GLB byte sizes recorded");
    }

    private readonly global::Inventor.Application _app;
    private readonly Report _report;
    private readonly int _reps;
    private readonly bool _keep;
    private readonly List<PartDocument> _owned = new();
    private CadEventJournal _journal = null!;
    private InventorCommandContext _context = null!;
    private IReadOnlyDictionary<string, IInventorCommand> _commands = null!;

    public Probe(global::Inventor.Application app, Report report, int reps, bool keep)
    {
        _app = app; _report = report; _reps = reps; _keep = keep;
    }

    // ---------------------------------------------------------------- plumbing

    private sealed record Result(bool Ok, JObject Data, string? Code, string? Message, JObject? Details, double Ms);

    private static string DocId(global::Inventor.Document doc) => "doc_" + doc.InternalName;
    private static string DocId(PartDocument doc) => "doc_" + doc.InternalName;

    private static void Pump()
    {
        for (int i = 0; i < 3; i++) { System.Windows.Forms.Application.DoEvents(); Thread.Sleep(20); }
    }

    private Result Exec(string command, JObject args)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var response = _commands[command].Execute(_context, args);
        double ms = watch.Elapsed.TotalMilliseconds;
        return new Result(response.Ok, response.Ok ? (JObject)response.Data! : new JObject(),
            response.Error?.Code, response.Error?.Message, response.Error?.Details, ms);
    }

    private static string Describe(Result result) => result.Ok ? "ok" : result.Code + ": " + result.Message + (result.Details == null ? "" : " " + result.Details.ToString(Formatting.None));

    private Result Batch(PartDocument doc, JArray operations, bool preview, bool previewMesh)
    {
        doc.Activate();
        string id = DocId(doc);
        var args = new JObject
        {
            ["document_id"] = id, ["expected_revision"] = _journal.Revision(id), ["operations"] = operations,
            ["preview"] = preview, ["include_preview_mesh"] = previewMesh, ["validate"] = new JArray("rebuild", "feature_health"),
        };
        var result = Exec("atomic_batch", args);
        Pump();
        return result;
    }

    private static JObject Op(string command, JObject arguments) => new() { ["command"] = command, ["arguments"] = arguments };

    private Result FlatRead(PartDocument doc, double toleranceMm)
        => Exec("get_flat_pattern_mesh", new JObject { ["document_id"] = DocId(doc), ["tolerance_mm"] = toleranceMm });

    private PartDocument NewPart(bool sheetMetal)
    {
        string template = sheetMetal
            ? _app.FileManager.GetTemplateFile(DocumentTypeEnum.kPartDocumentObject, SystemOfMeasureEnum.kDefaultSystemOfMeasure,
                DraftingStandardEnum.kDefault_DraftingStandard, SheetMetalSubType)
            : _app.FileManager.GetTemplateFile(DocumentTypeEnum.kPartDocumentObject);
        var doc = (PartDocument)_app.Documents.Add(DocumentTypeEnum.kPartDocumentObject, template, true);
        _owned.Add(doc);
        if (sheetMetal && doc.ComponentDefinition is not SheetMetalComponentDefinition)
            throw new InvalidOperationException("The sheet-metal template did not produce a sheet-metal part.");
        Pump();
        return doc;
    }

    private static SheetMetalComponentDefinition Def(PartDocument doc) => (SheetMetalComponentDefinition)doc.ComponentDefinition;

    private sealed record Folded(int Bodies, double VolumeCm3, string Revision, string Visual, int Features, double[] Range);

    private Folded Snap(PartDocument doc)
    {
        var def = Def(doc);
        dynamic box = def.RangeBox;
        dynamic lo = box.MinPoint, hi = box.MaxPoint;
        string id = DocId(doc);
        return new Folded(def.SurfaceBodies.Count, def.MassProperties.Volume, _journal.Revision(id), _journal.VisualRevision(id),
            (int)((dynamic)def).Features.Count, new[] { (double)lo.X, (double)lo.Y, (double)lo.Z, (double)hi.X, (double)hi.Y, (double)hi.Z });
    }

    private static string? Differences(Folded a, Folded b)
    {
        var diffs = new List<string>();
        if (a.Bodies != b.Bodies) diffs.Add("body count " + a.Bodies + " -> " + b.Bodies);
        if (Math.Abs(a.VolumeCm3 - b.VolumeCm3) > 1e-9 * Math.Max(1, Math.Abs(a.VolumeCm3))) diffs.Add("volume " + a.VolumeCm3 + " -> " + b.VolumeCm3);
        if (a.Features != b.Features) diffs.Add("feature count " + a.Features + " -> " + b.Features);
        if (a.Revision != b.Revision) diffs.Add("revision " + a.Revision + " -> " + b.Revision);
        if (a.Visual != b.Visual) diffs.Add("visual_revision " + a.Visual + " -> " + b.Visual);
        for (int i = 0; i < 6; i++) if (Math.Abs(a.Range[i] - b.Range[i]) > 1e-7) { diffs.Add("range box"); break; }
        return diffs.Count == 0 ? null : string.Join("; ", diffs);
    }

    /// <summary>Independent of the handler: ObjectTypeEnum of what Inventor calls the activated / active edit object.</summary>
    private string EditKind(PartDocument doc)
    {
        string Read(Func<object?> read)
        {
            try
            {
                var value = read();
                if (value == null) return "none";
                dynamic typed = value;
                return ((object)typed.Type).ToString() ?? "unknown";
            }
            catch { return "unavailable"; }
        }
        return "activated=" + Read(() => ((dynamic)doc).ActivatedObject) + "; active_edit=" + Read(() => ((dynamic)_app).ActiveEditObject);
    }

    private static double[] SortedSizes(JObject data)
    {
        var size = (JArray)data["mesh_bbox_mm"]!["size_mm"]!;
        return size.Select(v => (double)v).OrderByDescending(v => v).ToArray();
    }

    private static byte[] Glb(JObject data)
    {
        var bodies = ((JArray)data["bodies"]!).OfType<JObject>().Select(MeshPayload.FromJson).ToArray();
        return GlbBuilder.BuildDefinition(new GlbBuilder.MeshSource
        {
            Name = (string?)data["definition_name"] ?? "mesh", DocumentId = (string?)data["document_id"], Bodies = bodies,
        });
    }

    /// <summary>Union of POSITION accessor extents of a GLB in millimetres (the GLB is in metres).</summary>
    private static double[] GlbSizeMm(byte[] glb)
    {
        int jsonLength = BitConverter.ToInt32(glb, 12);
        var gltf = JObject.Parse(Encoding.UTF8.GetString(glb, 20, jsonLength));
        double[] lo = { double.MaxValue, double.MaxValue, double.MaxValue }, hi = { double.MinValue, double.MinValue, double.MinValue };
        foreach (var primitive in gltf["meshes"]!.SelectMany(m => m["primitives"]!))
        {
            var accessor = gltf["accessors"]![(int)primitive["attributes"]!["POSITION"]!]!;
            for (int i = 0; i < 3; i++)
            {
                lo[i] = Math.Min(lo[i], (double)accessor["min"]![i]!);
                hi[i] = Math.Max(hi[i], (double)accessor["max"]![i]!);
            }
        }
        return new[] { (hi[0] - lo[0]) * 1000, (hi[1] - lo[1]) * 1000, (hi[2] - lo[2]) * 1000 };
    }

    private static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static int PayloadBytes(JObject data) => Encoding.UTF8.GetByteCount(data.ToString(Formatting.None));

    // ---------------------------------------------------------------- run

    public void Run()
    {
        var original = _app.ActiveDocument;
        var trackerType = typeof(InventorCommandRegistry).Assembly.GetType("Bimwright.Ipt.Shared.Plugin.CadEventTracker", true)!;
        var tracker = (IDisposable)Activator.CreateInstance(trackerType, _app)!;
        _journal = (CadEventJournal)trackerType.GetProperty("Journal")!.GetValue(tracker)!;
        _commands = InventorCommandRegistry.Build(new PluginOptions(2027, false, false, 0));
        _context = new InventorCommandContext { Application = _app, Events = _journal, Commands = _commands, AllowExperimental = true, InventorYear = 2027 };
        _report.Findings["inventor_version"] = _app.SoftwareVersion.DisplayVersion;
        _report.Findings["experimental_handler_registered"] = _commands.ContainsKey("get_flat_pattern_mesh");
        try
        {
            if (!_commands.ContainsKey("get_flat_pattern_mesh"))
                throw new InvalidOperationException("get_flat_pattern_mesh is not registered: build the add-in with -p:SoExperimental=true into bin/M5/net48/.");
            RunFixtureStages();
            RunPlainPartStage();
        }
        catch (Exception ex)
        {
            _report.Findings["fatal"] = ex.ToString();
            _report.NotRunAll(AllIds, "stage aborted: " + ex.Message);
            Console.Error.WriteLine(ex);
        }
        finally
        {
            tracker.Dispose();
            foreach (var doc in _owned.AsEnumerable().Reverse())
            {
                if (_keep) continue;
                try { doc.Close(true); } catch (Exception ex) { _report.Findings["close_failed_" + doc.DisplayName] = ex.Message; }
            }
            try { original?.Activate(); } catch { /* the original document may itself have been closed by the user */ }
        }
    }

    private void RunFixtureStages()
    {
        var doc = NewPart(true);
        var def = Def(doc);
        string id = DocId(doc);
        double thicknessMm;
        string? edgeId;
        JArray flangeOps;

        // ---- fixture: base face from a closed rectangle, then a flange on a real edge
        try
        {
            var build = Batch(doc, new JArray(
                Op("create_sketch", new JObject { ["plane"] = "XY", ["name"] = "M5_Base" }),
                Op("draw_rectangle", new JObject { ["sketch_name"] = "M5_Base", ["x1"] = 0, ["y1"] = 0, ["x2"] = 100, ["y2"] = 60 }),
                Op("close_sketch", new JObject { ["sketch_name"] = "M5_Base" }),
                Op("sheet_metal_face", new JObject { ["sketch_name"] = "M5_Base" })), false, false);
            if (!build.Ok) throw new InvalidOperationException("base face: " + Describe(build));
            thicknessMm = SheetMetalThicknessMm(def) ?? throw new InvalidOperationException("the sheet thickness could not be read from the definition.");
            if (def.SurfaceBodies.Count != 1) throw new InvalidOperationException("base face produced " + def.SurfaceBodies.Count + " bodies.");
            (edgeId, flangeOps) = FindFlangeEdge(doc, id);
            if (edgeId == null) throw new InvalidOperationException("no flange edge accepted a preview (see findings.flange_edge_attempts).");
        }
        catch (Exception ex)
        {
            _report.Fail("FIXTURE", ex.Message);
            _report.NotRunAll(AllIds, "fixture unavailable: " + ex.Message);
            return;
        }

        // ---- D4: flange preview (the Design XR path: atomic preview + mesh + rollback) on the base face
        MeasureFlangePreview(doc, flangeOps);

        var commit = Batch(doc, flangeOps, false, false);
        if (!commit.Ok)
        {
            _report.Fail("FIXTURE", "flange commit: " + Describe(commit));
            _report.NotRunAll(AllIds, "fixture unavailable: flange commit failed");
            return;
        }
        _report.Pass("FIXTURE", "sheet-metal part " + doc.DisplayName + ": 100 x 60 mm base, thickness " + Timing.Fixed(thicknessMm) + " mm, flange " + FlangeHeightMm + " mm; bodies=" + def.SurfaceBodies.Count,
            new JObject { ["thickness_mm"] = thicknessMm, ["flange_edge_id"] = edgeId });

        // ---- D1a: no flat pattern yet
        var beforeRefusal = Snap(doc);
        var refused = FlatRead(doc, 0.1);
        bool refusedOk = !refused.Ok && refused.Code == InventorErrorCodes.INVALID_ARGUMENT
            && (string?)refused.Details?["reason"] == "FLAT_PATTERN_MISSING" && !def.HasFlatPattern && Differences(beforeRefusal, Snap(doc)) == null;
        _report.Verdict("D1A_REFUSED_WITHOUT_FLAT_PATTERN", refusedOk,
            "result=" + Describe(refused) + "; HasFlatPattern=" + def.HasFlatPattern + "; folded model " + (Differences(beforeRefusal, Snap(doc)) ?? "unchanged"));

        // ---- D4: create_flat_pattern (preview first: it must leave no pattern behind), then the real one
        MeasureCreateFlatPattern(doc);
        if (!def.HasFlatPattern)
        {
            var create = Batch(doc, new JArray(Op("create_flat_pattern", new JObject())), false, false);
            _report.Measurements["create_flat_pattern_commit"] = Timing.Stats(new[] { create.Ms });
            if (!create.Ok) _report.Findings["create_flat_pattern_commit_error"] = Describe(create);
        }
        if (!def.HasFlatPattern)
        {
            _report.Fail("D1B_MESH_PRESENT", "create_flat_pattern did not produce a flat pattern; D1b-D1d cannot run.");
            _report.NotRunAll(AllIds.Where(x => x.StartsWith("D1B_") || x.StartsWith("D1C_") || x.StartsWith("D1D_") || x.StartsWith("D4_FLAT")), "no flat pattern");
        }
        else RunReadStages(doc, thicknessMm);

        RunMultiBodyStage();
    }

    private static double Median(List<double> samples) => (double)Timing.Stats(samples)["median_ms"]!;

    private static double? SheetMetalThicknessMm(SheetMetalComponentDefinition def)
    {
        try { return Convert.ToDouble(((dynamic)def).Thickness.Value) * 10; } catch { return null; }
    }

    private (string? edgeId, JArray operations) FindFlangeEdge(PartDocument doc, string id)
    {
        var attempts = new JArray();
        _report.Findings["flange_edge_attempts"] = attempts;
        var context = Exec("get_design_context_xr", new JObject { ["document_id"] = id, ["expected_revision"] = _journal.Revision(id) });
        if (!context.Ok) throw new InvalidOperationException("get_design_context_xr: " + Describe(context));
        var lines = ((JArray)context.Data["edges"]!).OfType<JObject>()
            .Where(e => ((string?)e["kind"] ?? "").Contains("Line") && Math.Abs(((double?)e["length_mm"] ?? 0) - 100) < 0.01)
            .ToArray();
        bool AtY60(JObject e) => e["points_mm"] is JArray p && p.Count >= 6 && Math.Abs((double)p[1] - 60) < 0.01 && Math.Abs((double)p[p.Count - 2] - 60) < 0.01;
        foreach (var edge in lines.Where(AtY60).Concat(lines.Where(e => !AtY60(e))).Take(4))
        {
            var operations = new JArray(Op("sheet_metal_flange", new JObject
            {
                ["edge_ids"] = new JArray((string)edge["id"]!), ["height_mm"] = FlangeHeightMm, ["angle_degrees"] = 90,
            }));
            var trial = Batch(doc, operations, true, false);
            attempts.Add(new JObject { ["edge_id"] = edge["id"], ["ok"] = trial.Ok, ["detail"] = trial.Ok ? null : Describe(trial) });
            if (trial.Ok) return ((string)edge["id"]!, operations);
        }
        return (null, new JArray());
    }

    private void MeasureFlangePreview(PartDocument doc, JArray operations)
    {
        try
        {
            var before = Snap(doc);
            var samples = new List<double>();
            string? problem = null;
            for (int i = 0; i < _reps && problem == null; i++)
            {
                var preview = Batch(doc, operations, true, true);
                samples.Add(preview.Ms);
                if (!preview.Ok) { problem = "preview failed: " + Describe(preview); break; }
                if ((string?)preview.Data["status"] != "preview_rolled_back") problem = "status " + preview.Data["status"];
                if (i == 0)
                {
                    var mesh = preview.Data["preview_mesh"] as JObject;
                    _report.Measurements["flange_preview_mesh_payload"] = new JObject
                    {
                        ["payload_bytes"] = mesh == null ? null : PayloadBytes(mesh),
                        ["triangles"] = mesh?["triangle_count"], ["bodies"] = (mesh?["bodies"] as JArray)?.Count,
                    };
                }
                var drift = Differences(before, Snap(doc));
                if (drift != null) problem = "preview left residue: " + drift;
            }
            _report.Measurements["flange_preview_with_mesh"] = Timing.Stats(samples);
            _report.Verdict("D4_FLANGE_PREVIEW_TIMING", problem == null && samples.Count == _reps,
                problem ?? (samples.Count + " previews, model unchanged after each; median " + Timing.Fixed(Median(samples), 1) + " ms"));
        }
        catch (Exception ex) { _report.Fail("D4_FLANGE_PREVIEW_TIMING", ex.Message); }
    }

    private void MeasureCreateFlatPattern(PartDocument doc)
    {
        try
        {
            var def = Def(doc);
            var samples = new List<double>();
            string? problem = null;
            bool? rolledBackCleanly = null;
            for (int i = 0; i < _reps; i++)
            {
                var preview = Batch(doc, new JArray(Op("create_flat_pattern", new JObject())), true, false);
                if (!preview.Ok) { problem = "preview failed: " + Describe(preview); break; }
                samples.Add(preview.Ms);
                rolledBackCleanly = !def.HasFlatPattern;
                if (def.HasFlatPattern) { problem = "preview did not roll the flat pattern back; the pattern stays and the remaining repetitions were skipped."; break; }
            }
            _report.Findings["create_flat_pattern_preview_rolled_back_cleanly"] = rolledBackCleanly;
            if (samples.Count > 0) _report.Measurements["create_flat_pattern_preview"] = Timing.Stats(samples);
            if (samples.Count > 0 && problem == null)
                _report.Pass("D4_CREATE_FLAT_PATTERN_TIMING", samples.Count + " previews rolled back cleanly; median " + Timing.Fixed(Median(samples), 1) + " ms (committed creation is measured separately)");
            else if (samples.Count > 0) _report.Fail("D4_CREATE_FLAT_PATTERN_TIMING", problem!);
            else _report.Fail("D4_CREATE_FLAT_PATTERN_TIMING", problem ?? "no sample");
        }
        catch (Exception ex) { _report.Fail("D4_CREATE_FLAT_PATTERN_TIMING", ex.Message); }
    }

    private void RunReadStages(PartDocument doc, double thicknessMm)
    {
        var def = Def(doc);
        // ---- D1b: first read
        var beforeState = Snap(doc);
        string editBeforeIndependent = EditKind(doc);
        var first = FlatRead(doc, 0.1);
        string editAfterIndependent = EditKind(doc);
        var afterState = Snap(doc);
        if (!first.Ok)
        {
            _report.Fail("D1B_MESH_PRESENT", Describe(first));
            _report.NotRunAll(AllIds.Where(x => x.StartsWith("D1B_") || x.StartsWith("D1C_") || x.StartsWith("D1D_") || x.StartsWith("D4_FLAT") || x == "D4_SIZES"), "first read failed");
            return;
        }
        var data = first.Data;
        var slim = (JObject)data.DeepClone(); slim["bodies"] = "(omitted: " + PayloadBytes(data) + " bytes)";
        _report.Findings["flat_pattern_read_1"] = slim;

        int triangles = (int)data["triangle_count"]!;
        _report.Verdict("D1B_MESH_PRESENT", triangles > 0 && ((JArray)data["bodies"]!).Count > 0,
            triangles + " triangles in " + ((JArray)data["bodies"]!).Count + " body(ies); " + Timing.Fixed(first.Ms, 1) + " ms");

        string? source = (string?)data["source"];
        _report.Verdict("D1B_GEOMETRY_SOURCE", source != null, "source=" + source, new JObject { ["attempts"] = data["source_attempts"] });

        var editBefore = (JObject)data["edit_state_before"]!; var editAfter = (JObject)data["edit_state_after"]!;
        bool? handlerFlag = (bool?)editAfter["flat_pattern_edit_active"];
        bool independentActive = editAfterIndependent.Contains("FlatPattern");
        var editData = new JObject
        {
            ["handler_before"] = editBefore, ["handler_after"] = editAfter, ["forced_by_read"] = data["edit_mode_forced_by_read"],
            ["left_edit_mode_restored"] = data["left_edit_mode_restored"], ["independent_before"] = editBeforeIndependent, ["independent_after"] = editAfterIndependent,
        };
        if (handlerFlag == null && editAfterIndependent.Contains("unavailable"))
            _report.NotRun("D1B_EDIT_STATE", "edit state unreadable through ActivatedObject/ActiveEditObject on this build; the API assumption is not confirmed.");
        else
            _report.Verdict("D1B_EDIT_STATE", handlerFlag != true && !independentActive,
                "handler after=" + handlerFlag + ", forced_by_read=" + data["edit_mode_forced_by_read"] + ", restored=" + data["left_edit_mode_restored"] + "; independent after: " + editAfterIndependent, editData);

        var drift = Differences(beforeState, afterState);
        _report.Verdict("D1B_FOLDED_MODEL_UNCHANGED", drift == null, drift ?? "bodies=" + afterState.Bodies + ", volume=" + afterState.VolumeCm3 + " cm3, revision " + afterState.Revision + " unchanged");

        // ---- D1b: extents vs Inventor's own numbers
        double length = (double)data["flat_pattern"]!["length_mm"]!, width = (double)data["flat_pattern"]!["width_mm"]!;
        var sizes = SortedSizes(data);
        double dLong = sizes[0] - Math.Max(length, width), dShort = sizes[1] - Math.Min(length, width), dThick = sizes[2] - thicknessMm;
        var bbox = (JObject)data["mesh_bbox_mm"]!;
        var dims = new JObject
        {
            ["inventor_length_mm"] = length, ["inventor_width_mm"] = width, ["mesh_sizes_desc_mm"] = new JArray(sizes),
            ["delta_long_mm"] = dLong, ["delta_short_mm"] = dShort, ["delta_thickness_mm"] = dThick, ["thickness_mm"] = thicknessMm,
            ["range_box_mm"] = data["range_box_mm"], ["mesh_bbox_mm"] = bbox,
        };
        _report.Verdict("D1B_BBOX_VS_INVENTOR", Math.Abs(dLong) <= DimensionToleranceMm && Math.Abs(dShort) <= DimensionToleranceMm,
            "length delta " + Timing.Fixed(dLong) + " mm, width delta " + Timing.Fixed(dShort) + " mm (tolerance " + DimensionToleranceMm + ")", dims);
        _report.Verdict("D1B_THICKNESS", Math.Abs(dThick) <= DimensionToleranceMm,
            "thin extent " + Timing.Fixed(sizes[2]) + " mm vs thickness " + Timing.Fixed(thicknessMm) + " mm", dims);

        string thinAxis = (string)bbox["thin_axis"]!;
        int axis = "XYZ".IndexOf(thinAxis, StringComparison.Ordinal);
        double thinMin = (double)bbox["min_mm"]![axis]!, thinMax = (double)bbox["max_mm"]![axis]!;
        string side = Math.Abs(thinMin + thinMax) < 0.01 ? "centred on 0" : thinMin >= -0.01 ? "on the positive side of 0" : thinMax <= 0.01 ? "on the negative side of 0" : "straddles 0 off-centre";
        _report.Pass("D1B_ORIENTATION_REPORTED",
            "thin axis " + thinAxis + " => flat plane " + ("XYZ".Replace(thinAxis, "")) + "; thickness " + side + " (" + Timing.Fixed(thinMin) + ".." + Timing.Fixed(thinMax) + " mm); mesh min corner " + bbox["min_mm"]!.ToString(Formatting.None),
            new JObject { ["thin_axis"] = thinAxis, ["thickness_side"] = side, ["min_mm"] = bbox["min_mm"], ["max_mm"] = bbox["max_mm"], ["alignment"] = data["flat_pattern"]!["alignment"] });

        // ---- D1b: units and the server GLB
        var glb = Glb(data);
        var glbSizes = GlbSizeMm(glb);
        double unitDelta = Math.Max(Math.Abs(glbSizes[0] - ((JArray)bbox["size_mm"]!)[0]!.Value<double>()),
            Math.Max(Math.Abs(glbSizes[1] - ((JArray)bbox["size_mm"]!)[1]!.Value<double>()), Math.Abs(glbSizes[2] - ((JArray)bbox["size_mm"]!)[2]!.Value<double>())));
        _report.Verdict("D1B_UNITS_AND_GLB", (string?)data["units"] == "cm" && unitDelta < 0.01,
            "units=" + data["units"] + "; GLB (metres) extents in mm differ from the payload bbox by " + Timing.Fixed(unitDelta, 4) + " mm; GLB " + glb.Length + " bytes");

        // ---- D1c: cache stability
        var second = FlatRead(doc, 0.1);
        if (!second.Ok) _report.NotRunAll(new[] { "D1C_IDENTITY_STABLE", "D1C_MESH_HASH_STABLE" }, "second read failed: " + Describe(second));
        else
        {
            var a = (JObject)data["flat_pattern_identity"]!; var b = (JObject)second.Data["flat_pattern_identity"]!;
            var differing = a.Properties().Where(p => !JToken.DeepEquals(p.Value, b[p.Name])).Select(p => p.Name).ToArray();
            _report.Verdict("D1C_IDENTITY_STABLE", differing.Length == 0, differing.Length == 0 ? "identical (hash " + a["hash"] + ")" : "differs in: " + string.Join(", ", differing),
                new JObject { ["first"] = a, ["second"] = b });
            bool sameMeshHash = (string?)data["mesh_hash"] == (string?)second.Data["mesh_hash"];
            bool sameGlb = Sha(glb) == Sha(Glb(second.Data));
            _report.Verdict("D1C_MESH_HASH_STABLE", sameMeshHash && sameGlb,
                "ordered mesh hash " + (sameMeshHash ? "identical" : "DIFFERS") + ", GLB bytes " + (sameGlb ? "identical" : "DIFFER") +
                "; vertex_set_hash " + (a["vertex_set_hash"]?.ToString() == b["vertex_set_hash"]?.ToString() ? "identical" : "DIFFERS"));
        }

        MeasureReadTimings(doc);

        // ---- D1d: committed edit changes the identity
        RunEditStage(doc, data);
    }

    private void MeasureReadTimings(PartDocument doc)
    {
        var sizes = new JObject();
        try
        {
            var flatOk = true;
            foreach (double tolerance in new[] { 0.1, 0.5 })
            {
                var samples = new List<double>(); JObject? last = null;
                for (int i = 0; i < _reps; i++)
                {
                    var read = FlatRead(doc, tolerance);
                    if (!read.Ok) { flatOk = false; _report.Findings["flat_read_error_" + tolerance] = Describe(read); break; }
                    samples.Add(read.Ms); last = read.Data;
                }
                _report.Measurements["get_flat_pattern_mesh_tol_" + tolerance + "mm"] = Timing.Stats(samples);
                if (last != null) sizes["flat_pattern_tol_" + tolerance + "mm"] = SizeRow(last);
            }
            _report.Verdict("D4_FLAT_MESH_TIMING", flatOk, flatOk ? "timed at 0.1 and 0.5 mm, " + _reps + " runs each (handler only: no pipe, no GLB)" : "a read failed, see findings");

            bool foldedOk = true;
            foreach (double tolerance in new[] { 0.1, 0.5 })
            {
                var samples = new List<double>(); JObject? last = null;
                for (int i = 0; i < _reps; i++)
                {
                    var read = Exec("get_display_mesh", new JObject { ["document_id"] = DocId(doc), ["tolerance_mm"] = tolerance, ["include_face_ids"] = true });
                    if (!read.Ok) { foldedOk = false; _report.Findings["display_mesh_error_" + tolerance] = Describe(read); break; }
                    samples.Add(read.Ms); last = read.Data;
                }
                _report.Measurements["get_display_mesh_folded_tol_" + tolerance + "mm"] = Timing.Stats(samples);
                if (last != null) sizes["folded_display_mesh_tol_" + tolerance + "mm"] = SizeRow(last);
            }
            _report.Verdict("D4_DISPLAY_MESH_TIMING", foldedOk, foldedOk ? "timed at 0.1 and 0.5 mm, " + _reps + " runs each (face ids on)" : "a read failed, see findings");
            _report.Measurements["sizes"] = sizes;
            _report.Verdict("D4_SIZES", sizes.Count == 4, sizes.Count + " of 4 size rows recorded (triangles, payload bytes, GLB bytes)", sizes);
        }
        catch (Exception ex)
        {
            _report.Findings["measure_read_timings_error"] = ex.ToString();
            _report.NotRunAll(new[] { "D4_FLAT_MESH_TIMING", "D4_DISPLAY_MESH_TIMING", "D4_SIZES" }, "measurement aborted: " + ex.Message);
        }
    }

    private JObject SizeRow(JObject data)
    {
        var glbWatch = System.Diagnostics.Stopwatch.StartNew();
        var glb = Glb(data);
        double glbMs = glbWatch.Elapsed.TotalMilliseconds;
        return new JObject
        {
            ["triangles"] = data["triangle_count"], ["json_payload_bytes"] = PayloadBytes(data), ["glb_bytes"] = glb.Length, ["glb_build_ms"] = Math.Round(glbMs, 1),
        };
    }

    private void RunEditStage(PartDocument doc, JObject firstRead)
    {
        try
        {
            var def = Def(doc);
            string id = DocId(doc);
            var parameter = FindFlangeHeightParameter(def);
            string route;
            Result? committed = null;
            doc.Activate();
            if (parameter != null)
            {
                route = "set_parameter " + parameter + " = " + EditedFlangeHeightMm + " mm";
                committed = Batch(doc, new JArray(Op("set_parameter", new JObject { ["name"] = parameter, ["value"] = EditedFlangeHeightMm + " mm" })), false, false);
                if (!committed.Ok) throw new InvalidOperationException(route + " failed: " + Describe(committed));
            }
            else
            {
                // The flange height is not exposed as a parameter of that value: edit the feature definition in one native transaction.
                route = "native FlangeFeature.Definition.SetDistanceHeightExtent";
                var transaction = _app.TransactionManager.StartTransaction((_Document)doc, "M5 probe flange height");
                try
                {
                    dynamic feature = ((dynamic)def).Features.FlangeFeatures.Item(1);
                    dynamic definition = feature.Definition;
                    object datum = HeightDatumTypeEnum.kHeightDatumOuter;
                    try { datum = definition.HeightDatumType; } catch { /* the sheet flange was created with the outer datum */ }
                    definition.SetDistanceHeightExtent(EditedFlangeHeightMm / 10.0, PartFeatureExtentDirectionEnum.kPositiveExtentDirection, datum);
                    feature.Definition = definition;
                    transaction.End();
                }
                catch { transaction.Abort(); throw; }
                doc.Update();
                Pump();
            }
            if (!def.HasFlatPattern) throw new InvalidOperationException("the flat pattern disappeared after the edit.");
            var after = FlatRead(doc, 0.1);
            if (!after.Ok) throw new InvalidOperationException("read after edit: " + Describe(after));
            var a = (JObject)firstRead["flat_pattern_identity"]!; var b = (JObject)after.Data["flat_pattern_identity"]!;
            double lengthBefore = (double)a["length_mm"]!, lengthAfter = (double)b["length_mm"]!, widthBefore = (double)a["width_mm"]!, widthAfter = (double)b["width_mm"]!;
            bool changed = (string?)a["content_hash"] != (string?)b["content_hash"] && (string?)a["hash"] != (string?)b["hash"];
            bool dimensionsMoved = Math.Abs(lengthAfter - lengthBefore) > 0.1 || Math.Abs(widthAfter - widthBefore) > 0.1;
            _report.Verdict("D1D_IDENTITY_CHANGES_AFTER_EDIT", changed && dimensionsMoved,
                "edit via " + route + "; content_hash " + (changed ? "changed" : "UNCHANGED") + "; length " + Timing.Fixed(lengthBefore) + " -> " + Timing.Fixed(lengthAfter) +
                " mm, width " + Timing.Fixed(widthBefore) + " -> " + Timing.Fixed(widthAfter) + " mm (flange " + FlangeHeightMm + " -> " + EditedFlangeHeightMm + " mm)",
                new JObject { ["before"] = a, ["after"] = b, ["edit_route"] = route });
        }
        catch (Exception ex) { _report.Fail("D1D_IDENTITY_CHANGES_AFTER_EDIT", ex.Message); }
    }

    /// <summary>The model parameter whose value is the flange height (found by its unique value), or null.</summary>
    private static string? FindFlangeHeightParameter(SheetMetalComponentDefinition def)
    {
        try
        {
            foreach (dynamic parameter in (System.Collections.IEnumerable)((dynamic)def).Parameters)
            {
                double valueMm = Convert.ToDouble(parameter.Value) * 10;
                if (Math.Abs(valueMm - FlangeHeightMm) < 1e-6) return (string)parameter.Name;
            }
        }
        catch { /* falls back to the native edit */ }
        return null;
    }

    private void RunMultiBodyStage()
    {
        try
        {
            var doc = NewPart(true);
            var def = Def(doc);
            string? buildError = null;
            try
            {
                var build = Batch(doc, new JArray(
                    Op("create_sketch", new JObject { ["plane"] = "XY", ["name"] = "M5_A" }),
                    Op("draw_rectangle", new JObject { ["sketch_name"] = "M5_A", ["x1"] = 0, ["y1"] = 0, ["x2"] = 50, ["y2"] = 30 }),
                    Op("close_sketch", new JObject { ["sketch_name"] = "M5_A" }),
                    Op("sheet_metal_face", new JObject { ["sketch_name"] = "M5_A" }),
                    Op("create_sketch", new JObject { ["plane"] = "XY", ["name"] = "M5_B" }),
                    Op("draw_rectangle", new JObject { ["sketch_name"] = "M5_B", ["x1"] = 200, ["y1"] = 0, ["x2"] = 250, ["y2"] = 30 }),
                    Op("close_sketch", new JObject { ["sketch_name"] = "M5_B" }),
                    Op("sheet_metal_face", new JObject { ["sketch_name"] = "M5_B" })), false, false);
                if (!build.Ok) buildError = Describe(build);
            }
            catch (Exception ex) { buildError = ex.Message; }
            int bodies = def.SurfaceBodies.Count;
            if (buildError != null || bodies < 2)
            {
                _report.NotRun("D1E_MULTI_BODY_REFUSED", "could not build a multi-body sheet-metal fixture (bodies=" + bodies + (buildError == null ? "" : "; " + buildError) + "); the refusal is untested, not passed.");
                return;
            }
            var result = FlatRead(doc, 0.1);
            bool ok = !result.Ok && result.Code == InventorErrorCodes.INVALID_ARGUMENT && (string?)result.Details?["reason"] == "MULTI_BODY_PART" && !def.HasFlatPattern;
            _report.Verdict("D1E_MULTI_BODY_REFUSED", ok, bodies + " bodies; result=" + Describe(result) + "; HasFlatPattern=" + def.HasFlatPattern);
        }
        catch (Exception ex) { _report.Fail("D1E_MULTI_BODY_REFUSED", ex.Message); }
    }

    private void RunPlainPartStage()
    {
        try
        {
            var doc = NewPart(false);
            var result = FlatRead(doc, 0.1);
            _report.Verdict("D1E_NON_SHEET_METAL_REFUSED", !result.Ok && result.Code == InventorErrorCodes.WRONG_DOCUMENT_TYPE, "result=" + Describe(result));
        }
        catch (Exception ex) { _report.Fail("D1E_NON_SHEET_METAL_REFUSED", ex.Message); }
    }
}
