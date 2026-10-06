using Bimwright.Ipt.Server;
using Bimwright.Ipt.Server.Tools;
using Bimwright.Ipt.Shared.Contracts;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Tests;

public sealed class ReleasePackageTests : IAsyncDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ipt-release-" + Guid.NewGuid().ToString("N"));
    private readonly FakeAddIn _addIn;
    private readonly PluginClient _client;
    private readonly InventorMcpConfig _config;

    public ReleasePackageTests()
    {
        _addIn = new FakeAddIn(_dir);
        _config = new InventorMcpConfig { DescriptorDirectory = _dir, TimeoutMs = 5000, ReleaseDirectory = Path.Combine(_dir, "releases") };
        _client = new PluginClient(_config);
    }

    public async ValueTask DisposeAsync()
    {
        await _addIn.DisposeAsync();
        try { Directory.Delete(_dir, true); } catch { }
    }

    private sealed class Caller : ICallerIdentity { public string Client => "test"; public string? SessionId => null; }

    private void ScriptPart(Func<InventorCommandEnvelope, InventorCommandResult?> dxf)
    {
        _addIn.Override = env =>
        {
            if (env.Command == "get_document_info")
                return InventorCommandResult.Success(env.Id, new JObject { ["id"] = "doc_part", ["revision"] = "fake:1", ["document_type"] = "kPartDocumentObject", ["title"] = "P.ipt", ["dirty"] = false }, new InventorResponseMeta());
            if (env.Command == "save_artifact" && (string?)env.Params?["format"] == "dxf") return dxf(env);
            return null;
        };
    }

    private async Task<JObject> Build()
    {
        var tools = new ReleaseTools(_client, _config, new Caller());
        return JObject.Parse(await tools.BuildReleasePackage("doc_part", "fake:1", "t", new[] { "dxf" }));
    }

    private static JObject DxfEntry(JObject result) =>
        ((JArray)result["metadata"]!["excluded"]!).OfType<JObject>().First(e => (string?)e["artifact"] == "flat.dxf");

    [Theory]
    [InlineData(InventorErrorCodes.TIMEOUT)]
    [InlineData(InventorErrorCodes.STALE_REVISION)]
    public async Task A_dxf_timeout_or_stale_revision_keeps_the_package_incomplete(string code)
    {
        ScriptPart(env => InventorCommandResult.Fail(env.Id, code, "boom", new InventorResponseMeta()));
        var result = await Build();
        Assert.False((bool)result["complete"]!);
        Assert.False((bool)result["validation"]!["complete"]!);
        Assert.Equal("export failed", (string?)DxfEntry(result)["reason"]);
    }

    [Fact]
    public async Task A_non_sheet_metal_part_is_an_exclusion_not_a_failure()
    {
        ScriptPart(env => InventorCommandResult.Fail(env.Id, InventorErrorCodes.WRONG_DOCUMENT_TYPE, "DXF output covers the flat pattern of an active sheet-metal part only.", new InventorResponseMeta()));
        var result = await Build();
        Assert.Equal("not a sheet-metal part with a flat pattern", (string?)DxfEntry(result)["reason"]);
        Assert.True((bool)result["complete"]!);
    }

    [Fact]
    public async Task A_missing_flat_pattern_is_an_exclusion_not_a_failure()
    {
        ScriptPart(env => InventorCommandResult.Fail(env.Id, InventorErrorCodes.INVALID_ARGUMENT, "NO_FLAT_PATTERN: create the flat pattern first.",
            new JObject { ["reason"] = "NO_FLAT_PATTERN" }, new InventorResponseMeta()));
        var result = await Build();
        Assert.Equal("not a sheet-metal part with a flat pattern", (string?)DxfEntry(result)["reason"]);
        Assert.True((bool)result["complete"]!);
    }

    [Fact]
    public async Task An_unrelated_invalid_argument_stays_a_failure()
    {
        ScriptPart(env => InventorCommandResult.Fail(env.Id, InventorErrorCodes.INVALID_ARGUMENT, "dxf_version is not supported.", new InventorResponseMeta()));
        var result = await Build();
        Assert.Equal("export failed", (string?)DxfEntry(result)["reason"]);
        Assert.False((bool)result["complete"]!);
    }

    [Fact]
    public async Task A_bom_write_failure_is_recorded_and_the_package_is_incomplete()
    {
        _addIn.Override = env =>
        {
            if (env.Command == "get_document_info")
                return InventorCommandResult.Success(env.Id, new JObject { ["id"] = "doc_asm", ["revision"] = "fake:1", ["document_type"] = "kAssemblyDocumentObject", ["title"] = "A.iam", ["dirty"] = false }, new InventorResponseMeta());
            if (env.Command == "get_assembly_bom")
            {
                // The package folder exists by now: occupy bom.csv with a directory so the write fails.
                foreach (var folder in Directory.GetDirectories(_config.ReleaseDirectory))
                    Directory.CreateDirectory(Path.Combine(folder, "bom.csv"));
            }
            return null;
        };
        var tools = new ReleaseTools(_client, _config, new Caller());
        var result = JObject.Parse(await tools.BuildReleasePackage("doc_asm", "fake:1", "t", new[] { "bom" }));
        Assert.False((bool)result["complete"]!);
        Assert.Contains(((JArray)result["metadata"]!["excluded"]!).OfType<JObject>(), e => (string?)e["artifact"] == "bom.csv" && (string?)e["reason"] != "not requested");
    }
}
