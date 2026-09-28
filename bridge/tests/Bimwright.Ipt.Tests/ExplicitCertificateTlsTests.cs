using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Bimwright.Ipt.Server;
using Inventor.So.Mcp.Http;
using Inventor.So.Mcp.Http.Pairing;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bimwright.Ipt.Tests;

public sealed class ExplicitCertificateTlsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConfiguredPfxServesRealPinnedTls(bool resolveBeforeHost)
    {
        string root = Path.Combine(Path.GetTempPath(), "xr-explicit-pfx-" + Guid.NewGuid().ToString("N"));
        try
        {
            string path = Path.Combine(root, "server.pfx");
            using var created = SelfSignedCertificate.LoadOrCreate(path, new[] { "127.0.0.1" });
            string pin = SelfSignedCertificate.Sha256Hex(created);
            var config = new InventorMcpConfig
            {
                HttpUrls = new List<string> { "https://127.0.0.1:0" },
                HttpCertificatePath = path,
                DescriptorDirectory = Path.Combine(root, "targets"),
                AssetDirectory = Path.Combine(root, "assets"),
                AuditDirectory = Path.Combine(root, "audit"),
            };
            var tokens = new TokenRegistry();
            tokens.Add("test", TokenRegistry.Generate());
            using var resolved = resolveBeforeHost ? PairingSetup.ResolveCertificate(config) : null;
            await using var app = HttpHost.Build(Array.Empty<string>(), config, tokens,
                new HostOptions { Certificate = resolved });
            await app.StartAsync();
            try
            {
                string address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
                using var client = new HttpClient(new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback = (_, cert, _, _) =>
                        cert != null && SelfSignedCertificate.Sha256Hex(cert) == pin,
                });
                using var response = await client.GetAsync(address + "/healthz");
                response.EnsureSuccessStatusCode();
            }
            finally { await app.StopAsync(); }
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
