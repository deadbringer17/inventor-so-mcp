#if XR_SO_ACCEPTANCE
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Mcp;
using InventorXrSo.Core.Session;
using InventorXrSo.Unity.Scene;
using UnityEngine;

namespace InventorXrSo.Xr
{
    /// <summary>Opt-in, fixture-scoped M4 acceptance runner for a dedicated Quest build.</summary>
    internal sealed class M4QuestAcceptance : MonoBehaviour
    {
        private const string IntentExtra = "xr_m4_acceptance";
        private const string FixturePrefix = "XR_M4_Quest_Acceptance";
        private const int TimeoutSeconds = 120;
        private string _logPath;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AfterSceneLoad()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var intent = activity.Call<AndroidJavaObject>("getIntent"))
                {
                    if (!intent.Call<bool>("getBooleanExtra", IntentExtra, false)) return;
                }
                var host = new GameObject("M4 Quest Acceptance Runner");
                DontDestroyOnLoad(host);
                host.AddComponent<M4QuestAcceptance>();
            }
            catch (Exception ex)
            {
                Debug.LogError("[M4Quest] Could not read acceptance intent: " + ex.GetType().Name + ": " + ex.Message);
            }
#endif
        }

        private async void Start()
        {
            _logPath = Path.Combine(Application.persistentDataPath, "m4-acceptance.txt");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(TimeoutSeconds));
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_logPath));
                File.WriteAllText(_logPath, string.Empty);
                Record("START; waiting for online Inventor fixture");
                var app = await WaitFor(() => FindObjectOfType<AppController>(), timeout.Token);
                var appSession = await WaitFor(() => Read<SessionController>(app, "_session"), timeout.Token);
                var fixture = await WaitFor(() => appSession?.Status == SessionStatus.Online
                    && appSession.Scene?.Graph?.Root?.Name?.StartsWith(FixturePrefix, StringComparison.Ordinal) == true
                    ? appSession.Scene : null, timeout.Token);
                Check(fixture.Graph.Root.Name.StartsWith(FixturePrefix, StringComparison.Ordinal),
                    "connected document is the dedicated M4 acceptance fixture");
                Record("PASS; dedicated fixture loaded: " + fixture.Graph.Root.Name);

                typeof(AppController).GetMethod("EnterSession", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(app, new object[] { EnvironmentMode.StudioVr });
                var workspace = Read<AssemblyWorkspace>(app, "_assembly");
                workspace.Open();
                var backend = Read<IAssemblyWorkspaceBackend>(workspace, "_backend");
                var view = Read<CadSceneView>(workspace, "_view");
                var designSession = Read<DesignSession>(workspace, "_session");
                var previewView = Read<DesignPreviewView>(workspace, "_preview");
                Check(view != null && previewView != null && view.GetComponents<DesignPreviewView>().Contains(previewView),
                    "runner uses the workspace CAD view and its live preview renderer");
                await WaitFor(() => !ReadBoolean(workspace, "_busy")
                    ? Read<AssemblyContext>(workspace, "_context") : null, timeout.Token);
                Check(workspace.Active && Read<AssemblyContext>(workspace, "_context") != null,
                    "Assembly workspace opened and loaded context");

                var context = Read<AssemblyContext>(workspace, "_context");
                var occurrence = context.Occurrences.FirstOrDefault(item => item.CanMove);
                Check(occurrence != null, "fixture has an occurrence with complete movable DOF");
                await workspace.SelectOccurrenceAsync(occurrence.Id);
                await WaitFor(() => !ReadBoolean(workspace, "_busy")
                    ? Read<AssemblyOccurrence>(workspace, "_occurrence") : null, timeout.Token);
                occurrence = Read<AssemblyOccurrence>(workspace, "_occurrence");
                Check(occurrence != null && occurrence.CanMove, "selected fixture occurrence remains movable");

                var initialState = Read<DocumentState>(workspace, "_state");
                var initialContext = await backend.GetAssemblyContextAsync(initialState, occurrence.Id, timeout.Token);
                var initialOccurrence = initialContext.Occurrences.Single(item => item.Id == occurrence.Id);
                Check(initialOccurrence.Center.HasValue, "movable occurrence has a native rotation center");
                var originalCenter = initialOccurrence.Center.Value;
                Record("Fixture occurrence selected; initial center X=" + originalCenter.X.ToString("0.###"));

                var grounded = context.Occurrences.Single(item => item.Grounded);
                var groundedContext = await backend.GetAssemblyContextAsync(initialState, grounded.Id, timeout.Token);
                var movingContext = await backend.GetAssemblyContextAsync(initialState, occurrence.Id, timeout.Token);
                var referenceA = groundedContext.References.FirstOrDefault(item => item.Kind == "face" && item.FaceOrdinal == 3 && item.Available)
                    ?? groundedContext.References.First(item => item.Kind == "face" && item.Available);
                var referenceB = movingContext.References.FirstOrDefault(item => item.Kind == "face" && item.FaceOrdinal == 2 && item.Available)
                    ?? movingContext.References.First(item => item.Kind == "face" && item.Available);
                workspace.ChooseReference(referenceA);
                workspace.ChooseReference(referenceB);
                Check(Read<AssemblyReference>(workspace, "_a")?.Id == referenceA.Id
                    && Read<AssemblyReference>(workspace, "_b")?.Id == referenceB.Id
                    && Read<string>(workspace, "_screen") == "compatible",
                    "A/B references on different occurrences open the compatible-command page");
                var compatible = AssemblyOperations.CompatibleConstraints(referenceA, referenceB);
                Record("PASS; A/B selected on " + grounded.Name + "/" + occurrence.Name
                    + "; geometry=" + referenceA.Geometry + "/" + referenceB.Geometry
                    + "; compatible constraints=" + string.Join(",", compatible));
                try
                {
                    await backend.PreviewDesignAsync(initialState,
                        new Newtonsoft.Json.Linq.JArray(AssemblyOperations.Joint("planar", referenceA, referenceB)), timeout.Token);
                    Record("PLANAR DIAGNOSTIC; preview succeeded for physical A/B face ordinals");
                }
                catch (Exception ex)
                {
                    Record("PLANAR DIAGNOSTIC; preview rejected: " + ex.GetType().Name + ": " + ex.Message);
                }
                var afterPlanar = await backend.GetDocumentStateAsync(timeout.Token);
                Check(afterPlanar.DocumentId == initialState.DocumentId && afterPlanar.Revision == initialState.Revision,
                    "Planar preview leaves Inventor revision unchanged");

                workspace.BeginMove();
                Set(workspace, "_translation", new CadPoint(10, 0, 0));
                await workspace.PreviewAsync();
                Check(designSession.CanApply && previewView.IsShowing,
                    "10 mm move preview is rendered and enabled for Apply");
                var afterPreview = await backend.GetDocumentStateAsync(timeout.Token);
                Check(afterPreview.DocumentId == initialState.DocumentId && afterPreview.Revision == initialState.Revision,
                    "preview leaves the fixture revision unchanged");
                await CapturePreviewScreenshot(timeout.Token);
                Record("PASS; rendered 10 mm preview; revision unchanged");

                typeof(AssemblyWorkspace).GetMethod("Cancel", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(workspace, null);
                Check(!designSession.CanApply && designSession.Preview == null && !previewView.IsShowing,
                    "Cancel discards the preview and clears its rendering");
                Record("PASS; cancel clears the preview");

                await WaitFor(() => !ReadBoolean(workspace, "_busy")
                    ? Read<AssemblyContext>(workspace, "_context") : null, timeout.Token);
                context = Read<AssemblyContext>(workspace, "_context");
                occurrence = context.Occurrences.FirstOrDefault(item => item.Id == occurrence.Id && item.CanMove)
                    ?? context.Occurrences.FirstOrDefault(item => item.CanMove);
                Check(occurrence != null, "movable fixture occurrence reloads after cancel");
                await workspace.SelectOccurrenceAsync(occurrence.Id);
                await WaitFor(() => !ReadBoolean(workspace, "_busy")
                    ? Read<AssemblyOccurrence>(workspace, "_occurrence") : null, timeout.Token);
                workspace.BeginMove();
                Set(workspace, "_translation", new CadPoint(10, 0, 0));
                await workspace.PreviewAsync();
                Check(designSession.CanApply && previewView.IsShowing,
                    "second rendered move preview is ready for Apply");
                await workspace.ApplyAsync();
                Check(!designSession.CanApply && !previewView.IsShowing, "Apply consumes and clears the preview");

                var appState = await WaitFor(async () =>
                {
                    var state = await backend.GetDocumentStateAsync(timeout.Token);
                    return state.DocumentId == initialState.DocumentId && state.Revision != initialState.Revision ? state : null;
                }, timeout.Token);
                var committedContext = await backend.GetAssemblyContextAsync(appState, occurrence.Id, timeout.Token);
                var committedOccurrence = committedContext.Occurrences.Single(item => item.Id == occurrence.Id);
                Check(committedOccurrence.Center.HasValue
                    && Math.Abs(committedOccurrence.Center.Value.X - originalCenter.X - 10) < 0.02,
                    "committed move advances the occurrence center by 10 mm");
                Record("PASS; Apply changed revision and center X to " + committedOccurrence.Center.Value.X.ToString("0.###"));

                var historyBackend = (IDesignHistoryBackend)backend;
                var undoReceipt = await historyBackend.GetHistoryAsync(appState, timeout.Token);
                Check(undoReceipt.CanUndo, "XR history offers Undo after Apply");

                // Keep a valid plan at the committed revision; Undo must make its commit stale.
                var stalePreview = await backend.PreviewDesignAsync(appState,
                    new Newtonsoft.Json.Linq.JArray(AssemblyOperations.Move(occurrence.Id, new CadPoint(10, 0, 0))), timeout.Token);
                var undoneState = await historyBackend.ApplyHistoryAsync(undoReceipt, false, timeout.Token);
                var undoneContext = await backend.GetAssemblyContextAsync(undoneState, occurrence.Id, timeout.Token);
                var undoneOccurrence = undoneContext.Occurrences.Single(item => item.Id == occurrence.Id);
                Check(undoneOccurrence.Center.HasValue
                    && Math.Abs(undoneOccurrence.Center.Value.X - originalCenter.X) < 0.02,
                    "XR Undo restores the original occurrence center");
                var redoReceipt = await historyBackend.GetHistoryAsync(undoneState, timeout.Token);
                Check(redoReceipt.CanRedo, "XR history offers Redo after Undo");
                bool staleRejected = false;
                try { await backend.CommitDesignAsync(stalePreview, timeout.Token); }
                catch (McpToolException ex) when (ex.Code == "STALE_REVISION") { staleRejected = true; }
                Check(staleRejected, "commit of a pre-Undo preview is rejected as stale");
                var redoneState = await historyBackend.ApplyHistoryAsync(redoReceipt, true, timeout.Token);
                var redoneContext = await backend.GetAssemblyContextAsync(redoneState, occurrence.Id, timeout.Token);
                var redoneOccurrence = redoneContext.Occurrences.Single(item => item.Id == occurrence.Id);
                Check(redoneOccurrence.Center.HasValue
                    && Math.Abs(redoneOccurrence.Center.Value.X - committedOccurrence.Center.Value.X) < 0.02,
                    "XR Redo restores the committed occurrence center");
                Record("PASS; Undo/Redo restore original and committed centers; stale preview rejected");

                await WaitFor(async () =>
                {
                    var state = await backend.GetDocumentStateAsync(timeout.Token);
                    return state.DocumentId == redoneState.DocumentId && state.Revision == redoneState.Revision ? state : null;
                }, timeout.Token);
                await WaitFor(() => appSession.Scene?.Graph?.State?.Revision == redoneState.Revision
                    ? appSession.Scene : null, timeout.Token);
                workspace.Close();
                Check(!workspace.Active, "Assembly workspace closes");
                workspace.Open();
                var reopened = await WaitFor(() => Read<AssemblyContext>(workspace, "_context"), timeout.Token);
                Check(reopened.Occurrences.Any(item => item.CanMove), "reopened Assembly context exposes movable DOF");
                Record("PASS; reopened Assembly context and DOF");
                Record("PASS COMPLETE; physical controller input was not exercised by this runner");
            }
            catch (Exception ex)
            {
                Record("FAIL; " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private async Task CapturePreviewScreenshot(CancellationToken ct)
        {
            var directory = Application.persistentDataPath;
            Directory.CreateDirectory(directory);
            const string fileName = "m4-acceptance-preview.png";
            var path = Path.Combine(directory, fileName);
            if (File.Exists(path)) File.Delete(path);
            // Android resolves screenshot names under persistentDataPath itself.
            ScreenCapture.CaptureScreenshot(fileName);
            await WaitFor(() => File.Exists(path) && new FileInfo(path).Length > 0 ? path : null, ct);
            Record("Screenshot requested: " + path);
        }

        private void Record(string message)
        {
            var line = DateTime.UtcNow.ToString("O") + " [M4Quest] " + message;
            Debug.Log(line);
            if (string.IsNullOrEmpty(_logPath)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(_logPath));
            File.AppendAllText(_logPath, line + Environment.NewLine);
        }

        private static T Read<T>(object target, string name) where T : class
        {
            var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(target.GetType().FullName, name);
            return field.GetValue(target) as T;
        }

        private static bool ReadBoolean(object target, string name)
        {
            var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(target.GetType().FullName, name);
            return (bool)field.GetValue(target);
        }

        private static void Set<T>(object target, string name, T value)
        {
            var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(target.GetType().FullName, name);
            field.SetValue(target, value);
        }

        private static async Task<T> WaitFor<T>(Func<T> read, CancellationToken ct) where T : class
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var value = read();
                if (value != null) return value;
                await Task.Delay(100, ct);
            }
        }

        private static async Task<T> WaitFor<T>(Func<Task<T>> read, CancellationToken ct) where T : class
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var value = await read();
                if (value != null) return value;
                await Task.Delay(200, ct);
            }
        }

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
#endif
