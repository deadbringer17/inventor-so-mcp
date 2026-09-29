#if XR_SO_ACCEPTANCE
using System;
using System.Collections;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Mcp;
using InventorXrSo.Core.Selection;
using InventorXrSo.Core.Session;
using InventorXrSo.Unity.Scene;
using InventorXrSo.Unity.Ui;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace InventorXrSo.Xr
{
    /// <summary>Opt-in, fixture-scoped M3 (Design) acceptance runner for a dedicated Quest build.</summary>
    internal sealed class M3QuestAcceptance : QuestAcceptanceRunner
    {
        internal static readonly string[] ReflectedMembers =
        {
            "AppController._session", "AppController._inspect", "AppController._design", "AppController._selection",
            "AppController.EnterSession",
            "InspectWorkspace.DesignRequested",
            "DesignWorkspace._backend", "DesignWorkspace._session", "DesignWorkspace._panel", "DesignWorkspace._previewView",
            "DesignWorkspace._view", "DesignWorkspace._context", "DesignWorkspace._history", "DesignWorkspace._busy",
            "DesignWorkspace._pendingMutations", "DesignWorkspace._operation", "DesignWorkspace._negative",
            "DesignWorkspace._symmetric", "DesignWorkspace._dimension", "DesignWorkspace._diameter", "DesignWorkspace._through",
            "DesignWorkspace._face", "DesignWorkspace._edges", "DesignWorkspace._handle", "DesignWorkspace._ray",
            "DesignWorkspace._renderedPlan",
            "DesignPreviewView._originals", "DesignPreviewView._previewRoot",
            "HomePanel._entry", "HomePanel._keypad",
        };

        private DesignWorkspace _design;
        private InspectWorkspace _inspect;
        private IDesignWorkspaceBackend _backend;

        protected override string Milestone => "m3";
        protected override int TimeoutSeconds => 360;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AfterSceneLoad() => StartIfRequested<M3QuestAcceptance>("xr_m3_acceptance");

        private DesignSession DesignSess => Read<DesignSession>(_design, "_session");
        private DesignPreviewView PreviewView => Read<DesignPreviewView>(_design, "_previewView");
        private DesignContext DesignCtx => Read<DesignContext>(_design, "_context");
        private static string F(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

        protected override async Task Run(CancellationToken ct)
        {
            var fixture = await WaitForFixture(ct);
            Record("Dedicated fixture loaded: " + fixture.Graph.Root.Name);

            // ---- enter Design exactly like the wrist menu ("Progettazione" button raises DesignRequested)
            Call(App, "EnterSession", EnvironmentMode.StudioVr);
            _inspect = Read<InspectWorkspace>(App, "_inspect");
            _design = Read<DesignWorkspace>(App, "_design");
            Check(_inspect != null && _design != null, "app exposes the Inspect and Design workspaces");
            await OpenDesignFromWristAsync(ct);
            _backend = Read<IDesignWorkspaceBackend>(_design, "_backend");
            var inspection = _backend as IInspectionBackend;
            var historyBackend = _backend as IDesignHistoryBackend;
            Check(_backend != null && historyBackend != null, "Design backend supports context, preview, commit and XR history");
            var context = DesignCtx;
            Check(context.Sketches.Any(s => s.Name == "Base_M3"), "Design context lists the unconsumed sketch Base_M3");
            Check(context.Edges.Count > 0 && context.Faces.Count > 0, "Design context exposes edges and planar faces");
            Pass("M3-C1", "Design opened from the wrist path; context loaded with Base_M3, "
                + context.Edges.Count + " edges, " + context.Faces.Count + " planar faces");

            var state0 = await _backend.GetDocumentStateAsync(ct);
            double? volume0 = await ReadVolumeAsync(inspection, state0, "initial", ct);
            if (volume0.HasValue)
            {
                Check(Math.Abs(volume0.Value - 12000) <= 1, "initial volume is 12000 mm3 (measured " + F(volume0.Value) + ")");
                Pass("M3-C1", "initial volume " + F(volume0.Value) + " mm3");
            }
            double expectedJoined = 12000 + Math.PI * 25 * 20;

            // ---- C1: extrude preview / cancel
            await PreviewExtrudeAsync(state0, ct);
            var session = DesignSess;
            var previewView = PreviewView;
            var root = Read<GameObject>(previewView, "_previewRoot");
            var originals = Read<IList>(previewView, "_originals");
            Check(root != null && root.transform.childCount > 0, "preview solid result is built");
            Check(originals != null && originals.Count > 0, "original body is ghosted (translucent) under the preview");
            Check(Read<string>(_design, "_renderedPlan") == session.Preview.PlanId && previewView.PlanId == session.Preview.PlanId,
                "workspace rendered the current plan");
            await CaptureScreenshot("extrude-preview", ct);
            Pass("M3-C1", "extrude Base_M3 20 mm join positive: preview ready, solid result and translucent original shown, revision unchanged");
            Click("Annulla comando");
            await VerifyCancelledAsync(state0, ct);
            Pass("M3-C1", "Cancel clears preview and ghost; revision unchanged");

            // ---- C2: preview again -> Apply
            await PreviewExtrudeAsync(state0, ct);
            RequireFixture();
            Click("Applica");
            await SettleAsync(state0.Revision, ct);
            var state1 = await _backend.GetDocumentStateAsync(ct);
            Check(state1.DocumentId == state0.DocumentId && state1.Revision != state0.Revision, "Apply changed the revision");
            Check(!PreviewView.IsShowing && DesignSess.Preview == null, "Apply consumed and cleared the preview");
            double? volume1 = await ReadVolumeAsync(inspection, state1, "after Apply", ct);
            if (volume1.HasValue)
            {
                Check(Math.Abs(volume1.Value - expectedJoined) <= 1,
                    "volume after Apply is 12000 + pi*25*20 = " + F(expectedJoined) + " (measured " + F(volume1.Value) + ")");
                Pass("M3-C2", "Apply: revision changed, volume " + F(volume1.Value) + " mm3 (expected " + F(expectedJoined) + ")");
            }
            else
            {
                Pass("M3-C2", "Apply: revision changed " + state0.Revision + " -> " + state1.Revision);
                Record("NOT COVERED [M3-C2] volume was not readable through inventor_inspect_xr; revision evidence only");
            }
            var history1 = Read<DesignHistory>(_design, "_history");
            Check(history1.CanUndo, "XR history offers Undo after Apply");

            // stale plan: computed at the committed revision, committed only after Undo
            var committedContext = DesignCtx;
            var staleEdge = PickBottomLineEdge(committedContext);
            var stalePreview = await _backend.PreviewDesignAsync(state1,
                new JArray(DesignOperations.Fillet(new[] { staleEdge.Id }, 1)), ct);
            var afterStalePreview = await _backend.GetDocumentStateAsync(ct);
            Check(afterStalePreview.Revision == state1.Revision, "preview for the stale check left the revision unchanged");

            RequireFixture();
            Click("Annulla modifica XR");
            await SettleAsync(state1.Revision, ct);
            var state2 = await _backend.GetDocumentStateAsync(ct);
            Check(state2.Revision != state1.Revision, "XR Undo changed the revision");
            double? volume2 = await ReadVolumeAsync(inspection, state2, "after Undo", ct);
            if (volume2.HasValue)
            {
                Check(Math.Abs(volume2.Value - 12000) <= 1, "XR Undo restores 12000 mm3 (measured " + F(volume2.Value) + ")");
                Pass("M3-C2", "XR Undo restored volume " + F(volume2.Value) + " mm3; revision equals original: " + (state2.Revision == state0.Revision));
            }
            else
            {
                Pass("M3-C2", "XR Undo changed revision; revision equals original: " + (state2.Revision == state0.Revision));
                Record("NOT COVERED [M3-C2] volume after Undo was not readable; revision evidence only");
            }
            Check(Read<DesignHistory>(_design, "_history").CanRedo, "XR history offers Redo after Undo");

            RequireFixture();
            bool staleRejected = false;
            try { await _backend.CommitDesignAsync(stalePreview, ct); }
            catch (McpToolException ex) when (ex.Code == "STALE_REVISION") { staleRejected = true; }
            Check(staleRejected, "commit of a pre-Undo preview is rejected as STALE_REVISION");
            var afterStale = await _backend.GetDocumentStateAsync(ct);
            Check(afterStale.Revision == state2.Revision, "rejected stale commit left the document untouched");
            Pass("M3-Stale", "preview computed at the committed revision rejected with STALE_REVISION after Undo");

            RequireFixture();
            Click("Ripeti modifica XR");
            await SettleAsync(state2.Revision, ct);
            var state3 = await _backend.GetDocumentStateAsync(ct);
            Check(state3.Revision != state2.Revision, "XR Redo changed the revision");
            double? volume3 = await ReadVolumeAsync(inspection, state3, "after Redo", ct);
            if (volume3.HasValue)
            {
                Check(Math.Abs(volume3.Value - expectedJoined) <= 1, "XR Redo restores the committed volume (measured " + F(volume3.Value) + ")");
                Pass("M3-C2", "XR Redo restored committed volume " + F(volume3.Value) + " mm3");
            }
            else Pass("M3-C2", "XR Redo changed revision to " + state3.Revision);

            // ---- C9: blind hole on the top face of the block
            var topFace = DesignCtx.Faces.FirstOrDefault(f => Math.Abs(f.Normal.Z - 1) < 1e-3 && Math.Abs(f.PointMm.Z - 10) < 0.01);
            Check(topFace != null, "context exposes the block top face (normal +Z at Z=10 mm)");
            Click("Foro");
            Set(_design, "_face", topFace.Id);
            Click("Posizione esatta XYZ");
            TypeValues(12, 0, topFace.PointMm.Z);
            Click("Diametro");
            TypeValues(4);
            Click("Passante → Cieco");
            Click("Dimensione numerica");
            TypeValues(5);
            Check(!ReadBoolean(_design, "_through") && Math.Abs(ReadValue<double>(_design, "_diameter") - 4) < 1e-9
                && Math.Abs(ReadValue<double>(_design, "_dimension") - 5) < 1e-9, "hole draft is blind, diameter 4 mm, depth 5 mm");
            await PreviewAndVerifyAsync(state3, ct);
            Pass("M3-C9", "blind hole d4 x 5 mm at (12, 0, " + F(topFace.PointMm.Z) + ") on the top face: preview ready, revision unchanged");
            Click("Annulla comando");
            await VerifyCancelledAsync(state3, ct);
            Pass("M3-C9", "hole preview cancelled; revision unchanged");

            // ---- C10: fillet 1 mm
            var edge = PickBottomLineEdge(DesignCtx);
            Click("Raccordo");
            Read<HashSet<string>>(_design, "_edges").Add(edge.Id);
            Click("Dimensione numerica");
            TypeValues(1);
            Check(Math.Abs(ReadValue<double>(_design, "_dimension") - 1) < 1e-9, "fillet radius is 1 mm");
            await PreviewAndVerifyAsync(state3, ct);
            Pass("M3-C10", "fillet 1 mm on edge " + edge.Id + ": preview ready, revision unchanged");
            Click("Annulla comando");
            await VerifyCancelledAsync(state3, ct);
            Pass("M3-C10", "fillet preview cancelled; revision unchanged");

            // ---- C11: a 100 mm radius on one edge can be valid in Inventor.
            // Use the block's straight-edge group so the oversized radius creates a real CAD error.
            var blockEdges = DesignCtx.Edges.Where(e => e.Kind == "kLineSegmentCurve" || e.Kind == "kLineCurve")
                .Select(e => e.Id).Distinct().ToArray();
            Check(blockEdges.Length >= 8, "context exposes the block edge group for the invalid fillet case");
            Click("Raccordo");
            Read<HashSet<string>>(_design, "_edges").UnionWith(blockEdges);
            Click("Dimensione numerica");
            TypeValues(1);
            await PreviewAndVerifyAsync(state3, ct);
            Click("Dimensione numerica");
            TypeValues(100);
            Check(Math.Abs(ReadValue<double>(_design, "_dimension") - 100) < 1e-9, "fillet radius is 100 mm");
            Check(!DesignSess.CanApply && !ButtonInteractable("Applica"), "editing the radius disables Apply until a new preview");
            Click("Anteprima");
            await WaitUntil(() => DesignSess.Status != DesignStatus.Previewing, ct);
            Check(DesignSess.Status == DesignStatus.Error && !string.IsNullOrEmpty(DesignSess.Error),
                "fillet 100 mm fails validation (status " + DesignSess.Status + ")");
            Check(!DesignSess.CanApply && !ButtonInteractable("Applica"), "Apply is disabled after the validation failure");
            bool ghostRetained = PreviewView.IsShowing;
            Record("Fillet 100 mm error: '" + DesignSess.Error + "'; last ghost retained=" + ghostRetained);
            var afterInvalid = await _backend.GetDocumentStateAsync(ct);
            Check(afterInvalid.Revision == state3.Revision, "failed preview leaves the revision unchanged");
            Pass("M3-C11", "100 mm fillet rejected, Apply disabled, ghost retained=" + ghostRetained);
            Click("Dimensione numerica");
            TypeValues(1);
            await PreviewAndVerifyAsync(state3, ct);
            Check(ButtonInteractable("Applica"), "Apply is enabled again after a valid 1 mm preview");
            Pass("M3-C11", "1 mm again: preview ready and Apply re-enabled");
            Click("Annulla comando");
            await VerifyCancelledAsync(state3, ct);

            // ---- C12: parameter +1 mm through the Parameters page and keypad
            await RunParameterCaseAsync(state3, ct);

            // ---- C15: back to Inspect with a live ghost, Inspect selection, reopen
            Click("Raccordo");
            Read<HashSet<string>>(_design, "_edges").Add(edge.Id);
            Click("Dimensione numerica");
            TypeValues(1);
            await PreviewAndVerifyAsync(state3, ct);
            var contextBeforeClose = DesignCtx;
            Click("Torna a Ispeziona");
            Check(!_design.Active && !_inspect.DesignActive, "Design closed and Inspect is active again");
            Check(!PreviewView.IsShowing && Read<IList>(PreviewView, "_originals").Count == 0
                && DesignSess.Preview == null && DesignSess.Status == DesignStatus.Empty, "no ghost after leaving Design");
            Check(Read<LineRenderer>(_design, "_handle").positionCount == 0, "no dimension handle line is active");
            Check(Read<ControllerRay>(_design, "_ray").CanPick, "ray picking is handed back to Inspect");
            var view = Read<CadSceneView>(_design, "_view");
            CadBody pickBody = null; int pickTriangle = 0;
            foreach (var instance in view.Instances)
                foreach (var body in instance.Bodies)
                    if (pickBody == null && body.Primitive.FaceMap.FaceAtTriangle(0) != null) { pickBody = body; pickTriangle = 0; }
            Check(pickBody != null, "scene has a pickable body face for the Inspect selection check");
            var selection = Read<SelectionService>(App, "_selection");
            await _inspect.PickAsync(pickBody, pickTriangle, ct);
            Check(selection.Current.Kind == SelectionKind.Face, "Inspect selection works after Design (face selected)");
            await selection.ClearAsync(ct);
            _inspect.ClearSelection();
            Pass("M3-C15", "Torna a Inspect: ghost, handle and draft cleared; Inspect face selection works");

            await OpenDesignFromWristAsync(ct, contextBeforeClose);
            Check(DesignCtx != null && !ReferenceEquals(DesignCtx, contextBeforeClose) && DesignCtx.Edges.Count > 0,
                "reopened Design reloaded its context");
            Pass("M3-C15", "Design reopened: context reloaded (" + DesignCtx.Edges.Count + " edges)");

            Click("Torna a Ispeziona");
            var finalState = await _backend.GetDocumentStateAsync(ct);
            Check(!_design.Active, "Design closed at the end of the run");
            Record("Fixture left with the join extrusion of Base_M3 COMMITTED (Undo then Redo): revision " + finalState.Revision
                + "; Design workspace closed, no open command. Restore the fixture before rerunning.");
            NotCovered("M3-Physical", "controller sketch, edge pick, manipulator drag, tracking and readability require physical Quest input");
        }

        private async Task OpenDesignFromWristAsync(CancellationToken ct, DesignContext previousContext = null)
        {
            var requested = Read<Action>(_inspect, "DesignRequested");
            Check(requested != null, "Inspect wrist menu wires DesignRequested");
            requested();
            Check(_design.Active, "Design workspace opened from the wrist path");
            await WaitUntil(() => !ReadBoolean(_design, "_busy") && DesignCtx != null
                && !ReferenceEquals(DesignCtx, previousContext) && Read<DesignHistory>(_design, "_history") != null, ct);
        }

        private void Click(string label)
        {
            var panel = Read<HomePanel>(_design, "_panel");
            var button = panel.GetComponentsInChildren<Button>()
                .FirstOrDefault(b => b.gameObject.activeInHierarchy && b.GetComponentInChildren<Text>()?.text == label);
            Check(button != null, "Design panel has no button '" + label + "'");
            Check(button.interactable, "Design button '" + label + "' is disabled");
            button.onClick.Invoke();
        }

        private bool ButtonInteractable(string label)
        {
            var panel = Read<HomePanel>(_design, "_panel");
            var button = panel.GetComponentsInChildren<Button>()
                .FirstOrDefault(b => b.gameObject.activeInHierarchy && b.GetComponentInChildren<Text>()?.text == label);
            return button != null && button.interactable;
        }

        private void TypeValues(params double[] values)
        {
            var panel = Read<HomePanel>(_design, "_panel");
            foreach (var value in values)
            {
                Check(Read<RectTransform>(panel, "_keypad").gameObject.activeSelf, "numeric keypad is open");
                while (Read<string>(panel, "_entry").Length > 0) panel.Press(UiText.KeyBack);
                foreach (char c in F(value)) panel.Press(c.ToString());
                panel.Press(UiText.KeyOk);
            }
        }

        private async Task PreviewExtrudeAsync(DocumentState baseline, CancellationToken ct)
        {
            Click("Estrusione");
            Click("Base_M3");
            Click("Dimensione numerica");
            TypeValues(20);
            Check(Math.Abs(ReadValue<double>(_design, "_dimension") - 20) < 1e-9 && Read<string>(_design, "_operation") == "join"
                && !ReadBoolean(_design, "_negative") && !ReadBoolean(_design, "_symmetric"),
                "extrusion draft is 20 mm, join, positive");
            await PreviewAndVerifyAsync(baseline, ct);
        }

        private async Task PreviewAndVerifyAsync(DocumentState baseline, CancellationToken ct)
        {
            Click("Anteprima");
            await WaitUntil(() => DesignSess.Status != DesignStatus.Previewing, ct);
            Check(DesignSess.Status == DesignStatus.PreviewReady && DesignSess.CanApply,
                "preview is ready for Apply (status " + DesignSess.Status + ", error '" + DesignSess.Error + "')");
            Check(PreviewView.IsShowing, "DesignPreviewView is showing the preview");
            var state = await _backend.GetDocumentStateAsync(ct);
            Check(state.DocumentId == baseline.DocumentId && state.Revision == baseline.Revision,
                "preview leaves document id and revision unchanged");
        }

        private async Task VerifyCancelledAsync(DocumentState baseline, CancellationToken ct)
        {
            Check(DesignSess.Status == DesignStatus.Empty && DesignSess.Preview == null && !DesignSess.CanApply
                && !PreviewView.IsShowing && Read<IList>(PreviewView, "_originals").Count == 0,
                "Cancel discards the preview and restores the original materials");
            var state = await _backend.GetDocumentStateAsync(ct);
            Check(state.DocumentId == baseline.DocumentId && state.Revision == baseline.Revision, "Cancel leaves the revision unchanged");
        }

        private async Task SettleAsync(string previousRevision, CancellationToken ct)
        {
            await WaitUntil(() =>
            {
                var session = DesignSess;
                Check(session.Status != DesignStatus.Error, "mutation failed: " + session.Error);
                Check(!session.CommitOutcomeUnknown, "mutation outcome unknown");
                var ctx = DesignCtx;
                var history = Read<DesignHistory>(_design, "_history");
                return ReadValue<int>(_design, "_pendingMutations") == 0 && session.Status == DesignStatus.Empty
                    && !ReadBoolean(_design, "_busy") && ctx != null && ctx.State.Revision != previousRevision
                    && history != null && history.State.Revision == ctx.State.Revision;
            }, ct);
        }

        private static DesignEdge PickBottomLineEdge(DesignContext context)
        {
            bool IsLine(DesignEdge e) => e.Kind == "kLineSegmentCurve" || e.Kind == "kLineCurve";
            var edge = context.Edges.FirstOrDefault(e => IsLine(e) && e.PointsMm.All(p => Math.Abs(p.Z) < 0.01))
                ?? context.Edges.FirstOrDefault(IsLine);
            Check(edge != null, "context exposes a straight block edge for the fillet");
            return edge;
        }

        private async Task<double?> ReadVolumeAsync(IInspectionBackend inspection, DocumentState state, string when, CancellationToken ct)
        {
            if (inspection == null) { Record("NOT COVERED [M3-C2] backend has no IInspectionBackend (" + when + ")"); return null; }
            try
            {
                var info = await inspection.InspectAsync(state, Session.Scene.Graph.Root.OccurrenceId, ct);
                if (info.VolumeMm3.HasValue) Record("Volume " + when + ": " + F(info.VolumeMm3.Value) + " mm3");
                else Record("Volume " + when + " not returned by inventor_inspect_xr");
                return info.VolumeMm3;
            }
            catch (Exception ex)
            {
                Record("Volume " + when + " unreadable: " + ex.GetType().Name + ": " + ex.Message);
                return null;
            }
        }

        private async Task RunParameterCaseAsync(DocumentState baseline, CancellationToken ct)
        {
            var context = DesignCtx;
            var parameters = context.Parameters.OfType<JObject>().Where(p => p["value_mm"] != null).ToArray();
            Check(parameters.Length > 0, "context lists at least one length parameter");
            var preferred = context.SketchSnapshots.Where(s => s.Name == "Blocco").SelectMany(s => s.Dimensions).Select(d => d.Name).ToArray();
            var parameter = parameters.FirstOrDefault(p => preferred.Contains((string)p["name"])) ?? parameters[0];
            string name = (string)parameter["name"];
            double value = (double)parameter["value_mm"], target = value + 1;
            Click("Parametri");
            Button button = null;
            var panel = Read<HomePanel>(_design, "_panel");
            for (int page = 0; page < 6 && button == null; page++)
            {
                button = panel.GetComponentsInChildren<Button>().FirstOrDefault(b => b.gameObject.activeInHierarchy
                    && (b.GetComponentInChildren<Text>()?.text ?? "").StartsWith(name + " = ", StringComparison.Ordinal));
                if (button == null) { if (!ButtonInteractable("Successivi")) break; Click("Successivi"); }
            }
            Check(button != null && button.interactable, "Parameters page lists " + name);
            button.onClick.Invoke();
            TypeValues(target);
            await WaitUntil(() => DesignSess.Status != DesignStatus.Previewing, ct);
            Check(DesignSess.Status == DesignStatus.PreviewReady && DesignSess.CanApply && PreviewView.IsShowing,
                "parameter preview is ready (status " + DesignSess.Status + ", error '" + DesignSess.Error + "')");
            var state = await _backend.GetDocumentStateAsync(ct);
            Check(state.Revision == baseline.Revision, "parameter preview leaves the revision unchanged");
            Pass("M3-C12", "parameter " + name + " " + F(value) + " -> " + F(target) + " mm: preview ready, revision unchanged");
            Click("Annulla comando");
            await VerifyCancelledAsync(baseline, ct);
            Pass("M3-C12", "parameter preview cancelled; not committed");
        }
    }
}
#endif
