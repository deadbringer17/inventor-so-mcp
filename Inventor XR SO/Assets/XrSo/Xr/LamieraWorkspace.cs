using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Session;
using InventorXrSo.Core.Voice;
using InventorXrSo.Unity.Scene;
using InventorXrSo.Unity.Ui;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace InventorXrSo.Xr
{
    /// <summary>Numeric field currently armed for keypad or voice dictation (id, unit, range and current value).</summary>
    public sealed class LamieraNumericField
    {
        public string Id { get; }
        public string Label { get; }
        public QuantityUnit Unit { get; }
        public double Min { get; }
        public double Max { get; }
        public double Value { get; }
        public LamieraNumericField(string id, string label, QuantityUnit unit, double min, double max, double value)
        { Id = id; Label = label; Unit = unit; Min = min; Max = max; Value = value; }
    }

    /// <summary>
    /// Quest M5 Lamiera: sheet-metal authoring on top of the shared Design draft/preview/Apply cycle.
    /// Trigger selects (flange edges), Grip moves the view, Grip+Trigger on the knob edits the armed flange draft.
    /// Nothing reaches CAD without a rendered, validated preview and the physical Applica button.
    /// </summary>
    [DefaultExecutionOrder(115)]
    public sealed class LamieraWorkspace : MonoBehaviour
    {
        public const string FieldFlangeHeight = "flange.height_mm";
        public const string FieldFlangeAngle = "flange.angle_deg";
        public const string FieldThickness = "rule.thickness_mm";
        private static readonly CultureInfo Italian = CultureInfo.GetCultureInfo("it-IT");

        private CadSceneView _view;
        private SelectionVisuals _selection;
        private ControllerRay _ray;
        private Transform _head;
        private HomePanel _panel;
        private DesignPreviewView _previewView;
        private DesignGeometryView _geometry;
        private FlangeManipulator _manip;
        private FlatPatternDisplay _flatDisplay;
        private IDesignWorkspaceBackend _backend;
        private ISheetMetalBackend _sheet;
        private DesignSession _session;
        private IDisposable _flangeBinding;
        private FlatPatternView _flat;
        private readonly SheetMetalMode _mode = new SheetMetalMode();
        private readonly FlangeDraft _flange = new FlangeDraft();
        private SheetMetalContext _sheetContext;
        private DesignContext _designContext;
        private DesignHistory _history;
        private DocumentState _state, _sceneState;
        private string _kind, _screen = "tools", _notice = "", _renderedPlan;
        private string _sketchName, _ruleName, _extent = "thickness", _direction = "positive";
        private double? _thicknessMm;
        private bool _acrossBends, _pendingShowFlat, _wasPrimary;
        private string _armedField, _armedFieldScreen;
        private bool _online, _visible, _busy, _pinned, _reviewAfterRebind, _renderPending;
        private int _pendingMutations, _generation, _page;
        private float _nextRender;
        private CancellationTokenSource _reads = new CancellationTokenSource();
        private Transform _grab;
        private bool _grabFlat;
        private Vector3 _grabPosition;
        private Quaternion _grabRotation;
        private readonly Dictionary<string, string> _ids = new Dictionary<string, string>();
        private readonly Dictionary<string, bool> _enabled = new Dictionary<string, bool>();

        public bool Active { get; private set; }
        public bool RequiresCadReview => _reviewAfterRebind || _pendingMutations > 0 || _session?.Status == DesignStatus.Committing || _session?.Status == DesignStatus.RefreshRequired;
        public Func<bool> CanEnter { get; set; }
        public event Action<bool> ActiveChanged;
        /// <summary>Lamiera becomes (or stops being) the primary mode for the active document.</summary>
        public event Action<bool> PrimaryChanged;
        /// <summary>The user asked for Modello 3D / Schizzo (Design); the switcher decides.</summary>
        public event Action DesignRequested;
        public event Action ArmedFieldChanged;
        public bool IsPrimary => _mode.IsPrimary;
        public SheetMetalMode Mode => _mode;
        public FlangeDraft Flange => _flange;
        public FlatPatternView FlatPattern => _flat;
        public DesignSession Session => _session;
        public string LastFieldError { get; private set; }
        public LamieraNumericField ArmedField => _armedField == null ? null : Describe(_armedField);

        // ---------------------------------------------------------------- lifetime

        public void Initialize(CadSceneView view, SelectionVisuals selection, ControllerRay ray, Transform head)
        {
            _view = view; _selection = selection; _ray = ray; _head = head;
            _panel = HomePanel.Create(transform); _panel.name = "Lamiera";
            ((RectTransform)_panel.transform).sizeDelta = new Vector2(820, 1150);
            _panel.transform.localScale = Vector3.one * 0.00065f;
            XrUi.MakeInteractive(_panel.Canvas, head.GetComponent<Camera>());
            _panel.gameObject.SetActive(false);
            var previewHost = new GameObject("Lamiera preview"); previewHost.transform.SetParent(view.transform, false);
            _previewView = previewHost.AddComponent<DesignPreviewView>(); _previewView.Initialize(view, head);
            var geometry = new GameObject("Lamiera local geometry"); geometry.transform.SetParent(view.transform, false);
            _geometry = geometry.AddComponent<DesignGeometryView>(); _geometry.Initialize(ray.LineMaterial);
            var handle = new GameObject("Flange manipulator"); handle.transform.SetParent(view.transform, false);
            _manip = handle.AddComponent<FlangeManipulator>(); _manip.Initialize(view.transform, ray.LineMaterial);
            var flat = new GameObject("Sviluppo piano"); flat.transform.SetParent(transform, false);
            _flatDisplay = flat.AddComponent<FlatPatternDisplay>(); _flatDisplay.Initialize(view, head);
            _flange.Changed += OnFlangeChanged;
            _mode.Changed += OnModeChanged;
        }

        /// <summary>The same backend object normally serves both interfaces (<see cref="InventorBackend"/>).</summary>
        public void Bind(IDesignWorkspaceBackend design, ISheetMetalBackend sheet)
        {
            CancelReads();
            _reviewAfterRebind |= _pendingMutations > 0 || _session?.Status == DesignStatus.Committing || _session?.Status == DesignStatus.RefreshRequired;
            _flangeBinding?.Dispose(); _flangeBinding = null;
            if (_session != null) { _session.Changed -= SessionChanged; _session.Dispose(); }
            if (_flat != null) { _flat.Changed -= OnFlatChanged; _flat.Dispose(); }
            _backend = design; _sheet = sheet;
            _session = design == null ? null : new DesignSession(design);
            if (_session != null)
            {
                if (_reviewAfterRebind) _session.RequireCadReview();
                _session.Changed += SessionChanged;
                _flangeBinding = _mode.BindFlange(_session, _flange);
            }
            _flat = sheet == null ? null : new FlatPatternView(sheet);
            if (_flat != null) _flat.Changed += OnFlatChanged;
            if (_flatDisplay != null) _flatDisplay.Bind(_flat);
            _sheetContext = null; _designContext = null; _history = null; _state = null; _online = false;
            ResetDraft(); RefreshMode(); Render();
        }

        public void SetScene(LoadedScene scene)
        {
            // CadSceneView.Show rebuilds the view and releases the rendered ghost: its rendering proof is gone.
            if (_session?.CanEdit == true && _session.Status == DesignStatus.PreviewReady)
            { _renderedPlan = null; _session.RejectDraft("Scena aggiornata: ricalcola l'anteprima prima di applicare."); }
            _sceneState = scene?.Graph.State; _kind = scene?.Graph.Kind;
            SetDocumentState(_sceneState);
            RequestRender();
        }

        public void SetDocumentState(DocumentState state)
        {
            bool changed = _state?.DocumentId != state?.DocumentId || _state?.Revision != state?.Revision;
            _state = state;
            _session?.SetContext(state, _online, _kind == "part");
            if (changed) StateMoved(); else RefreshMode();
            if (changed && _visible && _online && _kind == "part") LoadContext();
        }

        public void SetOnline(bool online)
        {
            if (_online == online) return;
            _online = online; CancelReads(); _designContext = null; _history = null;
            ResetDraft();
            _session?.SetContext(_state, online, _kind == "part");
            _flat?.SetContext(_state, online);
            RefreshMode();
            if (online && _visible && _kind == "part")
            {
                // Same revision but a verification gap: the read-only pattern is fetched again before it is trusted.
                if (_flat != null && _flat.State == FlatPatternState.StaleReadOnly) ShowFlat();
                LoadContext();
            }
            Render();
        }

        public void SetVisible(bool visible)
        {
            _visible = visible;
            if (!visible) { Close(); return; }
            if (_online && _state != null && _kind == "part") LoadContext();
        }

        public void Open()
        {
            if (!_visible || CanEnter?.Invoke() == false) return;
            Active = true; _ray.CanPick = false; _selection?.Clear();
            ActiveChanged?.Invoke(true); _panel.gameObject.SetActive(true);
            if (!_pinned)
            {
                var forward = Vector3.ProjectOnPlane(_head.forward, Vector3.up).normalized;
                if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
                _panel.transform.SetPositionAndRotation(_head.position + forward * 0.9f + _head.right * 0.48f, Quaternion.LookRotation(forward));
            }
            _screen = "tools"; _flatDisplay.SetPresented(true);
            Render(); LoadContext();
        }

        public void Close()
        {
            Active = false; CancelReads();
            if (_session != null && _session.Status != DesignStatus.Committing && _session.Status != DesignStatus.RefreshRequired) _session.Cancel();
            ResetDraft(); _panel?.gameObject.SetActive(false); _selection?.Clear();
            _flatDisplay?.SetPresented(false);
            if (_ray != null) _ray.CanPick = _visible;
            ActiveChanged?.Invoke(false);
        }

        private void CancelReads()
        { _generation++; _reads.Cancel(); _reads.Dispose(); _reads = new CancellationTokenSource(); _busy = false; }

        private void RefreshMode() => _mode.Update(_state, _online, _kind == "part", _sheetContext);

        /// <summary>Document/revision changed: selections, drafts, previews and the flat pattern asset are no longer meaningful.</summary>
        private void StateMoved()
        {
            CancelReads(); _designContext = null; _history = null;
            ResetDraft();
            _flat?.SetContext(_state, _online);
            RefreshMode();
        }

        private void ResetDraft()
        {
            _mode.Disarm();
            _flange.Clear();
            _sketchName = null; _ruleName = null; _thicknessMm = null;
            _extent = "thickness"; _direction = "positive"; _acrossBends = false;
            DisarmField();
            _grab = null; _renderedPlan = null; _screen = "tools"; _page = 0;
            _previewView?.Clear(); _geometry?.Clear(); _manip?.Hide();
        }

        private void OnModeChanged()
        {
            bool primary = _mode.IsPrimary;
            if (primary != _wasPrimary) { _wasPrimary = primary; PrimaryChanged?.Invoke(primary); }
            RequestRender();
        }

        private void OnFlatChanged() => RequestRender();

        private void OnFlangeChanged()
        {
            RefreshFlangeVisuals();
            RequestRender();
        }

        // ---------------------------------------------------------------- reading

        private async void LoadContext()
        {
            if (!_online || _state == null || _kind != "part" || _sheet == null || _busy) { Render(); return; }
            int generation = _generation; var state = _state; _busy = true;
            try
            {
                var sheet = await _sheet.GetSheetMetalContextAsync(state, _reads.Token);
                if (generation != _generation) return;
                _sheetContext = sheet; RefreshMode();
                if (Active && _mode.CanWrite && _backend != null)
                {
                    var context = await _backend.GetDesignContextAsync(state, _reads.Token);
                    if (generation != _generation) return;
                    _designContext = context;
                    _notice = context.Truncated ? "Elenco CAD parziale: alcuni bordi superano i limiti." : "";
                    if (_backend is IDesignHistoryBackend historyBackend)
                    {
                        _history = null;
                        var history = await historyBackend.GetHistoryAsync(state, _reads.Token);
                        if (generation != _generation) return;
                        _history = history;
                    }
                }
                if (_pendingShowFlat)
                {
                    _pendingShowFlat = false;
                    if (_mode.Context?.FlatPattern.Exists == true) ShowFlat();
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (generation == _generation) _notice = ex.Message; }
            finally { if (generation == _generation) { _busy = false; Render(); } }
        }

        private async void ShowFlat()
        {
            var flat = _flat;
            if (flat == null) return;
            try { await flat.ShowAsync(); }
            catch (InvalidOperationException ex) { _notice = ex.Message; }
            catch (Exception ex) { _notice = ex.Message; }
            if (this != null) Render();
        }

        // ---------------------------------------------------------------- session

        private void SessionChanged()
        {
            if (_session.Status == DesignStatus.Previewing || _session.Status == DesignStatus.Committing) _history = null;
            if (_session.Preview != null && _session.Status == DesignStatus.PreviewReady && _renderedPlan != _session.Preview.PlanId && Active)
            {
                try
                {
                    _selection?.Clear(); _previewView.Show(_session.Preview);
                    _renderedPlan = _session.Preview.PlanId;
                    _session.ConfirmRendered(_renderedPlan);
                }
                catch (Exception ex) { _notice = "Anteprima non visualizzabile: " + ex.Message; }
            }
            if (_session.Preview == null) { _previewView.Clear(); _renderedPlan = null; }
            ShowValidationContext();
            RequestRender();
        }

        private void ShowValidationContext()
        {
            _geometry.ClearErrorContext();
            if (_session == null || _session.Status != DesignStatus.Error || _designContext == null) return;
            var paths = new List<IEnumerable<CadPoint>>();
            if (_mode.Armed == SheetMetalCommand.Flange)
                paths.AddRange(_designContext.Edges.Where(e => _flange.EdgeIds.Contains(e.Id)).Select(e => (IEnumerable<CadPoint>)e.PointsMm));
            else if (_sketchName != null)
                foreach (var sketch in _designContext.SketchSnapshots.Where(s => s.Name == _sketchName))
                    paths.AddRange(sketch.Elements.Select(e => e.Outline().Select(sketch.Frame.ToModel)));
            _geometry.ShowErrorContext(paths);
        }

        private void RefreshFlangeVisuals()
        {
            if (_geometry == null || _manip == null) return;
            if (_mode.Armed != SheetMetalCommand.Flange || _designContext == null || _flange.EdgeIds.Count == 0)
            { if (_mode.Armed == SheetMetalCommand.Flange) _geometry.Clear(); _manip.Hide(); return; }
            if (_manip.Dragging) return; // edges and frame cannot change mid-drag; the drag owns the height
            var selected = _designContext.Edges.Where(e => _flange.EdgeIds.Contains(e.Id)).ToArray();
            _geometry.ShowEdges(selected);
            var first = selected.FirstOrDefault(e => e.Id == _flange.EdgeIds[0]) ?? selected.FirstOrDefault();
            if (first != null && _session?.CanEdit == true
                && FlangeManipulator.TryFrame(first, _designContext.Faces, _mode.Context?.ThicknessMm, out var origin, out var axis, out var fromFaces))
                _manip.Show(origin, axis, SafeHeight(_flange.HeightMm), fromFaces);
            else _manip.Hide();
        }

        private static double SafeHeight(double mm) => double.IsNaN(mm) || double.IsInfinity(mm) ? FlangeManipulator.MinHeightMm : mm;

        // ---------------------------------------------------------------- commands

        private bool Idle => Active && !_busy && _pendingMutations == 0 && _session != null && _session.CanEdit
            && _session.Status != DesignStatus.Previewing && _mode.CanWrite && SceneCurrent;
        private bool SceneCurrent => _state != null && _sceneState != null && _state.DocumentId == _sceneState.DocumentId && _state.Revision == _sceneState.Revision;

        /// <summary>Shared by the buttons and the voice layer: the same command id, the same enablement.</summary>
        public bool IsEnabled(string commandId)
        {
            if (!Active || _session == null) return false;
            var status = _session.Status;
            bool working = status == DesignStatus.Committing || status == DesignStatus.RefreshRequired || _pendingMutations > 0;
            switch (commandId)
            {
                case CommandIds.Flange: return Idle && _designContext != null;
                case CommandIds.SheetMetalRule: return Idle;
                case CommandIds.SheetMetalFace:
                case CommandIds.SheetMetalCut: return Idle && _designContext != null && _designContext.Sketches.Count > 0;
                case CommandIds.FlatPatternCreate: return Idle && _mode.CheckFlatPattern(out _) == FlatPatternCreation.Allowed;
                case CommandIds.CancelDraft:
                    return !working && (status == DesignStatus.Draft || status == DesignStatus.Previewing || status == DesignStatus.PreviewReady
                        || status == DesignStatus.Error || _mode.Armed != SheetMetalCommand.None);
                case CommandIds.Apply: return !working && _session.CanApply && _mode.CanWrite && SceneCurrent;
                case CommandIds.Undo: return Idle && status == DesignStatus.Empty && _history?.CanUndo == true;
                case CommandIds.Redo: return Idle && status == DesignStatus.Empty && _history?.CanRedo == true;
                default: return false; // Design/Inspect commands belong to other workspaces
            }
        }

        /// <summary>
        /// Runs the command exactly as its button would. Apply never commits from here: it only shows what will be
        /// applied; the physical Applica button is the single commit path.
        /// </summary>
        public bool Invoke(string commandId)
        {
            if (!IsEnabled(commandId)) return false;
            switch (commandId)
            {
                case CommandIds.Flange: ArmFlange(); return true;
                case CommandIds.SheetMetalRule: OpenRule(); return true;
                case CommandIds.SheetMetalFace: OpenSketchPick(SheetMetalCommand.Face); return true;
                case CommandIds.SheetMetalCut: OpenSketchPick(SheetMetalCommand.Cut); return true;
                case CommandIds.FlatPatternCreate: StartFlatPattern(); return true;
                case CommandIds.CancelDraft: CancelCommand(); return true;
                case CommandIds.Undo: ApplyHistory(false); return true;
                case CommandIds.Redo: ApplyHistory(true); return true;
                case CommandIds.Apply:
                    _notice = "Piano pronto: " + PlanSummary() + ". Conferma premendo Applica sul pannello.";
                    Render(); return true;
                default: return false;
            }
        }

        private string PlanSummary()
        {
            switch (_mode.Armed)
            {
                case SheetMetalCommand.Flange:
                    return "flangia su " + _flange.EdgeIds.Count + " bordo/i, altezza " + Fmt(_flange.HeightMm) + " mm, angolo " + Fmt(_flange.AngleDegrees) + "°";
                case SheetMetalCommand.Face: return "faccia base da " + _sketchName;
                case SheetMetalCommand.Cut: return "taglio da " + _sketchName;
                case SheetMetalCommand.Rule: return "modifica regola/spessore";
                case SheetMetalCommand.FlatPattern: return "creazione dello sviluppo piano";
                default: return "modifica CAD";
            }
        }

        /// <summary>Discard the current draft/preview (never while a commit is in flight) and disarm.</summary>
        private void DiscardDraft()
        {
            if (_session == null) return;
            if (_session.Status != DesignStatus.Committing && _session.Status != DesignStatus.RefreshRequired && _session.Status != DesignStatus.Empty)
                _session.Cancel();
            ResetDraft();
        }

        private void CancelCommand()
        {
            try { DiscardDraft(); _notice = ""; }
            catch (Exception ex) { _notice = ex.Message; }
            Render();
        }

        private void ArmFlange()
        {
            DiscardDraft();
            if (!_mode.Arm(SheetMetalCommand.Flange, out var reason)) { _notice = reason; Render(); return; }
            _notice = ""; _screen = "flange"; Render();
        }

        private void OpenRule()
        {
            DiscardDraft();
            if (!_mode.Arm(SheetMetalCommand.Rule, out var reason)) { _notice = reason; Render(); return; }
            _notice = ""; Page("rule");
        }

        private void OpenSketchPick(SheetMetalCommand command)
        {
            DiscardDraft();
            if (!_mode.Arm(command, out var reason)) { _notice = reason; Render(); return; }
            _notice = ""; Page("sketches");
        }

        private void StartFlatPattern()
        {
            DiscardDraft();
            if (!_mode.Arm(SheetMetalCommand.FlatPattern, out var reason)) { _notice = reason; Render(); return; }
            _notice = ""; _screen = "flat";
            if (!_mode.SubmitFlatPattern(_session)) { _notice = _mode.LastError; Render(); return; }
            PreviewDraft();
        }

        private void ChooseSketch(string name)
        {
            _sketchName = name;
            ShowProfile(name);
            string hint = ProfileHint(name);
            if (hint == "aperto" || hint == "vuoto")
            {
                _notice = hint == "aperto" ? "Profilo aperto: chiudi lo schizzo in Inventor o scegline un altro."
                    : "Lo schizzo non contiene geometria.";
                if (_session.Status != DesignStatus.Empty) _session.Cancel();
                Render(); return;
            }
            _notice = "";
            if (_mode.Armed == SheetMetalCommand.Cut) _screen = "cut";
            if (!RebuildDraft()) { if (!string.IsNullOrEmpty(_mode.LastError)) _notice = _mode.LastError; Render(); return; }
            PreviewDraft();
        }

        private bool SubmitRuleDraft()
        {
            string active = _mode.Context?.Rule;
            string rule = !string.IsNullOrEmpty(_ruleName) && _ruleName != active ? _ruleName : null;
            double? thickness = _thicknessMm.HasValue && _mode.Context?.ThicknessMm != _thicknessMm ? _thicknessMm : null;
            if (rule == null && !thickness.HasValue)
            {
                if (_session.Status != DesignStatus.Empty) _session.Cancel();
                _notice = "Nessuna differenza rispetto alla regola attiva."; return false;
            }
            return _mode.SubmitRule(_session, rule, null, thickness);
        }

        /// <summary>Rebuild the executable draft of the armed command from the current editor state.</summary>
        private bool RebuildDraft()
        {
            if (_session?.CanEdit != true) return false;
            switch (_mode.Armed)
            {
                case SheetMetalCommand.Flange: return _mode.SubmitFlange(_session, _flange);
                case SheetMetalCommand.Face:
                    if (_sketchName == null) { _notice = "Scegli lo schizzo della faccia base."; return false; }
                    return _mode.SubmitFace(_session, _sketchName);
                case SheetMetalCommand.Cut:
                    if (_sketchName == null) { _notice = "Scegli lo schizzo del taglio."; return false; }
                    return _mode.SubmitCut(_session, _sketchName, _extent, _direction, _acrossBends);
                case SheetMetalCommand.Rule: return SubmitRuleDraft();
                case SheetMetalCommand.FlatPattern: return _mode.SubmitFlatPattern(_session);
                default: return false;
            }
        }

        /// <summary>Anteprima button: rebuild from the editor state, then ask Inventor.</summary>
        private void Preview()
        {
            if (!RebuildDraft())
            {
                if (!string.IsNullOrEmpty(_mode.LastError)) _notice = _mode.LastError;
                else if (string.IsNullOrEmpty(_notice)) _notice = "Nessuna bozza da calcolare.";
                Render(); return;
            }
            PreviewDraft();
        }

        private async void PreviewDraft()
        {
            var session = _session;
            try { if (session != null && session.Status == DesignStatus.Draft) await session.PreviewAsync(); }
            catch (Exception ex) { _notice = ex.Message; if (this != null) Render(); }
        }

        /// <summary>Manipulator release: the shared draft asks for a preview of its current version, never a commit.</summary>
        private async void PreviewPending()
        {
            var session = _session;
            try { await _mode.PreviewPendingAsync(session, _flange); }
            catch (Exception ex) { _notice = ex.Message; if (this != null) Render(); }
        }

        private async void ApplyPressed()
        {
            var session = _session;
            if (session?.CanApply != true || !_mode.CanWrite) return;
            _pendingShowFlat = _mode.Armed == SheetMetalCommand.FlatPattern;
            await RunMutation(session, () => session.ApplyAsync());
        }

        private async void ApplyHistory(bool redo)
        {
            var history = _history; var session = _session;
            await RunMutation(session, () => session.ApplyHistoryAsync(history, redo));
        }

        private async Task RunMutation(DesignSession owner, Func<Task> mutation)
        {
            if (owner == null || _pendingMutations > 0) return;
            _pendingMutations++;
            try { await mutation(); }
            catch (Exception ex) { if (owner == _session) _notice = ex.Message; }
            finally { _pendingMutations--; }
            if (this == null) return;
            if (owner == _session && !owner.CommitOutcomeUnknown && owner.Status == DesignStatus.RefreshRequired)
            {
                try { await RefreshAfterApply(false); }
                catch (Exception ex) { if (owner == _session) _notice = ex.Message; }
            }
            Render();
        }

        private async Task RefreshAfterApply(bool reviewed)
        {
            if (!_online || _backend == null || _pendingMutations > 0) return;
            var backend = _backend; var session = _session; int generation = _generation;
            var state = await backend.GetDocumentStateAsync(_reads.Token);
            if (this == null || backend != _backend || session != _session || generation != _generation) return;
            session.AcknowledgeRefresh(state, reviewed); _reviewAfterRebind = false; _state = state;
            StateMoved(); LoadContext();
            _notice = "Documento aggiornato. Attendo il modello da Inventor.";
        }

        private async void ReviewUnknown()
        {
            try { await RefreshAfterApply(true); }
            catch (Exception ex) { _notice = ex.Message; }
            Render();
        }

        // ---------------------------------------------------------------- numeric fields

        private LamieraNumericField Describe(string id)
        {
            switch (id)
            {
                case FieldFlangeHeight:
                    return new LamieraNumericField(id, "Altezza flangia", QuantityUnit.Millimeters, FlangeManipulator.MinHeightMm, SheetMetalOperations.MaxFlangeHeightMm, _flange.HeightMm);
                case FieldFlangeAngle:
                    return new LamieraNumericField(id, "Angolo flangia", QuantityUnit.Degrees, 0.1, 359.9, _flange.AngleDegrees);
                case FieldThickness:
                    return new LamieraNumericField(id, "Spessore lamiera", QuantityUnit.Millimeters, SheetMetalOperations.MinThicknessMm, SheetMetalOperations.MaxThicknessMm,
                        _thicknessMm ?? _mode.Context?.ThicknessMm ?? 1);
                default: return null;
            }
        }

        /// <summary>Arms a field of the current command for keypad/voice input. Only that field can then be set.</summary>
        public bool TryArmField(string fieldId)
        {
            LastFieldError = null;
            bool ok = fieldId == FieldFlangeHeight || fieldId == FieldFlangeAngle ? _mode.Armed == SheetMetalCommand.Flange && Idle
                : fieldId == FieldThickness && _mode.Armed == SheetMetalCommand.Rule && Idle;
            if (!ok) { LastFieldError = "Campo non disponibile: arma prima il comando corrispondente."; return false; }
            _armedField = fieldId; _armedFieldScreen = _screen;
            ArmedFieldChanged?.Invoke();
            return true;
        }

        public void DisarmField()
        {
            if (_armedField == null) return;
            _armedField = null; _armedFieldScreen = null;
            ArmedFieldChanged?.Invoke();
        }

        /// <summary>
        /// Sets the armed field (same path as the keypad): the draft is updated and a new preview requested,
        /// Apply stays invalid until it is rendered. Any other field id, a non-finite or out-of-range value is refused.
        /// </summary>
        public bool SetArmedField(string fieldId, double value)
        {
            LastFieldError = null;
            var field = ArmedField;
            if (field == null || field.Id != fieldId) { LastFieldError = "Nessun campo attivo con questo nome."; return false; }
            if (double.IsNaN(value) || double.IsInfinity(value) || value < field.Min || value > field.Max)
            { LastFieldError = "Valore fuori intervallo (" + Fmt(field.Min) + " – " + Fmt(field.Max) + ")."; return false; }
            if (!Idle) { LastFieldError = "Modifica non disponibile ora."; return false; }
            switch (fieldId)
            {
                case FieldFlangeHeight: _flange.SetHeight(value); PreviewDraft(); break;
                case FieldFlangeAngle: _flange.SetAngle(value); PreviewDraft(); break;
                case FieldThickness:
                    _thicknessMm = value;
                    if (SubmitRuleDraft()) PreviewDraft();
                    break;
            }
            RequestRender();
            return true;
        }

        private void AskNumber(string fieldId)
        {
            if (!TryArmField(fieldId)) { _notice = LastFieldError; Render(); return; }
            var field = ArmedField; int generation = _generation; string back = _screen; _screen = "numeric";
            _panel.PromptText(field.Label, "Valore da " + Fmt(field.Min) + " a " + Fmt(field.Max) + (field.Unit == QuantityUnit.Degrees ? " gradi" : " mm")
                + ". Separatore decimale: punto.", field.Value.ToString(CultureInfo.InvariantCulture), text =>
            {
                if (generation != _generation) return;
                _screen = back;
                if (!double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                    || !SetArmedField(fieldId, value)) _notice = LastFieldError ?? "Valore non valido.";
                else _notice = "";
                Render();
            }, () => { _screen = back; Render(); });
        }

        private void Page(string screen)
        {
            if (_armedField != null && _armedFieldScreen != screen) DisarmField();
            _screen = screen; _page = 0; Render();
        }

        // ---------------------------------------------------------------- sketches

        private SketchSnapshot Snapshot(string name) => _designContext?.SketchSnapshots.FirstOrDefault(s => s.Name == name);

        /// <summary>chiuso / aperto / vuoto when certain, otherwise "da verificare" (the server validates on preview).</summary>
        private string ProfileHint(string name)
        {
            var snapshot = Snapshot(name);
            if (snapshot == null) return "da verificare";
            if (snapshot.Elements.Count == 0) return snapshot.UnsupportedEntities > 0 ? "da verificare" : "vuoto";
            if (snapshot.UnsupportedEntities > 0) return "da verificare";
            var ends = new Dictionary<(long, long), int>();
            void Touch(CadPoint p)
            {
                var key = ((long)Math.Round(p.X * 1000), (long)Math.Round(p.Y * 1000));
                ends[key] = ends.TryGetValue(key, out var n) ? n + 1 : 1;
            }
            foreach (var e in snapshot.Elements)
                if (e.Shape == SketchShape.Line) { Touch(e.A); Touch(e.B); }
            return ends.Values.Any(n => n % 2 != 0) ? "aperto" : "chiuso";
        }

        private void ShowProfile(string name)
        {
            var snapshot = Snapshot(name);
            if (snapshot == null) { _geometry.Clear(); return; }
            _geometry.ShowEdges(snapshot.Elements.Select((e, i) => new DesignEdge("profile:" + i, "line", e.Outline().Select(snapshot.Frame.ToModel))));
        }

        // ---------------------------------------------------------------- panel

        private static string Fmt(double value) => value.ToString("0.###", Italian);

        private void RequestRender()
        {
            if (_manip != null && _manip.Dragging) _renderPending = true; // per-frame draft edits must not rebuild the panel every frame
            else Render();
        }

        private void Add(List<(string, Action)> actions, string label, Action action, bool enabled = true, string id = null)
        {
            actions.Add((label, action)); _enabled[label] = enabled;
            if (id != null) _ids[label] = id;
        }
        private void AddCommand(List<(string, Action)> actions, string id, string label, Action action) =>
            Add(actions, label, action, IsEnabled(id), id);

        private string FlatStatus()
        {
            var c = _mode.Context;
            string measures = c != null && c.FlatPattern.Exists
                ? Fmt(c.FlatPattern.LengthMm ?? 0) + " × " + Fmt(c.FlatPattern.WidthMm ?? 0) + " mm, " + (c.FlatPattern.BendCount ?? 0) + " pieghe" : null;
            if (_flat == null) return "—";
            switch (_flat.State)
            {
                case FlatPatternState.Loading: return "caricamento…";
                case FlatPatternState.Ready: return "visibile" + (_flat.Detached ? " (staccato)" : "") + (measures != null ? " • " + measures : "");
                case FlatPatternState.StaleReadOnly: return "revisione " + _flat.RevisionLabel + ", sola ispezione";
                case FlatPatternState.Unavailable: return "non disponibile: " + _flat.Reason;
                default:
                    if (c == null || !c.IsComplete) return "—";
                    return c.FlatPattern.Exists ? "presente (" + measures + "), non mostrato" : "assente";
            }
        }

        private string PreviewStatus()
        {
            switch (_session?.Status)
            {
                case DesignStatus.Draft: return "bozza da calcolare";
                case DesignStatus.Previewing: return "calcolo…";
                case DesignStatus.PreviewReady: return _session.CanApply ? "verificata" : "in attesa di visualizzazione";
                case DesignStatus.Committing: return "applicazione…";
                case DesignStatus.RefreshRequired: return "documento da aggiornare";
                case DesignStatus.Error: return "errore";
                default: return "nessuna";
            }
        }

        private string StatusBlock()
        {
            var c = _mode.Context; var sb = new StringBuilder();
            sb.Append("Regola: ").Append(c?.Rule ?? "—")
              .Append(" • Spessore: ").Append(c?.ThicknessMm.HasValue == true ? Fmt(c.ThicknessMm.Value) + " mm" : "—")
              .Append(" • Pieghe: ").Append(c?.BendCount?.ToString() ?? "—");
            sb.Append("\nSviluppo: ").Append(FlatStatus());
            sb.Append("\nAnteprima: ").Append(PreviewStatus()).Append(" • Applica: ").Append(_session?.CanApply == true ? "disponibile" : "non disponibile");
            return sb.ToString();
        }

        private void Render()
        {
            if (!Active || _screen == "numeric" || _panel == null) return;
            _ids.Clear(); _enabled.Clear();
            var actions = new List<(string, Action)>();
            string title = "LAMIERA", headline = null;
            var status = _session?.Status;
            bool locked = status == DesignStatus.Committing || status == DesignStatus.Previewing;
            bool interactive = false;
            if (_kind != "part") headline = "Lamiera richiede una parte in lamiera attiva in Inventor.";
            else if (_session == null) headline = "PC non collegato.";
            else if (!_online) headline = "Offline: Lamiera è in sola lettura. Nessuna modifica CAD.";
            else if (_busy) headline = "Lettura del contesto lamiera…";
            else if (status == DesignStatus.RefreshRequired)
            {
                headline = _pendingMutations > 0 ? "Attendo la conclusione della richiesta CAD precedente. Nessun nuovo comando verrà inviato."
                    : _session.CommitOutcomeUnknown ? "Esito della modifica CAD non confermato. Controlla il modello in Inventor prima di proseguire. Il comando non verrà ripetuto."
                    : "Aggiornamento del documento necessario prima di un altro comando.";
                if (_pendingMutations == 0) Add(actions, _session.CommitOutcomeUnknown ? "Ho controllato il CAD" : "Aggiorna documento", ReviewUnknown);
            }
            else if (!_mode.CanWrite)
            {
                headline = _mode.Reason ?? "Lamiera non disponibile.";
                Add(actions, "Riprova", LoadContext, _online && !_busy);
            }
            else if (_designContext == null)
            {
                headline = "Riferimenti CAD non disponibili.";
                Add(actions, "Riprova", LoadContext, !_busy);
            }
            else interactive = true;

            string body = headline != null ? headline + "\n" : "";
            if (interactive)
            {
                switch (_screen)
                {
                    case "flange":
                        title = "Flangia";
                        body += "Bordi selezionati: " + _flange.EdgeIds.Count + ". Trigger su un bordo: aggiungi o togli.\n"
                            + "Grip + Trigger sul pomello: altezza (solo con Flangia armata). Grip semplice muove la vista.\n"
                            + "Altezza " + Fmt(_flange.HeightMm) + " mm • Angolo " + Fmt(_flange.AngleDegrees) + "° • Riferimento " + DatumLabel(_flange.Datum) + ".\n";
                        if (_manip != null && _manip.Visible && !_manip.AxisFromFaces) body += "Direzione del pomello stimata: verifica il risultato in anteprima.\n";
                        if (_armedField != null) body += "Campo attivo: " + ArmedField?.Label + ".\n";
                        Add(actions, "Altezza: " + Fmt(_flange.HeightMm) + " mm", () => AskNumber(FieldFlangeHeight), !locked);
                        Add(actions, "Angolo: " + Fmt(_flange.AngleDegrees) + " °", () => AskNumber(FieldFlangeAngle), !locked);
                        Add(actions, "Riferimento: " + DatumLabel(_flange.Datum), CycleDatum, !locked);
                        if (_flange.EdgeIds.Count > 0) Add(actions, "Svuota bordi", () => { _flange.Clear(); Render(); }, !locked);
                        break;
                    case "rule":
                        title = "Regola / Spessore";
                        body += "Regola attiva: " + (_mode.Context.Rule ?? "—") + " • Spessore: " + Fmt(_mode.Context.ThicknessMm ?? 0) + " mm.\n"
                            + "Scegli una regola o imposta lo spessore: è una bozza separata, poi Anteprima e Applica.\n"
                            + (_ruleName != null ? "Regola scelta: " + _ruleName + ".\n" : "");
                        var rules = _mode.Context.AvailableRules.ToArray();
                        foreach (var rule in rules.Skip(_page * 6).Take(6))
                        { var name = rule; Add(actions, (name == _mode.Context.Rule ? "● " : "") + name, () => { _ruleName = name; Preview(); }, !locked); }
                        if (_page > 0) Add(actions, "Precedenti", () => { _page--; Render(); });
                        if ((_page + 1) * 6 < rules.Length) Add(actions, "Successivi", () => { _page++; Render(); });
                        Add(actions, "Spessore: " + Fmt(_thicknessMm ?? _mode.Context.ThicknessMm ?? 0) + " mm", () => AskNumber(FieldThickness), !locked);
                        break;
                    case "sketches":
                        title = _mode.Armed == SheetMetalCommand.Cut ? "Taglio da schizzo" : "Faccia da schizzo";
                        body += (_mode.Armed == SheetMetalCommand.Cut ? "Scegli lo schizzo con il profilo del taglio.\n" : "Scegli lo schizzo con il profilo chiuso della faccia base.\n")
                            + (_sketchName != null ? "Profilo usato: " + _sketchName + " (" + ProfileHint(_sketchName) + "), in evidenza sul modello.\n" : "");
                        var sketches = _designContext.Sketches.ToArray();
                        foreach (var sketch in sketches.Skip(_page * 6).Take(6))
                        { var name = sketch.Name; Add(actions, name + " • " + ProfileHint(name), () => ChooseSketch(name), !locked); }
                        if (_page > 0) Add(actions, "Precedenti", () => { _page--; Render(); });
                        if ((_page + 1) * 6 < sketches.Length) Add(actions, "Successivi", () => { _page++; Render(); });
                        if (sketches.Length == 0) body += "Nessuno schizzo nel documento: crealo da Modello 3D / Schizzo.\n";
                        break;
                    case "cut":
                        title = "Taglio da schizzo";
                        body += "Profilo usato: " + _sketchName + " (" + ProfileHint(_sketchName) + "), in evidenza sul modello.\n"
                            + "Estensione " + (_extent == "thickness" ? "spessore" : "passante") + " • Direzione " + DirectionLabel(_direction)
                            + " • Attraverso pieghe " + (_acrossBends ? "sì" : "no") + ".\n";
                        Add(actions, "Estensione: " + (_extent == "thickness" ? "spessore" : "passante"), () =>
                        { _extent = _extent == "thickness" ? "through_all" : "thickness"; if (_extent == "through_all") _acrossBends = false; Preview(); }, !locked);
                        Add(actions, "Direzione: " + DirectionLabel(_direction), () =>
                        { var options = new[] { "positive", "negative", "symmetric" }; _direction = options[(Array.IndexOf(options, _direction) + 1) % 3]; Preview(); }, !locked);
                        Add(actions, "Attraverso pieghe: " + (_acrossBends ? "sì" : "no"), () => { _acrossBends = !_acrossBends; Preview(); }, !locked && _extent == "thickness");
                        Add(actions, "Cambia schizzo", () => Page("sketches"), !locked);
                        break;
                    case "flat":
                        title = "Crea sviluppo";
                        body += "Crea sviluppo modifica il CAD: calcola l'anteprima, poi Applica. Se esiste già non viene ricostruito.\n";
                        break;
                    default:
                        body += "Scegli un comando. Grip muove la vista. Il CAD cambia solo con Applica dopo un'anteprima.\n";
                        AddCommand(actions, CommandIds.SheetMetalRule, "Regola / Spessore", OpenRule);
                        AddCommand(actions, CommandIds.SheetMetalFace, "Faccia da schizzo", () => OpenSketchPick(SheetMetalCommand.Face));
                        AddCommand(actions, CommandIds.Flange, "Flangia", ArmFlange);
                        AddCommand(actions, CommandIds.SheetMetalCut, "Taglio da schizzo", () => OpenSketchPick(SheetMetalCommand.Cut));
                        AddCommand(actions, CommandIds.FlatPatternCreate, "Crea sviluppo", StartFlatPattern);
                        if (_flat != null && _flat.IsVisible) Add(actions, "Nascondi sviluppo", () => _flat.Hide());
                        else if (_mode.Context.FlatPattern.Exists) Add(actions, "Mostra sviluppo", ShowFlat, _online && _flat != null && _flat.State != FlatPatternState.Loading);
                        if (_flat != null && _flat.IsVisible)
                            Add(actions, _flat.Detached ? "Riaggancia sviluppo" : "Stacca sviluppo", () => { if (_flat.Detached) _flat.Attach(); else _flat.Detach(); });
                        if (status == DesignStatus.Empty)
                        {
                            AddCommand(actions, CommandIds.Undo, "Annulla modifica XR", () => ApplyHistory(false));
                            AddCommand(actions, CommandIds.Redo, "Ripeti modifica XR", () => ApplyHistory(true));
                        }
                        Add(actions, "Aggiorna", LoadContext, !_busy);
                        break;
                }
                if (_screen != "tools")
                {
                    Add(actions, "Anteprima", Preview, !locked && _session.CanEdit && _mode.Armed != SheetMetalCommand.None);
                    AddCommand(actions, CommandIds.Apply, "Applica", ApplyPressed);
                    AddCommand(actions, CommandIds.CancelDraft, "Annulla comando", CancelCommand);
                }
            }
            body += "\n" + StatusBlock();
            if (!string.IsNullOrEmpty(_notice)) body += "\n" + _notice;
            if (!string.IsNullOrEmpty(_mode.LastError) && _mode.LastError != _notice) body += "\n" + _mode.LastError;
            if (!string.IsNullOrEmpty(_session?.Error))
            {
                string error = _session.Error.Length > 240 ? _session.Error.Substring(0, 240) + "…" : _session.Error;
                body += "\nComando non completato: " + error + "\nLa bozza resta modificabile: correggi i parametri e riprova.";
                if (_geometry != null && _geometry.HasErrorContext) body += "\nIn arancione: geometria da controllare.";
                Add(actions, "Dettagli errore", () => { _panel.ShowMessage("Dettagli", _session.Error); _panel.SetActions(("Indietro", Render)); });
            }
            Add(actions, "Modello 3D / Schizzo", () => DesignRequested?.Invoke(), !locked);
            Add(actions, "Ispeziona", Close);
            Add(actions, _pinned ? "Sblocca pannello" : "Pin pannello", () => { _pinned = !_pinned; Render(); });
            _panel.ShowMessage(title, body); _panel.SetActions(actions.ToArray());
            foreach (var button in _panel.GetComponentsInChildren<Button>())
            {
                string label = button.GetComponentInChildren<Text>()?.text;
                if (label == null || !_enabled.TryGetValue(label, out bool enabled)) continue;
                if (_ids.TryGetValue(label, out string id)) button.gameObject.name = id;
                button.interactable = enabled;
            }
        }

        private static string DatumLabel(string datum) => datum == "inner" ? "interno" : datum == "tangent" ? "tangente" : "esterno";
        private static string DirectionLabel(string direction) => direction == "negative" ? "negativa" : direction == "symmetric" ? "simmetrica" : "positiva";

        private void CycleDatum()
        {
            var options = new[] { "outer", "inner", "tangent" };
            _flange.SetDatum(options[(Array.IndexOf(options, _flange.Datum) + 1) % 3]);
            PreviewDraft();
            Render();
        }

        // ---------------------------------------------------------------- controller

        private void Update()
        {
            if (_renderPending && Time.unscaledTime >= _nextRender) { _renderPending = false; _nextRender = Time.unscaledTime + 0.15f; Render(); }
            if (!Active || !_visible || _ray == null || _ray.Origin == null) return;
            bool tracked = OVRInput.IsControllerConnected(_ray.Controller) && OVRInput.GetControllerPositionTracked(_ray.Controller)
                && OVRInput.GetControllerOrientationTracked(_ray.Controller);
            if (!tracked) { EndManipulation(); _grab = null; return; }
            var ray = new Ray(_ray.Origin.position, _ray.Origin.forward);
            var ui = EventSystem.current?.currentInputModule as ControllerUiInputModule;
            bool overUi = ui != null && ui.CurrentHit.isValid;
            bool grip = OVRInput.Get(OVRInput.Button.PrimaryHandTrigger, _ray.Controller);
            bool trigger = OVRInput.Get(OVRInput.Button.PrimaryIndexTrigger, _ray.Controller);
            bool canManipulate = _session?.CanEdit == true && _mode.CanWrite && _mode.Armed == SheetMetalCommand.Flange
                && _flange.EdgeIds.Count > 0 && _manip.Visible && _screen != "numeric";
            if (_manip.Dragging)
            {
                if (!grip || !trigger || !canManipulate) EndManipulation();
                else
                {
                    _flange.ApplyManipulatorHeight(_manip.Drag(CadCoordinates.FromWorld(_view.transform, _ray.Origin.position)));
                    return;
                }
            }
            else if (grip && trigger && canManipulate && !overUi)
            {
                // Only Grip+Trigger on the knob edits the draft; Grip alone (below) only moves the view.
                if (_manip.TryBeginDrag(ray, CadCoordinates.FromWorld(_view.transform, _ray.Origin.position))) { _grab = null; return; }
            }
            if (grip && !trigger)
            {
                if (OVRInput.GetDown(OVRInput.Button.PrimaryHandTrigger, _ray.Controller)) BeginGrab(ray, ui, overUi);
                if (_grab != null) MoveGrab();
                return;
            }
            _grab = null; _grabFlat = false;
            if (overUi || _screen == "numeric" || _session?.CanEdit != true || grip) return;
            if (!OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, _ray.Controller)) return;
            if (_mode.Armed == SheetMetalCommand.Flange && _designContext != null && _mode.CanWrite && SceneCurrent)
            {
                string edge = CadCoordinates.PickEdge(_view.transform, _designContext.Edges, ray, requireVisible: true);
                if (edge != null) { _flange.ToggleEdge(edge); _notice = ""; }
            }
        }

        private void EndManipulation()
        {
            if (_manip == null || !_manip.Dragging) return;
            _manip.EndDrag();
            _flange.ReleaseManipulator();
            _renderPending = false;
            Render();
            PreviewPending();
        }

        private void BeginGrab(Ray ray, ControllerUiInputModule ui, bool overUi)
        {
            _grab = null; _grabFlat = false;
            if (overUi && ui.CurrentHit.gameObject.transform.IsChildOf(_panel.transform)) _grab = _panel.transform;
            else if (!overUi && _flat != null && _flat.Detached && _flat.IsVisible && _flatDisplay.HitTest(ray) && _flatDisplay.MeshRoot != null)
            { _grab = _flatDisplay.MeshRoot; _grabFlat = true; }
            else if (!overUi && CadRaycaster.TryPick(ray, 20, out _, out _, out _)) _grab = _view.transform;
            if (_grab == null) return;
            _grabPosition = _ray.Origin.InverseTransformPoint(_grab.position);
            _grabRotation = Quaternion.Inverse(_ray.Origin.rotation) * _grab.rotation;
        }

        private void MoveGrab()
        {
            var position = _ray.Origin.TransformPoint(_grabPosition);
            if (_grabFlat)
            {
                // View state only: the offset lives in FlatPatternView and never reaches CAD.
                if (_flat == null || !_flat.Detached || !_flat.IsVisible) { _grab = null; _grabFlat = false; return; }
                var offset = _view.transform.InverseTransformPoint(position) - _flatDisplay.DefaultLocalPosition();
                _flat.MoveLocal(offset.x, offset.y, offset.z);
                return;
            }
            _grab.SetPositionAndRotation(position, _ray.Origin.rotation * _grabRotation);
        }

        private void OnDestroy()
        {
            _reads.Cancel(); _reads.Dispose();
            _flange.Changed -= OnFlangeChanged; _mode.Changed -= OnModeChanged;
            _flangeBinding?.Dispose();
            if (_session != null) { _session.Changed -= SessionChanged; _session.Dispose(); }
            if (_flat != null) { _flat.Changed -= OnFlatChanged; _flat.Dispose(); }
            if (_panel != null) Release(_panel.gameObject);
            if (_previewView != null) Release(_previewView.gameObject);
            if (_geometry != null) Release(_geometry.gameObject);
            if (_manip != null) Release(_manip.gameObject);
        }

        private static void Release(GameObject item)
        {
            item.SetActive(false);
            if (Application.isPlaying) Destroy(item); else DestroyImmediate(item);
        }
    }
}
