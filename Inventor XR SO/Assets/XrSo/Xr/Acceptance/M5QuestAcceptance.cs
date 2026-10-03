#if XR_SO_ACCEPTANCE
using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Mcp;
using InventorXrSo.Core.Ui;
using InventorXrSo.Core.Voice;
using InventorXrSo.Unity.Scene;
using InventorXrSo.Xr.Input;
using InventorXrSo.Xr.Voice;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace InventorXrSo.Xr
{
    /// <summary>Opt-in, fixture-scoped M5 (Lamiera) acceptance runner for a dedicated Quest build.</summary>
    internal sealed class M5QuestAcceptance : QuestAcceptanceRunner
    {
        internal static readonly string[] ReflectedMembers =
        {
            "AppController._inSession",
            "AppController._catalog",
            "AppController._lamiera",
            "AppController._voiceTarget",
            "AppController._input",
            "AppController.EnterSession",
            "AppController.OpenLamiera",
            "LamieraWorkspace._busy",
            "LamieraWorkspace.DragStep",
            "LamieraWorkspace.MoveGrab",
            "LamieraWorkspace.TwoHandStep",
            "LamieraWorkspace._grab",
            "LamieraWorkspace._twoHandActive",
            "LamieraWorkspace._designContext",
            "LamieraWorkspace._backend",
            "LamieraWorkspace._sheet",
            "LamieraWorkspace._state",
            "LamieraWorkspace._previewView",
            "LamieraWorkspace._flatDisplay",
            "LamieraWorkspace._notice",
            "LamieraWorkspace._pendingMutations",
            "LamieraWorkspace._ray",
            "LamieraWorkspace._view",
            "LamieraWorkspace._manip",
            "LamieraWorkspace.AttachInput",
            "DesignSession._operations",
        };

        private const string SketchName = "Taglio_M5";
        private const double FlangeHeightMm = 20;
        private const double DictatedHeightMm = 20.5;
        private const string DictatedPhrase = "venti virgola cinque";

        protected override string Milestone => "m5";
        protected override int TimeoutSeconds => 540;
        protected override string CompletionNote =>
            "physical controller input, microphone and audio were not exercised by this runner";

        private LamieraWorkspace _ws;
        private DesignSession _design;
        private IDesignWorkspaceBackend _backend;
        private ISheetMetalBackend _sheet;
        private DesignPreviewView _previewView;
        private VoiceCommandBridge _bridge;
        private InjectedRecognizer _recognizer;
        private string _flangeEdge;
        private bool _syntheticKeypadNoted;

        // Lamiera has no panel any more: every command goes through the action catalog by stable id (palette, ring and commit
        // bar share these actions) and numbers through the keypad entry. Both are SYNTHETIC input, never a controller.
        private ActionCatalog Catalog => Read<ActionCatalog>(App, "_catalog");

        /// <summary>Invokes a declared action by id through the catalog: the path of palette, ring and commit bar (synthetic tap).</summary>
        private void RunAction(string id)
        {
            var action = Catalog.Find(id);
            Check(action != null, "the action catalog has no action '" + id + "'");
            Check(action.Enabled, "action '" + id + "' is disabled: " + action.DisabledReason);
            Check(action.TryInvoke(), "action '" + id + "' did not run");
        }

        private bool ActionEnabled(string id) => Catalog.Find(id)?.Enabled == true;

        /// <summary>Chooses an entry of the open list (sketches, rules) by label.</summary>
        private void PickItem(string label, bool prefix = false)
        {
            var action = _ws.Actions.FirstOrDefault(a => a.Id.StartsWith(LamieraWorkspace.IdPickPrefix, StringComparison.Ordinal)
                && (prefix ? a.Label.StartsWith(label, StringComparison.Ordinal) : a.Label == label));
            Check(action != null, "the open list has no entry '" + label + "'");
            Check(action.Enabled && action.TryInvoke(), "list entry '" + label + "' did not run");
        }

        /// <summary>Types a value on the open keypad entry (synthetic keypad input).</summary>
        private void TypeValue(double value)
        {
            if (!_syntheticKeypadNoted) { Record("Numbers are typed on the keypad entry by the runner (synthetic keypad input)"); _syntheticKeypadNoted = true; }
            var entry = _ws.ActiveEntry;
            Check(entry != null && entry.Editing, "the numeric keypad is open");
            foreach (char c in F(value)) entry.Type(c);
            Check(entry.Commit(out var reason), "keypad accepted " + F(value) + ": " + reason);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AfterSceneLoad() => StartIfRequested<M5QuestAcceptance>("xr_m5_acceptance");

        protected override async Task Run(CancellationToken ct)
        {
            var fixture = await WaitForFixture(ct);
            Check(fixture.Graph.Root.Name.StartsWith(FixturePrefix, StringComparison.Ordinal),
                "connected document is the dedicated M5 acceptance fixture");
            Record("PASS; dedicated fixture loaded: " + fixture.Graph.Root.Name);

            // ---- M5-01: Lamiera is primary and opens through the app's own path
            if (!ReadBoolean(App, "_inSession"))
                Call(App, "EnterSession", EnvironmentMode.StudioVr);
            _ws = Read<LamieraWorkspace>(App, "_lamiera");
            Check(_ws != null, "AppController owns a Lamiera workspace");
            await WaitUntil(() => _ws.IsPrimary, ct);
            if (!_ws.Active) Call(App, "OpenLamiera");
            Check(_ws.Active, "Lamiera workspace is open");
            // The whole run uses the synthetic pen: real controllers (held, resting or untracked) must not nudge chips or arm fields.
            BeginSyntheticInput(); _holdSynthetic = true;
            _design = _ws.Session;
            _backend = Read<IDesignWorkspaceBackend>(_ws, "_backend");
            _sheet = Read<ISheetMetalBackend>(_ws, "_sheet");
            _previewView = Read<DesignPreviewView>(_ws, "_previewView");
            Check(_design != null && _backend != null && _sheet != null && _previewView != null,
                "Lamiera workspace exposes its session, backends and preview renderer");
            await WaitFor(() => !ReadBoolean(_ws, "_busy") ? Read<DesignContext>(_ws, "_designContext") : null, ct);
            await WaitUntil(() => _design.Status == DesignStatus.Empty && _ws.IsEnabled(CommandIds.Flange), ct);
            Check(_ws.Mode.IsPrimary && _ws.Mode.CanWrite, "sheet-metal mode is primary and writable");
            Pass("M5-01", "Lamiera is primary on the fixture (availability " + _ws.Mode.Availability
                + ") and the workspace opened through the app path");

            // ---- M5-02: rule and thickness bound to document id + revision
            RequireFixture();
            var initial = await _backend.GetDocumentStateAsync(ct);
            var context = _ws.Mode.Context;
            var workspaceState = Read<DocumentState>(_ws, "_state");
            Check(context != null && context.IsSheetMetal && context.IsComplete, "sheet-metal context is complete");
            Check(!string.IsNullOrWhiteSpace(context.Rule) && context.ThicknessMm.HasValue && context.ThicknessMm.Value > 0,
                "sheet-metal context carries the rule name and a positive thickness");
            Check(context.State.DocumentId == initial.DocumentId && context.State.Revision == initial.Revision
                && workspaceState.DocumentId == initial.DocumentId && workspaceState.Revision == initial.Revision,
                "sheet-metal context is bound to the native document id and revision");
            var nativeContext = await _sheet.GetSheetMetalContextAsync(initial, ct);
            Check(nativeContext.ThicknessMm.HasValue
                && Math.Abs(nativeContext.ThicknessMm.Value - context.ThicknessMm.Value) < 0.001,
                "thickness of a fresh native read equals the thickness held by the workspace context");
            Check(!context.FlatPattern.Exists && context.BendCount == 0 && context.IsSingleBody,
                "fixture starts with no flat pattern, zero bends and a single body");
            var design0 = Read<DesignContext>(_ws, "_designContext");
            Check(design0.Sketches.Any(sketch => sketch.Name == SketchName), "fixture sketch " + SketchName + " is listed");
            Pass("M5-02", "rule " + context.Rule + ", thickness " + F(context.ThicknessMm.Value) + " mm (fresh native read "
                + F(nativeContext.ThicknessMm.Value) + " mm), bound to " + initial.DocumentId + " @ " + initial.Revision
                + "; bends " + context.BendCount + ", rules available " + context.AvailableRules.Count);
            int? bends0 = context.BendCount;

            // ---- M5-03: flange preview through the numeric field path, screenshot, Cancel
            var candidates = FlangeCandidates(design0);
            Check(candidates.Length > 0, "the face exposes straight edges of 100 or 60 mm");
            string edgeId = null;
            foreach (var candidate in candidates)
            {
                if (await TryFlangePreview(candidate, FlangeHeightMm, ct)) { edgeId = candidate; break; }
                Record("Flange preview rejected on edge " + candidate + ": " + _design.Error);
                RunAction(CommitIds.Cancel);
                await WaitUntil(() => _design.Status == DesignStatus.Empty && _ws.IsEnabled(CommandIds.Flange), ct);
            }
            Check(edgeId != null, "at least one real edge of the face accepted a flange preview");
            _flangeEdge = edgeId;
            CheckFlangePreviewShown(FlangeHeightMm);
            await AssertUnchanged(initial, "flange preview", ct);
            await CaptureScreenshot("flange-preview", ct);
            Check(_ws.CommitBar.Phase == CommitBarPhase.Ready, "the commit bar offers Apply (" + _ws.CommitBar.Phase + ")");
            Pass("M5-03", "flange armed on edge " + edgeId + ", height " + F(FlangeHeightMm)
                + " mm typed on the keypad entry (synthetic); preview rendered and ready for Apply; revision " + initial.Revision + " unchanged");

            RunAction(CommitIds.Cancel);
            Check(_design.Status == DesignStatus.Empty && _design.Preview == null && !_design.CanApply && !_previewView.IsShowing,
                "Cancel discards the flange preview and clears its rendering");
            Check(_ws.Mode.Armed == SheetMetalCommand.None && _ws.Flange.EdgeIds.Count == 0,
                "Cancel disarms the flange command and clears the edge selection");
            await AssertUnchanged(initial, "flange Cancel", ct);
            Pass("M5-03", "Cancel cleared the flange draft, preview and rendering; revision unchanged");

            // ---- M5-03 (gesture path): synthetic XrInput frames (Trigger held on the knob) through the very events the controllers raise
            await CheckSyntheticFlangeGestures(edgeId, initial, ct);

            // ---- M5-04: Cut from Taglio_M5, preview, Cancel
            await WaitUntil(() => _ws.IsEnabled(CommandIds.SheetMetalCut), ct);
            RunAction(LamieraWorkspace.IdCut);
            Check(_ws.Mode.Armed == SheetMetalCommand.Cut, "Cut command is armed");
            PickItem(SketchName, prefix: true);
            Check(_design.Status != DesignStatus.Empty, "Cut draft from " + SketchName + " was accepted: " + Read<string>(_ws, "_notice"));
            await WaitUntil(() => _design.Status == DesignStatus.PreviewReady || _design.Status == DesignStatus.Error, ct);
            Check(_design.Status == DesignStatus.PreviewReady && _design.CanApply && _previewView.IsShowing,
                "Cut preview is rendered and ready: " + _design.Error);
            await AssertUnchanged(initial, "Cut preview", ct);
            RunAction(CommitIds.Cancel);
            Check(_design.Status == DesignStatus.Empty && _design.Preview == null && !_previewView.IsShowing
                && _ws.Mode.Armed == SheetMetalCommand.None, "Cancel discards the Cut preview and disarms the command");
            await AssertUnchanged(initial, "Cut Cancel", ct);
            Pass("M5-04", "Cut from " + SketchName + " previewed and cancelled; revision " + initial.Revision + " unchanged");

            // A valid Cut preview also has to commit and return to the original
            // fixture through XR Undo before the later flange and flat-pattern cases.
            RunAction(LamieraWorkspace.IdCut);
            PickItem(SketchName, prefix: true);
            await WaitUntil(() => _design.Status == DesignStatus.PreviewReady || _design.Status == DesignStatus.Error, ct);
            Check(_design.Status == DesignStatus.PreviewReady && _design.CanApply && _previewView.IsShowing,
                "second Cut preview is rendered and applicable: " + _design.Error);
            await AssertUnchanged(initial, "second Cut preview", ct);
            RequireFixture();
            RunAction(CommitIds.Apply);
            var cutCommitted = await WaitFor(async () =>
            {
                var state = await _backend.GetDocumentStateAsync(ct);
                return state.DocumentId == initial.DocumentId && state.Revision != initial.Revision ? state : null;
            }, ct);
            await WaitUntil(() => _design.Status == DesignStatus.Empty && _ws.IsEnabled(CommandIds.Undo), ct);
            Pass("M5-04", "Cut Apply changed native revision " + initial.Revision + " -> " + cutCommitted.Revision);
            RequireFixture();
            RunAction(LamieraWorkspace.IdUndo);
            var cutUndone = await WaitFor(async () =>
            {
                var state = await _backend.GetDocumentStateAsync(ct);
                return state.DocumentId == initial.DocumentId && state.Revision != cutCommitted.Revision ? state : null;
            }, ct);
            await WaitUntil(() => _design.Status == DesignStatus.Empty && _ws.IsEnabled(CommandIds.Redo), ct);
            var restoredSheet = await _sheet.GetSheetMetalContextAsync(cutUndone, ct);
            Check(restoredSheet.IsSingleBody && restoredSheet.BendCount == bends0
                && !restoredSheet.FlatPattern.Exists,
                "XR Undo of Cut restores the single-body blank, bend count and no flat pattern");
            initial = cutUndone;
            Pass("M5-04", "XR Undo restored the blank at revision " + initial.Revision);

            // ---- voice without microphone (M5-09, M5-10, M5-11)
            var voiceTarget = Read<WorkspaceVoiceTarget>(App, "_voiceTarget");
            Check(voiceTarget != null && voiceTarget.InSession && voiceTarget.AcceptsVoice,
                "app voice target is active in the session");
            _recognizer = new InjectedRecognizer();
            _bridge = new VoiceCommandBridge(voiceTarget, _recognizer, armingThreshold: TimeSpan.Zero);
            try
            {
                await RunVoice(initial, ct);
            }
            finally
            {
                _bridge.Dispose();
            }

            // ---- M5-03 (cont.): preview again, Apply, Undo, stale commit, Redo
            Check(await TryFlangePreview(edgeId, FlangeHeightMm, ct),
                "second flange preview on edge " + edgeId + " is ready: " + _design.Error);
            CheckFlangePreviewShown(FlangeHeightMm);
            await AssertUnchanged(initial, "second flange preview", ct);
            RequireFixture();
            Check(_ws.IsEnabled(CommandIds.Apply), "Apply is enabled for the rendered flange preview");
            RunAction(CommitIds.Apply);
            var committed = await WaitFor(async () =>
            {
                var state = await _backend.GetDocumentStateAsync(ct);
                return state.DocumentId == initial.DocumentId && state.Revision != initial.Revision ? state : null;
            }, ct);
            await WaitUntil(() => _design.Status == DesignStatus.Empty && _ws.IsEnabled(CommandIds.Undo), ct);
            var bendsCommitted = await NativeBends(ct);
            if (bendsCommitted.HasValue) Check(bendsCommitted.Value == 1, "committed flange gives exactly one bend, read " + bendsCommitted.Value);
            Pass("M5-03", "Apply changed revision " + initial.Revision + " -> " + committed.Revision + "; bends "
                + (bendsCommitted.HasValue ? bendsCommitted.Value.ToString(CultureInfo.InvariantCulture) : "not readable")
                + " (before " + (bends0.HasValue ? bends0.Value.ToString(CultureInfo.InvariantCulture) : "not readable") + ")");

            // A valid plan at the committed revision: Undo must make its commit stale.
            DesignPreview stale = null;
            try
            {
                stale = await _backend.PreviewDesignAsync(committed,
                    new JArray(SheetMetalOperations.Cut(SketchName)), ct);
            }
            catch (Exception ex)
            {
                Record("Stale-plan preview at the committed revision was rejected: " + ex.GetType().Name + ": " + ex.Message);
            }

            RequireFixture();
            Check(_ws.IsEnabled(CommandIds.Undo), "XR Undo is offered after Apply");
            RunAction(LamieraWorkspace.IdUndo);
            var undone = await WaitFor(async () =>
            {
                var state = await _backend.GetDocumentStateAsync(ct);
                return state.DocumentId == initial.DocumentId && state.Revision != committed.Revision ? state : null;
            }, ct);
            await WaitUntil(() => _design.Status == DesignStatus.Empty && _ws.IsEnabled(CommandIds.Redo), ct);
            var bendsUndone = await NativeBends(ct);
            if (bends0.HasValue && bendsUndone.HasValue)
                Check(bendsUndone.Value == bends0.Value, "XR Undo restores the bend count, read " + bendsUndone.Value);
            Pass("M5-03", "XR Undo moved revision " + committed.Revision + " -> " + undone.Revision
                + (undone.Revision == initial.Revision ? " (equal to the initial revision)" : " (differs from the initial revision " + initial.Revision + ")")
                + "; bends " + (bendsUndone.HasValue ? bendsUndone.Value.ToString(CultureInfo.InvariantCulture) : "not readable"));

            if (stale != null)
            {
                RequireFixture();
                bool staleRejected = false;
                try { await _backend.CommitDesignAsync(stale, ct); }
                catch (McpToolException ex) when (ex.Code == "STALE_REVISION") { staleRejected = true; }
                Check(staleRejected, "commit of a pre-Undo preview is rejected as stale");
                var afterStale = await _backend.GetDocumentStateAsync(ct);
                Check(afterStale.Revision == undone.Revision, "rejected stale commit leaves the revision unchanged");
                Pass("M5-07", "stale plan from revision " + committed.Revision + " rejected with STALE_REVISION after XR Undo");
            }
            else Record("NOT COVERED [M5-07] stale commit after Undo: no valid plan could be previewed at the committed revision");

            RequireFixture();
            Check(_ws.IsEnabled(CommandIds.Redo), "XR Redo is offered after Undo");
            RunAction(LamieraWorkspace.IdRedo);
            var redone = await WaitFor(async () =>
            {
                var state = await _backend.GetDocumentStateAsync(ct);
                return state.DocumentId == initial.DocumentId && state.Revision != undone.Revision ? state : null;
            }, ct);
            await WaitUntil(() => _design.Status == DesignStatus.Empty && _ws.IsEnabled(CommandIds.Undo), ct);
            var bendsRedone = await NativeBends(ct);
            if (bendsCommitted.HasValue && bendsRedone.HasValue)
                Check(bendsRedone.Value == bendsCommitted.Value, "XR Redo restores the committed bend count, read " + bendsRedone.Value);
            Pass("M5-03", "XR Redo moved revision " + undone.Revision + " -> " + redone.Revision + "; bends "
                + (bendsRedone.HasValue ? bendsRedone.Value.ToString(CultureInfo.InvariantCulture) : "not readable"));

            // ---- M5-05: flat pattern via the Crea sviluppo path
            await WaitUntil(() => _ws.IsEnabled(CommandIds.FlatPatternCreate), ct);
            RunAction(LamieraWorkspace.IdFlatCreate);
            Check(_ws.Mode.Armed == SheetMetalCommand.FlatPattern, "flat pattern command is armed");
            await WaitUntil(() => _design.Status == DesignStatus.PreviewReady || _design.Status == DesignStatus.Error, ct);
            Check(_design.Status == DesignStatus.PreviewReady && _design.CanApply,
                "flat pattern preview is ready: " + _design.Error);
            await AssertUnchanged(redone, "flat pattern preview", ct);
            RequireFixture();
            RunAction(CommitIds.Apply);
            var flat = _ws.FlatPattern;
            Check(flat != null, "workspace owns a flat pattern view");
            await WaitUntil(() => flat.State == FlatPatternState.Ready || flat.State == FlatPatternState.Unavailable, ct);
            Check(flat.State == FlatPatternState.Ready, "flat pattern is shown: " + flat.Reason);
            await WaitUntil(() => _design.Status == DesignStatus.Empty, ct);
            Check(flat.Asset != null && flat.Asset.Model != null && flat.Asset.Model.Primitives.Count > 0,
                "flat pattern view holds a mesh asset");
            var display = Read<FlatPatternDisplay>(_ws, "_flatDisplay");
            Check(display != null, "workspace owns a flat pattern display");
            await WaitUntil(() => display.IsShowing && display.LabelVisible, ct);
            Check(display.LabelText.Contains(FlatPatternView.Label), "flat pattern label reads \"" + FlatPatternView.Label + "\"");
            Check(!flat.IsCadSelectable && display.MeshRoot != null
                && display.MeshRoot.gameObject.layer == FlatPatternDisplay.NoRaycastLayer
                && display.MeshRoot.GetComponentInChildren<CadBody>(true) == null,
                "flat pattern is display only: no CadBody and outside the CAD raycast layer");
            var flatContext = _ws.Mode.Context;
            if (flatContext != null && flatContext.FlatPattern.Exists && flatContext.FlatPattern.BendCount.HasValue)
                Check(flatContext.FlatPattern.BendCount.Value == flat.Asset.BendCount, "flat pattern mesh and context report the same bend count");
            await CaptureScreenshot("flat-pattern", ct);
            Check(_ws.Mode.CheckFlatPattern(out _) == FlatPatternCreation.AlreadyExists && !_ws.IsEnabled(CommandIds.FlatPatternCreate),
                "an existing flat pattern is reported as AlreadyExists and Crea sviluppo is disabled");
            Pass("M5-05", "flat pattern shown: " + F(flat.Asset.LengthMm) + " x " + F(flat.Asset.WidthMm) + " mm, "
                + flat.Asset.BendCount + " bends; label \"" + FlatPatternView.Label + "\" present; existing pattern reported as AlreadyExists");

            // ---- M5-06: Detach is view state only and opens no second document
            await WaitUntil(() => _ws.IsEnabled(CommandIds.Undo), ct);
            var revisionBefore = await _backend.GetDocumentStateAsync(ct);
            var sceneBefore = Session.Scene;
            string rootBefore = sceneBefore.Graph.Root.Name, documentBefore = sceneBefore.Graph.DocumentId;
            var assetBefore = flat.Asset;
            bool sawLoading = false;
            Action watch = () => { if (flat.State == FlatPatternState.Loading) sawLoading = true; };
            flat.Changed += watch;
            try
            {
                RunAction(LamieraWorkspace.IdFlatDetach);
                Check(flat.Detached && flat.IsVisible, "flat pattern is detached and still visible");
                Check(flat.MoveLocal(0.05, 0, 0) && Math.Abs(flat.OffsetX - 0.05) < 1e-9, "detached view accepts a local offset");
                RunAction(LamieraWorkspace.IdFlatAttach);
                Check(!flat.Detached && flat.OffsetX == 0, "Attach resets the local offset");
                RunAction(LamieraWorkspace.IdFlatDetach);
            }
            finally { flat.Changed -= watch; }
            Check(!sawLoading && ReferenceEquals(flat.Asset, assetBefore) && flat.State == FlatPatternState.Ready,
                "Detach/Attach/offset never re-fetched the flat pattern (same asset, never Loading)");
            var revisionAfter = await _backend.GetDocumentStateAsync(ct);
            Check(revisionAfter.DocumentId == revisionBefore.DocumentId && revisionAfter.Revision == revisionBefore.Revision,
                "Detach leaves the Inventor revision unchanged");
            Check(Session.Scene.Graph.Root.Name == rootBefore
                && Session.Scene.Graph.DocumentId == documentBefore && Session.Scene.Graph.Root.Name.StartsWith(FixturePrefix, StringComparison.Ordinal),
                "session scene root is unchanged: no second document was opened");
            Pass("M5-06", "Detach performed no backend call (asset reused, never Loading), revision " + revisionAfter.Revision
                + " unchanged and scene root " + rootBefore + " unchanged");

            // ---- M5-06 (gesture path): synthetic Grip on the detached flat pattern
            await CheckSyntheticFlatGrab(flat, display, ct);

            NotCovered("M6-Lamiera-Input", "actions invoked by id through the catalog and numbers typed on the keypad entry are SYNTHETIC; palette ergonomics, ring, chip and commit bar legibility, and the flange handle dragged with the Trigger held is covered by the EditMode synthetic XrInput tests only (this runner feeds synthetic frames, never a hand), so ergonomics, tracking and precision feel need a person with the controllers");
            Record("NOT COVERED [M5-03-physical] real controller, real tracking, reduced-scale feel and readability of the flange manipulator: the Trigger-held knob drag and the Trigger edge pick above were synthetic XrInput frames, not a person's hand");
            Record("NOT COVERED [M5-04] Face and rule/thickness commands: Cut preview, Cancel, Apply and Undo run here");
            Record("NOT COVERED [M5-05] flat pattern outcomes on multi-body or non-unfoldable parts: only AlreadyExists is asserted");
            Record("NOT COVERED [M5-06-physical] real Grip grab of the detached pattern and visual comparison with the folded part: the grab above was a synthetic frame, orientation and legibility need a person wearing the Quest");
            Record("NOT COVERED [M5-07] network loss, late preview and uncertain commit: only the stale commit after Undo is asserted");
            Record("NOT COVERED [M5-08] push-to-talk button, microphone and audio: recognized text was injected into a PushToTalkController with silent samples, MicrophoneCapture and PushToTalkInput were not started");
            Record("NOT COVERED [M5-12] physical journey on Quest with Inventor in the real use environment and M1-M4 regressions");

            // ---- clean end
            if (ActionEnabled(CommitIds.Cancel)) RunAction(CommitIds.Cancel);
            await WaitUntil(() => _design.Status == DesignStatus.Empty, ct);
            flat.Hide();
            Check(flat.State == FlatPatternState.Hidden && !display.IsShowing, "flat pattern is hidden");
            _ws.Close();
            Check(!_ws.Active, "Lamiera workspace closes");
            Record("Clean end: command cancelled, flat pattern hidden, Lamiera closed");
        }

        // ---------------------------------------------------------------- synthetic controller gestures (M5-03, M5-06)

        /// <summary>One synthetic controller frame, called "synthetic" everywhere: never physical evidence.</summary>
        private XrInput _appInput, _syntheticInput;
        private SyntheticInputSource _syntheticSource;
        private float _syntheticClock;

        /// <summary>
        /// The workspace listens to a synthetic <see cref="XrInput"/> for the duration of the gesture checks (the application's
        /// real input is detached, then restored): the same semantic events the controllers raise, SYNTHETIC in every log.
        /// </summary>
        private bool _holdSynthetic;

        private void OnDestroy() { _holdSynthetic = false; EndSyntheticInput(); }

        private void BeginSyntheticInput()
        {
            if (_syntheticInput != null) return;
            _appInput = Read<XrInput>(App, "_input");
            _syntheticSource = new SyntheticInputSource();
            _syntheticInput = gameObject.AddComponent<XrInput>();
            _syntheticInput.Source = _syntheticSource;
            Check(_syntheticInput.Synthetic, "the gesture input is flagged synthetic");
            Call(_ws, "AttachInput", _syntheticInput);
            Frame(true, false, false, false, false, false);
        }

        private void EndSyntheticInput()
        {
            if (_syntheticInput == null || _holdSynthetic) return;
            Frame(true, false, false, false, false, false);
            Call(_ws, "AttachInput", _appInput);
            _ws.UiHitOverride = null;
            Destroy(_syntheticInput);
            _syntheticInput = null; _syntheticSource = null;
        }

        /// <summary>
        /// One synthetic controller frame. Only the held states matter (Trigger on the knob captures it, Grip alone moves the view);
        /// the edge flags and the UI flag of the old Grip+Trigger path are ignored.
        /// </summary>
        private void Frame(bool tracked, bool grip, bool trigger, bool gripDown, bool triggerDown, bool ui)
        {
            var frame = new XrInputFrame { PenTracked = tracked, PenGrip = grip, PenTrigger = trigger, PaletteTracked = true };
            _ws.UiHitOverride = () => ui;   // deterministic UI hit: the real controller's ray must not decide the gesture
            _syntheticSource.Next = frame;   // the component's own Update polls the same state: no phantom release between frames
            _syntheticInput.Poll(frame, _syntheticClock += 0.016f);
            // The workspace advances a held drag in its own Update; step it now so the runner reads the height of THIS frame (no real-controller pose in between).
            if (Read<FlangeManipulator>(_ws, "_manip").Dragging) Call(_ws, "DragStep");
            else if (ReadBoolean(_ws, "_twoHandActive")) Call(_ws, "TwoHandStep");
            else if (Read<Transform>(_ws, "_grab") != null) Call(_ws, "MoveGrab");
        }

        private static Vector3 AxisWorld(FlangeManipulator manip, Transform model)
            => model.TransformDirection(CadCoordinates.ToLocal(manip.Axis)).normalized;

        /// <summary>Puts the ray origin beside the knob, looking at it (within the knob pick radius).</summary>
        private static void AimAtKnob(FlangeManipulator manip, Transform model, Transform hand)
        {
            var axis = AxisWorld(manip, model);
            var side = Vector3.Cross(axis, Vector3.up).normalized;
            if (side.sqrMagnitude < 0.1f) side = Vector3.Cross(axis, Vector3.right).normalized;
            hand.SetPositionAndRotation(manip.KnobWorldPosition + side * 0.15f, Quaternion.LookRotation(-side));
        }

        /// <summary>Moves the hand by the physical distance that equals <paramref name="cadMm"/> CAD mm at the current view scale.</summary>
        private static void MoveAlongAxis(FlangeManipulator manip, Transform model, Transform hand, double cadMm)
            => hand.position += AxisWorld(manip, model) * (float)(cadMm * 0.001 * model.lossyScale.x);

        private async Task WaitPreviewReady(CancellationToken ct)
        {
            await WaitUntil(() => _design.Status == DesignStatus.PreviewReady || _design.Status == DesignStatus.Error, ct);
            Check(_design.Status == DesignStatus.PreviewReady && _design.CanApply && _previewView.IsShowing,
                "a new rendered native preview follows the gesture: " + _design.Error);
        }

        private async Task CancelFlangeDraft(CancellationToken ct)
        {
            RunAction(CommitIds.Cancel);
            await WaitUntil(() => _design.Status == DesignStatus.Empty && _ws.IsEnabled(CommandIds.Flange), ct);
        }

        private async Task CheckSyntheticFlangeGestures(string edgeId, DocumentState baseline, CancellationToken ct)
        {
            var hand = Read<ControllerRay>(_ws, "_ray").Origin;
            var model = Read<CadSceneView>(_ws, "_view").transform;
            var manip = Read<FlangeManipulator>(_ws, "_manip");
            var savedHandPosition = hand.position; var savedHandRotation = hand.rotation;
            var savedPosition = model.position; var savedRotation = model.rotation; var savedScale = model.localScale;
            const double DragMm = 10;
            BeginSyntheticInput();
            try
            {
                var reached = new double[2];
                var scales = new[] { 1f, 0.25f };
                for (int i = 0; i < scales.Length; i++)
                {
                    string label = scales[i] == 1f ? "1:1" : "0.25x";
                    model.localScale = Vector3.one * scales[i];
                    Check(await TryFlangePreview(edgeId, FlangeHeightMm, ct), "flange preview before the " + label + " synthetic gesture: " + _design.Error);
                    Check(manip.Visible, "flange knob is visible at " + label);
                    Record("diag " + "before drag" + ": angle " + F(_ws.Flange.AngleDegrees) + " height " + F(_ws.Flange.HeightMm) + " v" + _ws.Flange.Version + " armed " + _ws.ArmedField?.Id);
                    AimAtKnob(manip, model, hand);
                    Frame(true, false, true, false, true, false);
                    Record("diag " + "after press" + ": angle " + F(_ws.Flange.AngleDegrees) + " height " + F(_ws.Flange.HeightMm) + " v" + _ws.Flange.Version + " armed " + _ws.ArmedField?.Id);
                    Check(manip.Dragging, "synthetic Trigger held on the knob starts a drag at " + label);
                    MoveAlongAxis(manip, model, hand, DragMm);
                    Frame(true, false, true, false, false, false);
                    reached[i] = _ws.Flange.HeightMm;
                    Check(Math.Abs(reached[i] - (FlangeHeightMm + DragMm)) < 0.05,
                        "synthetic controller displacement at " + label + " converts to +" + F(DragMm) + " mm, height " + F(reached[i]));
                    Check(!_design.CanApply, "Apply is disabled while the knob edits the draft");
                    Record("diag " + "before release" + ": angle " + F(_ws.Flange.AngleDegrees) + " height " + F(_ws.Flange.HeightMm) + " v" + _ws.Flange.Version + " armed " + _ws.ArmedField?.Id);
                    Frame(true, false, false, false, false, false);
                    Record("diag " + "after release" + ": angle " + F(_ws.Flange.AngleDegrees) + " height " + F(_ws.Flange.HeightMm) + " v" + _ws.Flange.Version + " armed " + _ws.ArmedField?.Id);
                    Check(!manip.Dragging, "releasing the Trigger ends the drag");
                    await WaitPreviewReady(ct);
                    CheckFlangePreviewShown(FlangeHeightMm + DragMm);
                    await AssertUnchanged(baseline, "synthetic knob drag at " + label, ct);
                    if (i == 0)
                        Pass("M5-03-programmatic", "synthetic Trigger held on the flange knob at scale 1:1 changed the height " + F(FlangeHeightMm)
                            + " -> " + F(reached[i]) + " mm (+" + F(DragMm) + " within 0.05); rendered native preview ready, revision "
                            + baseline.Revision + " unchanged until Apply");
                    else
                        Pass("M5-03-programmatic", "synthetic Trigger held at scale 0.25x: the same physical displacement converted to the same CAD height "
                            + F(reached[i]) + " mm as at 1:1 (" + F(reached[0]) + " mm, within 0.05); rendered native preview ready, revision unchanged");
                    await CancelFlangeDraft(ct);
                }
                Check(Math.Abs(reached[0] - reached[1]) < 0.05, "1:1 and 0.25x gestures give equivalent CAD millimetres");

                // Plain Grip on the knob never writes the draft (it only moves the view, restored below).
                model.localScale = Vector3.one * scales[0];
                Check(await TryFlangePreview(edgeId, FlangeHeightMm, ct), "flange preview before the synthetic plain Grip: " + _design.Error);
                string plan = _design.Preview.PlanId; int version = _ws.Flange.Version;
                AimAtKnob(manip, model, hand);
                Frame(true, false, false, false, false, false);   // release first: grab starts are edge based
                Frame(true, true, false, true, false, false);
                MoveAlongAxis(manip, model, hand, DragMm);
                Frame(true, true, false, false, false, false);
                Check(!manip.Dragging && Math.Abs(_ws.Flange.HeightMm - FlangeHeightMm) < 1e-9 && _ws.Flange.Version == version,
                    "synthetic plain Grip left the flange draft untouched");
                Check(_design.Status == DesignStatus.PreviewReady && _design.Preview.PlanId == plan && _design.CanApply && _previewView.IsShowing,
                    "synthetic plain Grip left the rendered preview in place");
                Frame(true, false, false, false, false, false);
                model.SetPositionAndRotation(savedPosition, savedRotation);
                CheckFlangePreviewShown(FlangeHeightMm);
                await AssertUnchanged(baseline, "synthetic plain Grip", ct);
                Pass("M5-03-programmatic", "synthetic plain Grip (no Trigger) on the knob left the flange draft " + F(FlangeHeightMm)
                    + " mm, its preview and the native revision " + baseline.Revision + " unchanged");
                await CancelFlangeDraft(ct);

                // Release of the Trigger closes the capture with a preview request; a tracking loss closes it WITHOUT one (last valid height kept). Neither commits.
                // (M5-08 is the push-to-talk microphone gate; the manipulator drag belongs to M5-03.)
                Check(await TryFlangePreview(edgeId, FlangeHeightMm, ct), "flange preview before the synthetic release: " + _design.Error);
                AimAtKnob(manip, model, hand);
                Frame(true, false, true, false, true, false);
                Check(manip.Dragging, "drag armed before the synthetic Trigger release");
                MoveAlongAxis(manip, model, hand, DragMm);
                Frame(true, false, true, false, false, false);
                Frame(true, false, false, false, false, false);
                Check(!manip.Dragging, "releasing the Trigger closes the drag");
                MoveAlongAxis(manip, model, hand, 20);
                Frame(true, false, false, false, false, false);
                Check(Math.Abs(_ws.Flange.HeightMm - (FlangeHeightMm + DragMm)) < 0.05, "motion after the release does not change the height");
                Frame(true, false, false, false, false, false);
                model.SetPositionAndRotation(savedPosition, savedRotation);
                await WaitPreviewReady(ct);
                CheckFlangePreviewShown(FlangeHeightMm + DragMm);
                await AssertUnchanged(baseline, "synthetic Trigger release", ct);
                Pass("M5-03-programmatic", "synthetic Trigger release closed the drag at " + F(FlangeHeightMm + DragMm)
                    + " mm, asked for a preview and made no CAD mutation (revision " + baseline.Revision + ")");
                await CancelFlangeDraft(ct);

                Check(await TryFlangePreview(edgeId, FlangeHeightMm, ct), "flange preview before the synthetic tracking loss: " + _design.Error);
                AimAtKnob(manip, model, hand);
                Frame(true, false, true, false, true, false);
                Check(manip.Dragging, "drag armed before the synthetic tracking loss");
                MoveAlongAxis(manip, model, hand, 7);
                Frame(true, false, true, false, false, false);
                MoveAlongAxis(manip, model, hand, 500); // pose reported while tracking is lost must not reach the draft
                Frame(false, false, false, false, false, false);
                Check(!manip.Dragging && Math.Abs(_ws.Flange.HeightMm - (FlangeHeightMm + 7)) < 0.05,
                    "tracking loss closes the drag and keeps the last valid height " + F(_ws.Flange.HeightMm));
                Frame(false, false, true, false, true, false);
                Check(!manip.Dragging, "no drag can start while untracked");
                Frame(true, false, false, false, false, false);
                Check(_design.Status == DesignStatus.Draft && !_design.CanApply && !_previewView.IsShowing,
                    "a tracking loss is not a release: the draft keeps the last valid height and no preview was requested (status "
                    + _design.Status + ", CanApply " + _design.CanApply + ", showing " + _previewView.IsShowing + ", height " + F(_ws.Flange.HeightMm) + ")");
                await AssertUnchanged(baseline, "synthetic tracking loss", ct);
                Pass("M5-03-programmatic", "synthetic tracking loss closed the drag at the last valid " + F(FlangeHeightMm + 7)
                    + " mm, requested no preview (Apply stays off) and made no CAD mutation (revision " + baseline.Revision + ")");
                await CancelFlangeDraft(ct);

                // Trigger ray on the real edge, from the outward face normal so nothing hides it.
                model.localScale = Vector3.one * scales[0];
                await WaitUntil(() => _ws.IsEnabled(CommandIds.Flange), ct);
                RunAction(LamieraWorkspace.IdFlange);
                var design = Read<DesignContext>(_ws, "_designContext");
                var edge = design.Edges.First(item => item.Id == edgeId);
                bool framed = FlangeManipulator.TryFrame(edge, design.Faces, _ws.Mode.Context?.ThicknessMm, out var origin, out var normal, out bool fromFaces);
                if (framed && fromFaces)
                {
                    var point = model.TransformPoint(CadCoordinates.ToLocal(origin));
                    var away = model.TransformDirection(CadCoordinates.ToLocal(normal)).normalized;
                    hand.SetPositionAndRotation(point + away * 0.3f, Quaternion.LookRotation(-away));
                    Frame(true, false, false, false, false, false);   // a press needs a release before it: the events are edge based
                    Frame(true, false, true, false, true, false);
                    Frame(true, false, false, false, false, false);
                    Record("diag edge pick: " + _ws.Flange.EdgeIds.Count + " edge(s) after the first Trigger ray, armed " + _ws.Mode.Armed + ", dragging " + manip.Dragging);
                    if (_ws.Flange.EdgeIds.Count == 1 && _ws.Flange.EdgeIds[0] == edgeId)
                    {
                        // The knob now takes a Trigger aimed within its pick radius (M6): aim the second press at a point of the same edge outside it.
                        var points = edge.PointsMm; bool aimed = false;
                        for (int k = 1; k <= 9 && !aimed; k++)
                        {
                            var spot = points[0] + (points[points.Count - 1] - points[0]) * (k / 10.0);
                            hand.SetPositionAndRotation(model.TransformPoint(CadCoordinates.ToLocal(spot)) + away * 0.3f, Quaternion.LookRotation(-away));
                            var probe = new Ray(hand.position, hand.forward);
                            aimed = !manip.IsOverKnob(probe) && CadCoordinates.PickEdge(model, design.Edges, probe, requireVisible: true) == edgeId;
                        }
                        if (!aimed) { Record("NOT COVERED [M5-03-programmatic] second Trigger ray off the knob: no point of edge " + edgeId + " lies outside the knob pick radius"); await CancelFlangeDraft(ct); return; }
                        Frame(true, false, true, false, true, false);
                        Frame(true, false, false, false, false, false);
                        Check(_ws.Flange.EdgeIds.Count == 0, "a second synthetic Trigger ray on the same edge deselects it");
                        await AssertUnchanged(baseline, "synthetic Trigger edge pick", ct);
                        Pass("M5-03-programmatic", "synthetic Trigger ray on real edge " + edgeId + " toggled it in the flange draft (selected, then deselected); revision unchanged");
                    }
                    else Record("NOT COVERED [M5-03-programmatic] synthetic Trigger ray on edge " + edgeId + " selected nothing (edge hidden or off the ray): pick by ray not asserted");
                }
                else Record("NOT COVERED [M5-03-programmatic] synthetic Trigger edge pick: no face-derived frame for edge " + edgeId + " to aim from");
                await CancelFlangeDraft(ct);
            }
            finally
            {
                EndSyntheticInput();
                model.localScale = savedScale;
                model.SetPositionAndRotation(savedPosition, savedRotation);
                hand.SetPositionAndRotation(savedHandPosition, savedHandRotation);
            }
        }

        private async Task CheckSyntheticFlatGrab(FlatPatternView flat, FlatPatternDisplay display, CancellationToken ct)
        {
            var hand = Read<ControllerRay>(_ws, "_ray").Origin;
            var model = Read<CadSceneView>(_ws, "_view").transform;
            var savedHandPosition = hand.position; var savedHandRotation = hand.rotation;
            var savedPosition = model.position; var savedRotation = model.rotation; var savedScale = model.localScale;
            double offsetX = flat.OffsetX, offsetY = flat.OffsetY, offsetZ = flat.OffsetZ;
            BeginSyntheticInput();
            try
            {
                Check(flat.Detached && flat.IsVisible && display.IsShowing && display.MeshRoot != null,
                    "flat pattern is detached and shown before the synthetic grab");
                var baseline = await _backend.GetDocumentStateAsync(ct);
                var asset = flat.Asset; bool sawLoading = false;
                Action watch = () => { if (flat.State == FlatPatternState.Loading) sawLoading = true; };
                flat.Changed += watch;
                try
                {
                    var center = model.TransformPoint(display.LocalBounds.center);
                    hand.SetPositionAndRotation(center + Vector3.back * 0.5f, Quaternion.identity);
                    Check(display.HitTest(new Ray(hand.position, hand.forward)), "the synthetic ray hits the flat pattern");
                    display.Snap();
                    var rootBefore = display.MeshRoot.position;   // world: on the work plane the mesh root no longer lives in the model frame
                    var delta = new Vector3(0.05f, 0.02f, 0f);
                    Frame(true, false, false, false, false, false);   // release first: grab starts are edge based
                    Frame(true, true, false, true, false, false);
                    hand.position += delta;
                    Frame(true, true, false, false, false, false);
                    display.Snap();   // the plane placement eases in over ~250 ms: read the settled pose
                    var expected = rootBefore + delta;
                    Check(Vector3.Distance(display.MeshRoot.position, expected) < 1e-3f,
                        "synthetic Grip moved the flat mesh root by the hand displacement");
                    Check(Vector3.Distance(model.position, savedPosition) < 1e-5f && Quaternion.Angle(model.rotation, savedRotation) < 1e-3f
                        && Vector3.Distance(model.localScale, savedScale) < 1e-6f, "the model view transform is unchanged");
                    Frame(true, false, false, false, false, false);
                    var moved = display.MeshRoot.position;
                    hand.position += delta; Frame(true, false, false, false, false, false);
                    display.Snap();
                    Check(Vector3.Distance(display.MeshRoot.position, moved) < 1e-6f, "releasing Grip stops the flat movement");

                    flat.Attach(); display.Snap();
                    var attached = display.MeshRoot.position;
                    hand.SetPositionAndRotation(model.TransformPoint(display.LocalBounds.center) + Vector3.back * 0.5f, Quaternion.identity);
                    Frame(true, false, false, false, false, false);   // release first: grab starts are edge based
                    Frame(true, true, false, true, false, false);
                    hand.position += delta;
                    Frame(true, true, false, false, false, false);
                    Frame(true, false, false, false, false, false);
                    display.Snap();
                    Check(Vector3.Distance(display.MeshRoot.position, attached) < 1e-6f && flat.OffsetX == 0,
                        "an attached flat pattern is not grabbed");
                }
                finally { flat.Changed -= watch; }
                Check(!sawLoading && ReferenceEquals(flat.Asset, asset) && flat.State == FlatPatternState.Ready,
                    "the synthetic grabs never re-fetched the flat pattern (same asset, never Loading)");
                var after = await _backend.GetDocumentStateAsync(ct);
                Check(after.DocumentId == baseline.DocumentId && after.Revision == baseline.Revision, "synthetic grabs leave the native revision unchanged");
                Pass("M5-06-programmatic", "synthetic Grip on the detached flat pattern moved only the flat mesh root (view state): no backend call (asset reused, never Loading), "
                    + "revision " + after.Revision + " unchanged, model view transform unchanged; an attached pattern is not grabbed");
            }
            finally
            {
                EndSyntheticInput();
                model.localScale = savedScale;
                model.SetPositionAndRotation(savedPosition, savedRotation);
                hand.SetPositionAndRotation(savedHandPosition, savedHandRotation);
                if (!flat.Detached) flat.Detach();
                flat.MoveLocal(offsetX, offsetY, offsetZ);
            }
        }

        private async Task RunVoice(DocumentState baseline, CancellationToken ct)
        {
            // M5-10: dictation with the flange height field armed changes only that field.
            await WaitUntil(() => _design.Status == DesignStatus.Empty && _ws.IsEnabled(CommandIds.Flange), ct);
            RunAction(LamieraWorkspace.IdFlange);
            _ws.Flange.ToggleEdge(_flangeEdge);
            Check(_ws.TryArmField(LamieraWorkspace.FieldFlangeHeight), "flange height field is armed: " + _ws.LastFieldError);
            double heightBefore = _ws.Flange.HeightMm, angleBefore = _ws.Flange.AngleDegrees;
            string datumBefore = _ws.Flange.Datum;
            var edgesBefore = _ws.Flange.EdgeIds.ToArray();
            Check(Math.Abs(heightBefore - DictatedHeightMm) > 0.01, "dictated value differs from the current height");

            await Speak(DictatedPhrase, ct);
            Check(_bridge.Outcome == VoiceOutcomeKind.DictationProposed, "dictation is proposed, outcome " + _bridge.Outcome);
            var proposal = _bridge.Dictation.Pending;
            Check(proposal != null && proposal.Accepted && proposal.FieldId == LamieraWorkspace.FieldFlangeHeight
                && Math.Abs(proposal.Value - DictatedHeightMm) < 1e-9, "dictation proposal is " + F(DictatedHeightMm) + " mm for the height field");
            Check(Math.Abs(_ws.Flange.HeightMm - heightBefore) < 1e-9, "the proposal alone does not change the draft");
            Check(_bridge.ConfirmPendingAny(), "physical Conferma path applies the dictated value");
            Check(Math.Abs(_ws.Flange.HeightMm - DictatedHeightMm) < 1e-9, "flange height is now the dictated value");
            Check(Math.Abs(_ws.Flange.AngleDegrees - angleBefore) < 1e-9 && _ws.Flange.Datum == datumBefore
                && _ws.Flange.EdgeIds.SequenceEqual(edgesBefore), "angle, datum and edges are untouched");
            Check(!_design.CanApply, "Apply is not available before the new preview is rendered");
            await WaitUntil(() => _design.Status == DesignStatus.PreviewReady || _design.Status == DesignStatus.Error, ct);
            Check(_design.Status == DesignStatus.PreviewReady && _design.CanApply && _previewView.IsShowing,
                "the dictated height produced a new rendered preview: " + _design.Error);
            CheckFlangePreviewShown(DictatedHeightMm);
            await AssertUnchanged(baseline, "dictated flange preview", ct);
            Pass("M5-10", "\"" + DictatedPhrase + "\" proposed " + F(DictatedHeightMm) + " mm; after physical confirmation only the height changed ("
                + F(heightBefore) + " -> " + F(_ws.Flange.HeightMm) + " mm) and Apply required the new preview");

            // M5-11: a valid preview exists; spoken "applica" only opens the confirmation.
            _ws.DisarmField();
            var planBefore = _design.Preview.PlanId;
            await Speak("applica", ct);
            Check(_bridge.Outcome == VoiceOutcomeKind.ApplyConfirmationShown, "spoken Applica only shows the confirmation, outcome " + _bridge.Outcome);
            Check(_design.Status == DesignStatus.PreviewReady && _design.CanApply && _design.Preview.PlanId == planBefore
                && ReadValue<int>(_ws, "_pendingMutations") == 0, "spoken Applica started no commit and left the preview in place");
            string notice = Read<string>(_ws, "_notice") ?? "";
            Check(notice.Contains("Applica"), "workspace notice asks for the physical Applica button");
            await AssertUnchanged(baseline, "spoken Applica", ct);
            Pass("M5-11", "spoken \"applica\" with a valid preview opened only the confirmation; revision " + baseline.Revision + " unchanged");

            // M5-09: a command disabled in the current state and an unknown phrase do not mutate anything.
            Check(!_ws.IsEnabled(CommandIds.Fillet), "Fillet is disabled in Lamiera");
            int versionBefore = _ws.Flange.Version;
            await Speak("raccordo", ct);
            Check(_bridge.Outcome == VoiceOutcomeKind.NotExecuted, "disabled command is not executed, outcome " + _bridge.Outcome);
            await Speak("vorrei una pizza", ct);
            Check(_bridge.Outcome == VoiceOutcomeKind.Rejected, "phrase outside the vocabulary is rejected, outcome " + _bridge.Outcome);
            Check(_design.Status == DesignStatus.PreviewReady && _design.Preview.PlanId == planBefore
                && _ws.Flange.Version == versionBefore && ReadValue<int>(_ws, "_pendingMutations") == 0,
                "disabled and unknown phrases left draft and preview untouched");
            await AssertUnchanged(baseline, "disabled voice command", ct);
            Pass("M5-09", "disabled command \"raccordo\" and an out-of-vocabulary phrase changed nothing; revision " + baseline.Revision + " unchanged");

            RunAction(CommitIds.Cancel);
            Check(_design.Status == DesignStatus.Empty && !_previewView.IsShowing, "voice section ends with the draft cancelled");
            await WaitUntil(() => _ws.IsEnabled(CommandIds.Flange), ct);
        }

        /// <summary>Pushes recognized text through the real push-to-talk controller, router and bridge with silent samples.</summary>
        private async Task Speak(string text, CancellationToken ct)
        {
            _bridge.Pump();
            _recognizer.Text = text;
            var talk = _bridge.Controller;
            talk.Press();
            Check(talk.State == PushToTalkState.Pressed, "push-to-talk controller accepted the injected press");
            talk.Tick();
            Check(talk.State == PushToTalkState.Listening, "push-to-talk controller entered Listening");
            var silence = new short[PushToTalkController.SampleRate / 2];
            talk.AppendAudio(silence, silence.Length);
            talk.Release();
            await WaitUntil(() => talk.State == PushToTalkState.Result || talk.State == PushToTalkState.Error, ct);
            Check(talk.State == PushToTalkState.Result, "injected recognition result was delivered: " + talk.ErrorMessage);
            Check(!talk.MicrophoneOpen, "no microphone capture is open");
            _bridge.Pump();
        }

        private async Task<bool> TryFlangePreview(string edgeId, double heightMm, CancellationToken ct)
        {
            await StepWait("flange enabled", () => _ws.IsEnabled(CommandIds.Flange), ct);
            RunAction(LamieraWorkspace.IdFlange);
            Check(_ws.Mode.Armed == SheetMetalCommand.Flange, "flange command is armed");
            _ws.Flange.ToggleEdge(edgeId);
            Check(_ws.Flange.EdgeIds.Count == 1 && _ws.Flange.EdgeIds[0] == edgeId, "flange draft holds the chosen edge");
            RunAction(LamieraWorkspace.IdFlangeHeight);
            Check(_ws.ArmedField?.Id == LamieraWorkspace.FieldFlangeHeight, "flange height field is armed on the keypad");
            TypeValue(heightMm);
            await StepWait("flange preview", () => _design.Status == DesignStatus.PreviewReady || _design.Status == DesignStatus.Error, ct);
            return _design.Status == DesignStatus.PreviewReady;
        }

        /// <summary>WaitUntil that records the workspace state once if the condition takes more than 8 s (diagnostic for stalls).</summary>
        private async Task StepWait(string what, Func<bool> condition, CancellationToken ct)
        {
            var started = DateTime.UtcNow; bool logged = false;
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                if (condition()) return;
                if (!logged && (DateTime.UtcNow - started).TotalSeconds > 8)
                {
                    logged = true;
                    Record("diag stall waiting for " + what + ": status " + _design.Status + " armed " + _ws.Mode.Armed + " field " + _ws.ArmedField?.Id
                        + " active " + _ws.Active + " busy " + ReadBoolean(_ws, "_busy")
                        + " error " + _design.Error);
                }
                await Task.Delay(100, ct);
            }
        }

        private void CheckFlangePreviewShown(double heightMm)
        {
            Check(_design.Status == DesignStatus.PreviewReady && _design.CanApply && _previewView.IsShowing
                && _previewView.PlanId == _design.Preview.PlanId, "flange preview is rendered and enabled for Apply");
            var operations = Read<JArray>(_design, "_operations");
            Check(operations != null && operations.Count == 1 && (string)operations[0]["command"] == "sheet_metal_flange"
                && Math.Abs((double)operations[0]["arguments"]["height_mm"] - heightMm) < 1e-9
                && Math.Abs((double)operations[0]["arguments"]["angle_degrees"] - 90) < 1e-9,
                "previewed draft is one flange of " + F(heightMm) + " mm at 90 degrees (found "
                    + (operations == null ? "no operations" : operations.Count + " op(s): " + operations.ToString(Newtonsoft.Json.Formatting.None)) + ")");
        }

        private async Task AssertUnchanged(DocumentState baseline, string what, CancellationToken ct)
        {
            var now = await _backend.GetDocumentStateAsync(ct);
            Check(now.DocumentId == baseline.DocumentId && now.Revision == baseline.Revision,
                what + " leaves Inventor revision unchanged (" + baseline.Revision + " vs " + now.Revision + ")");
        }

        private async Task<int?> NativeBends(CancellationToken ct)
        {
            var state = await _backend.GetDocumentStateAsync(ct);
            var read = await _sheet.GetSheetMetalContextAsync(state, ct);
            return read.BendCount;
        }

        private static string[] FlangeCandidates(DesignContext context)
        {
            var lines = context.Edges
                .Where(edge => edge.Kind != null && edge.Kind.IndexOf("line", StringComparison.OrdinalIgnoreCase) >= 0)
                .ToArray();
            if (lines.Length == 0) lines = context.Edges.ToArray();
            return lines
                .Select(edge => new { edge.Id, Length = EdgeLength(edge) })
                .Where(item => Math.Abs(item.Length - 100) < 0.5 || Math.Abs(item.Length - 60) < 0.5)
                .OrderBy(item => Math.Abs(item.Length - 100) < 0.5 ? 0 : 1)
                .Select(item => item.Id)
                .Take(6)
                .ToArray();
        }

        private static double EdgeLength(DesignEdge edge)
        {
            double length = 0;
            for (int i = 1; i < edge.PointsMm.Count; i++) length += (edge.PointsMm[i] - edge.PointsMm[i - 1]).Length;
            return length;
        }

        private static string F(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

        private sealed class InjectedRecognizer : ISpeechRecognizer
        {
            public string Text;
            public Task<string> RecognizeAsync(short[] pcm16, CancellationToken cancellationToken) => Task.FromResult(Text);
        }
    }
}
#endif
