using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using InventorXrSo.Core.Backend;
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

namespace InventorXrSo.Editor
{
    /// <summary>Rendered Main scene on Meta XR; isolated HTTPS FakeAddIn or explicitly guarded native M7 fixture.</summary>
    [InitializeOnLoad]
    public static class XrSoSimulatorSmoke
    {
        internal const string ActiveKey = "xrso.simulator.smoke", OutputKey = "xrso.simulator.output", ErrorsKey = "xrso.simulator.errors";
        private static Process _host;
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
            Result["cad"] = Native ? "HTTPS Inventor 2027 / dedicated fixture" : "HTTPS FakeAddIn";
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
                    await Until(() => File.Exists(runnerFile), "legacy acceptance runner", 1500);
                    var runner = JObject.Parse(File.ReadAllText(runnerFile));
                    Result["runner_result"] = runner;
                    Result["not_covered"] = runner["not_covered"].DeepClone();
                    Check((bool?)runner["success"] == true, (string)runner["verdict"]);
                    await Screenshot("runner-final");
                    Result["status"] = "PASS_SYNTHETIC_SIMULATOR_RUNNER";
                    File.WriteAllText(Path.Combine(Output, "pass.marker"), "synthetic simulator runner only");
                    return;
                }
                var app = UnityEngine.Object.FindAnyObjectByType<AppController>();
                await Until(() => Read<SessionController>(app, "_session")?.Status == SessionStatus.Online, "HTTPS fixture scene");
                var home = Read<HomePanel>(app, "_home");
                await Screenshot("01-home");
                Check(home.InvokeVoiceAction(UiText.RealityCapture), "Home opens independent Capture");
                var capture = app.GetComponent<RealityCaptureWorkspace>();
                Check(capture.Active && !Read<CadSceneView>(app, "sceneView").gameObject.activeSelf, "Capture hides CAD");
                await Screenshot("02-capture");
                if (!Native)
                {
                    var session = Read<SessionController>(app, "_session");
                    _host.StandardInput.Close();
                    await Until(() => _host.HasExited, "owned HTTPS fixture shutdown");
                    _host.Dispose(); _host = null;
                    await Until(() => session.Status != SessionStatus.Online, "Capture detects lost HTTPS host", 60);
                    Check(capture.Active && !home.gameObject.activeSelf, "Capture remains open after real HTTPS fixture loss");
                    Check(!Read<CadSceneView>(app, "sceneView").gameObject.activeSelf, "offline Capture keeps CAD hidden");
                    Result["network_failure"] = "owned HTTPS FakeAddIn stopped; no reconnect certified";
                    await Screenshot("03-capture-offline");
                }
                Check(capture.Panel.InvokeVoiceAction(UiText.CaptureNewObject), "Object volume setup");
                Check(!capture.Panel.InvokeVoiceAction(UiText.CaptureStart), "Camera cannot start without volume confirmation");
                Check(capture.Panel.InvokeVoiceAction(UiText.Cancel), "Cancel before camera access");
                Check(capture.Panel.InvokeVoiceAction(UiText.ReturnHome), "Capture returns Home");
                Result["status"] = "PASS_SYNTHETIC_META_XR_SIMULATOR";
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
                var app = UnityEngine.Object.FindAnyObjectByType<AppController>();
                if (app != null) typeof(AppController).GetMethod("StopSession", Private).Invoke(app, null);
                if (_host != null) { _host.StandardInput.Close(); _host.Dispose(); _host = null; }
                EditorApplication.ExitPlaymode();
            }
        }

    }
}
