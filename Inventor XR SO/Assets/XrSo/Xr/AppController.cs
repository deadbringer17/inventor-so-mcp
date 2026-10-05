using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Mcp;
using InventorXrSo.Core.Navigation;
using InventorXrSo.Core.Net;
using InventorXrSo.Core.Pairing;
using InventorXrSo.Core.Selection;
using InventorXrSo.Core.Session;
using InventorXrSo.Core.Ui;
using InventorXrSo.Unity.Net;
using InventorXrSo.Unity.Pairing;
using InventorXrSo.Unity.Scene;
using InventorXrSo.Unity.Ui;
using InventorXrSo.Xr.Voice;
using UnityEngine;

namespace InventorXrSo.Xr
{
    /// <summary>
    /// Wires M1 together: pairing, session, Home, scene, selection. Everything runs on the main thread
    /// (awaits resume on Unity's synchronization context).
    /// </summary>
    public sealed class AppController : MonoBehaviour
    {
        [SerializeField] private CadSceneView sceneView;
        [SerializeField] private SelectionVisuals selectionVisuals;
        [SerializeField] private ControllerRay ray;
        [SerializeField] private EnvironmentModeController environment;
        [SerializeField] private Transform head;
        [SerializeField] private QrScanner qrScanner;

        private HomePanel _home;
        private HudView _badge;
        private ActionCatalog _catalog;
        private UiShell _shell;
        private InventorXrSo.Xr.Input.XrInput _input;
        private Workbench _workbench;
        private SketchSheetView _sheet;
        private ICredentialStore _store;
        private PairedServer _server;
        private SessionController _session;
        private SelectionService _selection;
        private CancellationTokenSource _run;
        private bool _inSession;
        private bool _placed;
        private string _documentId;
        private CancellationTokenSource _picking;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private bool _selecting;
        private InspectWorkspace _inspect;
        private DesignWorkspace _design;
        private AssemblyWorkspace _assembly;
        private LamieraWorkspace _lamiera;
        private VoiceRig _voice;
        private WorkspaceVoiceTarget _voiceTarget;
        private DocumentActions _document;
        private ViewActions _view;
        private ControllerLegend _legend;
        private SelectionLabel _selectionLabel;
        private SelectionLabelDriver _labelDriver;
        private string _assemblyHighlightId;
        // Cache of the face ordinal shown by the label (the lookup walks the bodies' face maps).
        private InventorXrSo.Core.Selection.Selection _labelFaceSelection;
        private int _labelFaceOrdinal;
        private InventorBackend _backend;
        private IReadOnlyList<OpenDocument> _openDocuments = new OpenDocument[0];

        private ContextWorkspaceSwitcher _contextSwitcher;
        private LoadedScene _currentScene;
        // M9 assieme fantasma: ultima scena caricata per documento (serve la scena del padre prima che quella nuova la sostituisca).
        private readonly Dictionary<string, LoadedScene> _sceneByDocument = new Dictionary<string, LoadedScene>();
        private GhostContext _ghost;
        private string _ghostKey;
        private BackNavigator _backNav;
        private readonly DirtyTracker _dirty = new DirtyTracker();

        /// <summary>Pila di navigazione Assieme › Sub › Parte, guidata dal documento attivo (ContextRouter).</summary>
        public NavigationStack Navigation { get; } = new NavigationStack();

        /// <summary>Contesto del documento attivo (Assieme, Parte, Lamiera); null senza documento supportato.</summary>
        public DocContext? Context => _contextSwitcher?.Context;

        public void Configure(CadSceneView view, SelectionVisuals visuals, ControllerRay controllerRay, EnvironmentModeController env, Transform centerEye, QrScanner scanner)
        {
            sceneView = view;
            selectionVisuals = visuals;
            ray = controllerRay;
            environment = env;
            head = centerEye;
            qrScanner = scanner;
        }

        private void Start()
        {
            _home = HomePanel.Create(null);
            XrUi.MakeInteractive(_home.Canvas, head.GetComponent<Camera>());
            _inspect = gameObject.AddComponent<InspectWorkspace>();
            var left = head.parent.Find("LeftHandAnchor/LeftControllerAnchor") ?? head.parent.Find("LeftHandAnchor");
            _inspect.Initialize(sceneView, selectionVisuals, ray, head, environment);
            _design = gameObject.AddComponent<DesignWorkspace>();
            _design.Initialize(sceneView, selectionVisuals, ray, head);
            _assembly = gameObject.AddComponent<AssemblyWorkspace>();
            _assembly.Initialize(sceneView, ray, head);
            _lamiera = gameObject.AddComponent<LamieraWorkspace>();
            _lamiera.Initialize(sceneView, selectionVisuals, ray, head);
            // One shared CAD guard: an uncertain commit in any authoring workspace blocks entering the others.
            // Il fantasma e figlio della radice della scena: Workbench (Adatta/Ricentra) lo sposta insieme alla parte.
            _ghost = new GameObject("GhostContextHost").AddComponent<GhostContext>();
            _ghost.transform.SetParent(sceneView.transform, false);
            Navigation.Changed += UpdateGhost;
            _contextSwitcher = new ContextWorkspaceSwitcher(Navigation, OpenContext, CloseAuthoring,
                text => _badge?.Flash(text, 6f), () => _inSession);
            _backNav = new BackNavigator(Navigation,
                () => _design.RequiresCadReview || _assembly.RequiresCadReview || _lamiera.RequiresCadReview,
                () => _session != null && _session.Status == SessionStatus.Online,
                () => _backend, () => _run?.Token ?? CancellationToken.None,
                text => _badge?.Flash(text, 6f), ResetNavigationOnActive);
            Navigation.Changed += ApplyDirty;
            _design.CanEnter = () => !_assembly.RequiresCadReview && !_lamiera.RequiresCadReview;
            _assembly.CanEnter = () => !_design.RequiresCadReview && !_lamiera.RequiresCadReview;
            _lamiera.CanEnter = () => !_design.RequiresCadReview && !_assembly.RequiresCadReview;
            _document = new DocumentActions(Navigation, GoBack, SaveDocument, RecenterWorkbench, CalibrateDesk,
                LeaveSession, LeaveSession, () => _inSession,
                () => _design.CanEnter() && _lamiera.CanEnter() && _assembly.CanEnter(),
                () => _openDocuments, JumpToDocument);
            _catalog = new ActionCatalog(_document);
            _document.Changed = _catalog.NotifyChanged;
            _shell = UiShell.Create(left, head, _catalog);
            _document.Attach(_shell.Palette);
            XrUi.MakeInteractive(_shell.Palette.Canvas, head.GetComponent<Camera>());
            XrUi.MakeInteractive(_shell.CommitBar.Canvas, head.GetComponent<Camera>());
            _badge = _shell.Hud;
            _badge.gameObject.SetActive(false);
            // Progettazione draws on the shell (palette, commit bar, chip, ring) and puts the model on the work plane.
            _workbench = gameObject.AddComponent<Workbench>();
            _sheet = gameObject.AddComponent<SketchSheetView>();
            _sheet.Bind(sceneView.transform);
            _input = gameObject.AddComponent<InventorXrSo.Xr.Input.XrInput>();
            _design.Attach(_shell, _workbench, _sheet, _input);
            // M9: X at rest is tap (hint) / hold 1 s (Torna); away from rest it stays the Back chain.
            _input.RestingProbe = AtRest;
            // M9: every key is gated by InputMap in the current state; stick right at rest rotates the view by 15 degrees.
            _input.Dispatcher.StateProbe = CurrentInputState;
            _input.Dispatcher.Invoked += OnInputInvoked;
            _input.BackTapped += _backNav.Tapped;
            _input.BackHeld += GoBack;
            _lamiera.Attach(_shell, _workbench, _sheet, _input);
            _assembly.Attach(_shell, _workbench, _sheet, _input);
            // Ispeziona is the default workspace: it draws on the palette whenever no authoring workspace is active.
            _inspect.Attach(_shell, _input);
            // M9: Ispeziona is a tool provider of every context (Misura, Sezione, + Visibilità and Verifica in Assieme); it pauses only
            // during a handle capture or a CAD review, and the authoring workspace keeps the grips, X and the trigger it needs.
            _inspect.SuspendProbe = () => _design.HandleCaptured || _assembly.HandleCaptured || _lamiera.HandleCaptured
                || _design.RequiresCadReview || _assembly.RequiresCadReview || _lamiera.RequiresCadReview;
            _inspect.AuthoringOwnsView = () => _design.Active || _assembly.Active || _lamiera.Active;
            _inspect.ExternalSelection = () => _assembly.Active ? _assembly.SelectedOccurrenceId ?? "" : null;
            Func<bool> measuring = () => _inspect.Active && (_inspect.Measuring);
            _design.PickSuppressed = measuring; _assembly.PickSuppressed = measuring; _lamiera.PickSuppressed = measuring;
            _view = new ViewActions(() => _design.Active || _assembly.Active || _lamiera.Active || _inspect.Active, FitActive) { Changed = _catalog.NotifyChanged };
            _catalog.ContextProbe = ActiveContext;
            _catalog.AddShared(_inspect);
            _catalog.AddShared(_view);
            // M9 legenda 3D: one label per key active in the state, on the controller models; follows the Vista preference.
            var right = head.parent.Find("RightHandAnchor/RightControllerAnchor") ?? head.parent.Find("RightHandAnchor");
            _legend = ControllerLegend.Create(head, left, right);
            _legend.Enabled = _view.LegendOn;
            _legend.Shown = false;
            _legend.TriggerProgress = () => _assembly.Active ? _assembly.DoubleTriggerProgress : 0f;
            _legend.BackProgress = () => _input.BackHoldProgress;
            _view.LegendChanged += on => _legend.Enabled = on;
            // Selection feedback: the label names what is selected (name, kind, level) above the highlight; hover rims follow the ray.
            _selectionLabel = SelectionLabel.Create(head);
            _labelDriver = new SelectionLabelDriver(_selectionLabel, Navigation, ResolveSelectionTarget);
            sceneView.Rebuilt += () => _assemblyHighlightId = null;
            ray.Hovered += OnHovered;
            _inspect.HudMessage += text => _badge.Flash(text, 6f);
            _catalog.SetActive(_inspect);
            _design.HudMessage += text => _badge.Flash(text, 6f);
            _design.ActiveChanged += active =>
            {
                if (active) _catalog.SetActive(_design);
                else if (ReferenceEquals(_catalog.Active, _design)) _catalog.SetActive(_inspect);
            };
            _design.Closed += () => { if (_inSession) Place(); };
            _assembly.HudMessage += text => _badge.Flash(text, 6f);
            _assembly.ActiveChanged += active =>
            {
                if (active) _catalog.SetActive(_assembly);
                else if (ReferenceEquals(_catalog.Active, _assembly)) _catalog.SetActive(_inspect);
            };
            _assembly.Closed += () => { if (_inSession) Place(); };
            // From the isolated component ("Apri"): its document is already active; the router expects the entry and the
            // document-change event pushes the level and switches the workspace.
            _assembly.EntryRequested += (documentId, occurrenceId, pose, sheetMetal) =>
                _contextSwitcher.ExpectEntry(documentId, occurrenceId, pose, sheetMetal);
            _lamiera.HudMessage += text => _badge.Flash(text, 6f);
            _lamiera.ActiveChanged += active =>
            {
                if (active) _catalog.SetActive(_lamiera);
                else if (ReferenceEquals(_catalog.Active, _lamiera)) _catalog.SetActive(_inspect);
            };
            _lamiera.Closed += () => { if (_inSession) Place(); };
            // Back, Fit, Recenter, step and zoom belong to the active authoring workspace (it subscribes through Attach); the palette owns the tabs.
            _input.TabDelta += _shell.Palette.SelectTab;
            // Sheet-metal detection is asynchronous: when it flips, the active document is routed again.
            _lamiera.PrimaryChanged += _ => RouteCurrentScene();
            // Voice enablement follows the active workspace: re-evaluate whenever the mode changes.
            _design.ActiveChanged += _ => NotifyVoiceModeChanged();
            _design.ActiveChanged += _ => _catalog.NotifyChanged();
            _assembly.ActiveChanged += _ => _catalog.NotifyChanged();
            _lamiera.ActiveChanged += _ => _catalog.NotifyChanged();
            _assembly.ActiveChanged += _ => NotifyVoiceModeChanged();
            _lamiera.ActiveChanged += _ => NotifyVoiceModeChanged();
            ray.Picked += OnPicked;
            ray.PickedNothing += OnPickedNothing;
            ray.CanPick = false;
            sceneView.gameObject.SetActive(false);
            _store = CredentialStores.Create();
            _server = _store.Load();
            ShowHome();
            if (_server == null) ShowPairing(UiText.NotPaired, UiText.NotPairedBody);
            else StartSession();
        }

        private void Update()
        {
            if (_inSession && OVRInput.GetDown(OVRInput.Button.Start)) LeaveSession();
            if (_legend != null) _legend.SetState(_input.Dispatcher.State);
            SyncAssemblyHighlight();
            _labelDriver?.Tick();
        }

        private void OnHovered(CadBody body)
        {
            if (!_inSession) body = null;
            selectionVisuals.Hover(body);
        }

        /// <summary>
        /// Assieme selects occurrences through its own context, not through SelectionService, so nothing drew the selected component:
        /// follow the workspace's selection here. Display only; the selection logic is unchanged.
        /// </summary>
        private void SyncAssemblyHighlight()
        {
            string id = _inSession && _assembly != null && _assembly.Active ? _assembly.SelectedOccurrenceId : null;
            if (id == null)
            {
                if (_assemblyHighlightId != null) { selectionVisuals.Clear(); _assemblyHighlightId = null; }
                return;
            }
            if (id == _assemblyHighlightId) return;
            _assemblyHighlightId = id;
            selectionVisuals.ShowOccurrences(sceneView.Instances.Where(i => _assembly.IsPartOfSelection(i.OccurrenceId)).Select(i => i.OccurrenceId).ToList());
        }

        private SelectionTarget ResolveSelectionTarget()
        {
            var target = new SelectionTarget();
            if (!_inSession) return target;
            if (_assembly != null && _assembly.Active)
            {
                if (_assembly.SelectedOccurrenceId == null) return target;
                target.Kind = _assembly.SelectedOccurrenceKind == "assembly" ? SelectionLabelKind.Subassembly : SelectionLabelKind.Part;
                target.Name = _assembly.SelectedOccurrenceName;
                target.HasBounds = selectionVisuals.TryGetBounds(out target.Bounds);
                return target;
            }
            var geometry = _design != null && _design.Active ? _design.Geometry : _lamiera != null && _lamiera.Active ? _lamiera.Geometry : null;
            if (geometry != null && geometry.TryGetSingleEdgeBounds(out var edgeBounds))
            {
                target.Kind = SelectionLabelKind.Edge;
                target.Name = Navigation.Top?.Name ?? _session?.Scene?.Graph.Root.Name;
                target.HasBounds = true; target.Bounds = edgeBounds;
                return target;
            }
            var current = selectionVisuals.Current;
            if (current != null && current.Kind == InventorXrSo.Core.Selection.SelectionKind.Face)
            {
                if (!ReferenceEquals(current, _labelFaceSelection))
                {
                    _labelFaceSelection = current; _labelFaceOrdinal = 0;
                    var instance = sceneView.Find(current.OccurrenceId);
                    if (instance != null)
                        foreach (var body in instance.Bodies)
                        {
                            var range = body.Primitive.FaceMap.Find(current.FaceId);
                            if (range != null) { _labelFaceOrdinal = range.Ordinal; break; }
                        }
                }
                target.Kind = SelectionLabelKind.Face;
                target.FaceOrdinal = _labelFaceOrdinal;
                target.Name = sceneView.Find(current.OccurrenceId)?.name ?? Navigation.Top?.Name;
                target.HasBounds = selectionVisuals.TryGetBounds(out target.Bounds);
                return target;
            }
            if (!selectionVisuals.HasHighlight) return target;
            // Ispeziona: the browser node that was picked (a sub-assembly highlights all its parts).
            string nodeName = _inspect?.SelectedNodeName;
            if (nodeName != null)
            {
                target.Kind = _inspect.SelectedNodeKind == "assembly" ? SelectionLabelKind.Subassembly : SelectionLabelKind.Part;
                target.Name = nodeName;
            }
            else if (current != null && current.Kind == InventorXrSo.Core.Selection.SelectionKind.Occurrence)
            {
                target.Kind = SelectionLabelKind.Part;
                target.Name = sceneView.Find(current.OccurrenceId)?.name;
            }
            else return target;
            target.HasBounds = selectionVisuals.TryGetBounds(out target.Bounds);
            return target;
        }

        private void OnDestroy()
        {
            _lifetime.Cancel();
            StopSession();
            qrScanner?.Stop();
            if (ray != null) { ray.Picked -= OnPicked; ray.PickedNothing -= OnPickedNothing; }
            if (_home != null) Destroy(_home.gameObject);
            if (_shell != null) Destroy(_shell.gameObject);
            if (_legend != null) Destroy(_legend.gameObject);
            if (_selectionLabel != null) Destroy(_selectionLabel.gameObject);
            if (ray != null) ray.Hovered -= OnHovered;
            _lifetime.Dispose();
        }

        private void StopSession()
        {
            if (_voice != null) { Destroy(_voice.gameObject); _voice = null; }
            _inspect?.Bind(null, null);
            _design?.Bind(null);
            _assembly?.Bind(null);
            _lamiera?.Bind(null, null);
            _currentScene = null;
            _sceneByDocument.Clear();
            _dirty.Clear();
            _contextSwitcher?.Clear();
            ClearGhost();
            if (_session != null)
            {
                _session.StatusChanged -= OnStatusChanged; _session.SceneLoaded -= OnSceneLoaded;
                _session.DocumentStateChanged -= OnDocumentStateForDirty;
                _session.DocumentStateChanged -= _inspect.SetDocumentState;
                _session.DocumentStateChanged -= _design.SetDocumentState;
                _session.DocumentStateChanged -= _assembly.SetDocumentState;
                _session.DocumentStateChanged -= _lamiera.SetDocumentState;
            }
            if (_selection != null) _selection.Changed -= selectionVisuals.Show;
            _session = null;
            _backend = null;
            _openDocuments = new OpenDocument[0];
            _selection = null;
            _picking?.Cancel();
            _picking?.Dispose();
            _picking = null;
            _run?.Cancel();
            _run?.Dispose();
            _run = null;
            _placed = false;
            _documentId = null;
        }

        private void OnStatusChanged(SessionStatus status)
        {
            Debug.Log($"[XrSession] status={status} error={_session?.LastError}");
            _inspect.SetOnline(status == SessionStatus.Online);
            _catalog?.NotifyChanged();
            _design.SetOnline(status == SessionStatus.Online);
            _assembly.SetOnline(status == SessionStatus.Online);
            _lamiera.SetOnline(status == SessionStatus.Online);
            if (status != SessionStatus.Online) _voice?.NotifyDisconnected();
            RefreshHome();
        }

        // --- pairing ---

        private void ShowPairing(string title, string body)
        {
            ShowHome();
            _home.ShowMessage(title, body);
            _home.SetActions((UiText.PairWithQr, StartQr), (UiText.PairWithCode, AskAddress));
        }

        private PairingClient Pairing() => new PairingClient(trust => new UnityHttpTransport(trust));

        private void StartQr()
        {
            _home.ShowMessage(UiText.ScanQr, "");
            _home.SetActions((UiText.Cancel, () => { qrScanner.Stop(); ShowPairing(UiText.NotPaired, UiText.NotPairedBody); }));
            qrScanner.Scan(text =>
            {
                PairingPayload payload;
                try { payload = PairingPayload.Parse(text); }
                catch (FormatException) { ShowPairing(UiText.QrInvalid, UiText.NotPairedBody); return; }
                Complete(Pairing().PairWithQrAsync(payload, SystemInfo.deviceName, _lifetime.Token));
            }, error => ShowPairing(error, UiText.NotPairedBody));
        }

        private void AskAddress()
        {
            _home.PromptText(UiText.EnterPcAddress, UiText.EnterPcAddressHint, "192.168.", async text =>
            {
                if (!PairingAddress.TryParse(text, out var host, out var port)) { ShowPairing(UiText.BadAddress, UiText.NotPairedBody); return; }
                _home.ShowMessage(UiText.Pairing, host + ":" + port);
                try
                {
                    var sha = await Pairing().ProbeFingerprintAsync(host, port, _lifetime.Token);
                    if (_lifetime.IsCancellationRequested) return;
                    _home.ShowMessage(UiText.CheckFingerprint, UiText.CheckFingerprintBody + CertificatePin.Display(sha));
                    _home.SetActions((UiText.FingerprintMatches, () => AskCode(host, port, sha)),
                                     (UiText.Cancel, () => ShowPairing(UiText.NotPaired, UiText.NotPairedBody)));
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) { if (!_lifetime.IsCancellationRequested) ShowPairing(UiText.Error(ex), UiText.NotPairedBody); }
            }, () => ShowPairing(UiText.NotPaired, UiText.NotPairedBody));
        }

        private void AskCode(string host, int port, string sha)
        {
            _home.PromptText(UiText.EnterCode, UiText.EnterCodeHint, "", code =>
                {
                    if (code.Length != 6 || !System.Text.RegularExpressions.Regex.IsMatch(code, "^[0-9]{6}$"))
                    { AskCode(host, port, sha); return; }
                    Complete(Pairing().PairAsync(host, port, code, ServerTrust.Pinned(sha), SystemInfo.deviceName, _lifetime.Token));
                },
                () => ShowPairing(UiText.NotPaired, UiText.NotPairedBody));
        }

        private async void Complete(Task<PairedServer> pairing)
        {
            _home.ShowMessage(UiText.Pairing, "");
            _home.SetActions();
            try
            {
                _server = await pairing;
                if (_lifetime.IsCancellationRequested) return;
                _store.Save(_server);
                StartSession();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (!_lifetime.IsCancellationRequested) ShowPairing(UiText.Error(ex), UiText.NotPairedBody); }
        }

        private void Forget()
        {
            StopSession();
            _store.Clear();
            _server = null;
            sceneView.Show(null);
            ShowPairing(UiText.NotPaired, UiText.NotPairedBody);
        }

        // --- session ---

        private void StartSession()
        {
            StopSession();
            _run = new CancellationTokenSource();
            var transport = new UnityHttpTransport(ServerTrust.Pinned(_server.CertSha256));
            var backend = new InventorBackend(transport, _server,
                new FileAssetCache(Path.Combine(Application.persistentDataPath, "assets")));
            _backend = backend;
            _session = new SessionController(backend, new TaskDelay());
            _selection = new SelectionService(backend);
            _inspect.Bind(backend, _selection);
            _design.Bind(backend);
            _assembly.Bind(backend);
            _lamiera.Bind(backend, backend);
            // Voice reuses the backend's transport and the paired server; every workspace exposes its command surface.
            _voiceTarget = WorkspaceVoiceTarget.ForWorkspaces(_lamiera, _design, _assembly, _inspect, _catalog);
            _voiceTarget.InSession = _inSession;
            _voice = VoiceRig.Create(head.parent, head, head.GetComponent<Camera>(), transport, _server,
                _voiceTarget, ray.Controller);
            _selection.Changed += selectionVisuals.Show;
            _session.StatusChanged += OnStatusChanged;
            _session.SceneLoaded += OnSceneLoaded;
            _session.DocumentStateChanged += OnDocumentStateForDirty;
            _session.DocumentStateChanged += _inspect.SetDocumentState;
            _session.DocumentStateChanged += _design.SetDocumentState;
            _session.DocumentStateChanged += _assembly.SetDocumentState;
            _session.DocumentStateChanged += _lamiera.SetDocumentState;
            RefreshHome();
            RunSession(_session, _run.Token);
        }

        private async void RunSession(SessionController session, CancellationToken ct)
        {
            try { await session.RunAsync(ct); }
            catch (OperationCanceledException) { return; }
            if (ct.IsCancellationRequested) return;
            if (session != _session || session.Status != SessionStatus.NeedsPairing) return;
            var reason = session.CertificateChanged ? UiText.CertificateChanged : UiText.TokenRevoked;
            Forget();
            ShowPairing(reason, UiText.NotPairedBody);
        }

        private void OnSceneLoaded(LoadedScene scene)
        {
            Debug.Log($"[XrSession] applying document={scene?.Graph.DocumentId} revision={scene?.Graph.VisualRevision}");
            _picking?.Cancel();
            _picking?.Dispose();
            _picking = CancellationTokenSource.CreateLinkedTokenSource(_run.Token);
            _selection.ResetLocal();
            if (_documentId != scene?.Graph.DocumentId) _placed = false;
            _documentId = scene?.Graph.DocumentId;
            sceneView.Show(scene);
            _inspect.SetScene(scene);
            _design.SetScene(scene);
            _assembly.SetScene(scene);
            _lamiera.SetScene(scene);
            _currentScene = scene;
            if (scene != null) _sceneByDocument[scene.Graph.DocumentId] = scene;
            RouteCurrentScene();
            UpdateGhost();
            if (scene != null && _inSession && !_placed) Place();
            RefreshOpenDocuments();
            _design.RefreshWorkbench();
            _lamiera.RefreshWorkbench();
            _assembly.RefreshWorkbench();
            if (scene != null && scene.Omitted.Count > 0) _badge.Flash(scene.Omitted.Count + UiText.Omitted);
            RefreshHome();
        }

        private void RefreshHome()
        {
            if (_session == null) return;
            var status = UiText.Status(_session.Status);
            _badge.SetStatus(status);
            if (_inSession) return;
            var caps = _session.Capabilities;
            var body = UiText.PcLabel + _server.Host + ":" + _server.Port + "  (" + status + ")\n"
                     + UiText.BackendLabel + UiText.VersionLabel(caps?.ServerVersion) + "\n"
                     + UiText.InventorLabel + (caps?.InventorYear?.ToString() ?? "-") + "\n"
                     + UiText.DocumentLabel + (_session.Scene?.Graph.Root.Name ?? "-") + " (" + UiText.DocumentKind(_session.Scene?.Graph.Kind ?? caps?.ActiveDocumentKind) + ")";
            var readinessHint = UiText.ReadinessHint(caps);
            if (!string.IsNullOrEmpty(readinessHint)) body += "\n" + readinessHint;
            if (_session.Scene?.Omitted.Count > 0) body += "\n" + _session.Scene.Omitted.Count + UiText.Omitted;
            _home.ShowMessage(UiText.AppTitle, body);
            if (_session.Status == SessionStatus.Online && _session.Scene != null)
                _home.SetActions((UiText.EnterMixedReality, () => EnterSession(EnvironmentMode.MixedReality)),
                                 (UiText.EnterStudio, () => EnterSession(EnvironmentMode.StudioVr)),
                                 (UiText.ForgetPc, Forget));
            else _home.SetActions((UiText.ForgetPc, Forget));
        }

        private void ShowHome()
        {
            _inSession = false;
            selectionVisuals.ClearHover();
            _selectionLabel?.Hide();
            if (_legend != null) _legend.Shown = false;
            _catalog?.NotifyChanged();
            if (_voiceTarget != null) _voiceTarget.InSession = false;
            _inspect?.SetVisible(false);
            _design?.SetVisible(false);
            _assembly?.SetVisible(false);
            _lamiera?.SetVisible(false);
            _contextSwitcher?.Leave();
            ClearGhost();
            _voice?.NotifyModeChanged();
            ray.CanPick = false;
            sceneView.gameObject.SetActive(false);
            _home.gameObject.SetActive(true);
            _badge.gameObject.SetActive(false);
            var forward = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 1e-6f) forward = Vector3.forward;
            _home.transform.position = head.position + forward * 1.0f;
            _home.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
        }

        private void EnterSession(EnvironmentMode mode)
        {
            environment.Set(mode);
            _inSession = true;
            if (_legend != null) _legend.Shown = true;
            _catalog?.NotifyChanged();
            if (_voiceTarget != null) _voiceTarget.InSession = true;
            _home.gameObject.SetActive(false);
            _badge.gameObject.SetActive(true);
            sceneView.gameObject.SetActive(true);
            _inspect.SetVisible(true);
            _design.SetVisible(true);
            _assembly.SetVisible(true);
            _lamiera.SetVisible(true);
            selectionVisuals.Show(_selection?.Current);
            ray.CanPick = true;
            if (!_placed) Place();
            RefreshHome();
            // The active document decides the workspace (Assieme / Progettazione / Lamiera).
            _contextSwitcher.Sync();
            UpdateGhost();
        }

        // --- assieme fantasma (M9) ---

        private void ClearGhost()
        {
            _ghost?.Clear();
            _ghostKey = null;
        }

        /// <summary>
        /// Mostra il padre diretto del livello in cima alla pila come fantasma, posato con l'inversa dell'occorrenza (vedi GhostPose).
        /// Il fantasma e una fotografia: finche il livello (padre, documento, occorrenza) non cambia non si ricostruisce, quindi non segue
        /// le modifiche alla parte. Pila alla radice, fuori sessione o padre non in cache: nessun fantasma.
        /// </summary>
        private void UpdateGhost()
        {
            if (_ghost == null) return;
            var top = Navigation.Top;
            var parent = Navigation.Parent;
            if (!_inSession || top == null || parent == null || !_sceneByDocument.TryGetValue(parent.DocumentId, out var parentScene))
            {
                ClearGhost();
                PruneSceneCache();
                return;
            }
            PruneSceneCache();
            var key = parent.DocumentId + "|" + top.DocumentId + "|" + top.FromOccurrenceId;
            if (key == _ghostKey && _ghost.IsShowing) return;
            _ghost.Show(parentScene, top.OccurrencePose, parentScene.Graph.Revision, top.FromOccurrenceId);
            _ghostKey = key;
            var note = _ghost.Label + " (rev. " + _ghost.RevisionLabel + ")";
            if (_ghost.Truncated) note += ". Contesto parziale: l'assieme supera i limiti di mesh, mostro solo le definizioni caricate.";
            _badge?.Flash(note, _ghost.Truncated ? 8f : 5f);
        }

        /// <summary>Tiene in cache solo le scene dei livelli della pila e del documento corrente.</summary>
        private void PruneSceneCache()
        {
            if (_sceneByDocument.Count == 0) return;
            var keep = new HashSet<string>(Navigation.Levels.Select(l => l.DocumentId));
            if (_currentScene != null) keep.Add(_currentScene.Graph.DocumentId);
            foreach (var id in _sceneByDocument.Keys.Where(k => !keep.Contains(k)).ToList()) _sceneByDocument.Remove(id);
        }

        // --- contesto guidato dal documento attivo (M9) ---

        /// <summary>DocInfo del documento della scena corrente (tipo da scena, lamiera dal rilevamento primario di Lamiera).</summary>
        private DocInfo CurrentDocInfo()
        {
            var graph = _currentScene?.Graph;
            if (graph == null || string.IsNullOrWhiteSpace(graph.DocumentId)) return null;
            string kind = graph.Kind ?? "";
            bool sheetMetal = kind == "part" && (_lamiera.IsPrimary || _contextSwitcher.SheetMetalHint(graph.DocumentId));
            return new DocInfo(graph.DocumentId, graph.Root?.Name, kind, sheetMetal);
        }

        private void RouteCurrentScene()
        {
            var doc = CurrentDocInfo();
            if (doc == null || _contextSwitcher == null) return;
            _contextSwitcher.OnDocument(doc);
            NotifyVoiceModeChanged();
        }

        /// <summary>Chiude gli altri workspace di authoring e apre quello del contesto; false se il CAD richiede una verifica.</summary>
        private bool OpenContext(DocContext context)
        {
            if (_design.RequiresCadReview || _assembly.RequiresCadReview || _lamiera.RequiresCadReview) return false;
            _design.Close(); _assembly.Close(); _lamiera.Close();
            switch (context)
            {
                case DocContext.Assembly: _assembly.Open(); return _assembly.Active;
                case DocContext.SheetMetal: _lamiera.Open(); return _lamiera.Active;
                default: _design.Open(); return _design.Active;
            }
        }

        /// <summary>Context of the open authoring workspace (it decides the tabs of the palette); null when none is open.</summary>
        private DocContext? ActiveContext()
            => _assembly.Active ? DocContext.Assembly : _lamiera.Active ? DocContext.SheetMetal : _design.Active ? DocContext.Part : (DocContext?)null;

        /// <summary>The shared «Adatta» of the Vista tab: the workspace in use fits its model to the work plane.</summary>
        private void FitActive()
        {
            if (_design.Active) _design.FitView();
            else if (_assembly.Active) _assembly.FitView();
            else if (_lamiera.Active) _lamiera.FitView();
            else _inspect.FitView();
        }

        private void CloseAuthoring() { _design?.Close(); _assembly?.Close(); _lamiera?.Close(); }

        private void NotifyVoiceModeChanged() { _voice?.NotifyModeChanged(); }

        // --- scheda Documento ---

        /// <summary>
        /// Torna al livello superiore: riattiva il documento padre in Inventor; il router riconcilia la pila (Popped) quando
        /// la scena del padre arriva. Non salva. Bloccato con revisione CAD in sospeso.
        /// </summary>
        public void GoBack() { _ = _backNav.GoBackAsync(); }

        /// <summary>Stato dei controller (spec M9 §3): tastierino > chip/maniglia armati > schizzo > componente selezionato > riposo.</summary>
        private InventorXrSo.Core.Input.InputState CurrentInputState()
        {
            bool keypad = false, armed = false, sketch = false, component = false;
            if (_design.Active) { keypad = _design.KeypadOpen; armed = _design.InputArmed; sketch = _design.SketchOpen; }
            else if (_assembly.Active) { keypad = _assembly.KeypadOpen; armed = _assembly.InputArmed; component = _assembly.ComponentSelected; }
            else if (_lamiera.Active) { keypad = _lamiera.KeypadOpen; armed = _lamiera.InputArmed; }
            else if (_inspect.Active) keypad = _inspect.KeypadOpen;
            return InventorXrSo.Core.Input.InputMap.Resolve(keypad, armed, sketch, component);
        }

        private void OnInputInvoked(InventorXrSo.Core.Input.InputAction action, int arg)
        {
            if (action == InventorXrSo.Core.Input.InputAction.RotateView && _inSession && sceneView != null && sceneView.gameObject.activeInHierarchy)
                _workbench.RotateView(sceneView.transform, arg);
        }

        /// <summary>X a riposo: nessun anello, tastierino, gruppo schede, bozza/comando o isolamento nel workspace attivo.</summary>
        private bool AtRest()
        {
            if (!_inSession) return false;
            if (_design.Active) return _design.AtRest;
            if (_assembly.Active) return _assembly.AtRest;
            if (_lamiera.Active) return _lamiera.AtRest;
            return false;
        }

        /// <summary>Padre chiuso dal PC: la pila riparte dal documento attivo.</summary>
        private void ResetNavigationOnActive()
        {
            var doc = CurrentDocInfo();
            var ctx = ContextRouter.ContextOf(doc);
            if (doc == null || ctx == null) { Navigation.Clear(); return; }
            Navigation.Reset(new NavLevel(doc.DocumentId, ctx.Value, doc.Name));
        }

        private void OnDocumentStateForDirty(DocumentState state)
        {
            _dirty.Observe(state);
            ApplyDirty();
        }

        private void ApplyDirty()
        {
            if (_dirty.Apply(Navigation)) _catalog?.NotifyChanged();
        }

        // Il backend client non ha uno strumento di salvataggio (solo i tool desktop inventor_save_document_safe): resta HUD.
        private void SaveDocument() { _badge?.Flash("Salva dal desktop: il visore non salva i documenti.", 5f); }

        private void RecenterWorkbench()
        {
            if (_design.Active) _design.RecenterView();
            else if (_assembly.Active) _assembly.RecenterView();
            else if (_lamiera.Active) _lamiera.RecenterView();
            else _inspect.Recenter();
        }

        /// <summary>Calibrazione del piano: l'altezza del controller diventa l'altezza del piano di lavoro dal prossimo ricentraggio.</summary>
        private void CalibrateDesk()
        {
            if (_workbench == null || ray == null || ray.Origin == null) { _badge?.Flash("Controller non tracciato.", 4f); return; }
            _workbench.SetDeskHeight(ray.Origin.position.y);
            RecenterWorkbench();
            _badge?.Flash("Piano calibrato all'altezza del controller.", 4f);
        }

        private async void RefreshOpenDocuments()
        {
            var backend = _backend;
            var ct = _run?.Token ?? CancellationToken.None;
            if (backend == null) return;
            try
            {
                var docs = await backend.ListOpenAsync(ct);
                if (backend != _backend) return;
                _openDocuments = docs.Where(d => d.Kind == "kPartDocumentObject" || d.Kind == "kAssemblyDocumentObject").ToArray();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Debug.Log("[XrSession] open documents: " + ex.Message); }
        }

        /// <summary>Un documento scelto dall'elenco: la pila riparte da lì (nessun Push/Pop).</summary>
        private async void JumpToDocument(string documentId)
        {
            var backend = _backend;
            if (backend == null || string.IsNullOrEmpty(documentId)) return;
            _contextSwitcher.ExpectJump(documentId);
            try
            {
                await backend.ActivateOpenAsync(documentId, _run?.Token ?? CancellationToken.None);
                _badge?.Flash("Documento attivato. Attendo la scena da Inventor…", 4f);
            }
            catch (OperationCanceledException) { _contextSwitcher.CancelJump(); }
            catch (Exception ex) { _contextSwitcher.CancelJump(); _badge?.Flash(UiText.Error(ex), 6f); }
        }

        private void LeaveSession()
        {
            ShowHome();
            RefreshHome();
        }

        private void Place()
        {
            var root = sceneView.transform;
            root.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            root.localScale = Vector3.one;
            var pose = ScenePlacement.InFront(ScenePlacement.LocalBounds(root), head.position, head.forward);
            root.SetPositionAndRotation(pose.position, pose.rotation);
            _placed = true;
        }

        // --- selection (read-only in M1: never edits the CAD) ---

        private async void OnPicked(CadBody body, int triangle)
        {
            if (_design.Active || _assembly.Active || _lamiera.Active) return;
            if (_inspect.Measuring) return;
            if (_selecting || body == null) return;
            if (_session == null || _session.Status != SessionStatus.Online || _session.Scene == null)
            {
                _badge.Flash(UiText.OfflineNoSelection);
                return;
            }
            _selecting = true;
            try { await _inspect.PickAsync(body, triangle, _picking.Token); }
            catch (OperationCanceledException) { }
            catch (Exception) { if (_badge != null) _badge.Flash(UiText.SelectionFailed); }
            finally { _selecting = false; }
        }

        private async void OnPickedNothing()
        {
            if (_design.Active || _assembly.Active || _lamiera.Active) return;
            if (_inspect.Measuring) return;
            _inspect.ClearSelection();
            if (_selection == null || _selecting || _session.Status != SessionStatus.Online) return;
            _selecting = true;
            try { await _selection.ClearAsync(_picking.Token); }
            catch (Exception) { selectionVisuals.Clear(); }
            finally { _selecting = false; }
        }
    }
}
