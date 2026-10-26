using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Bimwright.Ipt.Server;
using Bimwright.Ipt.Server.Assets;
using Bimwright.Ipt.Server.Planning;
using Bimwright.Ipt.Server.Tools;
using Inventor;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using File = System.IO.File;
using Path = System.IO.Path;

// Synthetic scan, real installed add-in and native CAD. Never uses an existing document.
internal static class Program
{
    [DllImport("oleaut32.dll", PreserveSig = false)]
    private static extern void GetActiveObject(ref Guid clsid, IntPtr reserved,
        [MarshalAs(UnmanagedType.IUnknown)] out object app);
    [STAThread]
    private static int Main(string[] args)
    {
        var clsid = Type.GetTypeFromProgID("Inventor.Application")!.GUID;
        GetActiveObject(ref clsid, IntPtr.Zero, out var obj);
        var app = (Application)obj;
        if (app.Documents.Count != 0) throw new InvalidOperationException("Run this isolated probe only with no open documents.");
        var config = new InventorMcpConfig { TargetId = "2027", EnableExperimental = true,
            AssetDirectory = Path.Combine(Path.GetTempPath(), "rc4-probe-assets-" + Guid.NewGuid().ToString("N")) };
        var client = new PluginClient(config);
        using var plans = new ChangePlanStore();
        var planning = new PlanningTools(client, plans, config, new StdioCallerIdentity(), new AssetStore(config));
        string? ownedId = null, path = null;
        int assertions = 0;
        JObject Call(string command, JObject? args = null) => (JObject)client.SendAsync(command, args ?? new JObject(), default).GetAwaiter().GetResult();
        void Check(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); assertions++; Console.WriteLine("PASS " + message); }
        try
        {
            string ownedName = "RC4_Simulator_" + Guid.NewGuid().ToString("N");
            Console.WriteLine("OWNED_CREATE name=" + ownedName + "; initial_open_documents=0");
            var created = Call("workspace_new_document", new JObject { ["kind"] = "part", ["name"] = ownedName });
            ownedId = created.Value<string>("document_id")!; path = created.Value<string>("path")!;
            string revision = created.Value<string>("revision")!;
            Check(created.Value<bool>("workspace_managed"), "owned empty destination");
            if (args.Contains("--diagnose"))
            {
                var native = (PartDocument)app.ActiveDocument; var def = native.ComponentDefinition; var tg = app.TransientGeometry;
                var transaction = app.TransactionManager.StartTransaction((_Document)native, "RC4 diagnostic rollback");
                void Step(string name, Action action) { Console.WriteLine("NATIVE STEP " + name); action(); }
                try
                {
                    Step("BOM", () => def.BOMStructure = BOMStructureEnum.kReferenceBOMStructure);
                    AttributeSet metadata = null!;
                    Step("metadata set", () => metadata = native.AttributeSets.Add("RC_Diagnostic", false));
                    Step("metadata bytes", () => metadata.Add("source_json", ValueTypeEnum.kByteArrayType, System.Text.Encoding.UTF8.GetBytes("{}")));
                    Step("metadata status", () => metadata.Add("status", ValueTypeEnum.kStringType, "estimated"));
                    Sketch3D cloud = null!;
                    Step("cloud", () => cloud = def.Sketches3D.Add());
                    Step("cloud name", () => cloud.Name = "RC_Diagnostic_PointCloud");
                    Step("defer", () => cloud.DeferUpdates = true);
                    Step("point", () => cloud.SketchPoints3D.Add(tg.CreatePoint(1, 2, -7), false));
                    Step("undefer", () => cloud.DeferUpdates = false);
                    WorkPoint center = null!, end = null!;
                    Step("work point", () => center = def.WorkPoints.AddFixed(tg.CreatePoint(1, 2, -7), false));
                    Step("work point metadata", () => center.AttributeSets.Add("RealityCapture", true).Add("primitive_json", ValueTypeEnum.kStringType, "{}"));
                    Step("construction point", () => end = def.WorkPoints.AddFixed(tg.CreatePoint(1, 2, 3), true));
                    Step("axis", () => def.WorkAxes.AddByTwoPoints(center, end, false));
                    Step("plane", () => def.WorkPlanes.AddFixed(tg.CreatePoint(1, 2, -7), tg.CreateUnitVector(1,0,0), tg.CreateUnitVector(0,1,0), false));
                    UserParameter parameter = null!;
                    Step("parameter", () => parameter = def.Parameters.UserParameters.AddByValue("RC_Diagnostic_Diameter", 1d, "mm"));
                    Step("parameter metadata", () => parameter.AttributeSets.Add("RealityCapture", true).Add("source_json", ValueTypeEnum.kStringType, "{}"));
                    Console.WriteLine("DIAGNOSTIC native primitives completed; this is not acceptance"); return 0;
                }
                finally { transaction.Abort(); }
            }
            var points = new JArray(new JObject { ["id"] = "p0", ["position_mm"] = new JArray(0, 0, 100) },
                new JObject { ["id"] = "p1", ["position_mm"] = new JArray(100, 0, 100) },
                new JObject { ["id"] = "p2", ["position_mm"] = new JArray(0, 100, 100) });
            string referenceName = "RC_" + Guid.NewGuid().ToString("N")[..12];
            var reference = new JObject
            {
                ["unit"] = "mm", ["geometry"] = "estimated", ["reference_id"] = referenceName,
                ["source"] = new JObject { ["document_id"] = ownedId, ["cad_revision"] = revision,
                    ["scan_id"] = Guid.NewGuid().ToString("N"), ["solve_revision"] = 1, ["review_version"] = 0,
                    ["points_sha256"] = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(points.ToString(Formatting.None)))).ToLowerInvariant(),
                    ["scan_metric_to_inventor_mm"] = new JArray(1,0,0,10, 0,1,0,20, 0,0,-1,30, 0,0,0,1) },
                ["points"] = points,
                ["primitives"] = new JArray(new[] { "point", "axis", "plane", "circle", "cylinder" }.Select(kind => new JObject
                    { ["kind"] = kind, ["origin_mm"] = new JArray(0,0,100), ["direction"] = new JArray(0,0,1),
                      ["radius_mm"] = 5, ["length_mm"] = 20, ["fit_rms_mm"] = .1 })),
                ["known_constraints"] = new JArray(new JObject { ["ValueMm"] = 100, ["UncertaintyMm"] = .1, ["Source"] = "synthetic external reference" })
            };
            var request = new JObject { ["document_id"] = ownedId, ["expected_revision"] = revision, ["preview"] = true,
                ["validate"] = new JArray("rebuild", "feature_health"),
                ["operations"] = new JArray(new JObject { ["command"] = "create_reality_reference", ["arguments"] = new JObject { ["reference"] = reference } }) };
            // Match MCP host binding: AtomicOperation.Arguments is a System.Text.Json element.
            var operations = System.Text.Json.JsonSerializer.Deserialize<AtomicOperation[]>(request["operations"]!.ToString())!;
            // Native document creation rewrites the discovery descriptor. Wait on
            // this read-only seam before preview; never retry an uncertain CAD write.
            var discoveryDeadline = DateTime.UtcNow.AddSeconds(10);
            while (client.ListTargets().Count != 1)
            {
                if (DateTime.UtcNow >= discoveryDeadline) throw new InvalidOperationException("Native target discovery did not settle after document creation.");
                Thread.Sleep(100);
            }
            Console.WriteLine("TARGET " + client.CurrentTarget?.TargetId);
            var plan = JObject.Parse(planning.PlanChange(ownedId, revision, operations, new[] { "rebuild", "feature_health" },
                "Synthetic RC4 acceptance").GetAwaiter().GetResult());
            var preview = (JObject)(plan["preview"] ?? throw new InvalidOperationException(plan.ToString()));
            var part = (PartDocument)app.ActiveDocument;
            Check(preview.Value<string>("status") == "preview_rolled_back", "native preview rolled back");
            Check(part.ComponentDefinition.Sketches3D.Count == 0 && !part.AttributeSets.NameIsUsed[referenceName], "preview leaves no reference geometry or provenance");
            Check(Call("get_visual_revision").Value<string>("revision") == revision, "preview preserves revision");
            var committed = JObject.Parse(planning.CommitPlan(plan.Value<string>("plan_id")!, ownedId, revision).GetAwaiter().GetResult());
            Check(committed.Value<string>("status") == "committed", "native atomic commit");
            void Inspect()
            {
                var def = part.ComponentDefinition;
                Check(def.SurfaceBodies.Count == 0 && def.Features.Count == 0, "reference creates no B-Rep");
                Check(def.BOMStructure == BOMStructureEnum.kReferenceBOMStructure, "reference BOM classification");
                Check(def.Sketches3D.Count == 1 && def.Sketches3D[1].SketchPoints3D.Count == 3, "three native cloud points");
                var p = def.Sketches3D[1].SketchPoints3D[1].Geometry;
                Check(Math.Abs(p.X - 1) < 1e-8 && Math.Abs(p.Y - 2) < 1e-8 && Math.Abs(p.Z + 7) < 1e-8, "LH mm to RH native cm transform");
                Check(def.WorkAxes.Count == 6 && def.WorkPlanes.Count == 5, "axis plane circle cylinder native work geometry");
                Check(def.Parameters.UserParameters.Count == 4, "estimated and known parameters separate");
                Check(part.AttributeSets.NameIsUsed[referenceName], "native provenance retained");
                var stored = (byte[])part.AttributeSets[referenceName]["source_json"].Value;
                Check(JToken.DeepEquals(reference, JObject.Parse(System.Text.Encoding.UTF8.GetString(stored))), "complete source provenance round trips");
                Check(Math.Abs(Convert.ToDouble(def.Parameters.UserParameters[referenceName + "_Known1"].Value) - 10) < 1e-8
                    && def.Parameters.UserParameters[referenceName + "_Known1"].Comment.Contains("known external"), "known 100 mm dimension retains its distinct status");
                Check(Math.Abs(Convert.ToDouble(def.Parameters.UserParameters[referenceName + "_P5_EstimatedDiameter"].Value) - 1) < 1e-8
                    && def.Parameters.UserParameters[referenceName + "_P5_EstimatedDiameter"].Comment == "estimated", "estimated 10 mm diameter retains its status");
            }
            Inspect();
            app.CommandManager.ControlDefinitions["AppUndoCmd"].Execute2(true);
            Check(part.ComponentDefinition.Sketches3D.Count == 0 && !part.AttributeSets.NameIsUsed[referenceName], "Undo removes complete reference transaction");
            app.CommandManager.ControlDefinitions["AppRedoCmd"].Execute2(true); Inspect();
            revision = Call("get_visual_revision").Value<string>("revision")!;
            var saved = Call("workspace_save_document", new JObject { ["document_id"] = ownedId, ["expected_revision"] = revision });
            Check(saved.Value<string>("status") == "saved_in_place" && File.Exists(path), "managed native save");
            Call("workspace_close_document", new JObject { ["document_id"] = ownedId }); ownedId = null;
            var reopened = Call("workspace_open_document", new JObject { ["file"] = Path.GetFileName(path) });
            ownedId = reopened.Value<string>("document_id")!; part = (PartDocument)app.ActiveDocument; Inspect();
            Console.WriteLine(JsonConvert.SerializeObject(new { status = "PASS", input = "synthetic scan", cad = "native Inventor 2027", assertions, preview, committed, saved }));
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally
        {
            if (ownedId != null) Call("workspace_close_document", new JObject { ["document_id"] = ownedId, ["discard_changes"] = true });
            // Retain the uniquely named owned file as native evidence; never delete user files.
            Console.WriteLine("CLEANUP open_documents=" + app.Documents.Count + " evidence=" + path);
        }
    }
}
