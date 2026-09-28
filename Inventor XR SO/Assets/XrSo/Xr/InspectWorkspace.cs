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
using InventorXrSo.Unity.Scene;
using InventorXrSo.Unity.Ui;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace InventorXrSo.Xr
{
    /// <summary>M2 controller: local inspection state and UI, with explicit guarded backend reads.</summary>
    [DefaultExecutionOrder(110)]
    public sealed class InspectWorkspace : MonoBehaviour
    {
        private readonly BrowserContext _context = new BrowserContext();
        private readonly CultureInfo _culture = CultureInfo.GetCultureInfo("it-IT");
        private CadSceneView _view;
        private SelectionVisuals _visuals;
        private ControllerRay _ray;
        private Transform _head, _hand;
        private EnvironmentModeController _environment;
        private HomePanel _panel;
        private Canvas _wrist, _breadcrumb, _compact;
        private Text _compactText, _modeText;
        private SectionPlane _section;
        private MeasurementView _measure;
        private IInspectionBackend _backend;
        private SelectionService _selection;
        private CancellationTokenSource _requests = new CancellationTokenSource();
        private LoadedScene _scene;
        private DocumentState _documentState;
        private SceneNode _selected;
        private bool _online, _visible, _pinned, _busy;
        private int _generation, _page;
        private string _screen = "tools", _notice = "";
        private string _errorDetails;
        private InspectionInfo _info;
        private ModelScaleMode _scaleMode;
        private float _roomExtent = 2f;
        private Transform _grab;
        private Vector3 _grabPosition;
        private Quaternion _grabRotation;
        private float _lastPickTime;
        private string _lastOccurrence;
        private Vector3 _compactAnchor;
        private IReadOnlyList<OpenDocument> _documents = new OpenDocument[0];
        public bool Measuring => _measure != null && _measure.Measuring;
        public event Action DesignRequested;
        public event Action AssemblyRequested;
        public void SetAssemblyActive(bool active) { SetDesignActive(active); if (active) _modeText.text = "ASSEMBLY"; }
        public event Action InspectionRequested;
        public bool DesignActive { get; private set; }
        public void SetDesignActive(bool active)
        {
            DesignActive = active;
            if (active) { _measure.Cancel(); _section.SetActive(false); _panel.gameObject.SetActive(false); _compact.gameObject.SetActive(false); }
            _modeText.text = active ? "DESIGN" : "INSPECT";
        }

        public void Initialize(CadSceneView view, SelectionVisuals visuals, ControllerRay ray, Transform head,
            Transform nonDominantHand, EnvironmentModeController environment)
        {
            _view = view; _visuals = visuals; _ray = ray; _head = head; _hand = nonDominantHand; _environment = environment;
            _panel = HomePanel.Create(transform);
            _panel.name = "Inspect Browser";
            ((RectTransform)_panel.transform).sizeDelta = new Vector2(820, 940);
            _panel.transform.localScale = Vector3.one * 0.0008f;
            XrUi.MakeInteractive(_panel.Canvas, head.GetComponent<Camera>());
            _panel.gameObject.SetActive(false);
            _wrist = UiFactory.WorldCanvas(_hand, "Polso Ispeziona", new Vector2(560, 150));
            _wrist.transform.localPosition = new Vector3(0, 0.09f, 0.03f);
            XrUi.MakeInteractive(_wrist, head.GetComponent<Camera>());
            var bg = UiFactory.Panel(_wrist.transform, "Background", UiFactory.Background); UiFactory.Stretch(bg);
            _modeText = UiFactory.Label(bg, "INSPECT", 24, FontStyle.Bold);
            Position(_modeText.rectTransform, new Vector2(0, 48), new Vector2(270, 40));
            var tools = UiFactory.Button(bg, "Ispeziona", UiFactory.Accent, 24, () => Open("tools"));
            Position((RectTransform)tools.transform, new Vector2(-210, -15), new Vector2(130, 65));
            var browser = UiFactory.Button(bg, "Browser", UiFactory.Key, 24, () => Open("browser"));
            Position((RectTransform)browser.transform, new Vector2(-70, -15), new Vector2(130, 65));
            var design = UiFactory.Button(bg, "Design", UiFactory.Key, 24, () => DesignRequested?.Invoke());
            Position((RectTransform)design.transform, new Vector2(70, -15), new Vector2(130,65));
            var assembly = UiFactory.Button(bg, "Assembly", UiFactory.Key, 22, () => AssemblyRequested?.Invoke());
            Position((RectTransform)assembly.transform, new Vector2(210, -15), new Vector2(130,65));

            _breadcrumb = UiFactory.WorldCanvas(transform, "Contesto", new Vector2(800, 50));
            _breadcrumb.transform.localScale = Vector3.one * 0.00065f;
            XrUi.MakeInteractive(_breadcrumb, head.GetComponent<Camera>());
            _compact = UiFactory.WorldCanvas(transform, "Selezione", new Vector2(400, 185));
            _compact.transform.localScale = Vector3.one * 0.0007f;
            XrUi.MakeInteractive(_compact, head.GetComponent<Camera>());
            var compactBg = UiFactory.Panel(_compact.transform, "Background", UiFactory.Background); UiFactory.Stretch(compactBg);
            _compactText = UiFactory.Label(compactBg, "", 24);
            Position(_compactText.rectTransform, new Vector2(0, 20), new Vector2(380, 115));
            var details = UiFactory.Button(compactBg, "Dettagli", UiFactory.Accent, 23, () => Open("details"));
            Position((RectTransform)details.transform, new Vector2(0, -62), new Vector2(370, 42));

            _section = new GameObject("Piano di sezione").AddComponent<SectionPlane>();
            _section.Initialize(view.transform, ray.LineMaterial);
            _view.Section = _section;
            _measure = new GameObject("Misure").AddComponent<MeasurementView>();
            _measure.transform.SetParent(view.transform, false);
            _measure.Initialize(ray.LineMaterial, head);
            _ray.PointPicked += OnPointPicked;
            SetVisible(false);
        }

        private static void Position(RectTransform rect, Vector2 at, Vector2 size)
        { rect.anchorMin = rect.anchorMax = new Vector2(0.5f,0.5f); rect.anchoredPosition = at; rect.sizeDelta = size; }

        public void Bind(IInspectionBackend backend, SelectionService selection)
        {
            CancelRequests();
            _backend = backend; _selection = selection;
            _online = false;
            _selected = null; _info = null; _documents = new OpenDocument[0];
            _measure.ClearAll(); _section.SetActive(false);
            _scene = null; _documentState = null; _context.SetGraph(null); _grab = null;
            _compact.gameObject.SetActive(false);
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
            _info = null;
            _notice = online ? "" : "Offline — strumenti locali disponibili. Dati CAD non aggiornati.";
            UpdateCompact(); Render();
        }
        public void SetScene(LoadedScene scene)
        {
            bool changedDocument = _scene?.Graph.DocumentId != scene?.Graph.DocumentId;
            CancelRequests(); _grab = null;
            _measure.ClearAll(); _section.SetActive(false); _selected = null; _info = null;
            _modeText.text = "INSPECT";
            if (_screen == "numeric") _screen = "tools";
            _scene = scene; _context.SetGraph(scene?.Graph); _page = 0;
            _documentState = scene?.Graph.State;
            _errorDetails = null;
            if (changedDocument) { _scaleMode = ModelScaleMode.OneToOne; _view.transform.localScale = Vector3.one; }
            else if (scene != null) InspectionGeometry.ApplyScale(_view.transform, ScenePlacement.LocalBounds(_view.transform), _scaleMode, _roomExtent);
            _section.ResetPlane(ScenePlacement.LocalBounds(_view.transform));
            _notice = "";
            if (scene != null && _scaleMode == ModelScaleMode.OneToOne && Mathf.Max(ScenePlacement.LocalBounds(_view.transform).size.x,
                ScenePlacement.LocalBounds(_view.transform).size.y, ScenePlacement.LocalBounds(_view.transform).size.z) > _roomExtent)
                _notice = "Modello oltre lo spazio impostato: scala 1:1 mantenuta. Scegli Scala per adattarlo.";
            UpdateCompact(); UpdateBreadcrumb(); Render();
        }
        public void SetDocumentState(DocumentState state)
        {
            if (_documentState?.DocumentId == state?.DocumentId && _documentState?.Revision == state?.Revision
                && _documentState?.VisualRevision == state?.VisualRevision) return;
            CancelRequests();
            _documentState = state;
            _info = null; _selected = null; _selection?.ResetLocal();
            _measure.ClearAll(); _section.SetActive(false); _modeText.text = "INSPECT";
            _notice = "Documento aggiornato. Seleziona nuovamente o aggiorna le proprietà.";
            UpdateCompact(); Render();
        }
        public void SetVisible(bool visible)
        {
            _visible = visible;
            _wrist.gameObject.SetActive(visible);
            _breadcrumb.gameObject.SetActive(visible && _scene != null);
            _compact.gameObject.SetActive(visible && _selected != null);
            if (!visible) { _panel.gameObject.SetActive(false); _grab = null; _measure.Cancel(); _modeText.text = "INSPECT"; }
            else if (_notice.StartsWith("Modello oltre")) Open("scale");
        }
        public void Open(string screen)
        {
            if (!_visible) return;
            if (DesignActive) InspectionRequested?.Invoke();
            _screen = screen; _page = 0;
            if (!_pinned) PlacePanel();
            _panel.gameObject.SetActive(true);
            Render();
        }
        private void PlacePanel()
        {
            var forward = Vector3.ProjectOnPlane(_head.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
            _panel.transform.SetPositionAndRotation(_head.position + forward * 0.9f + _head.right * 0.48f,
                Quaternion.LookRotation(forward));
        }

        public async Task PickAsync(CadBody body, int triangle, CancellationToken ct)
        {
            if (Measuring || !_online || _busy || _scene == null) return;
            var node = _context.SelectionTarget(body.Instance.OccurrenceId);
            if (node == null) { _notice = "Oggetto fuori dal contesto. Usa Indietro nel breadcrumb."; Render(); return; }
            bool doubleAction = node.OccurrenceId == _lastOccurrence && Time.unscaledTime - _lastPickTime < 0.4f;
            _lastPickTime = Time.unscaledTime; _lastOccurrence = node.OccurrenceId;
            if (doubleAction && _context.Current != node) { Enter(node); return; }
            _selected = node; _info = null;
            bool partContext = _context.Current.DefinitionKind == "part";
            var face = body.Primitive.FaceMap.FaceAtTriangle(triangle)?.FaceId;
            if (partContext && face == null) { _notice = UiText.NoFaceHere; Render(); return; }
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
                    UpdateCompact(); LoadInfo();
                }
                catch
                {
                    if (generation == _generation) { _selected = null; _info = null; UpdateCompact(); }
                    throw;
                }
                finally { if (generation == _generation) { _busy = false; Render(); } }
            }
        }
        private void ShowNodeSelection(SceneNode node)
        {
            _visuals.ShowOccurrences(BrowserContext.Descendants(node).Where(n => n.DefinitionKind == "part").Select(n => n.OccurrenceId));
        }
        public void ClearSelection() { _selected = null; _info = null; UpdateCompact(); Render(); }
        private void OnPointPicked(Vector3 point)
        {
            if (!Measuring) return;
            _measure.Pick(point);
            _notice = _measure.Measuring ? "Seleziona il secondo punto." : "Distanza tra punti sulla mesh (approssimata).";
            _modeText.text = _measure.Measuring ? "MISURA • punto 2" : "INSPECT";
            Render();
        }
        private void Enter(SceneNode node)
        {
            if (!_context.Enter(node)) return;
            CancelRequests();
            _selection?.ResetLocal(); _selected = null; _info = null; _page = 0;
            UpdateCompact(); UpdateBreadcrumb(); Render();
        }
        private void Back()
        {
            CancelRequests();
            _context.Back(); _selection?.ResetLocal(); _selected = null; _info = null; _page = 0;
            UpdateCompact(); UpdateBreadcrumb(); Render();
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
                _selected = node; _info = null; ShowNodeSelection(node); UpdateCompact(); LoadInfo();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (generation == _generation) ReportError(ex); }
            finally { if (generation == _generation) { _busy = false; Render(); } }
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
                _info = info; UpdateCompact(); Render();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (generation == _generation && node == (_selected ?? _context.Current)) { _info = null; ReportError(ex); UpdateCompact(); Render(); } }
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
                Open("documents");
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (generation == _generation) ReportError(ex); }
            finally { if (generation == _generation) { _busy = false; Render(); } }
        }
        private async void Activate(OpenDocument doc)
        {
            if (!_online || _busy) return;
            int generation = _generation; _busy = true;
            try
            {
                await _backend.ActivateOpenAsync(doc.Id, _requests.Token);
                if (generation != _generation) return;
                _notice = "Documento attivato. Attendo la scena da Inventor…";
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (generation == _generation) ReportError(ex); }
            finally { if (generation == _generation) { _busy = false; Render(); } }
        }

        private void UpdateCompact()
        {
            if (_compact == null) return;
            _compact.gameObject.SetActive(_visible && _selected != null);
            if (_selected == null) return;
            var instance = _view.Find(_selected.OccurrenceId);
            _compactAnchor = instance != null ? _view.transform.InverseTransformPoint(instance.transform.position)
                : ScenePlacement.LocalBounds(_view.transform).center;
            _compactText.text = _selected.Name + "\n" + (_online ? (_info?.Material ?? "Materiale —") : "Offline • dati non aggiornati")
                + "  •  " + Format(_info?.MassKg, "kg") + "\nVincoli: " + (_info?.Constraints?.ToString() ?? "—");
        }
        private string Format(double? value, string unit) => value.HasValue ? value.Value.ToString("0.###", _culture) + " " + unit : "—";
        private void ReportError(Exception ex)
        {
            _errorDetails = (ex is McpException error ? error.Code + ": " : "") + ex.Message;
            _notice = ex is McpToolException tool
                ? (tool.Code == "STALE_REVISION" || tool.Code == "DOCUMENT_CHANGED"
                    ? "Il documento è cambiato. Attendi l'aggiornamento e riprova."
                    : "Dati CAD non disponibili. Verifica il collegamento M2 sul PC o apri i dettagli.")
                : UiText.Error(ex);
        }
        private string InfoBody() => (_selected?.Name ?? _context.Current?.Name ?? "Nessun documento")
            + "\nMateriale: " + (_info?.Material ?? "—") + "   Massa: " + Format(_info?.MassKg, "kg")
            + "\nVolume: " + Format(_info?.VolumeMm3, "mm³") + "   Area: " + Format(_info?.AreaMm2, "mm²")
            + "\nVincoli: " + (_info?.Constraints?.ToString() ?? "—") + "   DOF: "
            + (_info?.TranslationDof?.ToString() ?? "—") + " trasl. / " + (_info?.RotationDof?.ToString() ?? "—") + " rot.";

        private void Render()
        {
            if (_panel == null || !_panel.gameObject.activeSelf || _screen == "numeric") return;
            var actions = new List<(string, Action)>();
            string body = _notice;
            string title = "Ispeziona";
            if (_screen == "error")
            {
                title = "Dettagli errore";
                body = _errorDetails ?? _notice;
                if (body.Length > 600) body = body.Substring(0, 600) + "…";
                actions.Add(("Ispeziona", () => Open("tools")));
            }
            else if (_screen == "tools")
            {
                body = "Solo ispezione • presa normale = spostamento visuale\n" + _notice;
                actions.Add(("Browser", () => Open("browser")));
                actions.Add(("Proprietà", () => { Open("details"); LoadInfo(); }));
                actions.Add(("Misura", BeginMeasure)); actions.Add(("Sezione", () => Open("section")));
                actions.Add(("Scala", () => Open("scale")));
                actions.Add((_environment.Mode == EnvironmentMode.MixedReality ? "Studio VR" : "Mixed Reality", () =>
                { _environment.Set(_environment.Mode == EnvironmentMode.MixedReality ? EnvironmentMode.StudioVr : EnvironmentMode.MixedReality); Render(); }));
            }
            else if (_screen == "browser")
            {
                title = "Browser";
                body = (_context.Current?.Name ?? "Nessun documento") + "\nSeleziona il nome; Apri entra nel contesto.\n" + _notice;
                var children = _context.Current?.Children ?? new SceneNode[0];
                _page = Mathf.Clamp(_page, 0, Mathf.Max(0, (children.Count - 1) / 3));
                foreach (var node in children.Skip(_page * 3).Take(3))
                {
                    var n = node;
                    actions.Add(((node.Suppressed ? "[Soppresso] " : "") + node.Name, () => SelectNode(n)));
                    actions.Add(("Apri ›", () => Enter(n)));
                }
                actions.Add(("‹ Pagina", () => { _page = Mathf.Max(0, _page - 1); Render(); }));
                actions.Add(("Pagina ›", () => { _page++; Render(); }));
                actions.Add(("Indietro", Back)); actions.Add(("Documenti aperti", LoadDocuments));
            }
            else if (_screen == "documents")
            {
                title = "Documenti aperti";
                body = "Attivazione in Inventor, senza salvare o modificare il CAD.\n" + _notice;
                _page = Mathf.Clamp(_page, 0, Mathf.Max(0, (_documents.Count - 1) / 6));
                foreach (var doc in _documents.Skip(_page * 6).Take(6)) { var d = doc; actions.Add((doc.Name, () => Activate(d))); }
                actions.Add(("‹ Pagina", () => { _page = Mathf.Max(0, _page - 1); Render(); }));
                actions.Add(("Pagina ›", () => { _page++; Render(); }));
                actions.Add(("Aggiorna", LoadDocuments)); actions.Add(("Browser", () => Open("browser")));
            }
            else if (_screen == "details")
            {
                title = "Proprietà"; body = InfoBody() + "\n" + _notice;
                actions.Add(("Aggiorna", LoadInfo)); actions.Add(("Misura", BeginMeasure));
                if (_selected != null && _context.Current != _selected) actions.Add(("Apri contesto", () => Enter(_selected)));
            }
            else if (_screen == "measure")
            {
                title = "Misura";
                body = "Distanza tra due punti sulla mesh • approssimata\n"
                    + (_measure.DistanceMm.HasValue ? "≈ " + Format(_measure.DistanceMm, "mm") : _measure.HasFirstPoint ? "Seleziona il secondo punto." : "Seleziona il primo punto.")
                    + "\nMisure pinnate: " + _measure.PinnedCount + "/20\n" + _notice;
                actions.Add(("Nuova misura", BeginMeasure));
                actions.Add(("Pin misura", () => { _notice = _measure.Pin() ? "Misura mantenuta sul modello." : "Completa una misura (massimo 20 pin)."; Render(); }));
                actions.Add(("Annulla misura", () => { _measure.Cancel(); _modeText.text = "INSPECT"; Render(); }));
                actions.Add(("Rimuovi tutte", () => { _measure.ClearAll(); _modeText.text = "INSPECT"; Render(); }));
            }
            else if (_screen == "scale")
            {
                title = "Scala visuale";
                body = "Scala: " + _view.transform.localScale.x.ToString("0.###", _culture) + "×\nFit to room: spazio disponibile "
                    + _roomExtent.ToString("0.##", _culture) + " m (impostato)\n" + _notice;
                actions.Add(("Mantieni 1:1", () => SetScale(ModelScaleMode.OneToOne)));
                actions.Add(("Fit to room", () => SetScale(ModelScaleMode.FitToRoom)));
                actions.Add(("Table scale · 60 cm", () => SetScale(ModelScaleMode.Table)));
                actions.Add(("Spazio disponibile", () => Number("Spazio disponibile (m)", _roomExtent, 0.2f, 20f, v => _roomExtent = v, "scale")));
                actions.Add(("Porta davanti a me", Recenter));
            }
            else if (_screen == "section")
            {
                title = "Sezione visuale";
                body = "Punta il piano e tieni Grip per spostarlo/ruotarlo.\nOffset: " + Format(_section.OffsetMm, "mm")
                    + "   Angolo Y: " + Format(_section.AngleDegrees, "°") + "\nSezione senza chiusura delle superfici tagliate.\n" + _notice;
                actions.Add((_section.Active ? "Disattiva sezione" : "Attiva sezione", () => { _section.SetActive(!_section.Active); Render(); }));
                actions.Add(("Offset numerico", () => Number("Offset (mm)", _section.OffsetMm, -1000000, 1000000, _section.SetOffset, "section")));
                actions.Add(("Angolo numerico", () => Number("Angolo Y (gradi)", _section.AngleDegrees, -360, 360, _section.SetAngle, "section")));
                actions.Add(("Reset piano", () => { _section.ResetPlane(ScenePlacement.LocalBounds(_view.transform)); _section.SetActive(true); Render(); }));
            }
            if (!string.IsNullOrEmpty(_errorDetails) && _screen != "error") actions.Add(("Dettagli errore", () => Open("error")));
            actions.Add((_pinned ? "Sblocca pannello" : "Pin pannello", () => { _pinned = !_pinned; Render(); }));
            actions.Add(("Chiudi", () => _panel.gameObject.SetActive(false)));
            _panel.ShowMessage(title, body);
            _panel.SetActions(actions.ToArray());
            // Backend-dependent actions fail closed; local geometry tools remain available offline.
            foreach (var button in _panel.GetComponentsInChildren<Button>())
            {
                var text = button.GetComponentInChildren<Text>()?.text;
                if (text == "Aggiorna" || text == "Documenti aperti" || _screen == "documents" && _documents.Any(d => d.Name == text)
                    || _screen == "browser" && (_context.Current?.Children.Any(n => text.EndsWith(n.Name)) ?? false))
                    button.interactable = _online && !_busy;
            }
        }
        private void BeginMeasure()
        {
            _measure.Begin(); _notice = ""; _modeText.text = "MISURA • punto 1"; Open("measure");
        }
        private void Number(string title, float value, float min, float max, Action<float> apply, string returnScreen)
        {
            _screen = "numeric";
            _panel.PromptText(title, "Valore da " + min + " a " + max + ". Separatore decimale: punto.", value.ToString(CultureInfo.InvariantCulture), text =>
            {
                if (float.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var result)
                    && !float.IsNaN(result) && !float.IsInfinity(result) && result >= min && result <= max) { apply(result); _notice = ""; }
                else _notice = "Valore non valido.";
                _screen = returnScreen; Render();
            }, () => { _screen = returnScreen; Render(); });
        }
        private void SetScale(ModelScaleMode mode)
        {
            _scaleMode = mode;
            InspectionGeometry.ApplyScale(_view.transform, ScenePlacement.LocalBounds(_view.transform), mode, _roomExtent);
            _notice = ""; Render();
        }
        private void Recenter()
        {
            var root = _view.transform;
            var bounds = ScenePlacement.LocalBounds(root);
            var scaled = new Bounds(bounds.center * root.localScale.x, bounds.size * root.localScale.x);
            var pose = ScenePlacement.InFront(scaled, _head.position, _head.forward);
            root.SetPositionAndRotation(pose.position, pose.rotation); Render();
        }
        private void UpdateBreadcrumb()
        {
            foreach (var child in _breadcrumb.transform.Cast<Transform>().ToArray()) Release(child.gameObject);
            var path = _context.Path;
            // Keep the root plus the latest three levels; Back always reaches omitted levels.
            var indices = Enumerable.Range(0, path.Count).Where(i => i == 0 || i >= path.Count - 3).ToArray();
            float width = 800f / Mathf.Max(1, indices.Length + 1);
            var back = UiFactory.Button(_breadcrumb.transform, "‹", UiFactory.Key, 24, Back);
            Position((RectTransform)back.transform, new Vector2(-400 + width / 2, 0), new Vector2(width - 5, 50));
            for (int j = 0; j < indices.Length; j++)
            {
                int index = indices[j];
                var button = UiFactory.Button(_breadcrumb.transform, path[index].Name, UiFactory.Background, 20, () =>
                { CancelRequests(); _context.GoTo(index); _selected = null; _info = null; _selection?.ResetLocal(); _page = 0; UpdateCompact(); UpdateBreadcrumb(); Render(); });
                Position((RectTransform)button.transform, new Vector2(-400 + width * (j + 1.5f), 0), new Vector2(width - 5, 50));
            }
            _breadcrumb.gameObject.SetActive(_visible && _scene != null);
        }

        private void Update()
        {
            if (!_visible || DesignActive) return;
            bool tracked = OVRInput.IsControllerConnected(_ray.Controller) && OVRInput.GetControllerPositionTracked(_ray.Controller)
                && OVRInput.GetControllerOrientationTracked(_ray.Controller);
            if (!tracked || !OVRInput.Get(OVRInput.Button.PrimaryHandTrigger, _ray.Controller))
            { if (_grab != null) { _grab = null; Render(); } return; }
            if (OVRInput.GetDown(OVRInput.Button.PrimaryHandTrigger, _ray.Controller))
            {
                var input = EventSystem.current?.currentInputModule as ControllerUiInputModule;
                if (input != null && input.CurrentHit.isValid && input.CurrentHit.gameObject.transform.IsChildOf(_panel.transform)) _grab = _panel.transform;
                else if (_screen == "section" && _section.HitHandle(new Ray(_ray.Origin.position, _ray.Origin.forward), out _)) _grab = _section.transform;
                else if (CadRaycaster.TryPick(new Ray(_ray.Origin.position, _ray.Origin.forward), 20, out _, out _, out _)) _grab = _view.transform;
                if (_grab != null)
                {
                    _grabPosition = _ray.Origin.InverseTransformPoint(_grab.position);
                    _grabRotation = Quaternion.Inverse(_ray.Origin.rotation) * _grab.rotation;
                }
            }
            if (_grab == null) return;
            var position = _ray.Origin.TransformPoint(_grabPosition); var rotation = _ray.Origin.rotation * _grabRotation;
            if (_grab == _section.transform) _section.SetWorldPose(position, rotation);
            else _grab.SetPositionAndRotation(position, rotation);
        }
        private void LateUpdate()
        {
            if (!_visible) return;
            _wrist.transform.rotation = _head.rotation;
            _breadcrumb.transform.SetPositionAndRotation(_head.position + _head.forward * 0.75f - _head.up * 0.24f, _head.rotation);
            if (_selected != null)
            {
                var position = _view.transform.TransformPoint(_compactAnchor);
                _compact.transform.SetPositionAndRotation(position + _head.right * 0.18f, _head.rotation);
            }
        }
        private void OnDestroy()
        {
            _requests.Cancel(); _requests.Dispose();
            if (_ray != null) _ray.PointPicked -= OnPointPicked;
            if (_panel != null) Release(_panel.gameObject);
            if (_wrist != null) Release(_wrist.gameObject);
            if (_breadcrumb != null) Release(_breadcrumb.gameObject);
            if (_compact != null) Release(_compact.gameObject);
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
