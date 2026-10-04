using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Bimwright.Ipt.Server;
using Inventor.So.Mcp.Http;
using Inventor.So.Mcp.Http.Pairing;
using Xunit;
using System.Security.Cryptography;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Tests;

public sealed class PairingStoreTests
{
    private DateTimeOffset _now = new(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);
    private PairingStore Store() => new(() => _now);

    [Fact]
    public void OneTimeTokenPairsExactlyOnce()
    {
        var store = Store();
        var window = store.Open("quest3", PairingStore.DefaultTtl);
        var first = store.Redeem(window.OneTimeToken);
        Assert.True(first.Ok);
        Assert.Equal("quest3", first.ClientName);
        Assert.Equal(PairingStore.Used, store.Redeem(window.OneTimeToken).ErrorCode);
    }

    [Fact]
    public void SixDigitCodePairsToo()
    {
        var store = Store();
        var window = store.Open("quest3", PairingStore.DefaultTtl);
        Assert.Matches("^[0-9]{6}$", window.Code);
        Assert.True(store.Redeem(window.Code).Ok);
    }

    [Fact]
    public void ExpiredWindowIsRefused()
    {
        var store = Store();
        var window = store.Open("quest3", TimeSpan.FromMinutes(2));
        _now += TimeSpan.FromMinutes(2);
        Assert.Equal(PairingStore.Expired, store.Redeem(window.OneTimeToken).ErrorCode);
    }

    [Fact]
    public void FiveWrongSecretsCloseTheWindow()
    {
        var store = Store();
        var window = store.Open("quest3", PairingStore.DefaultTtl);
        for (int i = 0; i < PairingStore.MaxFailures - 1; i++)
            Assert.Equal(PairingStore.Invalid, store.Redeem("not-a-code").ErrorCode);
        Assert.Equal(PairingStore.Expired, store.Redeem("not-a-code").ErrorCode);
        Assert.Equal(PairingStore.Expired, store.Redeem(window.OneTimeToken).ErrorCode);
    }

    [Fact]
    public void NoWindowMeansExpired() => Assert.Equal(PairingStore.Expired, Store().Redeem("anything").ErrorCode);

    [Fact]
    public void BadClientNameIsRefused() =>
        Assert.Throws<InvalidOperationException>(() => Store().Open("bad name", PairingStore.DefaultTtl));

    [Fact]
    public void ConcurrentRedeemsPairExactlyOnce()
    {
        var store = Store();
        var window = store.Open("quest3", PairingStore.DefaultTtl);
        var outcomes = Enumerable.Range(0, 32).AsParallel().Select(_ => store.Redeem(window.OneTimeToken)).ToList();
        Assert.Single(outcomes, o => o.Ok);
    }

    [Fact]
    public void ReopeningInvalidatesThePreviousSecrets()
    {
        var store = Store();
        var old = store.Open("quest3", PairingStore.DefaultTtl);
        store.Open("quest3", PairingStore.DefaultTtl);
        Assert.Equal(PairingStore.Invalid, store.Redeem(old.OneTimeToken).ErrorCode);
    }
}

public sealed class SelfSignedCertificateTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "so-cert-" + Guid.NewGuid().ToString("N"));
    private string Pfx => Path.Combine(_dir, "server.pfx");

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void CreatesAServerCertificateWithKeyAndSubjectAltNames()
    {
        using var cert = SelfSignedCertificate.LoadOrCreate(Pfx, new[] { "localhost", "127.0.0.1", "192.168.1.20" });
        Assert.True(cert.HasPrivateKey);
        Assert.True(File.Exists(Pfx));
        var san = cert.Extensions.OfType<X509SubjectAlternativeNameExtension>().Single();
        Assert.Contains(IPAddress.Parse("192.168.1.20"), san.EnumerateIPAddresses());
        Assert.Contains("localhost", san.EnumerateDnsNames());
        Assert.Matches("^[0-9a-f]{64}$", SelfSignedCertificate.Sha256Hex(cert));
    }

    [Fact]
    public void ReloadKeepsTheFingerprintEvenIfTheHostsChange()
    {
        string first, second;
        using (var a = SelfSignedCertificate.LoadOrCreate(Pfx, new[] { "localhost" })) first = SelfSignedCertificate.Sha256Hex(a);
        using (var b = SelfSignedCertificate.LoadOrCreate(Pfx, new[] { "localhost", "10.0.0.9" })) second = SelfSignedCertificate.Sha256Hex(b);
        Assert.Equal(first, second);
    }

    [Fact]
    public void AnExpiringCertificateIsReplaced()
    {
        string first;
        var longAgo = DateTimeOffset.UtcNow - SelfSignedCertificate.Validity + TimeSpan.FromDays(10);
        using (var old = SelfSignedCertificate.LoadOrCreate(Pfx, new[] { "localhost" }, longAgo)) first = SelfSignedCertificate.Sha256Hex(old);
        using var renewed = SelfSignedCertificate.LoadOrCreate(Pfx, new[] { "localhost" });
        Assert.NotEqual(first, SelfSignedCertificate.Sha256Hex(renewed));
    }

    [Fact]
    public void DisplayGroupsByFourUppercase() => Assert.Equal("ABCD 0123", SelfSignedCertificate.Display("abcd0123"));
}

public sealed class TokenFileTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "so-tokens-" + Guid.NewGuid().ToString("N"));
    private string File1 => Path.Combine(_dir, "sub", "tokens.txt");

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void AppendCreatesTheFileAndLoadReadsIt()
    {
        var token = TokenRegistry.Generate();
        TokenFile.Append(File1, "quest3", token);
        var registry = TokenRegistry.Load(new InventorMcpConfig { HttpTokenFile = File1 });
        Assert.Equal("quest3", registry.Authenticate(token));
    }

    /// <summary>First-time pairing (spec §3.1): --pair needs a token file that does not exist yet.</summary>
    [Fact]
    public void LoadOnAMissingTokenFileYieldsAnEmptyRegistry()
    {
        Assert.False(File.Exists(File1));
        var registry = TokenRegistry.Load(new InventorMcpConfig { HttpTokenFile = File1 });
        Assert.Equal(0, registry.Count);
        Assert.Empty(registry.Names);
    }

    [Fact]
    public void AppendAfterALineWithoutNewlineStartsANewLine()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(File1)!);
        var first = TokenRegistry.Generate();
        System.IO.File.WriteAllText(File1, "laptop:" + first);
        var second = TokenRegistry.Generate();
        TokenFile.Append(File1, "quest3", second);
        var registry = TokenRegistry.Load(new InventorMcpConfig { HttpTokenFile = File1 });
        Assert.Equal(new[] { "laptop", "quest3" }, registry.Names);
    }

    [Fact]
    public void AddWhileAuthenticatingIsSafe()
    {
        var registry = new TokenRegistry();
        var known = TokenRegistry.Generate();
        registry.Add("known", known);
        Parallel.For(0, 200, i =>
        {
            if (i % 2 == 0) registry.Add("c" + i, TokenRegistry.Generate());
            else Assert.Equal("known", registry.Authenticate(known));
        });
        Assert.Equal(101, registry.Count);
    }
}

/// <summary>L2: the real host over HTTPS with its self-signed certificate and an open pairing window.</summary>
public sealed class PairingEndpointTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "so-pair-" + Guid.NewGuid().ToString("N"));
    private readonly TokenRegistry _tokens = new();
    private PairingStore _store = null!;
    private WebApplication _app = null!;
    private string _base = "";
    private string _sha = "";
    private string TokenFilePath => Path.Combine(_root, "tokens.txt");

    public async Task InitializeAsync()
    {
        var config = new InventorMcpConfig
        {
            // Assignment, not collection-add: InventorMcpConfig.HttpUrls already defaults to
            // ["http://127.0.0.1:8787"], and `{ "https://..." }` here would append to that
            // default rather than replace it, leaving the test host listening on both.
            HttpUrls = new List<string> { "https://127.0.0.1:0" },
            HttpSelfSignedCertificate = true,
            HttpSelfSignedPath = Path.Combine(_root, "server.pfx"),
            HttpTokenFile = TokenFilePath,
            DescriptorDirectory = Path.Combine(_root, "targets"),
            AssetDirectory = Path.Combine(_root, "assets"),
            AuditDirectory = Path.Combine(_root, "audit"),
        };
        var certificate = PairingSetup.ResolveCertificate(config)!;
        _sha = SelfSignedCertificate.Sha256Hex(certificate);
        _store = new PairingStore(() => DateTimeOffset.UtcNow);
        _app = HttpHost.Build(Array.Empty<string>(), config, _tokens,
            new HostOptions { Certificate = certificate, Pairing = new PairingEndpoint(_store, _tokens, TokenFilePath) });
        await _app.StartAsync();
        _base = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
    }

    public async Task DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
        try { Directory.Delete(_root, true); } catch { }
    }

    private HttpClient Client(string? pin = null) => new(new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = (_, cert, _, _) => cert != null && SelfSignedCertificate.Sha256Hex(cert) == (pin ?? _sha),
    }) { BaseAddress = new Uri(_base) };

    private static StringContent Json(object body) => new(JObject.FromObject(body).ToString(), Encoding.UTF8, "application/json");

    [Fact]
    public async Task QrTokenPairsOnceAndTheNewTokenOpensMcp()
    {
        var window = _store.Open("quest3", PairingStore.DefaultTtl);
        using var http = Client();
        var response = await http.PostAsync("/pair", Json(new { secret = window.OneTimeToken, device_name = "Quest 3" }));
        var body = JObject.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("quest3", (string?)body["client_name"]);
        var token = (string)body["token"]!;
        Assert.Equal("quest3", _tokens.Authenticate(token));
        Assert.Contains("quest3:" + token, File.ReadAllText(TokenFilePath));

        using var init = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-06-18\",\"capabilities\":{},\"clientInfo\":{\"name\":\"t\",\"version\":\"1\"}}}", Encoding.UTF8, "application/json"),
        };
        init.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        init.Headers.Accept.ParseAdd("application/json");
        init.Headers.Accept.ParseAdd("text/event-stream");
        Assert.Equal(HttpStatusCode.OK, (await http.SendAsync(init)).StatusCode);

        var again = await http.PostAsync("/pair", Json(new { secret = window.OneTimeToken }));
        Assert.Equal(HttpStatusCode.Forbidden, again.StatusCode);
        Assert.Equal(PairingStore.Used, (string?)JObject.Parse(await again.Content.ReadAsStringAsync())["error"]!["code"]);
    }

    [Fact]
    public async Task SixDigitCodePairs()
    {
        var window = _store.Open("quest-code", PairingStore.DefaultTtl);
        using var http = Client();
        var response = await http.PostAsync("/pair", Json(new { secret = window.Code }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PairingCanOpenAgainWithoutRestartAndOldCredentialRemainsValid()
    {
        using var http = Client();
        var first = _store.Open("quest-first", PairingStore.DefaultTtl);
        var response = await http.PostAsync("/pair", Json(new { secret = first.Code }));
        var token = (string)JObject.Parse(await response.Content.ReadAsStringAsync())["token"]!;
        var second = _store.Open("quest-second", PairingStore.DefaultTtl);
        Assert.Equal(HttpStatusCode.OK, (await http.PostAsync("/pair", Json(new { secret = second.OneTimeToken }))).StatusCode);
        Assert.Equal("quest-first", _tokens.Authenticate(token));
        Assert.Equal(2, TokenRegistry.Load(new InventorMcpConfig { HttpTokenFile = TokenFilePath }).Count);
    }

    [Fact]
    public async Task UnwritableTokenFileReturns503WithoutActivatingCredential()
    {
        Directory.CreateDirectory(TokenFilePath); // A directory cannot be replaced by the token file.
        var window = _store.Open("quest-failure", PairingStore.DefaultTtl);
        using var http = Client();
        var response = await http.PostAsync("/pair", Json(new { secret = window.Code }));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("PAIRING_PERSIST_FAILED", (string?)JObject.Parse(await response.Content.ReadAsStringAsync())["error"]!["code"]);
        Assert.Equal(0, _tokens.Count);
        Assert.Equal("failed", _store.Status().State);
    }

    [Fact]
    public async Task MalformedBodyIsRefused()
    {
        _store.Open("quest-bad", PairingStore.DefaultTtl);
        using var http = Client();
        var response = await http.PostAsync("/pair", new StringContent("not json", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(PairingStore.Invalid, (string?)JObject.Parse(await response.Content.ReadAsStringAsync())["error"]!["code"]);
    }

    [Fact]
    public async Task AClientPinningAnotherCertificateCannotConnect()
    {
        using var http = Client(pin: new string('0', 64));
        await Assert.ThrowsAsync<HttpRequestException>(() => http.GetAsync("/healthz"));
    }
}

public sealed class PairingSetupTests
{
    [Fact]
    public void QrPayloadCarriesEverythingTheHeadsetNeeds()
    {
        var json = JObject.Parse(PairingSetup.QrPayload("192.168.1.20", 8443, "ott-value", new string('a', 64)));
        Assert.Equal(1, (int)json["v"]!);
        Assert.Equal("192.168.1.20", (string?)json["host"]);
        Assert.Equal(8443, (int)json["port"]!);
        Assert.Equal("ott-value", (string?)json["ott"]);
        Assert.Equal(new string('a', 64), (string?)json["cert_sha256"]);
    }

    [Theory]
    [InlineData("https://0.0.0.0:8443", 8443)]
    [InlineData("https://*:9443", 9443)]
    [InlineData("https://[::]:7443/", 7443)]
    public void HttpsPortComesFromTheFirstHttpsUrl(string url, int port) =>
        Assert.Equal(port, PairingSetup.HttpsPort(new InventorMcpConfig { HttpUrls = { "http://127.0.0.1:8787", url } }));

    [Fact]
    public void NoHttpsUrlMeansNoPort() => Assert.Null(PairingSetup.HttpsPort(new InventorMcpConfig()));
}
