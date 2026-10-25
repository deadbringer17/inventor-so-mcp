using System;
using System.Globalization;
using System.Threading.Tasks;
using System.Threading;
using InventorXrSo.Core.Capture;
using InventorXrSo.Xr.Capture;
using InventorXrSo.Unity.Ui;
using UnityEngine;

namespace InventorXrSo.Xr
{
    /// <summary>
    /// Independent Reality Capture section. Owns its UI, camera and raw sessions, without a CAD dependency.
    /// </summary>
    public sealed partial class RealityCaptureWorkspace : MonoBehaviour
    {
        private Transform _head;
        private Action _returnHome;
        private RealityCaptureController _controller;
        private Transform _tracking;
        private CaptureMode _mode;
        private double _width = 600, _height = 600, _depth = 600, _distance = 1000;
        private Matrix4x4 _volumeToTracking;
        private LineRenderer _boundary;
        private Material _boundaryMaterial;
        private bool _showingCapture, _dirty;
        private int _uiGeneration;
        private float _nextRefresh;
        private CaptureState? _presentedState;
        private Func<CaptureSessionClient> _transfer;
        private CancellationTokenSource _upload;
        public void BindTransfer(Func<CaptureSessionClient> transfer) => _transfer = transfer;
        public HomePanel Panel { get; private set; }
        public bool Active => Panel != null && Panel.gameObject.activeSelf;

        public void Initialize(Transform head, Action returnHome)
        {
            _head = head;
            _returnHome = returnHome;
            _tracking = head.GetComponentInParent<OVRCameraRig>()?.trackingSpace ?? head.parent;
            _controller = gameObject.AddComponent<RealityCaptureController>();
            _controller.Changed += OnCaptureChanged;
            Panel = HomePanel.Create(transform);
            Panel.name = "RealityCapturePanel";
            XrUi.MakeInteractive(Panel.Canvas, head.GetComponent<Camera>());
            Panel.gameObject.SetActive(false);
        }

        public void Open()
        {
            var forward = Vector3.ProjectOnPlane(_head.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 1e-6f) forward = Vector3.forward;
            Panel.transform.SetPositionAndRotation(_head.position + forward, Quaternion.LookRotation(forward, Vector3.up));
            Panel.gameObject.SetActive(true);
            ShowOverview();
        }

        public void Close()
        {
            CloseReview();
            _uiGeneration++;
            _upload?.Cancel();
            _showingCapture = false;
            if (_boundary != null) _boundary.gameObject.SetActive(false);
            Panel?.gameObject.SetActive(false);
            if (_controller != null) _ = _controller.EndAsync("workspace_closed", true);
        }

        private void ShowOverview()
        {
            CloseReview();
            _uiGeneration++;
            _showingCapture = false;
            Panel.ShowMessage(UiText.RealityCapture, UiText.CaptureHomeBody);
            Panel.SetActions((UiText.CaptureObjects, () => ShowInfo(UiText.CaptureObjects, UiText.CaptureObjectsBody)),
                (UiText.CaptureNewObject, () => ConfigureScan(CaptureMode.Object)),
                (UiText.CaptureNewArea, () => ConfigureScan(CaptureMode.Area)),
                (UiText.CaptureOpen, () => RunUiAsync(ShowSessionsAsync)),
                (UiText.CaptureReferences, () => ShowInfo(UiText.CaptureReferences, UiText.CaptureReferencesBody)),
                (UiText.ReturnHome, () => _returnHome()));
        }

        private void ShowInfo(string title, string body)
        {
            _uiGeneration++;
            _showingCapture = false;
            Panel.ShowMessage(UiText.RealityCapture + " · " + title, body);
            Panel.SetActions((UiText.RealityCapture, ShowOverview), (UiText.ReturnHome, () => _returnHome()));
        }

        private void ConfigureScan(CaptureMode mode)
        {
            _mode = mode; _width = _height = _depth = mode == CaptureMode.Object ? 600 : 3000;
            _distance = mode == CaptureMode.Object ? 1000 : 2500;
            PlaceBoundary(); ShowSetup();
        }

        private void PlaceBoundary()
        {
            var forward = Vector3.ProjectOnPlane(_head.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 1e-6f) forward = Vector3.forward;
            var pose = Matrix4x4.TRS(_head.position + _head.forward * (float)(_distance/1000), Quaternion.LookRotation(forward), Vector3.one);
            _volumeToTracking = (_tracking == null ? Matrix4x4.identity : _tracking.worldToLocalMatrix) * pose;
            DrawBoundary();
        }

        private void ShowSetup()
        {
            _uiGeneration++; _showingCapture = false;
            DrawBoundary();
            Panel.ShowMessage(UiText.RealityCapture + (_mode == CaptureMode.Object ? " · Oggetto" : " · Area"),
                "Delimita il volume con il riquadro azzurro. È una zona scelta dall’operatore, non una superficie rilevata.\n" +
                $"L × H × P: {_width:0} × {_height:0} × {_depth:0} mm. Distanza: {_distance:0} mm.\n" +
                "Le immagini saranno salvate solo dopo Avvia. Ricostruzione sul PC dopo trasferimento; copertura geometrica non disponibile.");
            Panel.SetActions(("Larghezza", () => PromptDimension("Larghezza", _width, n => _width = n)),
                ("Altezza", () => PromptDimension("Altezza", _height, n => _height = n)),
                ("Profondità", () => PromptDimension("Profondità", _depth, n => _depth = n)),
                ("Distanza", () => PromptDimension("Distanza", _distance, n => { _distance = n; PlaceBoundary(); })),
                ("Posiziona davanti a me", () => { PlaceBoundary(); ShowSetup(); }),
                ("Conferma volume", () => RunUiAsync(CreateScanAsync)),
                (UiText.Cancel, () => { if (_boundary != null) _boundary.gameObject.SetActive(false); ShowOverview(); }));
        }

        private void PromptDimension(string title, double initial, Action<double> apply)
        {
            Panel.PromptText(title, "Valore in mm, tra 10 e 100000.", initial.ToString(CultureInfo.InvariantCulture), text =>
            {
                if (double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double value) &&
                    !double.IsNaN(value) && !double.IsInfinity(value) && value >= 10 && value <= 100000) apply(value);
                ShowSetup();
            }, ShowSetup);
        }

        private async Task CreateScanAsync()
        {
            int generation = _uiGeneration;
            var volume = new CaptureVolume(QuestCameraSource.ToContract(_volumeToTracking), _width, _height, _depth);
            await _controller.CreateAsync(_mode, volume, Matrix4x4.identity);
            if (Active && generation == _uiGeneration && _controller.Current != null) ShowCapture(true);
        }

        private void ShowCapture(bool rebuildActions)
        {
            _showingCapture = true;
            Panel.ShowMessage(UiText.RealityCapture + " · " + (_mode == CaptureMode.Object ? "Oggetto" : "Area"),
                $"Frame salvati: {_controller.Accepted} · scartati: {_controller.Rejected}\n" +
                RealityCaptureController.ReasonText(_controller.Feedback) + "\n\n" +
                "Qualità basata sulle immagini. Copertura geometrica: non disponibile. Nessuna accuratezza dimensionale dichiarata.");
            if (!rebuildActions) return;
            _presentedState = _controller.State;
            int generation = _uiGeneration;
            if (_controller.State == CaptureState.Recording)
                Panel.SetActions((UiText.CapturePause, () => RunUiAsync(async () => { await _controller.PauseAsync(); if (Active && generation == _uiGeneration) ShowCapture(true); })),
                    (UiText.CaptureStop, () => RunUiAsync(FinishAsync)), (UiText.ReturnHome, () => _returnHome()));
            else if (_controller.CanResume)
                Panel.SetActions((_controller.State == CaptureState.Paused ? UiText.CaptureResume : UiText.CaptureStart,
                    () => RunUiAsync(async () => { await _controller.StartAsync(); if (Active && generation == _uiGeneration) ShowCapture(true); })),
                    (UiText.CaptureStop, () => RunUiAsync(FinishAsync)), (UiText.ReturnHome, () => _returnHome()));
            else Panel.SetActions((UiText.CaptureOpen, () => RunUiAsync(ShowSessionsAsync)),
                (UiText.RealityCapture, ShowOverview), (UiText.ReturnHome, () => _returnHome()));
        }

        private async Task FinishAsync()
        {
            int generation = _uiGeneration;
            await _controller.EndAsync();
            if (_boundary != null) _boundary.gameObject.SetActive(false);
            if (Active && generation == _uiGeneration) ShowCapture(true);
        }

        private async Task ShowSessionsAsync()
        {
            _showingCapture = false;
            int generation = ++_uiGeneration;
            var sessions = await _controller.ListAsync();
            if (!Active || generation != _uiGeneration) return;
            if (sessions.Count == 0) { ShowInfo(UiText.CaptureOpen, "Nessuna sessione salvata su questo visore."); return; }
            ShowSessionPage(sessions, 0);
        }

        private void ShowSessionPage(System.Collections.Generic.IReadOnlyList<ScanSession> sessions, int index)
        {
            var session = sessions[index];
            _showingCapture = false;
            Panel.ShowMessage(UiText.CaptureOpen, $"{index + 1}/{sessions.Count} · {session.CreatedAt.ToLocalTime():g}\n" +
                $"{(session.Mode == CaptureMode.Object ? "Oggetto" : "Area")} · {session.SessionId}\nRaw immutabili. La riapertura permette la verifica; per continuare acquisizioni di un avvio precedente serve una nuova sessione.");
            var actions = new System.Collections.Generic.List<(string, Action)> {
                ("Verifica integrità", () => RunUiAsync(async () =>
                {
                    int generation = ++_uiGeneration;
                    Panel.ShowMessage(UiText.CaptureOpen, "Verifica hash dei frame in corso…"); Panel.SetActions();
                    var result = await _controller.ReviewAsync(session.SessionId);
                    if (!Active || generation != _uiGeneration) return;
                    ShowInfo(UiText.CaptureOpen, $"{result.status}\n{result.count} frame: hash verificati.\nVerifica dei soli raw. Apri Revisioni PC dalla sessione per la ricostruzione; questa verifica non è validazione dimensionale.");
                })) };
            if (_transfer?.Invoke() != null) actions.Add(("Trasferisci al PC", () => RunUiAsync(async () =>
            {
                _upload?.Cancel(); _upload?.Dispose(); _upload = new CancellationTokenSource();
                int generation = ++_uiGeneration;
                Panel.ShowMessage(UiText.RealityCapture, "Trasferimento raw al PC associato. Puoi interrompere e riprendere più tardi.\nElaborazione locale; retention sul PC: 7 giorni. I raw sul visore restano conservati.");
                Panel.SetActions((UiText.Cancel, () => _upload.Cancel()),(UiText.ReturnHome, () => _returnHome()));
                try
                {
                    await _controller.UploadAsync(_transfer(),session.SessionId,_upload.Token);
                    if (Active && generation == _uiGeneration) ShowInfo(UiText.RealityCapture,"Trasferimento completato e hash verificati sul PC. Apri Revisioni PC dalla sessione per avviare o rivedere la ricostruzione.");
                }
                catch (OperationCanceledException) { if (Active && generation == _uiGeneration) ShowInfo(UiText.RealityCapture,"Trasferimento interrotto. Ripeti Trasferisci al PC per inviare i file mancanti."); }
            })));
            if (_reviewFactory != null) actions.Add(("Revisioni PC", () => RunUiAsync(() => OpenReviewsAsync(session.SessionId))));
            if (index > 0) actions.Add(("Precedente", () => ShowSessionPage(sessions, index - 1)));
            if (index + 1 < sessions.Count) actions.Add(("Successiva", () => ShowSessionPage(sessions, index + 1)));
            actions.Add((UiText.RealityCapture, ShowOverview)); actions.Add((UiText.ReturnHome, () => _returnHome()));
            Panel.SetActions(actions.ToArray());
        }

        private bool _uiBusy;
        private async void RunUiAsync(Func<Task> action)
        {
            if (_uiBusy) return;
            _uiBusy = true;
            try { await action(); }
            catch (Exception) { if (Active) ShowInfo(UiText.RealityCapture, "Operazione non completata. Controlla lo spazio locale o l’integrità della sessione."); }
            finally { _uiBusy = false; }
        }
        private void OnCaptureChanged() => _dirty = true;
        private void Update()
        {
            if (!Active || !_showingCapture || !_dirty || Time.unscaledTime < _nextRefresh) return;
            _dirty = false; _nextRefresh = Time.unscaledTime + 0.5f;
            // Keep buttons alive while feedback changes so an in-progress trigger click is not lost.
            ShowCapture(_presentedState != _controller.State);
        }

        private void DrawBoundary()
        {
            if (_boundary == null)
            {
                var go = new GameObject("Capture volume · operator selection"); go.transform.SetParent(transform, false);
                _boundary = go.AddComponent<LineRenderer>();
                _boundary.useWorldSpace = true; _boundary.widthMultiplier = 0.003f;
                _boundaryMaterial = new Material(Shader.Find("Sprites/Default")); _boundary.sharedMaterial = _boundaryMaterial;
                _boundary.startColor = _boundary.endColor = Color.cyan;
            }
            var matrix = (_tracking == null ? Matrix4x4.identity : _tracking.localToWorldMatrix) * _volumeToTracking;
            var corners = new Vector3[8];
            for (int i = 0; i < 8; i++) corners[i] = matrix.MultiplyPoint3x4(new Vector3(
                (float)(_width/2000) * ((i & 1) == 0 ? -1 : 1), (float)(_height/2000) * ((i & 2) == 0 ? -1 : 1),
                (float)(_depth/2000) * ((i & 4) == 0 ? -1 : 1)));
            int[] path = { 0,1,3,2,0,4,5,1,5,7,3,7,6,2,6,4 };
            _boundary.positionCount = path.Length;
            for (int i = 0; i < path.Length; i++) _boundary.SetPosition(i, corners[path[i]]);
            _boundary.gameObject.SetActive(true);
        }

        private void OnDestroy()
        {
            CloseReview();
            if (_controller != null) _controller.Changed -= OnCaptureChanged;
            _upload?.Cancel(); _upload?.Dispose();
            if (_boundaryMaterial != null) Destroy(_boundaryMaterial);
        }
    }
}
