using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Experience;
using InventorXrSo.Core.Mcp;
using InventorXrSo.Core.Pairing;
using InventorXrSo.Core.Session;
using InventorXrSo.Core.Verify;
using InventorXrSo.Core.Ui;
using InventorXrSo.Unity.Scene;
using InventorXrSo.Unity.Ui;
using InventorXrSo.Xr;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Management;
using Debug = UnityEngine.Debug;
using Activity = InventorXrSo.Core.Experience.Activity;

namespace InventorXrSo.Editor
{
    /// <summary>Rendered Main scene on Meta XR; isolated HTTPS FakeAddIn or explicitly guarded native M7 fixture.</summary>
    [InitializeOnLoad]
    public static class XrSoSimulatorSmoke
    {
        internal const string ActiveKey = "xrso.simulator.smoke", OutputKey = "xrso.simulator.output", ErrorsKey = "xrso.simulator.errors";
        private static Process _host;
        private static Process _restartedNativeHost;
        private static bool _nativeHostStopped;
        private static readonly JObject Result = new JObject { ["input"] = "synthetic", ["cad"] = "HTTPS FakeAddIn", ["checks"] = new JArray() };
        private static string Output => SessionState.GetString(OutputKey, "");
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static string LiveCredentialsPath => Environment.GetEnvironmentVariable("XR_SO_SIMULATOR_LIVE_CREDENTIALS");
        private static bool Native => !string.IsNullOrEmpty(LiveCredentialsPath);
        private static bool LegacyRunner => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("XR_SO_SIMULATOR_RUNNER"));

        static XrSoSimulatorSmoke()
        {
            EditorApplication.playModeStateChanged += Changed;
            Application.logMessageReceived += CaptureRuntimeError;
            if (SessionState.GetBool(ActiveKey, false) && EditorApplication.isPlayingOrWillChangePlaymode)
                AppController.EditorCredentialStoreFactory = StartFixture;
        }

        private sealed class Credentials : ICredentialStore
        {
            private PairedServer _value;
            public Credentials(PairedServer value) { _value = value; }
            public PairedServer Load() => _value;
            public void Save(PairedServer value) => _value = value;
            public void Clear() => _value = null;
        }

        private static ICredentialStore StartFixture()
        {
            if (Native)
            {
                var json = File.ReadAllText(LiveCredentialsPath);
                // Preserve the bootstrap's exact round-trip expiry string. Automatic
                // date parsing would normalize fractional seconds before strict S1 validation.
                var settings = Newtonsoft.Json.JsonConvert.DeserializeObject<JObject>(json,
                    new Newtonsoft.Json.JsonSerializerSettings { DateParseHandling = Newtonsoft.Json.DateParseHandling.None });
                bool fixture = (string)settings["fixture_name"] == "XR_M7_Quest_Acceptance.iam";
                if (LegacyRunner)
                {
                    string milestone = (string)settings["fixture_milestone"];
                    fixture = new[] { "m1", "m2", "m3", "m4", "m5", "m6", "m7", "m9n", "m9f", "m9h", "s1" }.Contains(milestone)
                        && ((string)settings["fixture_name"])?.StartsWith("XR_" + milestone.ToUpperInvariant() + "_Quest_Acceptance", StringComparison.Ordinal) == true;
                }
                if (!fixture || string.IsNullOrEmpty((string)settings["document_id"]))
                    throw new InvalidOperationException("Only the dedicated native M7 fixture is accepted.");
                if (settings["s1_bootstrap"] is JObject s1)
                {
                    var bootstrap = S1AcceptanceBootstrap.Parse(System.Text.Encoding.UTF8.GetBytes(s1.ToString()), (string)s1["run_id"], DateTimeOffset.UtcNow);
                    var app = UnityEngine.Object.FindAnyObjectByType<AppController>();
                    typeof(AppController).GetProperty("AcceptanceBootstrap", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(app, bootstrap.Metadata);
                    return new Credentials(bootstrap.CreatePairedServer(DateTimeOffset.UtcNow));
                }
                return new Credentials(PairedServer.FromJson(json));
            }
            string dll = Path.GetFullPath("Tests~/XrSo.TestHost/bin/Debug/net8.0/XrSo.TestHost.dll");
            if (!File.Exists(dll)) throw new FileNotFoundException("Build XrSo.TestHost first.", dll);
            var info = new ProcessStartInfo("dotnet", "\"" + dll + "\" --state \"" + Path.Combine(Output, "testhost") + "\"")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
            _host = Process.Start(info);
            _host.ErrorDataReceived += (_, __) => { }; _host.BeginErrorReadLine();
            // The host emits one readiness line. Never echo bearer tokens or write them to the real pairing store.
            var line = _host.StandardOutput.ReadLineAsync();
            if (!line.Wait(TimeSpan.FromSeconds(30)))
            {
                _host.Kill();
                throw new TimeoutException("HTTPS fixture readiness");
            }
            var ready = JObject.Parse(line.Result ?? throw new InvalidOperationException("Test host did not start."));

            var uri = new Uri((string)ready["base_url"]);
            return new Credentials(new PairedServer(uri.Host, uri.Port, (string)ready["cert_sha256"], "simulator", (string)ready["editor_token"]));
        }

        private static void Changed(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(ActiveKey, false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode) EditorApplication.delayCall += Run;
            if (state == PlayModeStateChange.EnteredEditMode)
            {
                SessionState.SetBool(ActiveKey, false);
                var errors = JArray.Parse(SessionState.GetString(ErrorsKey, "[]"));
                if (errors.Count > 0 && File.Exists(Path.Combine(Output, "result.json")))
                {
                    var result = JObject.Parse(File.ReadAllText(Path.Combine(Output, "result.json")));
                    result["runtime_errors"] = errors; result["status"] = "FAIL_RUNTIME_ERRORS";
                    File.WriteAllText(Path.Combine(Output, "result.json"), result.ToString());
                    File.Delete(Path.Combine(Output, "pass.marker"));
                }
                EditorApplication.Exit(File.Exists(Path.Combine(Output, "pass.marker")) ? 0 : 1);
            }
        }

        private static void CaptureRuntimeError(string message, string stack, LogType type)
        {
            if (!SessionState.GetBool(ActiveKey, false) || (type != LogType.Exception && type != LogType.Error)
                || !stack.Contains("InventorXrSo")) return;
            var errors = JArray.Parse(SessionState.GetString(ErrorsKey, "[]"));
            errors.Add(message + "\n" + stack); SessionState.SetString(ErrorsKey, errors.ToString());
        }

        private static T Read<T>(object owner, string field) => (T)owner.GetType().GetField(field, Private).GetValue(owner);
        private static void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException(name);
            ((JArray)Result["checks"]).Add("PASS " + name);
            Debug.Log("[XrSimulator] PASS synthetic: " + name);
        }
        private static async Task Until(Func<bool> ready, string name, int seconds = 45)
        {
            var until = DateTime.UtcNow.AddSeconds(seconds);
            while (!ready()) { if (DateTime.UtcNow >= until) throw new TimeoutException(name); await Task.Delay(100); }
        }
        private static async Task Screenshot(string name)
        {
            string path = Path.Combine(Output, name + ".png");
            ScreenCapture.CaptureScreenshot(path);
            await Until(() => File.Exists(path), "screenshot " + name, 10);
        }

        private static async void Run()
        {
            Result["cad"] = Native ? "HTTPS Inventor 2027 / native M7 fixture" : "HTTPS FakeAddIn";
            try
            {
                await Until(() => XRSettings.isDeviceActive, "OpenXR runtime connection", 60);
                var loader = XRGeneralSettings.Instance.Manager.activeLoader;
                Check(loader != null && loader.GetType().Name == "OpenXRLoader", "desktop OpenXR loader running");
                await Until(() => InputDevices.GetDeviceAtXRNode(XRNode.Head).isValid, "headset input device registration", 30);
                var headset = InputDevices.GetDeviceAtXRNode(XRNode.Head);
                Result["headset"] = headset.name; Result["runtime_manifest"] = Environment.GetEnvironmentVariable("XR_RUNTIME_JSON");
                Check(headset.isValid, "simulated headset connected");
                if (LegacyRunner)
                {
                    string runnerFile = Path.Combine(Output, "runner-result.json");
                    await Until(() => { HandleNetworkControl(); return File.Exists(runnerFile); }, "legacy acceptance runner", 1500);
                    var runner = JObject.Parse(File.ReadAllText(runnerFile));
                    Result["runner_result"] = runner;
                    Result["not_covered"] = runner["not_covered"].DeepClone();
                    Check((bool?)runner["success"] == true, (string)runner["verdict"]);
                    await Screenshot("runner-final");
                    Result["status"] = "PASS_SYNTHETIC_SIMULATOR_RUNNER";
                    File.WriteAllText(Path.Combine(Output, "pass.marker"), "synthetic simulator runner only");
                    return;
                }
                var app = UnityEngine.Object.FindFirstObjectByType<AppController>();
                await Until(() => Read<SessionController>(app, "_session")?.Status == SessionStatus.Online, "HTTPS fixture scene");
                var home = Read<HomePanel>(app, "_home");
                var catalog = Read<ActionCatalog>(app, "_catalog");
                var inspect = Read<InspectWorkspace>(app, "_inspect");
                var backend = Read<InventorBackend>(app, "_backend");
                var view = Read<CadSceneView>(app, "sceneView");
                var before = await backend.GetDocumentStateAsync(CancellationToken.None);
                Result["cad"] = Native ? "HTTPS Inventor 2027 / native M7 fixture" : "HTTPS FakeAddIn";
                Result["document_id"] = before.DocumentId; Result["revision_before"] = before.Revision;
                if (Native)
                {
                    var settings = JObject.Parse(File.ReadAllText(LiveCredentialsPath));
                    Check(before.DocumentId == (string)settings["document_id"] && view.Instances.Count == 4,
                        "native fixture identity and four occurrences");
                }
                Check(app.CurrentActivity == Activity.Home, "connection preserves Home");
                foreach (var label in new[] { UiText.RealityCapture, UiText.DesignActivity, UiText.PresentationActivity, UiText.VerificationActivity })
                    Check(home.VoiceActions.Any(a => a.label == label), "Home card " + label);
                await Task.Delay(1000); await Screenshot("01-home");

                Check(home.InvokeVoiceAction(UiText.PresentationActivity), "Presentation UI callback");
                Check(app.CurrentActivity == Activity.Presentation && !app.GetComponent<AssemblyWorkspace>().Active && !app.GetComponent<DesignWorkspace>().Active,
                    "Presentation excludes authoring");
                foreach (string id in new[] { "doc.save", "assembly.move", "commit.apply", InspectWorkspace.IdSection, InspectWorkspace.IdHealth })
                    Check(!catalog.TryInvoke(id), "Presentation blocks " + id);
                try { await backend.PreviewDesignAsync(before, new JArray(), CancellationToken.None); throw new InvalidOperationException("CAD preview escaped policy"); }
                catch (McpException error) { Check(error.Code == "ACTIVITY_FORBIDDEN", "direct CAD preview blocked before HTTP"); }
                Check(catalog.TryInvoke(InspectWorkspace.IdMeasure), "local measurement starts");
                var pick = typeof(InspectWorkspace).GetMethod("OnPointPicked", Private);
                pick.Invoke(inspect, new object[] { view.transform.TransformPoint(Vector3.zero) });
                pick.Invoke(inspect, new object[] { view.transform.TransformPoint(Vector3.right * 0.02f) });
                var measurement = Read<MeasurementView>(inspect, "_measure");
                Check(measurement.DistanceMm.HasValue && Math.Abs(measurement.DistanceMm.Value - 20) < 0.01, "synthetic two-point measure is 20 CAD mm");
                Check(catalog.TryInvoke(InspectWorkspace.IdMeasurePin), "measurement pinned");
                Check(catalog.TryInvoke(InspectWorkspace.IdScale) && catalog.TryInvoke(InspectWorkspace.IdScaleTable), "table scale picker");
                await Task.Delay(500); await Screenshot("02-presentation");
                var oldAction = catalog.Find(InspectWorkspace.IdMeasure);
                Check(catalog.TryInvoke(DocumentActions.IdExit), "Home keeps connection");
                Check(home.InvokeVoiceAction(UiText.VerificationActivity), "Verification UI callback");
                Check(!oldAction.TryInvoke(), "late Presentation callback rejected");
                Check(catalog.TryInvoke(InspectWorkspace.IdSection), "Verification section enabled");
                Check(catalog.TryInvoke(InspectWorkspace.IdBrowse), "Verification browser opens");
                await Task.Delay(500); await Screenshot("03-verification");
                if (Native) await VerifyNative(inspect, catalog);
                Check(catalog.TryInvoke(DocumentActions.IdExit), "Verification returns Home");
                Check(home.InvokeVoiceAction(UiText.DesignActivity), "Design UI callback");
                await Until(() => app.GetComponent<AssemblyWorkspace>().Active, "assembly authoring in Design");
                await Task.Delay(500); await Screenshot("04-design");
                Check(catalog.TryInvoke(DocumentActions.IdExit), "idle Design returns Home");
                Check(home.InvokeVoiceAction(UiText.RealityCapture), "Capture UI callback");
                var capture = app.GetComponent<RealityCaptureWorkspace>();
                Check(capture.Active && !view.gameObject.activeSelf, "independent Capture hides CAD");
                await Task.Delay(500); await Screenshot("05-capture");
                Check(capture.Panel.InvokeVoiceAction(UiText.ReturnHome), "Capture returns Home without recording");
                var after = await backend.GetDocumentStateAsync(CancellationToken.None);
                Check(before.DocumentId == after.DocumentId && before.Revision == after.Revision, "fixture CAD revision unchanged");
                Result["revision_after"] = after.Revision;
                Result["status"] = Native ? "PASS_SYNTHETIC_META_XR_SIMULATOR_NATIVE_CAD" : "PASS_SYNTHETIC_META_XR_SIMULATOR";
                File.WriteAllText(Path.Combine(Output, "pass.marker"), "synthetic simulator only");
            }
            catch (Exception error) { Result["status"] = "FAIL"; Result["error"] = error.ToString(); Debug.LogException(error); }
            finally
            {
                if (Result["not_covered"] == null) Result["not_covered"] = Native
                    ? new JArray("native CAD authoring/commit", "Android/Keystore", "camera acquisition", "physical input/ergonomics", "Quest GPU performance")
                    : new JArray("native Inventor COM", "Android/Keystore", "camera acquisition", "physical input/ergonomics", "Quest GPU performance");
                File.WriteAllText(Path.Combine(Output, "result.json"), Result.ToString());
                AppController.EditorCredentialStoreFactory = null;
                var app = UnityEngine.Object.FindFirstObjectByType<AppController>();
                if (app != null) typeof(AppController).GetMethod("StopSession", Private).Invoke(app, null);
                if (_host != null) { _host.StandardInput.Close(); _host.Dispose(); _host = null; }
                if (_restartedNativeHost != null) { if (!_restartedNativeHost.HasExited) _restartedNativeHost.Kill(); _restartedNativeHost.Dispose(); _restartedNativeHost = null; }
                EditorApplication.ExitPlaymode();
            }
        }

        private static void HandleNetworkControl()
        {
            string down = Path.Combine(Output, "network-down.request"), restart = Path.Combine(Output, "network-restart.request");
            if (!Native || (!File.Exists(down) && !File.Exists(restart))) return;
            var settings = JObject.Parse(File.ReadAllText(LiveCredentialsPath));
            if (!_nativeHostStopped && File.Exists(down))
            {
                using (var owned = Process.GetProcessById((int)settings["host_process_id"]))
                {
                    if (owned.StartTime.ToUniversalTime().Ticks != (long)settings["host_start_ticks"])
                        throw new InvalidOperationException("Native host identity changed; refusing to interrupt it.");
                    owned.Kill(); owned.WaitForExit(3000);
                }
                _nativeHostStopped = true; File.Delete(down);
                File.WriteAllText(Path.Combine(Output, "network-down.marker"), "owned HTTPS fixture host stopped");
            }
            if (_nativeHostStopped && _restartedNativeHost == null && File.Exists(restart))
            {
                string Quote(string value) => "\"" + value + "\"";
                string args = Quote((string)settings["host_dll"]) + " --config " + Quote((string)settings["host_config"])
                    + " --http-urls https://127.0.0.1:" + (int)settings["port"] + " --http-cert " + Quote((string)settings["host_certificate"])
                    + " --http-token-file " + Quote((string)settings["host_tokens"]) + " --target 2027 --enable-experimental";
                _restartedNativeHost = Process.Start(new ProcessStartInfo("dotnet", args)
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true });
                _restartedNativeHost.OutputDataReceived += (_, __) => { }; _restartedNativeHost.BeginOutputReadLine();
                _restartedNativeHost.ErrorDataReceived += (_, __) => { }; _restartedNativeHost.BeginErrorReadLine();
                File.Delete(restart); File.WriteAllText(Path.Combine(Output, "network-restart.marker"), "owned host restarted with same certificate/token");
            }
        }

        private static async Task SelectNative(InspectWorkspace inspect, ActionCatalog catalog, string name)
        {
            await Until(() => !Read<bool>(inspect, "_busy") && catalog.Find(InspectWorkspace.IdBrowse)?.Enabled == true, "browser ready");
            Check(catalog.TryInvoke(InspectWorkspace.IdBrowse), "native browser opens for " + name);
            var pick = inspect.Actions.FirstOrDefault(a => a.Id.StartsWith(InspectWorkspace.IdPickPrefix, StringComparison.Ordinal) && a.Label == name);
            Check(pick != null && pick.TryInvoke(), "native browser selects " + name);
            await Until(() => inspect.SelectedNodeName == name && !Read<bool>(inspect, "_busy"), "native selection " + name);
        }

        private static async Task VerifyNative(InspectWorkspace inspect, ActionCatalog catalog)
        {
            var session = Read<VerifySession>(inspect, "_verifySession");
            Check(catalog.TryInvoke(InspectWorkspace.IdInterference), "native interference UI callback");
            await Until(() => session.Interference.Status != VerifyStatus.Running, "native interference", 90);
            Check(session.Interference.Status == VerifyStatus.Done, "native interference completed: " + session.Interference.ErrorCode);
            var interference = session.Interference.Result;
            Check(interference.Count == 1 && Math.Abs(interference.TotalVolumeMm3 - 2000) < 0.1, "native interference is one pair / 2000 mm3");
            Check(interference.Pairs[0].AName == "M7_A" && interference.Pairs[0].BName == "M7_B", "native interference identifies A/B");
            Result["interference_mm3"] = interference.TotalVolumeMm3;
            await Task.Delay(200); await Screenshot("06-native-interference");
            inspect.Back();
            await SelectNative(inspect, catalog, "M7_A");
            Check(catalog.TryInvoke(InspectWorkspace.IdDistance), "native distance first selection");
            await SelectNative(inspect, catalog, "M7_C");
            Check(catalog.TryInvoke(InspectWorkspace.IdDistance), "native distance second selection");
            await Until(() => session.Distance.Status != VerifyStatus.Running, "native distance", 90);
            Check(session.Distance.Status == VerifyStatus.Done && Math.Abs(session.Distance.Result.DistanceMm - 30) < 0.01,
                "native minimum distance is 30 mm");
            Result["distance_mm"] = session.Distance.Result.DistanceMm;
            Check(session.Distance.Result.HasPoints, "native distance includes Inventor endpoints");
            await Screenshot("07-native-distance");
            Check(catalog.TryInvoke(InspectWorkspace.IdHealth), "native health UI callback");
            await Until(() => session.Health.Status != VerifyStatus.Running, "native health", 90);
            Check(session.Health.Status == VerifyStatus.Done, "native health completed: " + session.Health.ErrorCode);
            var health = session.Health.Result;
            Check(health.Issues.Any(i => i.Name == "M7_Sick"), "native health reports M7_Sick");
            Check(health.Unconstrained.Count == 1 && health.Unconstrained[0].Name == "M7_D", "native health reports only D unconstrained");
            Check(health.BomIssues.Any(i => i.Code == "DESCRIPTION_MISSING"), "native BOM reports missing description");
            await Screenshot("08-native-health");
        }
    }
}
