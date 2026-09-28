using System;
using System.Threading;
using System.Threading.Tasks;
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Mcp;
using InventorXrSo.Core.Net;

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
        public bool CertificateChanged { get; private set; }
        public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

        public event Action<SessionStatus> StatusChanged;
        public event Action<LoadedScene> SceneLoaded;
        public event Action<DocumentState> DocumentStateChanged;

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
                    await RefreshAsync(ct);
                    // Only a refresh that actually completes counts as recovery: a PC that answers
                    // capabilities but keeps failing to refresh must not be retried at the fastest
                    // backoff step forever (a throw from RefreshAsync skips this line entirely).
                    backoff.Reset();
                    await RunConnectedAsync(ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return;
                }
                catch (CertificateRejectedException ex)
                {
                    CertificateChanged = true;
                    LastError = ex.Message;
                    SetStatus(SessionStatus.NeedsPairing);
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
        private async Task RefreshAsync(CancellationToken ct)
        {
            var previous = Document;
            DocumentState state;
            try { state = await _backend.GetDocumentStateAsync(ct); }
            catch (McpToolException ex) when (ex.Code == "NO_DOCUMENT")
            {
                ClearSceneForNoDocument();
                return;
            }
            if (SceneDiff.Compare(Document, state).NeedsReload || Scene == null)
            {
                LoadedScene scene;
                try { scene = await _loader.LoadAsync(ct); }
                catch (McpToolException ex) when (ex.Code == "NO_DOCUMENT")
                {
                    // The document closed between the state read and the scene read: same outcome.
                    ClearSceneForNoDocument();
                    return;
                }
                Scene = scene;
                Document = scene.Graph.State;
                SceneLoaded?.Invoke(scene);
            }
            else Document = state;
            if (previous?.DocumentId != Document.DocumentId || previous?.Revision != Document.Revision || previous?.VisualRevision != Document.VisualRevision)
                DocumentStateChanged?.Invoke(Document);
            LastError = null;
            SetStatus(SessionStatus.Online);
        }

        private void ClearSceneForNoDocument()
        {
            bool hadDocument = Document != null;
            Document = null;
            if (Scene != null)
            {
                Scene = null;
                SceneLoaded?.Invoke(null);
            }
            SetStatus(SessionStatus.NoDocument);
            if (hadDocument) DocumentStateChanged?.Invoke(null);
        }

        private async Task RunConnectedAsync(CancellationToken ct)
        {
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                var changed = new SemaphoreSlim(0);
                Task events = _backend.RunEventsAsync(() => changed.Release(), linked.Token);
                var never = new TaskCompletionSource<bool>().Task;
                // One outstanding wake and one outstanding poll live across passes; each is replaced
                // only once it has actually completed. Creating a fresh WaitAsync on every pass (even
                // when the poll won) would abandon the previous one mid-flight: SemaphoreSlim.Release
                // satisfies waiters in order, so a later RaiseChanged could complete a stale, unawaited
                // waiter instead of the one this loop is actually watching, silently losing the event.
                Task wake = changed.WaitAsync(linked.Token);
                Task poll = _delay.Delay(PollInterval, linked.Token);
                try
                {
                    while (true)
                    {
                        var done = await Task.WhenAny(wake, poll, events);
                        linked.Token.ThrowIfCancellationRequested();
                        if (done == events)
                        {
                            // An unsupported stream uses polling. An established stream closing is a
                            // lost connection: reconnect and subscribe again, preserving the model.
                            try
                            {
                                await events;
                                throw new TransportException("The event stream closed.");
                            }
                            catch (McpException ex) when (ex.Code == "EVENT_STREAM_UNSUPPORTED") { }
                            events = never;
                            continue;
                        }
                        if (done == wake) wake = changed.WaitAsync(linked.Token);
                        else poll = _delay.Delay(PollInterval, linked.Token);
                        await RefreshAsync(ct);
                    }
                }
                finally
                {
                    linked.Cancel();
                    // A stream that faulted (rather than ended/being cancelled) must still be observed,
                    // or its exception surfaces later as an unobserved task exception.
                    if (!ReferenceEquals(events, never))
                        _ = events.ContinueWith(t => { var __ = t.Exception; }, TaskScheduler.Default);
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
