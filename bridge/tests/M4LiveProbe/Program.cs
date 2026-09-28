using System.Runtime.InteropServices;
using Inventor;
using Newtonsoft.Json.Linq;
using Path = System.IO.Path;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;
using Bimwright.Ipt.Shared.Plugin;

internal static class Program
{
    [DllImport("oleaut32.dll", PreserveSig = false)]
    private static extern void GetActiveObject(ref Guid clsid, IntPtr reserved,
        [MarshalAs(UnmanagedType.IUnknown)] out object app);

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Contains("--http")) return HttpProbe.Run();
        global::Inventor.Application? app = null;
        Document? original = null;
        PartDocument? part = null;
        AssemblyDocument? assembly = null;
        Transaction? owned = null;
        IDisposable? tracker = null;
        try
        {
            var clsid = Type.GetTypeFromProgID("Inventor.Application")!.GUID;
            GetActiveObject(ref clsid, IntPtr.Zero, out var active);
            app = (global::Inventor.Application)active;
            if (args.Contains("--quest-planar") || args.Contains("--quest-planar-commit"))
                return QuestPlanarProbe.Run(app, args.Contains("--quest-planar-commit"));
            if (args.Contains("--nested")) return NestedProbe.Run(app);
            if (args.Contains("--prepare-quest")) return QuestFixture.Prepare(app);
            if (args.Contains("--inspect-quest")) return QuestFixture.Inspect(app);
            if (args.Contains("--save-quest")) return QuestFixture.Save(app);
            if (args.Contains("--open-quest")) return QuestFixture.Open(app);
            if (args.Contains("--quit-saved-quest")) return QuestFixture.QuitSaved(app);
            if (args.Contains("--inventory-quest")) return QuestFixture.Inventory(app);
            if (args.Contains("--close-batch-part")) return QuestFixture.CloseBatchPart(app);
            if (args.Contains("--show-quest")) return QuestFixture.Show(app);
            if (args.Contains("--restore-quest")) return QuestFixture.Restore(app);
            original = app.ActiveDocument;
            var directory = Path.Combine(Path.GetTempPath(), "xrso-m4-probe-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            part = (PartDocument)app.Documents.Add(DocumentTypeEnum.kPartDocumentObject);
            var sketch = part.ComponentDefinition.Sketches.Add(part.ComponentDefinition.WorkPlanes[3]);
            sketch.SketchCircles.AddByCenterRadius(app.TransientGeometry.CreatePoint2d(0, 0), 1);
            var extrude = part.ComponentDefinition.Features.ExtrudeFeatures.CreateExtrudeDefinition(
                sketch.Profiles.AddForSolid(), PartFeatureOperationEnum.kJoinOperation);
            extrude.SetDistanceExtent(2, PartFeatureExtentDirectionEnum.kPositiveExtentDirection);
            part.ComponentDefinition.Features.ExtrudeFeatures.Add(extrude);
            var path = Path.Combine(directory, "probe.ipt");
            part.SaveAs(path, false);
            assembly = (AssemblyDocument)app.Documents.Add(DocumentTypeEnum.kAssemblyDocumentObject);
            var def = assembly.ComponentDefinition;
            var fixedPart = def.Occurrences.Add(path, app.TransientGeometry.CreateMatrix());
            fixedPart.Grounded = true;
            var matrix = app.TransientGeometry.CreateMatrix();
            matrix.SetTranslation(app.TransientGeometry.CreateVector(5, 0, 0));
            var moving = def.Occurrences.Add(path, matrix);
            moving.Grounded = false;
            Dump("free", moving, 3, 3);
            owned = app.TransactionManager.StartTransaction((_Document)assembly, "M4 native DOF characterization");
            fixedPart.CreateGeometryProxy(part.ComponentDefinition.WorkPlanes[3], out var planeA);
            moving.CreateGeometryProxy(part.ComponentDefinition.WorkPlanes[3], out var planeB);
            def.Constraints.AddFlushConstraint(planeA, planeB, 0);
            fixedPart.CreateGeometryProxy(part.ComponentDefinition.WorkPlanes[2], out planeA);
            moving.CreateGeometryProxy(part.ComponentDefinition.WorkPlanes[2], out planeB);
            def.Constraints.AddFlushConstraint(planeA, planeB, 0);
            if (!assembly.Update2()) throw new Exception("Slider rebuild failed.");
            Dump("slider", moving, 1, 0);
            var before = moving.Transformation.Copy();
            var allowed = before.Copy(); allowed.Cell[1, 4] += 1;
            moving.Transformation = allowed;
            if (!assembly.Update2() || Math.Abs(moving.Transformation.Cell[1, 4] - allowed.Cell[1, 4]) > 1e-5)
                throw new Exception("Solver refused available slider translation.");
            var blocked = moving.Transformation.Copy(); blocked.Cell[3, 4] += 1;
            moving.Transformation = blocked;
            assembly.Update2();
            if (Math.Abs(moving.Transformation.Cell[3, 4] - blocked.Cell[3, 4]) < 1e-5)
                throw new Exception("Solver allowed a forbidden translation.");
            owned.Abort(); owned = null;
            if (def.Constraints.Count != 0 || Math.Abs(moving.Transformation.Cell[1, 4] - 5) > 1e-5)
                throw new Exception("Rollback did not restore pose and constraints.");
            Dump("rollback", moving, 3, 3);
            moving.Grounded = true;
            Dump("grounded", moving, 0, 0);
            moving.Grounded = false;
            var trackerType = typeof(InventorCommandRegistry).Assembly.GetType("Bimwright.Ipt.Shared.Plugin.CadEventTracker", true)!;
            tracker = (IDisposable)Activator.CreateInstance(trackerType, app)!;
            var journal = (CadEventJournal)trackerType.GetProperty("Journal")!.GetValue(tracker)!;
            var commands = InventorCommandRegistry.Build(new PluginOptions(2027, false, false, 0));
            var context = new InventorCommandContext { Application = app, Events = journal, Commands = commands, AllowExperimental = true, InventorYear = 2027 };
            string document = "doc_" + assembly.InternalName;
            JObject Call(string command, JObject arguments)
            {
                var response = commands[command].Execute(context, arguments);
                if (!response.Ok) throw new Exception(command + ": " + response.Error?.Code + " " + response.Error?.Message);
                return (JObject)response.Data!;
            }
            JObject Request() => new() { ["document_id"] = document, ["expected_revision"] = journal.Revision(document) };
            var overview = Call("get_assembly_context_xr", Request());
            string occurrenceId = (string)((JArray)overview["occurrences"]!).Single(o => (string?)o["name"] == moving.Name)["occurrence_id"]!;
            var selected = Request(); selected["occurrence_id"] = occurrenceId;
            var details = Call("get_assembly_context_xr", selected);
            if (((JArray)details["references"]!).Count < 5) throw new Exception("Missing assembly proxy references.");
            Console.WriteLine("PASS: assembly context with " + ((JArray)details["references"]!).Count + " proxy references.");
            var operation = new JObject { ["command"] = "assembly_move", ["arguments"] = new JObject {
                ["occurrence_id"] = occurrenceId, ["translation_mm"] = new JArray(10, 0, 0) } };
            var directCall = commands["assembly_move"].Execute(context, (JObject)operation["arguments"]!);
            if (directCall.Ok) throw new Exception("Assembly mutation escaped the atomic transaction guard.");
            var request = Request(); request["operations"] = new JArray(operation);
            request["preview"] = true; request["include_preview_mesh"] = true;
            request["validate"] = new JArray("rebuild", "constraint_health", "interference");
            string revision = journal.Revision(document);
            var preview = Call("atomic_batch", request);
            var meshBody = ((JArray)preview["preview_mesh"]!["bodies"]!).Single(b => ((string)b["name"]!).StartsWith(moving.Name + "/"));
            var decoded = MeshPayload.FromJson((JObject)meshBody);
            var x = decoded.Positions.Where((_, index) => index % 3 == 0).ToArray();
            if (Math.Abs((x.Min() + x.Max()) / 2 - 6) > 0.01) throw new Exception("Preview is not in the tentative assembly coordinate frame.");
            if (Math.Abs(moving.Transformation.Cell[1, 4] - 5) > 1e-5 || journal.Revision(document) != revision)
                throw new Exception("Atomic preview failed to restore pose/revision.");
            if (decoded.Faces.Any(f => f.FaceId != null)) throw new Exception("Transient preview exposed CAD IDs.");
            Console.WriteLine("PASS: guarded atomic assembly preview captures tentative transformed mesh and restores revision.");
            string fixedId = (string)((JArray)overview["occurrences"]!).Single(o => (string?)o["name"] == fixedPart.Name)["occurrence_id"]!;
            var fixedRequest = Request(); fixedRequest["occurrence_id"] = fixedId;
            var fixedContext = Call("get_assembly_context_xr", fixedRequest);
            var refsA = ((JArray)fixedContext["references"]!).Cast<JObject>().ToArray();
            var refsB = ((JArray)details["references"]!).Cast<JObject>().ToArray();
            string Ref(JObject[] refs, string geometry) => (string)refs.First(r => (string?)r["geometry"] == geometry)["id"]!;
            foreach (string jointType in new[] { "rigid", "rotational", "slide", "cylindrical", "planar", "ball" })
            {
                var jointRequest = Request();
                jointRequest["preview"] = true; jointRequest["include_preview_mesh"] = true;
                jointRequest["validate"] = new JArray("rebuild", "constraint_health", "interference");
                jointRequest["operations"] = new JArray(new JObject { ["command"] = "assembly_joint", ["arguments"] = new JObject {
                    ["joint_type"] = jointType, ["origin_a_id"] = Ref(refsA, "kCircleCurve"), ["origin_b_id"] = Ref(refsB, "kCircleCurve"),
                    ["gap_mm"] = jointType == "ball" ? 0 : 30, ["flip_origin"] = jointType == "ball" } });
                Call("atomic_batch", jointRequest);
                if (def.Joints.Count != 0 || journal.Revision(document) != revision) throw new Exception("Joint preview leaked changes: " + jointType);
                Console.WriteLine("PASS: " + jointType + " joint preview, mesh and rollback.");
                jointRequest["operations"]![0]!["arguments"]!["origin_b_id"] = Ref(refsA, "kCircleCurve");
                if (commands["atomic_batch"].Execute(context, jointRequest).Ok || def.Joints.Count != 0 || journal.Revision(document) != revision)
                    throw new Exception("Same-occurrence joint was not rejected cleanly: " + jointType);
            }
            foreach (string constraintType in new[] { "mate", "flush", "mate_axis", "insert", "angle", "tangent" })
            {
                string geometry = constraintType == "insert" ? "kCircleCurve" : constraintType == "mate_axis" || constraintType == "tangent" ? "kCylinderSurface" : "kPlaneSurface";
                var arguments = new JObject { ["type"] = constraintType, ["entity_a_id"] = Ref(refsA, geometry), ["entity_b_id"] = Ref(refsB, geometry) };
                if (constraintType == "angle") arguments["angle_degrees"] = 0;
                else arguments["offset_mm"] = constraintType == "insert" || constraintType == "mate" || constraintType == "flush" ? 30 : 0;
                var constraintRequest = Request(); constraintRequest["preview"] = true; constraintRequest["include_preview_mesh"] = true;
                constraintRequest["operations"] = new JArray(new JObject { ["command"] = "assembly_constraint", ["arguments"] = arguments });
                // Feasibility and clean rollback are tested here; the overlapping coaxial fixture
                // intentionally cannot satisfy clearance for every constraint type.
                Call("atomic_batch", constraintRequest);
                if (def.Constraints.Count != 0 || journal.Revision(document) != revision) throw new Exception("Constraint preview leaked changes: " + constraintType);
                Console.WriteLine("PASS: " + constraintType + " constraint feasibility, mesh and rollback.");
                arguments["entity_b_id"] = arguments["entity_a_id"]!.DeepClone();
                if (commands["atomic_batch"].Execute(context, constraintRequest).Ok || def.Constraints.Count != 0 || journal.Revision(document) != revision)
                    throw new Exception("Same-occurrence constraint was not rejected cleanly: " + constraintType);
            }
            Console.WriteLine("PASS: native free/slider/grounded DOF, permitted/forbidden motion and rollback.");
            var commitJoint = Request(); commitJoint["preview"] = false; commitJoint["history_owner"] = "m4-probe";
            commitJoint["validate"] = new JArray("rebuild", "constraint_health", "interference");
            commitJoint["operations"] = new JArray(new JObject { ["command"] = "assembly_joint", ["arguments"] = new JObject {
                ["joint_type"] = "rotational", ["origin_a_id"] = Ref(refsA, "kCircleCurve"), ["origin_b_id"] = Ref(refsB, "kCircleCurve"), ["gap_mm"] = 30 } });
            Call("atomic_batch", commitJoint);
            if (def.Joints.Count != 1) throw new Exception("Joint commit did not persist.");
            Dump("rotational_joint", moving, 0, 1);
            moving.GetDegreesOfFreedom(out _, out _, out _, out var rotationAxes, out var rotationCenter);
            var rotationAxis = (Vector)rotationAxes[1];
            var rotationProbe = Request();
            rotationProbe["preview"] = true; rotationProbe["include_preview_mesh"] = true;
            rotationProbe["validate"] = new JArray("rebuild", "constraint_health", "interference");
            var rotationArgs = new JObject { ["occurrence_id"] = occurrenceId, ["translation_mm"] = new JArray(0, 0, 0),
                ["rotation_axis"] = new JArray(rotationAxis.X, rotationAxis.Y, rotationAxis.Z),
                ["rotation_center_mm"] = new JArray(rotationCenter.X * 10, rotationCenter.Y * 10, rotationCenter.Z * 10), ["rotation_degrees"] = 30 };
            rotationProbe["operations"] = new JArray(new JObject { ["command"] = "assembly_move", ["arguments"] = rotationArgs });
            var jointRevision = journal.Revision(document);
            Call("atomic_batch", rotationProbe);
            if (journal.Revision(document) != jointRevision || def.Joints.Count != 1) throw new Exception("Rotational move preview did not roll back cleanly.");
            rotationArgs["translation_mm"] = new JArray(10, 0, 0);
            rotationArgs["rotation_degrees"] = 0;
            if (commands["atomic_batch"].Execute(context, rotationProbe).Ok || journal.Revision(document) != jointRevision || def.Joints.Count != 1)
                throw new Exception("Translation forbidden by rotational joint was not rejected cleanly.");
            Console.WriteLine("PASS: M4 solver accepts residual 30 degree rotation and rejects forbidden translation.");
            JObject History(string action, string? ticket = null)
            {
                var historyRequest = Request(); historyRequest["owner"] = "m4-probe";
                historyRequest["action"] = action; if (ticket != null) historyRequest["ticket"] = ticket;
                return Call("history_xr", historyRequest);
            }
            var historyState = History("status");
            if ((bool?)historyState["can_undo"] != true) throw new Exception("Assembly XR Undo unavailable after own commit.");
            historyState = History("undo", (string)historyState["ticket"]!);
            if (def.Joints.Count != 0 || (bool?)historyState["can_redo"] != true) throw new Exception("Assembly XR Undo did not restore the prior state.");
            History("redo", (string)historyState["ticket"]!);
            if (def.Joints.Count != 1) throw new Exception("Assembly XR Redo did not restore the joint.");
            Console.WriteLine("PASS: persistent rotational joint, residual rotational DOF and scoped XR Undo/Redo.");
            historyState = History("status");
            historyState = History("undo", (string)historyState["ticket"]!);
            int redoId = app.TransactionManager.UndoneTransactions[app.TransactionManager.UndoneTransactions.Count].Id;
            Call("activate_open_document_xr", new JObject { ["document_id"] = "doc_" + part.InternalName });
            if (app.ActiveDocument.InternalName != part.InternalName) throw new Exception("Explicit document activation failed.");
            Call("activate_open_document_xr", new JObject { ["document_id"] = document });
            if (app.TransactionManager.UndoneTransactions.Count == 0 || app.TransactionManager.UndoneTransactions[app.TransactionManager.UndoneTransactions.Count].Id != redoId)
                throw new Exception("Activation changed the native Redo stack.");
            app.TransactionManager.RedoTransaction();
            if (def.Joints.Count != 1) throw new Exception("Native Redo unavailable after document activation.");
            owned = app.TransactionManager.StartTransaction((_Document)assembly, "M4 parent guard");
            bool activationBlocked = false;
            try { Call("activate_open_document_xr", new JObject { ["document_id"] = "doc_" + part.InternalName }); }
            catch (Exception e) when (e.Message.Contains("TRANSACTION_BUSY")) { activationBlocked = true; }
            if (!activationBlocked || app.TransactionManager.CurrentTransaction.Id != owned.Id || app.ActiveDocument.InternalName != assembly.InternalName)
                throw new Exception("Activation did not preserve the existing user transaction.");
            owned.Abort(); owned = null;
            Console.WriteLine("PASS: document activation preserves user transactions and native Redo.");
            owned = app.TransactionManager.StartTransaction((_Document)assembly, "M4 suppressed context probe");
            moving.Suppress();
            System.Windows.Forms.Application.DoEvents();
            var suppressedContext = Call("get_assembly_context_xr", Request());
            var suppressedOccurrence = ((JArray)suppressedContext["occurrences"]!).Single(o => (string?)o["name"] == moving.Name);
            if ((bool?)suppressedOccurrence["editable"] != false || (bool?)suppressedOccurrence["dof_complete"] != false)
                throw new Exception("Suppressed occurrence must be explicitly unavailable.");
            owned.Abort(); owned = null;
            Console.WriteLine("PASS: suppressed occurrence context remains readable and unavailable for editing.");
            Console.WriteLine("Probe artifacts: " + directory);
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally
        {
            try { owned?.Abort(); }
            finally
            {
                tracker?.Dispose();
                try { assembly?.Close(true); }
                finally { try { part?.Close(true); } finally { original?.Activate(); } }
            }
        }
    }

    private static void Dump(string name, ComponentOccurrence occurrence, int expectedT, int expectedR)
    {
        occurrence.GetDegreesOfFreedom(out var t, out var tv, out var r, out var rv, out var center);
        JArray Vectors(ObjectsEnumerator values)
        {
            var result = new JArray();
            foreach (object item in values)
            {
                if (item is not Vector vector) throw new Exception("Unexpected DOF vector type.");
                result.Add(new JArray(vector.X, vector.Y, vector.Z));
            }
            return result;
        }
        Console.WriteLine(new JObject { ["case"] = name, ["translation"] = t, ["rotation"] = r,
            ["translation_axes"] = Vectors(tv), ["rotation_axes"] = Vectors(rv),
            ["center_cm"] = center == null ? null : new JArray(center.X, center.Y, center.Z) });
        if (t != expectedT || r != expectedR) throw new Exception("Unexpected DOF counts for " + name);
    }
}
