using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Bimwright.Ipt.Server;
using Bimwright.Ipt.Server.Assets;
using Bimwright.Ipt.Server.Tools;
using Bimwright.Ipt.Shared.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Ipt.Tests;

/// <summary>Mesh cache budget, mesh size limits against the response cap, target listing cache, boolean config.</summary>
[Collection("ProcessEnvironment")]
public sealed class ServerHardeningTests : IAsyncLifetime
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "so-hard-" + Guid.NewGuid().ToString("N"));
    private FakeAddIn _addIn = null!;
    private InventorMcpConfig _config = null!;

    public Task InitializeAsync()
    {
        _addIn = new FakeAddIn(Path.Combine(_dir, "targets"));
        _config = new InventorMcpConfig
        {
            DescriptorDirectory = _addIn.DescriptorDirectory, AssetDirectory = Path.Combine(_dir, "assets"), TimeoutMs = 5000,
        };
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _addIn.DisposeAsync();
        try { Directory.Delete(_dir, true); } catch { }
    }

    private sealed class Caller : ICallerIdentity { public string Client => "test"; public string? SessionId => null; }

    private XrTools Tools(MeshCache? cache = null) =>
        new(new PluginClient(_config), new AssetStore(_config), _config, new Caller(), cache ?? new MeshCache());

    // ---- 1. mesh cache ----

    private static GlbBuilder.MeshSource Mesh(string name, int triangles)
    {
        var body = new MeshPayload.Body
        {
            Positions = new float[triangles * 9], Normals = new float[triangles * 9], Indices = new uint[triangles * 3],
        };
        return new GlbBuilder.MeshSource { Name = name, DocumentId = "d", Bodies = new[] { body } };
    }

    [Fact]
    public void CachedDataDropsTheBase64Bodies()
    {
        var cache = new MeshCache();
        var data = new JObject { ["revision"] = "r", ["definition_name"] = "n", ["bodies"] = new JArray(new JObject { ["positions"] = "AAAA" }) };
        cache.Put("k", data, Mesh("a", 1));
        Assert.True(cache.TryGet("k", out var cached, out _));
        Assert.Null(cached["bodies"]);
        Assert.Equal("n", (string?)cached["definition_name"]);
        Assert.NotNull(data["bodies"]);   // the caller's object is untouched
    }

    [Fact]
    public void ByteBudgetEvictsTheLeastRecentlyUsedEntry()
    {
        var one = MeshCache.EstimateBytes(Mesh("x", 100));
        var cache = new MeshCache(64, one * 2 + one / 2);   // room for two
        cache.Put("a", new JObject(), Mesh("a", 100));
        cache.Put("b", new JObject(), Mesh("b", 100));
        Assert.True(cache.TryGet("a", out _, out _));       // b is now the oldest
        cache.Put("c", new JObject(), Mesh("c", 100));

        Assert.Equal(2, cache.Count);
        Assert.True(cache.EstimatedBytes <= one * 2 + one / 2);
        Assert.True(cache.TryGet("a", out _, out _));
        Assert.False(cache.TryGet("b", out _, out _));
        Assert.True(cache.TryGet("c", out _, out _));
    }

    [Fact]
    public void AnEntryLargerThanTheBudgetIsNotCachedAndEvictsNothing()
    {
        var small = MeshCache.EstimateBytes(Mesh("s", 10));
        var cache = new MeshCache(64, small * 2);
        cache.Put("small", new JObject(), Mesh("s", 10));
        cache.Put("huge", new JObject(), Mesh("h", 100_000));
        Assert.True(cache.TryGet("small", out _, out _));
        Assert.False(cache.TryGet("huge", out _, out _));
    }

    [Fact]
    public async Task DisplayMeshIsServedFromTheCacheWithoutBodies()
    {
        var cache = new MeshCache();
        var tools = Tools(cache);
        var first = JObject.Parse(await tools.GetDisplayMesh(FakeAddIn.PlateId));
        var second = JObject.Parse(await tools.GetDisplayMesh(FakeAddIn.PlateId));
        Assert.Equal(1, _addIn.Commands.Count(c => c == "get_display_mesh"));
        Assert.Equal((string?)first["asset"]!["id"], (string?)second["asset"]!["id"]);
        Assert.Equal(12, (int)second["bodies"]![0]!["triangle_count"]!);
        Assert.Equal(1, cache.Count);
    }

    // ---- 2. mesh limits vs response cap ----

    [Fact]
    public async Task RequestedTriangleLimitIsClampedToTheResponseCap()
    {
        int? seen = null;
        _addIn.Override = env => { if (env.Command == "get_display_mesh") seen = (int?)env.Params?["max_triangles"]; return null; };
        var result = JObject.Parse(await Tools().GetDisplayMesh(FakeAddIn.PlateId, max_triangles: 5_000_000));

        var expected = XrTools.EffectiveMaxTriangles(5_000_000, _config.MaxResponseBytes);
        // indices alone (16 B/triangle) bound the 5 MB cap at ~300k: never below what could ship
        Assert.InRange(expected, 250_000, 320_000);
        Assert.Equal(200_000, XrTools.EffectiveMaxTriangles(200_000, _config.MaxResponseBytes));
        Assert.Equal(expected, seen);
        Assert.Equal(expected, (int)result["max_triangles_effective"]!);
    }

    [Fact]
    public async Task ASmallRequestIsNotChanged()
    {
        int? seen = null;
        _addIn.Override = env => { if (env.Command == "get_display_mesh") seen = (int?)env.Params?["max_triangles"]; return null; };
        var result = JObject.Parse(await Tools().GetDisplayMesh(FakeAddIn.PlateId, max_triangles: 1000));
        Assert.Equal(1000, seen);
        Assert.Equal(1000, (int)result["max_triangles_effective"]!);
    }

    [Fact]
    public async Task FlatPatternRequestIsClampedToo()
    {
        int? seen = null;
        _addIn.Override = env => { if (env.Command == "get_flat_pattern_mesh") seen = (int?)env.Params?["max_triangles"]; return null; };
        await Tools().GetFlatPatternMesh(FakeAddIn.PlateId, max_triangles: 5_000_000);
        Assert.Equal(XrTools.EffectiveMaxTriangles(5_000_000, _config.MaxResponseBytes), seen);
    }

    [Fact]
    public void TheClampScalesWithTheConfiguredCap()
    {
        Assert.True(XrTools.EffectiveMaxTriangles(5_000_000, 50_000_000) > XrTools.EffectiveMaxTriangles(5_000_000, 5_000_000));
        Assert.Equal(1, XrTools.EffectiveMaxTriangles(5_000_000, 1000));   // never zero
    }

    [Theory]
    [InlineData("inventor_get_display_mesh")]
    [InlineData("inventor_get_flat_pattern_mesh")]
    public async Task AResponseTooLargeFromAMeshCommandBecomesMeshTooLarge(string tool)
    {
        _addIn.Override = env => env.Command is "get_display_mesh" or "get_flat_pattern_mesh"
            ? InventorCommandResult.Fail(env.Id, InventorErrorCodes.RESPONSE_TOO_LARGE, "too big", new InventorResponseMeta()) : null;
        var xr = Tools();
        var json = tool == "inventor_get_display_mesh"
            ? await xr.GetDisplayMesh(FakeAddIn.PlateId) : await xr.GetFlatPatternMesh(FakeAddIn.PlateId);
        var error = JObject.Parse(json)["error"]!;
        Assert.Equal(InventorErrorCodes.MESH_TOO_LARGE, (string?)error["code"]);
        Assert.Contains("response limit", (string?)error["message"]);
        Assert.Equal(_config.MaxResponseBytes, (int)error["details"]!["max_response_bytes"]!);
    }

    [Fact]
    public async Task ARefusalAtAClampedLimitExplainsTheResponseLimit()
    {
        _addIn.Override = env => env.Command == "get_display_mesh"
            ? InventorCommandResult.Fail(env.Id, InventorErrorCodes.MESH_TOO_LARGE, "exceeds", new InventorResponseMeta()) : null;
        var clamped = JObject.Parse(await Tools().GetDisplayMesh(FakeAddIn.PlateId, max_triangles: 5_000_000))["error"]!;
        Assert.Equal(InventorErrorCodes.MESH_TOO_LARGE, (string?)clamped["code"]);
        Assert.Contains("response limit", (string?)clamped["message"]);

        var exact = JObject.Parse(await Tools().GetDisplayMesh(FakeAddIn.PlateId, max_triangles: 1000))["error"]!;
        Assert.Equal("exceeds", (string?)exact["message"]);   // the caller's own limit: untouched
    }

    // ---- 3. target listing cache ----

    private sealed class Clock { public DateTimeOffset Now = DateTimeOffset.UtcNow; }

    private PluginClient Client(Clock clock) => new(_config, _ => { }, () => clock.Now, TimeSpan.FromSeconds(1));

    private void WriteSecondTarget()
    {
        var d = new TargetDescriptor
        {
            TargetId = "inventor-2027-second", InventorYear = 2027, ProcessId = Environment.ProcessId, HostApp = "Inventor",
            Transport = "pipe", PipeName = "nope", AuthToken = "t", LastHeartbeatUtc = DateTimeOffset.UtcNow,
        };
        File.WriteAllText(Path.Combine(_addIn.DescriptorDirectory, "inventor-2027-second.json"), JsonConvert.SerializeObject(d));
    }

    [Fact]
    public void CurrentTargetReusesTheListingWithinTheTtlThenRefreshes()
    {
        var clock = new Clock();
        var client = Client(clock);
        var first = client.CurrentTarget;
        Assert.NotNull(first);

        File.Delete(Path.Combine(_addIn.DescriptorDirectory, "inventor-2027-fake.json"));
        clock.Now += TimeSpan.FromMilliseconds(500);
        Assert.Equal(first!.TargetId, client.CurrentTarget?.TargetId);   // still cached

        clock.Now += TimeSpan.FromMilliseconds(600);
        Assert.Null(client.CurrentTarget);                                 // TTL passed: the registry is read again
    }

    [Fact]
    public void ListTargetsAlwaysBypassesTheCache()
    {
        var clock = new Clock();
        var client = Client(clock);
        Assert.Single(client.ListTargets());
        WriteSecondTarget();
        Assert.Equal(2, client.ListTargets().Count);   // same instant: fresh
    }

    [Fact]
    public void SwitchTargetSeesANewDescriptorImmediately()
    {
        var clock = new Clock();
        var client = Client(clock);
        Assert.NotNull(client.CurrentTarget);
        WriteSecondTarget();
        Assert.True(client.SwitchTarget("inventor-2027-second"));
        Assert.Equal("inventor-2027-second", client.CurrentTarget?.TargetId);
    }

    [Fact]
    public void AnEmptyListingIsNeverCached()
    {
        var other = Path.Combine(_dir, "empty");
        var config = new InventorMcpConfig { DescriptorDirectory = other };
        var clock = new Clock();
        var client = new PluginClient(config, _ => { }, () => clock.Now, TimeSpan.FromSeconds(1));
        Assert.Null(client.CurrentTarget);
        File.Copy(Path.Combine(_addIn.DescriptorDirectory, "inventor-2027-fake.json"), Path.Combine(Directory.CreateDirectory(other).FullName, "t.json"));
        Assert.NotNull(client.CurrentTarget);   // same instant, found at once
    }

    // ---- 4. boolean configuration ----

    private static InventorMcpConfig LoadWithEnv(string? value, params string[] args)
    {
        const string Var = "BIMWRIGHT_INVENTOR_FULL_ACCESS";
        var prior = Environment.GetEnvironmentVariable(Var);
        try { Environment.SetEnvironmentVariable(Var, value); return InventorMcpConfig.Load(args); }
        finally { Environment.SetEnvironmentVariable(Var, prior); }
    }

    [Theory]
    [InlineData("0")]
    [InlineData("false")]
    [InlineData("No")]
    [InlineData("off")]
    public void FalseSpellingsTurnAFlagOff(string value)
        => Assert.False(LoadWithEnv(value, "--config", WriteJson("{\"fullAccess\": true}")).FullAccess);

    [Theory]
    [InlineData("enable")]
    [InlineData("y")]
    [InlineData("2")]
    public void AnUnrecognisedValueKeepsThePreviousLayer(string value)
    {
        var path = WriteJson("{\"fullAccess\": true}");
        var original = Console.Error;
        var captured = new StringWriter();
        Console.SetError(captured);
        try
        {
            Assert.True(LoadWithEnv(value, "--config", path).FullAccess);   // JSON true survives
            Assert.False(LoadWithEnv(value).FullAccess);                    // default false survives
        }
        finally { Console.SetError(original); }
        Assert.Contains("BIMWRIGHT_INVENTOR_FULL_ACCESS", captured.ToString());
        Assert.Contains("keeping the previous value", captured.ToString());
    }

    [Fact]
    public void PrecedenceIsJsonThenEnvironmentThenCli()
    {
        var json = WriteJson("{\"fullAccess\": false}");
        Assert.False(LoadWithEnv(null, "--config", json).FullAccess);                              // JSON
        Assert.True(LoadWithEnv("true", "--config", json).FullAccess);                             // env over JSON
        Assert.True(LoadWithEnv(null, "--config", json, "--full-access").FullAccess);              // CLI over JSON
        var jsonTrue = WriteJson("{\"fullAccess\": true}");
        Assert.False(LoadWithEnv("false", "--config", jsonTrue).FullAccess);                       // env over JSON
        Assert.False(LoadWithEnv("true", "--config", jsonTrue, "--no-full-access").FullAccess);    // CLI false over env and JSON
    }

    [Fact]
    public void EveryBooleanFlagHasANegativeForm()
    {
        var on = new[]
        {
            "--read-only", "--enable-send-code", "--enable-adaptive-bake", "--full-access", "--enable-experimental", "--http-self-signed",
            "--pair-control", "--http-allow-insecure-lan", "--http-allow-dangerous", "--voice-diagnostics", "--disable-toolbaker", "--no-audit",
        };
        var set = InventorMcpConfig.Load(on);
        Assert.True(set.ReadOnly && set.EnableSendCode && set.EnableAdaptiveBake && set.FullAccess && set.EnableExperimental && set.HttpSelfSignedCertificate
            && set.PairControl && set.HttpAllowInsecureLan && set.HttpAllowDangerous && set.VoiceDiagnostics && !set.EnableToolBaker && !set.AuditEnabled);

        var negatives = new[]
        {
            "--no-read-only", "--no-enable-send-code", "--no-enable-adaptive-bake", "--no-full-access", "--no-enable-experimental", "--no-http-self-signed",
            "--no-pair-control", "--no-http-allow-insecure-lan", "--no-http-allow-dangerous", "--no-voice-diagnostics", "--enable-toolbaker", "--audit",
        };
        var cleared = InventorMcpConfig.Load(on.Concat(negatives).ToArray());
        Assert.True(!cleared.ReadOnly && !cleared.EnableSendCode && !cleared.EnableAdaptiveBake && !cleared.FullAccess && !cleared.EnableExperimental
            && !cleared.HttpSelfSignedCertificate && !cleared.PairControl && !cleared.HttpAllowInsecureLan && !cleared.HttpAllowDangerous
            && !cleared.VoiceDiagnostics && cleared.EnableToolBaker && cleared.AuditEnabled);
    }

    private string WriteJson(string content)
    {
        var path = Path.Combine(_dir, "cfg-" + Guid.NewGuid().ToString("N") + ".json");
        Directory.CreateDirectory(_dir);
        File.WriteAllText(path, content);
        return path;
    }
}
