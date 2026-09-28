using Bimwright.Ipt.Server;
using Bimwright.Ipt.Tests;
using Inventor.So.Mcp.Http;
using Inventor.So.Mcp.Http.Pairing;
using InventorXrSo.Core.Net;
using InventorXrSo.Core.Pairing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace InventorXrSo.Core.Tests.Support;

/// <summary>The real HTTPS host (self-signed, pairing enabled) in front of FakeAddIn, per test class.</summary>
public sealed class BackendFixture : IAsyncLifetime
{
    private WebApplication _app;

    public string Root { get; } = Path.Combine(Path.GetTempPath(), "xrso-" + Guid.NewGuid().ToString("N"));
    public FakeAddIn AddIn { get; private set; }
    public TokenRegistry Tokens { get; } = new TokenRegistry();
    public PairingStore Pairing { get; private set; }
    public string BaseUrl { get; private set; }
    public Uri Base => new Uri(BaseUrl);
    public string CertSha256 { get; private set; }
    public string EditorToken { get; } = TokenRegistry.Generate();
    public PairedServer EditorServer => new PairedServer(Base.Host, Base.Port, CertSha256, "editor", EditorToken);

    public SystemHttpTransport Transport() => new SystemHttpTransport(ServerTrust.Pinned(CertSha256));

    public async Task InitializeAsync()
    {
        AddIn = new FakeAddIn(Path.Combine(Root, "targets"));
        Tokens.Add("editor", EditorToken);
        var config = new InventorMcpConfig
        {
            HttpSelfSignedCertificate = true,
            HttpSelfSignedPath = Path.Combine(Root, "server.pfx"),
            HttpTokenFile = Path.Combine(Root, "tokens.txt"),
            DescriptorDirectory = AddIn.DescriptorDirectory,
            AssetDirectory = Path.Combine(Root, "assets"),
            AuditDirectory = Path.Combine(Root, "audit"),
            EnableExperimental = true,
        };
        // RULING: assign, don't use the collection-initializer form ("HttpUrls = { ... }"), which
        // would append to the default ["http://127.0.0.1:8787"] and bind a fixed port too.
        config.HttpUrls = new List<string> { "https://127.0.0.1:0" };
        var certificate = PairingSetup.ResolveCertificate(config);
        CertSha256 = SelfSignedCertificate.Sha256Hex(certificate);
        Pairing = new PairingStore(() => DateTimeOffset.UtcNow);
        _app = HttpHost.Build(Array.Empty<string>(), config, Tokens,
            new HostOptions { Certificate = certificate, Pairing = new PairingEndpoint(Pairing, Tokens, config.HttpTokenFile) });
        await _app.StartAsync();
        BaseUrl = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>().Addresses.First().TrimEnd('/');
    }

    public async Task DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
        await AddIn.DisposeAsync();
        try { Directory.Delete(Root, true); } catch { }
    }
}
