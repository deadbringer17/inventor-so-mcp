using System;
using System.Threading;
using System.Threading.Tasks;
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Mcp;

namespace InventorXrSo.Core.Session
{
    public enum SessionStatus { Idle, Connecting, NotReady, NoDocument, Online, Offline, NeedsPairing }

    /// <summary>
    /// The headset's connection to Inventor (spec §5): connect, check capabilities, follow the active
    /// document through events plus a slow poll, and on any failure keep the last scene visible,
    /// read-only, while reconnecting with backoff.
    /// </summary>
    public sealed class SessionController
    {
        private static readonly TimeSpan NotReadyRetry = TimeSpan.FromSeconds(5);
        private readonly IInventorBackend _backend;
        private readonly IDelay _delay;
        private readonly SceneLoader _loader;

        public SessionController(IInventorBackend backend, IDelay delay)
        {
            _backend = backend;
            _delay = delay;
            _loader = new SceneLoader(backend);
        }

        public SessionStatus Status { get; private set; } = SessionStatus.Idle;
        public bool ReadOnly => Status != SessionStatus.Online;
        public CapabilitiesInfo Capabilities { get; private set; }
        public DocumentState Document { get; private set; }
        public LoadedScene Scene { get; private set; }
        public string LastError { get; private set; }
        public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

        public event Action<SessionStatus> StatusChanged;
        public event Action<LoadedScene> SceneLoaded;

        public async Task RunAsync(CancellationToken ct)
        {
            var backoff = new Backoff();
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    SetStatus(SessionStatus.Connecting);
                    await _backend.ConnectAsync(ct);
                    Capabilities = await _backend.GetCapabilitiesAsync(ct);
                    if (!Capabilities.IsXrReady)
                    {
                        SetStatus(SessionStatus.NotReady);
                        await _delay.Delay(NotReadyRetry, ct);
                        continue;
                    }
                    backoff.Reset();
                    await RefreshAsync(ct);
                    await RunConnectedAsync(ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return;
                }
                catch (McpUnauthorizedException ex)
                {
                    LastError = ex.Message;
                    SetStatus(SessionStatus.NeedsPairing);
                    return;
                }
                catch (McpToolException ex) when (ex.Code == "NO_TARGET" || ex.Code == "TARGET_UNAVAILABLE")
                {
                    LastError = ex.Message;
                    SetStatus(SessionStatus.NotReady);
                    if (!await Wait(NotReadyRetry, ct)) return;
                }
                catch (Exception ex)
                {
                    LastError = ex.Message;
                    SetStatus(SessionStatus.Offline);
                    if (!await Wait(backoff.Next(), ct)) return;
                }
            }
        }

        /// <summary>Re-read the document state; reload the scene only when the document or its geometry changed.</summary>
        public async Task RefreshAsync(CancellationToken ct)
        {
            DocumentState state;
            try { state = await _backend.GetDocumentStateAsync(ct); }
            catch (McpToolException ex) when (ex.Code == "NO_DOCUMENT")
            {
                Document = null;
                if (Scene != null)
                {
                    Scene = null;
                    SceneLoaded?.Invoke(null);
                }
                SetStatus(SessionStatus.NoDocument);
                return;
            }
            if (SceneDiff.Compare(Document, state).NeedsReload || Scene == null)
            {
                var scene = await _loader.LoadAsync(ct);
                Scene = scene;
                Document = scene.Graph.State;
                SceneLoaded?.Invoke(scene);
            }
            else Document = state;
            SetStatus(SessionStatus.Online);
        }

        private async Task RunConnectedAsync(CancellationToken ct)
        {
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                var changed = new SemaphoreSlim(0);
                Task events = _backend.RunEventsAsync(() => changed.Release(), linked.Token);
                var never = new TaskCompletionSource<bool>().Task;
                try
                {
                    while (true)
                    {
                        var wake = changed.WaitAsync(linked.Token);
                        var poll = _delay.Delay(PollInterval, linked.Token);
                        var done = await Task.WhenAny(wake, poll, events);
                        linked.Token.ThrowIfCancellationRequested();
                        if (done == events)
                        {
                            // A closed or unsupported stream leaves the poll; a broken one means offline.
                            try { await events; }
                            catch (McpException ex) when (ex.Code == "EVENT_STREAM_UNSUPPORTED") { }
                            events = never;
                            continue;
                        }
                        await RefreshAsync(ct);
                    }
                }
                finally
                {
                    linked.Cancel();
                }
            }
        }

        private async Task<bool> Wait(TimeSpan duration, CancellationToken ct)
        {
            try
            {
                await _delay.Delay(duration, ct);
                return true;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return false;
            }
        }

        private void SetStatus(SessionStatus status)
        {
            if (Status == status) return;
            Status = status;
            StatusChanged?.Invoke(status);
        }
    }
}
