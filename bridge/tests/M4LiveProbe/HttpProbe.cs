using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using Inventor;
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Glb;
using InventorXrSo.Core.Net;
using InventorXrSo.Core.Pairing;
using InventorXrSo.Core.Tests.Support;
using Newtonsoft.Json.Linq;
using Path = System.IO.Path;
using File = System.IO.File;

internal static class HttpProbe
{
    [DllImport("oleaut32.dll", PreserveSig = false)]
    private static extern void GetActiveObject(ref Guid clsid, IntPtr reserved,
        [MarshalAs(UnmanagedType.IUnknown)] out object app);

    internal static int Run()
    {
        global::Inventor.Application? app = null;
        global::Inventor.Document? original = null;
        PartDocument? part = null;
        AssemblyDocument? assembly = null;
        try
        {
            var clsid = Type.GetTypeFromProgID("Inventor.Application")!.GUID;
            GetActiveObject(ref clsid, IntPtr.Zero, out var running);
            app = (global::Inventor.Application)running;
            original = app.ActiveDocument;

            var fixtureDirectory = Path.Combine(Path.GetTempPath(), "xrso-m4-http-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(fixtureDirectory);
            var partPath = Path.Combine(fixtureDirectory, "probe-cylinder.ipt");
            part = (PartDocument)app.Documents.Add(DocumentTypeEnum.kPartDocumentObject,
                app.FileManager.GetTemplateFile(DocumentTypeEnum.kPartDocumentObject), true);
            var sketch = part.ComponentDefinition.Sketches.Add(part.ComponentDefinition.WorkPlanes[3]);
            sketch.SketchCircles.AddByCenterRadius(app.TransientGeometry.CreatePoint2d(0, 0), 1);
            var extrusion = part.ComponentDefinition.Features.ExtrudeFeatures.CreateExtrudeDefinition(
                sketch.Profiles.AddForSolid(), PartFeatureOperationEnum.kJoinOperation);
            extrusion.SetDistanceExtent(2, PartFeatureExtentDirectionEnum.kPositiveExtentDirection);
            part.ComponentDefinition.Features.ExtrudeFeatures.Add(extrusion);
            part.SaveAs(partPath, false);

            assembly = (AssemblyDocument)app.Documents.Add(DocumentTypeEnum.kAssemblyDocumentObject);
            var assemblyDefinition = assembly.ComponentDefinition;
            var fixedOccurrence = assemblyDefinition.Occurrences.Add(partPath, app.TransientGeometry.CreateMatrix());
            fixedOccurrence.Grounded = true;
            var freePlacement = app.TransientGeometry.CreateMatrix();
            freePlacement.SetTranslation(app.TransientGeometry.CreateVector(5, 0, 0));
            var freeOccurrence = assemblyDefinition.Occurrences.Add(partPath, freePlacement);
            freeOccurrence.Grounded = false;

            var local = Path.Combine(Path.GetTempPath(), "xrso-quest-live");
            var credentialLine = File.ReadLines(Path.Combine(local, "tokens-revocation.txt"))
                .Select(line => line.Trim()).First(line => line.Length > 0 && !line.StartsWith('#'));
            var credentials = credentialLine.Split(':', 2);
            if (credentials.Length != 2) throw new FormatException("Local HTTPS pairing entry is malformed.");
            using var certificate = X509CertificateLoader.LoadPkcs12FromFile(Path.Combine(local, "server.pfx"), null);
            var pin = CertificatePin.Sha256Hex(certificate.RawData);
            using var transport = new SystemHttpTransport(ServerTrust.Pinned(pin));
            var backend = new InventorBackend(transport,
                new PairedServer("localhost", 8443, pin, credentials[0], credentials[1].Trim()), new MemoryAssetCache());
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            var ct = timeout.Token;
            backend.ConnectAsync(ct).GetAwaiter().GetResult();

            var assemblyId = "doc_" + ((global::Inventor.Document)assembly).InternalName;
            var state = CurrentState(backend, assemblyId, ct);
            var overview = backend.GetAssemblyContextAsync(state, null, ct).GetAwaiter().GetResult();
            Check(overview.Occurrences.Count == 2, "HTTPS assembly context returns both fixture occurrences");
            var fixedDto = overview.Occurrences.Single(item => item.Name == fixedOccurrence.Name);
            var freeDto = overview.Occurrences.Single(item => item.Name == freeOccurrence.Name);
            Check(fixedDto.Grounded && !freeDto.Grounded && freeDto.CanMove, "Context identifies grounded and free occurrence DOF");

            var fixedContext = backend.GetAssemblyContextAsync(state, fixedDto.Id, ct).GetAwaiter().GetResult();
            var freeContext = backend.GetAssemblyContextAsync(state, freeDto.Id, ct).GetAwaiter().GetResult();
            var fixedCircle = fixedContext.References.First(reference => reference.IsCircle);
            var freeCircle = freeContext.References.First(reference => reference.IsCircle);
            Check(fixedCircle.Available && freeCircle.Available, "HTTP context exposes usable circular proxy references");
            var definitionMesh = backend.GetDefinitionMeshAsync(fixedDto.DefinitionId, ct).GetAwaiter().GetResult();
            var definition = GlbModel.Parse(backend.GetAssetAsync(definitionMesh, ct).GetAwaiter().GetResult());
            Check(definition.DocumentId == fixedDto.DefinitionId && definition.Primitives.Count > 0,
                "Pinned HTTPS definition mesh downloads and parses");
            Console.WriteLine("PASS HTTPS assembly context and cylinder definition mesh.");

            var initialX = freeOccurrence.Transformation.Cell[1, 4];
            var move = AssemblyOperations.Move(freeDto.Id, new CadPoint(10, 0, 0));
            var movePreview = backend.PreviewDesignWithChecksAsync(state, new JArray(move),
                new[] { "min_clearance:1mm" }, ct).GetAwaiter().GetResult();
            Check(movePreview.IsAssembly && movePreview.Model.Primitives.Count > 0,
                "Assembly movement preview returns mesh");
            Check(Math.Abs(freeOccurrence.Transformation.Cell[1, 4] - initialX) < 1e-6,
                "Movement preview rolls the native pose back");
            var afterMovePreview = CurrentState(backend, assemblyId, ct);
            Check(afterMovePreview.Revision == state.Revision, "Movement preview preserves assembly revision");
            state = afterMovePreview;
            Console.WriteLine("PASS HTTPS move preview, GLB mesh, rollback, and min_clearance validation.");

            state = backend.CommitDesignAsync(movePreview, ct).GetAwaiter().GetResult();
            Check(Math.Abs(freeOccurrence.Transformation.Cell[1, 4] - (initialX + 1)) < 1e-5,
                "Committed 10 mm movement changes native Inventor pose");
            var history = backend.GetHistoryAsync(state, ct).GetAwaiter().GetResult();
            Check(history.CanUndo, "XR Undo offered after movement commit");
            state = backend.ApplyHistoryAsync(history, false, ct).GetAwaiter().GetResult();
            Check(Math.Abs(freeOccurrence.Transformation.Cell[1, 4] - initialX) < 1e-5,
                "XR Undo restores native occurrence pose");
            history = backend.GetHistoryAsync(state, ct).GetAwaiter().GetResult();
            Check(history.CanRedo, "XR Redo offered after movement undo");
            state = backend.ApplyHistoryAsync(history, true, ct).GetAwaiter().GetResult();
            Check(Math.Abs(freeOccurrence.Transformation.Cell[1, 4] - (initialX + 1)) < 1e-5,
                "XR Redo restores the committed movement");
            Console.WriteLine("PASS HTTPS move commit and scoped Undo/Redo verified against Inventor pose.");

            var staleMove = AssemblyOperations.Move(freeDto.Id, new CadPoint(1, 0, 0));
            var stalePreview = backend.PreviewDesignWithChecksAsync(state, new JArray(staleMove),
                new[] { "min_clearance:1mm" }, ct).GetAwaiter().GetResult();
            var nativeTransaction = app.TransactionManager.StartTransaction((global::Inventor._Document)assembly,
                "M4 HTTPS stale-preview probe");
            try
            {
                var changed = freeOccurrence.Transformation.Copy();
                changed.Cell[1, 4] += 0.1; // 1 mm, Inventor database units are centimetres.
                freeOccurrence.Transformation = changed;
                nativeTransaction.End();
            }
            catch
            {
                nativeTransaction.Abort();
                throw;
            }
            var changedState = CurrentState(backend, assemblyId, ct, state.Revision);
            bool staleRejected = false;
            try { backend.CommitDesignAsync(stalePreview, ct).GetAwaiter().GetResult(); }
            catch (InventorXrSo.Core.Mcp.McpToolException ex) when (ex.Code == "STALE_REVISION") { staleRejected = true; }
            Check(staleRejected, "Commit rejects a preview after a native desktop edit");
            state = changedState;
            var desktopHistory = backend.GetHistoryAsync(state, ct).GetAwaiter().GetResult();
            Check(!desktopHistory.CanUndo && !desktopHistory.CanRedo, "Native desktop edit invalidates the XR history chain");
            Console.WriteLine("PASS HTTPS stale preview refused after native Inventor edit.");

            overview = backend.GetAssemblyContextAsync(state, null, ct).GetAwaiter().GetResult();
            fixedDto = overview.Occurrences.Single(item => item.Name == fixedOccurrence.Name);
            freeDto = overview.Occurrences.Single(item => item.Name == freeOccurrence.Name);
            fixedContext = backend.GetAssemblyContextAsync(state, fixedDto.Id, ct).GetAwaiter().GetResult();
            freeContext = backend.GetAssemblyContextAsync(state, freeDto.Id, ct).GetAwaiter().GetResult();
            fixedCircle = fixedContext.References.First(reference => reference.IsCircle);
            freeCircle = freeContext.References.First(reference => reference.IsCircle);
            var rotational = AssemblyOperations.Joint("rotational", fixedCircle, freeCircle, 30);
            var jointChecks = new[] { "min_clearance:1mm" };
            var jointPreview = backend.PreviewDesignWithChecksAsync(state, new JArray(rotational), jointChecks, ct)
                .GetAwaiter().GetResult();
            Check(jointPreview.IsAssembly && jointPreview.Model.Primitives.Count > 0,
                "Rotational joint preview returns assembly mesh");
            Check(assemblyDefinition.Joints.Count == 0, "Rotational joint preview rolls back");
            var afterJointPreview = CurrentState(backend, assemblyId, ct);
            Check(afterJointPreview.Revision == state.Revision, "Joint preview preserves assembly revision");
            state = afterJointPreview;
            var jointCommit = backend.CommitDesignAsync(jointPreview, ct).GetAwaiter().GetResult();
            Check(assemblyDefinition.Joints.Count == 1, "Rotational joint commit persists in Inventor");
            state = CurrentState(backend, assemblyId, ct);
            Check(state.Revision == jointCommit.Revision, "Joint commit returns the current revision");
            overview = backend.GetAssemblyContextAsync(state, null, ct).GetAwaiter().GetResult();
            freeDto = overview.Occurrences.Single(item => item.Name == freeOccurrence.Name);
            Check(freeDto.DofComplete && freeDto.TranslationCount == 0 && freeDto.RotationCount == 1,
                "Rotational joint leaves the expected one rotational DOF");
            Console.WriteLine("PASS HTTPS rotational joint preview/commit and residual DOF (0 translation, 1 rotation).");
            Console.WriteLine("M4 HTTP fixture retained at: " + fixtureDirectory);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("M4 HTTPS probe failed: " + ex.GetType().Name + ": " + ex.Message);
            Console.Error.WriteLine(ex.StackTrace);
            return 1;
        }
        finally
        {
            try { if (assembly != null) ((global::Inventor.Document)assembly).Close(true); }
            finally
            {
                try { if (part != null) ((global::Inventor.Document)part).Close(true); }
                finally { original?.Activate(); }
            }
        }
    }

    private static DocumentState CurrentState(InventorBackend backend, string documentId,
        CancellationToken ct, string? revisionToAvoid = null)
    {
        for (int attempt = 0; attempt < 12; attempt++)
        {
            System.Windows.Forms.Application.DoEvents();
            var state = backend.GetDocumentStateAsync(ct).GetAwaiter().GetResult();
            if (state.DocumentId == documentId && !string.IsNullOrWhiteSpace(state.Revision)
                && state.Revision != revisionToAvoid) return state;
            Thread.Sleep(150);
        }
        throw new InvalidOperationException("HTTPS document state did not settle for the temporary assembly.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Assertion failed: " + message);
    }
}
