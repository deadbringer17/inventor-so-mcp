using InventorXrSo.Core.Input;
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
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.EventSystems;

namespace InventorXrSo.Xr
{
    /// <summary>Numeric field currently armed for keypad or voice dictation (id, unit, range and current value).</summary>
    public sealed class AssemblyNumericField
    {
        public string Id { get; }
        public string Label { get; }
        public QuantityUnit Unit { get; }
        public double Min { get; }
        public double Max { get; }
        public double Value { get; }
        public AssemblyNumericField(string id, string label, QuantityUnit unit, double min, double max, double value)
        { Id = id; Label = label; Unit = unit; Min = min; Max = max; Value = value; }
    }

    /// <summary>
    /// Quest M4/M6 Assieme: native references, intentional drafts and rendered preview before Apply. No floating panel: the
    /// actions are declared to the palette (<see cref="IActionProvider"/>, see AssemblyActions.cs) and the commit bar is the
    /// only place that applies. Moving a component is a Trigger-held drag on the yellow handle (M5-08) that only edits the draft.
    /// </summary>
    [DefaultExecutionOrder(115)]
    public sealed partial class AssemblyWorkspace : MonoBehaviour, IActionProvider, ITabStateSource
    {
        public const string FieldMoveMm = "assembly.move.mm", FieldMoveDeg = "assembly.move.deg",
            FieldValueMm = "assembly.relation.mm", FieldValueDeg = "assembly.relation.deg", FieldClearance = "assembly.clearance_mm";
        private const string DraftInvalidated = "Bozza modificata: calcola una nuova anteprima.";
        private const string ShortError = "Comando non completato. Correggi e riprova.";
        private const float ZoomRatePerSecond = 1.5f;
        private static readonly CultureInfo Italian = CultureInfo.GetCultureInfo("it-IT");

        private CadSceneView _view;
        private ControllerRay _ray;
        private readonly InventorXrSo.Core.Input.DoubleTriggerDetector _doubleTrigger = new InventorXrSo.Core.Input.DoubleTriggerDetector();
        private Transform _head;
        private UiShell _shell;
        private ActionCatalog _catalog;
        private Workbench _bench;
        private XrInput _input;
        private DesignPreviewView _preview;
        private AssemblyVisuals _visuals;
        private ComponentIsolation _isolation;
        private ChipView _moveChip, _valueChip, _clearChip;
        private NumericEntry _moveChipEntry, _valueChipEntry, _ask;
        private readonly Dictionary<string, NumericEntry> _entries = new Dictionary<string, NumericEntry>();
        private bool _syncingEntries;
        private RingView _ring;
        private IAssemblyWorkspaceBackend _backend;
        private DesignSession _session;
        private DocumentState _state, _sceneState;
        private string _kind, _notice = "", _command, _type, _rendered, _lastHeadline, _lastErrorShown;
        private string _armedField;
        private AssemblyContext _context;
        private AssemblyOccurrence _occurrence;
        private SceneGraph _graph;
        private AssemblyReference _a, _b;
        private DesignHistory _history;
        private CadPoint _translation;
        private double _value, _angle, _clearance;
        private bool _online, _visible, _busy, _reviewAfterRebind, _flipOrigin, _flipAlignment, _inside, _opposed = true;
        private bool _rotating, _dragging, _pickEdges;
        private int _generation, _axisIndex, _mutations;
        private CadPoint _dragStart, _startTranslation;
        private Quaternion _dragRotation;
        private double _startAngle;
        private float _dragFactor = 1f;
        private Transform _grab, _leftHand;
        private bool _gripHeld, _twoHandActive;
        private bool GripDown => _gripHeld || (_input != null && _input.PenGripHeld);
        private Vector3 _grabPosition, _twoHandVector, _twoHandMid, _twoHandRootPosition;
        private Quaternion _grabRotation, _twoHandRootRotation;
        private float _twoHandRootScale;
        private int _hoverKey;
        private CancellationTokenSource _reads = new CancellationTokenSource();

        public bool Active { get; private set; }
        public bool RequiresCadReview => _reviewAfterRebind || _mutations > 0 || _session?.Status == DesignStatus.Committing || _session?.Status == DesignStatus.RefreshRequired;
        /// <summary>M9: Misura (Ispeziona) is armed and owns the trigger: the workspace does not pick or capture.</summary>
        public Func<bool> PickSuppressed { get; set; }
        /// <summary>M9: a handle is captured; Ispeziona tools suspend until it is released.</summary>
        public bool HandleCaptured => _dragging;
        /// <summary>M9 input state: the numeric keypad is open (A = OK, X = Annulla).</summary>
        public bool KeypadOpen => Active && (_ask != null || (_shell != null && _shell.Palette.KeypadVisible));
        /// <summary>M9 input state: a chip is armed or a handle captured (stick right = step, A = keypad).</summary>
        public bool InputArmed => Active && (_armedField != null || _dragging);
        /// <summary>M9 input state: a component is selected (A = Isola / Rilascia).</summary>
        public bool ComponentSelected => Active && _occurrence != null;
        /// <summary>M9: what is true now (sketch open, feature/move in progress): decides the tabs that appear by themselves.</summary>
        public TabState TabState => new TabState { FeatureInProgress = IsMove && InDraft };
        /// <summary>Occurrence id selected on the model (null when none); Ispeziona acts on it in Assieme.</summary>
        public string SelectedOccurrenceId => _occurrence?.Id;
        public Func<bool> CanEnter { get; set; }
        public event Action<bool> ActiveChanged;
        /// <summary>Raised only when an open workspace closes: the shell restores the Inspect placement of the model.</summary>
        public event Action Closed;
        /// <summary>
        /// The definition of the isolated component was activated in Inventor (documentId, occurrenceId, 16-float pose in this
        /// assembly, sheetMetal requested). Raised only after the activation succeeded; the context router drives the rest.
        /// </summary>
        public event Action<string, string, float[], bool> EntryRequested;
        public event Action ArmedFieldChanged;
        /// <summary>Notices and error detail for the HUD (the commit bar only carries a short message).</summary>
        public event Action<string> HudMessage;
        /// <summary>Last notice, also sent to <see cref="HudMessage"/> when non-empty.</summary>
        public string Notice => _notice;
        /// <summary>Assieme no longer has a floating panel: voice labels come from the declared actions.</summary>
        public HomePanel VoicePanel => null;
        /// <summary>The numeric entry currently waiting for the keypad (or dictation), if any.</summary>
        public NumericEntry ActiveEntry => _ask;
        public DesignSession Session => _session;
        public ComponentIsolation Isolation => _isolation;
        public string LastFieldError { get; private set; }
        public AssemblyNumericField ArmedField => _armedField == null ? null : Describe(_armedField);
        /// <summary>Same enablement of the vincoli / sposta / apri actions: the CAD context is read, nothing is in flight.</summary>
        private bool Editable => Active && !_busy && _context != null && _session?.CanEdit == true && SceneCurrent;
        private bool Locked => _session != null && (_session.Status == DesignStatus.Previewing || _session.Status == DesignStatus.Committing || _mutations > 0);
        private bool Idle => Editable && !Locked;
        /// <summary>
        /// The drawn scene matches the document geometry. SessionController reloads the scene only when the visual revision changes,
        /// so a non-visual revision bump (e.g. after returning from a part edit) must not leave the workspace blocked forever.
        /// </summary>
        private bool SceneCurrent => _state != null && _sceneState != null && _state.DocumentId == _sceneState.DocumentId && _state.VisualRevision == _sceneState.VisualRevision;
        /// <summary>A command (move, constraint, joint) is being drafted: Preview, Apply and Cancel live on the commit bar.</summary>
        private bool InDraft => _command != null;
        private bool IsMove => _command == "assembly_move";
        private IReadOnlyList<CadPoint> Axes => _occurrence == null ? Array.Empty<CadPoint>() : _rotating ? _occurrence.RotationAxes : _occurrence.TranslationAxes;

        // ---------------------------------------------------------------- lifetime

        public void Initialize(CadSceneView view, ControllerRay ray, Transform head)
        {
            _view = view; _ray = ray; _head = head;
            _preview = view.gameObject.AddComponent<DesignPreviewView>(); _preview.Initialize(view, head);
            _visuals = view.gameObject.AddComponent<AssemblyVisuals>(); _visuals.Initialize(ray.LineMaterial);
            _isolation = view.gameObject.AddComponent<ComponentIsolation>(); _isolation.Initialize(view);
            _isolation.Changed += OnIsolationChanged;
        }

        /// <summary>
        /// Shell parts the workspace draws on: palette/commit bar/HUD through the catalog, plus the chips and the ring created here.
        /// The bench and the input are optional (not present in headless tests). <paramref name="sheet"/> is accepted for symmetry
        /// with the other workspaces; Assieme has no sketch sheet.
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
            if (_moveChip == null) _moveChip = MakeChip(eye, "Spostamento", () => AskNumber(MoveField));
            if (_valueChip == null) _valueChip = MakeChip(eye, "Distanza", () => AskNumber(ValueField));
            if (_clearChip == null) _clearChip = MakeChip(eye, "Gioco min", () => AskNumber(FieldClearance));
            _clearChip.Bind(EntryFor(FieldClearance), "Gioco min");
        }

        private ChipView MakeChip(Camera eye, string label, Action tapped)
        {
            var chip = ChipView.Create(null);
            XrUi.MakeInteractive(chip.Canvas, eye);
            chip.Face(_head);
            chip.Tapped += tapped;
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
            _input.Dispatcher.Invoked += OnInvoked;
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
            _input.Dispatcher.Invoked -= OnInvoked;
            _input.Zoom -= OnZoom;
            _input = null;
        }

        public void Bind(IAssemblyWorkspaceBackend backend)
        {
            _reviewAfterRebind |= RequiresCadReview; CancelReads();
            if (_session != null) { _session.Changed -= Changed; _session.Dispose(); }
            _backend = backend; _session = backend == null ? null : new DesignSession(backend);
            if (_session != null) { if (_reviewAfterRebind) _session.RequireCadReview(); _session.Changed += Changed; }
            _online = false; _state = null; _context = null; Reset(); Refresh();
        }

        public void SetScene(LoadedScene scene)
        {
            // CadSceneView.Show rebuilds even for a visibility-only revision and
            // releases the rendered ghost. Its previous rendering proof is gone.
            if (_session?.CanEdit == true && _session.Status == DesignStatus.PreviewReady)
            { _rendered = null; _session.RejectDraft("Scena aggiornata: ricalcola l'anteprima prima di applicare."); }
            _graph = scene?.Graph;
            _sceneState = scene?.Graph.State; _kind = scene?.Graph.Kind; SetDocumentState(_sceneState);
        }

        private static float[] FindPose(SceneNode node, string occurrenceId)
        {
            if (node == null) return null;
            if (node.OccurrenceId == occurrenceId) return node.MatrixGltf;
            foreach (var child in node.Children) { var found = FindPose(child, occurrenceId); if (found != null) return found; }
            return null;
        }

        public void SetDocumentState(DocumentState state)
        {
            bool changed = _state?.DocumentId != state?.DocumentId || _state?.Revision != state?.Revision;
            _state = state;
            if (changed) { CancelReads(); _context = null; Reset(); }
            _session?.SetContext(state, _online, _kind == "assembly");
            if (changed && Active) Load();
            Refresh();
        }

        public void SetOnline(bool online)
        {
            if (_online == online) return;
            _online = online; CancelReads(); _context = null; Reset();
            _session?.SetContext(_state, online, _kind == "assembly");
            if (online && Active) Load();
            Refresh();
        }

        public void SetVisible(bool visible) { _visible = visible; if (!visible) Close(); }

        public void Open()
        {
            if (!_visible || CanEnter?.Invoke() == false) return;
            Active = true; _ray.CanPick = false; _lastHeadline = null;
            ActiveChanged?.Invoke(true);
            PlaceWorkbench(); Load(); Refresh();
        }

        public void Close()
        {
            bool was = Active;
            // The capture of the move handle ends with the workspace (M5-08): only the local draft was ever touched.
            _dragging = false;
            EndViewGrabs();
            Active = false; CancelReads();
            if (_session != null && !RequiresCadReview) _session.Cancel();
            Reset(true); _shell?.Palette.HideKeypad(); _ask = null; DisarmField();
            if (_ray != null) _ray.CanPick = _visible;
            if (was) _bench?.Release();
            UpdateBar(); UpdateChips();
            ActiveChanged?.Invoke(false);
            if (was) Closed?.Invoke();
        }

        private void CancelReads() { _generation++; _reads.Cancel(); _reads.Dispose(); _reads = new CancellationTokenSource(); _busy = false; }

        private void Reset(bool immediate = false)
        {
            _occurrence = null; _a = _b = null; _history = null; _command = _type = _rendered = null;
            _translation = default; _angle = _value = 0; _axisIndex = 0; _rotating = _dragging = false;
            _grab = null; _gripHeld = false; _twoHandActive = false; _lastErrorShown = null;
            _preview?.Clear(); _visuals?.Clear();
            ReleaseIsolation(immediate);
            HideRing(); ClosePicker(false); DisarmField(); _ask = null;
            SyncEntries(); UpdateChips();
        }

        // ---------------------------------------------------------------- notices, bar, chips, ring

        private void SetNotice(string text)
        {
            _notice = text ?? "";
            if (_notice.Length > 0) HudMessage?.Invoke(_notice);
        }

        /// <summary>Content or enablement changed: rebuild the declared actions, the bar and the chips, and tell the catalog.</summary>
        private void Refresh()
        {
            _actions = null;
            AnnounceAvailability();
            SyncEntries();
            UpdateBar();
            UpdateChips();
            _catalog?.NotifyChanged();
        }

        private void UpdateBar()
        {
            var s = _session;
            bool working = s != null && (s.Status == DesignStatus.Previewing || s.Status == DesignStatus.Committing);
            bool real = s != null && s.Status == DesignStatus.Error && s.Error != DraftInvalidated;
            CommitBar.Update(new CommitBarInputs(
                online: _online,
                outcomeUnknown: s?.CommitOutcomeUnknown == true,
                refreshRequired: s?.Status == DesignStatus.RefreshRequired,
                busy: working || _mutations > 0 || (_busy && InDraft),
                hasPreview: s != null && s.Status == DesignStatus.PreviewReady && s.CanApply && SceneCurrent,
                hasDraft: s != null && InDraft,
                error: real ? ShortError : ""), Time.unscaledTimeAsDouble);
        }

        /// <summary>The reason Assieme cannot be used at all goes to the HUD once (the palette actions carry it as disabled reason).</summary>
        private string Headline()
        {
            if (!Active) return null;
            if (_kind != "assembly") return "Attiva un assieme dal Browser.";
            if (_session == null) return "PC non collegato.";
            if (!_online) return "Offline — Applica non disponibile.";
            return null;
        }

        private void AnnounceAvailability()
        {
            string headline = Headline();
            if (headline != null && headline != _lastHeadline) SetNotice(headline);
            _lastHeadline = headline;
        }

        /// <summary>Component name, degrees of freedom and what blocks moving it: shown on the HUD when a component is selected.</summary>
        public string OccurrenceSummary()
        {
            if (_occurrence == null) return "Nessun componente selezionato.";
            var sb = new StringBuilder();
            sb.Append(_occurrence.Name).Append(" • Gradi di libertà: ").Append(_occurrence.TotalDof?.ToString() ?? "?");
            if (_occurrence.Grounded) sb.Append(" • Fissato");
            if (!string.IsNullOrEmpty(_occurrence.UnavailableReason)) sb.Append("\nComponente non modificabile: ").Append(_occurrence.UnavailableReason);
            if (!_occurrence.DofComplete) sb.Append("\nLibertà non disponibili: Sposta è disabilitato.");
            if (_occurrence.Kind == "assembly") sb.Append("\nAttivare la definizione per modificarne i figli; gli effetti riguardano tutte le istanze.");
            if (_a != null) sb.Append("\nA: ").Append(_a.Name);
            if (_b != null) sb.Append("   B: ").Append(_b.Name);
            return sb.ToString();
        }

        private void AnnounceSelection() => SetNotice(OccurrenceSummary());

        // ---- numeric entries (chips, keypad, dictation and thumbstick share them)

        private string MoveField => _rotating ? FieldMoveDeg : FieldMoveMm;
        private string ValueField => _type == "angle" ? FieldValueDeg : FieldValueMm;

        private AssemblyNumericField Describe(string id)
        {
            switch (id)
            {
                case FieldMoveMm:
                    return new AssemblyNumericField(id, "Spostamento", QuantityUnit.Millimeters, -1000000, 1000000,
                        Axes.Count > _axisIndex && !_rotating ? _translation.Dot(Axes[_axisIndex]) : 0);
                case FieldMoveDeg: return new AssemblyNumericField(id, "Rotazione", QuantityUnit.Degrees, -360, 360, _angle);
                case FieldValueMm: return new AssemblyNumericField(id, "Distanza", QuantityUnit.Millimeters, -1000000, 1000000, _value);
                case FieldValueDeg: return new AssemblyNumericField(id, "Angolo", QuantityUnit.Degrees, -359.999, 359.999, _value);
                case FieldClearance: return new AssemblyNumericField(id, "Gioco minimo", QuantityUnit.Millimeters, 0, 10000, _clearance);
                default: return null;
            }
        }

        private NumericEntry EntryFor(string fieldId)
        {
            if (_entries.TryGetValue(fieldId, out var entry)) return entry;
            var field = Describe(fieldId);
            if (field == null) return null;
            entry = new NumericEntry(fieldId, field.Unit, field.Value, field.Min, field.Max);
            entry.Changed += () => OnEntryChanged(entry);
            // Confirming the value the draft already has changes nothing, but a draft without a preview still deserves one.
            entry.Committed += () => { if (!_syncingEntries && FieldApplies(entry.Id) && Idle && PreviewWanted && NeedsPreview) Preview(); };
            _entries[fieldId] = entry;
            return entry;
        }

        private void SyncEntries()
        {
            _syncingEntries = true;
            try { foreach (var entry in _entries.Values) SyncEntry(entry); }
            finally { _syncingEntries = false; }
        }

        private void SyncEntry(NumericEntry entry)
        {
            if (entry == null || entry.Editing) return;
            var field = Describe(entry.Id);
            if (field == null || double.IsNaN(field.Value) || double.IsInfinity(field.Value)) return;
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
        }

        private bool FieldApplies(string fieldId)
        {
            switch (fieldId)
            {
                case FieldMoveMm: return IsMove && !_rotating && Axes.Count > _axisIndex;
                case FieldMoveDeg: return IsMove && _rotating && Axes.Count > _axisIndex;
                case FieldValueMm: return _command != null && !IsMove && _type != null && _type != "angle";
                case FieldValueDeg: return _command != null && !IsMove && _type == "angle";
                case FieldClearance: return true;
                default: return false;
            }
        }

        /// <summary>A preview can be requested now: a movable occurrence, or a relation with both references and a type.</summary>
        private bool PreviewWanted => IsMove ? _occurrence?.CanMove == true
            : _command != null && _type != null && _a != null && _b != null;

        /// <summary>The draft has no preview yet (never asked, or invalidated by an edit); a failed or running preview is not asked again.</summary>
        private bool NeedsPreview => _session != null && (_session.Status == DesignStatus.Empty || _session.Status == DesignStatus.Draft
            || (_session.Status == DesignStatus.Error && _session.Error == DraftInvalidated));

        private void ApplyField(string fieldId, double value)
        {
            switch (fieldId)
            {
                case FieldMoveMm:
                    InvalidateDraft(); _translation = Axes[_axisIndex] * value; Draw(); Preview(); break;
                case FieldMoveDeg:
                    InvalidateDraft(); _angle = value; Draw(); Preview(); break;
                case FieldValueMm:
                case FieldValueDeg:
                    InvalidateDraft(); _value = value; if (_b != null) Preview(); else Refresh(); break;
                case FieldClearance:
                    if (value < 0 || value > 10000 || Math.Abs(value - Math.Round(value, 3)) > 1e-9)
                    { SetNotice("Il gioco minimo deve essere tra 0 e 10000 mm, con massimo tre decimali."); SyncEntries(); return; }
                    InvalidateDraft(); _clearance = value; if (_command != null && PreviewWanted) Preview(); else Refresh();
                    break;
            }
        }

        /// <summary>Arms a field of the current command for keypad/voice input. Only that field can then be set.</summary>
        public bool TryArmField(string fieldId)
        {
            LastFieldError = null;
            bool ok = Idle && fieldId != null && Describe(fieldId) != null && (fieldId == FieldClearance || FieldApplies(fieldId));
            if (!ok) { LastFieldError = "Campo non disponibile: avvia prima il comando corrispondente."; return false; }
            _armedField = fieldId;
            UpdateChips();
            ArmedFieldChanged?.Invoke();
            return true;
        }

        public void DisarmField()
        {
            if (_armedField == null) return;
            _armedField = null;
            UpdateChips();
            ArmedFieldChanged?.Invoke();
        }

        /// <summary>
        /// Sets the armed field (same path as the keypad): the draft is updated and a new preview requested, Apply stays invalid
        /// until it is rendered. Any other field id, a non-finite or out-of-range value is refused.
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
            return true;
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

        /// <summary>The palette closed the keypad (Annulla / X): the pending prompt is gone.</summary>
        private void DropClosedKeypad()
        {
            if (_ask != null && _shell != null && !_shell.Palette.KeypadVisible) _ask = null;
        }

        private Vector3 ModelCenterWorld()
        {
            var bounds = ScenePlacement.LocalBounds(_view.transform);
            return _view.transform.TransformPoint(bounds.center);
        }

        private void UpdateChips()
        {
            if (_moveChip == null) return;
            bool editable = Active && _session?.CanEdit == true && _context != null;
            bool move = editable && IsMove && _occurrence != null && Axes.Count > _axisIndex && _occurrence.Center.HasValue;
            bool relation = editable && _command != null && !IsMove && _type != null;
            bool draft = editable && _command != null && (IsMove ? move : true);
            _moveChip.Canvas.gameObject.SetActive(move);
            _valueChip.Canvas.gameObject.SetActive(relation);
            _clearChip.Canvas.gameObject.SetActive(draft);
            bool modified = InDraft && NeedsPreview;
            var center = ModelCenterWorld();
            if (move)
            {
                var entry = EntryFor(MoveField);
                if (!ReferenceEquals(entry, _moveChipEntry)) { _moveChipEntry = entry; _moveChip.Bind(entry, _rotating ? "Rotazione" : "Spostamento"); }
                SyncEntries();
                _moveChip.Place(_view.transform.TransformPoint(CadCoordinates.ToLocal(_occurrence.Center.Value)) + Vector3.up * 0.12f);
                _moveChip.Armed = _armedField == MoveField; _moveChip.SetModified(modified);
            }
            if (relation)
            {
                var entry = EntryFor(ValueField);
                if (!ReferenceEquals(entry, _valueChipEntry)) { _valueChipEntry = entry; _valueChip.Bind(entry, _type == "angle" ? "Angolo" : "Distanza"); }
                SyncEntries();
                var anchor = _a?.Point.HasValue == true ? _view.transform.TransformPoint(CadCoordinates.ToLocal(_a.Point.Value)) : center;
                _valueChip.Place(anchor + Vector3.up * 0.12f);
                _valueChip.Armed = _armedField == ValueField; _valueChip.SetModified(modified);
            }
            if (draft)
            {
                SyncEntries();
                _clearChip.Place(center + Vector3.up * 0.22f - (_head != null ? _head.right * 0.15f : Vector3.zero));
                _clearChip.Armed = _armedField == FieldClearance; _clearChip.SetModified(modified);
            }
        }

        private void ShowRing(SelectionKind kind, Vector3 worldPoint)
        {
            if (_ring == null || _head == null) return;
            var actions = (_catalog != null && ReferenceEquals(_catalog.Active, this) ? _catalog.Context(kind) : ContextActions(kind)).ToArray();
            if (actions.Length == 0) { HideRing(); return; }
            _ring.Show(worldPoint + Vector3.up * 0.01f, actions, _head);
        }

        private void HideRing() => _ring?.Hide();

        /// <summary>M9: nothing for X to back out of; only then a held X means "Torna". Mirrors the <see cref="Back"/> chain.</summary>
        public bool AtRest => Active && !(_shell != null && (_shell.Palette.KeypadVisible || _shell.Palette.InTabGroup))
            && _ask == null && !(_ring != null && _ring.Visible) && _picker == null && _armedField == null && !(_isolation != null && _isolation.Active) && !_dragging && !InDraft
            && !Locked && !RequiresCadReview && (_session == null || _session.Status == DesignStatus.Empty);

        /// <summary>
        /// X: closes the keypad, else the ring, else the open list, else the armed field, else releases the isolated component,
        /// else drops the command being drafted. Local state only: it never touches the CAD.
        /// </summary>
        public void Back()
        {
            if (!Active) return;
            if (_shell != null && _shell.Palette.KeypadVisible) { _shell.Palette.HideKeypad(); _ask = null; return; }
            if (_shell == null && _ask != null) { _ask.CancelEdit(); _ask = null; return; }
            if (_ring != null && _ring.Visible) { HideRing(); return; }
            if (_picker != null) { ClosePicker(); return; }
            if (_shell != null && _shell.Palette.TryLeaveGroup(ContextTabs.Inspect)) return;
            if (_armedField != null) { DisarmField(); return; }
            if (_isolation != null && _isolation.Active) { ReleaseIsolation(); Refresh(); return; }
            if (_dragging || Locked) return;
            if (InDraft || (_session != null && _session.Status != DesignStatus.Empty)) CancelCommand();
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
            _bench.ApplyAssembly(_view.transform, ModelExtentM());
            PlaceCommitBar(frame);
        }

        private void PlaceCommitBar(WorkbenchFrame frame)
        {
            if (_shell == null || frame == null) return;
            var p = WorkbenchLayout.CommitBarPosition(frame);
            _shell.PlaceCommitBar(new Vector3((float)p.X, (float)p.Y, (float)p.Z), Quaternion.Euler(45, (float)frame.YawDegrees, 0));
        }

        /// <summary>The model changed (new revision/scene): put it back in the raised assembly pose.</summary>
        public void RefreshWorkbench()
        {
            if (!Active || _bench == null || _view == null) return;
            _bench.ApplyAssembly(_view.transform, ModelExtentM());
        }

        /// <summary>Y short press: fit the assembly to the raised box again.</summary>
        public void FitView()
        {
            if (!Active || _bench == null) return;
            _bench.Fit();
        }

        /// <summary>Y long press: new bench frame from the head.</summary>
        public void RecenterView()
        {
            if (!Active || _bench == null || _head == null) return;
            var frame = _bench.Recenter(_head);
            PlaceCommitBar(frame);
        }

        // ---------------------------------------------------------------- isolation (visual only)

        private WorkbenchFrame IsolationFrame()
        {
            if (_bench?.Frame != null) return _bench.Frame;
            var p = _head != null ? _head.position : Vector3.zero;
            return WorkbenchFrame.FromHead(new CadPoint(p.x, p.y, p.z), _head != null ? _head.eulerAngles.y : 0, null);
        }

        /// <summary>The selected component comes halfway to the user and the rest of the assembly fades. Nothing reaches Inventor.</summary>
        public bool Isolate()
        {
            if (!Active || _occurrence == null || _isolation == null) return false;
            HideRing();
            if (_isolation.Active && _isolation.OccurrenceId == _occurrence.Id) return true;
            if (!_isolation.Isolate(_occurrence.Id, IsolationFrame())) { SetNotice("Il componente non è nella scena."); Refresh(); return false; }
            SetNotice("Isolato: " + _occurrence.Name + ". Rilascia o premi X per rimetterlo al suo posto.");
            return true;
        }

        private void ReleaseIsolation(bool immediate = false)
        {
            if (_isolation != null && _isolation.Active) _isolation.Release(immediate);
        }

        private void OnIsolationChanged() { if (this != null) Refresh(); }

        // ---------------------------------------------------------------- reading

        private async void Load() { try { await LoadAsync(null); } catch (Exception ex) { SetNotice(ex.Message); Refresh(); } }

        private async Task LoadAsync(string occurrenceId)
        {
            if (!_online || !Active || _state == null || _kind != "assembly" || _backend == null || _busy) return;
            int generation = _generation; var state = _state; _busy = true;
            bool announce = false;
            try
            {
                var context = await _backend.GetAssemblyContextAsync(state, occurrenceId, _reads.Token);
                if (generation != _generation) return;
                _context = context;
                if (context.Truncated) SetNotice("Elenco parziale. Alcuni riferimenti non sono disponibili.");
                else _notice = "";
                if (occurrenceId != null) { _occurrence = context.Occurrences.FirstOrDefault(o => o.Id == occurrenceId); announce = _occurrence != null; }
                if (_session.Status == DesignStatus.Empty)
                {
                    var history = await _backend.GetHistoryAsync(state, _reads.Token);
                    if (generation != _generation) return;
                    _history = history;
                }
            }
            catch (OperationCanceledException) { }
            finally { if (generation == _generation) { _busy = false; Draw(); Refresh(); if (announce) AnnounceSelection(); } }
        }

        public async Task SelectOccurrenceAsync(string id)
        {
            if (!Editable) return;
            if (_occurrence?.Id != id)
            { InvalidateDraft(); ReleaseIsolation(); _translation = default; _angle = 0; _axisIndex = 0; }
            CancelReads(); await LoadAsync(id);
        }

        private async void PickBody(CadBody body, int triangle, Vector3 hit)
        {
            try { await PickBodyAsync(body, triangle, hit); }
            catch (Exception ex) { SetNotice(ex.Message); Refresh(); }
        }

        private async Task PickBodyAsync(CadBody body, int triangle, Vector3 hit)
        {
            if (!Editable || body == null) return;
            string id = body.Instance.OccurrenceId;
            await SelectOccurrenceAsync(id);
            if (!Active || _context == null || _busy || _occurrence?.Id != id) return;
            if (IsMove) { Draw(); Refresh(); return; }
            if (_pickEdges)
            {
                var edges = _context.References.Where(r => r.Kind == "edge" && r.Available).Select(r => new DesignEdge(r.Id, r.Geometry, r.Polyline)).ToArray();
                var edgeId = CadCoordinates.PickEdge(_view.transform, edges, new Ray(_ray.Origin.position, _ray.Origin.forward), requireVisible: true);
                if (edgeId != null) ChooseReference(_context.References.First(r => r.Id == edgeId));
                return;
            }
            var range = body.Primitive.FaceMap.FaceAtTriangle(triangle);
            var reference = range == null ? null : _context.References.FirstOrDefault(r => r.Kind == "face" && r.BodyIndex == body.Primitive.BodyIndex && r.FaceOrdinal == range.Ordinal);
            if (reference != null) ChooseReference(reference);
            // A component alone, with no command in progress and no pair of references yet, opens the ring: Isola, Sposta, Vincola, Apri.
            if (_command == null && _b == null) ShowRing(SelectionKind.Component, hit);
        }

        public void ChooseReference(AssemblyReference reference)
        {
            if (!Editable || reference == null || !reference.Available) return;
            HideRing();
            if (IsMove)
            {
                // Picking A/B starts a relation workflow, even if Sposta was used earlier.
                // A move ghost belongs to the old workflow and must not survive this switch.
                _session?.Cancel();
                _command = null; _type = null; _translation = default; _angle = 0; _dragging = false;
            }
            ReleaseIsolation();
            if (_a == null || _b != null) { _a = reference; _b = null; }
            else if (_a.OccurrenceId == reference.OccurrenceId) { SetNotice("Scegli il secondo riferimento su un altro componente."); Refresh(); return; }
            else _b = reference;
            InvalidateDraft(); Draw();
            if (_b != null && (_command == "assembly_constraint" || _command == "assembly_joint")) Preview();
            else if (_b != null) { Refresh(); OpenConstraintPicker(); return; }
            Refresh();
            if (_b == null) SetNotice("A: " + reference.Name + ". Scegli il secondo riferimento su un altro componente.");
        }

        public void BeginMove()
        {
            if (!Editable || _occurrence?.CanMove != true) return;
            HideRing(); ClosePicker(false); ReleaseIsolation();
            _command = "assembly_move"; _type = null; _a = _b = null; _translation = default; _angle = 0;
            _rotating = _occurrence.TranslationAxes.Count == 0; _axisIndex = 0;
            InvalidateDraft(); Draw(); Refresh();
            SetNotice("Sposta: punta il pezzo o l'asse giallo e trascina con il Trigger tenuto. Modo e asse nella scheda Vista.");
        }

        public void ChooseConstraint(string type)
        {
            if (!Editable) return;
            HideRing(); ReleaseIsolation();
            _command = "assembly_constraint"; _type = type; InvalidateDraft();
            if (_b != null) Preview(); else { Refresh(); SetNotice("Scegli i riferimenti A e B su due componenti diversi."); }
        }

        public void ChooseJoint(string type)
        {
            if (!Editable) return;
            HideRing(); ReleaseIsolation();
            _command = "assembly_joint"; _type = type; InvalidateDraft();
            if (_b != null) Preview(); else { Refresh(); SetNotice("Scegli le origini A e B su due componenti diversi."); }
        }

        private void InvalidateDraft()
        { if (_session?.CanEdit == true) _session.RejectDraft(DraftInvalidated); _history = null; }

        private JObject Operation()
        {
            if (IsMove)
            {
                if (_occurrence?.CanMove != true) throw new InvalidOperationException("Movimento non disponibile.");
                return AssemblyOperations.Move(_occurrence.Id, _translation, _angle != 0 ? Axes[_axisIndex] : (CadPoint?)null,
                    _angle != 0 ? _occurrence.Center : null, _angle);
            }
            if (_command == "assembly_constraint") return AssemblyOperations.Constraint(_type, _a, _b, _value, _opposed, _inside);
            if (_command == "assembly_joint") return AssemblyOperations.Joint(_type, _a, _b, _value, _flipOrigin, _flipAlignment);
            throw new InvalidOperationException("Scegli un comando Assieme.");
        }

        // ---------------------------------------------------------------- session

        private async void Preview() { try { await PreviewAsync(); } catch (Exception ex) { SetNotice(ex.Message); Refresh(); } }

        public async Task PreviewAsync()
        {
            if (!Editable) return;
            try
            {
                _session.SetDraft(new JArray(Operation()), _clearance > 0 ? new[] { "min_clearance:" + _clearance.ToString("0.###", CultureInfo.InvariantCulture) + "mm" } : null);
                await _session.PreviewAsync();
            }
            catch (Exception ex) { if (_session.CanEdit) _session.RejectDraft(ex.Message); _notice = ex.Message; }
            Refresh();
        }

        private void Changed()
        {
            if (_session.Status == DesignStatus.PreviewReady && Active && _rendered != _session.Preview?.PlanId)
            {
                try { _preview.Show(_session.Preview); _rendered = _session.Preview.PlanId; _session.ConfirmRendered(_rendered); }
                catch (Exception ex) { SetNotice("Anteprima non visualizzabile: " + ex.Message); }
            }
            if (_session.Preview == null) { _preview?.Clear(); _rendered = null; }
            ShowErrorDetail();
            Refresh();
        }

        /// <summary>The commit bar carries a short message; the detail goes to the HUD, once per error.</summary>
        private void ShowErrorDetail()
        {
            if (_session.Status != DesignStatus.Error || string.IsNullOrEmpty(_session.Error) || _session.Error == DraftInvalidated) { _lastErrorShown = null; return; }
            if (_session.Error == _lastErrorShown) return;
            _lastErrorShown = _session.Error;
            Haptics.Play(HapticPulse.Error);
            HudMessage?.Invoke("Comando non completato: " + _session.Error + "\nLa bozza resta modificabile: correggi i parametri e riprova.");
        }

        /// <summary>Applies the rendered preview. The only caller is the commit bar action "Applica" (M5-11).</summary>
        public async Task ApplyAsync()
        {
            if (_session?.CanApply != true) return;
            if (_preview?.IsShowing != true || _preview.PlanId != _session.Preview?.PlanId)
            { _session.RejectDraft("Anteprima non visibile: ricalcolala prima di applicare."); return; }
            await MutateAsync(() => _session.ApplyAsync());
        }

        private async void ApplyPressed()
        {
            var session = _session;
            try { await ApplyAsync(); }
            catch (Exception ex) { SetNotice(ex.Message); Refresh(); return; }
            if (this != null && session == _session && session.LastCommit != null && !session.CommitOutcomeUnknown)
            { CommitBar.MarkApplied(Time.unscaledTimeAsDouble); Haptics.Play(HapticPulse.Success); }
        }

        private async void History(bool redo)
        {
            try { await MutateAsync(() => _session.ApplyHistoryAsync(_history, redo)); }
            catch (Exception ex) { SetNotice(ex.Message); Refresh(); }
        }

        private async Task MutateAsync(Func<Task> mutation)
        {
            if (!Editable || _mutations > 0) return;
            var owner = _session; _mutations++;
            Refresh();
            try { await mutation(); } finally { _mutations--; }
            if (this == null || owner != _session) return;
            if (!owner.CommitOutcomeUnknown && owner.Status == DesignStatus.RefreshRequired) await RefreshAsync(false);
            Refresh();
        }

        private async Task RefreshAsync(bool reviewed)
        {
            if (!_online || _backend == null || _mutations > 0) return;
            var backend = _backend; var owner = _session; int generation = _generation;
            var state = await backend.GetDocumentStateAsync(_reads.Token);
            if (this == null || backend != _backend || owner != _session || generation != _generation) return;
            owner.AcknowledgeRefresh(state, reviewed); _reviewAfterRebind = false; _state = state; Reset(); Load();
        }

        private async void Review()
        {
            try { await RefreshAsync(true); } catch (Exception ex) { SetNotice(ex.Message); }
            Refresh();
        }

        private void CancelCommand()
        {
            if (RequiresCadReview) return;
            try { _session?.Cancel(); Reset(); Load(); _notice = ""; }
            catch (Exception ex) { SetNotice(ex.Message); }
            Refresh();
        }

        /// <summary>Activates the open definition document in Inventor (existing path of the document browser).</summary>
        private async Task<bool> ActivateDefinitionAsync(bool leaveCommand)
        {
            if (!Editable || RequiresCadReview || _occurrence == null || !(_backend is IInspectionBackend inspection)) return false;
            string id = _occurrence.DefinitionId;
            if (string.IsNullOrEmpty(id)) { SetNotice("Il componente non ha un documento di definizione."); return false; }
            if (leaveCommand) { _session.Cancel(); CancelReads(); }
            int generation = _generation;
            var token = _reads.Token;
            _busy = true; Refresh();
            try
            {
                var documents = await inspection.ListOpenAsync(token);
                if (generation != _generation) return false;
                if (!documents.Any(d => d.Id == id)) throw new InvalidOperationException("La definizione non è già aperta in Inventor.");
                await inspection.ActivateOpenAsync(id, token);
                if (generation != _generation) return false;
                SetNotice("Documento attivato. Attendo la scena da Inventor…");
                return true;
            }
            catch (OperationCanceledException) { return false; }
            catch (Exception ex) { if (generation == _generation) SetNotice(ex.Message); return false; }
            finally { if (generation == _generation) { _busy = false; Refresh(); } }
        }

        private async void OpenIsolated(bool lamiera)
        {
            try
            {
                var occurrence = _occurrence;
                var graph = _graph;
                if (!await ActivateDefinitionAsync(false)) return;
                if (occurrence == null) return;
                EntryRequested?.Invoke(occurrence.DefinitionId, occurrence.Id, FindPose(graph?.Root, occurrence.Id), lamiera);
            }
            catch (Exception ex) { SetNotice(ex.Message); Refresh(); }
        }

        private void ActivateSubassembly() => ActivateSubassemblyCore(false);

        /// <param name="enter">Double Trigger: the router must push a level, so the entry is announced like for a part.</param>
        private async void ActivateSubassemblyCore(bool enter)
        {
            try
            {
                if (_occurrence?.Kind != "assembly") return;
                var occurrence = _occurrence;
                var graph = _graph;
                if (await ActivateDefinitionAsync(true))
                {
                    if (enter) EntryRequested?.Invoke(occurrence.DefinitionId, occurrence.Id, FindPose(graph?.Root, occurrence.Id), false);
                    _context = null; _notice = "Attendo il sottoassieme attivo. Le modifiche alla definizione riguardano tutte le sue istanze."; HudMessage?.Invoke(_notice);
                }
            }
            catch (Exception ex) { SetNotice(ex.Message); Refresh(); }
        }

        /// <summary>Why a double Trigger cannot enter the selected component now, or null when it can.</summary>
        private string EntryBlockedReason()
        {
            if (!Active) return "Assieme non è aperto.";
            if (RequiresCadReview) return "Revisione CAD da fare: controlla il documento in Inventor prima di aprire il componente.";
            if (_dragging || _twoHandActive || _grab != null) return "Maniglia in uso: rilasciala prima di aprire il componente.";
            if (_session?.Status == DesignStatus.Previewing || _session?.Status == DesignStatus.PreviewReady) return "Anteprima in corso: applica o annulla prima di aprire il componente.";
            if (_command != null || _ask != null) return "Comando in corso: applica o annulla prima di aprire il componente.";
            if (_occurrence == null) return "Seleziona prima un componente: tocca il pezzo.";
            if (!Editable) return CommandReason();
            if (!(_backend is IInspectionBackend)) return "Apertura documenti non disponibile.";
            if (string.IsNullOrEmpty(_occurrence.DefinitionId)) return "Il componente non ha un documento di definizione.";
            return null;
        }

        /// <summary>
        /// Double Trigger on a component (M9): activates its definition in Inventor through the existing path and announces the
        /// entry. A part opens its context; a sub-assembly becomes a new level (edits affect all its instances). When blocked,
        /// nothing is activated and the HUD says why.
        /// </summary>
        public bool TryEnterSelected(out string blockedReason)
        {
            blockedReason = EntryBlockedReason();
            if (blockedReason != null) { SetNotice(blockedReason); return false; }
            HideRing();
            if (_occurrence.Kind == "assembly") ActivateSubassemblyCore(true);
            else OpenIsolated(false);
            return true;
        }

        /// <summary>Voice «apri &lt;componente&gt;»: finds a unique occurrence of the loaded assembly by normalized name (no side effects).</summary>
        public VoiceOpenResult FindOccurrenceByName(string spokenName)
        {
            if (!Active || _context == null)
                return new VoiceOpenResult(VoiceOpenStatus.Unavailable, null, "Componenti non ancora caricati.");
            return OccurrenceNameMatcher.Match(_context.Occurrences.Select(o => new KeyValuePair<string, string>(o.Id, o.Name)), spokenName);
        }

        /// <summary>
        /// Voice «apri &lt;componente&gt;»: selects the unique occurrence with that name and enters it through the same path as the
        /// double Trigger (<see cref="TryEnterSelected"/>, same guards and HUD). Ambiguous or unknown names change nothing.
        /// </summary>
        public VoiceOpenResult OpenByName(string spokenName)
        {
            var found = FindOccurrenceByName(spokenName);
            if (found.Found) OpenOccurrenceAsync(found.OccurrenceId);
            return found;
        }

        private async void OpenOccurrenceAsync(string id)
        {
            try
            {
                await SelectOccurrenceAsync(id);
                if (!Active || _occurrence?.Id != id) { SetNotice(Editable ? "Componente non selezionabile." : CommandReason()); Refresh(); return; }
                TryEnterSelected(out _);
            }
            catch (Exception ex) { SetNotice(ex.Message); Refresh(); }
        }

        /// <summary>Test seam: seconds clock of the double Trigger detector (default: unscaled game time).</summary>
        public Func<double> DoubleTriggerClock { get; set; }
        /// <summary>0..1 of the double-Trigger window after a first valid press (for the controller legend ring); 0 otherwise.</summary>
        public float DoubleTriggerProgress => (float)_doubleTrigger.PendingProgress(DoubleTriggerClock != null ? DoubleTriggerClock() : Time.unscaledTimeAsDouble);

        // ---------------------------------------------------------------- labels

        private static string Fmt(double value) => value.ToString("0.###", Italian);

        private static string RelationLabel(string type) => type switch
        {
            "mate" => "Accoppia", "flush" => "Allinea", "mate_axis" => "Assi coincidenti",
            "insert" => "Inserisci", "angle" => "Angolo", "tangent" => "Tangente",
            "rigid" => "Rigido", "rotational" => "Rotazionale", "revolute" => "Rotazionale",
            "slide" => "Scorrevole", "slider" => "Scorrevole", "cylindrical" => "Cilindrico",
            "planar" => "Planare", "ball" => "Sferico", _ => type
        };

        private void Draw() => _visuals?.Show(_occurrence, _a, _b, IsMove, _rotating, _axisIndex);

        // ---------------------------------------------------------------- controller input (XrInput events + per-frame steps)

        private bool InputTracked => _input == null || _input.PenTracked;
        private bool Precision => _input != null && _input.Precision;

        /// <summary>Runners with a synthetic pen set this so the real controller's UI hit cannot swallow the gesture.</summary>
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

        private bool CanCapture => Idle && IsMove && Axes.Count > _axisIndex && _ask == null;

        private void Update()
        {
            if (Active)
            {
                CommitBar.Tick(Time.unscaledTimeAsDouble);
                UpdateBar();
                DropClosedKeypad();
            }
            if (!Active || !_visible || _ray == null || _ray.Origin == null) return;
            if (!InputTracked) { _hoverKey = 0; return; }
            if (_dragging) { DragStep(); return; }
            if (_twoHandActive) { TwoHandStep(); return; }
            if (_grab != null) { MoveGrab(); return; }
            if (!TryPenRay(out var ray, out bool overUi)) return;
            HoverFeedback(ray, overUi);
        }

        /// <summary>Best effort: a short pulse when the ray enters an interactive target (palette, chip, ring, move handle).</summary>
        private void HoverFeedback(Ray ray, bool overUi)
        {
            int key = overUi ? 1 : (CanCapture && HitMoveTarget(ray)) ? 2 : 0;
            if (key != _hoverKey && key != 0) Haptics.Play(HapticPulse.Hover);
            _hoverKey = key;
        }

        /// <summary>
        /// Trigger press. With Sposta armed and the ray on the handle (or the selected component) it captures the handle (M5-08):
        /// from here on only the local draft changes. Otherwise it picks the component under the ray.
        /// </summary>
        private void OnPenPressed()
        {
            if (!TryPenRay(out var ray, out bool overUi)) return;
            if (PickSuppressed?.Invoke() == true) return;
            if (overUi || GripDown || _dragging || _twoHandActive) return;
            if (CanCapture && HitMoveTarget(ray)) { _doubleTrigger.Reset(); BeginDrag(); return; }
            bool picked = CadRaycaster.TryPick(ray, 20, out var body, out int triangle, out var hit);
            string target = picked ? ControllerRay.TargetId(body) : null;
            var dir = ray.direction;
            double now = DoubleTriggerClock != null ? DoubleTriggerClock() : Time.unscaledTimeAsDouble;
            if (_doubleTrigger.Press(now, target, dir.x, dir.y, dir.z))
            {
                // M9: the double Trigger is the Trigger's secondary action; the dispatcher says whether the current state binds it.
                if (_input != null && !_input.Dispatcher.InvokeSecondary(Key.Trigger)) return;
                // The first press selected the component as today; the second enters it (or says why it cannot).
                if (_occurrence?.Id != target && _occurrence != null && !_busy) SetNotice("Selezione in corso: ripeti il doppio Trigger.");
                else if (_occurrence?.Id != target && _busy) SetNotice("Lettura Inventor in corso: ripeti il doppio Trigger.");
                else TryEnterSelected(out _);
                return;
            }
            if (!Editable || _ask != null) return;
            if (picked) PickBody(body, triangle, hit);
            else HideRing();   // the selection persists while the user locates the second reference
        }

        private bool HitMoveTarget(Ray ray)
        {
            if (_visuals.HitHandle(ray)) return true;
            return _occurrence != null && CadRaycaster.TryPick(ray, 20, out var body, out _, out _)
                && body.Instance?.OccurrenceId == _occurrence.Id;
        }

        private void BeginDrag()
        {
            _grab = null; _dragging = true;
            _dragFactor = Precision ? 0.1f : 1f;
            Anchor();
            InvalidateDraft(); HideRing(); Refresh();
        }

        private void Anchor()
        {
            _dragStart = CadCoordinates.FromWorld(_view.transform, _ray.Origin.position);
            _dragRotation = _ray.Origin.rotation; _startTranslation = _translation; _startAngle = _angle;
        }

        private static bool Finite(Vector3 v) => !(float.IsNaN(v.x + v.y + v.z) || float.IsInfinity(v.x + v.y + v.z));

        /// <summary>Per frame while the handle is captured: the draft follows the hand (precision = 10x slower, re-anchored).</summary>
        private void DragStep()
        {
            if (!CanCaptureDrag) { EndDrag(false, false); return; }
            if (!Finite(_ray.Origin.position)) return;
            float factor = Precision ? 0.1f : 1f;
            if (!Mathf.Approximately(factor, _dragFactor)) { _dragFactor = factor; Anchor(); }
            var axis = Axes[_axisIndex];
            if (_rotating)
            {
                var worldAxis = _view.transform.TransformDirection(CadCoordinates.ToLocal(axis)).normalized;
                // CAD->Unity reflects X, so axial rotation changes sign.
                _angle = _startAngle - factor * AssemblyManipulation.TwistDegrees(_dragRotation, _ray.Origin.rotation, worldAxis);
            }
            else _translation = _startTranslation + axis * (factor * (CadCoordinates.FromWorld(_view.transform, _ray.Origin.position) - _dragStart).Dot(axis));
            SyncEntries();
            UpdateChips();
        }

        // The capture continues through the preview of its own draft: only the workspace closing or a change of context ends it.
        private bool CanCaptureDrag => Active && IsMove && Axes.Count > _axisIndex && _session?.CanEdit == true && _ask == null;

        /// <summary>
        /// Ends the handle capture. <paramref name="preview"/>: Trigger released, the draft is final and gets a preview request (never a
        /// commit). <paramref name="error"/>: tracking lost, the draft is dropped, the controller buzzes, no preview.
        /// </summary>
        private void EndDrag(bool preview, bool error)
        {
            if (!_dragging) return;
            if (preview && CanCaptureDrag && Finite(_ray.Origin.position)) DragStep();
            _dragging = false;
            if (error)
            {
                Haptics.Play(HapticPulse.Error);
                InvalidateDraft();
                SetNotice("Tracking perso: ripeti il gesto intenzionale.");
            }
            Refresh();
            if (preview && Active && _session?.CanEdit == true) Preview();
        }

        private void EndViewGrabs() { _grab = null; _gripHeld = false; _twoHandActive = false; }

        private void OnPenReleased() => EndDrag(true, false);

        private void OnTrackingLost()
        {
            // Raised before the release of a held Trigger: the capture is closed here, without a preview.
            if (!Active) return;
            EndDrag(false, true);
            EndViewGrabs(); _hoverKey = 0;
        }

        private void OnGrabStarted()
        {
            if (!TryPenRay(out var ray, out bool overUi)) return;
            _gripHeld = true;
            if (_dragging || _twoHandActive) return;
            BeginGrab(ray, overUi);
        }

        private void OnGrabEnded() { _grab = null; _gripHeld = false; }

        /// <summary>Grip alone: the assembly moves and turns with the hand. View only.</summary>
        private void BeginGrab(Ray ray, bool overUi)
        {
            _grab = null;
            if (!overUi && CadRaycaster.TryPick(ray, 20, out _, out _, out _)) { _bench?.Snap(); _grab = _view.transform; }
            if (_grab == null) return;
            _grabPosition = _ray.Origin.InverseTransformPoint(_grab.position);
            _grabRotation = Quaternion.Inverse(_ray.Origin.rotation) * _grab.rotation;
        }

        private void MoveGrab()
        {
            var position = _ray.Origin.TransformPoint(_grabPosition);
            if (!Finite(position)) return;
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
            EndDrag(false, false); _grab = null;
            _bench?.Snap();
            var root = _view.transform; var right = _ray.Origin.position;
            _twoHandVector = right - left; _twoHandMid = (right + left) * 0.5f;
            _twoHandRootPosition = root.position; _twoHandRootRotation = root.rotation; _twoHandRootScale = root.localScale.x;
            _twoHandActive = _twoHandVector.sqrMagnitude > 1e-6f;
        }

        /// <summary>Two hands: rotate, scale and move the assembly. View only; the CAD data never changes.</summary>
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

        /// <summary>Left stick up/down: zooms the model. View only, clamped to the layout limits.</summary>
        private void OnZoom(float axis)
        {
            if (!Active || _view == null || _dragging || _twoHandActive) return;
            ZoomView(axis, Mathf.Clamp(Time.unscaledDeltaTime, 1f / 120f, 1f / 20f));
        }

        /// <summary>Scales the model about the centre of its bounds, never outside [MinScale, MaxScale].</summary>
        public void ZoomView(float axis, float seconds)
        {
            var root = _view.transform;
            _bench?.Snap();
            float current = root.localScale.x;
            float target = Mathf.Clamp(current * Mathf.Exp(axis * ZoomRatePerSecond * seconds), (float)WorkbenchLayout.MinScale, (float)WorkbenchLayout.MaxScale);
            if (Mathf.Approximately(target, current)) return;
            var pivot = root.TransformPoint(ScenePlacement.LocalBounds(root).center);
            root.localScale = Vector3.one * target;
            root.position = pivot + (root.position - pivot) * (target / current);
        }

        /// <summary>Semantic keys routed by <see cref="InputDispatcher"/> (already gated by <see cref="InputMap"/>): Y, A and the right stick.</summary>
        private void OnInvoked(InputAction action, int arg)
        {
            if (!Active) return;
            switch (action)
            {
                case InputAction.Fit: FitView(); break;
                case InputAction.StepChange: OnStepDelta(arg); break;
                case InputAction.StepSize: OnStepSizeDelta(arg); break;
                case InputAction.Isolate: ToggleIsolation(); break;
                case InputAction.OpenKeypad:
                    {
                        string field = _armedField ?? (_dragging ? MoveField : null);
                        if (field != null && _ask == null) AskNumber(field);
                        break;
                    }
                case InputAction.KeypadOk:
                    if (_ask != null && _ask.Editing && !_ask.Commit(out var reason)) { SetNotice(reason); Refresh(); }
                    break;
            }
        }

        /// <summary>A on a selected component: isolates it, or releases the isolation when already isolated.</summary>
        public void ToggleIsolation()
        {
            if (_isolation != null && _isolation.Active) { ReleaseIsolation(); Refresh(); return; }
            Isolate();
        }

        /// <summary>Right stick left/right: one step on the armed chip. Silent when nothing can change now.</summary>
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
            if (_session != null) { _session.Changed -= Changed; _session.Dispose(); }
            if (_isolation != null) _isolation.Changed -= OnIsolationChanged;
            DestroyCanvas(_moveChip != null ? _moveChip.Canvas.gameObject : null);
            DestroyCanvas(_valueChip != null ? _valueChip.Canvas.gameObject : null);
            DestroyCanvas(_clearChip != null ? _clearChip.Canvas.gameObject : null);
            DestroyCanvas(_ring != null ? _ring.Canvas.gameObject : null);
            if (_visuals != null) Release(_visuals); if (_preview != null) Release(_preview); if (_isolation != null) Release(_isolation);
        }

        private static void DestroyCanvas(GameObject go)
        {
            if (go == null) return;
            if (Application.isPlaying) Destroy(go); else DestroyImmediate(go);
        }

        private static void Release(UnityEngine.Object item) { if (Application.isPlaying) Destroy(item); else DestroyImmediate(item); }
    }
}
