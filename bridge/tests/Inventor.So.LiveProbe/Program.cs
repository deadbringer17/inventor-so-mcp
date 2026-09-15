using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Handlers.Core;
using Bimwright.Ipt.Shared.Infrastructure;
using Newtonsoft.Json.Linq;

internal static class Program
{
    [DllImport("ole32.dll", CharSet = CharSet.Unicode)]
    private static extern int CLSIDFromProgID(string progId, out Guid clsid);
    [DllImport("oleaut32.dll", PreserveSig = false)]
    private static extern void GetActiveObject(ref Guid clsid, IntPtr reserved, [MarshalAs(UnmanagedType.IUnknown)] out object app);

    [STAThread]
    private static int Main(string[] args)
    {
        // Load installed Autodesk interop; do not redistribute it in test output.
        AssemblyLoadContext.Default.Resolving += (_, name) => name.Name == "Autodesk.Inventor.Interop"
            ? AssemblyLoadContext.Default.LoadFromAssemblyPath(@"C:\Program Files\Common Files\Autodesk Shared\Extensions 2027\Framework\Interop\Autodesk.Inventor.Interop.dll") : null;
        if (args.Contains("--assembly")) return AssemblyProbe();
        return args.Contains("--fixture") || args.Contains("--batch") ? Fixture(args.Contains("--batch")) : Probe();
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static int Fixture(bool batches)
    {
        Marshal.ThrowExceptionForHR(CLSIDFromProgID("Inventor.Application", out var clsid));
        GetActiveObject(ref clsid, IntPtr.Zero, out var com);
        var app = (Inventor.Application)com;
        var active = app.ActiveDocument;
        using var tracker = new Bimwright.Ipt.Shared.Plugin.CadEventTracker(app);
        int count = app.Documents.Count;
        var directory = Path.Combine(Environment.CurrentDirectory, "artifacts", "live-fixtures", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "reference-roundtrip.ipt");
        Inventor.PartDocument? part = null;
        try
        {
            var template = app.FileManager.GetTemplateFile(Inventor.DocumentTypeEnum.kPartDocumentObject);
            // Batch handlers target ActiveDocument, so the isolated batch fixture needs a view.
            part = (Inventor.PartDocument)app.Documents.Add(Inventor.DocumentTypeEnum.kPartDocumentObject, template, batches);
            var comp = part.ComponentDefinition;
            var sketch = comp.Sketches.Add(comp.WorkPlanes[3]);
            sketch.SketchCircles.AddByCenterRadius(app.TransientGeometry.CreatePoint2d(0, 0), 1.0);
            var profile = sketch.Profiles.AddForSolid();
            var definition = comp.Features.ExtrudeFeatures.CreateExtrudeDefinition(profile, Inventor.PartFeatureOperationEnum.kJoinOperation);
            definition.SetDistanceExtent(2.0, Inventor.PartFeatureExtentDirectionEnum.kPositiveExtentDirection);
            var feature = comp.Features.ExtrudeFeatures.Add(definition);
            if (batches)
            {
                part.Activate();
                TestBatches(app, part, tracker.Journal, ((Inventor.DistanceExtent)feature.Definition.Extent).Distance.Name);
                TestEdgeFeatures(app, part, tracker.Journal);
                TestFaceSketch(app, part, tracker.Journal);
                TestFourHoles(app, part, tracker.Journal);
                TestArtifacts(app, part, tracker.Journal);
                feature = (Inventor.ExtrudeFeature)comp.Features[1];
            }
            part.SaveAs(path, false);
            var edgeId = (string)EntityReferences.Describe((Inventor.Document)part, comp.SurfaceBodies[1].Edges[1])["id"]!;
            EntityReferences.ResolvePartEdge((Inventor.Document)part, edgeId);
            var reference = EntityReferences.Describe((Inventor.Document)part, comp.SurfaceBodies[1].Faces[1]);
            var id = (string)reference["id"]!;
            string revisionBefore = tracker.Journal.Revision(EntityReferences.DocumentId((Inventor.Document)part));
            AssertResolved(app, id, "initial");
            var extent = (Inventor.DistanceExtent)feature.Definition.Extent;
            extent.Distance.Expression = "30 mm";
            if (!part.Update2()) throw new InvalidOperationException("Fixture rebuild failed");
            AssertResolved(app, id, "after rebuild");
            EntityReferences.ResolvePartEdge((Inventor.Document)part, edgeId);
            if (tracker.Journal.Revision(EntityReferences.DocumentId((Inventor.Document)part)) == revisionBefore)
                throw new InvalidOperationException("No document change event observed for fixture edit");
            part.Save();
            part.Close(true);
            part = null;
            part = (Inventor.PartDocument)app.Documents.Open(path, false);
            AssertResolved(app, id, "after reopen");
            EntityReferences.ResolvePartEdge((Inventor.Document)part, edgeId);
            try
            {
                EntityReferences.ResolvePartEdge((Inventor.Document)part, id);
                throw new InvalidOperationException("Face reference accepted as an edge");
            }
            catch (ArgumentException ex) when (ex.Message.StartsWith("REFERENCE_TYPE_MISMATCH:")) { }
            if (!((JArray)tracker.Journal.Read()["events"]!).Any(e => (string?)e["type"] == "document_closed" && (string?)e["document_id"] == (string?)reference["document_id"]))
                throw new InvalidOperationException("Closed document event lost its document identity");
            Console.WriteLine(new JObject { ["probe"] = "face reference lifecycle", ["initial"] = "resolved", ["after_rebuild"] = "resolved",
                ["after_reopen"] = "resolved", ["fixture_path"] = path, ["document_events"] = tracker.Journal.Read() });
        }
        finally
        {
            part?.Close(true);
            if (active != null && app.ActiveDocument?.InternalName != active.InternalName) active.Activate();
        }
        if (app.Documents.Count != count) throw new InvalidOperationException("Document count was not restored");
        return 0;
    }

    private static void AssertResolved(Inventor.Application app, string id, string phase)
    {
        var result = EntityReferences.Resolve(app, id);
        if ((string?)result["status"] != "resolved") throw new InvalidOperationException(phase + ": " + result);
    }

    /// <summary>
    /// A refusal is identified by its code and, for a rollback, by details.reason - never by the
    /// wording of the message, which is guidance for a human and free to change.
    /// </summary>
    private static void Expect(InventorCommandResult result, string code, string what)
    {
        if (result.Ok) throw new Exception(what + " unexpectedly succeeded");
        if (result.Error?.Code != code)
            throw new Exception(what + " reported " + result.Error?.Code + " instead of " + code);
    }

    /// <summary>The clearance/interference refusals the safe assembly writes roll back on.</summary>
    private static bool Refused(CodedFailureException failure)
    {
        string? reason = (string?)failure.Details?["reason"];
        return reason == "CLEARANCE_FAILED" || reason == "INTERFERENCE";
    }

    private static int AssemblyProbe()
    {
        Marshal.ThrowExceptionForHR(CLSIDFromProgID("Inventor.Application", out var clsid));
        GetActiveObject(ref clsid, IntPtr.Zero, out var com);
        var app = (Inventor.Application)com;
        var previous = app.ActiveDocument;
        var initial = app.Documents.Cast<Inventor.Document>().Select(d => d.InternalName).ToHashSet();
        string fixture = Path.GetFullPath("artifacts/live-fixtures/f8000325f30b4823bb3ec05b97f8ab97/reference-roundtrip.ipt");
        Inventor.AssemblyDocument? assembly = null;
        using var tracker = new Bimwright.Ipt.Shared.Plugin.CadEventTracker(app);
        try
        {
            assembly = (Inventor.AssemblyDocument)app.Documents.Add(Inventor.DocumentTypeEnum.kAssemblyDocumentObject,
                app.FileManager.GetTemplateFile(Inventor.DocumentTypeEnum.kAssemblyDocumentObject), true);
            var a = assembly.ComponentDefinition.Occurrences.Add(fixture, app.TransientGeometry.CreateMatrix());
            var transform = app.TransientGeometry.CreateMatrix();
            transform.SetTranslation(app.TransientGeometry.CreateVector(5, 0, 0));
            var b = assembly.ComponentDefinition.Occurrences.Add(fixture, transform);
            a.Grounded = true; b.Grounded = false;
            var doc = (Inventor.Document)assembly;
            string id = EntityReferences.DocumentId(doc);
            string componentId = (string)EntityReferences.Describe(doc, b)["id"]!;
            var ctx = new Bimwright.Ipt.Shared.Infrastructure.InventorCommandContext { Application = app, Events = tracker.Journal };
            JObject Request(double dx, bool preview) => new()
            {
                ["document_id"] = id, ["expected_revision"] = tracker.Journal.Revision(id), ["component_id"] = componentId,
                ["translation_mm"] = new JArray(dx, 0, 0), ["minimum_clearance_mm"] = 5, ["preview"] = preview
            };
            var handler = new MoveComponentHandler();
            var preview = handler.Execute(ctx, Request(-20, true));
            if ((string?)preview.Data?["status"] != "preview_rolled_back" || Math.Abs(b.Transformation.Translation.X - 5) > 1e-7)
                throw new Exception("Assembly preview did not restore transform");
            foreach (double dx in new[] { -26.0, -40.0 })
            {
                try { handler.Execute(ctx, Request(dx, false)); throw new Exception("Unsafe move accepted"); }
                catch (CodedFailureException ex) when (ex.Code == InventorErrorCodes.ROLLED_BACK && Refused(ex)) { }
                if (Math.Abs(b.Transformation.Translation.X - 5) > 1e-7) throw new Exception("Unsafe move changed transform");
            }
            var committed = handler.Execute(ctx, Request(-20, false));
            if ((string?)committed.Data?["status"] != "committed" || Math.Abs(b.Transformation.Translation.X - 3) > 1e-7)
                throw new Exception("Assembly commit failed");
            Console.WriteLine("Assembly persistent translation: preview restored, insufficient clearance/interference rolled back, 20 mm move committed with 10 mm clearance");
            JObject Rotation(double angle, bool previewMode)
            {
                var request = Request(0, previewMode);
                request["rotation_axis"] = new JArray(0,1,0);
                request["rotation_center_mm"] = new JArray(30,0,0);
                request["rotation_degrees"] = angle;
                return request;
            }
            handler.Execute(ctx, Rotation(90,true));
            if (Math.Abs(b.Transformation.Cell[1,1]-1)>1e-7) throw new Exception("Rotation preview failed to restore orientation");
            try { handler.Execute(ctx, Rotation(-90,false)); throw new Exception("Unsafe rotation accepted"); }
            catch (CodedFailureException ex) when (ex.Code == InventorErrorCodes.ROLLED_BACK && Refused(ex)) { }
            if (Math.Abs(b.Transformation.Cell[1,1]-1)>1e-7) throw new Exception("Unsafe rotation changed orientation");
            var rotated = handler.Execute(ctx, Rotation(90,false));
            if ((string?)rotated.Data?["status"]!="committed" || Math.Abs(b.Transformation.Cell[1,3]-1)>1e-7 ||
                Math.Abs(b.Transformation.Translation.X-3)>1e-7) throw new Exception("Rotation commit failed");
            Console.WriteLine("Assembly rotation: 90-degree preview restored, unsafe -90 rolled back, safe +90 committed");
            var reset = app.TransientGeometry.CreateMatrix(); reset.SetTranslation(app.TransientGeometry.CreateVector(5,0,0));
            b.Transformation = reset;
            Inventor.FlushConstraint? offsetConstraint = null;
            for (int plane=1;plane<=3;plane++)
            {
                a.CreateGeometryProxy(((Inventor.PartComponentDefinition)a.Definition).WorkPlanes[plane], out var pa);
                b.CreateGeometryProxy(((Inventor.PartComponentDefinition)b.Definition).WorkPlanes[plane], out var pb);
                var c = assembly.ComponentDefinition.Constraints.AddFlushConstraint(pa,pb,plane==1 ? 5.0 : 0.0);
                if (plane==1) offsetConstraint=c;
            }
            if (!assembly.Update2()) throw new Exception("Fixture constraints failed to rebuild");
            double startX = b.Transformation.Translation.X;
            string constraintId = (string)EntityReferences.Describe(doc,offsetConstraint!)["id"]!;
            JObject Edit(double value, bool previewMode) => new()
            {
                ["document_id"] = id, ["expected_revision"] = tracker.Journal.Revision(id), ["constraint_id"] = constraintId,
                ["value"] = value, ["units"] = "mm", ["minimum_clearance_mm"] = 5, ["preview"] = previewMode
            };
            var editHandler = new EditConstraintHandler();
            var editPreview = editHandler.Execute(ctx,Edit(30,true));
            if ((string?)editPreview.Data?["status"]!="preview_rolled_back" || Math.Abs(b.Transformation.Translation.X-startX)>1e-7)
                throw new Exception("Constraint preview did not restore position");
            try { editHandler.Execute(ctx,Edit(24,false)); throw new Exception("Unsafe constraint edit accepted"); }
            catch (CodedFailureException ex) when (ex.Code == InventorErrorCodes.ROLLED_BACK &&
                (string?)ex.Details?["reason"] == "CLEARANCE_FAILED") { }
            if (Math.Abs(b.Transformation.Translation.X-startX)>1e-7) throw new Exception("Constraint rollback failed");
            var editCommit = editHandler.Execute(ctx,Edit(30,false));
            if ((string?)editCommit.Data?["status"]!="committed" || Math.Abs(Math.Abs(b.Transformation.Translation.X)-3)>1e-7)
                throw new Exception("Constraint-driven move failed");
            Console.WriteLine("Persistent constraint edit: preview restored, insufficient clearance rolled back, 30 mm offset committed");
        }
        finally
        {
            assembly?.Close(true);
            foreach (var document in app.Documents.Cast<Inventor.Document>().ToArray())
                if (!initial.Contains(document.InternalName) && string.Equals(document.FullFileName, fixture, StringComparison.OrdinalIgnoreCase))
                    document.Close(true);
            previous?.Activate();
        }
        if (!app.Documents.Cast<Inventor.Document>().Select(d => d.InternalName).ToHashSet().SetEquals(initial))
            throw new Exception("Assembly fixture did not restore original document set");
        return 0;
    }

    private static void TestArtifacts(Inventor.Application app, Inventor.PartDocument part,
        Bimwright.Ipt.Shared.Contracts.CadEventJournal journal)
    {
        var ctx = new Bimwright.Ipt.Shared.Infrastructure.InventorCommandContext { Application = app, Events = journal };
        string id = EntityReferences.DocumentId((Inventor.Document)part);
        foreach (string format in new[] { "native", "step" })
        {
            var response = new SaveArtifactHandler().Execute(ctx, new JObject
            { ["document_id"] = id, ["expected_revision"] = journal.Revision(id), ["format"] = format });
            if (!response.Ok || !File.Exists((string?)response.Data?["path"])) throw new Exception("Artifact failed: " + response.Error?.Message);
            Console.WriteLine("Safe artifact " + format + ": " + response.Data);
        }
    }

    private static void TestFourHoles(Inventor.Application app, Inventor.PartDocument part,
        Bimwright.Ipt.Shared.Contracts.CadEventJournal journal)
    {
        var doc = (Inventor.Document)part;
        var def = part.ComponentDefinition;
        var top = def.SurfaceBodies[1].Faces.Cast<Inventor.Face>()
            .Where(f => f.SurfaceType == Inventor.SurfaceTypeEnum.kPlaneSurface)
            .OrderByDescending(f => f.PointOnFace.Z).First();
        string faceId = (string)EntityReferences.Describe(doc, top)["id"]!;
        double z = top.PointOnFace.Z * 10;
        var points = new JArray(new JArray(-4, -4, z), new JArray(4, -4, z), new JArray(4, 4, z), new JArray(-4, 4, z));
        int sketches = def.Sketches.Count, features = def.Features.Count;
        double volume = def.MassProperties.Volume;
        var ctx = new Bimwright.Ipt.Shared.Infrastructure.InventorCommandContext
        {
            Application = app, Events = journal,
            Commands = new Dictionary<string, Bimwright.Ipt.Shared.Infrastructure.IInventorCommand>
            { ["hole"] = new Bimwright.Ipt.Shared.Handlers.Feature.HoleHandler(),
              ["set_parameter"] = new Bimwright.Ipt.Shared.Handlers.Parameters.SetParameterHandler() }
        };
        Bimwright.Ipt.Shared.Contracts.InventorCommandResult Run(bool preview, bool invalid) => new AtomicBatchHandler().Execute(ctx, new JObject
        {
            ["document_id"] = EntityReferences.DocumentId(doc),
            ["expected_revision"] = journal.Revision(EntityReferences.DocumentId(doc)), ["preview"] = preview,
            ["operations"] = new JArray(new JObject { ["command"] = "hole", ["arguments"] = new JObject
            {
                ["face_id"] = faceId, ["kind"] = "drilled", ["diameter_mm"] = 2.0, ["through"] = true,
                ["parametric_positioning"] = true,
                ["points_mm"] = invalid ? new JArray(points[0]!.DeepClone(), new JArray(1000, 0, z)) : points.DeepClone()
            } })
        });
        void AssertRestored()
        {
            if (def.Sketches.Count != sketches || def.Features.Count != features || Math.Abs(def.MassProperties.Volume - volume) > 1e-8)
                throw new Exception("Hole rollback left geometry or sketches behind");
        }
        var preview = Run(true, false);
        if ((string?)preview.Data?["status"] != "preview_rolled_back") throw new Exception("Hole preview failed");
        AssertRestored();
        var offFace = Run(false, true);
        Expect(offFace, InventorErrorCodes.ROLLED_BACK, "Off-face point");
        if (!(offFace.Error?.Message ?? "").Contains("does not land"))
            throw new Exception("Off-face point rolled back for the wrong reason: " + offFace.Error?.Message);
        AssertRestored();
        var committed = Run(false, false);
        double expectedRemoval = 4 * Math.PI * 0.1 * 0.1 * (z / 10);
        if ((string?)committed.Data?["status"] != "committed" || def.Features.Count != features + 1 ||
            // MassProperties is numerically integrated: allow 0.01 mm^3 absolute error.
            Math.Abs((volume - def.MassProperties.Volume) - expectedRemoval) > 1e-5)
            throw new Exception($"Four through holes mismatch: status={committed.Data}, features={def.Features.Count}/{features + 1}, removal={volume - def.MassProperties.Volume}, expected={expectedRemoval}, z={z}");
        Console.WriteLine("four persistent-face holes: preview restored, off-face rollback restored, commit volume verified");
        var holeData = committed.Data!["steps"]![0]!["data"]!;
        var positions = (JArray)holeData["position_parameters"]!;
        if (positions.Count != 4) throw new Exception("Missing hole position parameters");
        string parameter = (string)positions[0]!["x_parameter"]!;
        var placementSketch = def.Sketches[(string)holeData["sketch_name"]!];
        var dim = placementSketch.DimensionConstraints.Cast<Inventor.DimensionConstraint>()
            .OfType<Inventor.TwoPointDistanceDimConstraint>().First(d => d.Parameter.Name == parameter);
        var pointBefore = dim.PointTwo.Geometry.X;
        double originalValue = Convert.ToDouble(def.Parameters[parameter].Value);
        JObject MoveRequest(bool preview) => new JObject
        {
            ["document_id"] = EntityReferences.DocumentId(doc), ["expected_revision"] = journal.Revision(EntityReferences.DocumentId(doc)),
            ["preview"] = preview, ["operations"] = new JArray(new JObject
            { ["command"] = "set_parameter", ["arguments"] = new JObject { ["name"] = parameter, ["value"] = "5 mm" } })
        };
        new AtomicBatchHandler().Execute(ctx, MoveRequest(true));
        if (Math.Abs(Convert.ToDouble(def.Parameters[parameter].Value) - originalValue) > 1e-8)
            throw new Exception("Position edit preview failed to restore dimension");
        new AtomicBatchHandler().Execute(ctx, MoveRequest(false));
        placementSketch = def.Sketches[(string)holeData["sketch_name"]!];
        var movedDim = placementSketch.DimensionConstraints.Cast<Inventor.DimensionConstraint>()
            .OfType<Inventor.TwoPointDistanceDimConstraint>().First(d => d.Parameter.Name == parameter);
        if (Math.Abs(Math.Abs(movedDim.PointTwo.Geometry.X - pointBefore) - 0.1) > 1e-7)
            throw new Exception("Dimension edit did not move hole center by 1 mm");
        Console.WriteLine("parametric hole center: driving dimension preview restored and 1 mm move committed");
    }

    private static void TestEdgeFeatures(Inventor.Application app, Inventor.PartDocument part,
        Bimwright.Ipt.Shared.Contracts.CadEventJournal journal)
    {
        var ctx = new Bimwright.Ipt.Shared.Infrastructure.InventorCommandContext
        {
            Application = app, Events = journal,
            Commands = new Dictionary<string, Bimwright.Ipt.Shared.Infrastructure.IInventorCommand>
            {
                ["fillet"] = new Bimwright.Ipt.Shared.Handlers.Feature.FilletHandler(),
                ["chamfer"] = new Bimwright.Ipt.Shared.Handlers.Feature.ChamferHandler()
            }
        };
        var doc = (Inventor.Document)part;
        string docId = EntityReferences.DocumentId(doc);
        int baseline = part.ComponentDefinition.Features.Count;
        double volume = part.ComponentDefinition.MassProperties.Volume;
        foreach (string command in new[] { "fillet", "chamfer" })
        {
            var edge = part.ComponentDefinition.SurfaceBodies[1].Edges.Cast<Inventor.Edge>()
                .First(e => e.GeometryType == Inventor.CurveTypeEnum.kCircleCurve);
            string edgeId = (string)EntityReferences.Describe(doc, edge)["id"]!;
            var arguments = new JObject { ["edge_ids"] = new JArray(edgeId),
                [command == "fillet" ? "radius_mm" : "distance_mm"] = 1.0 };
            var response = new AtomicBatchHandler().Execute(ctx, new JObject
            {
                ["document_id"] = docId, ["expected_revision"] = journal.Revision(docId), ["preview"] = true,
                ["operations"] = new JArray(new JObject { ["command"] = command, ["arguments"] = arguments })
            });
            if ((string?)response.Data?["status"] != "preview_rolled_back") throw new Exception(command + " preview failed: " + response.Data);
            if (part.ComponentDefinition.Features.Count != baseline || Math.Abs(part.ComponentDefinition.MassProperties.Volume - volume) > 1e-8)
                throw new Exception(command + " preview did not restore geometry");
            Console.WriteLine(command + " persistent edge: created, health validated, preview geometry restored");
        }
    }

    private static void TestFaceSketch(Inventor.Application app, Inventor.PartDocument part,
        Bimwright.Ipt.Shared.Contracts.CadEventJournal journal)
    {
        var doc = (Inventor.Document)part;
        var faces = part.ComponentDefinition.SurfaceBodies[1].Faces.Cast<Inventor.Face>().ToArray();
        var planar = faces.First(f => f.SurfaceType == Inventor.SurfaceTypeEnum.kPlaneSurface);
        var curved = faces.First(f => f.SurfaceType != Inventor.SurfaceTypeEnum.kPlaneSurface);
        string id = (string)EntityReferences.Describe(doc, planar)["id"]!;
        string curvedId = (string)EntityReferences.Describe(doc, curved)["id"]!;
        var ctx = new Bimwright.Ipt.Shared.Infrastructure.InventorCommandContext
        {
            Application = app, Events = journal,
            Commands = new Dictionary<string, Bimwright.Ipt.Shared.Infrastructure.IInventorCommand>
            { ["create_sketch"] = new Bimwright.Ipt.Shared.Handlers.Sketch.CreateSketchHandler() }
        };
        string docId = EntityReferences.DocumentId(doc);
        int baseline = part.ComponentDefinition.Sketches.Count;
        Bimwright.Ipt.Shared.Contracts.InventorCommandResult Run(string faceId, bool preview) => new AtomicBatchHandler().Execute(ctx, new JObject
        {
            ["document_id"] = docId, ["expected_revision"] = journal.Revision(docId), ["preview"] = preview,
            ["operations"] = new JArray(new JObject { ["command"] = "create_sketch", ["arguments"] = new JObject { ["plane"] = faceId } })
        });
        Run(id, true);
        if (part.ComponentDefinition.Sketches.Count != baseline) throw new Exception("Sketch preview did not restore count");
        try { Run(curvedId, false); throw new Exception("Curved support accepted"); }
        catch (InvalidOperationException ex) when (ex.Message.Contains("REFERENCE_NOT_PLANAR")) { }
        if (part.ComponentDefinition.Sketches.Count != baseline) throw new Exception("Rejected face changed sketch count");
        var result = Run(id, false);
        if ((string?)result.Data?["status"] != "committed" || part.ComponentDefinition.Sketches.Count != baseline + 1)
            throw new Exception("Persistent-face sketch commit failed");
        Console.WriteLine("persistent planar face sketch: preview restored, curved face rejected, commit succeeded");
    }

    private static void TestBatches(Inventor.Application app, Inventor.PartDocument part,
        Bimwright.Ipt.Shared.Contracts.CadEventJournal journal, string parameterName)
    {
        var ctx = new Bimwright.Ipt.Shared.Infrastructure.InventorCommandContext
        {
            Application = app, Events = journal,
            Commands = new Dictionary<string, Bimwright.Ipt.Shared.Infrastructure.IInventorCommand>
            { ["set_parameter"] = new Bimwright.Ipt.Shared.Handlers.Parameters.SetParameterHandler() }
        };
        string docId = EntityReferences.DocumentId((Inventor.Document)part);
        var handler = new AtomicBatchHandler();
        JObject Request(bool preview, bool fail = false) => new()
        {
            ["document_id"] = docId, ["expected_revision"] = journal.Revision(docId), ["preview"] = preview,
            ["operations"] = new JArray(
                new JObject { ["command"] = "set_parameter", ["arguments"] = new JObject { ["name"] = parameterName, ["value"] = "25 mm" } },
                new JObject { ["command"] = "set_parameter", ["arguments"] = new JObject { ["name"] = fail ? "__does_not_exist__" : parameterName, ["value"] = "25 mm" } })
        };
        double Value() => Convert.ToDouble(part.ComponentDefinition.Parameters[parameterName].Value);
        double original = Value();
        var initialTransaction = app.TransactionManager.CurrentTransaction;
        Console.WriteLine("Fixture initial transaction: " + (initialTransaction == null ? "none" :
            $"{initialTransaction.Id}, {initialTransaction.DisplayName}, {initialTransaction.State}"));
        var preview = handler.Execute(ctx, Request(true));
        if ((string?)preview.Data?["status"] != "preview_rolled_back" || Math.Abs(Value() - original) > 1e-8)
            throw new InvalidOperationException("Live preview did not restore parameter");
        Expect(handler.Execute(ctx, Request(false, true)), InventorErrorCodes.ROLLED_BACK, "Invalid batch");
        if (Math.Abs(Value() - original) > 1e-8) throw new InvalidOperationException("Failed batch did not restore parameter");
        var stale = Request(false);
        stale["expected_revision"] = "stale";
        Expect(handler.Execute(ctx, stale), InventorErrorCodes.STALE_REVISION, "Stale revision");
        var external = app.TransactionManager.StartTransaction((Inventor._Document)part, "Fixture ownership test");
        try
        {
            Expect(handler.Execute(ctx, Request(false)), InventorErrorCodes.TRANSACTION_BUSY, "Existing transaction");
            if (!ReferenceEquals(app.TransactionManager.CurrentTransaction, external)) throw new Exception("External transaction ownership was changed");
        }
        finally { external.Abort(); }
        var committed = handler.Execute(ctx, Request(false));
        if ((string?)committed.Data?["status"] != "committed" || Math.Abs(Value() - 2.5) > 1e-8)
            throw new InvalidOperationException("Live commit failed");
        Console.WriteLine(new JObject { ["probe"] = "atomic batch", ["preview_restored"] = true, ["failure_rolled_back"] = true,
            ["stale_revision_rejected"] = true, ["external_transaction_preserved"] = true, ["committed_length_mm"] = Value() * 10 });
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static int Probe()
    {
        Marshal.ThrowExceptionForHR(CLSIDFromProgID("Inventor.Application", out var clsid));
        GetActiveObject(ref clsid, IntPtr.Zero, out var com);
        var app = (Inventor.Application)com;
        var doc = app.ActiveDocument ?? throw new InvalidOperationException("No active document");
        object entity = doc switch
        {
            Inventor.AssemblyDocument assembly when assembly.ComponentDefinition.Occurrences.Count > 0 => assembly.ComponentDefinition.Occurrences[1],
            Inventor.PartDocument part when part.ComponentDefinition.SurfaceBodies.Count > 0 => part.ComponentDefinition.SurfaceBodies[1].Faces[1],
            _ => throw new InvalidOperationException("Probe needs an existing assembly occurrence or part face")
        };
        int documentCount = app.Documents.Count;
        bool dirty = doc.Dirty;
        string revision = doc.DatabaseRevisionId;
        var descriptor = EntityReferences.Describe(doc, entity);
        var resolved = EntityReferences.Resolve(app, (string)descriptor["id"]!);
        if ((string?)resolved["status"] != "resolved") throw new InvalidOperationException(resolved.ToString());
        if (doc.Dirty != dirty || doc.DatabaseRevisionId != revision || app.Documents.Count != documentCount)
            throw new InvalidOperationException("Read-only probe changed observed document state");
        Console.WriteLine(new JObject { ["probe"] = "read-only persistent reference roundtrip", ["type"] = descriptor["type"],
            ["status"] = resolved["status"], ["document_count_unchanged"] = true, ["dirty_unchanged"] = true,
            ["database_revision_unchanged"] = true, ["reference_length"] = ((string)descriptor["id"]!).Length });
        return 0;
    }
}
