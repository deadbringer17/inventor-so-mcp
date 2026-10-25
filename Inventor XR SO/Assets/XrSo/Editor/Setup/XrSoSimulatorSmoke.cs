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
    /// <summary>Real Meta XR runtime and rendered Main scene; CAD is explicitly the HTTPS FakeAddIn fixture.</summary>
    [InitializeOnLoad]
    public static class XrSoSimulatorSmoke
    {
        internal const string ActiveKey = "xrso.simulator.smoke", OutputKey = "xrso.simulator.output", ErrorsKey = "xrso.simulator.errors";
        private static Process _host;
        private static readonly JObject Result = new JObject { ["input"] = "synthetic", ["cad"] = "HTTPS FakeAddIn", ["checks"] = new JArray() };
        private static string Output => SessionState.GetString(OutputKey, "");
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

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
            try
            {
                await Until(() => XRSettings.isDeviceActive, "OpenXR runtime connection", 60);
                var loader = XRGeneralSettings.Instance.Manager.activeLoader;
                Check(loader != null && loader.GetType().Name == "OpenXRLoader", "desktop OpenXR loader running");
                await Until(() => InputDevices.GetDeviceAtXRNode(XRNode.Head).isValid, "headset input device registration", 30);
                var headset = InputDevices.GetDeviceAtXRNode(XRNode.Head);
                Result["headset"] = headset.name; Result["runtime_manifest"] = Environment.GetEnvironmentVariable("XR_RUNTIME_JSON");
                Check(headset.isValid, "simulated headset connected");
                var app = UnityEngine.Object.FindFirstObjectByType<AppController>();
                await Until(() => Read<SessionController>(app, "_session")?.Status == SessionStatus.Online, "HTTPS fixture scene");
                var home = Read<HomePanel>(app, "_home");
                var catalog = Read<ActionCatalog>(app, "_catalog");
                var inspect = Read<InspectWorkspace>(app, "_inspect");
                var backend = Read<InventorBackend>(app, "_backend");
                var view = Read<CadSceneView>(app, "sceneView");
                var before = await backend.GetDocumentStateAsync(CancellationToken.None);
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
                Result["status"] = "PASS_SYNTHETIC_META_XR_SIMULATOR";
                File.WriteAllText(Path.Combine(Output, "pass.marker"), "synthetic simulator only");
            }
            catch (Exception error) { Result["status"] = "FAIL"; Result["error"] = error.ToString(); Debug.LogException(error); }
            finally
            {
                Result["not_covered"] = new JArray("native Inventor COM", "Android/Keystore", "camera acquisition", "physical input/ergonomics", "Quest GPU performance");
                File.WriteAllText(Path.Combine(Output, "result.json"), Result.ToString());
                AppController.EditorCredentialStoreFactory = null;
                var app = UnityEngine.Object.FindFirstObjectByType<AppController>();
                if (app != null) typeof(AppController).GetMethod("StopSession", Private).Invoke(app, null);
                if (_host != null) { _host.StandardInput.Close(); _host.Dispose(); _host = null; }
                EditorApplication.ExitPlaymode();
            }
        }
    }
}
