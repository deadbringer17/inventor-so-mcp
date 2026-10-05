using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Session;
using InventorXrSo.Core.Ui;
using InventorXrSo.Core.Voice;
using InventorXrSo.Unity.Scene;
using InventorXrSo.Unity.Ui;
using InventorXrSo.Xr.Input;
using UnityEngine;
using UnityEngine.EventSystems;
using UiSelectionKind = InventorXrSo.Core.Ui.SelectionKind;

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
    /// Quest M5/M6 Lamiera: sheet-metal authoring on top of the shared Design draft/preview/Apply cycle. No floating panel:
    /// the actions are declared to the palette (<see cref="IActionProvider"/>, see LamieraActions.cs) and the commit bar is
    /// the only place that applies. Nothing reaches CAD without a rendered, validated preview and the physical Applica button.
    /// </summary>
    [DefaultExecutionOrder(115)]
    public sealed partial class LamieraWorkspace : MonoBehaviour, IActionProvider
    {
        public const string FieldFlangeHeight = "flange.height_mm";
        public const string FieldFlangeAngle = "flange.angle_deg";
        public const string FieldThickness = "rule.thickness_mm";
        private static readonly CultureInfo Italian = CultureInfo.GetCultureInfo("it-IT");

        private CadSceneView _view;
        private SelectionVisuals _selection;
        private ControllerRay _ray;
        private Transform _head;
        private UiShell _shell;
        private ActionCatalog _catalog;
        private Workbench _bench;
        private XrInput _input;
        private ChipView _heightChip, _angleChip, _thicknessChip;
        private RingView _ring;
        private NumericEntry _heightEntry, _angleEntry, _thicknessEntry, _ask;
        private bool _syncingEntries;
        private string _ringEdge, _lastErrorShown, _lastHeadline, _lastFlatReason;
        private bool _statusAnnounced;
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
        // Handle direction confirmed against the preview; valid only for the draft key it was measured on.
        private string _calibKey; private CadPoint _calibAxis;
        private SheetMetalContext _sheetContext;
        private DesignContext _designContext;
        private DesignHistory _history;
        private DocumentState _state, _sceneState;
        private string _kind, _screen = "tools", _notice = "", _renderedPlan;
        private string _sketchName, _ruleName, _extent = "thickness", _direction = "positive";
        private double? _thicknessMm;
        private bool _acrossBends, _pendingShowFlat, _wasPrimary;
        private string _armedField, _armedFieldScreen;
        private bool _online, _visible, _busy, _reviewAfterRebind, _renderPending;
        private int _pendingMutations, _generation;
        private float _nextRender;
        private CancellationTokenSource _reads = new CancellationTokenSource();
        private Transform _grab;
        private bool _grabFlat, _gripHeld, _twoHandActive;
        private bool GripDown => _gripHeld || (_input != null && _input.PenGripHeld);
        private Vector3 _grabPosition;
        private Quaternion _grabRotation;
        private Transform _leftHand;
        private Vector3 _twoHandVector, _twoHandMid, _twoHandRootPosition;
        private Quaternion _twoHandRootRotation;
        private float _twoHandRootScale;
        private int _hoverKey;
        private const float ZoomRatePerSecond = 1.5f;

        public bool Active { get; private set; }
        public bool RequiresCadReview => _reviewAfterRebind || _pendingMutations > 0 || _session?.Status == DesignStatus.Committing || _session?.Status == DesignStatus.RefreshRequired;
        public Func<bool> CanEnter { get; set; }
        public event Action<bool> ActiveChanged;
        /// <summary>Raised only when an open workspace closes: the shell restores the Inspect placement of the model.</summary>
        public event Action Closed;
        /// <summary>Lamiera becomes (or stops being) the primary mode for the active document.</summary>
        public event Action<bool> PrimaryChanged;
        public event Action ArmedFieldChanged;
        /// <summary>Notices and error detail for the HUD (the commit bar only carries a short message).</summary>
        public event Action<string> HudMessage;
        /// <summary>Last notice, also sent to <see cref="HudMessage"/> when non-empty.</summary>
        public string Notice => _notice;
        /// <summary>Lamiera no longer has a floating panel: voice labels come from the declared actions.</summary>
        public HomePanel VoicePanel => null;
        /// <summary>The numeric entry currently waiting for the keypad (or dictation), if any.</summary>
        public NumericEntry ActiveEntry => _ask;
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

        /// <summary>
        /// Shell parts the workspace draws on: palette/commit bar/HUD through the catalog, plus chips and ring created here.
        /// The bench and the input are optional (not present in headless tests). <paramref name="sheet"/> is accepted for symmetry
        /// with Progettazione; Lamiera has no sketch sheet.
        /// </summary>
        public void Attach(UiShell shell, Workbench bench = null, SketchSheetView sheet = null, XrInput input = null)
        {
            _shell = shell; _catalog = shell?.Catalog; _bench = bench;
            AttachInput(input);
            if (shell == null || _head == null) return;
            var eye = _head.GetComponent<Camera>();
            if (_ring == null)
            {
                _ring = RingView.Create(null);
                XrUi.MakeInteractive(_ring.Canvas, eye);
            }
            if (_heightChip == null) _heightChip = MakeChip(eye, FieldFlangeHeight, "Altezza");
            if (_angleChip == null) _angleChip = MakeChip(eye, FieldFlangeAngle, "Angolo");
            if (_thicknessChip == null) _thicknessChip = MakeChip(eye, FieldThickness, "Spessore");
        }

        private ChipView MakeChip(Camera eye, string fieldId, string label)
        {
            var chip = ChipView.Create(null);
            XrUi.MakeInteractive(chip.Canvas, eye);
            chip.Face(_head);
            chip.Bind(EntryFor(fieldId), label);
            chip.Tapped += () => AskNumber(fieldId);
            chip.Canvas.gameObject.SetActive(false);
            return chip;
        }

        /// <summary>
        /// All controller input arrives as semantic events of <see cref="XrInput"/>; haptics go through <see cref="Haptics"/>.
        /// Without an input (tests, headless) the workspace simply never receives pen events and never throws.
        /// </summary>
        private void AttachInput(XrInput input)
        {
            DetachInput();
            _input = input;
            if (_input == null) return;
            _leftHand = _head != null && _head.parent != null
                ? _head.parent.Find("LeftHandAnchor/LeftControllerAnchor") ?? _head.parent.Find("LeftHandAnchor") : null;
            _input.PenPressed += OnPenPressed;
            _input.PenReleased += OnPenReleased;
            _input.TrackingLost += OnTrackingLost;
            _input.PenGrabStarted += OnGrabStarted;
            _input.PenGrabEnded += OnGrabEnded;
            _input.TwoHandChanged += OnTwoHandChanged;
            _input.Back += Back;
            _input.Fit += FitView;
            _input.Recenter += RecenterView;
            _input.StepDelta += OnStepDelta;
            _input.StepSizeDelta += OnStepSizeDelta;
            _input.Zoom += OnZoom;
        }

        private void DetachInput()
        {
            if (_input == null) return;
            _input.PenPressed -= OnPenPressed;
            _input.PenReleased -= OnPenReleased;
            _input.TrackingLost -= OnTrackingLost;
            _input.PenGrabStarted -= OnGrabStarted;
            _input.PenGrabEnded -= OnGrabEnded;
            _input.TwoHandChanged -= OnTwoHandChanged;
            _input.Back -= Back;
            _input.Fit -= FitView;
            _input.Recenter -= RecenterView;
            _input.StepDelta -= OnStepDelta;
            _input.StepSizeDelta -= OnStepSizeDelta;
            _input.Zoom -= OnZoom;
            _input = null;
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
            ResetDraft(); RefreshMode(); Refresh();
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
            Refresh();
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
            _screen = "tools"; _lastHeadline = null; _statusAnnounced = false;
            ActiveChanged?.Invoke(true);
            _flatDisplay.SetPresented(true);
            PlaceWorkbench(); LoadContext(); Refresh();
        }

        public void Close()
        {
            bool was = Active;
            // The capture of the flange knob ends with the workspace (M5-08): only the local draft was ever touched.
            if (_manip != null && _manip.Dragging) { _manip.EndDrag(); _renderPending = false; }
            EndViewGrabs();
            Active = false; CancelReads();
            if (_session != null && _session.Status != DesignStatus.Committing && _session.Status != DesignStatus.RefreshRequired) _session.Cancel();
            ResetDraft(); _selection?.Clear();
            _shell?.Palette.HideKeypad(); _ask = null;
            _flatDisplay?.SetPresented(false);
            if (_ray != null) _ray.CanPick = _visible;
            if (was) _bench?.Release();
            UpdateBar();
            ActiveChanged?.Invoke(false);
            if (was) Closed?.Invoke();
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
            _ask = null; _lastErrorShown = null;
            _grab = null; _grabFlat = false; _gripHeld = false; _twoHandActive = false; _renderedPlan = null; _screen = "tools";
            _previewView?.Clear(); _geometry?.Clear(); _manip?.Hide();
            HideRing(); ClosePicker(false);
            UpdateChips();
        }

        private void OnModeChanged()
        {
            bool primary = _mode.IsPrimary;
            if (primary != _wasPrimary) { _wasPrimary = primary; PrimaryChanged?.Invoke(primary); }
            RequestRender();
        }

        private void OnFlatChanged()
        {
            // The reason a flat pattern cannot be shown (budget, revision, missing asset) goes to the HUD once.
            if (_flat != null && _flat.State == FlatPatternState.Unavailable && !string.IsNullOrEmpty(_flat.Reason))
            {
                if (_flat.Reason != _lastFlatReason) { _lastFlatReason = _flat.Reason; SetNotice("Sviluppo non disponibile: " + _flat.Reason); }
            }
            else _lastFlatReason = null;
            RequestRender();
        }

        private void OnFlangeChanged()
        {
            RefreshFlangeVisuals();
            SyncEntries();
            RequestRender();
        }

        // ---------------------------------------------------------------- notices, bar, chips, ring

        private void SetNotice(string text)
        {
            _notice = text ?? "";
            if (_notice.Length > 0) HudMessage?.Invoke(_notice);
        }

        private void RequestRender()
        {
            if (_manip != null && _manip.Dragging) _renderPending = true; // per-frame draft edits must not rebuild the shell every frame
            else Refresh();
        }

        /// <summary>Content or enablement changed: rebuild the declared actions, the bar and the chips, and tell the catalog.</summary>
        private void Refresh()
        {
            _actions = null;
            AnnounceAvailability();
            UpdateBar();
            UpdateChips();
            _catalog?.NotifyChanged();
        }

        private const string ShortError = "Comando non completato. Correggi e riprova.";

        /// <summary>The screens that edit a draft: Preview, Apply and Cancel live on the commit bar.</summary>
        private bool InDraftScreen => _screen != "tools";

        private void UpdateBar()
        {
            var s = _session;
            bool working = s != null && (s.Status == DesignStatus.Previewing || s.Status == DesignStatus.Committing);
            CommitBar.Update(new CommitBarInputs(
                online: _online,
                outcomeUnknown: s?.CommitOutcomeUnknown == true,
                refreshRequired: s?.Status == DesignStatus.RefreshRequired,
                busy: working || _pendingMutations > 0 || (_busy && InDraftScreen),
                hasPreview: s != null && s.Status == DesignStatus.PreviewReady && s.CanApply && _mode.CanWrite && SceneCurrent,
                hasDraft: s != null && InDraftScreen,
                error: s?.Status == DesignStatus.Error ? ShortError : ""), Time.unscaledTimeAsDouble);
        }

        /// <summary>The reason Lamiera cannot be used at all goes to the HUD once (the palette actions carry it as disabled reason).</summary>
        private string Headline()
        {
            if (!Active) return null;
            if (_kind != "part") return "Lamiera richiede una parte in lamiera attiva in Inventor.";
            if (_session == null) return "PC non collegato.";
            if (!_online) return "Offline: Lamiera è in sola lettura. Nessuna modifica CAD.";
            if (_busy || _session.Status == DesignStatus.RefreshRequired) return null;
            if (!_mode.CanWrite) return _mode.Reason ?? "Lamiera non disponibile.";
            return null;
        }

        private void AnnounceAvailability()
        {
            string headline = Headline();
            if (headline != null && headline != _lastHeadline) SetNotice(headline);
            _lastHeadline = headline;
        }

        private void AnnounceStatus()
        {
            if (!Active || _statusAnnounced || !_mode.CanWrite || _mode.Context == null) return;
            _statusAnnounced = true;
            HudMessage?.Invoke(StatusSummary());
        }

        /// <summary>Rule, thickness, bends and flat pattern of the active document, as shown on the HUD when Lamiera opens.</summary>
        public string StatusSummary()
        {
            var c = _mode.Context; var sb = new StringBuilder();
            sb.Append("Regola: ").Append(c?.Rule ?? "—")
              .Append(" • Spessore: ").Append(c?.ThicknessMm.HasValue == true ? Fmt(c.ThicknessMm.Value) + " mm" : "—")
              .Append(" • Pieghe: ").Append(c?.BendCount?.ToString() ?? "—");
            sb.Append("\nSviluppo: ").Append(FlatStatus());
            return sb.ToString();
        }

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

        // ---- numeric entries (chips, keypad, dictation and thumbstick share them)

        private NumericEntry EntryFor(string fieldId)
        {
            switch (fieldId)
            {
                case FieldFlangeHeight: return _heightEntry ?? (_heightEntry = MakeEntry(fieldId));
                case FieldFlangeAngle: return _angleEntry ?? (_angleEntry = MakeEntry(fieldId));
                case FieldThickness: return _thicknessEntry ?? (_thicknessEntry = MakeEntry(fieldId));
                default: return null;
            }
        }

        private NumericEntry MakeEntry(string fieldId)
        {
            var field = Describe(fieldId);
            var entry = new NumericEntry(fieldId, field.Unit, field.Value, field.Min, field.Max);
            entry.Changed += () => OnEntryChanged(entry);
            // Confirming the value the draft already has changes nothing, but a draft without a preview still deserves one (PreviewDraft only acts on a Draft).
            entry.Committed += () => { if (!_syncingEntries && FieldApplies(entry.Id) && Idle) PreviewDraft(); };
            return entry;
        }

        private void SyncEntries()
        {
            _syncingEntries = true;
            try { SyncEntry(_heightEntry); SyncEntry(_angleEntry); SyncEntry(_thicknessEntry); }
            finally { _syncingEntries = false; }
        }

        private void SyncEntry(NumericEntry entry)
        {
            if (entry == null || entry.Editing) return;
            var field = Describe(entry.Id);
            if (double.IsNaN(field.Value) || double.IsInfinity(field.Value)) return;
            entry.SetValue(Math.Max(field.Min, Math.Min(field.Max, field.Value)), out _);
        }

        // Keypad confirmation, thumbstick step and dictation arrive here; typing in the keypad does not.
        private void OnEntryChanged(NumericEntry entry)
        {
            if (_syncingEntries || entry.Editing) return;
            if (_ask == entry) _ask = null;   // confirmed or cancelled from the keypad
            var field = Describe(entry.Id);
            if (field == null || entry.Value == field.Value) return;
            LastFieldError = null;
            if (!FieldApplies(entry.Id) || !Idle)
            {
                LastFieldError = "Modifica non disponibile ora.";
                SetNotice(LastFieldError);
                SyncEntries();
                return;
            }
            ApplyField(entry.Id, entry.Value);
            RequestRender();
        }

        private bool FieldApplies(string fieldId) => fieldId == FieldThickness ? _mode.Armed == SheetMetalCommand.Rule : _mode.Armed == SheetMetalCommand.Flange;

        /// <summary>The palette closed the keypad (Annulla / X): the pending prompt is gone.</summary>
        private void DropClosedKeypad()
        {
            if (_ask != null && _shell != null && !_shell.Palette.KeypadVisible) _ask = null;
        }

        private void UpdateChips()
        {
            if (_heightChip == null) return;
            bool editable = Active && _session?.CanEdit == true && _designContext != null && _mode.CanWrite;
            bool flange = editable && _mode.Armed == SheetMetalCommand.Flange && _flange.EdgeIds.Count > 0 && _manip != null && _manip.Visible;
            bool rule = editable && _mode.Armed == SheetMetalCommand.Rule && _view != null;
            _heightChip.Canvas.gameObject.SetActive(flange);
            _angleChip.Canvas.gameObject.SetActive(flange);
            _thicknessChip.Canvas.gameObject.SetActive(rule);
            bool modified = _session?.Status == DesignStatus.Draft;
            if (flange)
            {
                SyncEntries();
                _heightChip.Place(_manip.KnobWorldPosition + Vector3.up * 0.05f);
                _angleChip.Place(_manip.OriginWorldPosition + Vector3.up * 0.05f - (_head != null ? _head.right * 0.12f : Vector3.zero));
                _heightChip.Armed = _armedField == FieldFlangeHeight; _heightChip.SetModified(modified);
                _angleChip.Armed = _armedField == FieldFlangeAngle; _angleChip.SetModified(modified);
            }
            if (rule)
            {
                SyncEntries();
                var bounds = ScenePlacement.LocalBounds(_view.transform);
                _thicknessChip.Place(_view.transform.TransformPoint(bounds.center) + Vector3.up * 0.15f);
                _thicknessChip.Armed = _armedField == FieldThickness; _thicknessChip.SetModified(modified);
            }
        }

        private void ShowRing(UiSelectionKind kind, Vector3 worldPoint)
        {
            if (_ring == null || _head == null) return;
            var actions = ContextActions(kind).ToArray();
            if (actions.Length == 0) { HideRing(); return; }
            _ring.Show(worldPoint + Vector3.up * 0.01f, actions, _head);
        }

        private void HideRing()
        {
            _ringEdge = null;
            _ring?.Hide();
        }

        private void SelectEdge(string edgeId)
        {
            var edge = _designContext?.Edges.FirstOrDefault(e => e.Id == edgeId);
            if (edge == null) return;
            _selection?.Clear();
            _geometry.ShowEdges(new[] { edge });
            SetNotice("Spigolo selezionato.");
            ShowRing(UiSelectionKind.Edge, _view.transform.TransformPoint(CadCoordinates.ToLocal(FlangeManipulator.Midpoint(edge.PointsMm))));
            _ringEdge = edgeId;
        }

        /// <summary>M9: nothing for X to back out of; only then a held X means "Torna". Mirrors the <see cref="Back"/> chain.</summary>
        public bool AtRest => Active && !(_shell != null && (_shell.Palette.KeypadVisible || _shell.Palette.InTabGroup))
            && _ask == null && !(_ring != null && _ring.Visible) && _picker == null && _armedField == null && !InDraftScreen && _mode.Armed == SheetMetalCommand.None && !(_manip != null && _manip.Dragging)
            && !Locked && !RequiresCadReview && (_session == null || _session.Status == DesignStatus.Empty);

        /// <summary>
        /// X: closes the keypad, else the ring, else the open list, else the armed field, else drops the last draft step (the last
        /// selected flange edge; with none left, the armed command). Local state only: it never touches the CAD.
        /// </summary>
        public void Back()
        {
            if (!Active) return;
            if (_shell != null && _shell.Palette.KeypadVisible) { _shell.Palette.HideKeypad(); _ask = null; return; }
            if (_shell == null && _ask != null) { _ask.CancelEdit(); _ask = null; return; }
            if (_ring != null && _ring.Visible) { HideRing(); return; }
            if (_picker != null) { ClosePicker(); return; }
            if (_armedField != null) { DisarmField(); return; }
            DiscardLastDraftStep();
        }

        private void DiscardLastDraftStep()
        {
            if (_session == null || Locked || _manip?.Dragging == true) return;
            if (_mode.Armed == SheetMetalCommand.Flange && _flange.EdgeIds.Count > 0 && _session.CanEdit)
            { _flange.ToggleEdge(_flange.EdgeIds[_flange.EdgeIds.Count - 1]); Refresh(); return; }
            if (_mode.Armed != SheetMetalCommand.None || _session.Status != DesignStatus.Empty) CancelCommand();
        }

        // ---------------------------------------------------------------- workbench

        private double ModelExtentM()
        {
            var size = ScenePlacement.LocalBounds(_view.transform).size;
            return Math.Max(size.x, Math.Max(size.y, size.z));
        }

        private void PlaceWorkbench()
        {
            if (_bench == null || _head == null || _view == null) return;
            var frame = _bench.Recenter(_head);
            _bench.ApplyPart(_view.transform, ModelExtentM());
            _flatDisplay?.SetWorkPlane(frame);
            PlaceCommitBar(frame);
        }

        private void PlaceCommitBar(WorkbenchFrame frame)
        {
            if (_shell == null || frame == null) return;
            var p = WorkbenchLayout.CommitBarPosition(frame);
            _shell.PlaceCommitBar(new Vector3((float)p.X, (float)p.Y, (float)p.Z), Quaternion.Euler(45, (float)frame.YawDegrees, 0));
        }

        /// <summary>The model changed (new revision/scene): put it back on the work plane.</summary>
        public void RefreshWorkbench()
        {
            if (!Active || _bench == null || _view == null) return;
            _bench.ApplyPart(_view.transform, ModelExtentM());
        }

        /// <summary>Y short press: fit the model to the work plane again.</summary>
        public void FitView()
        {
            if (!Active || _bench == null) return;
            _bench.Fit();
            _flatDisplay?.ResetPlane();
        }

        /// <summary>Y long press: new bench frame from the head.</summary>
        public void RecenterView()
        {
            if (!Active || _bench == null || _head == null) return;
            var frame = _bench.Recenter(_head);
            _flatDisplay?.SetWorkPlane(frame);
            PlaceCommitBar(frame);
        }

        // ---------------------------------------------------------------- reading

        private async void LoadContext()
        {
            if (!_online || _state == null || _kind != "part" || _sheet == null || _busy) { Refresh(); return; }
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
                    _notice = "";
                    if (context.Truncated) SetNotice("Elenco CAD parziale: alcuni bordi superano i limiti.");
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
            catch (Exception ex) { if (generation == _generation) SetNotice(ex.Message); }
            finally { if (generation == _generation) { _busy = false; AnnounceStatus(); Refresh(); } }
        }

        /// <summary>
        /// "Sviluppo" view: the pattern lies on the work plane in front of the user and replaces the folded part there.
        /// Fetched (and verified for this revision) first when it is not on screen yet.
        /// </summary>
        private async void ShowFlat()
        {
            var flat = _flat;
            if (flat == null) return;
            _flatDisplay?.SetFlatView(true);
            if (flat.State == FlatPatternState.Ready) { Refresh(); return; }
            try { await flat.ShowAsync(); }
            catch (InvalidOperationException ex) { SetNotice(ex.Message); }
            catch (Exception ex) { SetNotice(ex.Message); }
            if (this != null) Refresh();
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
                    CalibrateFlangeAxis(_session.Preview);
                }
                catch (Exception ex) { SetNotice("Anteprima non visualizzabile: " + ex.Message); }
            }
            // The blue ghost belongs to the Ready state only: an edited draft (Bozza) must not keep showing the stale result.
            if (_session.Preview == null || _session.Status == DesignStatus.Draft) { _previewView.Clear(); _renderedPlan = null; }
            ShowValidationContext();
            ShowErrorDetail();
            RequestRender();
        }

        /// <summary>The commit bar carries a short message; the detail (and the orange-geometry hint) goes to the HUD, once per error.</summary>
        private void ShowErrorDetail()
        {
            if (_session.Status != DesignStatus.Error || string.IsNullOrEmpty(_session.Error)) { _lastErrorShown = null; return; }
            if (_session.Error == _lastErrorShown) return;
            _lastErrorShown = _session.Error;
            Haptics.Play(HapticPulse.Error);
            string detail = "Comando non completato: " + _session.Error + "\nLa bozza resta modificabile: correggi i parametri e riprova.";
            if (_geometry != null && _geometry.HasErrorContext) detail += "\nIn arancione: geometria da controllare.";
            HudMessage?.Invoke(detail);
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

        private string CalibrationKey() => string.Join(",", _flange.EdgeIds) + "|" + _flange.Datum + "|" + _flange.AngleDegrees.ToString("R", CultureInfo.InvariantCulture);

        private DesignEdge FirstSelectedEdge()
        {
            if (_designContext == null || _flange.EdgeIds.Count == 0) return null;
            return _designContext.Edges.FirstOrDefault(e => e.Id == _flange.EdgeIds[0]);
        }

        /// <summary>
        /// Once Inventor's preview of the current flange draft is on screen, check the handle direction against it.
        /// View only: never touches the draft values nor CAD. On ambiguous evidence the heuristic (and its warning) stays.
        /// </summary>
        private void CalibrateFlangeAxis(DesignPreview preview)
        {
            _calibKey = null;
            try
            {
                var edge = FirstSelectedEdge();
                if (_mode.Armed == SheetMetalCommand.Flange && edge != null && preview?.Model != null
                    && double.IsFinite(_flange.HeightMm) && _flange.HeightMm > 0
                    && FlangeManipulator.TryFrame(edge, _designContext.Faces, _mode.Context?.ThicknessMm, out _, out var candidate, out var fromFaces)
                    && FlangeManipulator.TryCalibrate(preview.Model, edge, candidate, fromFaces, _flange.HeightMm, _mode.Context?.ThicknessMm, out var axis))
                { _calibKey = CalibrationKey(); _calibAxis = axis; }
            }
            catch (Exception) { _calibKey = null; }
            RefreshFlangeVisuals();
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
            {
                bool calibrated = _calibKey != null && _calibKey == CalibrationKey();
                if (calibrated) axis = _calibAxis;
                _manip.Show(origin, axis, SafeHeight(_flange.HeightMm), fromFaces, calibrated);
            }
            else _manip.Hide();
        }

        private static double SafeHeight(double mm) => double.IsNaN(mm) || double.IsInfinity(mm) ? FlangeManipulator.MinHeightMm : mm;

        // ---------------------------------------------------------------- commands

        private bool Idle => Active && !_busy && _pendingMutations == 0 && _session != null && _session.CanEdit
            && _session.Status != DesignStatus.Previewing && _mode.CanWrite && SceneCurrent;
        /// <summary>The drawn scene matches the document geometry. The scene reloads only on a visual revision change, so a non-visual revision bump keeps it current (context reads and Apply stay revision-bound).</summary>
        private bool SceneCurrent => _state != null && _sceneState != null && _state.DocumentId == _sceneState.DocumentId && _state.VisualRevision == _sceneState.VisualRevision;

        /// <summary>Shared by the palette actions, the commit bar and the voice layer: the same command id, the same enablement.</summary>
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
        /// Runs the command exactly as its action would. Apply never commits from here: it only shows what will be
        /// applied; the commit bar is the single commit path (M5-11).
        /// </summary>
        public bool Invoke(string commandId)
        {
            var id = CommandActionId(commandId);
            if (id == null || !IsEnabled(commandId)) return false;
            if (commandId == CommandIds.Apply)
            {
                SetNotice("Piano pronto: " + PlanSummary() + ". Conferma premendo Applica sulla barra di conferma.");
                Refresh();
                return true;
            }
            return Actions.First(a => a.Id == id).TryInvoke();
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
            catch (Exception ex) { SetNotice(ex.Message); }
            Refresh();
        }

        private void SetScreen(string screen)
        {
            if (_armedField != null && _armedFieldScreen != screen) DisarmField();
            _screen = screen;
        }

        /// <summary>Editing the part needs the folded model on the plane: "Piegato" (the pattern stays loaded, only hidden).</summary>
        private void FoldedView() => _flatDisplay?.SetFlatView(false);

        private void ArmFlange()
        {
            FoldedView();
            DiscardDraft();
            if (!_mode.Arm(SheetMetalCommand.Flange, out var reason)) { SetNotice(reason); Refresh(); return; }
            _notice = ""; _screen = "flange"; Refresh();
        }

        /// <summary>Flangia from the ring of an edge takes that edge into the draft; from the palette it starts an empty selection.</summary>
        private void StartFlange()
        {
            string edge = _ringEdge;
            ArmFlange();
            if (edge != null && _mode.Armed == SheetMetalCommand.Flange) _flange.ToggleEdge(edge);
            HideRing();
        }

        private void OpenRule()
        {
            if (!(_mode.Armed == SheetMetalCommand.Rule && _screen == "rule"))
            {
                FoldedView();
                DiscardDraft();
                if (!_mode.Arm(SheetMetalCommand.Rule, out var reason)) { SetNotice(reason); Refresh(); return; }
                _notice = ""; SetScreen("rule");
            }
            OpenRulePicker();
        }

        private void OpenSketchPick(SheetMetalCommand command)
        {
            FoldedView();
            DiscardDraft();
            if (!_mode.Arm(command, out var reason)) { SetNotice(reason); Refresh(); return; }
            _notice = ""; SetScreen("sketches");
            OpenSketchPicker();
        }

        private void StartFlatPattern()
        {
            FoldedView();
            DiscardDraft();
            if (!_mode.Arm(SheetMetalCommand.FlatPattern, out var reason)) { SetNotice(reason); Refresh(); return; }
            _notice = ""; _screen = "flat";
            if (!_mode.SubmitFlatPattern(_session)) { SetNotice(_mode.LastError); Refresh(); return; }
            PreviewDraft();
        }

        private void ChooseSketch(string name)
        {
            _sketchName = name;
            ShowProfile(name);
            string hint = ProfileHint(name);
            if (hint == "aperto" || hint == "vuoto")
            {
                SetNotice(hint == "aperto" ? "Profilo aperto: chiudi lo schizzo in Inventor o scegline un altro."
                    : "Lo schizzo non contiene geometria.");
                if (_session.Status != DesignStatus.Empty) _session.Cancel();
                Refresh();
                OpenSketchPicker();   // the list stays on offer: the refused profile is not a dead end
                return;
            }
            _notice = "";
            if (_mode.Armed == SheetMetalCommand.Cut) SetScreen("cut");
            if (!RebuildDraft()) { if (!string.IsNullOrEmpty(_mode.LastError)) SetNotice(_mode.LastError); Refresh(); return; }
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
                SetNotice("Nessuna differenza rispetto alla regola attiva."); return false;
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
                    if (_sketchName == null) { SetNotice("Scegli lo schizzo della faccia base."); return false; }
                    return _mode.SubmitFace(_session, _sketchName);
                case SheetMetalCommand.Cut:
                    if (_sketchName == null) { SetNotice("Scegli lo schizzo del taglio."); return false; }
                    return _mode.SubmitCut(_session, _sketchName, _extent, _direction, _acrossBends);
                case SheetMetalCommand.Rule: return SubmitRuleDraft();
                case SheetMetalCommand.FlatPattern: return _mode.SubmitFlatPattern(_session);
                default: return false;
            }
        }

        /// <summary>Anteprima: rebuild from the editor state, then ask Inventor.</summary>
        private void Preview()
        {
            if (!RebuildDraft())
            {
                if (!string.IsNullOrEmpty(_mode.LastError)) SetNotice(_mode.LastError);
                else if (string.IsNullOrEmpty(_notice)) SetNotice("Nessuna bozza da calcolare.");
                Refresh(); return;
            }
            PreviewDraft();
        }

        private async void PreviewDraft()
        {
            var session = _session;
            try { if (session != null && session.Status == DesignStatus.Draft) await session.PreviewAsync(); }
            catch (Exception ex) { SetNotice(ex.Message); if (this != null) Refresh(); }
        }

        /// <summary>Manipulator release: the shared draft asks for a preview of its current version, never a commit.</summary>
        private async void PreviewPending()
        {
            var session = _session;
            try { await _mode.PreviewPendingAsync(session, _flange); }
            catch (Exception ex) { SetNotice(ex.Message); if (this != null) Refresh(); }
        }

        private async void ApplyPressed()
        {
            var session = _session;
            if (session?.CanApply != true || !_mode.CanWrite) return;
            _pendingShowFlat = _mode.Armed == SheetMetalCommand.FlatPattern;
            await RunMutation(session, () => session.ApplyAsync());
            if (this != null && session == _session && session.LastCommit != null && !session.CommitOutcomeUnknown)
            { CommitBar.MarkApplied(Time.unscaledTimeAsDouble); Haptics.Play(HapticPulse.Success); }
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
            Refresh();
            try { await mutation(); }
            catch (Exception ex) { if (owner == _session) SetNotice(ex.Message); }
            finally { _pendingMutations--; }
            if (this == null) return;
            if (owner == _session && !owner.CommitOutcomeUnknown && owner.Status == DesignStatus.RefreshRequired)
            {
                try { await RefreshAfterApply(false); }
                catch (Exception ex) { if (owner == _session) SetNotice(ex.Message); }
            }
            Refresh();
        }

        private async Task RefreshAfterApply(bool reviewed)
        {
            if (!_online || _backend == null || _pendingMutations > 0) return;
            var backend = _backend; var session = _session; int generation = _generation;
            var state = await backend.GetDocumentStateAsync(_reads.Token);
            if (this == null || backend != _backend || session != _session || generation != _generation) return;
            session.AcknowledgeRefresh(state, reviewed); _reviewAfterRebind = false; _state = state;
            StateMoved(); LoadContext();
            SetNotice("Documento aggiornato. Attendo il modello da Inventor.");
        }

        private async void ReviewUnknown()
        {
            try { await RefreshAfterApply(true); }
            catch (Exception ex) { SetNotice(ex.Message); }
            Refresh();
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
            UpdateChips();
            ArmedFieldChanged?.Invoke();
            return true;
        }

        public void DisarmField()
        {
            if (_armedField == null) return;
            _armedField = null; _armedFieldScreen = null;
            UpdateChips();
            ArmedFieldChanged?.Invoke();
        }

        /// <summary>
        /// Sets the armed field (same path as the keypad): the draft is updated and a new preview requested,
        /// Apply stays invalid until it is rendered. Any other field id, a non-finite or out-of-range value is refused.
        /// With the keypad open on that field, dictation confirms the value like the OK key.
        /// </summary>
        public bool SetArmedField(string fieldId, double value)
        {
            LastFieldError = null;
            var field = ArmedField;
            if (field == null || field.Id != fieldId) { LastFieldError = "Nessun campo attivo con questo nome."; return false; }
            if (double.IsNaN(value) || double.IsInfinity(value) || value < field.Min || value > field.Max)
            { LastFieldError = "Valore fuori intervallo (" + Fmt(field.Min) + " – " + Fmt(field.Max) + ")."; return false; }
            if (!Idle) { LastFieldError = "Modifica non disponibile ora."; return false; }
            if (_ask != null && _ask.Id == fieldId)
            {
                if (!_ask.CommitValue(value, out var reason)) { LastFieldError = reason; return false; }
                return true;   // the entry's Committed/Changed path applied it
            }
            ApplyField(fieldId, value);
            SyncEntries();
            RequestRender();
            return true;
        }

        private void ApplyField(string fieldId, double value)
        {
            switch (fieldId)
            {
                case FieldFlangeHeight: _flange.SetHeight(value); PreviewDraft(); break;
                case FieldFlangeAngle: _flange.SetAngle(value); PreviewDraft(); break;
                case FieldThickness:
                    _thicknessMm = value;
                    if (SubmitRuleDraft()) PreviewDraft();
                    break;
            }
        }

        /// <summary>Chip tap or palette action: arms the field and opens the palette keypad on its shared entry.</summary>
        private void AskNumber(string fieldId)
        {
            if (!TryArmField(fieldId)) { SetNotice(LastFieldError); Refresh(); return; }
            var entry = EntryFor(fieldId);
            SyncEntries();
            _ask = entry;
            if (_shell != null) _shell.Palette.ShowKeypad(entry, ArmedField.Label); else entry.BeginEdit();
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

        // ---------------------------------------------------------------- labels

        private static string Fmt(double value) => value.ToString("0.###", Italian);
        private static string DatumLabel(string datum) => datum == "inner" ? "interno" : datum == "tangent" ? "tangente" : "esterno";
        private static string DirectionLabel(string direction) => direction == "negative" ? "negativa" : direction == "symmetric" ? "simmetrica" : "positiva";

        private void CycleDatum()
        {
            var options = new[] { "outer", "inner", "tangent" };
            _flange.SetDatum(options[(Array.IndexOf(options, _flange.Datum) + 1) % 3]);
            PreviewDraft();
            Refresh();
        }

        // ---------------------------------------------------------------- controller input (XrInput events + per-frame hover)

        private bool InputTracked => _input == null || _input.PenTracked;
        private bool Precision => _input != null && _input.Precision;

        /// <summary>The pen ray, or false when the workspace cannot take pen input right now.</summary>
        /// <summary>Runners with a synthetic pen set this so the real controller's UI hit cannot swallow the gesture (the old runner passed the UI flag per frame).</summary>
        public Func<bool> UiHitOverride { get; set; }

        private bool TryPenRay(out Ray ray, out bool overUi)
        {
            ray = default; overUi = false;
            if (!Active || !_visible || _ray == null || _ray.Origin == null || !InputTracked) return false;
            ray = new Ray(_ray.Origin.position, _ray.Origin.forward);
            var ui = EventSystem.current?.currentInputModule as ControllerUiInputModule;
            overUi = UiHitOverride != null ? UiHitOverride() : ui != null && ui.CurrentHit.isValid;
            return true;
        }

        /// <summary>The flange knob can take the pen: armed Flange with an edge, an editable draft, no keypad waiting.</summary>
        private bool CanManipulate => _manip != null && _session?.CanEdit == true && _mode.CanWrite && _mode.Armed == SheetMetalCommand.Flange
            && _flange.EdgeIds.Count > 0 && _manip.Visible && _ask == null;

        private void Update()
        {
            if (_renderPending && Time.unscaledTime >= _nextRender) { _renderPending = false; _nextRender = Time.unscaledTime + 0.15f; Refresh(); }
            if (Active)
            {
                CommitBar.Tick(Time.unscaledTimeAsDouble);
                UpdateBar();
                DropClosedKeypad();
            }
            if (!Active || !_visible || _ray == null || _ray.Origin == null) return;
            if (!InputTracked) { _hoverKey = 0; return; }
            if (_manip.Dragging) { DragStep(); return; }
            if (_twoHandActive) { TwoHandStep(); return; }
            if (_grab != null) { MoveGrab(); return; }
            if (!TryPenRay(out var ray, out bool overUi)) return;
            HoverFeedback(ray, overUi);
        }

        /// <summary>Best effort: a short pulse when the ray enters an interactive target (palette, chip, ring, flange knob).</summary>
        private void HoverFeedback(Ray ray, bool overUi)
        {
            int key = overUi ? 1 : (CanManipulate && _manip.IsOverKnob(ray)) ? 2 : 0;
            if (key != _hoverKey && key != 0) Haptics.Play(HapticPulse.Hover);
            _hoverKey = key;
        }

        /// <summary>Trigger press with the ray on the knob captures it (M5-08): from here on only the local draft changes.</summary>
        private void OnPenPressed()
        {
            if (!TryPenRay(out var ray, out bool overUi)) return;
            if (!overUi && !GripDown && CanManipulate
                && _manip.TryBeginDrag(ray, CadCoordinates.FromWorld(_view.transform, _ray.Origin.position), Precision ? FlangeManipulator.PrecisionFactor : 1))
            { _grab = null; _grabFlat = false; return; }
            if (overUi || _ask != null || _session?.CanEdit != true || GripDown || _manip.Dragging) return;
            if (_mode.Armed == SheetMetalCommand.Flange && _designContext != null && _mode.CanWrite && SceneCurrent)
            {
                string edge = CadCoordinates.PickEdge(_view.transform, _designContext.Edges, ray, requireVisible: true);
                if (edge != null) { _flange.ToggleEdge(edge); _notice = ""; }
            }
            else if (_mode.Armed == SheetMetalCommand.None && _ring != null && Idle && _designContext != null)
            {
                // Idle: an edge or a real planar face opens the ring; any other hit or the empty space closes it.
                string edge = CadCoordinates.PickEdge(_view.transform, _designContext.Edges, ray, requireVisible: true);
                if (edge != null) { SelectEdge(edge); return; }
                HideRing();
                if (CadRaycaster.TryPick(ray, 20, out var body, out int triangle, out var hit) && SelectPlanarFace(body, triangle, hit))
                    ShowRing(UiSelectionKind.PlanarFace, hit);
            }
        }

        /// <summary>
        /// A body hit is a planar face only when the revision-bound CAD context (the same B-rep at this revision) lists it;
        /// otherwise the ring stays closed and a notice says why.
        /// </summary>
        private bool SelectPlanarFace(CadBody body, int triangle, Vector3 hit)
        {
            if (_designContext == null || _state == null || _designContext.State.DocumentId != _state.DocumentId
                || _designContext.State.Revision != _state.Revision || !SceneCurrent || body.Instance.DefinitionId != _state.DocumentId) return false;
            var range = body.Primitive.FaceMap?.FaceAtTriangle(triangle);
            var face = range == null ? null : _designContext.Faces.FirstOrDefault(f =>
                f.BodyIndex > 0 && f.FaceOrdinal > 0 && f.BodyIndex == body.Primitive.BodyIndex && f.FaceOrdinal == range.Ordinal);
            if (face == null && range != null) face = _designContext.Faces.FirstOrDefault(f => f.Id == range.FaceId);
            if (face == null)
            {
                _selection?.Clear();
                SetNotice("Faccia non piana o riferimenti non aggiornati. Prova Aggiorna.");
                return false;
            }
            _selection?.Show(new InventorXrSo.Core.Selection.Selection(InventorXrSo.Core.Selection.SelectionKind.Face, body.Instance.OccurrenceId, range.FaceId, face.Id));
            SetNotice("Faccia piana selezionata.");
            return true;
        }

        private static bool Finite(Vector3 v) => !(float.IsNaN(v.x + v.y + v.z) || float.IsInfinity(v.x + v.y + v.z));

        /// <summary>Per frame while the knob is captured: the draft height follows the hand (precision = 10x slower, re-anchored).</summary>
        private void DragStep()
        {
            if (!CanManipulate) { EndCapture(false, false); return; }
            if (!Finite(_ray.Origin.position)) return;
            _flange.ApplyManipulatorHeight(_manip.Drag(CadCoordinates.FromWorld(_view.transform, _ray.Origin.position),
                Precision ? FlangeManipulator.PrecisionFactor : 1));
        }

        /// <summary>
        /// Ends the knob capture. <paramref name="preview"/>: Trigger released, the draft is final and gets a preview request (never a
        /// commit). <paramref name="error"/>: tracking lost, the draft keeps the last valid height, the controller buzzes, no preview.
        /// </summary>
        private void EndCapture(bool preview, bool error)
        {
            if (_manip == null || !_manip.Dragging) return;
            _manip.EndDrag();
            _renderPending = false;
            if (error) Haptics.Play(HapticPulse.Error);
            Refresh();
            if (!preview || !Active || _session?.CanEdit != true) return;
            _flange.ReleaseManipulator();
            PreviewPending();
        }

        private void EndViewGrabs() { _grab = null; _grabFlat = false; _gripHeld = false; _twoHandActive = false; }

        private void OnPenReleased() => EndCapture(true, false);

        private void OnTrackingLost()
        {
            // Raised before the release of a held Trigger: the capture is closed here, without a preview.
            if (!Active) return;
            EndCapture(false, true);
            EndViewGrabs(); _hoverKey = 0;
        }

        private void OnGrabStarted()
        {
            if (!TryPenRay(out var ray, out bool overUi)) return;
            _gripHeld = true;
            if (_manip.Dragging || _twoHandActive) return;
            BeginGrab(ray, overUi);
        }

        private void OnGrabEnded() { _grab = null; _grabFlat = false; _gripHeld = false; }

        /// <summary>Grip alone: the detached flat pattern moves (its mesh only), otherwise the folded model moves. View only.</summary>
        private void BeginGrab(Ray ray, bool overUi)
        {
            _grab = null; _grabFlat = false;
            if (!overUi && _flat != null && _flat.Detached && _flat.IsVisible && _flatDisplay.HitTest(ray) && _flatDisplay.MeshRoot != null)
            { _grab = _flatDisplay.MeshRoot; _grabFlat = true; }
            else if (!overUi && !(_flatDisplay != null && _flatDisplay.FoldedHidden) && CadRaycaster.TryPick(ray, 20, out _, out _, out _))
            { _bench?.Snap(); _grab = _view.transform; }
            if (_grab == null) return;
            _grabPosition = _ray.Origin.InverseTransformPoint(_grab.position);
            _grabRotation = Quaternion.Inverse(_ray.Origin.rotation) * _grab.rotation;
        }

        private void MoveGrab()
        {
            var position = _ray.Origin.TransformPoint(_grabPosition);
            if (!Finite(position)) return;
            if (_grabFlat)
            {
                // View state only: the offset lives in FlatPatternView and never reaches CAD.
                if (_flat == null || !_flat.Detached || !_flat.IsVisible) { _grab = null; _grabFlat = false; return; }
                var offset = _flatDisplay.OffsetForRootPosition(position);
                _flat.MoveLocal(offset.x, offset.y, offset.z);
                return;
            }
            _grab.SetPositionAndRotation(position, _ray.Origin.rotation * _grabRotation);
        }

        private bool LeftHandPose(out Vector3 position)
        {
            position = default;
            if (_leftHand == null || _input == null || !_input.PaletteTracked) return false;
            position = _leftHand.position; return true;
        }

        private void OnTwoHandChanged(bool on)
        {
            if (!Active) return;
            if (!on) { _twoHandActive = false; return; }
            if (_ray?.Origin == null || !LeftHandPose(out var left)) return;
            EndCapture(false, false); _grab = null; _grabFlat = false;
            _bench?.Snap();
            var root = _view.transform; var right = _ray.Origin.position;
            _twoHandVector = right - left; _twoHandMid = (right + left) * 0.5f;
            _twoHandRootPosition = root.position; _twoHandRootRotation = root.rotation; _twoHandRootScale = root.localScale.x;
            _twoHandActive = _twoHandVector.sqrMagnitude > 1e-6f;
        }

        /// <summary>Two hands: rotate, scale and move the model. View only; the CAD data never changes.</summary>
        private void TwoHandStep()
        {
            if (_ray?.Origin == null || !LeftHandPose(out var left)) return;
            var right = _ray.Origin.position; var vector = right - left;
            if (vector.sqrMagnitude < 1e-6f) return;
            float scale = Mathf.Clamp(_twoHandRootScale * vector.magnitude / _twoHandVector.magnitude, (float)WorkbenchLayout.MinScale, (float)WorkbenchLayout.MaxScale);
            var turn = Quaternion.FromToRotation(_twoHandVector, vector);
            var root = _view.transform;
            root.localScale = Vector3.one * scale;
            root.rotation = turn * _twoHandRootRotation;
            root.position = (right + left) * 0.5f + turn * ((_twoHandRootPosition - _twoHandMid) * (scale / _twoHandRootScale));
        }

        /// <summary>Left stick up/down: zooms the model (or the flat pattern on the plane). View only, clamped to the layout limits.</summary>
        private void OnZoom(float axis)
        {
            if (!Active || _view == null || _manip.Dragging || _twoHandActive) return;
            ZoomView(axis, Mathf.Clamp(Time.unscaledDeltaTime, 1f / 120f, 1f / 20f));
        }

        /// <summary>Scales the model about the centre of its bounds (or the flat pattern on the plane), never outside [MinScale, MaxScale].</summary>
        public void ZoomView(float axis, float seconds)
        {
            if (_flatDisplay != null && _flatDisplay.FoldedHidden && _flatDisplay.IsShowing) { _flatDisplay.ZoomPlane(axis, seconds); return; }
            var root = _view.transform;
            _bench?.Snap();
            float current = root.localScale.x;
            float target = Mathf.Clamp(current * Mathf.Exp(axis * ZoomRatePerSecond * seconds), (float)WorkbenchLayout.MinScale, (float)WorkbenchLayout.MaxScale);
            if (Mathf.Approximately(target, current)) return;
            var pivot = root.TransformPoint(ScenePlacement.LocalBounds(root).center);
            root.localScale = Vector3.one * target;
            root.position = pivot + (root.position - pivot) * (target / current);
        }

        /// <summary>Right stick left/right: one step on the armed chip (height, angle or thickness). Silent when nothing can change now.</summary>
        private void OnStepDelta(int direction)
        {
            if (!Active || _armedField == null) return;
            var entry = EntryFor(_armedField);
            if (entry == null || entry.Editing || !FieldApplies(_armedField) || !Idle) return;
            if (entry.Nudge(direction)) Haptics.Play(HapticPulse.Tick);
        }

        /// <summary>Right stick up/down: step 0,1 / 1 / 10 of the armed chip.</summary>
        private void OnStepSizeDelta(int direction)
        {
            if (!Active || _armedField == null) return;
            var entry = EntryFor(_armedField);
            if (entry == null || entry.Editing) return;
            entry.CycleStep(direction);
            Haptics.Play(HapticPulse.Tick);
        }

        private void OnDestroy()
        {
            DetachInput();
            _reads.Cancel(); _reads.Dispose();
            _flange.Changed -= OnFlangeChanged; _mode.Changed -= OnModeChanged;
            _flangeBinding?.Dispose();
            if (_session != null) { _session.Changed -= SessionChanged; _session.Dispose(); }
            if (_flat != null) { _flat.Changed -= OnFlatChanged; _flat.Dispose(); }
            DestroyCanvas(_heightChip != null ? _heightChip.Canvas.gameObject : null);
            DestroyCanvas(_angleChip != null ? _angleChip.Canvas.gameObject : null);
            DestroyCanvas(_thicknessChip != null ? _thicknessChip.Canvas.gameObject : null);
            DestroyCanvas(_ring != null ? _ring.Canvas.gameObject : null);
            if (_previewView != null) Release(_previewView.gameObject);
            if (_geometry != null) Release(_geometry.gameObject);
            if (_manip != null) Release(_manip.gameObject);
        }

        private static void DestroyCanvas(GameObject go)
        {
            if (go == null) return;
            if (Application.isPlaying) Destroy(go); else DestroyImmediate(go);
        }

        private static void Release(GameObject item)
        {
            item.SetActive(false);
            if (Application.isPlaying) Destroy(item); else DestroyImmediate(item);
        }
    }
}
