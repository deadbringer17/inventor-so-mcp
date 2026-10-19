using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using Bimwright.Ipt.Server;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Server.Imports;
using Inventor;
using Inventor.So.Mcp.Http;
using Inventor.So.Mcp.Http.Pairing;
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Net;
using InventorXrSo.Core.Pairing;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using File = System.IO.File;
using Path = System.IO.Path;

internal static partial class Program
{
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length is < 1 or > 3 || args.Skip(1).Any(a => a is not ("--allow-existing-inventor" or "--structure56")) || args.Skip(1).Distinct().Count() != args.Length - 1) return 64;
        bool structure54 = args.Contains("--structure56"), allowExisting = args.Contains("--allow-existing-inventor");
        string family = structure54 ? "image-structure56" : "image-http53";
        string fixturePrefix = structure54 ? "S1_ImageStructure56_" : "S1_ImageHttp53_";
        var report = new JObject { ["captured_utc"] = DateTime.UtcNow.ToString("O"),
            ["evidence"] = "Actual loaded Inventor add-in, production HTTP MCP and PNG asset, production Core backend/client. No Unity, Quest or physical evidence.",
            ["native_identity_criterion"] = "Exact native reference keys (context0), not transient out-of-process COM proxy addresses." };
        if (structure54) report["evidence"] = "Actual Inventor add-in structural image snapshot, production HTTP MCP, Core56 parser and native mutation/anchor guards. No Unity, Quest or physical evidence.";
        string scratch = Path.Combine(Path.GetTempPath(), "s1-" + family + "-" + Guid.NewGuid().ToString("N"));
        Inventor.Application? app = null; PartDocument? doc = null; WebApplication? host = null;
        int pid = 0, exit = 1; bool owned = false;
        var existingProcesses = ExistingProcesses();
        report["existing_processes_before"] = new JArray(existingProcesses.Select(pair => new JObject { ["pid"] = pair.Key, ["start_utc_ticks"] = pair.Value }));
        try
        {
            if (existingProcesses.Count != 0 && !allowExisting) throw new ProbeFailure("Inventor must be closed unless existing-process isolation is explicitly enabled.");
            Directory.CreateDirectory(scratch);
            app = (Inventor.Application)Activator.CreateInstance(Type.GetTypeFromProgID("Inventor.Application")!)!;
            GetWindowThreadProcessId(new IntPtr(unchecked((long)(uint)app.MainFrameHWND)), out uint nativePid);
            pid = checked((int)nativePid);
            // This is the only COM read before proving the returned instance is distinct. Never
            // inspect Documents, set Visible, create fixtures or Quit a previously running PID.
            if (pid == 0 || existingProcesses.ContainsKey(pid)) { pid = 0; throw new ProbeFailure("COM did not return a distinct Inventor process; existing instance left untouched."); }
            owned = app.Documents.Count == 0;
            if (!owned) throw new ProbeFailure("Cannot prove exclusive ownership of empty Inventor process.");
            report["owned_pid"] = pid; app.Visible = true;
            string descriptors = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "InventorSO", "inventor-so-mcp");
            TargetDescriptor? target = null; var wait = Stopwatch.StartNew();
            while (wait.Elapsed < TimeSpan.FromSeconds(45))
            {
                string descriptor = Path.Combine(descriptors, $"inventor-2027-{pid}.json");
                if (File.Exists(descriptor))
                {
                    try { target = JsonConvert.DeserializeObject<TargetDescriptor>(File.ReadAllText(descriptor)); } catch (IOException) { }
                    if (target?.ProcessId == pid && target.Transport == "pipe" && !string.IsNullOrEmpty(target.AuthToken)) break;
                }
                Thread.Sleep(100);
            }
            if (target?.ProcessId != pid || target.InventorYear != 2027 || target.HostApp != "Inventor" || target.Transport != "pipe"
                || string.IsNullOrEmpty(target.TargetId) || string.IsNullOrEmpty(target.PipeName) || string.IsNullOrEmpty(target.AuthToken))
                throw new ProbeFailure("Owned authenticated add-in descriptor unavailable.");
            var tokens = new TokenRegistry(); string token = TokenRegistry.Generate(), foreign = TokenRegistry.Generate();
            string owner = "s1-" + family + "-editor";
            tokens.Add(owner, token); tokens.Add("s1-" + family + "-foreign", foreign);
            var config = new InventorMcpConfig { DescriptorDirectory = descriptors, TargetId = target.TargetId,
                HttpUrls = new List<string> { "https://127.0.0.1:0" }, HttpSelfSignedCertificate = true,
                HttpSelfSignedPath = Path.Combine(scratch, "server.pfx"), AssetDirectory = Path.Combine(scratch, "assets"),
                AuditDirectory = Path.Combine(scratch, "audit"), EnableExperimental = true };
            using var certificate = PairingSetup.ResolveCertificate(config)!;
            string sha = SelfSignedCertificate.Sha256Hex(certificate);
            var imports = structure54 ? new ImportResourceStore(new ImportResourceStorePolicy(1024 * 1024, 2 * 1024 * 1024, 4, 4096, 16, TimeSpan.FromMinutes(10), new[] { "bmp" })) : null;
            host = HttpHost.Build(Array.Empty<string>(), config, tokens, new Inventor.So.Mcp.Http.Pairing.HostOptions {
                Certificate = certificate, ConfigureServices = imports == null ? null : services => services.AddSingleton(imports) });
            host.StartAsync().GetAwaiter().GetResult();
            var address = new Uri(host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single());
            var paired = new PairedServer("127.0.0.1", address.Port, sha, owner, token);
            using var transport = new ProbeTransport(ServerTrust.Pinned(sha));
            var backend = new InventorBackend(transport, paired, new MemoryAssetCache());
            backend.ConnectAsync(CancellationToken.None).GetAwaiter().GetResult();
            var cases = new JArray(); report["cases"] = cases;
            foreach (bool sheet in new[] { false, true })
            {
                report["phase"] = (sheet ? "sheetmetal" : "part") + "_fixture";
                if (app.Documents.Count != 0) throw new ProbeFailure("Owned fixture must begin with zero documents.");
                string template = sheet ? app.FileManager.GetTemplateFile(DocumentTypeEnum.kPartDocumentObject,
                    SystemOfMeasureEnum.kDefaultSystemOfMeasure, DraftingStandardEnum.kDefault_DraftingStandard,
                    "{9C464203-9BAE-11D3-8BAD-0060B0CE6BB4}") : app.FileManager.GetTemplateFile(DocumentTypeEnum.kPartDocumentObject);
                doc = (PartDocument)app.Documents.Add(DocumentTypeEnum.kPartDocumentObject, template, true);
                doc.DisplayName = fixturePrefix + (sheet ? "Sheet" : "Part") + ".ipt";
                if ((doc.ComponentDefinition is SheetMetalComponentDefinition) != sheet) throw new ProbeFailure("Fixture subtype differs.");
                var sketch = doc.ComponentDefinition.Sketches.Add(doc.ComponentDefinition.WorkPlanes[3]);
                if (structure54)
                {
                    RunStructure56(app, doc, sketch, backend, transport, paired, imports!, scratch, report, cases, sheet,
                        target, descriptors, host.Services.GetRequiredService<PluginClient>());
                    doc.Close(true); doc = null;
                    if (app.Documents.Count != 0) throw new ProbeFailure("Owned structure fixture did not close.");
                    continue;
                }
                string source = Path.Combine(scratch, "source-" + sheet + ".bmp"); WriteBmp(source);
                var image = sketch.SketchImages.Add(source, app.TransientGeometry.CreatePoint2d(1.3, 2.1), false);
                double x = image.Position.X * 10, y = image.Position.Y * 10, width = image.Width * 10, height = image.Height * 10;
                var raw = new[] { new[] { x, y }, new[] { x + width, y }, new[] { x + width, y - height }, new[] { x, y - height } };
                var objects = app.TransientObjects.CreateObjectCollection(); objects.Add(image);
                sketch.RotateSketchObjects(objects, app.TransientGeometry.CreatePoint2d(x / 10, y / 10), 37 * Math.PI / 180, false, false);
                image.MirrorHorizontal(); doc.Update2(true); File.Delete(source);
                if (image.LinkedToFile || image.Transparent || File.Exists(source)) throw new ProbeFailure("Fixture is not opaque, embedded and source independent.");
                if (image.AttributeSets.NameIsUsed["InventorSO_SketchImagePayload"] || image.AttributeSets.NameIsUsed["InventorSO_SketchImageFrame"])
                    throw new ProbeFailure("Fixture unexpectedly has managed image metadata.");
                string documentId = DocumentIdComposer.Compose(doc.InternalName, doc.FullFileName, false);
                string Reference(object entity, string kind)
                {
                    byte[] key = Array.Empty<byte>(); ((dynamic)entity).GetReferenceKey(ref key, 0);
                    return new PersistentEntityReference { DocumentId = documentId, EntityType = kind, Key = key, Context = Array.Empty<byte>() }.Encode();
                }
                string sketchId = Reference(sketch, "sketch"), imageId = Reference(image, "sketch_image");
                var state = backend.GetDocumentStateAsync(CancellationToken.None).GetAwaiter().GetResult();
                if (state.DocumentId != documentId) throw new ProbeFailure("HTTP host did not target owned fixture.");
                var before = State(doc, image, sketch); int gets = transport.AssetGets;
                if (!JToken.DeepEquals(before, State(doc, image, sketch))) throw new ProbeFailure("Native reference-key baseline is not stable before the image query.");
                report["phase"] = (sheet ? "sheetmetal" : "part") + "_production_http_core_image";
                using var asset = backend.GetNativeSketchImageAsync(state, sketchId, imageId, CancellationToken.None).GetAwaiter().GetResult();
                report["phase"] = (sheet ? "sheetmetal" : "part") + "_core_asset_metadata";
                if (!asset.Matches(state, sketchId, imageId) || asset.PixelWidth != 3 || asset.PixelHeight != 2 || !asset.Visible
                    || asset.Transparent || asset.SourceKind != "native_bitmap" || asset.Provenance != "native_boundary_roles") throw new ProbeFailure("Core asset identity/metadata differs.");
                var expected = new[] { 1, 0, 3, 2 }.Select(i => (double[])raw[i].Clone()).ToArray();
                double cosine = Math.Cos(37 * Math.PI / 180), sine = Math.Sin(37 * Math.PI / 180);
                for (int i = 0; i < 4; i++)
                {
                    double dx = expected[i][0] - x, dy = expected[i][1] - y;
                    expected[i][0] = x + dx * cosine - dy * sine; expected[i][1] = y + dx * sine + dy * cosine;
                    Near(asset.CornersMm[i].X, expected[i][0]); Near(asset.CornersMm[i].Y, expected[i][1]);
                }
                Near(asset.WidthMm, width); Near(asset.HeightMm, height);
                report["phase"] = (sheet ? "sheetmetal" : "part") + "_png_rgba";
                byte[] png = asset.CopyPngBytes();
                try { VerifyPng(png); } finally { Array.Clear(png); }
                if (transport.AssetGets != gets + 1 || transport.LastAssetUrl != paired.BaseUrl + "/assets/" + asset.AssetId)
                    throw new ProbeFailure("Actual paired HTTPS asset GET was not observed.");
                var request = new TransportRequest("GET", transport.LastAssetUrl) { MaxResponseBytes = 4096 };
                report["phase"] = (sheet ? "sheetmetal" : "part") + "_foreign_owner";
                request.Headers["Authorization"] = "Bearer " + foreign;
                var foreignResponse = transport.SendAsync(request, CancellationToken.None).GetAwaiter().GetResult();
                if (foreignResponse.Status != 404) throw new ProbeFailure("Foreign owner could read generated asset.");
                var after = backend.GetDocumentStateAsync(CancellationToken.None).GetAwaiter().GetResult();
                report["phase"] = (sheet ? "sheetmetal" : "part") + "_readonly_comparison";
                var nativeAfter = State(doc, image, sketch);
                var comparisons = report["readonly_comparisons"] as JArray;
                if (comparisons == null) { comparisons = new JArray(); report["readonly_comparisons"] = comparisons; }
                comparisons.Add(new JObject { ["kind"] = sheet ? "sheetmetal" : "part", ["native_before"] = before.DeepClone(),
                    ["native_after"] = nativeAfter.DeepClone(), ["native_exact"] = JToken.DeepEquals(before, nativeAfter),
                    ["backend_before"] = new JObject { ["document_id"] = state.DocumentId, ["revision"] = state.Revision, ["visual_revision"] = state.VisualRevision },
                    ["backend_after"] = new JObject { ["document_id"] = after.DocumentId, ["revision"] = after.Revision, ["visual_revision"] = after.VisualRevision } });
                if (state.DocumentId != after.DocumentId || state.Revision != after.Revision || state.VisualRevision != after.VisualRevision
                    || !JToken.DeepEquals(before, nativeAfter)) throw new ProbeFailure("Image query changed native document or backend revision.");
                cases.Add(new JObject { ["kind"] = sheet ? "sheetmetal" : "part", ["outcome"] = "PASS",
                    ["source_deleted"] = true, ["actual_mcp_https_asset_core"] = true, ["pixel_rgba_exact"] = true,
                    ["frame_theory_exact"] = true, ["read_only_exact"] = true, ["foreign_owner_refused"] = true,
                    ["expected_corners_mm"] = new JArray(expected.Select(p => new JArray(p))), ["asset_id"] = asset.AssetId });
                doc.Close(true); doc = null;
                if (app.Documents.Count != 0) throw new ProbeFailure("Owned fixture did not close.");
            }
            report["completed_cases"] = cases.Count; report["outcome"] = "PASS"; exit = 0;
        }
        catch (Exception error)
        {
            // Never serialize exceptions: transport messages may contain request headers or wire data.
            report["outcome"] = "FAIL"; report["error_type"] = error.GetType().FullName; report["error_hresult"] = error.HResult;
            if (error is ProbeFailure) report["reason"] = error.Message;
            if (error is InventorXrSo.Core.Mcp.McpException gateway && System.Text.RegularExpressions.Regex.IsMatch(gateway.Code ?? "", "^[A-Z_]{2,64}$")) report["error_code"] = gateway.Code;
            if (error is InventorXrSo.Core.Mcp.McpToolException step && step.Details?["details"] is JObject stepDetails && stepDetails["step_index"]?.Type == JTokenType.Integer)
            {
                report["step_index"] = stepDetails["step_index"]!.DeepClone();
                string? stepCode = (string?)stepDetails["step_code"];
                if (System.Text.RegularExpressions.Regex.IsMatch(stepCode ?? "", "^[A-Z_]{2,64}$")) report["step_code"] = stepCode;
            }
        }
        finally
        {
            try { if (host != null) { host.StopAsync().GetAwaiter().GetResult(); host.DisposeAsync().AsTask().GetAwaiter().GetResult(); } } catch { report["host_cleanup_failed"] = true; exit = 1; }
            try
            {
                if (owned && app != null)
                {
                    if (doc != null && app.Documents.Count == 1 && doc.DisplayName.StartsWith(fixturePrefix, StringComparison.Ordinal) && string.IsNullOrEmpty(doc.FullFileName)) { doc.Close(true); doc = null; }
                    if (app.Documents.Count != 0) throw new ProbeFailure("Unowned documents prevent shutdown.");
                    app.Quit(); report["owned_quit_requested"] = true;
                }
            }
            catch { report["native_cleanup_failed"] = true; exit = 1; }
            if (pid > 0)
            {
                var watch = Stopwatch.StartNew(); bool gone = false;
                while (watch.Elapsed < TimeSpan.FromSeconds(30))
                {
                    try { using var process = Process.GetProcessById(pid); if (process.HasExited) { gone = true; break; } }
                    catch (ArgumentException) { gone = true; break; }
                    Thread.Sleep(100);
                }
                report["owned_pid_exited"] = gone; if (!gone) exit = 1;
            }
            // Delete only exclusively created scratch after documents and owned process are closed.
            if ((bool?)report["owned_pid_exited"] == true)
            {
                try
                {
                    string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
                    if (Path.GetDirectoryName(Path.GetFullPath(scratch)) != root || !Path.GetFileName(scratch).StartsWith("s1-" + family + "-", StringComparison.Ordinal))
                        throw new ProbeFailure("Scratch cleanup target escaped exclusive temporary root.");
                    Directory.Delete(scratch, true); report["scratch_cleanup_complete"] = true;
                }
                catch { report["scratch_cleanup_complete"] = false; exit = 1; }
            }
            bool existingUnchanged = true;
            foreach (var pair in existingProcesses)
            {
                try { using var process = Process.GetProcessById(pair.Key); if (process.HasExited || process.StartTime.ToUniversalTime().Ticks != pair.Value) existingUnchanged = false; }
                catch { existingUnchanged = false; }
            }
            report["existing_process_identity_preserved"] = existingUnchanged;
            if (!existingUnchanged) exit = 1;
            if (exit != 0) report["outcome"] = "FAIL";
            File.WriteAllText(Path.GetFullPath(args[0]), report.ToString(Formatting.Indented));
        }
        return exit;
    }

    private static Dictionary<int, long> ExistingProcesses()
    {
        var values = new Dictionary<int, long>();
        foreach (var process in Process.GetProcessesByName("Inventor")) using (process) values.Add(process.Id, process.StartTime.ToUniversalTime().Ticks);
        return values;
    }

    // Only this probe's fixed assertion text may enter a report; external exception text is omitted.
    private sealed class ProbeFailure : InvalidOperationException
    { internal ProbeFailure(string message) : base(message) { } }

    private static void Near(double a, double b) { if (!double.IsFinite(a) || Math.Abs(a - b) > 1e-5) throw new ProbeFailure("Current image frame differs from independent geometric theory."); }
    private static JObject State(PartDocument doc, SketchImage image, PlanarSketch sketch) => new() {
        ["id"] = doc.InternalName, ["revision"] = doc.DatabaseRevisionId, ["dirty"] = doc.Dirty, ["path"] = doc.FullFileName,
        ["sketch_entities"] = sketch.SketchEntities.Count, ["images"] = sketch.SketchImages.Count,
        ["visible"] = image.Visible, ["transparent"] = image.Transparent, ["linked"] = image.LinkedToFile,
        ["position"] = new JArray(image.Position.X, image.Position.Y), ["width"] = image.Width, ["height"] = image.Height,
        ["image_attributes"] = image.AttributeSets.Count,
        ["boundary"] = new JArray(image.BoundaryGeometry.Cast<SketchLine>().Select(line => new JObject {
            ["identity"] = Identity(line), ["start_identity"] = Identity(line.StartSketchPoint), ["end_identity"] = Identity(line.EndSketchPoint),
            ["start"] = new JArray(line.StartSketchPoint.Geometry.X, line.StartSketchPoint.Geometry.Y),
            ["end"] = new JArray(line.EndSketchPoint.Geometry.X, line.EndSketchPoint.Geometry.Y) })),
        ["ole"] = new JArray(doc.ReferencedOLEFileDescriptors.Cast<ReferencedOLEFileDescriptor>().Select(Identity)) };

    private static string Identity(object value)
    {
        // Out-of-process COM proxies may be collected and recreated during HTTPS/Task.Run work;
        // their pointer addresses cannot identify persistent Inventor model entities (r4 evidence).
        byte[] key = Array.Empty<byte>(); ((dynamic)value).GetReferenceKey(ref key, 0);
        if (key.Length == 0) throw new ProbeFailure("Native entity has no stable reference key.");
        return Convert.ToBase64String(key);
    }

    private static void WriteBmp(string path)
    {
        byte[] bytes = new byte[78]; bytes[0] = 66; bytes[1] = 77;
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(2), bytes.Length); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(10), 54);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(14), 40); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(18), 3);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(22), -2); BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(26), 1);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(28), 32);
        for (int y = 0; y < 2; y++) for (int x = 0; x < 3; x++) { int at = 54 + (y * 3 + x) * 4; bytes[at] = (byte)(y == 0 ? 255 : 0); bytes[at + 1] = (byte)(x * 100); bytes[at + 2] = (byte)(y == 1 ? 255 : 0); }
        File.WriteAllBytes(path, bytes); Array.Clear(bytes);
    }

    private static void VerifyPng(byte[] png)
    {
        using var idat = new MemoryStream(); int at = 8;
        while (at < png.Length)
        {
            int length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(at, 4)));
            if (System.Text.Encoding.ASCII.GetString(png, at + 4, 4) == "IDAT") idat.Write(png, at + 8, length);
            at += length + 12;
        }
        idat.Position = 0; using var zlib = new ZLibStream(idat, CompressionMode.Decompress); byte[] rows = new byte[26];
        zlib.ReadExactly(rows); if (zlib.ReadByte() != -1) throw new ProbeFailure("Unexpected decoded PNG extent.");
        for (int y = 0; y < 2; y++)
        {
            if (rows[y * 13] != 0) throw new ProbeFailure("Unexpected PNG fixture filter.");
            for (int x = 0; x < 3; x++) { int p = y * 13 + 1 + x * 4;
                if (rows[p] != (y == 1 ? 255 : 0) || rows[p + 1] != x * 100 || rows[p + 2] != (y == 0 ? 255 : 0) || rows[p + 3] != 255)
                    throw new ProbeFailure("PNG RGBA differs from six independent source pixels."); }
        }
        Array.Clear(rows);
    }
}
