#if XR_SO_ACCEPTANCE || UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Capture;
using InventorXrSo.Core.Session;
using InventorXrSo.Core.Experience;
using InventorXrSo.Core.Mcp;
using InventorXrSo.Core.Ui;
using InventorXrSo.Unity.Scene;
using InventorXrSo.Unity.Ui;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace InventorXrSo.Xr
{
    /// <summary>QX F1–F4 navigation on the dedicated M7 fixture. Synthetic input, never a physical acceptance.</summary>
    internal sealed class QxQuestAcceptance : QuestAcceptanceRunner
    {
        internal static readonly string[] ReflectedMembers = {
            "AppController._voiceTarget", "RealityCaptureWorkspace._controller", "RealityCaptureController._recorder",
            "AppController._backend", "AppController._catalog", "AppController._inspect", "AppController._home",
            "AppController.sceneView", "AppController._design", "AppController._assembly", "AppController._lamiera",
            "InspectWorkspace.OnPointPicked"
        };
        protected override string Milestone => "qx";
        protected override string FixtureMilestone => "m7";
        protected override int TimeoutSeconds => 300;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap() => StartIfRequested<QxQuestAcceptance>("xr_qx_acceptance");

        protected override string Completion() => "PASS SOFTWARE; synthetic QX subset only; device ergonomics and historical gates remain open";

        protected override async Task Run(CancellationToken ct)
        {
            await WaitForFixture(ct);
            RequireFixture();
            var backend = Read<InventorBackend>(App, "_backend");
            var before = await backend.GetDocumentStateAsync(ct);
            var catalog = Read<ActionCatalog>(App, "_catalog");
            var inspect = Read<InspectWorkspace>(App, "_inspect");
            var home = Read<HomePanel>(App, "_home");
            var view = Read<CadSceneView>(App, "sceneView");
            Check(App.CurrentActivity == Activity.Home, "connection leaves Home selected");
            foreach (string label in new[] { UiText.RealityCapture, UiText.DesignActivity, UiText.PresentationActivity, UiText.VerificationActivity })
                Check(home.VoiceActions.Any(a => a.label == label), "Home has " + label);
            Pass("QX-01", "four Home choices after connection; explicit synthetic UI callbacks below");

            Check(home.InvokeVoiceAction(UiText.PresentationActivity), "Home Presentation callback");
            Check(App.CurrentActivity == Activity.Presentation && !Read<DesignWorkspace>(App, "_design").Active
                && !Read<AssemblyWorkspace>(App, "_assembly").Active && !Read<LamieraWorkspace>(App, "_lamiera").Active, "no authoring workspace");
            foreach (string id in new[] { "doc.save", "assembly.move", "commit.apply", InspectWorkspace.IdSection, InspectWorkspace.IdHealth })
                Check(!catalog.TryInvoke(id), "Presentation rejects " + id);
            try
            {
                await backend.PreviewDesignAsync(before, new JArray(), ct);
                Check(false, "Presentation must reject direct preview");
            }
            catch (McpException error) { Check(error.Code == "ACTIVITY_FORBIDDEN", "blocked before MCP transport"); }
            Check(catalog.TryInvoke(InspectWorkspace.IdMeasure), "measure enabled");
            Call(inspect, "OnPointPicked", view.transform.TransformPoint(Vector3.zero));
            Call(inspect, "OnPointPicked", view.transform.TransformPoint(Vector3.right * 0.02f));
            Check(catalog.TryInvoke(InspectWorkspace.IdMeasurePin), "synthetic 20 mm measurement pinned");
            Check(catalog.TryInvoke(InspectWorkspace.IdScale), "scale picker");
            Check(catalog.TryInvoke(InspectWorkspace.IdScaleTable), "table view scale");
            await CaptureScreenshot("presentation", ct);
            Pass("QX-04", "synthetic palette + direct backend preview blocked; no native revision change checked below");
            Pass("QX-06", "synthetic local measure and scale in Presentation; physical picking not exercised");

            var captured = catalog.Find(InspectWorkspace.IdMeasure);
            Check(catalog.TryInvoke(DocumentActions.IdExit), "Home preserves session");
            Check(home.InvokeVoiceAction(UiText.VerificationActivity), "Home Verification callback");
            Check(!captured.TryInvoke(), "late action from Presentation rejected");
            Check(catalog.TryInvoke(InspectWorkspace.IdSection), "Verification section");
            Check(catalog.TryInvoke(InspectWorkspace.IdBrowse), "Verification scene browser");
            await CaptureScreenshot("verification", ct);
            Check(catalog.TryInvoke(DocumentActions.IdExit), "Home from Verification");
            var navigationOnly = await backend.GetDocumentStateAsync(ct);
            Check(before.DocumentId == navigationOnly.DocumentId && before.Revision == navigationOnly.Revision,
                "activity navigation alone preserves the native revision");
#if UNITY_EDITOR
            await CheckDesktopAndNetwork(backend, catalog, inspect, home, before.DocumentId, ct);
            CheckCaptureExitGuards();
#else
            NotCovered("QX-02", "desktop switch/reconnection exercised by Core/EditMode; dedicated native scenario still required");
#endif
            var after = await backend.GetDocumentStateAsync(ct);
            Check(before.DocumentId == after.DocumentId && !string.IsNullOrEmpty(before.VisualRevision)
                && before.VisualRevision == after.VisualRevision, "native visual revision unchanged after document activation and reconnect");
            Record("Native revision " + before.Revision + " -> " + after.Revision
                + "; document activation may advance revision; visual revision " + after.VisualRevision
                + " is unchanged. Wrapper independently compares native geometry, poses and dirty state.");
            NotCovered("QX-03-physical", "actual camera capture, controller tracking and physical gestures require headset hardware");
            NotCovered("QX-05", "run the migrated historical Design runners separately");
            NotCovered("QX-07", "run migrated M7 against this fixture for analysis, stale, scope and focus");
            NotCovered("QX-15", "Quest profiler benchmark not performed by navigation assertions");
            NotCovered("QX-16", "physical ergonomics and understanding require a person and controllers");
        }

#if UNITY_EDITOR
        private async Task CheckDesktopAndNetwork(InventorBackend backend, ActionCatalog catalog, InspectWorkspace inspect,
            HomePanel home, string assemblyId, CancellationToken ct)
        {
            Check(home.InvokeVoiceAction(UiText.PresentationActivity), "Presentation before desktop switch");
            var part = (await backend.ListOpenAsync(ct)).Single(d => d.Name == "XR_M7_Quest_Acceptance_Cube.ipt");
            await backend.ActivateOpenAsync(part.Id, ct);
            await WaitUntil(() => Session.Status == SessionStatus.Online && Session.Scene?.Graph.DocumentId == part.Id, ct);
            Check(App.CurrentActivity == Activity.Presentation && !App.GetComponent<DesignWorkspace>().Active
                && !App.GetComponent<AssemblyWorkspace>().Active && !App.GetComponent<LamieraWorkspace>().Active,
                "native desktop activation preserves Presentation and excludes all authoring workspaces");
            Check(!catalog.TryInvoke("design.extrude") && !catalog.TryInvoke("commit.apply"), "part switch does not enable authoring in Presentation");
            var voice = Read<InventorXrSo.Xr.Voice.WorkspaceVoiceTarget>(App, "_voiceTarget");
            Check(!voice.Invoke("act:assembly.move") && !voice.Invoke("act:commit.apply"), "voice target also rejects authoring in Presentation");
            await backend.ActivateOpenAsync(assemblyId, ct);
            await WaitUntil(() => Session.Status == SessionStatus.Online && Session.Scene?.Graph.DocumentId == assemblyId, ct);
            Check(App.CurrentActivity == Activity.Presentation, "desktop return to assembly preserves activity");
            Pass("QX-02-native-document", "native activation assembly/part/assembly preserves Presentation; no authoring via catalog or voice target");
            File.WriteAllText(Path.Combine(EvidenceDirectory, "network-down.request"), "stop only the owned fixture host");
            await WaitUntil(() => Session.Status == SessionStatus.Offline, ct, 90);
            Check(App.CurrentActivity == Activity.Presentation, "real HTTPS outage preserves Presentation");
            Check(catalog.TryInvoke(InspectWorkspace.IdMeasure), "Presentation local measurement remains available offline");
            Check(catalog.TryInvoke(InspectWorkspace.IdMeasureCancel), "offline measurement cancels");
            Check(!catalog.TryInvoke("assembly.move"), "offline authoring remains blocked");
            Check(App.RequestActivity(Activity.Verification), "offline Verification opens");
            Check(catalog.TryInvoke(InspectWorkspace.IdBrowse), "offline Verification browser opens");
            Check(!catalog.TryInvoke(InspectWorkspace.IdHealth), "native analysis is disabled offline");
            Check(App.RequestActivity(Activity.Capture), "Capture opens during HTTPS outage");
            var capture = App.GetComponent<RealityCaptureWorkspace>();
            Check(capture.Active, "Capture stays independent of offline CAD");
            File.WriteAllText(Path.Combine(EvidenceDirectory, "network-restart.request"), "restart with the same pinned certificate and token");
            await WaitUntil(() => Session.Status == SessionStatus.Online && Session.Scene?.Graph.DocumentId == assemblyId, ct, 90);
            Check(App.CurrentActivity == Activity.Capture && capture.Active && !Read<CadSceneView>(App, "sceneView").gameObject.activeSelf,
                "reconnection preserves Capture and keeps CAD hidden");
            await CaptureScreenshot("reconnected-capture", ct);
            Check(App.RequestActivity(Activity.Home), "Home after reconnection");
            Pass("QX-02-native-reconnect", "owned HTTPS process stopped and restarted; local tools/browser remain usable; analysis disabled offline; Capture survives reconnect");
        }

        private void CheckCaptureExitGuards()
        {
            Check(App.RequestActivity(Activity.Capture), "Capture opens for synthetic lifecycle guards");
            var capture = App.GetComponent<RealityCaptureWorkspace>();
            var controller = Read<InventorXrSo.Xr.Capture.RealityCaptureController>(capture, "_controller");
            var original = Read<ScanRecorder>(controller, "_recorder");
            var store = new LocalScanStore(Path.Combine(EvidenceDirectory, "capture-guard-raw"), 10000000, 0, () => long.MaxValue);
            var scan = new ScanSession(Guid.NewGuid().ToString("N"), CaptureMode.Object, "synthetic-simulator", "1", DateTimeOffset.UtcNow,
                new CaptureVolume(CaptureTransform.Identity, 600, 600, 600), CaptureTransform.Identity);
            var recorder = new ScanRecorder(store, scan);
            try
            {
                Set(controller, "_recorder", recorder); recorder.Start();
                var frame = new CaptureFrame(1000000, "synthetic-left", new CaptureIntrinsics(16,16,12,12,8,8), CaptureTransform.Identity,
                    CaptureTransform.Identity, CaptureTransform.Identity, new CaptureQuality(120,0.01,100,0.3), true);
                Check(recorder.Record(frame, new byte[16*16*3]), "synthetic raw frame is retained");
                Check(!App.RequestActivity(Activity.Home) && capture.Active && recorder.State == CaptureState.Recording,
                    "Home refuses Recording without stopping or losing synthetic raw");
                recorder.Pause();
                Check(!App.RequestActivity(Activity.Home) && capture.Active && recorder.State == CaptureState.Paused, "Home refuses Paused capture");
                recorder.Stop("acceptance"); Set(controller, "_recorder", original);
                foreach (string field in new[] { "_uiBusy", "_reviewBusy", "_referenceCommitUnconfirmed" })
                {
                    try { Set(capture, field, true); Check(!App.RequestActivity(Activity.Home) && capture.Active, "Home refuses synthetic " + field); }
                    finally { Set(capture, field, false); }
                }
                Check(store.Verify(scan.SessionId) == 1, "guarded activity changes preserve raw frame hashes");
                Check(App.RequestActivity(Activity.Home), "Home succeeds after explicit completion");
                Pass("QX-03-synthetic-lifecycle", "Play Mode Capture guards Recording, Paused, upload/review busy and uncertain reference commit; synthetic raw retained; no camera claimed");
            }
            finally { recorder.Stop("cleanup"); Set(controller, "_recorder", original); }
        }
#endif
    }
}
#endif
