#if XR_SO_ACCEPTANCE
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using InventorXrSo.Core.Backend;
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
            "AppController._backend", "AppController._catalog", "AppController._inspect", "AppController._home",
            "AppController.sceneView", "AppController._design", "AppController._assembly", "AppController._lamiera",
            "InspectWorkspace.OnPointPicked"
        };
        protected override string Milestone => "qx";
        protected override string FixtureMilestone => "m7";
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
            var after = await backend.GetDocumentStateAsync(ct);
            Check(before.DocumentId == after.DocumentId && before.Revision == after.Revision, "native CAD unchanged by activity navigation");
            NotCovered("QX-02", "desktop switch/reconnection exercised by Core/EditMode; dedicated native scenario still required");
            NotCovered("QX-03", "draft/acquisition/uncertain commit cases are software-tested; live lifecycle still required");
            NotCovered("QX-05", "run the migrated historical Design runners separately");
            NotCovered("QX-07", "run migrated M7 against this fixture for analysis, stale, scope and focus");
            NotCovered("QX-15", "Quest profiler benchmark not performed by navigation assertions");
            NotCovered("QX-16", "physical ergonomics and understanding require a person and controllers");
        }
    }
}
#endif
