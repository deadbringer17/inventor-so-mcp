using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Glb;

namespace InventorXrSo.Core.Tests.Backend;

public class FlatPatternViewTests
{
    private static readonly DocumentState R1 = new("doc", "r1", "v1"), R2 = new("doc", "r2", "v2");

    private static FlatPatternMesh Mesh(string doc = "doc", string rev = "r1", string hash = "h1") =>
        new(doc, rev, hash, "a_" + new string('0', 64), new GlbModel(doc, Array.Empty<GlbPrimitive>()), 120, 40, 2, 2.0);

    private sealed class Backend : ISheetMetalBackend
    {
        public readonly List<TaskCompletionSource<FlatPatternMesh>> Pending = new();
        public readonly List<CancellationToken> Tokens = new();
        public Func<DocumentState, FlatPatternMesh> Immediate;
        public Exception Fail;
        public int Meshes;
        public Task<SheetMetalContext> GetSheetMetalContextAsync(DocumentState state, CancellationToken ct) => throw new NotSupportedException();
        public Task<FlatPatternMesh> GetFlatPatternMeshAsync(DocumentState state, CancellationToken ct)
        {
            Meshes++; Tokens.Add(ct);
            if (Fail != null) throw Fail;
            if (Immediate != null) return Task.FromResult(Immediate(state));
            var tcs = new TaskCompletionSource<FlatPatternMesh>(); Pending.Add(tcs); return tcs.Task;
        }
    }

    private static (FlatPatternView view, Backend backend) Setup()
    {
        var backend = new Backend { Immediate = s => Mesh(s.DocumentId, s.Revision) };
        var view = new FlatPatternView(backend); view.SetContext(R1, true);
        return (view, backend);
    }

    [Fact]
    public async Task LoadsAssetWithNativeMeasuresKeyedByDocumentRevisionAndIdentity()
    {
        var (view, _) = Setup(); using var cleanup = view;
        Assert.Equal(FlatPatternState.Hidden, view.State);
        await view.ShowAsync();
        Assert.Equal(FlatPatternState.Ready, view.State); Assert.True(view.IsVisible);
        Assert.Equal(120, view.Asset.LengthMm); Assert.Equal(2, view.Asset.BendCount); Assert.Equal("doc|r1|h1", view.Asset.Key);
        Assert.Equal("Sviluppo — sola vista", view.StatusText); Assert.False(view.IsCadSelectable);
        await view.ShowAsync(); Assert.Equal(1, view.CachedCount);
    }

    [Fact] // M5-05: distinct unavailable reasons
    public async Task FailuresKeepDistinctOutcomesAndNeverShowAnEstimate()
    {
        foreach (var failure in new[] { FlatPatternFailure.Missing, FlatPatternFailure.MultiBody, FlatPatternFailure.WrongDocumentType, FlatPatternFailure.TooLarge })
        {
            var (view, backend) = Setup(); using var cleanup = view;
            backend.Fail = new FlatPatternException(failure, "motivo " + failure);
            await view.ShowAsync();
            Assert.Equal(FlatPatternState.Unavailable, view.State); Assert.Equal(failure, view.Failure);
            Assert.Equal("motivo " + failure, view.StatusText); Assert.Null(view.Asset); Assert.False(view.IsVisible);
        }
        var (other, b2) = Setup(); using var c2 = other; b2.Fail = new IOException("rete");
        await other.ShowAsync(); Assert.Equal(FlatPatternFailure.Other, other.Failure); Assert.Equal("rete", other.Reason);
    }

    [Fact] // M5-07: late responses
    public async Task LateResponseAfterRevisionDocumentChangeHideOrReloadIsDiscarded()
    {
        var (view, backend) = Setup(); using var cleanup = view; backend.Immediate = null;
        var first = view.ShowAsync(); Assert.Equal(FlatPatternState.Loading, view.State);
        view.SetContext(R2, true); Assert.Equal(FlatPatternState.Hidden, view.State);
        Assert.True(backend.Tokens[0].IsCancellationRequested);
        backend.Pending[0].SetResult(Mesh(rev: "r1")); await first;
        Assert.Equal(FlatPatternState.Hidden, view.State); Assert.Null(view.Asset);

        var second = view.ShowAsync(); view.SetContext(new("other", "r1", "v"), true);
        backend.Pending[1].SetResult(Mesh(rev: "r2")); await second;
        Assert.Equal(FlatPatternState.Hidden, view.State);

        view.SetContext(R1, true);
        var third = view.ShowAsync(); view.Hide(); Assert.Equal(FlatPatternState.Hidden, view.State);
        backend.Pending[2].SetResult(Mesh()); await third; Assert.Equal(FlatPatternState.Hidden, view.State);

        var fourth = view.ShowAsync(); var fifth = view.ShowAsync();
        backend.Pending[3].SetResult(Mesh(hash: "old")); await fourth; Assert.Equal(FlatPatternState.Loading, view.State);
        backend.Pending[4].SetResult(Mesh(hash: "new")); await fifth;
        Assert.Equal(FlatPatternState.Ready, view.State); Assert.Equal("new", view.Asset.ContentHash);
    }

    [Fact]
    public async Task MeshOfAnotherRevisionIsRejectedAsStaleNotShown()
    {
        var (view, backend) = Setup(); using var cleanup = view;
        backend.Immediate = _ => Mesh(rev: "r0");
        await view.ShowAsync();
        Assert.Equal(FlatPatternState.Unavailable, view.State); Assert.Equal(FlatPatternFailure.Stale, view.Failure); Assert.Null(view.Asset);
    }

    [Fact]
    public async Task CancelledRequestNeverSurfacesAnError()
    {
        var (view, backend) = Setup(); using var cleanup = view; backend.Immediate = null;
        var pending = view.ShowAsync(); view.Hide();
        backend.Pending[0].SetCanceled(); await pending;
        Assert.Equal(FlatPatternState.Hidden, view.State); Assert.Null(view.Reason);
    }

    [Fact]
    public async Task RevisionChangeHidesUntilReverifiedAndDocumentChangeResetsDetach()
    {
        var (view, _) = Setup(); using var cleanup = view;
        await view.ShowAsync(); view.Detach(); view.MoveLocal(0.3, 0, 0.1);
        view.SetContext(R2, true);
        Assert.Equal(FlatPatternState.Hidden, view.State); Assert.Null(view.Asset); Assert.False(view.IsVisible);
        Assert.True(view.Detached); Assert.Equal(0.3, view.OffsetX); // same document: choice kept, asset must be re-verified
        await view.ShowAsync(); Assert.Equal("r2", view.RevisionLabel); Assert.Equal(FlatPatternState.Ready, view.State);
        view.SetContext(new("other", "r1", "v"), true);
        Assert.False(view.Detached); Assert.Equal(0, view.OffsetX);
    }

    [Fact] // M5-07: network loss
    public async Task DisconnectKeepsAssetReadOnlyWithRevisionLabelAndReverifiesOnShow()
    {
        var (view, backend) = Setup(); using var cleanup = view;
        await view.ShowAsync();
        view.SetContext(R1, false);
        Assert.Equal(FlatPatternState.StaleReadOnly, view.State); Assert.True(view.IsVisible);
        Assert.Contains("revisione r1", view.StatusText); Assert.Contains("sola ispezione", view.StatusText);
        await Assert.ThrowsAsync<InvalidOperationException>(() => view.ShowAsync());
        view.SetContext(R1, true); Assert.Equal(FlatPatternState.StaleReadOnly, view.State); // not trusted until re-verified
        await view.ShowAsync(); Assert.Equal(FlatPatternState.Ready, view.State);
        // A failed re-verification drops the old asset instead of reusing it.
        view.SetContext(R1, false); view.SetContext(R1, true); backend.Fail = new FlatPatternException(FlatPatternFailure.Missing, "manca");
        await view.ShowAsync(); Assert.Equal(FlatPatternState.Unavailable, view.State); Assert.Null(view.Asset);
    }

    [Fact]
    public async Task DisconnectWhileLoadingDropsTheRequest()
    {
        var (view, backend) = Setup(); using var cleanup = view; backend.Immediate = null;
        var pending = view.ShowAsync(); view.SetContext(R1, false);
        Assert.Equal(FlatPatternState.Hidden, view.State);
        backend.Pending[0].SetResult(Mesh()); await pending;
        Assert.Equal(FlatPatternState.Hidden, view.State); Assert.Null(view.Asset);
    }

    [Fact] // M5-06 core: detach is view-only
    public async Task DetachAndMoveAreLocalViewStateAndNeverTouchTheBackend()
    {
        var (view, backend) = Setup(); using var cleanup = view;
        Assert.False(view.MoveLocal(1, 0, 0)); // not detached, not visible
        await view.ShowAsync(); int calls = backend.Meshes;
        Assert.False(view.MoveLocal(1, 0, 0)); // visible but attached
        view.Detach(); Assert.True(view.Detached);
        Assert.True(view.MoveLocal(0.5, 0.1, -0.2)); Assert.Equal(-0.2, view.OffsetZ);
        Assert.False(view.MoveLocal(double.NaN, 0, 0)); Assert.Equal(0.5, view.OffsetX);
        view.SetContext(R1, false); Assert.True(view.MoveLocal(0.6, 0, 0)); // read-only still repositionable locally
        Assert.Equal(calls, backend.Meshes); Assert.False(view.IsCadSelectable);
        view.SetContext(R1, true); view.Attach(); Assert.False(view.Detached); Assert.Equal(0, view.OffsetX);
        Assert.Equal(calls, backend.Meshes);
    }

    [Fact]
    public async Task CacheIsBoundedAndClearedOnDispose()
    {
        var (view, backend) = Setup();
        for (int i = 0; i < 6; i++) { backend.Immediate = s => Mesh(hash: "h" + i); await view.ShowAsync(); }
        Assert.Equal(4, view.CachedCount);
        view.Dispose(); Assert.Equal(0, view.CachedCount); Assert.Null(view.Asset);
        await Assert.ThrowsAsync<InvalidOperationException>(() => view.ShowAsync());
    }

    [Fact]
    public void InvalidMeshIsRefusedAtConstruction()
    {
        Assert.Throws<FlatPatternException>(() => new FlatPatternMesh("doc", "r1", "", "a", new GlbModel("doc", Array.Empty<GlbPrimitive>()), 1, 1, 0));
        Assert.Throws<FlatPatternException>(() => new FlatPatternMesh("doc", "r1", "h", "a", new GlbModel("x", Array.Empty<GlbPrimitive>()), 1, 1, 0));
        Assert.Throws<FlatPatternException>(() => new FlatPatternMesh("doc", "r1", "h", "a", new GlbModel("doc", Array.Empty<GlbPrimitive>()), 0, 1, 0));
    }
}
