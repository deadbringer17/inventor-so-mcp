using System.Security.Cryptography.X509Certificates;
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Net;
using InventorXrSo.Core.Pairing;
using InventorXrSo.Core.Tests.Support;
using Newtonsoft.Json.Linq;
using System.Reflection;
using Inventor;
using Bimwright.Ipt.Shared.Handlers.Core;
using File = System.IO.File;
using Path = System.IO.Path;
using Environment = System.Environment;

internal static class QuestPlanarProbe
{
    internal static int Run(global::Inventor.Application app, bool commit)
    {
        var fixture = JObject.Parse(File.ReadAllText(Path.Combine(Environment.CurrentDirectory,
            "artifacts", "m4-verification", "quest-fixture.json")));
        var expectedId = (string?)fixture["document_id"] ?? throw new InvalidDataException("Fixture document missing.");
        var movingName = (string?)fixture["moving"] ?? throw new InvalidDataException("Fixture occurrence missing.");
        var local = Path.Combine(Path.GetTempPath(), "xrso-quest-live");
        var line = File.ReadLines(Path.Combine(local, "tokens-revocation.txt"))
            .Select(item => item.Trim()).First(item => item.Length > 0 && !item.StartsWith('#'));
        var credentials = line.Split(':', 2);
        using var certificate = X509CertificateLoader.LoadPkcs12FromFile(Path.Combine(local, "server.pfx"), null);
        var pin = CertificatePin.Sha256Hex(certificate.RawData);
        using var transport = new SystemHttpTransport(ServerTrust.Pinned(pin));
        var backend = new InventorBackend(transport,
            new PairedServer("localhost", 8443, pin, credentials[0], credentials[1].Trim()), new MemoryAssetCache());
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var ct = timeout.Token;
        backend.ConnectAsync(ct).GetAwaiter().GetResult();
        var initial = backend.GetDocumentStateAsync(ct).GetAwaiter().GetResult();
        if (initial.DocumentId != expectedId) throw new InvalidOperationException("The dedicated Quest fixture is not active.");
        var overview = backend.GetAssemblyContextAsync(initial, null, ct).GetAwaiter().GetResult();
        var fixedOccurrence = overview.Occurrences.Single(item => item.Grounded);
        var moving = overview.Occurrences.Single(item => item.Name == movingName);
        var fixedReferences = backend.GetAssemblyContextAsync(initial, fixedOccurrence.Id, ct).GetAwaiter().GetResult().References;
        var movingReferences = backend.GetAssemblyContextAsync(initial, moving.Id, ct).GetAwaiter().GetResult().References;
        var a = fixedReferences.Single(item => item.Kind == "face" && item.FaceOrdinal == 3);
        var b = movingReferences.Single(item => item.Kind == "face" && item.FaceOrdinal == 2);
        Console.WriteLine($"A {fixedOccurrence.Name} {a.Name}: {a.Geometry}; B {moving.Name} {b.Name}: {b.Geometry}");
        Console.WriteLine("Compatible constraints: " + string.Join(", ", AssemblyOperations.CompatibleConstraints(a, b)));
        void PreviewPair(AssemblyReference left, AssemblyReference right, double gap = 0)
        {
            try
            {
                backend.PreviewDesignAsync(initial,
                    new JArray(AssemblyOperations.Joint("planar", left, right, gap)), ct).GetAwaiter().GetResult();
                Console.WriteLine($"PLANAR PASS {left.Name}/{right.Name} gap={gap} mm ({left.Geometry}/{right.Geometry})");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"PLANAR REJECTED {left.Name}/{right.Name} gap={gap} mm ({left.Geometry}/{right.Geometry}): {ex.GetType().Name}: {ex.Message}");
            }
        }
        PreviewPair(a, b);
        PreviewPair(a, b, 30);
        var fixedPlanes = fixedReferences.Where(item => item.IsPlane && item.Available).Take(2).ToArray();
        var movingPlanes = movingReferences.Where(item => item.IsPlane && item.Available).Take(2).ToArray();
        foreach (var left in fixedPlanes)
            foreach (var right in movingPlanes)
                if (left.Id != a.Id || right.Id != b.Id) PreviewPair(left, right);
        var fixedCircle = fixedReferences.First(item => item.IsCircle && item.Available);
        var movingCircle = movingReferences.First(item => item.IsCircle && item.Available);
        PreviewPair(fixedCircle, movingCircle);
        var nativeAssembly = app.ActiveDocument as AssemblyDocument
            ?? throw new InvalidOperationException("Quest fixture is not an Inventor assembly.");
        var resolve = typeof(CreateJointHandler).GetMethod("Resolve", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingMethodException("CreateJointHandler.Resolve");
        void NativeStages(AssemblyReference left, AssemblyReference right)
        {
            try
            {
                var (entityA, _) = ((object, ComponentOccurrence))resolve.Invoke(null, new object[] { nativeAssembly, left.Id })!;
                var (entityB, _) = ((object, ComponentOccurrence))resolve.Invoke(null, new object[] { nativeAssembly, right.Id })!;
                var def = nativeAssembly.ComponentDefinition;
                var intentA = def.CreateGeometryIntent(entityA);
                Console.WriteLine($"NATIVE {left.Name}/{right.Name}: intent A OK");
                var intentB = def.CreateGeometryIntent(entityB);
                Console.WriteLine($"NATIVE {left.Name}/{right.Name}: intent B OK");
                var definition = def.Joints.CreateAssemblyJointDefinition(AssemblyJointTypeEnum.kPlanarJointType, intentA, intentB);
                Console.WriteLine($"NATIVE {left.Name}/{right.Name}: joint definition OK");
                definition.Gap = 0;
                Console.WriteLine($"NATIVE {left.Name}/{right.Name}: gap OK");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"NATIVE {left.Name}/{right.Name}: {ex.GetType().Name}: {ex.InnerException?.Message ?? ex.Message}");
            }
        }
        NativeStages(a, b);
        NativeStages(fixedCircle, movingCircle);
        var createOrigin = typeof(CreateJointHandler).GetMethod("CreateOriginIntent", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingMethodException("CreateJointHandler.CreateOriginIntent");
        try
        {
            var (entityA, _) = ((object, ComponentOccurrence))resolve.Invoke(null, new object[] { nativeAssembly, a.Id })!;
            var (entityB, _) = ((object, ComponentOccurrence))resolve.Invoke(null, new object[] { nativeAssembly, b.Id })!;
            var def = nativeAssembly.ComponentDefinition;
            GeometryIntent FaceCenter(object entity)
            {
                var face = (FaceProxy)entity;
                var edge = face.Edges.Cast<EdgeProxy>().First(item => item.GeometryType == CurveTypeEnum.kCircleCurve);
                var center = def.CreateGeometryIntent(edge, PointIntentEnum.kCenterPointIntent);
                return def.CreateGeometryIntent(face, center);
            }
            var planeDefinition = def.Joints.CreateAssemblyJointDefinition(AssemblyJointTypeEnum.kPlanarJointType,
                FaceCenter(entityA), FaceCenter(entityB));
            planeDefinition.Gap = 0;
            Console.WriteLine("NATIVE face + circular-edge center: planar definition and gap OK");
            var actualA = (GeometryIntent)createOrigin.Invoke(null,
                new object[] { def, entityA, AssemblyJointTypeEnum.kPlanarJointType })!;
            var actualB = (GeometryIntent)createOrigin.Invoke(null,
                new object[] { def, entityB, AssemblyJointTypeEnum.kPlanarJointType })!;
            var actual = def.Joints.CreateAssemblyJointDefinition(AssemblyJointTypeEnum.kPlanarJointType, actualA, actualB);
            actual.Gap = 0;
            Console.WriteLine("PASS NATIVE production planar-face origin helper creates definition and gap");
        }
        catch (Exception ex)
        {
            Console.WriteLine("NATIVE face + circular-edge center: " + ex.GetType().Name + ": " + (ex.InnerException?.Message ?? ex.Message));
        }
        var after = backend.GetDocumentStateAsync(ct).GetAwaiter().GetResult();
        if (after.DocumentId != initial.DocumentId || after.Revision != initial.Revision)
            throw new InvalidOperationException("Planar preview changed Inventor revision.");
        Console.WriteLine("PASS; fixture revision unchanged after Planar diagnostic.");
        if (commit)
        {
            var beforeCenter = moving.Center ?? throw new InvalidOperationException("Moving occurrence has no center.");
            var plan = backend.PreviewDesignAsync(initial,
                new JArray(AssemblyOperations.Joint("planar", a, b, 30)), ct).GetAwaiter().GetResult();
            var committed = backend.CommitDesignAsync(plan, ct).GetAwaiter().GetResult();
            var joined = backend.GetAssemblyContextAsync(committed, null, ct).GetAwaiter().GetResult();
            var joinedMoving = joined.Occurrences.Single(item => item.Name == movingName);
            if (!joinedMoving.DofComplete || joinedMoving.TotalDof != 3)
                throw new InvalidOperationException("Committed Planar joint did not leave its expected three DOF.");
            Console.WriteLine("PASS; Planar face joint committed with three residual DOF.");
            var history = backend.GetHistoryAsync(committed, ct).GetAwaiter().GetResult();
            if (!history.CanUndo) throw new InvalidOperationException("No XR Undo after Planar commit.");
            var undone = backend.ApplyHistoryAsync(history, false, ct).GetAwaiter().GetResult();
            var undoneContext = backend.GetAssemblyContextAsync(undone, null, ct).GetAwaiter().GetResult();
            var restored = undoneContext.Occurrences.Single(item => item.Name == movingName);
            if (!restored.Center.HasValue || Math.Abs(restored.Center.Value.X - beforeCenter.X) > 0.02 ||
                Math.Abs(restored.Center.Value.Y - beforeCenter.Y) > 0.02 ||
                Math.Abs(restored.Center.Value.Z - beforeCenter.Z) > 0.02 || restored.TotalDof != moving.TotalDof)
                throw new InvalidOperationException("Undo did not restore the original free occurrence.");
            Console.WriteLine("PASS; XR Undo restored original pose and DOF after Planar face joint.");
        }
        return 0;
    }
}
