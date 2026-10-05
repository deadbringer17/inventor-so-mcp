#if XR_SO_ACCEPTANCE
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Mcp;
using InventorXrSo.Core.Session;
using InventorXrSo.Core.Ui;
using InventorXrSo.Unity.Scene;
using InventorXrSo.Xr.Input;
using UnityEngine;

namespace InventorXrSo.Xr
{
    /// <summary>
    /// Opt-in, fixture-scoped M4 (Assieme) acceptance runner for a dedicated Quest build. Assieme has no panel any more: every
    /// command goes through the action catalog by stable id (palette, ring and commit bar share these actions), numbers through
    /// the keypad entry, gestures through a synthetic XrInput. All of that is SYNTHETIC input, never a person's hand.
    /// </summary>
    internal sealed class M4QuestAcceptance : QuestAcceptanceRunner
    {
        protected override string Milestone => "m4";
        protected override int TimeoutSeconds => 180;

        private AssemblyWorkspace _ws;
        private bool _syntheticKeypadNoted;
        private XrInput _appInput, _syntheticInput;
        private SyntheticInputSource _syntheticSource;
        private float _syntheticClock;
        // M9: two Trigger presses on the same component inside 350 ms are a double Trigger (enter the component). The synthetic frames
        // are microseconds apart, so every press gets its own far-away window on a clock the runner owns.
        private double _doubleClock = 1000;
        private bool _lastTrigger;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AfterSceneLoad() => StartIfRequested<M4QuestAcceptance>("xr_m4_acceptance");

        private ActionCatalog Catalog => Read<ActionCatalog>(App, "_catalog");
        private DesignSession DesignSess => Read<DesignSession>(_ws, "_session");
        private DesignPreviewView PreviewView => Read<DesignPreviewView>(_ws, "_preview");
        private AssemblyContext Ctx => Read<AssemblyContext>(_ws, "_context");
        private AssemblyOccurrence Occurrence => Read<AssemblyOccurrence>(_ws, "_occurrence");

        /// <summary>Invokes a declared action by id through the catalog, the same path as palette, ring and commit bar.</summary>
        private void RunAction(string id)
        {
            var action = Catalog.Find(id);
            Check(action != null, "the action catalog has no action '" + id + "'");
            Check(action.Enabled, "action '" + id + "' is disabled: " + action.DisabledReason);
            Check(action.TryInvoke(), "action '" + id + "' did not run");
        }

        /// <summary>Chooses an entry of the open picker list (components, references, axes) by label.</summary>
        private void PickItem(string label)
        {
            var action = _ws.Actions.FirstOrDefault(a => a.Id.StartsWith(AssemblyWorkspace.IdPickPrefix, StringComparison.Ordinal)
                && a.Label.EndsWith(label, StringComparison.Ordinal));
            Check(action != null, "the open list has no entry '" + label + "'");
            Check(action.Enabled && action.TryInvoke(), "list entry '" + label + "' did not run");
        }

        private void TypeValues(params double[] values)
        {
            if (!_syntheticKeypadNoted) { Record("Numbers are typed on the keypad entry by the runner (synthetic keypad input)"); _syntheticKeypadNoted = true; }
            foreach (var value in values)
            {
                var entry = _ws.ActiveEntry;
                Check(entry != null && entry.Editing, "numeric keypad is open");
                foreach (char c in NumericEntry.Format(value)) entry.Type(c);
                Check(entry.Commit(out var reason), "keypad accepted " + value + ": " + reason);
            }
        }

        private async Task WaitContext(CancellationToken ct)
            => await WaitFor(() => !ReadBoolean(_ws, "_busy") ? Ctx : null, ct);

        /// <summary>Selects a component from the "Componenti" list, as the palette does.</summary>
        private async Task SelectViaList(AssemblyOccurrence occurrence, CancellationToken ct)
        {
            await WaitContext(ct);
            RunAction(AssemblyWorkspace.IdComponents);
            PickItem(occurrence.Name);
            await WaitUntil(() => !ReadBoolean(_ws, "_busy") && Occurrence?.Id == occurrence.Id, ct);
        }

        private async Task WaitPreviewReady(string what, CancellationToken ct)
        {
            await WaitUntil(() => DesignSess.Status == DesignStatus.PreviewReady || DesignSess.Status == DesignStatus.Error, ct);
            Check(DesignSess.Status == DesignStatus.PreviewReady && DesignSess.CanApply && PreviewView.IsShowing,
                what + ": a rendered native preview is ready (status " + DesignSess.Status + ", error '" + DesignSess.Error + "')");
            Check(_ws.CommitBar.Phase == CommitBarPhase.Ready, "the commit bar offers Apply (" + _ws.CommitBar.Phase + ")");
        }

        /// <summary>Sposta, then the exact distance on the keypad: the draft previews on its own when the value is confirmed.</summary>
        private async Task PreviewMoveAsync(double mm, CancellationToken ct)
        {
            RunAction(AssemblyWorkspace.IdMove);
            RunAction(AssemblyWorkspace.IdMoveValue);
            TypeValues(mm);
            await WaitPreviewReady("move of " + mm + " mm", ct);
        }

        protected override async Task Run(CancellationToken ct)
        {
            var fixture = await WaitForFixture(ct);
            var app = App;
            var appSession = Session;
            Check(fixture.Graph.Root.Name.StartsWith(FixturePrefix, StringComparison.Ordinal),
                "connected document is the dedicated M4 acceptance fixture");
            Record("PASS; dedicated fixture loaded: " + fixture.Graph.Root.Name);

            Call(app, "EnterSession", EnvironmentMode.StudioVr);
            _ws = Read<AssemblyWorkspace>(app, "_assembly");
            // M9: the router opens Assieme for the fixture assembly (no Spazi action any more).
            await WaitUntil(() => _ws.Active, ct, 60);
            var workspace = _ws;
            var backend = Read<IAssemblyWorkspaceBackend>(workspace, "_backend");
            var view = Read<CadSceneView>(workspace, "_view");
            var designSession = DesignSess;
            var previewView = PreviewView;
            Check(view != null && previewView != null && view.GetComponents<DesignPreviewView>().Contains(previewView),
                "runner uses the workspace CAD view and its live preview renderer");
            await WaitContext(ct);
            Check(workspace.Active && Ctx != null, "Assembly workspace opened and loaded context");

            // M6-06: the assembly is raised on opening (65 cm in front, 15 cm below the head), 250 ms tween.
            var bench = Read<Workbench>(app, "_workbench");
            Check(bench.Raised && bench.Frame != null, "the model is in the raised assembly pose");
            bench.Snap();
            var layout = WorkbenchLayout.Assembly(bench.Frame, ModelExtent(view));
            Check(Math.Abs(view.transform.position.y - (float)layout.Position.Y) < 0.01f
                && Math.Abs(view.transform.localScale.x - (float)layout.Scale) < 0.01f,
                "the assembly pose matches WorkbenchLayout.Assembly");
            Pass("M6-06-programmatic", "Assieme opened in the raised pose of WorkbenchLayout.Assembly (scale " + layout.Scale.ToString("0.###") + ")");

            var context = Ctx;
            var occurrence = context.Occurrences.FirstOrDefault(item => item.CanMove);
            Check(occurrence != null, "fixture has an occurrence with complete movable DOF");
            await SelectViaList(occurrence, ct);
            occurrence = Occurrence;
            Check(occurrence != null && occurrence.CanMove, "selected fixture occurrence remains movable");

            var initialState = Read<DocumentState>(workspace, "_state");
            var initialContext = await backend.GetAssemblyContextAsync(initialState, occurrence.Id, ct);
            var initialOccurrence = initialContext.Occurrences.Single(item => item.Id == occurrence.Id);
            Check(initialOccurrence.Center.HasValue, "movable occurrence has a native rotation center");
            var originalCenter = initialOccurrence.Center.Value;
            Record("Fixture occurrence selected from the list; initial center X=" + originalCenter.X.ToString("0.###"));

            var grounded = context.Occurrences.Single(item => item.Grounded);
            var groundedContext = await backend.GetAssemblyContextAsync(initialState, grounded.Id, ct);
            var movingContext = await backend.GetAssemblyContextAsync(initialState, occurrence.Id, ct);
            var referenceA = groundedContext.References.FirstOrDefault(item => item.Kind == "face" && item.FaceOrdinal == 3 && item.Available)
                ?? groundedContext.References.First(item => item.Kind == "face" && item.Available);
            var referenceB = movingContext.References.FirstOrDefault(item => item.Kind == "face" && item.FaceOrdinal == 2 && item.Available)
                ?? movingContext.References.First(item => item.Kind == "face" && item.Available);
            // The reference list entries and the ray pick both end in ChooseReference: the runner calls that shared path (synthetic).
            workspace.ChooseReference(referenceA);
            workspace.ChooseReference(referenceB);
            Check(Read<AssemblyReference>(workspace, "_a")?.Id == referenceA.Id
                && Read<AssemblyReference>(workspace, "_b")?.Id == referenceB.Id
                && workspace.Actions.Any(a => a.Id.StartsWith(AssemblyWorkspace.IdConstraintPrefix, StringComparison.Ordinal)),
                "A/B references on different occurrences open the compatible-constraint list");
            var compatible = AssemblyOperations.CompatibleConstraints(referenceA, referenceB);
            Check(compatible.All(type => workspace.Actions.Any(a => a.Id == AssemblyWorkspace.IdConstraintPrefix + type)),
                "the list offers exactly the compatible constraints");
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
            await PreviewMoveAsync(10, ct);
            Check(designSession.CanApply, "CAD Move preview is ready before switching to A/B");
            workspace.ChooseReference(referenceA);
            workspace.ChooseReference(referenceB);
            Check(Read<string>(workspace, "_command") == null
                && !designSession.CanApply && !previewView.IsShowing
                && workspace.Actions.Any(a => a.Id.StartsWith(AssemblyWorkspace.IdConstraintPrefix, StringComparison.Ordinal))
                && !Catalog.Find(CommitIds.Apply).Enabled,
                "choosing A/B after CAD Move discards the stale move preview and Apply");
            var afterReferences = await backend.GetDocumentStateAsync(ct);
            Check(afterReferences.DocumentId == initialState.DocumentId && afterReferences.Revision == initialState.Revision,
                "A/B selection after CAD Move leaves Inventor revision unchanged");
            Pass("A07-programmatic", "A/B after CAD Move offers compatible commands without preview, Apply or CAD revision change");

            await PreviewMoveAsync(10, ct);
            Check(designSession.CanApply && previewView.IsShowing,
                "10 mm move preview is rendered and enabled for Apply");
            var afterPreview = await backend.GetDocumentStateAsync(ct);
            Check(afterPreview.DocumentId == initialState.DocumentId && afterPreview.Revision == initialState.Revision,
                "preview leaves the fixture revision unchanged");
            await CaptureScreenshot("preview", ct);
            Record("PASS; rendered 10 mm preview; revision unchanged");

            RunAction(CommitIds.Cancel);
            Check(!designSession.CanApply && designSession.Preview == null && !previewView.IsShowing,
                "Cancel discards the preview and clears its rendering");
            Record("PASS; cancel clears the preview");

            await WaitContext(ct);
            context = Ctx;
            occurrence = context.Occurrences.FirstOrDefault(item => item.Id == occurrence.Id && item.CanMove)
                ?? context.Occurrences.FirstOrDefault(item => item.CanMove);
            Check(occurrence != null, "movable fixture occurrence reloads after cancel");
            await SelectViaList(occurrence, ct);
            await PreviewMoveAsync(10, ct);
            Check(designSession.CanApply && previewView.IsShowing,
                "second rendered move preview is ready for Apply");
            RunAction(CommitIds.Apply);
            await WaitUntil(() => !designSession.CanApply && !previewView.IsShowing, ct);
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
            // M9: Close()/Open() are the calls the router makes when the context changes (UNVERIFIED without a device).
            workspace.Close();
            Check(!workspace.Active, "Assembly workspace closes");
            workspace.Open();
            var reopened = await WaitFor(() => Ctx, ct);
            Check(reopened.Occurrences.Any(item => item.CanMove), "reopened Assembly context exposes movable DOF");
            Record("PASS; reopened Assembly context and DOF");

            await CheckIsolation(backend, view, reopened.Occurrences.First(item => item.CanMove), ct);
            await CheckSyntheticControllerPaths(workspace, backend, view, designSession, previewView,
                reopened.Occurrences.First(item => item.CanMove), ct);
            NotCovered("A01-physical", "plain Grip with a real controller requires a person wearing the Quest");
            NotCovered("A02-physical", "final pose equivalence for real gestures at reduced scale requires a person wearing the Quest");
            NotCovered("A07-physical", "real face picking and unintended UI activation require controller observation");
            NotCovered("A14-physical", "tracking loss, click-through and error readability require observation in the headset");
            NotCovered("M6-Assembly-Input", "actions invoked by id through the catalog, numbers typed on the keypad entry and the Trigger-held handle drag fed as synthetic XrInput frames are SYNTHETIC; palette ergonomics, ring, chip and commit bar legibility, the raised pose and the isolation distance need a person with the controllers");
        }

        private static double ModelExtent(CadSceneView view)
        {
            var size = ScenePlacement.LocalBounds(view.transform).size;
            return Math.Max(size.x, Math.Max(size.y, size.z));
        }

        private Vector3 CenterOf(CadSceneView view, string occurrenceId)
        {
            var instance = view.Find(occurrenceId);
            return instance.transform.TransformPoint(ScenePlacement.LocalBounds(instance.transform).center);
        }

        /// <summary>M6-06: Isola moves only the Unity instance and fades the rest; Rilascia (and closing) restores. Nothing reaches Inventor.</summary>
        private async Task CheckIsolation(IAssemblyWorkspaceBackend backend, CadSceneView view, AssemblyOccurrence occurrence, CancellationToken ct)
        {
            var before = await backend.GetDocumentStateAsync(ct);
            await SelectViaList(occurrence, ct);
            var iso = _ws.Isolation;
            var home = view.Find(occurrence.Id).transform.localPosition;
            RunAction(AssemblyWorkspace.IdIsolate);
            Check(iso.Active && iso.OccurrenceId == occurrence.Id, "Isola isolates the selected component");
            iso.Snap();
            var others = view.Instances.Where(i => i.OccurrenceId != occurrence.Id).ToArray();
            Check(others.Length > 0 && others.All(i => i.Bodies.All(b => !b.Renderer.enabled)) && iso.FadedBodies > 0,
                "the rest of the assembly is replaced by 20% ghosts");
            var state = await backend.GetDocumentStateAsync(ct);
            Check(state.DocumentId == before.DocumentId && state.Revision == before.Revision && !DesignSess.CanApply
                && _ws.CommitBar.Phase == CommitBarPhase.Empty, "isolation is visual only: no draft, no CAD revision change");
            RunAction(AssemblyWorkspace.IdRelease);
            iso.Snap();
            Check(!iso.Active && view.Find(occurrence.Id).transform.localPosition == home
                && view.Instances.SelectMany(i => i.Bodies).All(b => b.Renderer.enabled),
                "Rilascia restores the position and the opacity");
            Pass("M6-06-programmatic", "Isola/Rilascia by action id: component advanced halfway, rest at 20%, restored; Inventor revision "
                + before.Revision + " unchanged");
            NotCovered("M6-06-physical", "the isolated component's distance, readability of the faded assembly and the Apri in Progettazione/Lamiera hand-off need a person (the hand-off reuses the document activation path, covered by M2)");
        }

        private void BeginSyntheticInput()
        {
            if (_syntheticInput != null) return;
            _appInput = Read<XrInput>(App, "_input");
            _syntheticSource = new SyntheticInputSource();
            _syntheticInput = gameObject.AddComponent<XrInput>();
            _syntheticInput.Source = _syntheticSource;
            Check(_syntheticInput.Synthetic, "the gesture input is flagged synthetic");
            Call(_ws, "AttachInput", _syntheticInput);
            _ws.DoubleTriggerClock = () => _doubleClock;
            Frame(true, false, false, false);
            Record("Controller gestures are fed as SYNTHETIC XrInput frames, not a person's hand");
        }

        private void EndSyntheticInput()
        {
            if (_syntheticInput == null) return;
            Frame(true, false, false, false);
            Call(_ws, "AttachInput", _appInput);
            _ws.UiHitOverride = null;
            _ws.DoubleTriggerClock = null;
            Destroy(_syntheticInput);
            _syntheticInput = null; _syntheticSource = null;
        }

        private void OnDestroy() => EndSyntheticInput();

        /// <summary>One synthetic controller frame: Trigger on the handle captures it, Grip alone moves the view.</summary>
        private void Frame(bool tracked, bool grip, bool trigger, bool ui)
        {
            var frame = new XrInputFrame { PenTracked = tracked, PenGrip = grip, PenTrigger = trigger, PaletteTracked = true };
            if (trigger && !_lastTrigger) _doubleClock += 5;   // a new press never joins the previous one into a double Trigger
            _lastTrigger = trigger;
            _ws.UiHitOverride = () => ui;   // deterministic UI hit: the real controller's ray must not decide the gesture
            _syntheticSource.Next = frame;   // the component's own Update polls the same state: no phantom release between frames
            _syntheticInput.Poll(frame, _syntheticClock += 0.016f);
            // The workspace advances a held drag in its own Update; step it now so the runner reads the value of THIS frame.
            if (ReadBoolean(_ws, "_dragging")) Call(_ws, "DragStep");
        }

        private async Task CheckSyntheticControllerPaths(AssemblyWorkspace workspace, IAssemblyWorkspaceBackend backend,
            CadSceneView view, DesignSession designSession, DesignPreviewView previewView,
            AssemblyOccurrence target, CancellationToken ct)
        {
            var ray = Read<ControllerRay>(workspace, "_ray");
            var hand = ray.Origin;
            var savedScale = view.transform.localScale;
            var savedPosition = hand.position;
            var savedRotation = hand.rotation;
            const float scale = 0.25f;
            BeginSyntheticInput();
            try
            {
                view.transform.localScale = Vector3.one * scale;
                await SelectViaList(target, ct);
                var occurrence = Occurrence;
                Check(occurrence != null && occurrence.TranslationAxes.Count > 0 && occurrence.RotationAxes.Count > 0,
                    "fixture has translation and rotation axes for reduced-scale gestures");
                var before = await backend.GetDocumentStateAsync(ct);

                RunAction(AssemblyWorkspace.IdMove);
                var axis = occurrence.TranslationAxes[0];
                var worldAxis = view.transform.TransformDirection(CadCoordinates.ToLocal(axis)).normalized;
                var point = view.transform.TransformPoint(CadCoordinates.ToLocal(occurrence.Center.Value + axis * 50));
                var side = Vector3.Cross(worldAxis, Vector3.up).normalized;
                if (side.sqrMagnitude < 0.1f) side = Vector3.Cross(worldAxis, Vector3.right).normalized;
                hand.SetPositionAndRotation(point + side * 0.15f, Quaternion.LookRotation(-side));
                Check(Read<AssemblyVisuals>(workspace, "_visuals").HitHandle(new Ray(hand.position, hand.forward)),
                    "reduced-scale translation handle can be hit");
                Frame(true, false, false, false);   // release first: presses are edge based
                Frame(true, false, true, false);
                Check(ReadBoolean(workspace, "_dragging"), "synthetic Trigger held on the handle arms reduced-scale translation");
                hand.position += worldAxis * (0.010f * scale);
                Frame(true, false, true, false);
                Check(Math.Abs(ReadValue<CadPoint>(workspace, "_translation").Dot(axis) - 10) < 0.05,
                    "reduced-scale controller displacement converts to 10 mm in CAD");
                Check(!designSession.CanApply, "Apply is disabled while the handle edits the draft");
                Frame(true, false, false, false);
                await WaitUntil(() => designSession.CanApply && previewView.IsShowing, ct);
                Check((await backend.GetDocumentStateAsync(ct)).Revision == before.Revision,
                    "reduced-scale translation preview leaves native revision unchanged");
                Pass("A02-programmatic", "synthetic 10 mm translation at 0.25x creates a rendered native preview (Trigger held on the handle, M5-08)");

                RunAction(CommitIds.Cancel);
                await SelectViaList(target, ct);
                occurrence = Occurrence;
                RunAction(AssemblyWorkspace.IdMove);
                RunAction(AssemblyWorkspace.IdMoveMode);
                var rotationAxis = occurrence.RotationAxes[0];
                var worldRotationAxis = view.transform.TransformDirection(CadCoordinates.ToLocal(rotationAxis)).normalized;
                var ring = view.GetComponentsInChildren<LineRenderer>()
                    .First(item => item.name == "DOF rotazione 1");
                point = ring.transform.TransformPoint(ring.GetPosition(0));
                hand.SetPositionAndRotation(point + worldRotationAxis * 0.1f,
                    Quaternion.LookRotation(-worldRotationAxis));
                Check(Read<AssemblyVisuals>(workspace, "_visuals").HitHandle(new Ray(hand.position, hand.forward)),
                    "reduced-scale rotation handle can be hit");
                Frame(true, false, true, false);
                Check(ReadBoolean(workspace, "_dragging"), "synthetic Trigger held on the ring arms reduced-scale rotation");
                hand.rotation = Quaternion.AngleAxis(-12, worldRotationAxis) * hand.rotation;
                Frame(true, false, true, false);
                Check(Math.Abs(ReadValue<double>(workspace, "_angle") - 12) < 0.1,
                    "reduced-scale wrist twist converts to 12 CAD degrees");
                Frame(true, false, false, false);
                await WaitUntil(() => designSession.CanApply && previewView.IsShowing, ct);
                Check((await backend.GetDocumentStateAsync(ct)).Revision == before.Revision,
                    "reduced-scale rotation preview leaves native revision unchanged");
                Pass("A02-programmatic", "synthetic 12 degree rotation at 0.25x creates a rendered native preview");

                RunAction(CommitIds.Cancel);
                await SelectViaList(target, ct);
                occurrence = Occurrence;
                RunAction(AssemblyWorkspace.IdMove);
                axis = occurrence.TranslationAxes[0];
                worldAxis = view.transform.TransformDirection(CadCoordinates.ToLocal(axis)).normalized;
                point = view.transform.TransformPoint(CadCoordinates.ToLocal(occurrence.Center.Value + axis * 50));
                side = Vector3.Cross(worldAxis, Vector3.up).normalized;
                if (side.sqrMagnitude < 0.1f) side = Vector3.Cross(worldAxis, Vector3.right).normalized;
                hand.SetPositionAndRotation(point + side * 0.15f, Quaternion.LookRotation(-side));
                Frame(true, false, true, true);
                Check(!ReadBoolean(workspace, "_dragging") && !designSession.CanApply,
                    "UI hit blocks a synthetic Trigger gesture");
                Frame(true, false, false, false);
                Frame(true, true, true, false);
                Check(!ReadBoolean(workspace, "_dragging"), "Grip held with the Trigger is view only: it never captures the handle");
                Frame(true, false, false, false);
                Frame(true, false, true, false);
                Check(ReadBoolean(workspace, "_dragging"), "gesture arms before synthetic tracking loss");
                Frame(false, false, false, false);
                Check(!ReadBoolean(workspace, "_dragging") && !designSession.CanApply
                    && !previewView.IsShowing && Read<string>(workspace, "_notice").Contains("Tracking perso"),
                    "tracking loss cancels armed gesture with readable error and no Apply");
                Check((await backend.GetDocumentStateAsync(ct)).Revision == before.Revision,
                    "UI hit and tracking loss leave native revision unchanged");
                Pass("A14-programmatic", "UI hit prevents gesture; tracking loss cancels armed gesture without CAD mutation");
                Frame(true, false, false, false);
                RunAction(CommitIds.Cancel);
                workspace.Close();   // M9: the calls the router makes (UNVERIFIED without a device)
                workspace.Open();
                await WaitFor(() => Ctx, ct);
                Check(workspace.Active && !ReadBoolean(workspace, "_dragging") && !designSession.CanApply,
                    "Assembly reopens clean after tracking loss");
                Pass("A14-programmatic", "workspace reopens with no armed gesture or Apply");
            }
            finally
            {
                EndSyntheticInput();
                view.transform.localScale = savedScale;
                hand.SetPositionAndRotation(savedPosition, savedRotation);
            }
        }
    }
}
#endif
