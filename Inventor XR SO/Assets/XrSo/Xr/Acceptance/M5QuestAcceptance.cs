#if XR_SO_ACCEPTANCE
using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Mcp;
using InventorXrSo.Core.Voice;
using InventorXrSo.Unity.Scene;
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
            "AppController._lamiera",
            "AppController._voiceTarget",
            "AppController.EnterSession",
            "AppController.OpenLamiera",
            "LamieraWorkspace._busy",
            "LamieraWorkspace._designContext",
            "LamieraWorkspace._backend",
            "LamieraWorkspace._sheet",
            "LamieraWorkspace._state",
            "LamieraWorkspace._previewView",
            "LamieraWorkspace._flatDisplay",
            "LamieraWorkspace._notice",
            "LamieraWorkspace._pendingMutations",
            "LamieraWorkspace.ArmFlange",
            "LamieraWorkspace.CancelCommand",
            "LamieraWorkspace.OpenSketchPick",
            "LamieraWorkspace.ChooseSketch",
            "LamieraWorkspace.StartFlatPattern",
            "LamieraWorkspace.ApplyPressed",
            "LamieraWorkspace.ApplyHistory",
            "DesignSession._operations",
        };

        private const string SketchName = "Taglio_M5";
        private const double FlangeHeightMm = 20;
        private const double DictatedHeightMm = 20.5;
        private const string DictatedPhrase = "venti virgola cinque";

        protected override string Milestone => "m5";
        protected override int TimeoutSeconds => 420;
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
                Call(_ws, "CancelCommand");
                await WaitUntil(() => _design.Status == DesignStatus.Empty && _ws.IsEnabled(CommandIds.Flange), ct);
            }
            Check(edgeId != null, "at least one real edge of the face accepted a flange preview");
            _flangeEdge = edgeId;
            CheckFlangePreviewShown(FlangeHeightMm);
            await AssertUnchanged(initial, "flange preview", ct);
            await CaptureScreenshot("flange-preview", ct);
            Pass("M5-03", "flange armed on edge " + edgeId + ", height " + F(FlangeHeightMm)
                + " mm set through SetArmedField; preview rendered and ready for Apply; revision " + initial.Revision + " unchanged");

            Call(_ws, "CancelCommand");
            Check(_design.Status == DesignStatus.Empty && _design.Preview == null && !_design.CanApply && !_previewView.IsShowing,
                "Cancel discards the flange preview and clears its rendering");
            Check(_ws.Mode.Armed == SheetMetalCommand.None && _ws.Flange.EdgeIds.Count == 0,
                "Cancel disarms the flange command and clears the edge selection");
            await AssertUnchanged(initial, "flange Cancel", ct);
            Pass("M5-03", "Cancel cleared the flange draft, preview and rendering; revision unchanged");

            // ---- M5-04: Cut from Taglio_M5, preview, Cancel
            await WaitUntil(() => _ws.IsEnabled(CommandIds.SheetMetalCut), ct);
            Call(_ws, "OpenSketchPick", SheetMetalCommand.Cut);
            Check(_ws.Mode.Armed == SheetMetalCommand.Cut, "Cut command is armed");
            Call(_ws, "ChooseSketch", SketchName);
            Check(_design.Status != DesignStatus.Empty, "Cut draft from " + SketchName + " was accepted: " + Read<string>(_ws, "_notice"));
            await WaitUntil(() => _design.Status == DesignStatus.PreviewReady || _design.Status == DesignStatus.Error, ct);
            Check(_design.Status == DesignStatus.PreviewReady && _design.CanApply && _previewView.IsShowing,
                "Cut preview is rendered and ready: " + _design.Error);
            await AssertUnchanged(initial, "Cut preview", ct);
            Call(_ws, "CancelCommand");
            Check(_design.Status == DesignStatus.Empty && _design.Preview == null && !_previewView.IsShowing
                && _ws.Mode.Armed == SheetMetalCommand.None, "Cancel discards the Cut preview and disarms the command");
            await AssertUnchanged(initial, "Cut Cancel", ct);
            Pass("M5-04", "Cut from " + SketchName + " previewed and cancelled; revision " + initial.Revision + " unchanged");

            // A valid Cut preview also has to commit and return to the original
            // fixture through XR Undo before the later flange and flat-pattern cases.
            Call(_ws, "OpenSketchPick", SheetMetalCommand.Cut);
            Call(_ws, "ChooseSketch", SketchName);
            await WaitUntil(() => _design.Status == DesignStatus.PreviewReady || _design.Status == DesignStatus.Error, ct);
            Check(_design.Status == DesignStatus.PreviewReady && _design.CanApply && _previewView.IsShowing,
                "second Cut preview is rendered and applicable: " + _design.Error);
            await AssertUnchanged(initial, "second Cut preview", ct);
            RequireFixture();
            Call(_ws, "ApplyPressed");
            var cutCommitted = await WaitFor(async () =>
            {
                var state = await _backend.GetDocumentStateAsync(ct);
                return state.DocumentId == initial.DocumentId && state.Revision != initial.Revision ? state : null;
            }, ct);
            await WaitUntil(() => _design.Status == DesignStatus.Empty && _ws.IsEnabled(CommandIds.Undo), ct);
            Pass("M5-04", "Cut Apply changed native revision " + initial.Revision + " -> " + cutCommitted.Revision);
            RequireFixture();
            Call(_ws, "ApplyHistory", false);
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
            Call(_ws, "ApplyPressed");
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
            Call(_ws, "ApplyHistory", false);
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
            Call(_ws, "ApplyHistory", true);
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
            Call(_ws, "StartFlatPattern");
            Check(_ws.Mode.Armed == SheetMetalCommand.FlatPattern, "flat pattern command is armed");
            await WaitUntil(() => _design.Status == DesignStatus.PreviewReady || _design.Status == DesignStatus.Error, ct);
            Check(_design.Status == DesignStatus.PreviewReady && _design.CanApply,
                "flat pattern preview is ready: " + _design.Error);
            await AssertUnchanged(redone, "flat pattern preview", ct);
            RequireFixture();
            Call(_ws, "ApplyPressed");
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
                flat.Detach();
                Check(flat.Detached && flat.IsVisible, "flat pattern is detached and still visible");
                Check(flat.MoveLocal(0.05, 0, 0) && Math.Abs(flat.OffsetX - 0.05) < 1e-9, "detached view accepts a local offset");
                flat.Attach();
                Check(!flat.Detached && flat.OffsetX == 0, "Attach resets the local offset");
                flat.Detach();
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

            Record("NOT COVERED [M5-03] Grip+Trigger manipulator gesture and trigger edge pick: the edge was toggled on FlangeDraft and the height set through the numeric field path");
            Record("NOT COVERED [M5-04] Face and rule/thickness commands: Cut preview, Cancel, Apply and Undo run here");
            Record("NOT COVERED [M5-05] flat pattern outcomes on multi-body or non-unfoldable parts: only AlreadyExists is asserted");
            Record("NOT COVERED [M5-06] Grip repositioning gesture and visual comparison of the detached pattern with the folded part");
            Record("NOT COVERED [M5-07] network loss, late preview and uncertain commit: only the stale commit after Undo is asserted");
            Record("NOT COVERED [M5-08] push-to-talk button, microphone and audio: recognized text was injected into a PushToTalkController with silent samples, MicrophoneCapture and PushToTalkInput were not started");
            Record("NOT COVERED [M5-12] physical journey on Quest with Inventor in the real use environment and M1-M4 regressions");

            // ---- clean end
            Call(_ws, "CancelCommand");
            await WaitUntil(() => _design.Status == DesignStatus.Empty, ct);
            flat.Hide();
            Check(flat.State == FlatPatternState.Hidden && !display.IsShowing, "flat pattern is hidden");
            _ws.Close();
            Check(!_ws.Active, "Lamiera workspace closes");
            Record("Clean end: command cancelled, flat pattern hidden, Lamiera closed");
        }

        private async Task RunVoice(DocumentState baseline, CancellationToken ct)
        {
            // M5-10: dictation with the flange height field armed changes only that field.
            await WaitUntil(() => _design.Status == DesignStatus.Empty && _ws.IsEnabled(CommandIds.Flange), ct);
            Call(_ws, "ArmFlange");
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

            Call(_ws, "CancelCommand");
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
            await WaitUntil(() => _ws.IsEnabled(CommandIds.Flange), ct);
            Call(_ws, "ArmFlange");
            Check(_ws.Mode.Armed == SheetMetalCommand.Flange, "flange command is armed");
            _ws.Flange.ToggleEdge(edgeId);
            Check(_ws.Flange.EdgeIds.Count == 1 && _ws.Flange.EdgeIds[0] == edgeId, "flange draft holds the chosen edge");
            Check(_ws.TryArmField(LamieraWorkspace.FieldFlangeHeight), "flange height field is armed: " + _ws.LastFieldError);
            Check(_ws.SetArmedField(LamieraWorkspace.FieldFlangeHeight, heightMm), "height " + F(heightMm) + " mm is accepted: " + _ws.LastFieldError);
            await WaitUntil(() => _design.Status == DesignStatus.PreviewReady || _design.Status == DesignStatus.Error, ct);
            return _design.Status == DesignStatus.PreviewReady;
        }

        private void CheckFlangePreviewShown(double heightMm)
        {
            Check(_design.Status == DesignStatus.PreviewReady && _design.CanApply && _previewView.IsShowing
                && _previewView.PlanId == _design.Preview.PlanId, "flange preview is rendered and enabled for Apply");
            var operations = Read<JArray>(_design, "_operations");
            Check(operations != null && operations.Count == 1 && (string)operations[0]["command"] == "sheet_metal_flange"
                && Math.Abs((double)operations[0]["arguments"]["height_mm"] - heightMm) < 1e-9
                && Math.Abs((double)operations[0]["arguments"]["angle_degrees"] - 90) < 1e-9,
                "previewed draft is one flange of " + F(heightMm) + " mm at 90 degrees");
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
