using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using Bimwright.Ipt.Server;
using Bimwright.Ipt.Shared.Contracts;
using Inventor;
using Inventor.So.Mcp.Http;
using Inventor.So.Mcp.Http.Pairing;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using File = System.IO.File;
using Path = System.IO.Path;

internal static partial class Program
{
    // Prevent beforefieldinit from capturing Console.Out after suppression.
    static Program() { }
    private static Options73 Settings = null!;
    private static Inventor.Application? Application;
    private static int StaThread, OwnedPid;
    private static long OwnedStart;
    private static Dictionary<int, long> Existing = new();
    private static readonly List<OwnedDocument73> Documents = new();
    private static JObject Evidence = new();
    private static string Phase = "arguments", FixtureDirectory = "", TargetId = "";
    private static DateTimeOffset ExpiresUtc;
    private static string? Bearer;
    private static string CertificateSha = "";
    private static int Port;
    private static bool BootstrapWritten;
    private static readonly TextWriter SafeOutput = Console.Out;
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [STAThread]
    private static int Main(string[] args)
    {
        // All host/framework console output is suppressed. Only our fixed markers go to
        // the saved writer. Credentials and descriptor contents never reach a console.
        Console.SetOut(TextWriter.Null); Console.SetError(TextWriter.Null);
        WebApplication? host = null; X509Certificate2? certificate = null;
        int exit = 1; bool outputOwned = false;
        StaThread = System.Environment.CurrentManagedThreadId;
        try
        {
            Settings = Options73.Parse(args);
            CreatePrivateDirectory73(Settings.OutputDirectory); outputOwned = true;
            Evidence = new JObject { ["schema"] = 1, ["run_id"] = Settings.RunId, ["status"] = "preparing",
                ["started_utc"] = DateTimeOffset.UtcNow.ToString("O"), ["open_s1_gates"] = 18,
                ["quest_proof"] = false, ["physical_proof"] = false };
            CheckInstallation73("installation_before");
            Existing = Inventory73(); Evidence["existing_processes_before"] = ProcessJson73(Existing);
            Phase = "new_owned_inventor";
            // Inherited only by the freshly activated process. No persisted machine/user setting.
            System.Environment.SetEnvironmentVariable("INVENTOR_SO_EXPERIMENTAL", "1", System.EnvironmentVariableTarget.Process);
            DateTime started = DateTime.UtcNow;
            Application = (Inventor.Application)Activator.CreateInstance(Type.GetTypeFromProgID("Inventor.Application", true)!)!;
            GetWindowThreadProcessId(new IntPtr(unchecked((long)(uint)Application.MainFrameHWND)), out uint nativePid);
            if (nativePid == 0 || nativePid == 91056 || Existing.ContainsKey(checked((int)nativePid)))
                throw new HostFailure73("COM did not return a distinct owned process. No Documents access or Quit allowed.");
            int candidate = checked((int)nativePid);
            using (var process = Process.GetProcessById(candidate))
            {
                long startTicks = process.StartTime.ToUniversalTime().Ticks;
                if (startTicks < started.AddSeconds(-2).Ticks) throw new HostFailure73("Returned process predates owned launch.");
                OwnedPid = candidate; OwnedStart = startTicks;
            }
            Evidence["owned_pid"] = OwnedPid; Evidence["owned_start_utc_ticks"] = OwnedStart;
            GuardProcess73();
            if (Application.Documents.Count != 0 || Application.ActiveDocument != null)
                throw new HostFailure73("New owned process is not initially empty.");
            Evidence["owned_initial_empty"] = true; PersistOwnership73();

            Phase = "owned_addin_descriptor";
            WaitDescriptor73();
            Phase = "owned_loaded_addin";
            Evidence["loaded_addin"] = VerifyLoadedAddin73();
            FixtureDirectory = Path.Combine(Path.GetTempPath(), "s1-ownedquest73-" + Settings.RunId + "-" + Guid.NewGuid().ToString("N"));
            CreatePrivateDirectory73(FixtureDirectory); Evidence["fixture_directory"] = FixtureDirectory;
            CreateFixture73(false); CreateFixture73(true);
            GuardDocuments73(); Documents.Single(d => !d.Sheet).Document.Activate();
            Evidence["baseline"] = Inspect73(); PersistOwnership73();

            Phase = "https_host";
            var config = new InventorMcpConfig { DescriptorDirectory = Settings.DescriptorDirectory, TargetId = TargetId,
                HttpUrls = new List<string> { "https://" + Settings.Host + ":0" }, PairHost = Settings.Host,
                HttpSelfSignedCertificate = true, HttpSelfSignedPath = Path.Combine(FixtureDirectory, "host.pfx"),
                AssetDirectory = Path.Combine(FixtureDirectory, "assets"), AuditDirectory = Path.Combine(FixtureDirectory, "audit"),
                AuditEnabled = false, EnableExperimental = true, EnableSendCode = false, FullAccess = false };
            certificate = PairingSetup.ResolveCertificate(config) ?? throw new HostFailure73("Dedicated HTTPS certificate unavailable.");
            CertificateSha = SelfSignedCertificate.Sha256Hex(certificate);
            Bearer = TokenRegistry.Generate();
            var tokens = new TokenRegistry(); tokens.Add("s1-qa-" + Settings.RunId, Bearer);
            host = HttpHost.Build(Array.Empty<string>(), config, tokens, new Inventor.So.Mcp.Http.Pairing.HostOptions {
                Certificate = certificate, ConfigureServices = services => services.RemoveAll<ILoggerProvider>() });
            host.StartAsync().GetAwaiter().GetResult();
            var addresses = host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses;
            var bound = new Uri(addresses.Single());
            if (bound.Scheme != "https" || bound.Host != Settings.Host || bound.Port < 1)
                throw new HostFailure73("HTTPS bound address differs from the requested run host.");
            Port = bound.Port; ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(30);
            Evidence["status"] = "ready"; Evidence["host"] = Settings.Host; Evidence["port"] = Port;
            Evidence["cert_sha256"] = CertificateSha; Evidence["target_id"] = TargetId;
            Evidence["expires_utc"] = ExpiresUtc.ToString("O");
            Evidence["part_document_id"] = Documents.Single(d => !d.Sheet).DocumentId;
            WriteSupplementOracle83();
            Evidence["sheet_document_id"] = Documents.Single(d => d.Sheet).DocumentId;
            Evidence["client_name"] = "s1-qa-" + Settings.RunId;
            CheckInstallation73("installation_ready");
            WritePublic73("ready.json", Evidence); SafeOutput.WriteLine("READY"); SafeOutput.Flush();

            // This worker handles stdin only. Every COM call remains on this STA thread.
            using var commands = new BlockingCollection<string>(8);
            using var inputCancellation = new CancellationTokenSource();
            _ = Task.Run(() =>
            {
                try
                {
                    while (!inputCancellation.IsCancellationRequested)
                    {
                        string? line = Console.In.ReadLine();
                        commands.Add(line == null ? "__eof" : line.Length <= 4096 ? line.Trim() : "__invalid", inputCancellation.Token);
                        if (line == null) return;
                    }
                }
                catch (OperationCanceledException) { }
                catch { try { commands.TryAdd("__input_failure"); } catch { } }
            });
            int inspection = 0;
            while (true)
            {
                if (DateTimeOffset.UtcNow >= ExpiresUtc) { Evidence["shutdown_reason"] = "expired"; break; }
                if (!commands.TryTake(out string? command, 250)) continue;
                Phase = "stdin_command";
                if (command is "stop" or "__eof") { Evidence["shutdown_reason"] = command == "stop" ? "stop" : "stdin_eof"; break; }
                if (command == "bootstrap")
                {
                    WriteBootstrap73(); SafeOutput.WriteLine("BOOTSTRAP_WRITTEN"); SafeOutput.Flush();
                }
                else if (command == "inspect")
                {
                    var current = Inspect73(); CheckInstallation73("installation_inspect");
                    WritePublic73("inspect-" + (++inspection).ToString("D3") + ".json", current);
                    SafeOutput.WriteLine("INSPECTED"); SafeOutput.Flush();
                }
                else throw new HostFailure73("Unknown or failed stdin command.");
            }
            inputCancellation.Cancel(); exit = 0;
        }
        catch (Exception error)
        {
            Evidence["failure"] = Error73(error); Evidence["status"] = "failed";
            SafeOutput.WriteLine("ERROR"); SafeOutput.Flush();
        }
        finally
        {
            Phase = "stop_http";
            try
            {
                if (host != null)
                {
                    using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                    host.StopAsync(cancellation.Token).GetAwaiter().GetResult(); host.DisposeAsync().AsTask().GetAwaiter().GetResult();
                }
                Evidence["http_stopped"] = true;
            }
            catch (Exception error) { Evidence["http_stop_failure"] = Error73(error); exit = 1; }
            Bearer = null;
            try { if (outputOwned && BootstrapWritten) File.Delete(Path.Combine(Settings.OutputDirectory, "bootstrap.json")); }
            catch (Exception error) { Evidence["bootstrap_cleanup_failure"] = Error73(error); exit = 1; }
            Phase = "close_owned_documents";
            try { CloseOwned73(); }
            catch (Exception error) { Evidence["native_cleanup_failure"] = Error73(error); exit = 1; }
            try { certificate?.Dispose(); }
            catch (Exception error) { Evidence["certificate_cleanup_failure"] = Error73(error); exit = 1; }
            try { if (Application != null && Marshal.IsComObject(Application)) Marshal.ReleaseComObject(Application); }
            catch (Exception error) { Evidence["com_release_failure"] = Error73(error); exit = 1; }
            Application = null;
            bool gone = OwnedPid == 0;
            if (OwnedPid > 0)
            {
                try { using var process = Process.GetProcessById(OwnedPid); gone = process.HasExited || process.WaitForExit(30000); }
                catch (ArgumentException) { gone = true; }
                catch (Exception error) { Evidence["owned_exit_check_failure"] = Error73(error); exit = 1; }
                Evidence["owned_pid_exited"] = gone; if (!gone) exit = 1;
            }
            try
            {
                var after = Inventory73(); Evidence["existing_processes_after"] = ProcessJson73(after);
                bool preserved = Existing.All(pair => after.TryGetValue(pair.Key, out long start) && start == pair.Value);
                Evidence["existing_processes_preserved"] = preserved; if (!preserved) exit = 1;
            }
            catch (Exception error) { Evidence["inventory_after_failure"] = Error73(error); exit = 1; }
            if (outputOwned)
            {
                try { CheckInstallation73("installation_after"); }
                catch (Exception error) { Evidence["installation_failure"] = Error73(error); exit = 1; }
                Evidence["exit_code"] = exit; Evidence["status"] = exit == 0 ? "stopped" : "failed";
                try { WritePublic73("stopped.json", Evidence); } catch { exit = 1; }
            }
            // Fixture/certificate/assets directory is retained for the wrapper's guarded
            // inspection and cleanup. Only the bootstrap credential file is deleted here.
            SafeOutput.WriteLine(exit == 0 ? "STOPPED" : "STOP_FAILED"); SafeOutput.Flush();
        }
        return exit;
    }

    private static JObject Error73(Exception error)
    {
        var value = new JObject { ["phase"] = Phase, ["type"] = error.GetType().FullName, ["hresult"] = error.HResult };
        if (error is HostFailure73) value["assertion"] = error.Message;
        return value;
    }
}
