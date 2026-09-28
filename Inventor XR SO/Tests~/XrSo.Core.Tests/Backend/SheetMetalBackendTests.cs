using Bimwright.Ipt.Tests;
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Tests.Support;

namespace InventorXrSo.Core.Tests.Backend;

public class SheetMetalBackendTests : IClassFixture<BackendFixture>
{
    private readonly BackendFixture _fixture;
    public SheetMetalBackendTests(BackendFixture fixture) { _fixture = fixture; }

    private async Task<(InventorBackend backend, DocumentState state)> Connect()
    {
        var backend = new InventorBackend(_fixture.Transport(), _fixture.EditorServer, new MemoryAssetCache());
        await backend.ConnectAsync(default);
        return (backend, await backend.GetDocumentStateAsync(default));
    }

    [Fact]
    public async Task FlatPatternMeshCrossesHttpsWithNativeMeasuresAndSeparateCache()
    {
        var (backend, state) = await Connect();
        var mesh = await backend.GetFlatPatternMeshAsync(new DocumentState(FakeAddIn.PlateId, state.Revision, state.VisualRevision), default);
        Assert.Equal(FakeAddIn.PlateId, mesh.DocumentId); Assert.Equal(state.Revision, mesh.Revision);
        Assert.Equal("fake-content", mesh.ContentHash); Assert.Equal(50, mesh.LengthMm); Assert.Equal(1, mesh.BendCount);
        Assert.Equal(2.0, mesh.ThicknessMm); Assert.NotEmpty(mesh.Model.Primitives);
        Assert.All(mesh.Model.Primitives.SelectMany(p => p.Faces), f => Assert.Null(f.FaceId));
    }

    [Fact]
    public async Task StaleRevisionIsRefusedAsStale()
    {
        var (backend, state) = await Connect();
        var ex = await Assert.ThrowsAsync<FlatPatternException>(() =>
            backend.GetFlatPatternMeshAsync(new DocumentState(FakeAddIn.PlateId, "old", "v"), default));
        Assert.Equal(FlatPatternFailure.Stale, ex.Failure);
    }

    [Fact]
    public async Task MissingPatternAndWrongDocumentTypeAreDistinctOutcomes()
    {
        var (backend, state) = await Connect();
        var missing = await Assert.ThrowsAsync<FlatPatternException>(() =>
            backend.GetFlatPatternMeshAsync(new DocumentState(FakeAddIn.BoltId, state.Revision, null), default));
        Assert.Equal(FlatPatternFailure.Missing, missing.Failure);
        var wrong = await Assert.ThrowsAsync<FlatPatternException>(() =>
            backend.GetFlatPatternMeshAsync(new DocumentState(FakeAddIn.AssemblyId, state.Revision, null), default));
        Assert.Equal(FlatPatternFailure.WrongDocumentType, wrong.Failure);
        Assert.NotEqual(missing.Message, wrong.Message);
    }

    [Fact]
    public async Task FlatViewDrivenByRealBackendShowsThenHidesOnRevisionChange()
    {
        var (backend, state) = await Connect();
        using var view = new FlatPatternView(backend);
        var plate = new DocumentState(FakeAddIn.PlateId, state.Revision, state.VisualRevision);
        view.SetContext(plate, true); await view.ShowAsync();
        Assert.Equal(FlatPatternState.Ready, view.State);
        view.SetContext(new DocumentState(FakeAddIn.PlateId, "other", null), true);
        Assert.Equal(FlatPatternState.Hidden, view.State);
    }
}
