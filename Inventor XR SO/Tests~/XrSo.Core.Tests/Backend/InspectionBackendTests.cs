using Bimwright.Ipt.Tests;
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Mcp;
using InventorXrSo.Core.Tests.Support;
using Newtonsoft.Json.Linq;

namespace InventorXrSo.Core.Tests.Backend;

public class InspectionBackendTests : IClassFixture<BackendFixture>
{
    private readonly BackendFixture _fixture;
    public InspectionBackendTests(BackendFixture fixture) { _fixture = fixture; }
    [Fact]
    public async Task InspectUsesRevisionAndDoesNotRunCadWrites()
    {
        var backend = new InventorBackend(_fixture.Transport(), _fixture.EditorServer, new MemoryAssetCache());
        await backend.ConnectAsync(default);
        var state = await backend.GetDocumentStateAsync(default);
        var info = await backend.InspectAsync(state, "ent_occ_1", default);
        Assert.Equal(1.28, info.MassKg); Assert.Equal("Steel", info.Material);
        Assert.Equal(1, info.TranslationDof); Assert.Equal(0, info.RotationDof);
        var stale = await Assert.ThrowsAsync<McpToolException>(() => backend.InspectAsync(new DocumentState(state.DocumentId, "old", "v"), null, default));
        Assert.Equal("STALE_REVISION", stale.Code);
        var changed = await Assert.ThrowsAsync<McpToolException>(() => backend.InspectAsync(new DocumentState("other", state.Revision, "v"), null, default));
        Assert.Equal("DOCUMENT_CHANGED", changed.Code);
        Assert.Equal(state.Revision, (await backend.GetDocumentStateAsync(default)).Revision);
        Assert.DoesNotContain("atomic_batch", _fixture.AddIn.Commands);
    }
    [Fact]
    public async Task ListsStableDocumentIdsAndRefusesActivationOfUnknownDocument()
    {
        var backend = new InventorBackend(_fixture.Transport(), _fixture.EditorServer, new MemoryAssetCache());
        await backend.ConnectAsync(default);
        var docs = await backend.ListOpenAsync(default);
        Assert.Equal(FakeAddIn.AssemblyId, Assert.Single(docs).Id);
        await backend.ActivateOpenAsync(docs[0].Id, default);
        await Assert.ThrowsAsync<McpToolException>(() => backend.ActivateOpenAsync("missing", default));
    }
    [Fact]
    public void UnavailablePropertiesStayUnknown()
    {
        var info = InspectionInfo.FromJson(new JObject());
        Assert.Null(info.MassKg); Assert.Null(info.TranslationDof); Assert.Null(info.Material);
    }
}
