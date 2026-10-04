using Bimwright.Ipt.Server;
using Inventor.So.Mcp.Http.Pairing;
using Inventor.So.Pairing.Control;
using Microsoft.Extensions.DependencyInjection;
using HostOptions = Inventor.So.Mcp.Http.Pairing.HostOptions;

namespace Inventor.So.Mcp.Http;

public static class HttpProgram
{
    public static async Task<int> Main(string[] args)
    {
        var generate = Array.IndexOf(args, "--generate-token");
        if (generate >= 0)
        {
            var name = generate + 1 < args.Length ? args[generate + 1] : "client";
            var registry = new TokenRegistry();
            var token = TokenRegistry.Generate();
            registry.Add(name, token);
            Console.WriteLine(name + ":" + token);
            return 0;
        }
        try
        {
            var config = InventorMcpConfig.Load(args);
            if (config.PairControl)
            {
                if (!OperatingSystem.IsWindows()) throw new InvalidOperationException("--pair-control requires Windows.");
                if (string.IsNullOrWhiteSpace(config.HttpTokenFile)) throw new InvalidOperationException("--pair-control requires --http-token-file.");
                if (config.HttpUrls.Any(url => BindingPolicy.TryParse(url, out var scheme, out var host) && scheme == "http" && !BindingPolicy.IsLoopback(host)))
                    throw new InvalidOperationException("Managed LAN pairing requires HTTPS on every non-loopback listener.");
                OwnerStorage.ProtectDirectory(Path.GetDirectoryName(Path.GetFullPath(config.HttpTokenFile))!);
                if (string.IsNullOrWhiteSpace(config.HttpCertificatePath))
                    OwnerStorage.ProtectDirectory(Path.GetDirectoryName(Path.GetFullPath(config.HttpSelfSignedPath))!);
            }
            var tokens = TokenRegistry.Load(config);
            using var certificate = PairingSetup.ResolveCertificate(config);
            var store = new PairingStore(() => DateTimeOffset.UtcNow);
            PairingWindow? window = null;
            PairingEndpoint? pairing = null;
            if (config.PairClientName != null || config.PairControl)
            {
                if (string.IsNullOrWhiteSpace(config.HttpTokenFile)) throw new InvalidOperationException("Pairing needs --http-token-file.");
                if (certificate == null || PairingSetup.HttpsPort(config) == null)
                    throw new InvalidOperationException("Pairing needs HTTPS: use --http-self-signed or --http-cert with an https:// URL.");
                if (config.PairClientName != null)
                {
                    if (tokens.Names.Contains(config.PairClientName)) throw new InvalidOperationException("This client already has a token; choose another name.");
                    window = store.Open(config.PairClientName, PairingStore.DefaultTtl);
                }
                pairing = new PairingEndpoint(store, tokens, config.HttpTokenFile!);
            }
            await using var app = HttpHost.Build(args, config, tokens, new HostOptions { Certificate = certificate, Pairing = pairing });
            await app.StartAsync(); // No QR or local readiness before Kestrel has bound successfully.
            await using var control = config.PairControl ? new ControlPipeServer(ControlNames.HostPipe,
                new LocalPairingController(store, config, app.Services.GetRequiredService<PluginClient>(),
                    SelfSignedCertificate.Sha256Hex(certificate!)).Handle) : null;
            string? png = null;
            if (window != null)
            {
                var host = config.PairHost ?? PairingSetup.LanAddresses().FirstOrDefault() ?? "127.0.0.1";
                var sha = SelfSignedCertificate.Sha256Hex(certificate!);
                var port = PairingSetup.HttpsPort(config)!.Value;
                png = Path.Combine(Path.GetDirectoryName(config.HttpSelfSignedPath)!, "pairing-qr.png");
                try { PairingSetup.Announce(Console.Error, window, host, port, sha, png); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Console.Error.WriteLine("Could not write QR; PC: " + host + ":" + port + " code: " + window.Code);
                    Console.Error.WriteLine("Certificate: " + SelfSignedCertificate.Display(sha));
                }
            }
            void CleanQr()
            {
                if (png == null || store.Status().State == "waiting") return;
                try { File.Delete(png); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
            if (pairing != null) pairing.Paired += (_, _) => CleanQr();
            using var cleanup = new Timer(_ => CleanQr(), null, 1000, 1000);
            try { await app.WaitForShutdownAsync(); }
            finally
            {
                var current = store.Status().Window;
                if (current != null) store.Cancel(current.Id);
                CleanQr();
            }
            return 0;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException
            or System.Security.Cryptography.CryptographicException)
        {
            Console.Error.WriteLine("inventor-so-mcp-http: startup failed (" + ex.GetType().Name + "). Check runtime, HTTPS configuration, port and local file permissions.");
            return 2;
        }
    }
}
