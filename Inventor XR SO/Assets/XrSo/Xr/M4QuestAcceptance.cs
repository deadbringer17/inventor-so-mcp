#if XR_SO_ACCEPTANCE
using System;
using System.Linq;
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
    internal sealed class M4QuestAcceptance : QuestAcceptanceRunner
    {
        protected override string Milestone => "m4";
        protected override int TimeoutSeconds => 120;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AfterSceneLoad() => StartIfRequested<M4QuestAcceptance>("xr_m4_acceptance");

        protected override async Task Run(CancellationToken ct)
        {
            var fixture = await WaitForFixture(ct);
            var app = App;
            var appSession = Session;
            Check(fixture.Graph.Root.Name.StartsWith(FixturePrefix, StringComparison.Ordinal),
                "connected document is the dedicated M4 acceptance fixture");
            Record("PASS; dedicated fixture loaded: " + fixture.Graph.Root.Name);

            Call(app, "EnterSession", EnvironmentMode.StudioVr);
            var workspace = Read<AssemblyWorkspace>(app, "_assembly");
            workspace.Open();
            var backend = Read<IAssemblyWorkspaceBackend>(workspace, "_backend");
            var view = Read<CadSceneView>(workspace, "_view");
            var designSession = Read<DesignSession>(workspace, "_session");
            var previewView = Read<DesignPreviewView>(workspace, "_preview");
            Check(view != null && previewView != null && view.GetComponents<DesignPreviewView>().Contains(previewView),
                "runner uses the workspace CAD view and its live preview renderer");
            await WaitFor(() => !ReadBoolean(workspace, "_busy")
                ? Read<AssemblyContext>(workspace, "_context") : null, ct);
            Check(workspace.Active && Read<AssemblyContext>(workspace, "_context") != null,
                "Assembly workspace opened and loaded context");

            var context = Read<AssemblyContext>(workspace, "_context");
            var occurrence = context.Occurrences.FirstOrDefault(item => item.CanMove);
            Check(occurrence != null, "fixture has an occurrence with complete movable DOF");
            await workspace.SelectOccurrenceAsync(occurrence.Id);
            await WaitFor(() => !ReadBoolean(workspace, "_busy")
                ? Read<AssemblyOccurrence>(workspace, "_occurrence") : null, ct);
            occurrence = Read<AssemblyOccurrence>(workspace, "_occurrence");
            Check(occurrence != null && occurrence.CanMove, "selected fixture occurrence remains movable");

            var initialState = Read<DocumentState>(workspace, "_state");
            var initialContext = await backend.GetAssemblyContextAsync(initialState, occurrence.Id, ct);
            var initialOccurrence = initialContext.Occurrences.Single(item => item.Id == occurrence.Id);
            Check(initialOccurrence.Center.HasValue, "movable occurrence has a native rotation center");
            var originalCenter = initialOccurrence.Center.Value;
            Record("Fixture occurrence selected; initial center X=" + originalCenter.X.ToString("0.###"));

            var grounded = context.Occurrences.Single(item => item.Grounded);
            var groundedContext = await backend.GetAssemblyContextAsync(initialState, grounded.Id, ct);
            var movingContext = await backend.GetAssemblyContextAsync(initialState, occurrence.Id, ct);
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
                    new Newtonsoft.Json.Linq.JArray(AssemblyOperations.Joint("planar", referenceA, referenceB)), ct);
                Record("PLANAR DIAGNOSTIC; preview succeeded for physical A/B face ordinals");
            }
            catch (Exception ex)
            {
                Record("PLANAR DIAGNOSTIC; preview rejected: " + ex.GetType().Name + ": " + ex.Message);
            }
            var afterPlanar = await backend.GetDocumentStateAsync(ct);
            Check(afterPlanar.DocumentId == initialState.DocumentId && afterPlanar.Revision == initialState.Revision,
                "Planar preview leaves Inventor revision unchanged");

            // A07 regression: CAD Move used immediately before choosing A/B must not
            // leave a move preview or an Apply action behind the compatible list.
            workspace.BeginMove();
            Set(workspace, "_translation", new CadPoint(10, 0, 0));
            await workspace.PreviewAsync();
            Check(designSession.CanApply, "CAD Move preview is ready before switching to A/B");
            workspace.ChooseReference(referenceA);
            workspace.ChooseReference(referenceB);
            Check(Read<string>(workspace, "_screen") == "compatible"
                && Read<string>(workspace, "_command") == null
                && !designSession.CanApply && !previewView.IsShowing,
                "choosing A/B after CAD Move discards the stale move preview and Apply");
            var afterReferences = await backend.GetDocumentStateAsync(ct);
            Check(afterReferences.DocumentId == initialState.DocumentId && afterReferences.Revision == initialState.Revision,
                "A/B selection after CAD Move leaves Inventor revision unchanged");
            Pass("A07-programmatic", "A/B after CAD Move offers compatible commands without preview, Apply or CAD revision change");

            workspace.BeginMove();
            Set(workspace, "_translation", new CadPoint(10, 0, 0));
            await workspace.PreviewAsync();
            Check(designSession.CanApply && previewView.IsShowing,
                "10 mm move preview is rendered and enabled for Apply");
            var afterPreview = await backend.GetDocumentStateAsync(ct);
            Check(afterPreview.DocumentId == initialState.DocumentId && afterPreview.Revision == initialState.Revision,
                "preview leaves the fixture revision unchanged");
            await CaptureScreenshot("preview", ct);
            Record("PASS; rendered 10 mm preview; revision unchanged");

            Call(workspace, "Cancel");
            Check(!designSession.CanApply && designSession.Preview == null && !previewView.IsShowing,
                "Cancel discards the preview and clears its rendering");
            Record("PASS; cancel clears the preview");

            await WaitFor(() => !ReadBoolean(workspace, "_busy")
                ? Read<AssemblyContext>(workspace, "_context") : null, ct);
            context = Read<AssemblyContext>(workspace, "_context");
            occurrence = context.Occurrences.FirstOrDefault(item => item.Id == occurrence.Id && item.CanMove)
                ?? context.Occurrences.FirstOrDefault(item => item.CanMove);
            Check(occurrence != null, "movable fixture occurrence reloads after cancel");
            await workspace.SelectOccurrenceAsync(occurrence.Id);
            await WaitFor(() => !ReadBoolean(workspace, "_busy")
                ? Read<AssemblyOccurrence>(workspace, "_occurrence") : null, ct);
            workspace.BeginMove();
            Set(workspace, "_translation", new CadPoint(10, 0, 0));
            await workspace.PreviewAsync();
            Check(designSession.CanApply && previewView.IsShowing,
                "second rendered move preview is ready for Apply");
            await workspace.ApplyAsync();
            Check(!designSession.CanApply && !previewView.IsShowing, "Apply consumes and clears the preview");

            var appState = await WaitFor(async () =>
            {
                var state = await backend.GetDocumentStateAsync(ct);
                return state.DocumentId == initialState.DocumentId && state.Revision != initialState.Revision ? state : null;
            }, ct);
            var committedContext = await backend.GetAssemblyContextAsync(appState, occurrence.Id, ct);
            var committedOccurrence = committedContext.Occurrences.Single(item => item.Id == occurrence.Id);
            Check(committedOccurrence.Center.HasValue
                && Math.Abs(committedOccurrence.Center.Value.X - originalCenter.X - 10) < 0.02,
                "committed move advances the occurrence center by 10 mm");
            Record("PASS; Apply changed revision and center X to " + committedOccurrence.Center.Value.X.ToString("0.###"));

            var historyBackend = (IDesignHistoryBackend)backend;
            var undoReceipt = await historyBackend.GetHistoryAsync(appState, ct);
            Check(undoReceipt.CanUndo, "XR history offers Undo after Apply");

            // Keep a valid plan at the committed revision; Undo must make its commit stale.
            var stalePreview = await backend.PreviewDesignAsync(appState,
                new Newtonsoft.Json.Linq.JArray(AssemblyOperations.Move(occurrence.Id, new CadPoint(10, 0, 0))), ct);
            var undoneState = await historyBackend.ApplyHistoryAsync(undoReceipt, false, ct);
            var undoneContext = await backend.GetAssemblyContextAsync(undoneState, occurrence.Id, ct);
            var undoneOccurrence = undoneContext.Occurrences.Single(item => item.Id == occurrence.Id);
            Check(undoneOccurrence.Center.HasValue
                && Math.Abs(undoneOccurrence.Center.Value.X - originalCenter.X) < 0.02,
                "XR Undo restores the original occurrence center");
            var redoReceipt = await historyBackend.GetHistoryAsync(undoneState, ct);
            Check(redoReceipt.CanRedo, "XR history offers Redo after Undo");
            bool staleRejected = false;
            try { await backend.CommitDesignAsync(stalePreview, ct); }
            catch (McpToolException ex) when (ex.Code == "STALE_REVISION") { staleRejected = true; }
            Check(staleRejected, "commit of a pre-Undo preview is rejected as stale");
            var redoneState = await historyBackend.ApplyHistoryAsync(redoReceipt, true, ct);
            var redoneContext = await backend.GetAssemblyContextAsync(redoneState, occurrence.Id, ct);
            var redoneOccurrence = redoneContext.Occurrences.Single(item => item.Id == occurrence.Id);
            Check(redoneOccurrence.Center.HasValue
                && Math.Abs(redoneOccurrence.Center.Value.X - committedOccurrence.Center.Value.X) < 0.02,
                "XR Redo restores the committed occurrence center");
            Record("PASS; Undo/Redo restore original and committed centers; stale preview rejected");

            await WaitFor(async () =>
            {
                var state = await backend.GetDocumentStateAsync(ct);
                return state.DocumentId == redoneState.DocumentId && state.Revision == redoneState.Revision ? state : null;
            }, ct);
            await WaitFor(() => appSession.Scene?.Graph?.State?.Revision == redoneState.Revision
                ? appSession.Scene : null, ct);
            workspace.Close();
            Check(!workspace.Active, "Assembly workspace closes");
            workspace.Open();
            var reopened = await WaitFor(() => Read<AssemblyContext>(workspace, "_context"), ct);
            Check(reopened.Occurrences.Any(item => item.CanMove), "reopened Assembly context exposes movable DOF");
            Record("PASS; reopened Assembly context and DOF");

            await CheckSyntheticControllerPaths(workspace, backend, view, designSession, previewView,
                reopened.Occurrences.First(item => item.CanMove).Id, ct);
            NotCovered("A01-physical", "plain Grip with a real controller requires a person wearing the Quest");
            NotCovered("A02-physical", "final pose equivalence for real gestures at reduced scale requires a person wearing the Quest");
            NotCovered("A07-physical", "real face picking and unintended UI activation require controller observation");
            NotCovered("A14-physical", "tracking loss, click-through and error readability require observation in the headset");
        }

        private async Task CheckSyntheticControllerPaths(AssemblyWorkspace workspace, IAssemblyWorkspaceBackend backend,
            CadSceneView view, DesignSession designSession, DesignPreviewView previewView,
            string occurrenceId, CancellationToken ct)
        {
            var ray = Read<ControllerRay>(workspace, "_ray");
            var hand = ray.Origin;
            var savedScale = view.transform.localScale;
            var savedPosition = hand.position;
            var savedRotation = hand.rotation;
            const float scale = 0.25f;
            try
            {
                view.transform.localScale = Vector3.one * scale;
                await workspace.SelectOccurrenceAsync(occurrenceId);
                var occurrence = Read<AssemblyOccurrence>(workspace, "_occurrence");
                Check(occurrence != null && occurrence.TranslationAxes.Count > 0 && occurrence.RotationAxes.Count > 0,
                    "fixture has translation and rotation axes for reduced-scale gestures");
                var before = await backend.GetDocumentStateAsync(ct);

                workspace.BeginMove();
                var axis = occurrence.TranslationAxes[0];
                var worldAxis = view.transform.TransformDirection(CadCoordinates.ToLocal(axis)).normalized;
                var target = view.transform.TransformPoint(CadCoordinates.ToLocal(occurrence.Center.Value + axis * 50));
                var side = Vector3.Cross(worldAxis, Vector3.up).normalized;
                if (side.sqrMagnitude < 0.1f) side = Vector3.Cross(worldAxis, Vector3.right).normalized;
                hand.SetPositionAndRotation(target + side * 0.15f, Quaternion.LookRotation(-side));
                Check(Read<AssemblyVisuals>(workspace, "_visuals").HitHandle(new Ray(hand.position, hand.forward)),
                    "reduced-scale translation handle can be hit");
                ControllerFrame(workspace, true, true, true, false, true, false);
                Check(ReadBoolean(workspace, "_dragging"), "synthetic Grip+Trigger arms reduced-scale translation");
                hand.position += worldAxis * (0.010f * scale);
                ControllerFrame(workspace, true, true, true, false, false, false);
                Check(Math.Abs(ReadValue<CadPoint>(workspace, "_translation").Dot(axis) - 10) < 0.05,
                    "reduced-scale controller displacement converts to 10 mm in CAD");
                ControllerFrame(workspace, true, false, false, false, false, false);
                await WaitUntil(() => designSession.CanApply && previewView.IsShowing, ct);
                Check((await backend.GetDocumentStateAsync(ct)).Revision == before.Revision,
                    "reduced-scale translation preview leaves native revision unchanged");
                Pass("A02-programmatic", "synthetic 10 mm translation at 0.25× creates a rendered native preview");

                Call(workspace, "Cancel");
                await WaitFor(() => !ReadBoolean(workspace, "_busy")
                    ? Read<AssemblyContext>(workspace, "_context") : null, ct);
                await workspace.SelectOccurrenceAsync(occurrenceId);
                occurrence = Read<AssemblyOccurrence>(workspace, "_occurrence");
                workspace.BeginMove();
                Set(workspace, "_rotating", true);
                Call(workspace, "Draw");
                var rotationAxis = occurrence.RotationAxes[0];
                var worldRotationAxis = view.transform.TransformDirection(CadCoordinates.ToLocal(rotationAxis)).normalized;
                var ring = view.GetComponentsInChildren<LineRenderer>()
                    .First(item => item.name == "DOF rotazione 1");
                target = ring.transform.TransformPoint(ring.GetPosition(0));
                hand.SetPositionAndRotation(target + worldRotationAxis * 0.1f,
                    Quaternion.LookRotation(-worldRotationAxis));
                Check(Read<AssemblyVisuals>(workspace, "_visuals").HitHandle(new Ray(hand.position, hand.forward)),
                    "reduced-scale rotation handle can be hit");
                ControllerFrame(workspace, true, true, true, false, true, false);
                Check(ReadBoolean(workspace, "_dragging"), "synthetic Grip+Trigger arms reduced-scale rotation");
                hand.rotation = Quaternion.AngleAxis(-12, worldRotationAxis) * hand.rotation;
                ControllerFrame(workspace, true, true, true, false, false, false);
                Check(Math.Abs(ReadValue<double>(workspace, "_angle") - 12) < 0.1,
                    "reduced-scale wrist twist converts to 12 CAD degrees");
                ControllerFrame(workspace, true, false, false, false, false, false);
                await WaitUntil(() => designSession.CanApply && previewView.IsShowing, ct);
                Check((await backend.GetDocumentStateAsync(ct)).Revision == before.Revision,
                    "reduced-scale rotation preview leaves native revision unchanged");
                Pass("A02-programmatic", "synthetic 12 degree rotation at 0.25× creates a rendered native preview");

                Call(workspace, "Cancel");
                await WaitFor(() => !ReadBoolean(workspace, "_busy")
                    ? Read<AssemblyContext>(workspace, "_context") : null, ct);
                await workspace.SelectOccurrenceAsync(occurrenceId);
                occurrence = Read<AssemblyOccurrence>(workspace, "_occurrence");
                workspace.BeginMove();
                axis = occurrence.TranslationAxes[0];
                worldAxis = view.transform.TransformDirection(CadCoordinates.ToLocal(axis)).normalized;
                target = view.transform.TransformPoint(CadCoordinates.ToLocal(occurrence.Center.Value + axis * 50));
                side = Vector3.Cross(worldAxis, Vector3.up).normalized;
                if (side.sqrMagnitude < 0.1f) side = Vector3.Cross(worldAxis, Vector3.right).normalized;
                hand.SetPositionAndRotation(target + side * 0.15f, Quaternion.LookRotation(-side));
                ControllerFrame(workspace, true, true, true, false, true, true);
                Check(!ReadBoolean(workspace, "_dragging") && !designSession.CanApply,
                    "UI hit blocks a synthetic Grip+Trigger gesture");
                ControllerFrame(workspace, true, true, true, false, true, false);
                Check(ReadBoolean(workspace, "_dragging"), "gesture arms before synthetic tracking loss");
                ControllerFrame(workspace, false, false, false, false, false, false);
                Check(!ReadBoolean(workspace, "_dragging") && !designSession.CanApply
                    && !previewView.IsShowing && Read<string>(workspace, "_notice").Contains("Tracking perso"),
                    "tracking loss cancels armed gesture with readable error and no Apply");
                Check((await backend.GetDocumentStateAsync(ct)).Revision == before.Revision,
                    "UI hit and tracking loss leave native revision unchanged");
                Pass("A14-programmatic", "UI hit prevents gesture; tracking loss cancels armed gesture without CAD mutation");
                Call(workspace, "Cancel");
                workspace.Close();
                workspace.Open();
                await WaitFor(() => Read<AssemblyContext>(workspace, "_context"), ct);
                Check(workspace.Active && !ReadBoolean(workspace, "_dragging") && !designSession.CanApply,
                    "Assembly reopens clean after tracking loss");
                Pass("A14-programmatic", "workspace reopens with no armed gesture or Apply");
            }
            finally
            {
                view.transform.localScale = savedScale;
                hand.SetPositionAndRotation(savedPosition, savedRotation);
            }
        }

        private static void ControllerFrame(AssemblyWorkspace workspace, bool tracked, bool grip,
            bool trigger, bool gripDown, bool triggerDown, bool ui)
            => Call(workspace, "ProcessControllerFrame", tracked, grip, trigger, gripDown, triggerDown, ui);
    }
}
#endif
