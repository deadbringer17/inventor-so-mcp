using InventorXrSo.Core.Input;
using TMPro;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Selection;
using InventorXrSo.Core.Session;
using InventorXrSo.Core.Ui;
using InventorXrSo.Core.Voice;
using InventorXrSo.Unity.Scene;
using InventorXrSo.Unity.Ui;
using InventorXrSo.Xr.Input;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UiSelectionKind = InventorXrSo.Core.Ui.SelectionKind;

namespace InventorXrSo.Xr
{
    /// <summary>
    /// Quest M3/M6: local drafts, explicit Inventor preview and explicit Apply. No floating panel: the actions are declared to
    /// the palette (<see cref="IActionProvider"/>, see DesignActions.cs), the commit bar is the only place that applies.
    /// </summary>
    [DefaultExecutionOrder(115)]
    public sealed partial class DesignWorkspace : MonoBehaviour, IActionProvider, ITabStateSource
    {
        private CadSceneView _view;
        private SelectionVisuals _selection;
        private ControllerRay _ray;
        private Transform _head;
        private UiShell _shell;
        private ActionCatalog _catalog;
        private Workbench _bench;
        private SketchSheetView _sheet;
        private ChipView _chip;
        private RingView _ring;
        private NumericEntry _dimensionEntry, _ask;
        private bool _syncingEntry, _sheetSuppressed;
        private string _ringEdge, _lastErrorShown;
        private DesignPreviewView _previewView;
        private DesignGeometryView _geometry;
        private IDesignWorkspaceBackend _backend;
        private DesignSession _session;
        private bool _reviewAfterRebind;
        private int _pendingMutations;
        private DocumentState _state;
        private DocumentState _sceneState;
        private string _kind, _screen = "tools", _notice = "", _renderedPlan, _face, _feature, _existingSketch;
        private CadPoint _facePoint;
        private Selection _faceSelection;
        private DesignContext _context;
        private DesignHistory _history;
        private SketchDraft _sketch;
        private SketchShape _shape;
        private int _dimensionStep, _dimensionIndex = -1;
        private string _constraintKind;
        private string _parameterName;
        private readonly List<int> _constraintPicks = new List<int>();
        private CadPoint? _first;
        private SketchSnap _candidate;
        private bool _snapLocked, _online, _visible, _busy, _through = true, _negative, _symmetric;
        private string _operation = "join";
        private readonly HashSet<string> _edges = new HashSet<string>();
        private double _dimension = 10, _diameter = 5;
        private int _generation;
        private CancellationTokenSource _reads = new CancellationTokenSource();
        private Transform _grab;
        private Vector3 _grabPosition;
        private Quaternion _grabRotation;
        private bool _dimensionDrag, _gripHeld;
        private bool GripDown => _gripHeld || (_input != null && _input.PenGripHeld);
        private CadPoint _dragStart;
        private double _dragValue;
        private double _dragSide=1, _dragFactor=1;
        private float _dragDraftAt;
        private XrInput _input;
        private Transform _leftHand;
        private bool _twoHandActive;
        private Vector3 _twoHandVector, _twoHandMid, _twoHandRootPosition;
        private Quaternion _twoHandRootRotation;
        private float _twoHandRootScale;
        private int _hoverKey;
        private string _snapKey = "";
        /// <summary>Precision mode (left trigger held): pen drags are ten times slower.</summary>
        public const double PrecisionFactor = 0.1;
        private const float DraftThrottleSeconds = 0.1f, ZoomRatePerSecond = 1.5f;
        private LineRenderer _handle;
        private TextMeshProUGUI _cursorText;
        private Canvas _cursorCanvas;
        public bool Active { get; private set; }
        public bool RequiresCadReview => _reviewAfterRebind || _pendingMutations > 0 || _session?.Status == DesignStatus.Committing || _session?.Status == DesignStatus.RefreshRequired;
        /// <summary>M9: Misura (Ispeziona) is armed and owns the trigger: the workspace does not pick or capture.</summary>
        public Func<bool> PickSuppressed { get; set; }
        /// <summary>M9: a handle is captured; Ispeziona tools suspend until it is released.</summary>
        public bool HandleCaptured => _dimensionDrag;
        /// <summary>M9 input state: the numeric keypad is open (A = OK, X = Annulla).</summary>
        public bool KeypadOpen => Active && (_ask != null || (_shell != null && _shell.Palette.KeypadVisible));
        /// <summary>M9 input state: the dimension chip of a feature is armed or a handle captured (stick right = step, A = keypad).</summary>
        public bool InputArmed => Active && (_dimensionDrag || (_screen == "feature" && DimensionApplies));
        /// <summary>M9 input state: a sketch is open on its plane (A = Snap si/no).</summary>
        public bool SketchOpen => Active && _screen == "sketch" && _sketch?.Frame != null;
        /// <summary>M9: what is true now (sketch open, feature/move in progress): decides the tabs that appear by themselves.</summary>
        public TabState TabState => new TabState { SketchOpen = _screen == "sketch" && _sketch != null, FeatureInProgress = _screen == "feature" };
        public Func<bool> CanEnter { get; set; }
        public event Action<bool> ActiveChanged;
        /// <summary>Raised only when an open workspace closes: the shell restores the Inspect placement of the model.</summary>
        public event Action Closed;
        /// <summary>Notices and error detail for the HUD (the commit bar only carries a short message).</summary>
        public event Action<string> HudMessage;
        /// <summary>Last notice, also sent to <see cref="HudMessage"/> when non-empty.</summary>
        public string Notice => _notice;
        /// <summary>The numeric entry currently waiting for the keypad (or dictation), if any.</summary>
        public NumericEntry ActiveEntry => _ask;

        public void Initialize(CadSceneView view, SelectionVisuals selection, ControllerRay ray, Transform head)
        {
            _view = view; _selection = selection; _ray = ray; _head = head;
            _previewView = view.gameObject.AddComponent<DesignPreviewView>(); _previewView.Initialize(view,head);
            var geometry = new GameObject("Design local geometry"); geometry.transform.SetParent(view.transform, false);
            _geometry = geometry.AddComponent<DesignGeometryView>(); _geometry.Initialize(ray.LineMaterial);
            _handle = CadCoordinates.Line(geometry.transform, "Dimension handle", Array.Empty<CadPoint>(), ray.LineMaterial, 0.003f);
            var label = UiFactory.WorldCanvas(transform, "Sketch coordinates", new Vector2(620, 60));
            _cursorCanvas = label;
            label.transform.localScale = Vector3.one * 0.0005f;
            _cursorText = UiFactory.Label(label.transform, "", 24);
            UiFactory.Stretch(_cursorText.rectTransform); label.gameObject.SetActive(false);
        }

        /// <summary>
        /// Shell parts the workspace draws on: palette/commit bar/HUD through the catalog, plus chip and ring created here.
        /// The bench and the sheet view are optional (not present in headless tests).
        /// </summary>
        public void Attach(UiShell shell, Workbench bench = null, SketchSheetView sheet = null, XrInput input = null)
        {
            _shell = shell; _catalog = shell?.Catalog; _bench = bench; _sheet = sheet;
            AttachInput(input);
            if (shell == null || _head == null) return;
            var eye = _head.GetComponent<Camera>();
            if (_ring == null)
            {
                _ring = RingView.Create(null);
                XrUi.MakeInteractive(_ring.Canvas, eye);
            }
            if (_chip == null)
            {
                _chip = ChipView.Create(null);
                XrUi.MakeInteractive(_chip.Canvas, eye);
                _chip.Face(_head);
                _chip.Bind(DimEntry, "Dimensione");
                _chip.Tapped += OpenDimensionKeypad;
                _chip.Canvas.gameObject.SetActive(false);
            }
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

        public void Bind(IDesignWorkspaceBackend backend)
        {
            CancelReads();
            _reviewAfterRebind |= _pendingMutations>0 || _session?.Status==DesignStatus.Committing
                || _session?.Status==DesignStatus.RefreshRequired;
            if (_session != null) { _session.Changed -= SessionChanged; _session.Dispose(); }
            _backend = backend;
            _session = backend == null ? null : new DesignSession(backend);
            if (_session != null)
            {
                if (_reviewAfterRebind) _session.RequireCadReview();
                _session.Changed += SessionChanged;
            }
            _context = null; _state = null; _online = false; ResetDraft();
            Refresh();
        }
        public void SetScene(LoadedScene scene)
        {
            _sceneState = scene?.Graph.State;
            _kind = scene?.Graph.Kind;
            SetDocumentState(scene?.Graph.State);
        }
        public void SetDocumentState(DocumentState state)
        {
            bool changed = _state?.DocumentId != state?.DocumentId || _state?.Revision != state?.Revision;
            _state = state;
            if (changed) { CancelReads(); _context = null; ResetDraft(); }
            _session?.SetContext(state, _online, _kind == "part");
            if (changed && Active && _online && _kind == "part") LoadContext();
        }
        public void SetOnline(bool online)
        {
            if (_online == online) return;
            _online = online; CancelReads(); _context = null; ResetDraft();
            _session?.SetContext(_state, online, _kind == "part");
            if (online && Active) LoadContext();
            Refresh();
        }
        public void SetVisible(bool visible)
        {
            _visible = visible;
            if (!visible) Close();
        }
        public void Open()
        {
            if (!_visible || CanEnter?.Invoke() == false) return;
            Active = true; _ray.CanPick = false; _selection.Clear();
            _screen = "tools";
            ActiveChanged?.Invoke(true);
            PlaceWorkbench(); Refresh(); LoadContext();
        }
        public void Close()
        {
            bool was = Active;
            _dimensionDrag = false; _dragFactor = 1; EndViewGrabs();   // capture ends with the workspace (M5-08)
            Active = false; CancelReads();
            if (_session != null && _session.Status != DesignStatus.Committing && _session.Status != DesignStatus.RefreshRequired) _session.Cancel();
            ResetDraft(); _selection?.Clear();
            _shell?.Palette.HideKeypad();
            if (_chip != null) _chip.Canvas.gameObject.SetActive(false);
            if (_ray != null) _ray.CanPick = _visible;
            if (was) { _sheet?.Exit(); _bench?.Release(); }
            UpdateBar();
            ActiveChanged?.Invoke(false);
            if (was) Closed?.Invoke();
        }
        private void CancelReads()
        { _generation++; _reads.Cancel(); _reads.Dispose(); _reads = new CancellationTokenSource(); _busy = false; }
        private void ResetDraft()
        {
            _constraintKind=null; _constraintPicks.Clear(); _parameterName=null;
            _dimensionStep = 0; _dimensionIndex = -1;
            _history = null;
            _sketch = null; _first = null; _feature = null; _existingSketch = null; _face = null;
            _faceSelection = null; _ringEdge = null; _ask = null; _lastErrorShown = null;
            _edges.Clear(); _snapLocked = false; _dimensionDrag = false; _grab = null; _gripHeld = false; _twoHandActive = false; _renderedPlan = null;
            _previewView?.Clear(); _geometry?.Clear();
            if (_handle != null) _handle.positionCount = 0;
            if (_cursorCanvas != null) _cursorCanvas.gameObject.SetActive(false);
            _screen = "tools";
            HideRing(); ClosePicker(false);
            if (_sheet != null && _sheet.State == SketchSheetState.Sheet && _bench?.Frame != null) _sheet.ShowModel(_bench.PartPose);
            _sheetSuppressed = false;
        }
        private async void LoadContext()
        {
            if (!_online || _state == null || _kind != "part" || _backend == null || _busy) { Refresh(); return; }
            int generation = _generation; var state = _state; _busy = true; _history = null;
            Refresh();
            try
            {
                var context = await _backend.GetDesignContextAsync(state, _reads.Token);
                if (generation != _generation) return;
                _context = context; _notice = "";
                if (context.Truncated) SetNotice("Elenco CAD parziale: alcune entità superano i limiti.");
                if (_backend is IDesignHistoryBackend historyBackend)
                {
                    _history = null;
                    var history = await historyBackend.GetHistoryAsync(state,_reads.Token);
                    if (generation != _generation) return;
                    _history = history;
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (generation == _generation) SetNotice(ex.Message); }
            finally { if (generation == _generation) { _busy = false; Refresh(); } }
        }
        private void SessionChanged()
        {
            if (_session.Status == DesignStatus.Previewing || _session.Status == DesignStatus.Committing) _history = null;
            if (_session.Preview != null && _session.Status == DesignStatus.PreviewReady && _renderedPlan != _session.Preview.PlanId && Active)
            {
                try
                {
                    _selection.Clear(); _previewView.Show(_session.Preview);
                    _renderedPlan = _session.Preview.PlanId;
                    if (_sketch != null)
                    {
                        var snapshot = _session.Preview.Sketches.FirstOrDefault(s => s.Name == _sketch.Name);
                        if (snapshot != null) _sketch.Frame = snapshot.Frame;
                        _geometry.ShowDraft(_sketch);
                    }
                    _session.ConfirmRendered(_renderedPlan);
                }
                catch (Exception ex) { SetNotice("Anteprima non visualizzabile: " + ex.Message); }
            }
            if (_session.Preview == null) { _previewView.Clear(); _renderedPlan = null; }
            ShowValidationContext();
            ShowErrorDetail();
            if (Active && _sketch?.Frame != null && !_sheetSuppressed && _sheet != null && _sheet.State != SketchSheetState.Sheet) EnterSheet();
            Refresh();
        }

        /// <summary>The commit bar carries a short message; the detail (and the orange-geometry hint) goes to the HUD, once per error.</summary>
        private void ShowErrorDetail()
        {
            if (_session.Status != DesignStatus.Error || string.IsNullOrEmpty(_session.Error)) { _lastErrorShown = null; return; }
            if (_session.Error == _lastErrorShown) return;
            _lastErrorShown = _session.Error;
            Haptics.Play(HapticPulse.Error);
            string detail = "Comando non completato: " + _session.Error;
            if (_geometry != null && _geometry.HasErrorContext) detail += "\nIn arancione: geometria del comando da controllare.";
            HudMessage?.Invoke(detail);
        }

        private void ShowValidationContext()
        {
            _geometry.ClearErrorContext();
            if (_session.Status != DesignStatus.Error) return;
            var paths = new List<IEnumerable<CadPoint>>();
            if (_feature == "fillet" || _feature == "chamfer")
                paths.AddRange(_context.Edges.Where(e=>_edges.Contains(e.Id)).Select(e=>e.PointsMm));
            else if (_sketch?.Frame != null)
                paths.AddRange(_sketch.Elements.Select(e=>e.Outline().Select(_sketch.Frame.ToModel)));
            else if (_feature == "extrude")
                foreach (var sketch in _context.SketchSnapshots.Where(s=>s.Name==_existingSketch))
                    paths.AddRange(sketch.Elements.Select(e=>e.Outline().Select(sketch.Frame.ToModel)));
            else if (_screen=="parameter")
            {
                foreach(var sketch in _context.SketchSnapshots.Where(s=>s.Dimensions.Any(d=>d.Name==_parameterName)))
                    paths.AddRange(sketch.Elements.Select(e=>e.Outline().Select(sketch.Frame.ToModel)));
                // A feature parameter can affect several bodies. Without a precise dependency
                // result, identify the model being edited rather than inventing a failing face.
                if(paths.Count==0) paths.AddRange(_context.Edges.Select(e=>e.PointsMm));
            }
            _geometry.ShowErrorContext(paths);
            if (_feature == "hole" && _face != null)
                _selection.Show(_faceSelection);
        }

        // ---------------------------------------------------------------- notices, bar, chip, ring

        private void SetNotice(string text)
        {
            _notice = text ?? "";
            if (_notice.Length > 0) HudMessage?.Invoke(_notice);
        }

        /// <summary>Content or enablement changed: rebuild the declared actions, the bar and the chip, and tell the catalog.</summary>
        private void Refresh()
        {
            _actions = null;
            UpdateBar();
            UpdateChip();
            _catalog?.NotifyChanged();
        }

        private const string ShortError = "Comando non completato. Correggi e riprova.";

        private void UpdateBar()
        {
            var s = _session;
            bool working = s != null && (s.Status == DesignStatus.Previewing || s.Status == DesignStatus.Committing);
            CommitBar.Update(new CommitBarInputs(
                online: _online,
                outcomeUnknown: s?.CommitOutcomeUnknown == true,
                refreshRequired: s?.Status == DesignStatus.RefreshRequired,
                busy: working || _pendingMutations > 0 || (_busy && InDraftScreen),
                hasPreview: s != null && s.Status == DesignStatus.PreviewReady && s.CanApply,
                hasDraft: s != null && InDraftScreen,
                error: s?.Status == DesignStatus.Error ? ShortError : ""), Time.unscaledTimeAsDouble);
        }

        private bool DimensionApplies => _feature == "extrude" || _feature == "fillet" || _feature == "chamfer" || (_feature == "hole" && !_through);

        private void UpdateChip()
        {
            if (_chip == null) return;
            bool show = Active && _screen == "feature" && DimensionApplies && HasDimensionTarget;
            _chip.Canvas.gameObject.SetActive(show);
            if (!show) return;
            _chip.Place(_view.transform.TransformPoint(CadCoordinates.ToLocal(DimensionEnd)) + Vector3.up * 0.05f);
            _chip.Armed = true;
            _chip.SetModified(_session?.Status == DesignStatus.Draft);
        }

        private NumericEntry DimEntry
        {
            get
            {
                if (_dimensionEntry == null)
                {
                    _dimensionEntry = new NumericEntry(FieldDimension, QuantityUnit.Millimeters, _dimension, MinDimensionMm, MaxDimensionMm);
                    _dimensionEntry.Changed += OnDimensionEntryChanged;
                }
                return _dimensionEntry;
            }
        }

        private void SyncDimensionEntry()
        {
            _syncingEntry = true;
            try { DimEntry.SetValue(_dimension, out _); } finally { _syncingEntry = false; }
        }

        // Keypad confirmation, thumbstick step and dictation all arrive here; typing in the keypad does not.
        private void OnDimensionEntryChanged()
        {
            if (_syncingEntry || _dimensionEntry.Editing) return;
            if (_ask == _dimensionEntry) _ask = null;   // confirmed or cancelled from the keypad
            if (_screen != "feature" || _dimensionEntry.Value == _dimension) return;
            _dimension = _dimensionEntry.Value;
            try { _notice = ""; UpdateDraft(); } catch (Exception ex) { SetNotice(ex.Message); }
            Refresh();
        }

        /// <summary>The palette closed the keypad (Annulla / X): the pending prompt is gone.</summary>
        private void DropClosedKeypad()
        {
            if (_ask != null && _shell != null && !_shell.Palette.KeypadVisible) _ask = null;
        }

        private void OpenDimensionKeypad()
        {
            if (_screen != "feature") return;
            SyncDimensionEntry();
            _ask = DimEntry;
            if (_shell != null) _shell.Palette.ShowKeypad(DimEntry, "Dimensione"); else DimEntry.BeginEdit();
        }

        /// <summary>Thumbstick left/right on the armed chip: one step.</summary>
        public bool NudgeDimension(int direction) => Active && _screen == "feature" && DimensionApplies && DimEntry.Nudge(direction);
        /// <summary>Thumbstick up/down on the armed chip: 0,1 / 1 / 10 step.</summary>
        public void CycleDimensionStep(int direction) { if (Active && _screen == "feature" && DimensionApplies) DimEntry.CycleStep(direction); }

        private void ShowRing(UiSelectionKind kind, Vector3 worldPoint)
        {
            if (_ring == null || _head == null) return;
            var actions = (_catalog != null && ReferenceEquals(_catalog.Active, this) ? _catalog.Context(kind) : ContextActions(kind)).ToArray();
            if (actions.Length == 0) { HideRing(); return; }
            _ring.Show(worldPoint + Vector3.up * 0.01f, actions, _head);
        }

        private void HideRing()
        {
            _ringEdge = null;
            _ring?.Hide();
        }

        /// <summary>
        /// M9: nothing for X to back out of (no keypad, ring, tab group, list, draft screen or pending command): only then
        /// a held X means "Torna". Mirrors the <see cref="Back"/> chain.
        /// </summary>
        public bool AtRest => Active && !(_shell != null && (_shell.Palette.KeypadVisible || _shell.Palette.InTabGroup))
            && _ask == null && !(_ring != null && _ring.Visible) && _picker == null && !InDraftScreen
            && (_session == null || _session.Status == DesignStatus.Empty);

        /// <summary>
        /// X: closes the keypad, else the ring, else the open list, else discards the last draft step (pending dimension
        /// pick, first point, last element). Local draft only: it never touches the CAD.
        /// </summary>
        public void Back()
        {
            if (!Active) return;
            if (_shell != null && _shell.Palette.KeypadVisible) { _shell.Palette.HideKeypad(); _ask = null; return; }
            if (_shell == null && _ask != null) { _ask.CancelEdit(); _ask = null; return; }
            if (_ring != null && _ring.Visible) { HideRing(); return; }
            if (_picker != null) { ClosePicker(); return; }
            if (_shell != null && _shell.Palette.TryLeaveGroup(ContextTabs.Inspect)) return;
            DiscardLastDraftStep();
        }

        private void DiscardLastDraftStep()
        {
            if (_screen != "sketch" || _sketch == null || _session?.CanEdit != true) return;
            if (_dimensionStep != 0) { _dimensionStep = 0; _geometry.ShowDraft(_sketch); Refresh(); return; }
            if (_first.HasValue) { _first = null; _snapLocked = false; Refresh(); return; }
            if (_sketch.Elements.Count == 0) return;
            _sketch.RemoveLast(); UpdateDraft(); Refresh();
        }

        // ---------------------------------------------------------------- workbench and sheet

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
            PlaceCommitBar(frame);
        }

        private void PlaceCommitBar(WorkbenchFrame frame)
        {
            if (_shell == null || frame == null) return;
            var p = WorkbenchLayout.CommitBarPosition(frame);
            _shell.PlaceCommitBar(new Vector3((float)p.X, (float)p.Y, (float)p.Z), Quaternion.Euler(45, (float)frame.YawDegrees, 0));
        }

        /// <summary>The model changed (new revision/scene): put it back on the work plane, or on the sheet if a sketch is open.</summary>
        public void RefreshWorkbench()
        {
            if (!Active || _bench == null || _view == null) return;
            if (_sheet != null && _sheet.State == SketchSheetState.Sheet && _sketch?.Frame != null) EnterSheet();
            else _bench.ApplyPart(_view.transform, ModelExtentM());
        }

        /// <summary>Y short press: fit the model to the work plane (or the sheet) again.</summary>
        public void FitView()
        {
            if (!Active || _bench == null) return;
            if (_sheet != null && _sheet.State == SketchSheetState.Sheet) EnterSheet(); else _bench.Fit();
        }

        /// <summary>Y long press: new bench frame from the head; keeps the sheet if one is open.</summary>
        public void RecenterView()
        {
            if (!Active || _bench == null || _head == null) return;
            var frame = _bench.Recenter(_head);
            PlaceCommitBar(frame);
            if (_sheet != null && _sheet.State == SketchSheetState.Sheet) EnterSheet();
        }

        private void EnterSheet()
        {
            if (_sheet == null || _bench?.Frame == null || _sketch?.Frame == null) return;
            double width = 100, height = 100;
            foreach (var element in _sketch.Elements)
                foreach (var p in element.Outline()) { width = Math.Max(width, Math.Abs(p.X) * 1.25); height = Math.Max(height, Math.Abs(p.Y) * 1.25); }
            _sheet.Enter(_sketch.Frame, width, height, _bench.Frame);
        }

        private void ShowModelView()
        {
            _sheetSuppressed = true;
            if (_sheet != null && _bench?.Frame != null) _sheet.ShowModel(_bench.PartPose);
            Refresh();
        }

        private void ShowSheetView()
        {
            _sheetSuppressed = false;
            EnterSheet();
            Refresh();
        }

        // ---------------------------------------------------------------- commands

        private void StartSketch(string plane, SketchFrame frame)
        {
            ResetDraft(); _sketch = new SketchDraft(plane, frame); _shape = SketchShape.Line; _screen = "sketch";
            UpdateDraft(); EnterSheet(); Refresh(); Preview();
        }
        private void CreateSketch()
        {
            if (_face != null && _context?.PlanarFaces.Contains(_face) == true)
                StartSketch(_face, null);
            else OpenPicker("Piano dello schizzo", _context.Planes.Select(plane =>
                new PickerItem(plane.Name, () => StartSketch(plane.Reference, plane.Frame))));
        }
        private void Feature(string feature)
        {
            _dimensionStep=0; _dimensionIndex=-1;
            if (feature != "extrude") { _sketch = null; _existingSketch = null; _geometry.Clear(); }
            _feature = feature; _screen = "feature"; _first = null; _dimension = feature == "fillet" || feature == "chamfer" ? 2 : 10;
            SyncDimensionEntry();
            _notice = ""; UpdateDraft(); Refresh();
        }
        private void PickExtrusionSketch()
        {
            if (_screen == "sketch" && _sketch != null) { Feature("extrude"); return; }
            OpenPicker("Profilo da estrudere", _context.Sketches.Select(sketch =>
                new PickerItem(sketch.Name, () => { _sketch = null; _existingSketch = sketch.Name; Feature("extrude"); })));
        }
        private JObject FeatureOperation()
        {
            switch (_feature)
            {
                case "extrude": return DesignOperations.Extrude(_sketch?.Name ?? _existingSketch, _dimension, _operation,
                    _symmetric ? "symmetric" : _negative ? "negative" : "positive");
                case "hole": return DesignOperations.Hole(_face, _facePoint.X, _facePoint.Y, _facePoint.Z, _diameter, _through ? (double?)null : _dimension);
                case "fillet": return DesignOperations.Fillet(_edges.ToArray(), _dimension);
                case "chamfer": return DesignOperations.Chamfer(_edges.ToArray(), _dimension);
                default: return null;
            }
        }
        private void UpdateDraft()
        {
            if (_session?.CanEdit != true) return;
            try
            {
                _geometry.ShowDraft(_sketch);
                if (_feature == "fillet" || _feature == "chamfer") _geometry.ShowEdges(_context.Edges.Where(e => _edges.Contains(e.Id)));
                var feature = FeatureOperation();
                if (_sketch != null) _session.SetDraft(_sketch.Operations(feature));
                else if (feature != null) _session.SetDraft(new JArray(feature));
                UpdateHandle();
            }
            catch (Exception ex) { SetNotice(ex.Message); UpdateHandle(); _session.RejectDraft(ex.Message); }
        }
        private async void Preview()
        {
            try { UpdateDraft(); if (_session.Status != DesignStatus.Error) await _session.PreviewAsync(); }
            catch (Exception ex) { SetNotice(ex.Message); Refresh(); }
        }
        private async void Apply()
        {
            var session=_session;
            await RunMutation(session,()=>session.ApplyAsync());
            if (this != null && session == _session && session.LastCommit != null && !session.CommitOutcomeUnknown)
            { CommitBar.MarkApplied(Time.unscaledTimeAsDouble); Haptics.Play(HapticPulse.Success); }
        }
        private async void ApplyHistory(bool redo)
        {
            var history = _history;
            var session=_session;
            await RunMutation(session,()=>session.ApplyHistoryAsync(history,redo));
        }
        private async Task RunMutation(DesignSession owner,Func<Task> mutation)
        {
            if (owner==null || _pendingMutations>0) return;
            _pendingMutations++;
            Refresh();
            try
            {
                await mutation();
            }
            catch (Exception ex) { if (owner==_session) SetNotice(ex.Message); }
            finally { _pendingMutations--; }
            if (this==null) return;
            if (owner==_session && !owner.CommitOutcomeUnknown && owner.Status==DesignStatus.RefreshRequired)
            {
                try { await RefreshAfterApply(false); }
                catch(Exception ex) { if(owner==_session) SetNotice(ex.Message); }
            }
            Refresh();
        }
        private async Task RefreshAfterApply(bool reviewed)
        {
            if (!_online || _backend == null || _pendingMutations>0) return;
            var backend=_backend; var session=_session; int generation=_generation;
            var state = await backend.GetDocumentStateAsync(_reads.Token);
            if (this==null || backend!=_backend || session!=_session || generation!=_generation) return;
            session.AcknowledgeRefresh(state, reviewed); _reviewAfterRebind=false; _state = state; ResetDraft(); LoadContext();
            SetNotice("Documento aggiornato. Attendo il modello da Inventor.");
        }
        private async void ReviewUnknown()
        {
            try { await RefreshAfterApply(true); }
            catch (Exception ex) { SetNotice(ex.Message); }
            Refresh();
        }
        private async void PreviewParameter() { try { await _session.PreviewAsync(); } catch (Exception ex) { SetNotice(ex.Message); Refresh(); } }
        private void CancelDraft() { _session.Cancel(); ResetDraft(); Refresh(); }

        // ---------------------------------------------------------------- voice surface

        public const string FieldDimension = "design.dimension";
        private const double MinDimensionMm = 0.001, MaxDimensionMm = 10000;

        private bool InDraftScreen => _screen == "sketch" || _screen == "feature" || _screen == "parameter";
        /// <summary>Progettazione no longer has a floating panel: voice labels come from the declared actions.</summary>
        public HomePanel VoicePanel => null;

        private bool DimensionFieldAvailable => Active && _screen == "feature" && _session?.CanEdit == true && _context != null && !_busy
            && _pendingMutations == 0 && _session.Status != DesignStatus.Committing && _session.Status != DesignStatus.Previewing
            && DimensionApplies;

        /// <summary>The numeric field waiting for the keypad, else the dimension chip of the feature, or null when none is on screen.</summary>
        public DictationField ArmedField => _ask != null ? new DictationField(_ask.Id, _ask.Unit, _ask.Min, _ask.Max)
            : DimensionFieldAvailable ? new DictationField(FieldDimension, QuantityUnit.Millimeters, MinDimensionMm, MaxDimensionMm) : null;

        /// <summary>
        /// Dictation: validated by the entry like the keypad. With the keypad open it confirms that value; otherwise it sets the
        /// chip (draft updated, preview invalidated, Apply blocked until a new preview).
        /// </summary>
        public bool SetArmedField(string fieldId, double value)
        {
            if (_ask != null && fieldId == _ask.Id) return _ask.CommitValue(value, out _);
            if (fieldId != FieldDimension || !DimensionFieldAvailable) return false;
            if (!DimEntry.SetValue(value, out _)) return false;
            if (_dimension != value)
            {
                _dimension = value;
                try { _notice = ""; UpdateDraft(); } catch (Exception ex) { SetNotice(ex.Message); }
            }
            Refresh();
            return true;
        }

        private static string Format(double value) => value.ToString("0.###", CultureInfo.GetCultureInfo("it-IT"));

        /// <summary>Numeric prompts, one value at a time through the palette keypad; <paramref name="done"/> runs after the last one.</summary>
        private void Ask(string title, string[] labels, double[] values, Action<double[]> done, QuantityUnit unit = QuantityUnit.Millimeters)
        {
            int generation = _generation, index = 0;
            var results = (double[])values.Clone();
            void Next()
            {
                var entry = new NumericEntry("design.ask." + index, unit, results[index], -100000, 100000);
                _ask = entry;
                entry.Committed += () =>
                {
                    if (generation != _generation || _ask != entry) return;
                    results[index++] = entry.Value;
                    if (index < results.Length) { Next(); return; }
                    _ask = null;
                    try { _notice = ""; done(results); } catch (Exception ex) { SetNotice(ex.Message); }
                    Refresh();
                };
                if (_shell != null) _shell.Palette.ShowKeypad(entry, title + " • " + labels[index]); else entry.BeginEdit();
            }
            Next();
        }
        private void NumericShape()
        {
            if (_sketch == null) return;
            if (_shape == SketchShape.Circle)
                Ask("Cerchio", new[] { "Centro X", "Centro Y", "Raggio mm" }, new[] { 0.0,0.0,10.0 }, n => AddShape(new SketchElement(_shape,new CadPoint(n[0],n[1]),default,n[2])));
            else Ask(_shape == SketchShape.Line ? "Linea" : "Rettangolo", new[] { "X1","Y1","X2","Y2" }, new[] { 0.0,0.0,20.0,20.0 },
                n => AddShape(new SketchElement(_shape,new CadPoint(n[0],n[1]),new CadPoint(n[2],n[3]))));
        }
        private void DimensionElement(int index, CadPoint? textPoint)
        {
            var item = _sketch.Elements[index];
            if (item.Shape == SketchShape.Circle) Ask("Quota raggio", new[] { "mm" }, new[] { item.Radius }, n =>
            { _sketch.Replace(index,new SketchElement(item.Shape,item.A,item.B,n[0],true,textPoint)); UpdateDraft(); });
            else if (item.Shape == SketchShape.Rectangle) Ask("Quote rettangolo", new[] { "Larghezza mm","Altezza mm" }, new[] { item.B.X-item.A.X,item.B.Y-item.A.Y }, n =>
            { _sketch.Replace(index,new SketchElement(item.Shape,item.A,item.A+new CadPoint(n[0],n[1]),dimensioned:true,dimensionText:textPoint)); UpdateDraft(); });
            else Ask("Quota lunghezza", new[] { "mm" }, new[] { (item.B-item.A).Length }, n =>
            { if (n[0] <= 0) throw new ArgumentException("Lunghezza positiva richiesta."); _sketch.Replace(index,new SketchElement(item.Shape,item.A,item.A+(item.B-item.A)*(n[0]/(item.B-item.A).Length),dimensioned:true,dimensionText:textPoint)); UpdateDraft(); });
        }
        private SketchSnapshot CurrentSketchSnapshot => _session?.Status==DesignStatus.PreviewReady
            ? _session.Preview?.Sketches.FirstOrDefault(s=>s.Name==_sketch?.Name) : null;
        private void ShowConstraints(IEnumerable<string> constraints)
        {
            _dimensionStep=0;
            var rows=constraints.Where(s=>!string.IsNullOrEmpty(s)).GroupBy(ConstraintLabel)
                .Select(g=>g.Key+" × "+g.Count()).OrderBy(s=>s).ToArray();
            SetNotice(rows.Length==0 ? "Nessun vincolo geometrico restituito." : "Vincoli Inventor:\n"+string.Join("\n",rows));
            Refresh();
        }
        private static string ConstraintLabel(string type)
        {
            switch(type)
            {
                case "kCoincidentConstraintObject": return "Coincidente";
                case "kHorizontalConstraintObject": return "Orizzontale";
                case "kVerticalConstraintObject": return "Verticale";
                case "kParallelConstraintObject": return "Parallelo";
                case "kPerpendicularConstraintObject": return "Perpendicolare";
                case "kTangentSketchConstraintObject": return "Tangente";
                case "kEqualLengthConstraintObject": return "Uguale lunghezza";
                case "kEqualRadiusConstraintObject": return "Uguale raggio";
                case "kRadiusDimConstraintObject": return "Quota raggio";
                case "kTwoPointDistanceDimConstraintObject": return "Quota distanza";
                default: return type;
            }
        }
        private void AddShape(SketchElement element) { _sketch.Add(element); _first = null; _snapLocked = false; UpdateDraft(); Refresh(); }

        private SketchFrame ExtrusionFrame => _sketch?.Frame ?? _context?.Sketches.FirstOrDefault(s => s.Reference == _existingSketch)?.Frame;
        private DesignEdge DimensionEdge => _context?.Edges.FirstOrDefault(e => _edges.Contains(e.Id));
        private bool HasDimensionTarget => _feature == "extrude" ? ExtrusionFrame != null
            : _feature == "hole" ? _context?.Faces.Any(f => f.Id == _face) == true
            : (_feature == "fillet" || _feature == "chamfer") && DimensionEdge != null;
        private static CadPoint LengthMidpoint(IReadOnlyList<CadPoint> points)
        {
            if (points == null || points.Count == 0) return default;
            double length = 0;
            for (int i=1;i<points.Count;i++) length += (points[i]-points[i-1]).Length;
            double remaining = length*0.5;
            for (int i=1;i<points.Count;i++)
            {
                var segment = points[i]-points[i-1];
                if (segment.Length>0 && remaining<=segment.Length)
                    return points[i-1]+segment*(remaining/segment.Length);
                remaining -= segment.Length;
            }
            return points[0];
        }
        private CadPoint DimensionOrigin
        {
            get
            {
                if (_feature == "extrude") return ExtrusionFrame?.OriginMm ?? default;
                if (_feature != "fillet" && _feature != "chamfer") return _facePoint;
                return LengthMidpoint(DimensionEdge?.PointsMm);
            }
        }
        private CadPoint DimensionAxis => _feature == "extrude"
            ? (ExtrusionFrame?.Normal ?? new CadPoint(0,0,1)) * (_negative && !_symmetric ? -1 : 1)
            : _feature == "hole" ? (_context?.Faces.FirstOrDefault(f => f.Id == _face)?.Normal ?? new CadPoint(0,0,1)) * -1
            : new CadPoint(0,1,0);
        private bool SymmetricHandle => _feature=="extrude" && _symmetric;
        private CadPoint DimensionStart => SymmetricHandle ? DimensionOrigin-DimensionAxis*(_dimension*0.5) : DimensionOrigin;
        private CadPoint DimensionEnd => DimensionOrigin+DimensionAxis*(_dimension*(SymmetricHandle ? 0.5 : 1));
        private double DragDimension(CadPoint position) => Math.Min(MaxDimensionMm,Math.Max(0.01,
            _dragValue+(position-_dragStart).Dot(DimensionAxis)*(SymmetricHandle ? 2*_dragSide : 1)*_dragFactor));
        private void UpdateHandle()
        {
            if (!HasDimensionTarget) { _handle.positionCount = 0; UpdateChip(); return; }
            _handle.positionCount = 2; _handle.SetPosition(0,CadCoordinates.ToLocal(DimensionStart));
            _handle.SetPosition(1,CadCoordinates.ToLocal(DimensionEnd));
            UpdateChip();
        }

        // Pen: the tip of the right controller when it is within 2 cm of the sheet, the ray otherwise (SketchSheetView).
        private bool TryPenPoint(Ray ray, out CadPoint point)
        {
            if (_sheet != null && _sheet.State == SketchSheetState.Sheet)
            {
                point = default;
                if (!_sheet.ProjectPen(_ray.Origin.position, ray, _sketch.Frame, out var mm)) return false;
                point = new CadPoint(mm.x, mm.y);
                return true;
            }
            return CadCoordinates.SketchRay(_view.transform, _sketch.Frame, ray, out point);
        }

        // ---------------------------------------------------------------- controller input (XrInput events + per-frame hover)

        private bool InputTracked => _input == null || _input.PenTracked;

        /// <summary>Runners with a synthetic pen set this so the real controller's UI hit cannot swallow the gesture (same hook as Assieme and Lamiera).</summary>
        public Func<bool> UiHitOverride { get; set; }

        /// <summary>The pen ray, or false when the workspace cannot take pen input right now.</summary>
        private bool TryPenRay(out Ray ray, out bool overUi)
        {
            ray = default; overUi = false;
            if (!Active || !_visible || _ray == null || _ray.Origin == null || !InputTracked) return false;
            ray = new Ray(_ray.Origin.position, _ray.Origin.forward);
            var ui = EventSystem.current?.currentInputModule as ControllerUiInputModule;
            overUi = UiHitOverride != null ? UiHitOverride() : ui != null && ui.CurrentHit.isValid;
            return true;
        }

        private bool OverHandle(Ray ray)
        {
            if (_session?.CanEdit != true || !HasDimensionTarget) return false;
            var handle = new DesignEdge("handle","line",new[] { DimensionStart,DimensionEnd });
            return CadCoordinates.PickEdge(_view.transform,new[] { handle },ray,0.025f) != null;
        }

        /// <summary>Trigger held on the dimension handle captures it (M5-08): from here on only the local draft changes.</summary>
        private bool TryBeginHandleDrag(Ray ray)
        {
            if (_dimensionDrag || !OverHandle(ray)) return false;
            var picked = CadCoordinates.ClosestPointOnSegment(ray,_view.transform.TransformPoint(CadCoordinates.ToLocal(DimensionStart)),
                _view.transform.TransformPoint(CadCoordinates.ToLocal(DimensionEnd)));
            _dragSide = (CadCoordinates.FromWorld(_view.transform,picked)-DimensionOrigin).Dot(DimensionAxis) < 0 ? -1 : 1;
            _dimensionDrag = true; _grab = null;
            _dragStart = CadCoordinates.FromWorld(_view.transform,_ray.Origin.position); _dragValue = _dimension;
            _dragFactor = Precision ? PrecisionFactor : 1;
            _dragDraftAt = Time.unscaledTime;
            UpdateDraft();
            return true;
        }

        private bool Precision => _input != null && _input.Precision;

        private static bool Finite(CadPoint p) => !(double.IsNaN(p.X) || double.IsNaN(p.Y) || double.IsNaN(p.Z)
            || double.IsInfinity(p.X) || double.IsInfinity(p.Y) || double.IsInfinity(p.Z));

        private void DragStep()
        {
            var position = CadCoordinates.FromWorld(_view.transform,_ray.Origin.position);
            if (!Finite(position)) return;
            double factor = Precision ? PrecisionFactor : 1;
            if (factor != _dragFactor) { _dragFactor = factor; _dragStart = position; _dragValue = _dimension; }   // re-anchor: no jump
            double value = DragDimension(position);
            if (value == _dimension) return;
            _dimension = value;
            SyncDimensionEntry();
            UpdateHandle();
            // The session draft follows the capture (never the CAD); throttled because every change rebuilds the shell actions.
            if (Time.unscaledTime - _dragDraftAt >= DraftThrottleSeconds) { _dragDraftAt = Time.unscaledTime; UpdateDraft(); }
        }

        /// <summary>
        /// Ends the handle capture. <paramref name="preview"/>: trigger released, the draft is final and gets previewed.
        /// <paramref name="error"/>: tracking lost, the draft keeps the last valid value and the controller buzzes.
        /// </summary>
        private void EndCapture(bool preview, bool error)
        {
            if (!_dimensionDrag) return;
            _dimensionDrag = false; _dragFactor = 1;
            if (error) Haptics.Play(HapticPulse.Error);
            if (_session?.CanEdit != true || !Active) return;
            if (preview) { UpdateDraft(); Preview(); }
            else UpdateDraft();
        }

        private void EndViewGrabs() { _grab = null; _gripHeld = false; _twoHandActive = false; }

        private void OnPenReleased() => EndCapture(true, false);

        private void OnTrackingLost()
        {
            // Raised before the release of a held trigger: the capture is closed here, without preview.
            if (!Active) return;
            EndCapture(false, true);
            EndViewGrabs(); _first = null; _snapLocked = false; _hoverKey = 0;
            _geometry?.Cursor(null,default,null,_shape);
            if (_cursorCanvas != null) _cursorCanvas.gameObject.SetActive(false);
        }

        private void OnGrabStarted()
        {
            if (!TryPenRay(out var ray, out bool overUi)) return;
            _gripHeld = true;
            if (_dimensionDrag || _twoHandActive || overUi || !CadRaycaster.TryPick(ray,20,out _,out _,out _)) return;
            SnapPoses();
            _grab = _view.transform;
            _grabPosition = _ray.Origin.InverseTransformPoint(_grab.position);
            _grabRotation = Quaternion.Inverse(_ray.Origin.rotation)*_grab.rotation;
        }

        private void OnGrabEnded() { _grab = null; _gripHeld = false; }

        /// <summary>The scene root is about to be moved by hand: finish any tween so it does not fight the gesture.</summary>
        private void SnapPoses() { _sheet?.Snap(); _bench?.Snap(); }

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
            _dimensionDrag = false; _grab = null;
            SnapPoses();
            var root = _view.transform; var right = _ray.Origin.position;
            _twoHandVector = right-left; _twoHandMid = (right+left)*0.5f;
            _twoHandRootPosition = root.position; _twoHandRootRotation = root.rotation; _twoHandRootScale = root.localScale.x;
            _twoHandActive = _twoHandVector.sqrMagnitude > 1e-6f;
        }

        /// <summary>Two hands: rotate, scale and move the scene root. View only; the CAD data never changes.</summary>
        private void TwoHandStep()
        {
            if (_ray?.Origin == null || !LeftHandPose(out var left)) return;
            var right = _ray.Origin.position; var vector = right-left;
            if (vector.sqrMagnitude < 1e-6f) return;
            float scale = Mathf.Clamp(_twoHandRootScale*vector.magnitude/_twoHandVector.magnitude,(float)WorkbenchLayout.MinScale,(float)WorkbenchLayout.MaxScale);
            var turn = Quaternion.FromToRotation(_twoHandVector,vector);
            var root = _view.transform;
            root.localScale = Vector3.one*scale;
            root.rotation = turn*_twoHandRootRotation;
            root.position = (right+left)*0.5f+turn*((_twoHandRootPosition-_twoHandMid)*(scale/_twoHandRootScale));
        }

        /// <summary>Left stick up/down: scales the sheet (or the model) about its centre. View only, clamped to the layout limits.</summary>
        private void OnZoom(float axis)
        {
            if (!Active || _view == null || _dimensionDrag || _twoHandActive) return;
            ZoomView(axis, Mathf.Clamp(Time.unscaledDeltaTime,1f/120f,1f/20f));
        }

        /// <summary>Scales the scene root about the sheet centre (sketch origin) or the model, never outside [MinScale, MaxScale].</summary>
        public void ZoomView(float axis, float seconds)
        {
            var root = _view.transform;
            SnapPoses();
            float current = root.localScale.x;
            float target = Mathf.Clamp(current*Mathf.Exp(axis*ZoomRatePerSecond*seconds),(float)WorkbenchLayout.MinScale,(float)WorkbenchLayout.MaxScale);
            if (Mathf.Approximately(target,current)) return;
            var pivot = root.position;
            if (_sheet != null && _sheet.State == SketchSheetState.Sheet && _sketch?.Frame != null)
                pivot = root.TransformPoint(CadCoordinates.ToLocal(_sketch.Frame.OriginMm));
            root.localScale = Vector3.one*target;
            root.position = pivot+(root.position-pivot)*(target/current);
        }

        /// <summary>Semantic keys routed by <see cref="InputDispatcher"/> (already gated by <see cref="InputMap"/>): Y, A and the right stick.</summary>
        private void OnInvoked(InputAction action, int arg)
        {
            if (!Active) return;
            switch (action)
            {
                case InputAction.Fit: FitView(); break;
                case InputAction.SnapToggle: OnSnapToggled(); break;
                case InputAction.StepChange: OnStepDelta(arg); break;
                case InputAction.StepSize: OnStepSizeDelta(arg); break;
                case InputAction.OpenKeypad:
                    if (_screen == "feature" && DimensionApplies && _ask == null) OpenDimensionKeypad();
                    break;
                case InputAction.KeypadOk:
                    if (_ask != null && _ask.Editing) _ask.Commit(out _);
                    break;
            }
        }

        private void OnSnapToggled()
        {
            if (!Active || _screen != "sketch" || _sketch?.Frame == null || _dimensionStep != 0 || _session?.CanEdit != true) return;
            _snapLocked = !_snapLocked;
            Haptics.Play(HapticPulse.Tick);
        }

        private void OnStepDelta(int direction) { if (NudgeDimension(direction)) Haptics.Play(HapticPulse.Tick); }
        private void OnStepSizeDelta(int direction)
        {
            if (!Active || _screen != "feature" || !DimensionApplies) return;
            CycleDimensionStep(direction); Haptics.Play(HapticPulse.Tick);
        }

        private void OnPenPressed()
        {
            if (!TryPenRay(out var ray, out bool overUi)) return;
            if (PickSuppressed?.Invoke() == true) return;
            if (_session?.CanEdit == true && !overUi && !GripDown && TryBeginHandleDrag(ray)) return;
            if (overUi || _ask != null || _session?.CanEdit != true || _gripHeld || _dimensionDrag) return;
            if (_screen == "sketch" && _sketch?.Frame != null) { SketchPress(ray); return; }
            if (_context == null) return;
            if (_feature == "fillet" || _feature == "chamfer")
            {
                string edge = CadCoordinates.PickEdge(_view.transform,_context.Edges,ray,requireVisible:true);
                if (edge != null) { if (!_edges.Add(edge)) _edges.Remove(edge); UpdateDraft(); Refresh(); }
                return;
            }
            // Idle: an edge or a face opens the ring; the empty space closes it.
            if (_feature == null && _sketch == null)
            {
                string edge = CadCoordinates.PickEdge(_view.transform,_context.Edges,ray,requireVisible:true);
                if (edge != null) { SelectEdge(edge); return; }
            }
            if (CadRaycaster.TryPick(ray,20,out var body,out int triangle,out var hit))
            {
                _ringEdge = null;
                SelectPlanarFace(body, triangle, hit);
                if (_feature == null && _face != null) ShowRing(UiSelectionKind.PlanarFace, hit);
            }
            else HideRing();
        }

        private void SketchPress(Ray ray)
        {
            if (!TryPenPoint(ray,out var point)) return;
            if (_dimensionStep != 0)
            {
                if (_dimensionStep==3)
                {
                    var selected=CurrentSketchSnapshot?.Pick(point,2);
                    if (selected==null) { SetNotice("Nessuna geometria dell’anteprima vicina al raggio."); Haptics.Play(HapticPulse.Error); Refresh(); }
                    else ShowConstraints(selected.Constraints);
                    return;
                }
                if (_dimensionStep == 1)
                {
                    _dimensionIndex=_sketch.Pick(point,2);
                    if (_dimensionIndex < 0) { SetNotice("Nessuna geometria vicina al raggio."); Haptics.Play(HapticPulse.Error); Refresh(); return; }
                    _dimensionStep=2; _geometry.ShowDraft(_sketch,_dimensionIndex);
                    SetNotice("Quota: indica dove posizionare il testo sul piano dello schizzo."); Refresh();
                }
                else { _dimensionStep=0; DimensionElement(_dimensionIndex,point); }
                return;
            }
            if (!_snapLocked) _candidate = _sketch.Snap(point,_first,1.0);
            if (!_first.HasValue) { _first=_candidate.Point; _snapLocked=false; Refresh(); return; }
            try { AddShape(new SketchElement(_shape,_first.Value,_candidate.Point,(_candidate.Point-_first.Value).Length)); }
            catch (Exception ex) { SetNotice(ex.Message); Haptics.Play(HapticPulse.Error); Refresh(); }
        }

        private void Update()
        {
            if (!Active) return;
            CommitBar.Tick(Time.unscaledTimeAsDouble);
            UpdateBar();
            DropClosedKeypad();
            if (!_visible || _ray == null || _ray.Origin == null) return;
            if (!InputTracked) { _hoverKey = 0; _geometry.Cursor(null,default,null,_shape); return; }
            if (_dimensionDrag) { DragStep(); return; }
            if (_twoHandActive) { TwoHandStep(); return; }
            if (_grab != null) { _grab.SetPositionAndRotation(_ray.Origin.TransformPoint(_grabPosition),_ray.Origin.rotation*_grabRotation); return; }
            if (!TryPenRay(out var ray, out bool overUi)) return;
            HoverFeedback(ray, overUi);
            if (overUi || _ask != null || _session?.CanEdit != true || _gripHeld) return;
            if (_screen != "sketch" || _sketch?.Frame == null) return;
            if (!TryPenPoint(ray,out var point)) return;
            if (_dimensionStep != 0) { _geometry.Cursor(_sketch.Frame,point,null,_shape); return; }
            if (!_snapLocked) _candidate = _sketch.Snap(point,_first,1.0);
            string snapKey = _candidate.Kind.Length == 0 ? "" : _candidate.Kind+_candidate.Point.X.ToString("R",CultureInfo.InvariantCulture)+"/"+_candidate.Point.Y.ToString("R",CultureInfo.InvariantCulture);
            if (snapKey != _snapKey) { if (snapKey.Length > 0) Haptics.Play(HapticPulse.Tick); _snapKey = snapKey; }
            _geometry.Cursor(_sketch.Frame,_candidate.Point,_first,_shape);
            _cursorCanvas.gameObject.SetActive(true);
            _cursorCanvas.transform.SetPositionAndRotation(_view.transform.TransformPoint(CadCoordinates.ToLocal(_sketch.Frame.ToModel(_candidate.Point)))+Vector3.up*0.035f,
                Quaternion.LookRotation(_head.forward));
            _cursorText.text = Format(_candidate.Point.X)+", "+Format(_candidate.Point.Y)+" mm • "+_candidate.Kind+(_snapLocked ? " • bloccato" : "");
        }

        /// <summary>Best effort: a short pulse when the ray enters an interactive target (chip, ring, palette, dimension handle).</summary>
        private void HoverFeedback(Ray ray, bool overUi)
        {
            int key = overUi ? 1 : OverHandle(ray) ? 2 : 0;
            if (key != _hoverKey && key != 0) Haptics.Play(HapticPulse.Hover);
            _hoverKey = key;
        }

        private void SelectEdge(string edgeId)
        {
            var edge = _context.Edges.FirstOrDefault(e => e.Id == edgeId);
            if (edge == null) return;
            _face = null; _faceSelection = null; _selection.Clear();
            _geometry.ShowEdges(new[] { edge });
            SetNotice("Spigolo selezionato.");
            var mid = LengthMidpoint(edge.PointsMm);
            ShowRing(UiSelectionKind.Edge, _view.transform.TransformPoint(CadCoordinates.ToLocal(mid)));
            _ringEdge = edgeId;
        }
        private void SelectPlanarFace(CadBody body, int triangle, Vector3 hit)
        {
            if (_context == null || _state == null || _context.State.DocumentId != _state.DocumentId
                || _context.State.Revision != _state.Revision || _sceneState?.DocumentId != _state.DocumentId
                || _sceneState?.VisualRevision != _state.VisualRevision || body.Instance.DefinitionId != _state.DocumentId) return;
            var range = body.Primitive.FaceMap.FaceAtTriangle(triangle);
            // Inventor reference contexts may differ for the same face. The mesh and Design
            // context enumerate the same B-rep bodies/faces at this document revision.
            var face = range == null ? null : _context.Faces.FirstOrDefault(f =>
                f.BodyIndex > 0 && f.FaceOrdinal > 0 && f.BodyIndex == body.Primitive.BodyIndex && f.FaceOrdinal == range.Ordinal);
            if (face == null && range != null)
                face = _context.Faces.FirstOrDefault(f => f.Id == range.FaceId);
            if (face == null)
            {
                _face = null; _faceSelection = null; _selection.Clear();
                SetNotice("Faccia non piana o riferimenti non aggiornati. Prova Aggiorna riferimenti.");
                if (_feature == "hole") UpdateDraft();
                Refresh(); return;
            }
            _face = face.Id;
            _facePoint = face.Project(CadCoordinates.FromWorld(_view.transform, hit));
            _faceSelection = new Selection(InventorXrSo.Core.Selection.SelectionKind.Face, body.Instance.OccurrenceId, range.FaceId, face.Id);
            _selection.Show(_faceSelection);
            SetNotice("Faccia piana selezionata.");
            if (_feature == "hole") UpdateDraft();
            Refresh();
        }
        private void OnDestroy()
        {
            DetachInput();
            _reads.Cancel(); _reads.Dispose();
            if (_session != null) { _session.Changed-=SessionChanged; _session.Dispose(); }
            DestroyCanvas(_cursorCanvas != null ? _cursorCanvas.gameObject : null);
            DestroyCanvas(_chip != null ? _chip.Canvas.gameObject : null);
            DestroyCanvas(_ring != null ? _ring.Canvas.gameObject : null);
        }
        private static void DestroyCanvas(GameObject go)
        {
            if (go == null) return;
            if (Application.isPlaying) Destroy(go); else DestroyImmediate(go);
        }
    }
}
