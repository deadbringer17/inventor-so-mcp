using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Mcp;
using InventorXrSo.Core.Net;
using InventorXrSo.Core.Session;
using InventorXrSo.Core.Tests.Glb;
using InventorXrSo.Core.Tests.Support;
using Newtonsoft.Json.Linq;

namespace InventorXrSo.Core.Tests.Session;

public class SessionControllerTests
{
    [Fact]
    public async Task NonVisualRevisionNotifiesInspectionWithoutReloadingMeshes()
    {
        var backend = Backend();
        var session = new SessionController(backend, new ManualDelay());
        var changed = new TaskCompletionSource<DocumentState>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.DocumentStateChanged += state => { if (state?.Revision == "properties-r2") changed.TrySetResult(state); };
        using var cts = new CancellationTokenSource();
        var run = session.RunAsync(cts.Token);
        await Until(() => session.Status == SessionStatus.Online && backend.RaiseChanged != null);
        var original = session.Scene;
        backend.State = new DocumentState("doc", "properties-r2", "v1");
        backend.RaiseChanged();
        var state = await changed.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal("properties-r2", state.Revision);
        Assert.Same(original, session.Scene);
        Assert.Single(backend.Calls.Where(c => c == "scene"));
        cts.Cancel(); await run;
    }
    /// <summary>Delays complete when the test says so; each one is recorded.</summary>
    private sealed class ManualDelay : IDelay
    {
        private readonly List<TaskCompletionSource<bool>> _pending = new();
        private readonly List<TimeSpan> _requested = new();

        /// <summary>A snapshot taken under the lock: safe to poll while the session keeps requesting delays in the background.</summary>
        public List<TimeSpan> Requested { get { lock (_pending) return new List<TimeSpan>(_requested); } }

        public Task Delay(TimeSpan duration, CancellationToken ct)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_pending) { _requested.Add(duration); _pending.Add(tcs); }
            ct.Register(() => tcs.TrySetCanceled());
            return tcs.Task;
        }

        public void ReleaseAll()
        {
            List<TaskCompletionSource<bool>> all;
            lock (_pending) { all = _pending.ToList(); _pending.Clear(); }
            foreach (var t in all) t.TrySetResult(true);
        }
    }

    private static FakeBackend Backend()
    {
        var backend = new FakeBackend { Scene = () => FakeBackend.Assembly() };
        backend.Meshes["p"] = GlbModelTests.BoltGlb();
        backend.Meshes["q"] = GlbModelTests.BoltGlb();
        return backend;
    }

    private static async Task Until(Func<bool> condition)
    {
        for (int i = 0; i < 200 && !condition(); i++) await Task.Delay(10);
        Assert.True(condition());
    }

    [Fact]
    public async Task GoesOnlineWithTheScene()
    {
        var backend = Backend();
        var session = new SessionController(backend, new ManualDelay());
        LoadedScene loaded = null;
        session.SceneLoaded += s => loaded = s;
        using var cts = new CancellationTokenSource();
        var run = session.RunAsync(cts.Token);
        await Until(() => session.Status == SessionStatus.Online);
        Assert.False(session.ReadOnly);
        Assert.Equal(2, loaded.Models.Count);
        Assert.Equal("v1", session.Document.VisualRevision);
        cts.Cancel();
        await run;
    }

    [Fact]
    public async Task AChangedCertificateStopsWithoutRetry()
    {
        var backend = Backend();
        backend.FailNext = new CertificateRejectedException(new string('0', 64));
        var delay = new ManualDelay();
        var session = new SessionController(backend, delay);
        await session.RunAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(SessionStatus.NeedsPairing, session.Status);
        Assert.True(session.CertificateChanged);
        Assert.Empty(delay.Requested);
        Assert.Single(backend.Calls);
    }

    [Fact]
    public async Task ClosedEventStreamKeepsSceneAndReconnects()
    {
        var backend = Backend();
        backend.CloseEventsImmediately = true;
        var delay = new ManualDelay();
        var session = new SessionController(backend, delay);
        using var cts = new CancellationTokenSource();
        var run = session.RunAsync(cts.Token);
        await Until(() => session.Status == SessionStatus.Offline);
        Assert.NotNull(session.Scene);
        backend.CloseEventsImmediately = false;
        delay.ReleaseAll();
        await Until(() => session.Status == SessionStatus.Online && backend.RaiseChanged != null);
        Assert.Equal(2, backend.Calls.Count(call => call == "connect"));
        cts.Cancel();
        await run;
    }

    [Fact]
    public async Task AnEventWithNewGeometryReloadsTheScene()
    {
        var backend = Backend();
        var session = new SessionController(backend, new ManualDelay());
        int loads = 0;
        session.SceneLoaded += _ => loads++;
        using var cts = new CancellationTokenSource();
        var run = session.RunAsync(cts.Token);
        await Until(() => session.Status == SessionStatus.Online && backend.RaiseChanged != null);

        backend.State = new DocumentState("doc", "r2", "v2");
        backend.Scene = () => FakeBackend.Assembly("v2");
        backend.RaiseChanged();
        await Until(() => loads == 2);

        backend.State = new DocumentState("doc", "r3", "v2");   // save only: no reload
        backend.RaiseChanged();
        await Until(() => backend.Calls.Count(c => c == "state") >= 3);
        Assert.Equal(2, loads);
        cts.Cancel();
        await run;
    }

    [Fact]
    public async Task NetworkLossKeepsTheSceneReadOnlyAndReconnects()
    {
        var backend = Backend();
        var delay = new ManualDelay();
        var session = new SessionController(backend, delay);
        using var cts = new CancellationTokenSource();
        var run = session.RunAsync(cts.Token);
        await Until(() => session.Status == SessionStatus.Online && backend.RaiseChanged != null);

        backend.FailNext = new TransportException("wifi gone");
        backend.RaiseChanged();
        await Until(() => session.Status == SessionStatus.Offline);
        Assert.True(session.ReadOnly);
        Assert.NotNull(session.Scene);
        Assert.Contains(TimeSpan.FromSeconds(1), delay.Requested);

        delay.ReleaseAll();
        await Until(() => session.Status == SessionStatus.Online);
        cts.Cancel();
        await run;
    }

    [Fact]
    public async Task ARevokedTokenStopsTheSessionForPairing()
    {
        var backend = Backend();
        backend.FailNext = new McpUnauthorizedException();
        var session = new SessionController(backend, new ManualDelay());
        await session.RunAsync(CancellationToken.None);
        Assert.Equal(SessionStatus.NeedsPairing, session.Status);
    }

    [Fact]
    public async Task MissingXrToolsMeanNotReady()
    {
        var backend = Backend();
        backend.Capabilities = CapabilitiesInfo.FromJson(JObject.Parse(@"{""target"":{""reachable"":true},""capabilities"":{}}"));
        var session = new SessionController(backend, new ManualDelay());
        using var cts = new CancellationTokenSource();
        var run = session.RunAsync(cts.Token);
        await Until(() => session.Status == SessionStatus.NotReady);
        Assert.DoesNotContain("scene", backend.Calls);
        cts.Cancel();
        await run;
    }

    [Fact]
    public async Task NoOpenDocumentClearsTheSceneAndWaits()
    {
        var backend = Backend();
        backend.FailNext = null;
        var session = new SessionController(backend, new ManualDelay());
        var scenes = new List<LoadedScene>();
        session.SceneLoaded += scenes.Add;
        using var cts = new CancellationTokenSource();
        var run = session.RunAsync(cts.Token);
        await Until(() => session.Status == SessionStatus.Online && backend.RaiseChanged != null);

        backend.FailNext = new McpToolException("inventor_get_visual_revision", "NO_DOCUMENT", "none open", null);
        backend.RaiseChanged();
        await Until(() => session.Status == SessionStatus.NoDocument);
        Assert.Null(session.Scene);
        Assert.Null(scenes.Last());
        cts.Cancel();
        await run;
    }

    [Fact]
    public async Task PollingSeveralTimesStillDeliversTheNextEvent()
    {
        var backend = Backend();
        var delay = new ManualDelay();
        var session = new SessionController(backend, delay);
        int loads = 0;
        session.SceneLoaded += _ => loads++;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var run = session.RunAsync(cts.Token);
        await Until(() => session.Status == SessionStatus.Online && backend.RaiseChanged != null);
        Assert.Equal(1, loads);

        // Release the poll delay a few times while connected: each is a refresh with no geometry
        // change, so no reload, but (pre-fix) each pass also abandoned the still-pending event waiter.
        for (int i = 0; i < 3; i++)
        {
            int before = backend.Calls.Count(c => c == "state");
            delay.ReleaseAll();
            await Until(() => backend.Calls.Count(c => c == "state") > before);
        }
        Assert.Equal(1, loads);

        backend.State = new DocumentState("doc", "r9", "v9");
        backend.Scene = () => FakeBackend.Assembly("v9");
        backend.RaiseChanged();
        await Until(() => loads == 2);

        cts.Cancel();
        await run;
    }

    [Fact]
    public async Task RepeatedRefreshFailuresGrowTheBackoffWithoutResetting()
    {
        var backend = Backend();
        backend.Scene = () => throw new TransportException("db unreachable");
        var delay = new ManualDelay();
        var session = new SessionController(backend, delay);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var run = session.RunAsync(cts.Token);

        await Until(() => session.Status == SessionStatus.Offline);
        Assert.Contains(TimeSpan.FromSeconds(1), delay.Requested);

        delay.ReleaseAll();
        await Until(() => delay.Requested.Contains(TimeSpan.FromSeconds(2)));

        delay.ReleaseAll();
        await Until(() => delay.Requested.Contains(TimeSpan.FromSeconds(4)));

        cts.Cancel();
        await run;
    }

    [Fact]
    public async Task DocumentClosedDuringSceneReloadMeansNoDocument()
    {
        var backend = Backend();
        var session = new SessionController(backend, new ManualDelay());
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var run = session.RunAsync(cts.Token);
        await Until(() => session.Status == SessionStatus.Online && backend.RaiseChanged != null);

        backend.State = new DocumentState("doc", "r2", "v2");
        backend.Scene = () => throw new McpToolException("inventor_get_scene_graph", "NO_DOCUMENT", "closed mid-reload", null);
        backend.RaiseChanged();
        await Until(() => session.Status == SessionStatus.NoDocument);
        Assert.Null(session.Scene);

        cts.Cancel();
        await run;
    }
}
