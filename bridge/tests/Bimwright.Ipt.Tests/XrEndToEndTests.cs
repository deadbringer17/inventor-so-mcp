using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Bimwright.Ipt.Server;
using Inventor.So.Mcp.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Tests;

/// <summary>
/// L2 end to end over the real remote host and a real named pipe to <see cref="FakeAddIn"/>: the
/// Quest MVP flow of plan §31 minus Inventor itself - capabilities, scene graph with instanced
/// meshes, asset download, face pick, plan/preview/commit, incremental refresh, stale revision.
/// </summary>
public sealed class XrEndToEndTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "so-e2e-" + System.Guid.NewGuid().ToString("N"));
    private readonly string _token = TokenRegistry.Generate();
    private FakeAddIn _addIn = null!;
    private WebApplication _app = null!;
    private HttpClient _http = null!;
    private string _session = "";
    private int _id = 10;

    public async Task InitializeAsync()
    {
        _addIn = new FakeAddIn(Path.Combine(_root, "targets"));
        var config = new InventorMcpConfig
        {
            HttpUrls = { "http://127.0.0.1:0" },
            DescriptorDirectory = _addIn.DescriptorDirectory,
            AssetDirectory = Path.Combine(_root, "assets"),
            AuditDirectory = Path.Combine(_root, "audit"),
            EnableExperimental = true,
        };
        var tokens = new TokenRegistry();
        tokens.Add("quest", _token);
        _app = HttpHost.Build(System.Array.Empty<string>(), config, tokens);
        await _app.StartAsync();
        var address = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        _http = new HttpClient { BaseAddress = new System.Uri(address) };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        var init = await Rpc("initialize", new { protocolVersion = "2025-06-18", capabilities = new { }, clientInfo = new { name = "e2e", version = "1" } });
        Assert.NotNull(init["result"]);
        await Rpc("notifications/initialized", null, notification: true);
    }

    public async Task DisposeAsync()
    {
        _http.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
        await _addIn.DisposeAsync();
        try { Directory.Delete(_root, true); } catch { }
    }

    private async Task<JObject> Rpc(string method, object? parameters, bool notification = false)
    {
        var message = new JObject { ["jsonrpc"] = "2.0", ["method"] = method };
        if (!notification) message["id"] = ++_id;
        if (parameters != null) message["params"] = JObject.FromObject(parameters);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp") { Content = new StringContent(message.ToString(), Encoding.UTF8, "application/json") };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        if (_session.Length > 0) request.Headers.Add("Mcp-Session-Id", _session);
        using var response = await _http.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, text);
        if (response.Headers.TryGetValues("Mcp-Session-Id", out var values)) _session = values.First();
        if (notification) return new JObject();
        return response.Content.Headers.ContentType?.MediaType == "text/event-stream"
            ? text.Split('\n').Where(l => l.StartsWith("data:")).Select(l => JObject.Parse(l[5..])).Last()
            : JObject.Parse(text);
    }

    private async Task<JObject> Tool(string name, object arguments)
    {
        var reply = await Rpc("tools/call", new { name, arguments });
        return JObject.Parse((string)reply["result"]!["content"]![0]!["text"]!);
    }

    private async Task<byte[]> Asset(string url)
    {
        var response = await _http.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsByteArrayAsync();
    }

    [Fact]
    public async Task QuestMvpFlowWorksAgainstAFakeAddIn()
    {
        var caps = await Tool("inventor_get_capabilities", new { });
        Assert.True((bool)caps["capabilities"]!["xr_mesh"]!);
        Assert.True((bool)caps["capabilities"]!["scene_graph"]!);

        // Scene graph with instanced meshes: two bolts share one definition asset.
        var scene = await Tool("inventor_get_scene_graph", new { include_meshes = true });
        var definitions = (JArray)scene["definitions"]!;
        Assert.Equal(2, definitions.Count);
        var boltBefore = (string)definitions.Single(d => (string?)d["definition_document_id"] == FakeAddIn.BoltId)["mesh_asset_id"]!;
        var plateBefore = (string)definitions.Single(d => (string?)d["definition_document_id"] == FakeAddIn.PlateId)["mesh_asset_id"]!;
        var bolt2 = scene["root"]!["children"]![1]!;
        Assert.Equal(0.03, (double)bolt2["matrix_gltf"]![12]!, 9);   // 3 cm -> 0.03 m

        var composed = await Asset((string)scene["scene_asset"]!["asset_url"]!);
        var (gltf, _) = GlbReader.Read(composed);
        Assert.Equal(2, ((JArray)gltf["meshes"]!).Count);
        var boltNodes = gltf["nodes"]!.Where(n => ((string?)n["name"])!.StartsWith("Bolt")).ToArray();
        Assert.Equal((int)boltNodes[0]["mesh"]!, (int)boltNodes[1]["mesh"]!);

        // Per-definition GLB carries the face table used for triangle -> face picking.
        var boltGlb = await Asset((string)definitions.Single(d => (string?)d["mesh_asset_id"] == boltBefore)["asset"]!["asset_url"]!);
        var (boltGltf, _) = GlbReader.Read(boltGlb);
        var faces = boltGltf["meshes"]![0]!["primitives"]![0]!["extras"]!["faces"]!;
        Assert.Equal(6, faces.Count());
        string faceId = (string)faces[2]!["face_id"]!;

        var pick = await Tool("inventor_pick_entity", new { occurrence_id = "ent_occ_2", face_id = faceId });
        Assert.Equal("face_proxy", (string?)pick["type"]);
        var highlight = await Tool("inventor_highlight_entity", new { entity_ids = new[] { (string)pick["entity_id"]! } });
        Assert.Equal(1, (int)highlight["highlighted"]!);

        // Plan, preview, commit.
        var info = await Tool("inventor_get_document_info", new { });
        string revision = (string)info["revision"]!;
        var plan = await Tool("inventor_plan_change", new
        {
            document_id = FakeAddIn.AssemblyId, expected_revision = revision,
            operations = new[] { new { command = "set_parameter", arguments = new { name = "BoltLength", value = "20 mm" } } },
            intent = "longer bolts",
        });
        Assert.Equal("preview_rolled_back", (string?)plan["preview"]!["status"]);
        Assert.Equal(revision, _addIn.Revision);   // preview changed nothing
        var commit = await Tool("inventor_commit_plan", new { plan_id = (string)plan["plan_id"]!, document_id = FakeAddIn.AssemblyId, expected_revision = revision });
        Assert.Equal("committed", (string?)commit["status"]);
        Assert.NotEqual(revision, (string?)commit["revision"]);

        // Incremental refresh: only the changed definition gets a new asset id.
        var after = await Tool("inventor_get_scene_graph", new { include_meshes = true });
        var afterDefinitions = (JArray)after["definitions"]!;
        Assert.NotEqual(boltBefore, (string)afterDefinitions.Single(d => (string?)d["definition_document_id"] == FakeAddIn.BoltId)["mesh_asset_id"]!);
        Assert.Equal(plateBefore, (string)afterDefinitions.Single(d => (string?)d["definition_document_id"] == FakeAddIn.PlateId)["mesh_asset_id"]!);

        // A plan made at the old revision is refused, and a used plan cannot be committed twice.
        var stale = await Tool("inventor_plan_change", new
        {
            document_id = FakeAddIn.AssemblyId, expected_revision = revision,
            operations = new[] { new { command = "set_parameter", arguments = new { name = "BoltLength", value = "25 mm" } } },
        });
        Assert.Equal("STALE_REVISION", (string?)stale["error"]!["code"]);
        var again = await Tool("inventor_commit_plan", new { plan_id = (string)plan["plan_id"]!, document_id = FakeAddIn.AssemblyId, expected_revision = revision });
        Assert.Equal("PLAN_NOT_FOUND", (string?)again["error"]!["code"]);

        // The whole write path is audited under the token name.
        var audit = _app.Services.GetRequiredService<Bimwright.Ipt.Server.Audit.AuditLog>();
        var records = File.ReadAllLines(audit.CurrentFile!).Select(JObject.Parse).ToArray();
        Assert.Contains(records, r => (string?)r["tool"] == "inventor_commit_plan" && (string?)r["result"] == "committed" && (string?)r["client"] == "quest");
    }

    [Fact]
    public async Task FlatPatternMeshIsItsOwnFaceIdFreeAssetAndRefusalsKeepTheirReason()
    {
        var folded = await Tool("inventor_get_display_mesh", new { document_id = FakeAddIn.PlateId });
        var flat = await Tool("inventor_get_flat_pattern_mesh", new { document_id = FakeAddIn.PlateId });
        Assert.Equal("m", (string?)flat["units"]);
        Assert.Equal("SurfaceBodies", (string?)flat["source"]);
        Assert.Equal("fake-content", (string?)flat["flat_pattern_identity"]!["content_hash"]);
        Assert.Equal(12, (int)flat["triangle_count"]!);
        Assert.NotEqual((string)folded["asset"]!["asset_id"]!, (string)flat["asset"]!["asset_id"]!);

        var (gltf, _) = GlbReader.Read(await Asset((string)flat["asset"]!["asset_url"]!));
        foreach (var face in (JArray)gltf["meshes"]![0]!["primitives"]![0]!["extras"]!["faces"]!)
            Assert.Equal(JTokenType.Null, face["face_id"]!.Type);

        var missing = await Tool("inventor_get_flat_pattern_mesh", new { document_id = FakeAddIn.BoltId });
        Assert.Equal("INVALID_ARGUMENT", (string?)missing["error"]!["code"]);
        Assert.Equal("FLAT_PATTERN_MISSING", (string?)missing["error"]!["details"]!["reason"]);
        var wrong = await Tool("inventor_get_flat_pattern_mesh", new { document_id = FakeAddIn.AssemblyId });
        Assert.Equal("WRONG_DOCUMENT_TYPE", (string?)wrong["error"]!["code"]);
        var range = await Tool("inventor_get_flat_pattern_mesh", new { tolerance_mm = 9.0 });
        Assert.Equal("INVALID_ARGUMENT", (string?)range["error"]!["code"]);
    }

    [Fact]
    public async Task DisplayMeshOfOnePartIsAValidGlbInMetres()
    {
        var mesh = await Tool("inventor_get_display_mesh", new { document_id = FakeAddIn.PlateId });
        Assert.Equal("m", (string?)mesh["units"]);
        Assert.Equal(12, (int)mesh["bodies"]![0]!["triangle_count"]!);
        var glb = await Asset((string)mesh["asset"]!["asset_url"]!);
        var (gltf, _) = GlbReader.Read(glb);
        var position = (int)gltf["meshes"]![0]!["primitives"]![0]!["attributes"]!["POSITION"]!;
        Assert.Equal(0.05, (double)gltf["accessors"]![position]!["max"]![0]!, 6);   // 5 cm plate

        var wrong = await Tool("inventor_get_display_mesh", new { document_id = FakeAddIn.AssemblyId });
        Assert.Equal("WRONG_DOCUMENT_TYPE", (string?)wrong["error"]!["code"]);
    }
}
