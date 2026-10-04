using Bimwright.Ipt.Tests;
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Mcp;
using InventorXrSo.Core.Tests.Support;
using Newtonsoft.Json.Linq;

namespace InventorXrSo.Core.Tests.Backend;

public class VerifyBackendTests : IClassFixture<BackendFixture>
{
    private readonly BackendFixture _fixture;
    public VerifyBackendTests(BackendFixture fixture) { _fixture = fixture; }

    private async Task<(InventorBackend backend, DocumentState state)> Connect()
    {
        var backend = new InventorBackend(_fixture.Transport(), _fixture.EditorServer, new MemoryAssetCache());
        await backend.ConnectAsync(default);
        return (backend, await backend.GetDocumentStateAsync(default));
    }

    private sealed class TimeoutSpy : InventorXrSo.Core.Net.IHttpTransport
    {
        private readonly InventorXrSo.Core.Net.IHttpTransport _inner;
        public readonly Dictionary<string, TimeSpan> ToolTimeouts = new();
        public TimeoutSpy(InventorXrSo.Core.Net.IHttpTransport inner) { _inner = inner; }

        public Task<InventorXrSo.Core.Net.TransportResponse> SendAsync(InventorXrSo.Core.Net.TransportRequest request, CancellationToken ct)
        {
            var text = request.Body == null ? "" : System.Text.Encoding.UTF8.GetString(request.Body);
            foreach (var tool in new[] { "inventor_check_interference_xr", "inventor_measure_min_distance_xr", "inventor_assembly_health_xr" })
                if (text.Contains("\"" + tool + "\"")) ToolTimeouts[tool] = request.Timeout;
            return _inner.SendAsync(request, ct);
        }

        public Task<int> StreamLinesAsync(InventorXrSo.Core.Net.TransportRequest request, Action<string> onLine, CancellationToken ct) =>
            _inner.StreamLinesAsync(request, onLine, ct);
    }

    [Fact]
    public async Task TheThreeVerificationsAskForSeventyFiveSeconds()
    {
        var spy = new TimeoutSpy(_fixture.Transport());
        var backend = new InventorBackend(spy, _fixture.EditorServer, new MemoryAssetCache());
        await backend.ConnectAsync(default);
        var state = await backend.GetDocumentStateAsync(default);
        await backend.CheckInterferenceAsync(state, null, default);
        await backend.MeasureMinDistanceAsync(state, "ent_occ_1", "ent_occ_2", default);
        await backend.GetAssemblyHealthAsync(state, default);
        Assert.Equal(3, spy.ToolTimeouts.Count);
        Assert.All(spy.ToolTimeouts.Values, t => Assert.Equal(TimeSpan.FromSeconds(75), t));
    }

    [Fact]
    public async Task InterferenceCarriesIdsVolumeAndBoxesAndIsRevisionBound()
    {
        var (backend, state) = await Connect();
        var report = await backend.CheckInterferenceAsync(state, null, default);
        Assert.Equal(state.Revision, report.Revision);
        Assert.Equal(1, report.Count); Assert.Equal(3, report.Analyzed);
        var pair = Assert.Single(report.Pairs);
        Assert.Equal("ent_occ_1", pair.AOccurrenceId); Assert.Equal("ent_occ_3", pair.BOccurrenceId);
        Assert.Equal(2000.0, pair.VolumeMm3);
        var box = Assert.Single(pair.Boxes);
        Assert.Equal(new[] { 15.0, 0, 0 }, box.MinMm); Assert.Equal(new[] { 20.0, 20, 20 }, box.MaxMm);
        var stale = await Assert.ThrowsAsync<McpToolException>(() => backend.CheckInterferenceAsync(new DocumentState(state.DocumentId, "old", "v"), null, default));
        Assert.Equal("STALE_REVISION", stale.Code);
        Assert.DoesNotContain("atomic_batch", _fixture.AddIn.Commands);
    }

    [Fact]
    public async Task DistanceReportsPointsWhenInventorGivesThem()
    {
        var (backend, state) = await Connect();
        var report = await backend.MeasureMinDistanceAsync(state, "ent_occ_1", "ent_occ_2", default);
        Assert.Equal(30.0, report.DistanceMm);
        Assert.True(report.HasPoints);
        Assert.Equal(new[] { 30.0, 0, 0 }, report.PointBMm);
        var same = await Assert.ThrowsAsync<McpToolException>(() => backend.MeasureMinDistanceAsync(state, "ent_occ_1", "ent_occ_1", default));
        Assert.Equal("INVALID_ARGUMENT", same.Code);
    }

    [Fact]
    public async Task HealthMergesRelationshipsUnconstrainedAndBom()
    {
        var (backend, state) = await Connect();
        var report = await backend.GetAssemblyHealthAsync(state, default);
        Assert.False(report.Healthy);
        var issue = Assert.Single(report.Issues);
        Assert.Equal("constraint", issue.Kind); Assert.Equal("M7_Sick", issue.Name);
        Assert.Equal("ent_occ_1", issue.AOccurrenceId); Assert.Equal("ent_occ_3", issue.BOccurrenceId);
        Assert.Equal("ent_occ_2", Assert.Single(report.Unconstrained).OccurrenceId);
        // The real get_assembly_bom replaces a blank part number with the file name, so the BOM finding is a missing description (a warning).
        Assert.True(report.BomValid);
        var bom = Assert.Single(report.BomIssues);
        Assert.Equal("DESCRIPTION_MISSING", bom.Code); Assert.Equal("warning", bom.Severity);
    }

    [Fact]
    public async Task HealthRefusesWhenTheRevisionMovesBetweenTheBomAndTheFinalRecheck()
    {
        var (backend, state) = await Connect();
        _fixture.AddIn.BumpRevisionOnNextBom = true;
        var stale = await Assert.ThrowsAsync<McpToolException>(() => backend.GetAssemblyHealthAsync(state, default));
        Assert.Equal("STALE_REVISION", stale.Code);
        Assert.False(_fixture.AddIn.BumpRevisionOnNextBom, "the hook is one-shot");
    }

    [Fact]
    public async Task HealthRefusesWhenTheDocumentChangesWhileReading()
    {
        var (backend, state) = await Connect();
        _fixture.AddIn.RaiseDocumentChanged(geometry: false);
        var stale = await Assert.ThrowsAsync<McpToolException>(() => backend.GetAssemblyHealthAsync(state, default));
        Assert.Equal("STALE_REVISION", stale.Code);
    }

    [Fact]
    public void MissingPointsAndBoxesStayUnknown()
    {
        var distance = DistanceReport.FromJson(new JObject { ["revision"] = "r", ["distance_mm"] = 4.5, ["point_a"] = null, ["points_source"] = "unavailable" });
        Assert.False(distance.HasPoints); Assert.Null(distance.PointAMm);
        var interference = InterferenceReport.FromJson(new JObject { ["revision"] = "r", ["count"] = 1,
            ["pairs"] = new JArray(new JObject { ["a_occurrence_id"] = "a", ["b_occurrence_id"] = "b", ["volume_mm3"] = 1.0, ["boxes"] = new JArray(new JObject { ["min_mm"] = new JArray(1, 2) }) }) });
        Assert.Empty(Assert.Single(interference.Pairs).Boxes);
    }
}
