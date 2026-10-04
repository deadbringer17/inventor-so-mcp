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
        private InventorBackend _backend;
        private IReadOnlyList<OpenDocument> _openDocuments = new OpenDocument[0];

        /// <summary>Pila di navigazione Assieme › Sub › Parte (la guida del router arriva con il collegamento completo).</summary>
        public NavigationStack Navigation { get; } = new NavigationStack();

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
            _design.CanEnter = () => !_assembly.RequiresCadReview && !_lamiera.RequiresCadReview;
            _assembly.CanEnter = () => !_design.RequiresCadReview && !_lamiera.RequiresCadReview;
            _lamiera.CanEnter = () => !_design.RequiresCadReview && !_assembly.RequiresCadReview;
            _document = new DocumentActions(Navigation, GoBack, SaveDocument, RecenterWorkbench, CalibrateDesk,
                LeaveSession, LeaveSession, () => _inSession,
                () => _design.CanEnter() && _lamiera.CanEnter() && _assembly.CanEnter(),
                () => _openDocuments, ActivateDocument);
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
            _lamiera.Attach(_shell, _workbench, _sheet, _input);
            _assembly.Attach(_shell, _workbench, _sheet, _input);
            // Ispeziona is the default workspace: it draws on the palette whenever no authoring workspace is active.
            _inspect.Attach(_shell, _input);
            _inspect.OtherWorkspaceActive = () => _design.Active || _assembly.Active || _lamiera.Active;
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
            // From the isolated component: its document is already active, the switch only changes workspace.
            _assembly.DesignRequested += () => { if (!_design.RequiresCadReview && !_lamiera.RequiresCadReview) { _assembly.Close(); _design.Open(); } };
            _assembly.LamieraRequested += () => { if (!_design.RequiresCadReview && !_lamiera.RequiresCadReview) { _assembly.Close(); _lamiera.Open(); } };
            _lamiera.HudMessage += text => _badge.Flash(text, 6f);
            _lamiera.ActiveChanged += active =>
            {
                if (active) _catalog.SetActive(_lamiera);
                else if (ReferenceEquals(_catalog.Active, _lamiera)) _catalog.SetActive(_inspect);
            };
            _lamiera.Closed += () => { if (_inSession) Place(); };
            // Back, Fit, Recenter, step and zoom belong to the active authoring workspace (it subscribes through Attach); the palette owns the tabs.
            _input.TabDelta += _shell.Palette.SelectTab;
            _lamiera.DesignRequested += () => { if (!_assembly.RequiresCadReview && !_lamiera.RequiresCadReview) { _lamiera.Close(); _design.Open(); } };
            _lamiera.PrimaryChanged += OnLamieraPrimaryChanged;
            // Ispeziona stops its local tools while an authoring workspace is open and resumes when it closes.
            _design.ActiveChanged += _ => _inspect.OthersChanged();
            _assembly.ActiveChanged += _ => _inspect.OthersChanged();
            _lamiera.ActiveChanged += _ => _inspect.OthersChanged();
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
        }

        private void OnDestroy()
        {
            _lifetime.Cancel();
            StopSession();
            qrScanner?.Stop();
            if (ray != null) { ray.Picked -= OnPicked; ray.PickedNothing -= OnPickedNothing; }
            if (_home != null) Destroy(_home.gameObject);
            if (_shell != null) Destroy(_shell.gameObject);
            _lifetime.Dispose();
        }

        private void StopSession()
        {
            if (_voice != null) { Destroy(_voice.gameObject); _voice = null; }
            _inspect?.Bind(null, null);
            _design?.Bind(null);
            _assembly?.Bind(null);
            _lamiera?.Bind(null, null);
            if (_session != null)
            {
                _session.StatusChanged -= OnStatusChanged; _session.SceneLoaded -= OnSceneLoaded;
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
            _catalog?.NotifyChanged();
            if (_voiceTarget != null) _voiceTarget.InSession = false;
            _inspect?.SetVisible(false);
            _design?.SetVisible(false);
            _assembly?.SetVisible(false);
            _lamiera?.SetVisible(false);
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
            // A sheet-metal part opens Lamiera as the primary mode; an ordinary part never does.
            if (_lamiera.IsPrimary) OpenLamiera();
        }

        private void OpenDesign() { if (!_assembly.RequiresCadReview && !_lamiera.RequiresCadReview) { _assembly.Close(); _lamiera.Close(); _design.Open(); } }
        private void OpenAssembly() { if (!_design.RequiresCadReview && !_lamiera.RequiresCadReview) { _design.Close(); _lamiera.Close(); _assembly.Open(); } }
        private void OpenInspection() { _design.Close(); _assembly.Close(); _lamiera.Close(); NotifyVoiceModeChanged(); }

        private void OpenLamiera()
        {
            if (_design.RequiresCadReview || _assembly.RequiresCadReview) return;
            _design.Close(); _assembly.Close(); _lamiera.Open();
        }

        private void OnLamieraPrimaryChanged(bool primary)
        {
            // Auto-open only on the transition to primary and only from plain inspection: never over an authoring workspace.
            if (primary && _inSession && !_design.Active && !_assembly.Active && !_lamiera.Active) OpenLamiera();
        }

        private void NotifyVoiceModeChanged() { _voice?.NotifyModeChanged(); }

        // --- scheda Documento ---

        /// <summary>Torna al livello superiore. Il collegamento al router arriva con il passo successivo di M9: per ora non cambia il documento.</summary>
        public void GoBack() { if (Navigation.CanPop) _badge?.Flash("Torna a " + Navigation.Parent.Name + ": non ancora collegato.", 4f); }

        private void SaveDocument() { _badge?.Flash("Salvataggio dal visore non ancora disponibile.", 4f); }

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

        private async void ActivateDocument(string documentId)
        {
            var backend = _backend;
            if (backend == null || string.IsNullOrEmpty(documentId)) return;
            try
            {
                await backend.ActivateOpenAsync(documentId, _run?.Token ?? CancellationToken.None);
                _badge?.Flash("Documento attivato. Attendo la scena da Inventor…", 4f);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { _badge?.Flash(UiText.Error(ex), 6f); }
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
