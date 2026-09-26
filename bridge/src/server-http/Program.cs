using System.Linq;
using Bimwright.Ipt.Server;
using Inventor.So.Mcp.Http.Pairing;
using HostOptions = Inventor.So.Mcp.Http.Pairing.HostOptions;

namespace Inventor.So.Mcp.Http;

// Inventor.So.Mcp.Http - remote MCP host for Inventor SO (docs/INVENTOR_SO_MCP_IMPLEMENTATION_PLAN.md §21).
//   --generate-token <name>   print a new "name:token" line for the token file and exit
//   otherwise                 same configuration as the stdio server, plus the --http-* options
// An explicit class, not top-level statements: the stdio server already owns the global Program.
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
            registry.Add(name, token);   // validates the name
            Console.WriteLine(name + ":" + token);
            return 0;
        }

        var config = InventorMcpConfig.Load(args);
        TokenRegistry tokens;
        WebApplication app;
        PairingWindow? window = null;
        PairingEndpoint? pairing = null;
        System.Security.Cryptography.X509Certificates.X509Certificate2? certificate;
        try
        {
            tokens = TokenRegistry.Load(config);
            certificate = PairingSetup.ResolveCertificate(config);
            if (config.PairClientName != null)
            {
                if (string.IsNullOrWhiteSpace(config.HttpTokenFile))
                    throw new InvalidOperationException("--pair needs --http-token-file: the new token is appended there.");
                if (certificate == null)
                    throw new InvalidOperationException("--pair needs HTTPS: use --http-self-signed (or --http-cert) with an https:// URL.");
                if (tokens.Names.Contains(config.PairClientName))
                    throw new InvalidOperationException("--pair: client '" + config.PairClientName + "' already has a token; choose another name.");
                var store = new PairingStore(() => DateTimeOffset.UtcNow);
                window = store.Open(config.PairClientName, PairingStore.DefaultTtl);
                pairing = new PairingEndpoint(store, tokens, config.HttpTokenFile!);
            }
            app = HttpHost.Build(args, config, tokens, new HostOptions { Certificate = certificate, Pairing = pairing });
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or System.Security.Cryptography.CryptographicException)
        {
            Console.Error.WriteLine("inventor-so-mcp-http: " + ex.Message);
            return 2;
        }
        Console.Error.WriteLine("inventor-so-mcp-http: listening on " + string.Join(", ", config.HttpUrls) +
            " for " + tokens.Count + " client token(s)" + (config.HttpAllowInsecureLan ? " (INSECURE LAN MODE)" : "") + ".");
        if (window != null && pairing != null)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            var host = config.PairHost ?? PairingSetup.LanAddresses().FirstOrDefault() ?? "127.0.0.1";
            var sha = SelfSignedCertificate.Sha256Hex(certificate!);
            var png = Path.Combine(Path.GetDirectoryName(config.HttpSelfSignedPath)!, "pairing-qr.png");
            PairingSetup.Announce(Console.Error, window, host, PairingSetup.HttpsPort(config) ?? 443, sha, png);
            pairing.Paired += (client, device) =>
            {
                Console.Error.WriteLine("inventor-so-mcp-http: paired '" + client + "'" + (device == null ? "" : " (" + device + ")") + ".");
                try { File.Delete(png); } catch (IOException) { }
            };
        }
        await app.RunAsync();
        return 0;
    }
}
