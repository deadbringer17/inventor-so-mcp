using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Bimwright.Ipt.Server;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Handlers.Core;
using Inventor.So.Mcp.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Tests;

/// <summary>
/// M9 gate M9-08 at the levels available without Inventor: the pure rules of <c>face_feature</c>
/// (classification, editable, units, JSON shape), the tool contract, and the wire path through the
/// real server and a pipe to <see cref="FakeAddIn"/>. The Inventor COM reads (Face.CreatedByFeature,
/// parameter properties per feature type) are NOT covered here: see the M3LiveProbe --face-feature case.
/// </summary>
public sealed class FaceFeatureModelTests
{
    [Theory]
    [InlineData("kExtrudeFeatureObject", "extrude")]
    [InlineData("kRevolveFeatureObject", "revolve")]
    [InlineData("kFilletFeatureObject", "fillet")]
    [InlineData("kChamferFeatureObject", "chamfer")]
    [InlineData("kHoleFeatureObject", "hole")]
    [InlineData("kRectangularPatternFeatureObject", "rectangular_pattern")]
    [InlineData("kCircularPatternFeatureObject", "circular_pattern")]
    [InlineData("kFlangeFeatureObject", "flange")]
    public void SupportedTypesMapToTheSpecTable(string objectType, string wire)
    {
        Assert.Equal(wire, FaceFeatureModel.SupportedType(objectType));
        Assert.True(FaceFeatureModel.RolesByType.ContainsKey(wire));
    }

    [Theory]
    [InlineData("kSweepFeatureObject", "sweep")]
    [InlineData("kLoftFeatureObject", "loft")]
    [InlineData("kMirrorFeatureObject", "mirror")]
    [InlineData("kCoilFeatureObject", "coil")]
    [InlineData("kThreadFeatureObject", "thread")]
    [InlineData("kDeleteFaceFeatureObject", "delete_face")]
    public void OtherTypesAreUnsupportedButStillNamed(string objectType, string readable)
    {
        Assert.Null(FaceFeatureModel.SupportedType(objectType));
        Assert.Equal(readable, FaceFeatureModel.GenericType(objectType));
    }

    [Fact]
    public void RolesFollowTheSpecTable()
    {
        Assert.Equal(new[] { "distance" }, FaceFeatureModel.RolesByType["extrude"]);
        Assert.Equal(new[] { "angle" }, FaceFeatureModel.RolesByType["revolve"]);
        Assert.Equal(new[] { "radius" }, FaceFeatureModel.RolesByType["fillet"]);
        Assert.Equal(new[] { "distance" }, FaceFeatureModel.RolesByType["chamfer"]);
        Assert.Equal(new[] { "diameter", "depth" }, FaceFeatureModel.RolesByType["hole"]);
        Assert.Equal(new[] { "count", "spacing" }, FaceFeatureModel.RolesByType["rectangular_pattern"]);
        Assert.Equal(new[] { "count", "angle" }, FaceFeatureModel.RolesByType["circular_pattern"]);
        Assert.Equal(new[] { "distance", "angle" }, FaceFeatureModel.RolesByType["flange"]);
    }

    [Theory]
    [InlineData("kNonParametricBaseFeatureObject", true)]
    [InlineData("kDerivedPartFeatureObject", true)]
    [InlineData("kReferenceFeatureObject", true)]
    [InlineData("kExtrudeFeatureObject", false)]
    [InlineData("kSweepFeatureObject", false)]
    [InlineData(null, false)]
    public void BodiesWithoutAnOperationHaveNoOwningFeature(string? objectType, bool expected)
        => Assert.Equal(expected, FaceFeatureModel.IsBodyWithoutOperation(objectType));

    [Theory]
    [InlineData("20 mm", true)]
    [InlineData("20mm", true)]
    [InlineData("  2.5 cm ", true)]
    [InlineData("-3", true)]
    [InlineData("6", true)]
    [InlineData("45 deg", true)]
    [InlineData("0,5 mm", true)]
    [InlineData("d1*2", false)]
    [InlineData("d1 * 2", false)]
    [InlineData("d1", false)]
    [InlineData("20 mm + 5 mm", false)]
    [InlineData("sqrt(4 ul)", false)]
    [InlineData("2 * 10 mm", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void OnlyAPlainValueIsASimpleExpression(string? expression, bool simple)
        => Assert.Equal(simple, FaceFeatureModel.IsSimpleExpression(expression));

    [Theory]
    [InlineData("20 mm", false, true, true)]
    [InlineData("d1*2", false, true, false)]
    [InlineData("20 mm", true, true, false)]
    [InlineData("20 mm", false, false, false)]
    [InlineData(null, false, true, false)]
    public void EditableNeedsSimpleValueAndAHealthyActiveFeature(string? expression, bool suppressed, bool healthy, bool editable)
        => Assert.Equal(editable, FaceFeatureModel.IsEditable(expression, suppressed, healthy));

    [Fact]
    public void ValuesAreConvertedToMillimetresDegreesAndCounts()
    {
        Assert.Equal(20.0, FaceFeatureModel.ToWireValue("distance", 2.0));
        Assert.Equal(1.0, FaceFeatureModel.ToWireValue("radius", 0.1));
        Assert.Equal(5.0, FaceFeatureModel.ToWireValue("diameter", 0.5));
        Assert.Equal(12.5, FaceFeatureModel.ToWireValue("spacing", 1.25));
        Assert.Equal(180.0, FaceFeatureModel.ToWireValue("angle", System.Math.PI));
        Assert.Equal(6.0, FaceFeatureModel.ToWireValue("count", 6));
        Assert.Equal("mm", FaceFeatureModel.UnitOf("distance"));
        Assert.Equal("deg", FaceFeatureModel.UnitOf("angle"));
        Assert.Equal("", FaceFeatureModel.UnitOf("count"));
    }

    [Fact]
    public void ResultMatchesTheSpecExample()
    {
        var json = FaceFeatureModel.Result("Estrusione1", "extrude", false, true,
            new[] { new FaceFeatureModel.RawParameter("d3", "distance", 2.0, "20 mm") }, "Schizzo1");
        Assert.Equal(JToken.Parse(@"{
            ""feature"": { ""name"": ""Estrusione1"", ""type"": ""extrude"", ""suppressed"": false, ""healthy"": true },
            ""parameters"": [ { ""name"": ""d3"", ""role"": ""distance"", ""value"": 20.0, ""unit"": ""mm"",
                                ""expression"": ""20 mm"", ""editable"": true } ],
            ""previous_feature"": ""Schizzo1"" }").ToString(), json.ToString());
    }

    [Fact]
    public void FirstFeatureHasNoPreviousAndRolesOutsideTheTableAreDropped()
    {
        var json = FaceFeatureModel.Result("Estrusione1", "extrude", false, true, new[]
        {
            new FaceFeatureModel.RawParameter("d3", "distance", 2.0, "20 mm"),
            new FaceFeatureModel.RawParameter("d4", "angle", 0.1, "5 deg"),
        }, null);
        Assert.Single((JArray)json["parameters"]!);
        Assert.Equal(JTokenType.Null, json["previous_feature"]!.Type);
    }

    [Fact]
    public void PreviousFeatureFollowsBrowserOrder()
    {
        var names = new[] { "Estrusione1", "Raccordo1", "Foro1" };
        Assert.Null(FaceFeatureModel.PreviousOf(names, "Estrusione1"));
        Assert.Equal("Estrusione1", FaceFeatureModel.PreviousOf(names, "Raccordo1"));
        Assert.Equal("Raccordo1", FaceFeatureModel.PreviousOf(names, "Foro1"));
        Assert.Null(FaceFeatureModel.PreviousOf(names, "Missing"));
    }

    [Fact]
    public void FailuresCarryCodesAndTheUnsupportedOneKeepsNameAndType()
    {
        var none = FaceFeatureModel.NoOwningFeature();
        Assert.Equal(InventorErrorCodes.NO_OWNING_FEATURE, none.Code);
        var unsupported = FaceFeatureModel.Unsupported("Sweep1", "sweep", false, true, "Schizzo2");
        Assert.Equal(InventorErrorCodes.UNSUPPORTED_FEATURE, unsupported.Code);
        Assert.Equal("Sweep1", (string?)unsupported.Details!["feature"]!["name"]);
        Assert.Equal("sweep", (string?)unsupported.Details!["feature"]!["type"]);
        Assert.Equal("Schizzo2", (string?)unsupported.Details!["previous_feature"]);
    }
}

public sealed class FaceFeatureContractTests
{
    [Fact]
    public void ToolIsExperimentalRevisionBoundReadOnlyAndUnverified()
    {
        Assert.Equal(ToolContracts.Experimental, SoToolPolicy.TierOf("inventor_face_feature"));
        var contract = ToolContracts.Contracts.Single(c => c.Name == "inventor_face_feature");
        Assert.Equal("query", contract.Access);
        Assert.True(contract.RequiresRevision);
        Assert.Contains("pending", contract.Verification);   // not live-verified: gate M9-08 stays open
    }
}

/// <summary>Wire path: MCP tool -> server -> named pipe -> FakeAddIn, over the real HTTP host.</summary>
public sealed class FaceFeatureWireTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "so-ff-" + System.Guid.NewGuid().ToString("N"));
    private readonly string _token = TokenRegistry.Generate();
    private FakeAddIn _addIn = null!;
    private WebApplication _app = null!;
    private HttpClient _http = null!;
    private string _session = "";
    private int _id = 10;

    public async Task InitializeAsync()
    {
        _addIn = new FakeAddIn(Path.Combine(_root, "targets")) { DesignPartMode = true };
        var config = new InventorMcpConfig
        {
            HttpUrls = new() { "http://127.0.0.1:0" },   // replace the default 8787, which parallel host tests also bind
            DescriptorDirectory = _addIn.DescriptorDirectory,
            AssetDirectory = Path.Combine(_root, "assets"),
            AuditDirectory = Path.Combine(_root, "audit"),
            EnableExperimental = true,
        };
        var tokens = new TokenRegistry();
        tokens.Add("quest", _token);
        _app = HttpHost.Build(System.Array.Empty<string>(), config, tokens);
        await _app.StartAsync();
        var address = _app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>()
            .Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>()!.Addresses.First();
        _http = new HttpClient { BaseAddress = new System.Uri(address) };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        await Rpc("initialize", new { protocolVersion = "2025-06-18", capabilities = new { }, clientInfo = new { name = "ff", version = "1" } });
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

    private Task<JObject> FaceFeature(string faceId, string? revision = null)
        => Tool("inventor_face_feature", new { document_id = _addIn.ActiveDocumentId, expected_revision = revision ?? _addIn.Revision, face_id = faceId });

    [Fact]
    public async Task ExtrusionWithASimpleValueIsEditable()
    {
        var result = await FaceFeature("ent_face_extrude");
        Assert.Equal("Estrusione1", (string?)result["feature"]!["name"]);
        Assert.Equal("extrude", (string?)result["feature"]!["type"]);
        Assert.False((bool)result["feature"]!["suppressed"]!);
        Assert.True((bool)result["feature"]!["healthy"]!);
        var parameter = result["parameters"]!.Single();
        Assert.Equal("d3", (string?)parameter["name"]);
        Assert.Equal("distance", (string?)parameter["role"]);
        Assert.Equal(20.0, (double)parameter["value"]!);
        Assert.Equal("mm", (string?)parameter["unit"]);
        Assert.Equal("20 mm", (string?)parameter["expression"]);
        Assert.True((bool)parameter["editable"]!);
        Assert.Equal("Schizzo1", (string?)result["previous_feature"]);
    }

    [Fact]
    public async Task ParameterDrivenByAnExpressionIsReadOnlyAndShowsTheExpression()
    {
        var parameter = (await FaceFeature("ent_face_expression"))["parameters"]!.Single();
        Assert.Equal("d1*2", (string?)parameter["expression"]);
        Assert.Equal(20.0, (double)parameter["value"]!);
        Assert.False((bool)parameter["editable"]!);
    }

    [Fact]
    public async Task SuppressedFeatureIsReadableButNotEditable()
    {
        var result = await FaceFeature("ent_face_suppressed");
        Assert.True((bool)result["feature"]!["suppressed"]!);
        var parameter = result["parameters"]!.Single();
        Assert.Equal("radius", (string?)parameter["role"]);
        Assert.Equal(1.0, (double)parameter["value"]!);
        Assert.False((bool)parameter["editable"]!);
    }

    [Fact]
    public async Task HoleAndPatternReportTheirRolesInMillimetresAndDegrees()
    {
        var hole = (await FaceFeature("ent_face_hole"))["parameters"]!;
        Assert.Equal(new[] { "diameter", "depth" }, hole.Select(x => (string)x["role"]!).ToArray());
        Assert.Equal(new[] { 5.0, 10.0 }, hole.Select(x => (double)x["value"]!).ToArray());
        var pattern = (await FaceFeature("ent_face_circular"))["parameters"]!;
        Assert.Equal(new[] { "count", "angle" }, pattern.Select(x => (string)x["role"]!).ToArray());
        Assert.Equal(6.0, (double)pattern[0]!["value"]!);
        Assert.Equal("", (string?)pattern[0]!["unit"]);
        Assert.Equal(180.0, (double)pattern[1]!["value"]!);
        Assert.Equal("deg", (string?)pattern[1]!["unit"]);
    }

    [Fact]
    public async Task BaseBodyFaceHasNoOwningFeature()
    {
        var result = await FaceFeature("ent_face_base");
        Assert.Equal("NO_OWNING_FEATURE", (string?)result["error"]!["code"]);
    }

    [Fact]
    public async Task UnsupportedTypeIsRefusedButNamed()
    {
        var result = await FaceFeature("ent_face_sweep");
        Assert.Equal("UNSUPPORTED_FEATURE", (string?)result["error"]!["code"]);
        Assert.Equal("Sweep1", (string?)result["error"]!["details"]!["feature"]!["name"]);
        Assert.Equal("sweep", (string?)result["error"]!["details"]!["feature"]!["type"]);
    }

    [Fact]
    public async Task OldRevisionIsRefusedAndNothingIsWritten()
    {
        string revision = _addIn.Revision;
        _addIn.RaiseDocumentChanged(geometry: true);
        var stale = await FaceFeature("ent_face_extrude", revision);
        Assert.Equal("STALE_REVISION", (string?)stale["error"]!["code"]);
        Assert.DoesNotContain("atomic_batch", _addIn.Commands);
    }

    [Fact]
    public async Task MissingFaceIdIsRejectedByTheServerBeforeTheWire()
    {
        var result = await Tool("inventor_face_feature", new { document_id = _addIn.ActiveDocumentId, expected_revision = _addIn.Revision, face_id = " " });
        Assert.Equal("INVALID_ARGUMENT", (string?)result["error"]!["code"]);
        Assert.DoesNotContain("face_feature", _addIn.Commands);
    }

    [Fact]
    public async Task AssemblyDocumentIsWrongType()
    {
        _addIn.DesignPartMode = false;
        var result = await Tool("inventor_face_feature", new { document_id = FakeAddIn.AssemblyId, expected_revision = _addIn.Revision, face_id = "ent_face_extrude" });
        Assert.Equal("WRONG_DOCUMENT_TYPE", (string?)result["error"]!["code"]);
    }
}
