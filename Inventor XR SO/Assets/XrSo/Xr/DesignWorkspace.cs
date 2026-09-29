using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Selection;
using InventorXrSo.Core.Session;
using InventorXrSo.Core.Voice;
using InventorXrSo.Unity.Scene;
using InventorXrSo.Unity.Ui;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace InventorXrSo.Xr
{
    /// <summary>Quest M3: local drafts, explicit Inventor preview and explicit Apply.</summary>
    [DefaultExecutionOrder(115)]
    public sealed class DesignWorkspace : MonoBehaviour
    {
        private CadSceneView _view;
        private SelectionVisuals _selection;
        private ControllerRay _ray;
        private Transform _head;
        private HomePanel _panel;
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
        private string[] _constraintRows = Array.Empty<string>();
        private string _constraintKind;
        private string _parameterName;
        private readonly List<int> _constraintPicks = new List<int>();
        private CadPoint? _first;
        private SketchSnap _candidate;
        private bool _snapLocked, _online, _visible, _busy, _through = true, _negative, _symmetric, _pinned;
        private string _operation = "join";
        private readonly HashSet<string> _edges = new HashSet<string>();
        private double _dimension = 10, _diameter = 5;
        private int _page, _generation;
        private CancellationTokenSource _reads = new CancellationTokenSource();
        private Transform _grab;
        private Vector3 _grabPosition;
        private Quaternion _grabRotation;
        private bool _dimensionDrag;
        private CadPoint _dragStart;
        private double _dragValue;
        private double _dragSide=1;
        private LineRenderer _handle;
        private Text _cursorText;
        private Canvas _cursorCanvas;
        private float _nextRender;
        public bool Active { get; private set; }
        public bool RequiresCadReview => _reviewAfterRebind || _pendingMutations > 0 || _session?.Status == DesignStatus.Committing || _session?.Status == DesignStatus.RefreshRequired;
        public Func<bool> CanEnter { get; set; }
        public event Action<bool> ActiveChanged;

        public void Initialize(CadSceneView view, SelectionVisuals selection, ControllerRay ray, Transform head)
        {
            _view = view; _selection = selection; _ray = ray; _head = head;
            _panel = HomePanel.Create(transform); _panel.name = "Design";
            ((RectTransform)_panel.transform).sizeDelta = new Vector2(820, 1100);
            _panel.transform.localScale = Vector3.one * 0.00065f;
            XrUi.MakeInteractive(_panel.Canvas, head.GetComponent<Camera>());
            _panel.gameObject.SetActive(false);
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
            Render();
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
            Render();
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
            ActiveChanged?.Invoke(true); _panel.gameObject.SetActive(true);
            if (!_pinned)
            {
                var forward = Vector3.ProjectOnPlane(_head.forward, Vector3.up).normalized;
                if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
                _panel.transform.SetPositionAndRotation(_head.position+forward*0.9f+_head.right*0.48f, Quaternion.LookRotation(forward));
            }
            _screen = "tools"; Render(); LoadContext();
        }
        public void Close()
        {
            Active = false; CancelReads();
            if (_session != null && _session.Status != DesignStatus.Committing && _session.Status != DesignStatus.RefreshRequired) _session.Cancel();
            ResetDraft(); _panel?.gameObject.SetActive(false); _selection?.Clear();
            if (_ray != null) _ray.CanPick = _visible;
            ActiveChanged?.Invoke(false);
        }
        private void CancelReads()
        { _generation++; _reads.Cancel(); _reads.Dispose(); _reads = new CancellationTokenSource(); _busy = false; }
        private void ResetDraft()
        {
            _constraintKind=null; _constraintPicks.Clear(); _parameterName=null;
            _dimensionStep = 0; _dimensionIndex = -1;
            _history = null;
            _sketch = null; _first = null; _feature = null; _existingSketch = null; _face = null;
            _faceSelection = null;
            _edges.Clear(); _snapLocked = false; _dimensionDrag = false; _grab = null; _renderedPlan = null;
            _previewView?.Clear(); _geometry?.Clear();
            if (_handle != null) _handle.positionCount = 0;
            if (_cursorCanvas != null) _cursorCanvas.gameObject.SetActive(false);
            _screen = "tools";
        }
        private async void LoadContext()
        {
            if (!_online || _state == null || _kind != "part" || _backend == null || _busy) { Render(); return; }
            int generation = _generation; var state = _state; _busy = true; _history = null;
            try
            {
                var context = await _backend.GetDesignContextAsync(state, _reads.Token);
                if (generation != _generation) return;
                _context = context; _notice = context.Truncated ? "Elenco CAD parziale: alcune entità superano i limiti." : "";
                if (_backend is IDesignHistoryBackend historyBackend)
                {
                    _history = null;
                    var history = await historyBackend.GetHistoryAsync(state,_reads.Token);
                    if (generation != _generation) return;
                    _history = history;
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (generation == _generation) _notice = ex.Message; }
            finally { if (generation == _generation) { _busy = false; Render(); } }
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
                catch (Exception ex) { _notice = "Anteprima non visualizzabile: " + ex.Message; }
            }
            if (_session.Preview == null) { _previewView.Clear(); _renderedPlan = null; }
            ShowValidationContext();
            Render();
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

        private void StartSketch(string plane, SketchFrame frame)
        {
            ResetDraft(); _sketch = new SketchDraft(plane, frame); _shape = SketchShape.Line; _screen = "sketch";
            UpdateDraft(); Preview();
        }
        private void CreateSketch()
        {
            if (_face != null && _context?.PlanarFaces.Contains(_face) == true)
                StartSketch(_face, null);
            else Page("planes");
        }
        private void Feature(string feature)
        {
            _dimensionStep=0; _dimensionIndex=-1;
            if (feature != "extrude") { _sketch = null; _existingSketch = null; _geometry.Clear(); }
            _feature = feature; _screen = "feature"; _first = null; _dimension = feature == "fillet" || feature == "chamfer" ? 2 : 10;
            _notice = ""; UpdateDraft(); Render();
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
            catch (Exception ex) { _notice = ex.Message; UpdateHandle(); _session.RejectDraft(ex.Message); }
        }
        private async void Preview()
        {
            try { UpdateDraft(); if (_session.Status != DesignStatus.Error) await _session.PreviewAsync(); }
            catch (Exception ex) { _notice = ex.Message; Render(); }
        }
        private async void Apply()
        {
            var session=_session;
            await RunMutation(session,()=>session.ApplyAsync());
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
            try
            {
                await mutation();
            }
            catch (Exception ex) { if (owner==_session) _notice=ex.Message; }
            finally { _pendingMutations--; }
            if (this==null) return;
            if (owner==_session && !owner.CommitOutcomeUnknown && owner.Status==DesignStatus.RefreshRequired)
            {
                try { await RefreshAfterApply(false); }
                catch(Exception ex) { if(owner==_session) _notice=ex.Message; }
            }
            Render();
        }
        private async Task RefreshAfterApply(bool reviewed)
        {
            if (!_online || _backend == null || _pendingMutations>0) return;
            var backend=_backend; var session=_session; int generation=_generation;
            var state = await backend.GetDocumentStateAsync(_reads.Token);
            if (this==null || backend!=_backend || session!=_session || generation!=_generation) return;
            session.AcknowledgeRefresh(state, reviewed); _reviewAfterRebind=false; _state = state; ResetDraft(); LoadContext();
            _notice = "Documento aggiornato. Attendo il modello da Inventor.";
        }
        private async void ReviewUnknown()
        {
            try { await RefreshAfterApply(true); }
            catch (Exception ex) { _notice = ex.Message; }
            Render();
        }

        private void Render()
        {
            if (!Active || _screen == "numeric" || _panel == null) return;
            var actions = new List<(string, Action)>();
            string title = "DESIGN", body = "Seleziona una faccia con il raggio. Grip sposta solo la vista.\n";
            bool editable = _session?.CanEdit == true && _context != null && !_busy;
            if (_kind != "part") body = "Design richiede una parte attiva in Inventor.\nApri il Browser e attiva una parte già aperta sul PC.";
            else if (!_online) body = "Offline: anteprima e modifiche CAD disabilitate.";
            else if (_busy) body = "Lettura del contesto CAD…";
            else if (_context == null && _session?.Status != DesignStatus.RefreshRequired)
            { body = "Riferimenti Design non disponibili."; actions.Add(("Riprova", LoadContext)); }
            else if (_session?.Status == DesignStatus.RefreshRequired)
            {
                body = _pendingMutations>0 ? "Attendo la conclusione della richiesta CAD precedente. Nessun nuovo comando verrà inviato."
                    : _session.CommitOutcomeUnknown ? "Esito della modifica CAD non confermato. Controlla il modello in Inventor prima di proseguire. Il comando non verrà ripetuto."
                    : "Aggiornamento del documento necessario prima di un altro comando.";
                if(_pendingMutations==0) actions.Add((_session.CommitOutcomeUnknown ? "Ho controllato il CAD" : "Aggiorna documento", ReviewUnknown));
            }
            else if (editable)
            {
                if (_screen == "tools")
                {
                    actions.Add(("Crea schizzo", CreateSketch));
                    actions.Add(("Estrusione", () => Page("sketches")));
                    actions.Add(("Foro", () => Feature("hole")));
                    actions.Add(("Raccordo", () => Feature("fillet")));
                    actions.Add(("Smusso", () => Feature("chamfer")));
                    actions.Add(("Parametri", () => Page("parameters")));
                    actions.Add(("Aggiorna riferimenti", LoadContext));
                    if (_session.Status == DesignStatus.Empty)
                    {
                        if (_history?.CanUndo == true) actions.Add(("Annulla modifica XR", () => ApplyHistory(false)));
                        if (_history?.CanRedo == true) actions.Add(("Ripeti modifica XR", () => ApplyHistory(true)));
                    }
                    body += "Un’anteprima nuova cancella la possibilità di Ripeti in Inventor.\n";
                }
                else if (_screen == "planes")
                {
                    title = "Piano dello schizzo";
                    if (_face != null) actions.Add(("Faccia selezionata", () => StartSketch(_face, null)));
                    var planes = _context.Planes.ToArray();
                    foreach (var plane in planes.Skip(_page*6).Take(6)) actions.Add((plane.Name, () => StartSketch(plane.Reference, plane.Frame)));
                    Pagination(actions, planes.Length);
                }
                else if (_screen == "sketches")
                {
                    title = "Profilo da estrudere";
                    foreach (var sketch in _context.Sketches.Skip(_page*6).Take(6)) actions.Add((sketch.Name, () => { _sketch = null; _existingSketch = sketch.Name; Feature("extrude"); }));
                    Pagination(actions, _context.Sketches.Count);
                }
                else if (_screen == "parameters")
                {
                    title = "Parametri CAD";
                    var items = _context.Parameters.OfType<JObject>().Where(p => p["value_mm"] != null || p["value_deg"] != null || (string)p["unit"] == "ul").ToArray();
                    foreach (var p in items.Skip(_page*6).Take(6))
                    {
                        string name = (string)p["name"], unit = p["value_mm"] != null ? "mm" : p["value_deg"] != null ? "deg" : "ul";
                        double value = (double?)(p["value_mm"] ?? p["value_deg"] ?? p["value_unitless"]) ?? 0;
                        actions.Add((name + " = " + Format(value) + " " + unit, () => Numbers(name, new[] { unit }, new[] { value }, values =>
                        { _feature = null; _sketch = null; _parameterName=name; _screen = "parameter"; _session.SetDraft(new JArray(DesignOperations.Parameter(name, values[0], unit))); PreviewParameter(); })));
                    }
                    Pagination(actions, items.Length);
                }
                else if (_screen == "sketch")
                {
                    title = "Schizzo • " + _shape;
                    body = (_sketch?.Frame == null ? "Attendo il piano esatto da Inventor." : "Trigger: primo e secondo punto. A: blocca/sblocca snap.\n")
                        + "Geometrie: " + _sketch?.Elements.Count + ". Vincoli confermati: " + _sketch?.ConstraintCount + ". Massimo 30 complessivi. Input in mm.\n";
                    foreach (SketchShape shape in Enum.GetValues(typeof(SketchShape)))
                    { var value = shape; actions.Add((shape == SketchShape.Line ? "Linea" : shape == SketchShape.Rectangle ? "Rettangolo" : "Cerchio", () => { _shape = value; _first = null; _dimensionStep=0; _geometry.ShowDraft(_sketch); Render(); })); }
                    actions.Add(("Coordinate numeriche", NumericShape));
                    actions.Add(("Quota geometria", () => { _dimensionStep=1; _dimensionIndex=-1; _first=null; _notice="Quota: seleziona una linea, un rettangolo o un cerchio con il raggio."; Render(); }));
                    actions.Add(("Aggiungi vincolo",()=>{_dimensionStep=0; _first=null; Page("constraint_kind");}));
                    actions.Add(("Vincoli della geometria", () => {
                        if (CurrentSketchSnapshot == null) _notice="Calcola prima l’anteprima per leggere i vincoli reali.";
                        else { _dimensionStep=3; _first=null; _notice="Seleziona la geometria per vedere i vincoli di Inventor."; } Render(); }));
                    actions.Add(("Mostra tutti i vincoli", () => {
                        var snapshot=CurrentSketchSnapshot;
                        if (snapshot == null) { _notice="Calcola prima l’anteprima per leggere i vincoli reali."; Render(); }
                        else ShowConstraints(snapshot.Constraints); }));
                    actions.Add(("Rimuovi ultimo", () => { _dimensionStep=0; _sketch.RemoveLast(); UpdateDraft(); Render(); }));
                    actions.Add(("Estrudi schizzo", () => Feature("extrude")));
                }
                else if (_screen == "constraint_kind")
                {
                    title="Vincolo da confermare"; body="Seleziona il tipo, poi le geometrie. Il CAD cambia soltanto con Applica.";
                    foreach (var item in new[] { ("Tangente","tangent"),("Uguale","equal"),("Simmetria","symmetric"),
                        ("Parallelo","parallel"),("Perpendicolare","perpendicular"),("Concentrico","concentric") })
                    { var kind=item.Item2; actions.Add((item.Item1,()=>{_constraintKind=kind; _constraintPicks.Clear(); Page("constraint_entities");})); }
                    if (_sketch.ConstraintCount>0) actions.Add(("Rimuovi ultimo vincolo",()=>{_sketch.RemoveLastConstraint(); UpdateDraft(); Page("sketch");}));
                    actions.Add(("Torna allo schizzo",()=>Page("sketch")));
                }
                else if (_screen == "constraint_entities")
                {
                    title="Geometrie del vincolo";
                    int needed=_constraintKind=="symmetric" ? 3 : 2;
                    body=_constraintPicks.Count==2 && needed==3 ? "Seleziona la linea asse di simmetria." : "Seleziona geometria "+(_constraintPicks.Count+1)+" di "+needed+".";
                    var entities=_sketch.ConstraintEntityKeys.Where(i=>!_constraintPicks.Contains(i)).ToArray();
                    foreach(int index in entities.Skip(_page*6).Take(6))
                    {
                        int selected=index;
                        actions.Add((_sketch.ConstraintEntityLabel(index),()=>{
                            _constraintPicks.Add(selected); _geometry.ShowDraft(_sketch,selected/4,_sketch.Elements[selected/4].Shape==SketchShape.Rectangle ? selected%4 : -1);
                            Page(_constraintPicks.Count==needed ? "constraint_confirm" : "constraint_entities"); }));
                    }
                    if(entities.Length==0) body+=" Nessuna geometria disponibile. Disegna linee o cerchi separati.";
                    Pagination(actions,entities.Length);
                    actions.Add(("Annulla selezione",()=>{_constraintPicks.Clear(); Page("sketch");}));
                }
                else if (_screen == "constraint_confirm")
                {
                    title="Conferma vincolo";
                    string kindLabel=_constraintKind switch { "tangent"=>"Tangente", "equal"=>"Uguale", "symmetric"=>"Simmetria", "parallel"=>"Parallelo", "perpendicular"=>"Perpendicolare", _=>"Concentrico" };
                    body=kindLabel+" • "+string.Join(", ",_constraintPicks.Select(_sketch.ConstraintEntityLabel))+".\nInserisce il vincolo nella bozza; poi Anteprima e Applica.";
                    actions.Add(("Conferma nella bozza",()=>{
                        try { _sketch.AddEntityConstraint(_constraintKind,_constraintPicks[0],_constraintPicks[1],_constraintPicks.Count==3 ? _constraintPicks[2] : -1); _notice="Vincolo aggiunto alla bozza. Calcola l’anteprima."; UpdateDraft(); Page("sketch"); }
                        catch(Exception ex) { _notice=ex.Message; Render(); }
                    }));
                    actions.Add(("Scegli di nuovo",()=>{_constraintPicks.Clear(); Page("constraint_entities");}));
                    actions.Add(("Annulla vincolo",()=>Page("sketch")));
                }
                else if (_screen == "constraints")
                {
                    title="Vincoli Inventor";
                    body=_constraintRows.Length==0 ? "Nessun vincolo geometrico restituito." : string.Join("\n",_constraintRows.Skip(_page*8).Take(8));
                    if (_page>0) actions.Add(("Precedenti",()=>{_page--;Render();}));
                    if ((_page+1)*8<_constraintRows.Length) actions.Add(("Successivi",()=>{_page++;Render();}));
                    actions.Add(("Torna allo schizzo",()=>Page("sketch")));
                }
                else if (_screen == "feature")
                {
                    title = _feature == "extrude" ? "Estrusione" : _feature == "hole" ? "Foro" : _feature == "fillet" ? "Raccordo" : "Smusso";
                    body = (_feature=="extrude" && _symmetric ? "Dimensione totale (metà per lato): " : "Dimensione: ") + Format(_dimension) + " mm.\nGrip + Trigger sulla freccia: regola la dimensione.\n";
                    if (_feature == "fillet" || _feature == "chamfer") body += "Trigger sugli spigoli: aggiungi/rimuovi. Selezionati: " + _edges.Count + "\n";
                    actions.Add(("Dimensione numerica", () => Numbers("Dimensione", new[] { "mm" }, new[] { _dimension }, n => { _dimension = n[0]; UpdateDraft(); })));
                    if (_feature == "hole")
                    {
                        body += "Diametro: " + Format(_diameter) + " mm. Seleziona una faccia piana.\n";
                        actions.Add(("Diametro", () => Numbers("Diametro foro", new[] { "mm" }, new[] { _diameter }, n => { _diameter = n[0]; UpdateDraft(); })));
                        actions.Add((_through ? "Passante → Cieco" : "Cieco → Passante", () => { _through = !_through; UpdateDraft(); Render(); }));
                        actions.Add(("Posizione esatta XYZ", () => Numbers("Centro foro nel modello", new[] { "X mm", "Y mm", "Z mm" }, new[] { _facePoint.X,_facePoint.Y,_facePoint.Z }, n => { _facePoint = new CadPoint(n[0],n[1],n[2]); UpdateDraft(); })));
                    }
                    if (_feature == "extrude")
                    {
                        actions.Add(("Operazione: " + _operation, () => { var options = new[] { "join","cut","intersect","new_body" }; _operation = options[(Array.IndexOf(options,_operation)+1)%4]; UpdateDraft(); Render(); }));
                        actions.Add((_symmetric ? "Simmetrica" : _negative ? "Negativa" : "Positiva", () => { if (_symmetric) { _symmetric=false; _negative=false; } else if (_negative) _symmetric=true; else _negative=true; UpdateDraft(); Render(); }));
                    }
                }
            }
            if (_session != null && _session.Status != DesignStatus.RefreshRequired && _session.Status != DesignStatus.Committing)
            {
                if (_screen == "sketch" || _screen == "feature" || _screen == "parameter")
                {
                    actions.Add(("Anteprima", _screen == "parameter" ? PreviewParameter : Preview));
                    actions.Add(("Applica", Apply));
                    actions.Add(("Annulla comando", () => { _session.Cancel(); ResetDraft(); Render(); }));
                }
                if (_screen != "tools") actions.Add(("Strumenti", () => Page("tools")));
            }
            body += "\n" + (_session?.Status == DesignStatus.Previewing ? "Calcolo anteprima…" : _session?.Status == DesignStatus.Committing ? "Applicazione in corso…" : _session?.CanApply == true ? "Anteprima verificata. Applica oppure modifica." : "");
            body += "\n" + _notice;
            if (!string.IsNullOrEmpty(_session?.Error))
            {
                body += "\nComando non completato. Correggi i parametri e riprova.";
                if (_geometry.HasErrorContext) body += "\nIn arancione: geometria del comando da controllare.";
                actions.Add(("Dettagli errore", () => { _panel.ShowMessage("Dettagli", _session.Error); _panel.SetActions(("Indietro", Render)); }));
            }
            actions.Add((_pinned ? "Sblocca pannello" : "Pin pannello", () => { _pinned = !_pinned; Render(); }));
            actions.Add(("Torna a Inspect", Close));
            _panel.ShowMessage(title, body); _panel.SetActions(actions.ToArray());
            foreach (var button in _panel.GetComponentsInChildren<Button>())
            {
                string label = button.GetComponentInChildren<Text>()?.text;
                if (label == "Applica") button.interactable = _session?.CanApply == true;
                else if (_session?.Status == DesignStatus.Committing || _session?.Status == DesignStatus.Previewing)
                    button.interactable = label == "Torna a Inspect" || label == "Annulla comando";
            }
        }
        // ---------------------------------------------------------------- voice surface

        public const string FieldDimension = "design.dimension";
        private const double MinDimensionMm = 0.001, MaxDimensionMm = 10000;

        private bool InDraftScreen => _screen == "sketch" || _screen == "feature" || _screen == "parameter";

        /// <summary>Same gate as the tool buttons: part, online, context read, no request in flight, on the tools page.</summary>
        private bool ToolsEditable => Active && _panel != null && _kind == "part" && _online && !_busy && _context != null
            && _pendingMutations == 0 && _session != null && _session.CanEdit && _screen == "tools"
            && _session.Status != DesignStatus.Committing && _session.Status != DesignStatus.Previewing;

        /// <summary>Same enablement as the buttons "Crea schizzo", "Raccordo", "Smusso", "Annulla modifica XR", "Ripeti modifica XR", "Annulla comando" and "Applica".</summary>
        public bool IsEnabled(string commandId)
        {
            switch (commandId)
            {
                case CommandIds.CreateSketch:
                case CommandIds.Fillet:
                case CommandIds.Chamfer: return ToolsEditable;
                case CommandIds.Undo: return ToolsEditable && _session.Status == DesignStatus.Empty && _history?.CanUndo == true;
                case CommandIds.Redo: return ToolsEditable && _session.Status == DesignStatus.Empty && _history?.CanRedo == true;
                case CommandIds.CancelDraft:
                    return Active && _panel != null && _session != null && InDraftScreen
                        && _session.Status != DesignStatus.RefreshRequired && _session.Status != DesignStatus.Committing;
                case CommandIds.Apply:
                    return Active && _panel != null && _session != null && InDraftScreen && _pendingMutations == 0
                        && _session.Status != DesignStatus.RefreshRequired && _session.Status != DesignStatus.Committing
                        && _session.CanApply;
                default: return false;   // sheet-metal, inspect commands belong to other workspaces
            }
        }

        /// <summary>Runs the command exactly as its button would. Apply only shows a notice: the physical Applica is the single commit path.</summary>
        public bool Invoke(string commandId)
        {
            if (!IsEnabled(commandId)) return false;
            switch (commandId)
            {
                case CommandIds.CreateSketch: CreateSketch(); return true;
                case CommandIds.Fillet: Feature("fillet"); return true;
                case CommandIds.Chamfer: Feature("chamfer"); return true;
                case CommandIds.Undo: ApplyHistory(false); return true;
                case CommandIds.Redo: ApplyHistory(true); return true;
                case CommandIds.CancelDraft: _session.Cancel(); ResetDraft(); Render(); return true;
                case CommandIds.Apply:
                    _notice = "Anteprima verificata. Conferma premendo Applica sul pannello.";
                    Render(); return true;
                default: return false;
            }
        }

        private bool DimensionFieldAvailable => Active && _screen == "feature" && _session?.CanEdit == true && _context != null && !_busy
            && _pendingMutations == 0 && _session.Status != DesignStatus.Committing && _session.Status != DesignStatus.Previewing
            && (_feature == "extrude" || _feature == "fillet" || _feature == "chamfer" || (_feature == "hole" && !_through));

        /// <summary>The single numeric field of the feature page ("Dimensione numerica"), or null when none is on screen.</summary>
        public DictationField ArmedField => DimensionFieldAvailable
            ? new DictationField(FieldDimension, QuantityUnit.Millimeters, MinDimensionMm, MaxDimensionMm) : null;

        /// <summary>Sets the dimension through the keypad path (draft updated, preview invalidated, Apply blocked until a new preview).</summary>
        public bool SetArmedField(string fieldId, double value)
        {
            if (fieldId != FieldDimension || !DimensionFieldAvailable) return false;
            if (double.IsNaN(value) || double.IsInfinity(value) || value < MinDimensionMm || value > MaxDimensionMm) return false;
            try { _notice = ""; _dimension = value; UpdateDraft(); } catch (Exception ex) { _notice = ex.Message; }
            Render();
            return true;
        }

        private async void PreviewParameter() { try { await _session.PreviewAsync(); } catch (Exception ex) { _notice = ex.Message; Render(); } }
        private void Page(string page) { _screen = page; _page = 0; Render(); }
        private void Pagination(List<(string,Action)> actions, int count)
        {
            if (_page > 0) actions.Add(("Precedenti", () => { _page--; Render(); }));
            if ((_page+1)*6 < count) actions.Add(("Successivi", () => { _page++; Render(); }));
        }
        private static string Format(double value) => value.ToString("0.###", CultureInfo.GetCultureInfo("it-IT"));
        private void Numbers(string title, string[] labels, double[] values, Action<double[]> done)
        {
            int generation = _generation, index = 0; string back = _screen; _screen = "numeric";
            void Ask()
            {
                _panel.PromptText(title + " • " + labels[index], "Valore esatto. Separatore decimale: punto.", values[index].ToString(CultureInfo.InvariantCulture), text =>
                {
                    if (generation != _generation) return;
                    if (!double.TryParse(text.Replace(',','.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                        || double.IsNaN(value) || double.IsInfinity(value)) { Ask(); return; }
                    values[index++] = value;
                    if (index < values.Length) { Ask(); return; }
                    _screen = back;
                    try { _notice = ""; done(values); } catch (Exception ex) { _notice = ex.Message; }
                    Render();
                }, () => { _screen = back; Render(); });
            }
            Ask();
        }
        private void NumericShape()
        {
            if (_sketch == null) return;
            if (_shape == SketchShape.Circle)
                Numbers("Cerchio", new[] { "Centro X", "Centro Y", "Raggio mm" }, new[] { 0.0,0.0,10.0 }, n => AddShape(new SketchElement(_shape,new CadPoint(n[0],n[1]),default,n[2])));
            else Numbers(_shape == SketchShape.Line ? "Linea" : "Rettangolo", new[] { "X1","Y1","X2","Y2" }, new[] { 0.0,0.0,20.0,20.0 },
                n => AddShape(new SketchElement(_shape,new CadPoint(n[0],n[1]),new CadPoint(n[2],n[3]))));
        }
        private void DimensionElement(int index, CadPoint? textPoint)
        {
            var item = _sketch.Elements[index];
            if (item.Shape == SketchShape.Circle) Numbers("Quota raggio", new[] { "mm" }, new[] { item.Radius }, n =>
            { _sketch.Replace(index,new SketchElement(item.Shape,item.A,item.B,n[0],true,textPoint)); UpdateDraft(); });
            else if (item.Shape == SketchShape.Rectangle) Numbers("Quote rettangolo", new[] { "Larghezza mm","Altezza mm" }, new[] { item.B.X-item.A.X,item.B.Y-item.A.Y }, n =>
            { _sketch.Replace(index,new SketchElement(item.Shape,item.A,item.A+new CadPoint(n[0],n[1]),dimensioned:true,dimensionText:textPoint)); UpdateDraft(); });
            else Numbers("Quota lunghezza", new[] { "mm" }, new[] { (item.B-item.A).Length }, n =>
            { if (n[0] <= 0) throw new ArgumentException("Lunghezza positiva richiesta."); _sketch.Replace(index,new SketchElement(item.Shape,item.A,item.A+(item.B-item.A)*(n[0]/(item.B-item.A).Length),dimensioned:true,dimensionText:textPoint)); UpdateDraft(); });
        }
        private SketchSnapshot CurrentSketchSnapshot => _session?.Status==DesignStatus.PreviewReady
            ? _session.Preview?.Sketches.FirstOrDefault(s=>s.Name==_sketch?.Name) : null;
        private void ShowConstraints(IEnumerable<string> constraints)
        {
            _dimensionStep=0;
            _constraintRows=constraints.Where(s=>!string.IsNullOrEmpty(s)).GroupBy(ConstraintLabel)
                .Select(g=>g.Key+" × "+g.Count()).OrderBy(s=>s).ToArray();
            Page("constraints");
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
        private void AddShape(SketchElement element) { _sketch.Add(element); _first = null; _snapLocked = false; UpdateDraft(); Render(); }

        private SketchFrame ExtrusionFrame => _sketch?.Frame ?? _context?.Sketches.FirstOrDefault(s => s.Reference == _existingSketch)?.Frame;
        private DesignEdge DimensionEdge => _context?.Edges.FirstOrDefault(e => _edges.Contains(e.Id));
        private bool HasDimensionTarget => _feature == "extrude" ? ExtrusionFrame != null
            : _feature == "hole" ? _context?.Faces.Any(f => f.Id == _face) == true
            : (_feature == "fillet" || _feature == "chamfer") && DimensionEdge != null;
        private CadPoint DimensionOrigin
        {
            get
            {
                if (_feature == "extrude") return ExtrusionFrame?.OriginMm ?? default;
                if (_feature != "fillet" && _feature != "chamfer") return _facePoint;
                var points = DimensionEdge?.PointsMm;
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
        }
        private CadPoint DimensionAxis => _feature == "extrude"
            ? (ExtrusionFrame?.Normal ?? new CadPoint(0,0,1)) * (_negative && !_symmetric ? -1 : 1)
            : _feature == "hole" ? (_context?.Faces.FirstOrDefault(f => f.Id == _face)?.Normal ?? new CadPoint(0,0,1)) * -1
            : new CadPoint(0,1,0);
        private bool SymmetricHandle => _feature=="extrude" && _symmetric;
        private CadPoint DimensionStart => SymmetricHandle ? DimensionOrigin-DimensionAxis*(_dimension*0.5) : DimensionOrigin;
        private CadPoint DimensionEnd => DimensionOrigin+DimensionAxis*(_dimension*(SymmetricHandle ? 0.5 : 1));
        private double DragDimension(CadPoint position) => Math.Max(0.01,_dragValue+(position-_dragStart).Dot(DimensionAxis)*(SymmetricHandle ? 2*_dragSide : 1));
        private void UpdateHandle()
        {
            if (!HasDimensionTarget) { _handle.positionCount = 0; return; }
            _handle.positionCount = 2; _handle.SetPosition(0,CadCoordinates.ToLocal(DimensionStart));
            _handle.SetPosition(1,CadCoordinates.ToLocal(DimensionEnd));
        }
        private void Update()
        {
            if (!Active || !_visible || _ray.Origin == null) return;
            bool tracked = OVRInput.IsControllerConnected(_ray.Controller) && OVRInput.GetControllerPositionTracked(_ray.Controller)
                && OVRInput.GetControllerOrientationTracked(_ray.Controller);
            if (!tracked) { _dimensionDrag = false; _grab = null; _first = null; _snapLocked = false; _geometry.Cursor(null,default,null,_shape); return; }
            var ray = new Ray(_ray.Origin.position,_ray.Origin.forward);
            var ui = EventSystem.current?.currentInputModule as ControllerUiInputModule;
            bool overUi = ui != null && ui.CurrentHit.isValid;
            bool grip = OVRInput.Get(OVRInput.Button.PrimaryHandTrigger,_ray.Controller);
            bool trigger = OVRInput.Get(OVRInput.Button.PrimaryIndexTrigger,_ray.Controller);
            if (!grip || !trigger)
            {
                if (_dimensionDrag) { _dimensionDrag = false; UpdateDraft(); Preview(); }
            }
            else if (_session?.CanEdit == true && HasDimensionTarget && !overUi)
            {
                if (!_dimensionDrag)
                {
                    var handle = new DesignEdge("handle","line",new[] { DimensionStart,DimensionEnd });
                    if (CadCoordinates.PickEdge(_view.transform,new[] { handle },ray,0.025f) != null)
                    {
                        var picked=CadCoordinates.ClosestPointOnSegment(ray,_view.transform.TransformPoint(CadCoordinates.ToLocal(DimensionStart)),_view.transform.TransformPoint(CadCoordinates.ToLocal(DimensionEnd)));
                        _dragSide=(CadCoordinates.FromWorld(_view.transform,picked)-DimensionOrigin).Dot(DimensionAxis)<0 ? -1 : 1;
                        _dimensionDrag = true; _grab = null; _dragStart = CadCoordinates.FromWorld(_view.transform,_ray.Origin.position); _dragValue = _dimension; UpdateDraft();
                    }
                }
                if (_dimensionDrag)
                {
                    _dimension = DragDimension(CadCoordinates.FromWorld(_view.transform,_ray.Origin.position));
                    UpdateHandle();
                    if (Time.unscaledTime > _nextRender) { _nextRender=Time.unscaledTime+0.15f; Render(); }
                    return;
                }
            }
            if (grip && !trigger)
            {
                if (OVRInput.GetDown(OVRInput.Button.PrimaryHandTrigger,_ray.Controller))
                {
                    if (overUi && ui.CurrentHit.gameObject.transform.IsChildOf(_panel.transform)) _grab = _panel.transform;
                    else if (!overUi && CadRaycaster.TryPick(ray,20,out _,out _,out _)) _grab = _view.transform;
                    if (_grab != null) { _grabPosition=_ray.Origin.InverseTransformPoint(_grab.position); _grabRotation=Quaternion.Inverse(_ray.Origin.rotation)*_grab.rotation; }
                }
                if (_grab != null) _grab.SetPositionAndRotation(_ray.Origin.TransformPoint(_grabPosition),_ray.Origin.rotation*_grabRotation);
                return;
            }
            _grab = null;
            if (overUi || _screen == "numeric" || _session?.CanEdit != true || grip) return;
            if (_screen == "sketch" && _sketch?.Frame != null)
            {
                if (!CadCoordinates.SketchRay(_view.transform,_sketch.Frame,ray,out var point)) return;
                if (_dimensionStep != 0)
                {
                    _geometry.Cursor(_sketch.Frame,point,null,_shape);
                    if (!OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger,_ray.Controller)) return;
                    if (_dimensionStep==3)
                    {
                        var selected=CurrentSketchSnapshot?.Pick(point,2);
                        if (selected==null) { _notice="Nessuna geometria dell’anteprima vicina al raggio."; Render(); }
                        else ShowConstraints(selected.Constraints);
                        return;
                    }
                    if (_dimensionStep == 1)
                    {
                        _dimensionIndex=_sketch.Pick(point,2);
                        if (_dimensionIndex < 0) { _notice="Nessuna geometria vicina al raggio."; Render(); return; }
                        _dimensionStep=2; _geometry.ShowDraft(_sketch,_dimensionIndex);
                        _notice="Quota: indica dove posizionare il testo sul piano dello schizzo."; Render();
                    }
                    else { _dimensionStep=0; DimensionElement(_dimensionIndex,point); }
                    return;
                }
                if (!_snapLocked) _candidate = _sketch.Snap(point,_first,1.0);
                if (OVRInput.GetDown(OVRInput.Button.One,_ray.Controller)) _snapLocked=!_snapLocked;
                _geometry.Cursor(_sketch.Frame,_candidate.Point,_first,_shape);
                _cursorCanvas.gameObject.SetActive(true);
                _cursorCanvas.transform.SetPositionAndRotation(_view.transform.TransformPoint(CadCoordinates.ToLocal(_sketch.Frame.ToModel(_candidate.Point)))+Vector3.up*0.035f,
                    Quaternion.LookRotation(_head.forward));
                _cursorText.text = Format(_candidate.Point.X)+", "+Format(_candidate.Point.Y)+" mm • "+_candidate.Kind+(_snapLocked ? " • bloccato" : "");
                if (OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger,_ray.Controller))
                {
                    if (!_first.HasValue) { _first=_candidate.Point; _snapLocked=false; }
                    else try { AddShape(new SketchElement(_shape,_first.Value,_candidate.Point,(_candidate.Point-_first.Value).Length)); }
                        catch (Exception ex) { _notice=ex.Message; Render(); }
                }
                return;
            }
            if (!OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger,_ray.Controller) || _context == null) return;
            if (_feature == "fillet" || _feature == "chamfer")
            {
                string edge = CadCoordinates.PickEdge(_view.transform,_context.Edges,ray,requireVisible:true);
                if (edge != null) { if (!_edges.Add(edge)) _edges.Remove(edge); UpdateDraft(); Render(); }
            }
            else if (CadRaycaster.TryPick(ray,20,out var body,out int triangle,out var hit))
            {
                SelectPlanarFace(body, triangle, hit);
            }
        }
        private void SelectPlanarFace(CadBody body, int triangle, Vector3 hit)
        {
            if (_context == null || _state == null || _context.State.DocumentId != _state.DocumentId
                || _context.State.Revision != _state.Revision || _sceneState?.DocumentId != _state.DocumentId
                || _sceneState?.Revision != _state.Revision || body.Instance.DefinitionId != _state.DocumentId) return;
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
                _notice = "Faccia non piana o riferimenti non aggiornati. Prova Aggiorna riferimenti.";
                if (_feature == "hole") UpdateDraft();
                Render(); return;
            }
            _face = face.Id;
            _facePoint = face.Project(CadCoordinates.FromWorld(_view.transform, hit));
            _faceSelection = new Selection(SelectionKind.Face, body.Instance.OccurrenceId, range.FaceId, face.Id);
            _selection.Show(_faceSelection);
            _notice = "Faccia piana selezionata.";
            if (_feature == "hole") UpdateDraft();
            Render();
        }
        private void OnDestroy()
        {
            _reads.Cancel(); _reads.Dispose();
            if (_session != null) { _session.Changed-=SessionChanged; _session.Dispose(); }
            if (_cursorCanvas != null)
            {
                if (Application.isPlaying) Destroy(_cursorCanvas.gameObject);
                else DestroyImmediate(_cursorCanvas.gameObject);
            }
        }
    }
}
