#if XR_SO_ACCEPTANCE || UNITY_EDITOR
using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Pairing;
using InventorXrSo.Core.Session;
using InventorXrSo.Core.Ui;
using InventorXrSo.Unity.Ui;
using InventorXrSo.Unity.Scene;
using InventorXrSo.Xr.Input;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace InventorXrSo.Xr
{
    /// <summary>Incremental S1 QA only. Real product actions and Inventor backend; synthetic catalog/keypad input.</summary>
    internal sealed partial class S1QuestAcceptance : QuestAcceptanceRunner
    {
        internal static readonly string[] ReflectedMembers =
        {
            "AppController._inSession", "AppController.EnterSession", "AppController._catalog",
            "AppController._design", "AppController._lamiera", "AppController._input", "DesignWorkspace._backend",
            "DesignWorkspace._nativeEditor", "LamieraWorkspace._nativeEditor", "LamieraWorkspace._backend",
            "DesignWorkspace._renderedPlan", "LamieraWorkspace._renderedPlan",
            "DesignWorkspace._previewView", "LamieraWorkspace._previewView", "DesignPreviewView._previewRoot",
            "DesignWorkspace._session", "DesignWorkspace._context", "DesignGeometryView._textLayer", "NativeSketchTextLayer._entries"
        };
        private const string PartName = "XR_S1_Quest_Acceptance_Part.ipt";
        private const string SheetName = "XR_S1_Quest_Acceptance_Sheet.ipt";
        private const string SketchName = "S1_Create";
        private const string LineAction = "sketch.native.SketchLineCmd";
        private DesignWorkspace _design;
        private LamieraWorkspace _lamiera;
        private InventorBackend _backend;
        private IInspectionBackend _inspection;
        private string _expectedDocumentId;
        private S1AcceptanceBootstrapMetadata _binding;
        private XrInput _input;
        private IXrInputSource _originalSource;
        private ActionCatalog Catalog => Read<ActionCatalog>(App, "_catalog");
        protected override string Milestone => "s1";
        protected override int TimeoutSeconds => 1200;
        // Always incremental: wrapper/COM restoration and remaining S1 cases are independent evidence.
        protected override string Completion() => "PARTIAL; incremental S1 subset; NOT COVERED: "
            + string.Join(", ", NotCoveredGates);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AfterSceneLoad() => StartIfRequested<S1QuestAcceptance>("xr_s1_acceptance");

        protected override async Task Run(CancellationToken ct)
        {
            await WaitForFixture(ct);
            RequireFixture();
            _binding = App.AcceptanceBootstrap;
            GuardBinding();
            if (!ReadBoolean(App, "_inSession")) Call(App, "EnterSession", EnvironmentMode.StudioVr);
            _design = await WaitFor(() => Read<DesignWorkspace>(App, "_design"), ct);
            _lamiera = await WaitFor(() => Read<LamieraWorkspace>(App, "_lamiera"), ct);
            _backend = await WaitFor(() => Read<InventorBackend>(_design, "_backend"), ct);
            await WaitUntil(() => ReferenceEquals(Read<InventorBackend>(_lamiera, "_backend"), _backend), ct);
            _inspection = _backend;
            Check(Catalog != null, "The real application catalog is required.");
            var documents = await _inspection.ListOpenAsync(ct);
            var part = documents.SingleOrDefault(d => d.Name == PartName && d.Id == _binding.PartDocumentId);
            var sheet = documents.SingleOrDefault(d => d.Name == SheetName && d.Id == _binding.SheetDocumentId);
            Check(part != null && sheet != null && part.Id != sheet.Id,
                "Dedicated S1 Part and Sheet fixture documents must both be open.");
            string initialId = Session.Scene.Graph.DocumentId;
            Check(initialId == part.Id || initialId == sheet.Id,
                "Active document must be one of the exact dedicated fixture identities.");
            Record("SYNTHETIC CATALOG/KEYPAD input only; no controller frames, physical pointing or tracking are exercised.");
            Record("QA binding run=" + _binding.RunId + " target=" + _binding.TargetId
                + "; target/PID/start attestation belongs to the pinned owned host manifest; runner verifies actual document IDs/names.");
            NotCovered("S1-16-fixture-restoration", "Host wrapper must inspect and close owned fixture documents WITHOUT saving, reactivate previous document and verify ordinary APK hash. Finish below is not restoration.");
            NotCovered("S1-16-remaining", "Incremental subset: line Part/Sheet, readonly native text Part/Sheet and parameter UI in Progettazione only. Other commands, offline and performance remain open.");
            NotCovered("S1-parameter-options-remaining", "Increment81 exercises a native editor subset in Part/Sheet; existing Units property changes and remaining parameter options still require native/Desktop verification.");
            NotCovered("S1-desktop-wireless", "Desktop parity and wireless transport are not exercised by this synthetic runner.");
            NotCovered("S1-17-physical", "No person/controller input, MR readability, tracking or ergonomics exercised.");
            try
            {
                _input = Read<XrInput>(App, "_input");
                Check(_input != null, "Application input source is required for deterministic idle input.");
                _originalSource = _input.Source;
                _input.Source = new SyntheticInputSource
                { Next = new XrInputFrame { PenTracked = true, PaletteTracked = true } };
                Record("SYNTHETIC idle controller source suppresses physical button events; only catalog/keypad commands below are exercised.");
                await RunContext(part.Id, false, ct);
                await RunContext(sheet.Id, true, ct);
                await RunTextReadContext(part.Id, false, ct);
                await RunTextReadContext(sheet.Id, true, ct);
                await PaceQaRateBudget(ct);
                await RunParameterUiContext(part.Id, ct);
                await PaceQaRateBudget(ct);
                await RunNativeParameterUiContext(part.Id, false, 35, ct);
                await PaceQaRateBudget(ct);
                await RunNativeParameterUiContext(sheet.Id, true, 10, ct);
            }
            finally
            {
                // Fresh bounded token: cancellation of the run must not skip local cleanup.
                try
                {
                    using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(45));
                    await Cleanup(initialId, part.Id, sheet.Id, cleanup.Token);
                }
                finally
                {
                    if (_input != null && _originalSource != null) _input.Source = _originalSource;
                }
            }
        }

        private NativeSketchEditorUi Editor(bool sheet) => sheet
            ? Read<NativeSketchEditorUi>(_lamiera, "_nativeEditor")
            : Read<NativeSketchEditorUi>(_design, "_nativeEditor");

        private async Task Activate(string documentId, bool sheet, CancellationToken ct)
        {
            RequireFixture();
            GuardBinding();
            Check(ReadBoolean(App, "_inSession"), "Activation refused outside the QA session.");
            Check(documentId == (sheet ? _binding.SheetDocumentId : _binding.PartDocumentId), "Activation refused: unbound document.");
            _expectedDocumentId = documentId;
            var documents = await _inspection.ListOpenAsync(ct);
            Check(documents.Any(d => d.Id == documentId && d.Name == (sheet ? SheetName : PartName)),
                "Fixture identity/name changed before activation.");
            await _inspection.ActivateOpenAsync(documentId, ct);
            await WaitUntil(() => Session.Status == SessionStatus.Online
                && Session.Scene?.Graph?.DocumentId == documentId
                && (sheet ? _lamiera.Active : _design.Active), ct);
            await WaitUntil(() => Catalog.Find("sketch.editor.open")?.Enabled == true, ct);
            RequireFixture();
            Check(Session.Scene?.Graph?.DocumentId == _expectedDocumentId,
                "Action refused: active fixture identity changed.");
        }

        private void Action(string id)
        {
            GuardAction();
            var action = Catalog.Find(id);
            Check(action != null && action.Enabled, "Action unavailable: " + id + " / " + action?.DisabledReason);
            Check(action.TryInvoke(), "Action rejected: " + id);
        }

        private async Task RunContext(string documentId, bool sheet, CancellationToken ct)
        {
            string context = sheet ? "Sheet" : "Part";
            await Activate(documentId, sheet, ct);
            Action("sketch.editor.open");
            var provider = sheet ? (IActionProvider)_lamiera : _design;
            await WaitUntil(() => provider.Actions.Any(a => a.Label == SketchName
                && a.Id.StartsWith(sheet ? LamieraWorkspace.IdPickPrefix : "design.pick.", StringComparison.Ordinal)), ct);
            var pick = provider.Actions.Single(a => a.Label == SketchName
                && a.Id.StartsWith(sheet ? LamieraWorkspace.IdPickPrefix : "design.pick.", StringComparison.Ordinal));
            Action(pick.Id);
            await WaitUntil(() => Editor(sheet)?.Session.CanEdit == true && Editor(sheet).Session.Snapshot?.Name == SketchName, ct);
            var editor = Editor(sheet);
            var native = editor.Session;
            var baseline = native.Snapshot;
            Check(baseline.State.DocumentId == documentId && !baseline.Partial && baseline.Name == SketchName,
                "Wrong or partial native sketch baseline.");
            AssertEmpty(baseline);
            Check(baseline.SupportsVariant("line"), "Native line capability must be exposed by the loaded Inventor add-in.");
            await Unchanged(baseline, ct);
            Pass("S1-04-Q-open-" + context, "SYNTHETIC catalog: real workspace picker opened empty S1_Create; native document/sketch/revision checked.");

            Line(editor);
            Check(native.HasDraft && native.Mutation.Status == DesignStatus.Draft, "Numeric line did not prepare a product draft.");
            Check(Catalog.Find("sketch.editor.finish")?.Enabled == false, "Finish must reject a pending draft.");
            await Unchanged(baseline, ct);
            await Preview(native, baseline, sheet, ct);
            Action(CommitIds.Cancel);
            await WaitUntil(() => !native.HasDraft && native.Mutation.Status == DesignStatus.Empty, ct);
            Check(!PreviewView(sheet).IsShowing && PreviewView(sheet).PlanId == null,
                "Cancel left the preview renderer visible.");
            await Unchanged(baseline, ct);
            Pass("S1-13-Q-cancel-" + context, "SYNTHETIC numeric line: native preview rolled back; Cancel cleared draft/plan with CAD content and revision unchanged.");

            Line(editor);
            await Preview(native, baseline, sheet, ct);
            Action(CommitIds.Apply);
            await WaitUntil(() => native.Snapshot != null && native.Snapshot.State.Revision != baseline.State.Revision
                && !native.NeedsRefresh && native.CanEdit && native.Mutation.Status == DesignStatus.Empty, ct);
            var state = await _backend.GetDocumentStateAsync(ct);
            Check(state.DocumentId == documentId && state.Revision != baseline.State.Revision,
                "Commit did not change the expected document revision.");
            var actual = await _backend.GetNativeSketchAsync(state, native.SketchId, ct);
            Check(!actual.Partial && actual.Dimensions.Count == 0,
                "Unexpected partial snapshot or dimensions after native line creation.");
            var lines = actual.Entities.Where(e => e.Type == "line").ToArray();
            var points = actual.Entities.Where(e => e.Type == "point").ToArray();
            Check(lines.Length == 1 && lines.Length + points.Length == actual.Entities.Count,
                "Expected one native line and only its structural endpoint points.");
            var line = lines[0];
            Check(line.Type == "line" && line.Supported && line.Reference.Matches(state, actual.SketchId),
                "Committed object is not a current supported native line.");
            Check(line.TryGetSubpartPoint(SketchSubpart.Start, out var start), "Native line lacks its start anchor.");
            Check(line.TryGetSubpartPoint(SketchSubpart.End, out var end), "Native line lacks its end anchor.");
            Check((start - new CadPoint(0, 0)).Length <= 1e-6
                && (end - new CadPoint(20, 0)).Length <= 1e-6, "Native line endpoints differ from (0,0)-(20,0) mm.");
            // SketchEntities can include endpoint SketchPoints. If exposed, demand the exact pair,
            // not an arbitrary number of coincident/free points or a silent exclusion from readback.
            Check(points.Length == 0 || points.Length == 2, "Unexpected structural point cardinality.");
            if (points.Length == 2)
            {
                Check(points.All(p => p.Supported && p.Reference.Matches(state, actual.SketchId))
                    && points.Select(p => p.Reference.EntityId).Distinct(StringComparer.Ordinal).Count() == 2,
                    "Structural endpoint identities are unavailable or duplicated.");
                var positions = points.Select(p =>
                {
                    Check(p.TryGetSubpartPoint(SketchSubpart.Point, out var point), "Native structural point lacks coordinates.");
                    return point;
                }).ToArray();
                Check(positions.Count(p => (p - start).Length <= 1e-6) == 1
                    && positions.Count(p => (p - end).Length <= 1e-6) == 1,
                    "Native structural point set differs from the two analytic line endpoints.");
            }
            // Inventor AddByTwoPoints creates two non-deletable endpoint Coincident constraints.
            // Demand their exact associations; do not accept arbitrary inferred constraints.
            Check(points.Length == 2 && actual.Constraints.Count == 2,
                "Expected the two native structural endpoint constraints.");
            Record("native-constraint-bindings " + context + ": "
                + new JArray(actual.Constraints.Select(c => c.Data)).ToString(Newtonsoft.Json.Formatting.None));
            foreach (var point in points)
            {
                point.TryGetSubpartPoint(SketchSubpart.Point, out var position);
                var endpoint = (position - start).Length <= 1e-6 ? SketchSubpart.Start : SketchSubpart.End;
                var matches = actual.Constraints.Where(c => c.Supported && c.Reference.Matches(state, actual.SketchId)
                    && (string)c.Data["native_type"] == "kCoincidentConstraintObject"
                    && (bool?)c.Data["deletable"] == false && c.BindingState == "complete"
                    && c.RelatedEntities.Count == 4
                    && c.RelatedEntities.Any(r => r.EntityId == line.Reference.EntityId && r.Subpart == SketchSubpart.Entity)
                    && c.RelatedEntities.Any(r => r.EntityId == line.Reference.EntityId && r.Subpart == endpoint)
                    && c.RelatedEntities.Any(r => r.EntityId == point.Reference.EntityId && r.Subpart == SketchSubpart.Entity)
                    && c.RelatedEntities.Any(r => r.EntityId == point.Reference.EntityId && r.Subpart == SketchSubpart.Point)).ToArray();
                Check(matches.Length == 1, "Native structural coincidence does not bind exactly one line endpoint and its point.");
            }
            Record("native-readback " + context + ": " + new JObject
            {
                ["document_id"] = state.DocumentId, ["revision_before"] = baseline.State.Revision,
                ["revision_after"] = state.Revision, ["sketch_id"] = actual.SketchId, ["line"] = line.Data,
                ["structural_points"] = new JArray(points.Select(p => p.Data)),
                ["structural_constraints"] = new JArray(actual.Constraints.Select(c => c.Data))
            }.ToString(Newtonsoft.Json.Formatting.None));
            await CaptureScreenshot(context + "-committed-line", ct);
            Pass("S1-05-Q-line-" + context, "SYNTHETIC catalog/keypad: Preview rendered, Apply committed and fresh Inventor readback confirms analytic line endpoints within 1e-6 mm.");
            Action("sketch.editor.finish");
            await WaitUntil(() => !editor.IsOpen, ct);
            var finished = await _backend.GetDocumentStateAsync(ct);
            Check(finished.DocumentId == state.DocumentId && finished.Revision == state.Revision,
                "Finish unexpectedly mutated the CAD document.");
            Pass("S1-13-Q-finish-" + context, "Product editor closed after commit without another mutation; committed line remains for host inspection, not saved.");
        }

        private void Line(NativeSketchEditorUi editor)
        {
            Action(LineAction);
            foreach (double value in new[] { 0d, 0d, 20d, 0d })
            {
                var entry = editor.ActiveEntry;
                GuardAction();
                Check(entry != null && entry.Editing, "Product numeric coordinate keypad is not editing.");
                foreach (char c in value.ToString("0.###", CultureInfo.InvariantCulture)) entry.Type(c);
                Check(entry.Commit(out var reason), "Coordinate keypad rejected input: " + reason);
            }
        }

        private DesignPreviewView PreviewView(bool sheet) => sheet
            ? Read<DesignPreviewView>(_lamiera, "_previewView") : Read<DesignPreviewView>(_design, "_previewView");

        private async Task Preview(NativeSketchSession native, NativeSketchSnapshot baseline, bool sheet, CancellationToken ct)
        {
            Action(CommitIds.Preview);
            await WaitUntil(() => native.Mutation.Status == DesignStatus.PreviewReady && native.Mutation.CanApply, ct);
            Check(native.Mutation.Preview != null && native.Mutation.Preview.DocumentId == baseline.State.DocumentId
                && native.Mutation.Preview.Revision == baseline.State.Revision, "Rendered preview has the wrong baseline.");
            string renderedPlan = sheet ? Read<string>(_lamiera, "_renderedPlan") : Read<string>(_design, "_renderedPlan");
            var view = PreviewView(sheet);
            Check(view != null && view.IsShowing && view.PlanId == native.Mutation.Preview.PlanId
                && renderedPlan == view.PlanId, "Workspace has not rendered this exact preview plan.");
            var root = Read<GameObject>(view, "_previewRoot");
            Check(root != null && root.activeInHierarchy, "Preview root is not actually visible.");
            Vector3 a = CadCoordinates.ToLocal(baseline.Frame.ToModel(new CadPoint(0, 0)));
            Vector3 b = CadCoordinates.ToLocal(baseline.Frame.ToModel(new CadPoint(20, 0)));
            Check(root.GetComponentsInChildren<LineRenderer>(false).Any(line => line.enabled && line.positionCount == 2
                && !line.useWorldSpace && Vector3.Distance(line.GetPosition(0), a) <= 1e-7f
                && Vector3.Distance(line.GetPosition(1), b) <= 1e-7f),
                "Rendered preview lacks the requested visible 20 mm line in model coordinates.");
            await Unchanged(baseline, ct);
            await CaptureScreenshot((sheet ? "Sheet" : "Part") + "-preview-" + Guid.NewGuid().ToString("N"), ct);
        }

        private static void AssertEmpty(NativeSketchSnapshot snapshot)
            => Check(snapshot.Entities.Count == 0 && snapshot.Dimensions.Count == 0 && snapshot.Constraints.Count == 0,
                "Dedicated S1_Create must be completely empty before this run; never delete existing geometry to reset it.");

        private async Task Unchanged(NativeSketchSnapshot baseline, CancellationToken ct)
        {
            RequireFixture();
            var state = await _backend.GetDocumentStateAsync(ct);
            Check(state.DocumentId == baseline.State.DocumentId && state.Revision == baseline.State.Revision,
                "Draft/preview/cancel changed the document or revision.");
            var current = await _backend.GetNativeSketchAsync(state, baseline.SketchId, ct);
            Check(!current.Partial && current.Name == baseline.Name, "Fresh native readback is partial or a different sketch.");
            AssertEmpty(current);
        }

        private async Task Cleanup(string initialId, string partId, string sheetId, CancellationToken ct)
        {
            RequireFixture();
            // No Delete/Undo guesses: wrapper owns full fixture restoration, including after unknown commit outcome.
            bool activeSheet = Session.Scene?.Graph?.DocumentId == sheetId;
            Check(Session.Scene?.Graph?.DocumentId == partId || activeSheet, "Cleanup refused: fixture document changed.");
            _expectedDocumentId = Session.Scene.Graph.DocumentId;
            GuardAction();
            var editor = Editor(activeSheet);
            if (editor?.IsOpen == true)
            {
                editor.CancelPrompt();
                if (editor.Session.Mutation.Status == DesignStatus.Committing)
                    await WaitUntil(() => editor.Session.Mutation.Status != DesignStatus.Committing, ct, 30);
                Check(!editor.Session.Mutation.CommitOutcomeUnknown, "Cleanup requires native inspection: commit outcome unknown.");
                if (Catalog.Find(CommitIds.Cancel)?.Enabled == true) Action(CommitIds.Cancel);
                if (Catalog.Find("sketch.editor.finish")?.Enabled == true) Action("sketch.editor.finish");
                Check(!editor.IsOpen, "Local editor cleanup incomplete; wrapper must restore fixture.");
            }
            var design = Read<DesignSession>(_design, "_session");
            if (!activeSheet && design != null)
            {
                if (_design.ActiveParameterTextEntry != null) Action("design.parameter.input.cancel");
                if (design.Status == DesignStatus.Committing)
                    await WaitUntil(() => design.Status != DesignStatus.Committing, ct, 30);
                Check(!design.CommitOutcomeUnknown, "Parameter cleanup requires native inspection: commit outcome unknown.");
                if (Catalog.Find(CommitIds.Cancel)?.Enabled == true) Action(CommitIds.Cancel);
            }
            GuardAction();
            await _inspection.ActivateOpenAsync(initialId, ct);
            await WaitUntil(() => Session.Status == SessionStatus.Online && Session.Scene?.Graph?.DocumentId == initialId, ct, 30);
            Record("LOCAL CLEANUP complete; fixture documents and committed geometry remain unsaved. HOST RESTORE + ORDINARY APK verification REQUIRED even after failure.");
        }
    }
}
#endif
