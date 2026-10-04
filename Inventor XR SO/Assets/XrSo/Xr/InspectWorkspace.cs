using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Mcp;
using InventorXrSo.Core.Selection;
using InventorXrSo.Core.Session;
using InventorXrSo.Core.Ui;
using InventorXrSo.Core.Voice;
using InventorXrSo.Unity.Scene;
using InventorXrSo.Unity.Ui;
using InventorXrSo.Xr.Input;
using UnityEngine;
using UnityEngine.EventSystems;

namespace InventorXrSo.Xr
{
    /// <summary>
    /// Quest M2/M6 Ispeziona: local inspection state with explicit guarded backend reads. It is the default workspace: the action
    /// catalog falls back to it whenever no authoring workspace is active. No floating panel, wrist menu, breadcrumb or compact
    /// card: the actions are declared to the palette (<see cref="IActionProvider"/>, see InspectActions.cs) and context and
    /// properties go to the HUD. Grip is view only (model, section plane, two hands); picks come from the controller ray.
    /// </summary>
    [DefaultExecutionOrder(110)]
    public sealed partial class InspectWorkspace : MonoBehaviour
    {
        private const int PinLimit = 20;
        private readonly BrowserContext _context = new BrowserContext();
        private readonly CultureInfo _culture = CultureInfo.GetCultureInfo("it-IT");
        private CadSceneView _view;
        private SelectionVisuals _visuals;
        private ControllerRay _ray;
        private Transform _head, _leftHand;
        private EnvironmentModeController _environment;
        private UiShell _shell;
        private ActionCatalog _catalog;
        private XrInput _input;
        private SectionPlane _section;
        private MeasurementView _measure;
        private IInspectionBackend _backend;
        private SelectionService _selection;
        private CancellationTokenSource _requests = new CancellationTokenSource();
        private LoadedScene _scene;
        private DocumentState _documentState;
        private SceneNode _selected;
        private NumericEntry _ask;
        private bool _online, _visible, _busy, _showInfo;
        private int _generation;
        private string _notice = "";
        private InspectionInfo _info;
        private ModelScaleMode _scaleMode;
        private float _roomExtent = 2f;
        private Transform _grab;
        private Vector3 _grabPosition, _twoHandVector, _twoHandMid, _twoHandRootPosition;
        private Quaternion _grabRotation, _twoHandRootRotation;
        private float _twoHandRootScale, _lastPickTime;
        private bool _twoHandActive;
        private string _lastOccurrence;
        private IReadOnlyList<OpenDocument> _documents = new OpenDocument[0];

        public bool Measuring => _measure != null && _measure.Measuring;
        /// <summary>Inspect is the workspace in use: the scene is shown and no authoring workspace is open.</summary>
        public bool Active => _visible && !(OtherWorkspaceActive?.Invoke() ?? false);
        /// <summary>True while Progettazione, Lamiera or Assieme is open (set by the app controller).</summary>
        public Func<bool> OtherWorkspaceActive { get; set; }
        /// <summary>Notices, context and properties for the HUD.</summary>
        public event Action<string> HudMessage;
        public string Notice => _notice;
        /// <summary>The numeric entry currently waiting for the keypad, if any.</summary>
        public NumericEntry ActiveEntry => _ask;
        public SelectionService SelectionService => _selection;
        public string InfoText => InfoBody();

        public void Initialize(CadSceneView view, SelectionVisuals visuals, ControllerRay ray, Transform head, EnvironmentModeController environment)
        {
            _view = view; _visuals = visuals; _ray = ray; _head = head; _environment = environment;
            _section = new GameObject("Piano di sezione").AddComponent<SectionPlane>();
            _section.Initialize(view.transform, ray.LineMaterial);
            _view.Section = _section;
            _measure = new GameObject("Misure").AddComponent<MeasurementView>();
            _measure.transform.SetParent(view.transform, false);
            _measure.Initialize(ray.LineMaterial, head);
            _ray.PointPicked += OnPointPicked;
            InitializeVerify(ray.LineMaterial);
            SetVisible(false);
        }

        /// <summary>Shell and input; both are optional (headless tests). Without an input the workspace never receives grips.</summary>
        public void Attach(UiShell shell, XrInput input = null)
        {
            _shell = shell; _catalog = shell?.Catalog;
            DetachInput();
            _input = input;
            if (_input == null) return;
            _leftHand = _head != null && _head.parent != null
                ? _head.parent.Find("LeftHandAnchor/LeftControllerAnchor") ?? _head.parent.Find("LeftHandAnchor") : null;
            _input.PenGrabStarted += OnGrabStarted;
            _input.PenGrabEnded += OnGrabEnded;
            _input.TwoHandChanged += OnTwoHandChanged;
            _input.TrackingLost += OnTrackingLost;
            _input.Back += Back;
            _input.Fit += FitView;
            _input.Recenter += Recenter;
        }

        private void DetachInput()
        {
            if (_input == null) return;
            _input.PenGrabStarted -= OnGrabStarted;
            _input.PenGrabEnded -= OnGrabEnded;
            _input.TwoHandChanged -= OnTwoHandChanged;
            _input.TrackingLost -= OnTrackingLost;
            _input.Back -= Back;
            _input.Fit -= FitView;
            _input.Recenter -= Recenter;
            _input = null;
        }

        /// <summary>An authoring workspace opened or closed: the local tools stop (or resume) with it.</summary>
        public void OthersChanged()
        {
            if (!Active) { _measure?.Cancel(); _section?.SetActive(false); EndGrabs(); CloseKeypad(); ClosePicker(false); LeaveVerifyView(); }
            Refresh();
        }

        public void Bind(IInspectionBackend backend, SelectionService selection)
        {
            CancelRequests();
            _backend = backend; _selection = selection;
            BindVerify(backend);
            _online = false;
            _selected = null; _info = null; _documents = new OpenDocument[0];
            _measure.ClearAll(); _section.SetActive(false);
            _scene = null; _documentState = null; _context.SetGraph(null); EndGrabs();
            CloseKeypad(); ClosePicker(false);
            Refresh();
        }

        private void CancelRequests()
        {
            ++_generation; _requests.Cancel(); _requests.Dispose();
            _requests = new CancellationTokenSource(); _busy = false;
        }

        public void SetOnline(bool online)
        {
            if (_online == online) return;
            _online = online;
            CancelRequests();
            _info = null; ClosePicker(false);
            if (online) _notice = "";
            else SetNotice("Offline — strumenti locali disponibili. Dati CAD non aggiornati.");
            Refresh();
        }

        public void SetScene(LoadedScene scene)
        {
            bool changedDocument = _scene?.Graph.DocumentId != scene?.Graph.DocumentId;
            CancelRequests(); EndGrabs();
            _measure.ClearAll(); _section.SetActive(false); _selected = null; _info = null;
            CloseKeypad(); ClosePicker(false);
            _scene = scene; _context.SetGraph(scene?.Graph);
            _documentState = scene?.Graph.State;
            if (changedDocument) { _scaleMode = ModelScaleMode.OneToOne; _view.transform.localScale = Vector3.one; }
            else if (scene != null) InspectionGeometry.ApplyScale(_view.transform, ScenePlacement.LocalBounds(_view.transform), _scaleMode, _roomExtent);
            _section.ResetPlane(ScenePlacement.LocalBounds(_view.transform));
            if (changedDocument) ResetVerify(); else { _overlay?.Clear(); _focusSnapshot = null; }
            _notice = "";
            if (scene != null && TooBig()) SetNotice(OversizeNotice);
            Refresh();
        }

        private const string OversizeNotice = "Modello oltre lo spazio impostato: scala 1:1 mantenuta. Scegli Scala per adattarlo.";

        private bool TooBig()
        {
            var size = ScenePlacement.LocalBounds(_view.transform).size;
            return _scaleMode == ModelScaleMode.OneToOne && Mathf.Max(size.x, size.y, size.z) > _roomExtent;
        }

        public void SetDocumentState(DocumentState state)
        {
            if (_documentState?.DocumentId == state?.DocumentId && _documentState?.Revision == state?.Revision
                && _documentState?.VisualRevision == state?.VisualRevision) return;
            if (_documentState?.DocumentId != state?.DocumentId) ResetVerify();
            else _verifySession.OnDocumentState(state);
            CancelRequests();
            _documentState = state;
            _info = null; _selected = null; _selection?.ResetLocal();
            _measure.ClearAll(); _section.SetActive(false); _distanceA = null;
            ClosePicker(false);
            SetNotice("Documento aggiornato. Seleziona nuovamente o aggiorna le proprietà.");
            Refresh();
        }

        public void SetVisible(bool visible)
        {
            _visible = visible;
            if (!visible) { EndGrabs(); _measure?.Cancel(); CloseKeypad(); ClosePicker(false); LeaveVerifyView(); }
            else if (_scene != null && TooBig()) SetNotice(OversizeNotice);
            Refresh();
        }

        // ---------------------------------------------------------------- notices

        private void SetNotice(string text)
        {
            _notice = text ?? "";
            if (_notice.Length > 0) HudMessage?.Invoke(_notice);
        }

        /// <summary>Content or enablement changed: rebuild the declared actions and tell the catalog.</summary>
        private void Refresh()
        {
            _actions = null;
            _catalog?.NotifyChanged();
        }

        private string ContextPath() => string.Join(" › ", _context.Path.Select(node => node.Name));

        private string SelectionSummary()
        {
            if (_selected == null) return "Nessuna selezione.";
            return _selected.Name + "\n" + (_online ? (_info?.Material ?? "Materiale —") : "Offline • dati non aggiornati")
                + "  •  " + Format(_info?.MassKg, "kg") + "\nVincoli: " + (_info?.Constraints?.ToString() ?? "—");
        }

        private string Format(double? value, string unit) => value.HasValue ? value.Value.ToString("0.###", _culture) + " " + unit : "—";

        private void ReportError(Exception ex)
        {
            string detail = (ex is McpException error ? error.Code + ": " : "") + ex.Message;
            if (detail.Length > 400) detail = detail.Substring(0, 400) + "…";
            string summary = ex is McpToolException tool
                ? (tool.Code == "STALE_REVISION" || tool.Code == "DOCUMENT_CHANGED"
                    ? "Il documento è cambiato. Attendi l'aggiornamento e riprova."
                    : "Dati CAD non disponibili. Verifica il collegamento M2 sul PC.")
                : UiText.Error(ex);
            SetNotice(summary + "\n" + detail);
        }

        private string InfoBody() => (_selected?.Name ?? _context.Current?.Name ?? "Nessun documento")
            + "\nMateriale: " + (_info?.Material ?? "—") + "   Massa: " + Format(_info?.MassKg, "kg")
            + "\nVolume: " + Format(_info?.VolumeMm3, "mm³") + "   Area: " + Format(_info?.AreaMm2, "mm²")
            + "\nVincoli: " + (_info?.Constraints?.ToString() ?? "—") + "   DOF: "
            + (_info?.TranslationDof?.ToString() ?? "—") + " trasl. / " + (_info?.RotationDof?.ToString() ?? "—") + " rot.";

        // ---------------------------------------------------------------- selection and context

        public async Task PickAsync(CadBody body, int triangle, CancellationToken ct)
        {
            if (Measuring || !_online || _busy || _scene == null) return;
            var node = _context.SelectionTarget(body.Instance.OccurrenceId);
            if (node == null) { SetNotice("Oggetto fuori dal contesto. Usa Indietro nella scheda Vista."); Refresh(); return; }
            bool doubleAction = node.OccurrenceId == _lastOccurrence && Time.unscaledTime - _lastPickTime < 0.4f;
            _lastPickTime = Time.unscaledTime; _lastOccurrence = node.OccurrenceId;
            if (doubleAction && _context.Current != node) { Enter(node); return; }
            _selected = node; _info = null;
            bool partContext = _context.Current.DefinitionKind == "part";
            var face = body.Primitive.FaceMap.FaceAtTriangle(triangle)?.FaceId;
            if (partContext && face == null) { SetNotice(UiText.NoFaceHere); Refresh(); return; }
            int generation = _generation;
            _busy = true;
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _requests.Token))
            {
                try
                {
                    if (partContext) await _selection.SelectAsync("part", body.Instance.OccurrenceId, face, linked.Token);
                    else
                    {
                        await _selection.ClearAsync(linked.Token);
                        linked.Token.ThrowIfCancellationRequested();
                        await _selection.SelectAsync("assembly", node.OccurrenceId, null, linked.Token);
                        linked.Token.ThrowIfCancellationRequested();
                        ShowNodeSelection(node);
                    }
                    linked.Token.ThrowIfCancellationRequested();
                    if (generation != _generation) return;
                    SetNotice(SelectionSummary()); LoadInfo();
                }
                catch
                {
                    if (generation == _generation) { _selected = null; _info = null; }
                    throw;
                }
                finally { if (generation == _generation) { _busy = false; Refresh(); } }
            }
        }

        private void ShowNodeSelection(SceneNode node)
        {
            _visuals.ShowOccurrences(BrowserContext.Descendants(node).Where(n => n.DefinitionKind == "part").Select(n => n.OccurrenceId));
        }

        public void ClearSelection() { _selected = null; _info = null; Refresh(); }

        private void OnPointPicked(Vector3 point)
        {
            if (!Measuring) return;
            _measure.Pick(point);
            SetNotice(_measure.Measuring ? "Seleziona il secondo punto."
                : "Punto-punto (locale): ≈ " + Format(_measure.DistanceMm, "mm"));
            Refresh();
        }

        private void Enter(SceneNode node)
        {
            if (!_context.Enter(node)) return;
            CancelRequests();
            _selection?.ResetLocal(); _selected = null; _info = null; ClosePicker(false);
            SetNotice("Contesto: " + ContextPath());
            Refresh();
        }

        private void ContextBack()
        {
            CancelRequests();
            _context.Back(); _selection?.ResetLocal(); _selected = null; _info = null; ClosePicker(false);
            SetNotice("Contesto: " + ContextPath());
            Refresh();
        }

        private async void SelectNode(SceneNode node)
        {
            if (!_online || _busy || node.Suppressed) return;
            int generation = _generation;
            _busy = true;
            try
            {
                await _selection.ClearAsync(_requests.Token);
                if (generation != _generation) return;
                await _selection.SelectAsync("assembly", node.OccurrenceId, null, _requests.Token);
                if (generation != _generation) return;
                _selected = node; _info = null; ShowNodeSelection(node); SetNotice(SelectionSummary()); LoadInfo();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (generation == _generation) ReportError(ex); }
            finally { if (generation == _generation) { _busy = false; Refresh(); } }
        }

        private async void LoadInfo()
        {
            if (!_online || _backend == null || _scene == null || _documentState == null) return;
            int generation = _generation;
            var node = _selected ?? _context.Current;
            try
            {
                var info = await _backend.InspectAsync(_documentState, node?.OccurrenceId, _requests.Token);
                if (generation != _generation || node != (_selected ?? _context.Current)) return;
                _info = info;
                if (_showInfo || _selected != null) SetNotice(_showInfo ? InfoBody() : SelectionSummary());
                _showInfo = false;
                Refresh();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (generation == _generation && node == (_selected ?? _context.Current)) { _info = null; _showInfo = false; ReportError(ex); Refresh(); }
            }
        }

        private async void LoadDocuments()
        {
            if (!_online || _busy) return;
            int generation = _generation; _busy = true;
            try
            {
                var docs = await _backend.ListOpenAsync(_requests.Token);
                if (generation != _generation) return;
                _documents = docs.Where(d => d.Kind == "kPartDocumentObject" || d.Kind == "kAssemblyDocumentObject").ToArray();
                _busy = false;
                OpenDocumentsPicker();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (generation == _generation) ReportError(ex); }
            finally { if (generation == _generation) { _busy = false; Refresh(); } }
        }

        private async void Activate(OpenDocument doc)
        {
            if (!_online || _busy) return;
            int generation = _generation; _busy = true;
            try
            {
                await _backend.ActivateOpenAsync(doc.Id, _requests.Token);
                if (generation != _generation) return;
                SetNotice("Documento attivato. Attendo la scena da Inventor…");
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (generation == _generation) ReportError(ex); }
            finally { if (generation == _generation) { _busy = false; Refresh(); } }
        }

        // ---------------------------------------------------------------- tools

        private void BeginMeasure()
        {
            CloseKeypad();
            _measure.Begin();
            SetNotice("Punto-punto (locale): seleziona il primo punto sulla mesh.");
            Refresh();
        }

        private void PinMeasure()
        {
            SetNotice(_measure.Pin() ? "Misura mantenuta sul modello." : "Completa una misura (massimo " + PinLimit + " pin).");
            Refresh();
        }

        /// <summary>Opens the palette keypad on a fresh entry; the value is applied only when the keypad confirms it.</summary>
        private void AskNumber(string id, string label, QuantityUnit unit, double min, double max, double value, Action<double> apply)
        {
            var entry = new NumericEntry(id, unit, value, min, max);
            entry.Committed += () =>
            {
                if (_ask == entry) _ask = null;
                apply(entry.Value);
                SetNotice("");
                Refresh();
            };
            _ask = entry;
            if (_shell != null) _shell.Palette.ShowKeypad(entry, label); else entry.BeginEdit();
        }

        private void CloseKeypad()
        {
            if (_shell != null && _shell.Palette.KeypadVisible) _shell.Palette.HideKeypad();
            else _ask?.CancelEdit();
            _ask = null;
        }

        private void DropClosedKeypad()
        {
            if (_ask != null && _shell != null && !_shell.Palette.KeypadVisible) _ask = null;
        }

        private void SetScale(ModelScaleMode mode)
        {
            _scaleMode = mode;
            InspectionGeometry.ApplyScale(_view.transform, ScenePlacement.LocalBounds(_view.transform), mode, _roomExtent);
            SetNotice(""); Refresh();
        }

        /// <summary>Y short press: applies the current scale mode again.</summary>
        public void FitView()
        {
            if (!Active || _view == null) return;
            InspectionGeometry.ApplyScale(_view.transform, ScenePlacement.LocalBounds(_view.transform), _scaleMode, _roomExtent);
        }

        /// <summary>Brings the model in front of the user (also Y long press).</summary>
        public void Recenter()
        {
            if (!Active || _view == null || _head == null) return;
            var root = _view.transform;
            var bounds = ScenePlacement.LocalBounds(root);
            var scaled = new Bounds(bounds.center * root.localScale.x, bounds.size * root.localScale.x);
            var pose = ScenePlacement.InFront(scaled, _head.position, _head.forward);
            root.SetPositionAndRotation(pose.position, pose.rotation);
        }

        /// <summary>X: closes the keypad, else the open list, else the measurement, else goes up one context level. Local only.</summary>
        public void Back()
        {
            if (!Active) return;
            if (_ask != null || (_shell != null && _shell.Palette.KeypadVisible)) { CloseKeypad(); return; }
            if (_picker != null) { ClosePicker(); return; }
            if (ClearFocus()) { SetNotice(""); Refresh(); return; }
            if (_distanceA != null) { _distanceA = null; SetNotice("Distanza annullata."); Refresh(); return; }
            if (Measuring) { _measure.Cancel(); SetNotice("Misura annullata."); Refresh(); return; }
            if (_context.Path.Count > 1) ContextBack();
        }

        // ---------------------------------------------------------------- grip (view only)

        private bool TryPenRay(out Ray ray, out bool overUi)
        {
            ray = default; overUi = false;
            if (!Active || _ray == null || _ray.Origin == null || (_input != null && !_input.PenTracked)) return false;
            ray = new Ray(_ray.Origin.position, _ray.Origin.forward);
            var ui = EventSystem.current?.currentInputModule as ControllerUiInputModule;
            overUi = ui != null && ui.CurrentHit.isValid;
            return true;
        }

        private static bool Finite(Vector3 v) => !(float.IsNaN(v.x + v.y + v.z) || float.IsInfinity(v.x + v.y + v.z));

        /// <summary>Grip alone: the model (or the section plane under the ray) moves and turns with the hand. Never the CAD.</summary>
        private void OnGrabStarted()
        {
            if (_twoHandActive || !TryPenRay(out var ray, out bool overUi) || overUi) return;
            _grab = null;
            if (_section.Active && _section.HitHandle(ray, out _)) _grab = _section.transform;
            else if (CadRaycaster.TryPick(ray, 20, out _, out _, out _)) _grab = _view.transform;
            if (_grab == null) return;
            _grabPosition = _ray.Origin.InverseTransformPoint(_grab.position);
            _grabRotation = Quaternion.Inverse(_ray.Origin.rotation) * _grab.rotation;
        }

        private void OnGrabEnded() { _grab = null; Refresh(); }
        private void OnTrackingLost() { EndGrabs(); }
        private void EndGrabs() { _grab = null; _twoHandActive = false; }

        private void MoveGrab()
        {
            var position = _ray.Origin.TransformPoint(_grabPosition);
            if (!Finite(position)) return;
            var rotation = _ray.Origin.rotation * _grabRotation;
            if (_grab == _section.transform) _section.SetWorldPose(position, rotation);
            else _grab.SetPositionAndRotation(position, rotation);
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
            _grab = null;
            var root = _view.transform; var right = _ray.Origin.position;
            _twoHandVector = right - left; _twoHandMid = (right + left) * 0.5f;
            _twoHandRootPosition = root.position; _twoHandRootRotation = root.rotation; _twoHandRootScale = root.localScale.x;
            _twoHandActive = _twoHandVector.sqrMagnitude > 1e-6f;
        }

        /// <summary>Two hands: rotate, scale and move the model. View only.</summary>
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

        private void Update()
        {
            if (!_visible) return;
            DropClosedKeypad();
            TickVerify();
            if (!Active || _ray == null || _ray.Origin == null) return;
            if (_input != null && !_input.PenTracked) { EndGrabs(); return; }
            if (_twoHandActive) { TwoHandStep(); return; }
            if (_grab != null) MoveGrab();
        }

        private void OnDestroy()
        {
            DetachInput();
            DisposeVerify();
            _requests.Cancel(); _requests.Dispose();
            if (_ray != null) _ray.PointPicked -= OnPointPicked;
            if (_section != null) Release(_section.gameObject);
            if (_measure != null) Release(_measure.gameObject);
        }

        private static void Release(GameObject item)
        {
            item.SetActive(false);
            if (Application.isPlaying) Destroy(item); else DestroyImmediate(item);
        }
    }
}
