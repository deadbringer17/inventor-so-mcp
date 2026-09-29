using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Session;
using InventorXrSo.Core.Voice;
using InventorXrSo.Unity.Scene;
using InventorXrSo.Unity.Ui;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.EventSystems;

namespace InventorXrSo.Xr
{
    /// <summary>Assembly authoring: native references, intentional drafts and rendered preview before Apply.</summary>
    [DefaultExecutionOrder(115)]
    public sealed class AssemblyWorkspace : MonoBehaviour
    {
        private CadSceneView _view;
        private ControllerRay _ray;
        private Transform _head;
        private HomePanel _panel;
        private DesignPreviewView _preview;
        private AssemblyVisuals _visuals;
        private IAssemblyWorkspaceBackend _backend;
        private DesignSession _session;
        private DocumentState _state, _sceneState;
        private string _kind, _notice = "", _screen = "tools", _command, _type, _rendered;
        private AssemblyContext _context;
        private AssemblyOccurrence _occurrence;
        private AssemblyReference _a, _b;
        private DesignHistory _history;
        private CadPoint _translation;
        private double _value, _angle, _clearance;
        private bool _online, _visible, _busy, _numeric, _reviewAfterRebind, _flipOrigin, _flipAlignment, _inside, _opposed = true;
        private bool _rotating, _dragging, _pickEdges, _pinned;
        private int _generation, _page, _axisIndex, _mutations;
        private CadPoint _dragStart, _startTranslation;
        private Quaternion _dragRotation;
        private double _startAngle;
        private float _nextRender;
        private Transform _grab;
        private Vector3 _grabPosition;
        private Quaternion _grabRotation;
        private CancellationTokenSource _reads = new CancellationTokenSource();
        public bool Active { get; private set; }
        public bool RequiresCadReview => _reviewAfterRebind || _mutations > 0 || _session?.Status == DesignStatus.Committing || _session?.Status == DesignStatus.RefreshRequired;
        public Func<bool> CanEnter { get; set; }
        public event Action<bool> ActiveChanged;
        private bool Editable => Active && !_busy && _context != null && _session?.CanEdit == true
            && _state?.DocumentId == _sceneState?.DocumentId && _state?.Revision == _sceneState?.Revision;
        private IReadOnlyList<CadPoint> Axes => _occurrence == null ? Array.Empty<CadPoint>() : _rotating ? _occurrence.RotationAxes : _occurrence.TranslationAxes;

        public void Initialize(CadSceneView view, ControllerRay ray, Transform head)
        {
            _view = view; _ray = ray; _head = head;
            _panel = HomePanel.Create(transform); _panel.name = "Assembly";
            ((RectTransform)_panel.transform).sizeDelta = new Vector2(820, 1150);
            _panel.transform.localScale = Vector3.one * 0.00065f;
            XrUi.MakeInteractive(_panel.Canvas, head.GetComponent<Camera>()); _panel.gameObject.SetActive(false);
            _preview = view.gameObject.AddComponent<DesignPreviewView>(); _preview.Initialize(view, head);
            _visuals = view.gameObject.AddComponent<AssemblyVisuals>(); _visuals.Initialize(ray.LineMaterial);
            ray.Picked += Pick; ray.PickedNothing += PickEmpty;
        }
        public void Bind(IAssemblyWorkspaceBackend backend)
        {
            _reviewAfterRebind |= RequiresCadReview; CancelReads();
            if (_session != null) { _session.Changed -= Changed; _session.Dispose(); }
            _backend = backend; _session = backend == null ? null : new DesignSession(backend);
            if (_session != null) { if (_reviewAfterRebind) _session.RequireCadReview(); _session.Changed += Changed; }
            _online = false; _state = null; _context = null; Reset(); Render();
        }
        public void SetScene(LoadedScene scene)
        {
            // CadSceneView.Show rebuilds even for a visibility-only revision and
            // releases the rendered ghost. Its previous rendering proof is gone.
            if (_session?.CanEdit == true && _session.Status == DesignStatus.PreviewReady)
            { _rendered = null; _session.RejectDraft("Scena aggiornata: ricalcola l'anteprima prima di applicare."); }
            _sceneState = scene?.Graph.State; _kind = scene?.Graph.Kind; SetDocumentState(_sceneState);
        }
        public void SetDocumentState(DocumentState state)
        {
            bool changed = _state?.DocumentId != state?.DocumentId || _state?.Revision != state?.Revision;
            _state = state;
            if (changed) { CancelReads(); _context = null; Reset(); }
            _session?.SetContext(state, _online, _kind == "assembly");
            if (changed && Active) Load();
        }
        public void SetOnline(bool online)
        {
            if (_online == online) return;
            _online = online; CancelReads(); _context = null; Reset();
            _session?.SetContext(_state, online, _kind == "assembly");
            if (online && Active) Load(); Render();
        }
        public void SetVisible(bool visible) { _visible = visible; if (!visible) Close(); }
        public void Open()
        {
            if (!_visible || CanEnter?.Invoke() == false) return;
            Active = true; ActiveChanged?.Invoke(true); _ray.CanPick = true;
            _panel.gameObject.SetActive(true);
            var forward = Vector3.ProjectOnPlane(_head.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
            _panel.transform.SetPositionAndRotation(_head.position + forward * 0.9f + _head.right * 0.48f, Quaternion.LookRotation(forward));
            Render(); Load();
        }
        public void Close()
        {
            Active = false; CancelReads();
            if (_session != null && !RequiresCadReview) _session.Cancel();
            Reset(); _panel?.gameObject.SetActive(false); ActiveChanged?.Invoke(false);
            if (_ray != null) _ray.CanPick = _visible;
        }
        private void CancelReads() { _generation++; _reads.Cancel(); _reads.Dispose(); _reads = new CancellationTokenSource(); _busy = false; _numeric = false; }
        private void Reset()
        {
            _occurrence = null; _a = _b = null; _history = null; _command = _type = _rendered = null;
            _translation = default; _angle = _value = 0; _axisIndex = 0; _rotating = _dragging = false;
            _screen = "tools"; _page = 0; _grab = null; _preview?.Clear(); _visuals?.Clear();
        }
        private async void Load() { try { await LoadAsync(null); } catch (Exception ex) { _notice = ex.Message; Render(); } }
        private async Task LoadAsync(string occurrenceId)
        {
            if (!_online || !Active || _state == null || _kind != "assembly" || _backend == null || _busy) return;
            int generation = _generation; var state = _state; _busy = true;
            try
            {
                var context = await _backend.GetAssemblyContextAsync(state, occurrenceId, _reads.Token);
                if (generation != _generation) return;
                _context = context; _notice = context.Truncated ? "Elenco parziale. Alcuni riferimenti non sono disponibili." : "";
                if (occurrenceId != null) _occurrence = context.Occurrences.FirstOrDefault(o => o.Id == occurrenceId);
                if (_session.Status == DesignStatus.Empty)
                {
                    var history = await _backend.GetHistoryAsync(state, _reads.Token);
                    if (generation != _generation) return;
                    _history = history;
                }
            }
            catch (OperationCanceledException) { }
            finally { if (generation == _generation) { _busy = false; Draw(); Render(); } }
        }
        public async Task SelectOccurrenceAsync(string id)
        {
            if (!Editable) return;
            if (_occurrence?.Id != id)
            { InvalidateDraft(); _translation = default; _angle = 0; _axisIndex = 0; }
            CancelReads(); await LoadAsync(id);
        }
        private async void Pick(CadBody body, int triangle)
        {
            if (!Editable || body == null) return;
            try
            {
                await SelectOccurrenceAsync(body.Instance.OccurrenceId);
                if (!Active || _context == null || _busy || _occurrence?.Id != body.Instance.OccurrenceId) return;
                if (_command == "assembly_move") { Draw(); Render(); return; }
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
            }
            catch (Exception ex) { _notice = ex.Message; Render(); }
        }
        private void PickEmpty() { /* Selection persists while the user locates the second reference. */ }
        public void ChooseReference(AssemblyReference reference)
        {
            if (!Editable || reference == null || !reference.Available) return;
            if (_a == null || _b != null) { _a = reference; _b = null; }
            else if (_a.OccurrenceId == reference.OccurrenceId) { _notice = "Scegli il secondo riferimento su un altro componente."; Render(); return; }
            else _b = reference;
            InvalidateDraft(); Draw();
            if (_b != null && _command != null) Preview();
            else if (_b != null) _screen = "compatible";
            Render();
        }
        public void BeginMove()
        {
            if (!Editable || _occurrence?.CanMove != true) return;
            _command = "assembly_move"; _a = _b = null; _translation = default; _angle = 0;
            _rotating = _occurrence.TranslationAxes.Count == 0; _axisIndex = 0; _screen = "move";
            InvalidateDraft(); Draw(); Render();
        }
        public void ChooseConstraint(string type) { if (!Editable) return; _command = "assembly_constraint"; _type = type; _screen = "relation"; InvalidateDraft(); if (_b != null) Preview(); Render(); }
        public void ChooseJoint(string type) { if (!Editable) return; _command = "assembly_joint"; _type = type; _screen = "relation"; InvalidateDraft(); if (_b != null) Preview(); Render(); }
        private void InvalidateDraft()
        { if (_session?.CanEdit == true) _session.RejectDraft("Bozza modificata: calcola una nuova anteprima."); _history = null; }
        private JObject Operation()
        {
            if (_command == "assembly_move")
            {
                if (_occurrence?.CanMove != true) throw new InvalidOperationException("Movimento non disponibile.");
                return AssemblyOperations.Move(_occurrence.Id, _translation, _angle != 0 ? Axes[_axisIndex] : (CadPoint?)null,
                    _angle != 0 ? _occurrence.Center : null, _angle);
            }
            if (_command == "assembly_constraint") return AssemblyOperations.Constraint(_type, _a, _b, _value, _opposed, _inside);
            if (_command == "assembly_joint") return AssemblyOperations.Joint(_type, _a, _b, _value, _flipOrigin, _flipAlignment);
            throw new InvalidOperationException("Scegli un comando Assembly.");
        }
        private async void Preview() { try { await PreviewAsync(); } catch (Exception ex) { _notice = ex.Message; Render(); } }
        public async Task PreviewAsync()
        {
            if (!Editable) return;
            try
            {
                _session.SetDraft(new JArray(Operation()), _clearance > 0 ? new[] { "min_clearance:" + _clearance.ToString("0.###", CultureInfo.InvariantCulture) + "mm" } : null);
                await _session.PreviewAsync();
            }
            catch (Exception ex) { if (_session.CanEdit) _session.RejectDraft(ex.Message); _notice = ex.Message; }
            Render();
        }
        private void Changed()
        {
            if (_session.Status == DesignStatus.PreviewReady && Active && _rendered != _session.Preview?.PlanId)
            {
                try { _preview.Show(_session.Preview); _rendered = _session.Preview.PlanId; _session.ConfirmRendered(_rendered); }
                catch (Exception ex) { _notice = "Anteprima non visualizzabile: " + ex.Message; }
            }
            if (_session.Preview == null) { _preview?.Clear(); _rendered = null; }
            Render();
        }
        public async Task ApplyAsync()
        {
            if (_session?.CanApply != true) return;
            if (_preview?.IsShowing != true || _preview.PlanId != _session.Preview?.PlanId)
            { _session.RejectDraft("Anteprima non visibile: ricalcolala prima di applicare."); return; }
            await MutateAsync(() => _session.ApplyAsync());
        }
        private async void Apply() { try { await ApplyAsync(); } catch (Exception ex) { _notice = ex.Message; Render(); } }
        private async void History(bool redo) { try { await MutateAsync(() => _session.ApplyHistoryAsync(_history, redo)); } catch (Exception ex) { _notice = ex.Message; Render(); } }
        private async Task MutateAsync(Func<Task> mutation)
        {
            if (!Editable || _mutations > 0) return;
            var owner = _session; _mutations++;
            try { await mutation(); } finally { _mutations--; }
            if (this == null || owner != _session) return;
            if (!owner.CommitOutcomeUnknown && owner.Status == DesignStatus.RefreshRequired) await RefreshAsync(false);
            Render();
        }
        private async Task RefreshAsync(bool reviewed)
        {
            if (!_online || _backend == null || _mutations > 0) return;
            var backend = _backend; var owner = _session; int generation = _generation;
            var state = await backend.GetDocumentStateAsync(_reads.Token);
            if (this == null || backend != _backend || owner != _session || generation != _generation) return;
            owner.AcknowledgeRefresh(state, reviewed); _reviewAfterRebind = false; _state = state; Reset(); Load();
        }
        private async void Review() { try { await RefreshAsync(true); } catch (Exception ex) { _notice = ex.Message; } Render(); }
        private async void ActivateSubassembly()
        {
            if (!Editable || RequiresCadReview || _occurrence?.Kind != "assembly" || !(_backend is IInspectionBackend inspection)) return;
            string id = _occurrence.DefinitionId;
            _session.Cancel(); CancelReads(); Reset();
            int generation = _generation;
            var token = _reads.Token;
            _busy = true; Render();
            try
            {
                var documents = await inspection.ListOpenAsync(token);
                if (generation != _generation) return;
                if (!documents.Any(d => d.Id == id)) throw new InvalidOperationException("La definizione non è già aperta in Inventor.");
                await inspection.ActivateOpenAsync(id, token);
                if (generation != _generation) return;
                _context = null; _notice = "Attendo il sottoassieme attivo. Le modifiche alla definizione riguardano tutte le sue istanze.";
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (generation == _generation) _notice = ex.Message; }
            finally { if (generation == _generation) { _busy = false; Render(); } }
        }
        private void Cancel() { if (RequiresCadReview) return; _session?.Cancel(); Reset(); Load(); Render(); }
        // ---------------------------------------------------------------- voice surface

        private bool OnToolsPage => _screen != "components" && _screen != "references" && _screen != "constraints"
            && _screen != "compatible" && _screen != "joints" && _screen != "move";
        private bool VoiceEditable => Editable && !RequiresCadReview && !_numeric && _panel != null;

        /// <summary>Same enablement as "Annulla comando", "Annulla/Ripeti modifica XR" and "Applica". Design-only commands stay disabled.</summary>
        public bool IsEnabled(string commandId)
        {
            switch (commandId)
            {
                case CommandIds.CancelDraft: return VoiceEditable;
                case CommandIds.Apply: return VoiceEditable && _session.CanApply;
                case CommandIds.Undo: return VoiceEditable && OnToolsPage && _session.Status == DesignStatus.Empty && _history?.CanUndo == true;
                case CommandIds.Redo: return VoiceEditable && OnToolsPage && _session.Status == DesignStatus.Empty && _history?.CanRedo == true;
                default: return false;
            }
        }

        /// <summary>Runs the command as its button would. Apply only shows a notice: the physical Applica commits.</summary>
        public bool Invoke(string commandId)
        {
            if (!IsEnabled(commandId)) return false;
            switch (commandId)
            {
                case CommandIds.CancelDraft: Cancel(); return true;
                case CommandIds.Undo: History(false); return true;
                case CommandIds.Redo: History(true); return true;
                case CommandIds.Apply:
                    _notice = "Anteprima verificata. Conferma premendo Applica sul pannello.";
                    Render(); return true;
                default: return false;
            }
        }

        private void Page(string screen) { _screen = screen; _page = 0; Render(); }
        private void Number(string label, double initial, Action<double> done)
        {
            int generation = _generation; _numeric = true;
            void Ask() => _panel.PromptText(label, "Valore CAD esatto (mm o gradi).", initial.ToString(CultureInfo.InvariantCulture), text =>
            {
                if (generation != _generation) return;
                if (!double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || double.IsNaN(value) || double.IsInfinity(value)) { Ask(); return; }
                _numeric = false; InvalidateDraft(); done(value); Draw(); Render();
            }, () => { _numeric = false; Render(); });
            Ask();
        }
        private void Render()
        {
            if (!Active || _numeric || _panel == null) return;
            var actions = new List<(string, Action)>();
            string body = !_online ? "Offline — Applica non disponibile." : _kind != "assembly" ? "Attiva un assieme dal Browser." :
                "Trigger destro: seleziona. Grip destro: solo vista.\nPer muovere: seleziona → CAD Move → pezzo o asse giallo → Grip poi Trigger destro.\n";
            body += "\n" + (_occurrence?.Name ?? "Nessun componente") + " • DOF: " + (_occurrence?.TotalDof?.ToString() ?? "?");
            if (_occurrence?.Grounded == true) body += " • Grounded";
            if (!string.IsNullOrEmpty(_occurrence?.UnavailableReason)) body += "\nComponente non modificabile: " + _occurrence.UnavailableReason;
            if (_occurrence != null && !_occurrence.DofComplete) body += "\nLibertà non disponibili: CAD Move disabilitato.";
            if (_occurrence?.Kind == "assembly") body += "\nAttivare la definizione per modificarne i figli; gli effetti riguardano tutte le istanze.";
            if (_a != null) body += "\nA: " + _a.Name;
            if (_b != null) body += "   B: " + _b.Name;
            if (_type != null) body += "\n" + _type + " • valore " + _value.ToString("0.###", CultureInfo.InvariantCulture);
            if (_busy) body += "\nLettura Inventor…";
            if (_dragging) body += "\nCAD MOVE ARMED";
            body += "\n" + _notice;
            if (!string.IsNullOrEmpty(_session?.Error))
            {
                if (_session.Error == "Bozza modificata: calcola una nuova anteprima.") body += "\n" + _session.Error;
                else
                {
                    body += "\nComando non completato. Controlla geometrie e parametri; Applica richiede una nuova anteprima.";
                    actions.Add(("Dettagli errore", () => { _panel.ShowMessage("Dettagli", _session.Error); _panel.SetActions(("Indietro", Render)); }));
                }
            }
            if (RequiresCadReview)
            { body += "\nVerifica il risultato in Inventor prima di continuare."; if (_mutations == 0) actions.Add(("Ho controllato il CAD", Review)); }
            else if (Editable)
            {
                if (_screen == "components")
                {
                    foreach (var occurrence in _context.Occurrences.Skip(_page * 6).Take(6))
                    { var item = occurrence; actions.Add((item.Name, async () => { try { await SelectOccurrenceAsync(item.Id); Page("tools"); } catch (Exception ex) { _notice = ex.Message; Render(); } })); }
                    More(actions, _context.Occurrences.Count);
                }
                else if (_screen == "references")
                {
                    actions.Add((_pickEdges ? "Raggio: spigoli → facce" : "Raggio: facce → spigoli", () => { _pickEdges = !_pickEdges; Render(); }));
                    foreach (var reference in _context.References.Where(r => r.Available).Skip(_page * 6).Take(6))
                    { var item = reference; actions.Add((item.Name + " • " + item.Geometry.Replace("k", ""), () => { ChooseReference(item); if (_b == null) Page("tools"); })); }
                    More(actions, _context.References.Count(r => r.Available));
                }
                else if (_screen == "constraints" || _screen == "compatible")
                {
                    var types = _screen == "compatible" ? AssemblyOperations.CompatibleConstraints(_a, _b) : new[] { "mate", "flush", "mate_axis", "insert", "angle", "tangent" };
                    foreach (var type in types) { var item = type; actions.Add((item, () => ChooseConstraint(item))); }
                    actions.Add(("Joint", () => Page("joints")));
                }
                else if (_screen == "joints")
                { foreach (var type in AssemblyOperations.JointTypes) { var item = type; actions.Add((item == "slide" ? "Slider" : item, () => ChooseJoint(item))); } }
                else if (_screen == "move")
                {
                    body += "\nPunta il pezzo selezionato o l'asse giallo; tieni Grip destro, premi Trigger destro, muovi e rilascia. Applica solo dopo l'anteprima.";
                    body += "\nΔ mm: " + _translation.X.ToString("0.##") + ", " + _translation.Y.ToString("0.##") + ", " + _translation.Z.ToString("0.##") + " • " + _angle.ToString("0.##") + "°";
                    actions.Add((_rotating ? "Rotazione → Traslazione" : "Traslazione → Rotazione", () => { _rotating = !_rotating; _axisIndex = 0; _angle = 0; InvalidateDraft(); Draw(); Render(); }));
                    for (int i = 0; i < Axes.Count; i++) { int index = i; actions.Add(("Asse " + (i + 1), () => { _axisIndex = index; _angle = 0; InvalidateDraft(); Draw(); Render(); })); }
                    if (Axes.Count > _axisIndex) actions.Add((_rotating ? "Angolo preciso" : "Spostamento preciso", () => Number(_rotating ? "Gradi" : "Millimetri lungo asse", _rotating ? _angle : _translation.Dot(Axes[_axisIndex]), v => { if (_rotating) _angle = v; else _translation = Axes[_axisIndex] * v; Preview(); })));
                    actions.Add(("Anteprima", Preview));
                }
                else
                {
                    actions.Add(("Componenti", () => { Page("components"); CancelReads(); Load(); }));
                    if (_occurrence != null) actions.Add(("Facce / spigoli", async () =>
                    {
                        string id = _occurrence.Id;
                        try { await SelectOccurrenceAsync(id); if (_occurrence?.Id == id) Page("references"); }
                        catch (Exception ex) { _notice = ex.Message; Render(); }
                    }));
                    if (_occurrence?.CanMove == true) actions.Add(("CAD Move", BeginMove));
                    if (_occurrence?.Kind == "assembly" && _backend is IInspectionBackend) actions.Add(("Attiva questo assieme", ActivateSubassembly));
                    actions.Add(("Vincolo", () => Page("constraints"))); actions.Add(("Joint", () => Page("joints")));
                    if (_command != null && _command != "assembly_move")
                    {
                        actions.Add((_type == "angle" ? "Angolo" : "Offset / Gap", () => Number(_type == "angle" ? "Gradi" : "Millimetri", _value, v => { _value = v; if (_b != null) Preview(); })));
                        actions.Add(("Inverti direzione", () => { _opposed = !_opposed; _flipOrigin = !_flipOrigin; InvalidateDraft(); Preview(); }));
                        actions.Add((_command == "assembly_joint" ? "Inverti allineamento" : "Tangente interna/esterna", () => { _flipAlignment = !_flipAlignment; _inside = !_inside; InvalidateDraft(); Preview(); }));
                    }
                    if (_session.Status == DesignStatus.Empty && _history?.CanUndo == true) actions.Add(("Annulla modifica XR", () => History(false)));
                    if (_session.Status == DesignStatus.Empty && _history?.CanRedo == true) actions.Add(("Ripeti modifica XR", () => History(true)));
                }
                if (_session.CanApply) actions.Add(("Applica", Apply));
                actions.Add(("Clearance " + _clearance.ToString("0.###") + " mm", () => Number("Clearance minima mm (0–10000)", _clearance, v =>
                { if (v < 0 || v > 10000 || Math.Abs(v - Math.Round(v, 3)) > 1e-9) { _notice = "Clearance tra 0 e 10000 mm, massimo tre decimali."; return; } _clearance = v; if (_command != null) Preview(); })));
                actions.Add(("Annulla comando", Cancel));
                if (_screen != "tools") actions.Add(("Strumenti", () => Page("tools")));
                body += "\nPreview: rebuild, vincoli/joint, nessuna interferenza finale. Contatto ammesso.";
            }
            actions.Add((_pinned ? "Sblocca pannello" : "Pin pannello", () => { _pinned = !_pinned; Render(); }));
            actions.Add(("Chiudi Assembly", Close));
            _panel.ShowMessage("ASSEMBLY", body); _panel.SetActions(actions.ToArray());
        }
        private void More(List<(string, Action)> actions, int total)
        { if (_page > 0) actions.Add(("Precedenti", () => { _page--; Render(); })); if ((_page + 1) * 6 < total) actions.Add(("Successivi", () => { _page++; Render(); })); }
        private void Draw() => _visuals?.Show(_occurrence, _a, _b, _command == "assembly_move", _rotating, _axisIndex);
        private void Update()
        {
            if (!Active || _ray?.Origin == null) return;
            bool tracked = OVRInput.IsControllerConnected(_ray.Controller) && OVRInput.GetControllerPositionTracked(_ray.Controller) && OVRInput.GetControllerOrientationTracked(_ray.Controller);
            bool grip = tracked && OVRInput.Get(OVRInput.Button.PrimaryHandTrigger, _ray.Controller);
            bool trigger = tracked && OVRInput.Get(OVRInput.Button.PrimaryIndexTrigger, _ray.Controller);
            bool ui = EventSystem.current?.currentInputModule is ControllerUiInputModule input && input.CurrentHit.isValid;
            ProcessControllerFrame(tracked, grip, trigger,
                OVRInput.GetDown(OVRInput.Button.PrimaryHandTrigger, _ray.Controller),
                OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, _ray.Controller), ui);
        }
        private void ProcessControllerFrame(bool tracked, bool grip, bool trigger, bool gripDown, bool triggerDown, bool ui)
        {
            if (!Active || _ray?.Origin == null) return;
            if (!tracked) { if (_dragging) { _dragging = false; InvalidateDraft(); _notice = "Tracking perso: ripeti il gesto intenzionale."; Render(); } _grab = null; return; }
            if (_dragging)
            {
                if (!grip || !trigger) { _dragging = false; Preview(); return; }
                var axis = Axes[_axisIndex];
                if (_rotating)
                {
                    var worldAxis = _view.transform.TransformDirection(CadCoordinates.ToLocal(axis)).normalized;
                    // CAD->Unity reflects X, so axial rotation changes sign.
                    _angle = _startAngle - AssemblyManipulation.TwistDegrees(_dragRotation, _ray.Origin.rotation, worldAxis);
                }
                else _translation = _startTranslation + axis * ((CadCoordinates.FromWorld(_view.transform, _ray.Origin.position) - _dragStart).Dot(axis));
                if (Time.unscaledTime >= _nextRender) { _nextRender = Time.unscaledTime + 0.15f; Render(); }
                return;
            }
            if (Editable && grip && trigger && !ui && _command == "assembly_move" && Axes.Count > _axisIndex
                && (triggerDown || gripDown)
                && HitMoveTarget(new Ray(_ray.Origin.position, _ray.Origin.forward)))
            {
                _grab = null; _dragging = true; _dragStart = CadCoordinates.FromWorld(_view.transform, _ray.Origin.position);
                _dragRotation = _ray.Origin.rotation; _startTranslation = _translation; _startAngle = _angle; InvalidateDraft(); Render(); return;
            }
            if (!grip || trigger) { _grab = null; return; }
            if (gripDown)
            {
                if (ui) { if (!_pinned) _grab = _panel.transform; }
                else if (CadRaycaster.TryPick(new Ray(_ray.Origin.position, _ray.Origin.forward), 20, out _, out _, out _)) _grab = _view.transform;
                if (_grab != null) { _grabPosition = _ray.Origin.InverseTransformPoint(_grab.position); _grabRotation = Quaternion.Inverse(_ray.Origin.rotation) * _grab.rotation; }
            }
            if (_grab != null) _grab.SetPositionAndRotation(_ray.Origin.TransformPoint(_grabPosition), _ray.Origin.rotation * _grabRotation);
        }
        private bool HitMoveTarget(Ray ray)
        {
            if (_visuals.HitHandle(ray)) return true;
            return _occurrence != null && CadRaycaster.TryPick(ray, 20, out var body, out _, out _)
                && body.Instance?.OccurrenceId == _occurrence.Id;
        }
        private void OnDestroy()
        {
            _reads.Cancel(); _reads.Dispose(); if (_session != null) { _session.Changed -= Changed; _session.Dispose(); }
            if (_ray != null) { _ray.Picked -= Pick; _ray.PickedNothing -= PickEmpty; }
            if (_panel != null) Release(_panel.gameObject); if (_visuals != null) Release(_visuals); if (_preview != null) Release(_preview);
        }
        private static void Release(UnityEngine.Object item) { if (Application.isPlaying) Destroy(item); else DestroyImmediate(item); }
    }
}
