using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Bimwright.Ipt.Server;
using Bimwright.Ipt.Server.Assets;
using Inventor.So.Mcp.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Tests;

public sealed class BindingPolicyTests
{
    private static InventorMcpConfig Config(params string[] urls) => new() { HttpUrls = urls.ToList() };

    [Theory]
    [InlineData("http://127.0.0.1:8787")]
    [InlineData("http://localhost:8787")]
    [InlineData("http://[::1]:8787")]
    public void LoopbackPlainHttpIsAllowed(string url) => Assert.Empty(BindingPolicy.Check(Config(url)));

    [Theory]
    [InlineData("http://0.0.0.0:8787")]
    [InlineData("http://*:8787")]
    [InlineData("http://+:8787")]
    [InlineData("http://192.168.1.20:8787")]
    [InlineData("http://[::]:8787")]
    public void LanPlainHttpNeedsTheExplicitOptIn(string url)
    {
        Assert.NotEmpty(BindingPolicy.Check(Config(url)));
        var optIn = Config(url);
        optIn.HttpAllowInsecureLan = true;
        Assert.Empty(BindingPolicy.Check(optIn));
    }

    [Fact]
    public void HttpsNeedsACertificate()
    {
        Assert.NotEmpty(BindingPolicy.Check(Config("https://0.0.0.0:8443")));
        var withCert = Config("https://0.0.0.0:8443");
        withCert.HttpCertificatePath = "/certs/pc.pfx";
        Assert.Empty(BindingPolicy.Check(withCert));
    }

    [Theory]
    [InlineData("ftp://127.0.0.1:21")]
    [InlineData("127.0.0.1:8787")]
    public void MalformedUrlsAreRefused(string url) => Assert.NotEmpty(BindingPolicy.Check(Config(url)));
}

public sealed class TokenRegistryTests
{
    [Fact]
    public void AuthenticatesByNameAndRejectsEverythingElse()
    {
        var registry = new TokenRegistry();
        var token = TokenRegistry.Generate();
        registry.Add("quest", token);
        Assert.Equal("quest", registry.Authenticate(token));
        Assert.Null(registry.Authenticate(token + "x"));
        Assert.Null(registry.Authenticate(""));
        Assert.Null(registry.Authenticate(null));
    }

    [Fact]
    public void WeakDuplicateOrBadlyNamedTokensAreRefused()
    {
        var registry = new TokenRegistry();
        var token = TokenRegistry.Generate();
        registry.Add("a", token);
        Assert.Throws<System.InvalidOperationException>(() => registry.Add("b", "short"));
        Assert.Throws<System.InvalidOperationException>(() => registry.Add("b", token));
        Assert.Throws<System.InvalidOperationException>(() => registry.Add("a", TokenRegistry.Generate()));
        Assert.Throws<System.InvalidOperationException>(() => registry.Add("bad name", TokenRegistry.Generate()));
        Assert.True(TokenRegistry.Generate().Length >= TokenRegistry.MinimumTokenLength);
    }

    [Fact]
    public void LoadsTheTokenFileAndTheEnvironmentToken()
    {
        var file = Path.GetTempFileName();
        try
        {
            File.WriteAllText(file, "# clients\nquest:" + new string('q', 40) + "\n\nlaptop:" + new string('l', 40) + "\n");
            var registry = TokenRegistry.Load(new InventorMcpConfig { HttpTokenFile = file, HttpToken = new string('d', 40) });
            Assert.Equal(new[] { "quest", "laptop", "default" }, registry.Names);
        }
        finally { File.Delete(file); }
    }
}

/// <summary>L2: the real remote host on a loopback port, no Inventor behind it.</summary>
public sealed class HttpHostTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "so-http-" + System.Guid.NewGuid().ToString("N"));
    private readonly string _questToken = TokenRegistry.Generate();
    private readonly string _laptopToken = TokenRegistry.Generate();
    private WebApplication _app = null!;
    private string _base = "";

    private InventorMcpConfig Config(int rate = 1000) => new()
    {
        HttpUrls = { "http://127.0.0.1:0" },
        DescriptorDirectory = Path.Combine(_root, "targets"),
        AssetDirectory = Path.Combine(_root, "assets"),
        AuditDirectory = Path.Combine(_root, "audit"),
        HttpRateLimitPerMinute = rate,
        HttpAllowedOrigins = { "https://allowed.example" },
    };

    private async Task Start(InventorMcpConfig config)
    {
        var tokens = new TokenRegistry();
        tokens.Add("quest", _questToken);
        tokens.Add("laptop", _laptopToken);
        config.HttpUrls.Clear();
        config.HttpUrls.Add("http://127.0.0.1:0");
        _app = HttpHost.Build(System.Array.Empty<string>(), config, tokens);
        await _app.StartAsync();
        _base = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
    }

    public Task InitializeAsync() => Start(Config());

    public async Task DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
        try { Directory.Delete(_root, true); } catch { }
    }

    private HttpClient Client(string? token)
    {
        var client = new HttpClient { BaseAddress = new System.Uri(_base) };
        if (token != null) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static StringContent Json(object value) => new(Newtonsoft.Json.JsonConvert.SerializeObject(value), Encoding.UTF8, "application/json");

    private static async Task<JObject> Rpc(HttpClient client, object message, string? session = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp") { Content = Json(message) };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        if (session != null) request.Headers.Add("Mcp-Session-Id", session);
        using var response = await client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, response.StatusCode + ": " + text);
        var session2 = response.Headers.TryGetValues("Mcp-Session-Id", out var values) ? values.First() : session;
        var payload = response.Content.Headers.ContentType?.MediaType == "text/event-stream"
            ? text.Split('\n').Where(l => l.StartsWith("data:")).Select(l => JObject.Parse(l[5..])).Last()
            : JObject.Parse(text);
        payload["_session"] = session2;
        return payload;
    }

    private static async Task<string> Initialize(HttpClient client)
    {
        var init = await Rpc(client, new { jsonrpc = "2.0", id = 1, method = "initialize", @params = new
        {
            protocolVersion = "2025-06-18", capabilities = new { }, clientInfo = new { name = "test-client", version = "1" },
        } });
        var session = (string)init["_session"]!;
        using var note = new HttpRequestMessage(HttpMethod.Post, "/mcp") { Content = Json(new { jsonrpc = "2.0", method = "notifications/initialized" }) };
        note.Headers.Accept.ParseAdd("application/json");
        note.Headers.Accept.ParseAdd("text/event-stream");
        note.Headers.Add("Mcp-Session-Id", session);
        (await client.SendAsync(note)).EnsureSuccessStatusCode();
        return session;
    }

    [Fact]
    public async Task HealthIsAnonymousAndSaysNothingElse()
    {
        using var client = Client(null);
        var response = await client.GetAsync("/healthz");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("{\"status\":\"ok\"}", await response.Content.ReadAsStringAsync());
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-valid-token-but-long-enough-to-try-000")]
    public async Task McpRejectsMissingOrWrongTokens(string? token)
    {
        using var client = Client(token);
        var response = await client.PostAsync("/mcp", Json(new { jsonrpc = "2.0", id = 1, method = "initialize" }));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("Bearer", response.Headers.WwwAuthenticate.ToString());
        Assert.Contains("UNAUTHORIZED", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AuthenticatedClientSeesThePolicyFilteredSurfaceWithoutSwitchTarget()
    {
        using var client = Client(_questToken);
        var session = await Initialize(client);
        var list = await Rpc(client, new { jsonrpc = "2.0", id = 2, method = "tools/list" }, session);
        var names = list["result"]!["tools"]!.Select(t => (string)t["name"]!).ToArray();
        Assert.Contains("inventor_get_capabilities", names);
        Assert.Contains("inventor_atomic_batch", names);
        Assert.DoesNotContain("inventor_switch_target", names);       // F22: process-wide state
        Assert.DoesNotContain("inventor_get_display_mesh", names);    // experimental not enabled
        Assert.DoesNotContain("inventor_set_parameter", names);       // unreviewed legacy write

        var call = await Rpc(client, new { jsonrpc = "2.0", id = 3, method = "tools/call", @params = new { name = "inventor_get_capabilities", arguments = new { } } }, session);
        var caps = JObject.Parse((string)call["result"]!["content"]![0]!["text"]!);
        Assert.Equal("http", (string?)caps["server"]!["transport"]);
        Assert.False((bool)caps["target"]!["reachable"]!);
    }

    [Fact]
    public async Task ASessionCannotBeUsedWithAnotherClientsToken()
    {
        using var quest = Client(_questToken);
        var session = await Initialize(quest);
        using var laptop = Client(_laptopToken);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp") { Content = Json(new { jsonrpc = "2.0", id = 9, method = "tools/list" }) };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        request.Headers.Add("Mcp-Session-Id", session);
        var response = await laptop.SendAsync(request);
        Assert.False(response.IsSuccessStatusCode);
    }

    [Fact]
    public async Task AssetsAreServedOnlyToTheirOwnerWithIntegrityHeaders()
    {
        var store = _app.Services.GetRequiredService<AssetStore>();
        var record = store.Put(new byte[] { 1, 2, 3, 4 }, "model/gltf-binary", "quest");
        using var quest = Client(_questToken);
        var response = await quest.GetAsync("/assets/" + record.Id);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("model/gltf-binary", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, await response.Content.ReadAsByteArrayAsync());
        Assert.Equal("\"" + record.Sha256 + "\"", response.Headers.ETag!.Tag);

        using var again = new HttpRequestMessage(HttpMethod.Get, "/assets/" + record.Id);
        again.Headers.IfNoneMatch.Add(new EntityTagHeaderValue("\"" + record.Sha256 + "\""));
        Assert.Equal(HttpStatusCode.NotModified, (await quest.SendAsync(again)).StatusCode);

        using var laptop = Client(_laptopToken);
        Assert.Equal(HttpStatusCode.NotFound, (await laptop.GetAsync("/assets/" + record.Id)).StatusCode);
        using var anonymous = Client(null);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/assets/" + record.Id)).StatusCode);
    }

    [Theory]
    [InlineData("/assets/..%2F..%2Fappsettings.json")]
    [InlineData("/assets/a_0000")]
    [InlineData("/viewer/..%2F..%2FInventor.So.Mcp.Http.dll")]
    [InlineData("/viewer/vendor/../../appsettings.json")]
    [InlineData("/viewer/secrets.txt")]
    public async Task NoPathEverReachesTheFilesystem(string path)
    {
        using var client = Client(_questToken);
        var response = await client.GetAsync(path);
        Assert.True(response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest, path + " -> " + response.StatusCode);
    }

    [Fact]
    public async Task ViewerIsServedWithAStrictCspMatchingItsImportMap()
    {
        using var client = Client(null);
        var response = await client.GetAsync("/viewer/");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        var csp = response.Headers.GetValues("Content-Security-Policy").Single();
        Assert.Contains("connect-src 'self'", csp);
        var importMap = Regex.Match(html, "<script type=\"importmap\">(.*?)</script>", RegexOptions.Singleline).Groups[1].Value;
        var hash = System.Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(importMap)));
        Assert.Contains("'sha256-" + hash + "'", csp);
        foreach (var file in HttpHost.ViewerFiles.Keys)
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/viewer/" + file)).StatusCode);
    }

    [Fact]
    public async Task CorsAllowsOnlyListedOrigins()
    {
        using var client = Client(null);
        async Task<HttpResponseMessage> Preflight(string origin)
        {
            using var request = new HttpRequestMessage(HttpMethod.Options, "/mcp");
            request.Headers.Add("Origin", origin);
            request.Headers.Add("Access-Control-Request-Method", "POST");
            request.Headers.Add("Access-Control-Request-Headers", "authorization,content-type");
            return await client.SendAsync(request);
        }
        var allowed = await Preflight("https://allowed.example");
        Assert.Equal("https://allowed.example", allowed.Headers.GetValues("Access-Control-Allow-Origin").Single());
        var denied = await Preflight("https://evil.example");
        Assert.False(denied.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task WritesAreAuditedUnderTheTokenName()
    {
        using var client = Client(_questToken);
        var session = await Initialize(client);
        await Rpc(client, new { jsonrpc = "2.0", id = 5, method = "tools/call", @params = new
        {
            name = "inventor_commit_plan", arguments = new { plan_id = "plan_unknown", document_id = "doc_x", expected_revision = "e:1" },
        } }, session);
        var audit = _app.Services.GetRequiredService<Bimwright.Ipt.Server.Audit.AuditLog>();
        var line = File.ReadAllLines(audit.CurrentFile!).Select(JObject.Parse).Last();
        Assert.Equal("quest", (string?)line["client"]);
        Assert.Equal("test-client", (string?)line["declared_client"]);
        Assert.Equal("inventor_commit_plan", (string?)line["tool"]);
        Assert.Equal("PLAN_NOT_FOUND", (string?)line["error_code"]);
        Assert.DoesNotContain(_questToken, File.ReadAllText(audit.CurrentFile!));
    }

    [Fact]
    public async Task RateLimitReturns429WithACode()
    {
        await DisposeAsync();
        await Start(Config(rate: 3));
        using var client = Client(_questToken);
        HttpResponseMessage? last = null;
        for (int i = 0; i < 5; i++) last = await client.GetAsync("/assets/a_" + new string('0', 64));
        Assert.Equal((HttpStatusCode)429, last!.StatusCode);
        Assert.Contains("RATE_LIMITED", await last.Content.ReadAsStringAsync());
    }

    [Fact]
    public void RefusesToStartWithoutTokensOrWithAnInsecureLanBinding()
    {
        Assert.Throws<System.InvalidOperationException>(() => HttpHost.Build(System.Array.Empty<string>(), Config(), new TokenRegistry()));
        var lan = Config();
        lan.HttpUrls.Clear();
        lan.HttpUrls.Add("http://0.0.0.0:8787");
        var tokens = new TokenRegistry();
        tokens.Add("quest", TokenRegistry.Generate());
        Assert.Throws<System.InvalidOperationException>(() => HttpHost.Build(System.Array.Empty<string>(), lan, tokens));
    }
}
