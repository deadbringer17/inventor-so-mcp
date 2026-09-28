using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Bimwright.Ipt.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using QRCoder;

namespace Inventor.So.Mcp.Http.Pairing;

/// <summary>Startup helpers shared by the CLI host and the Inventor XR SO test host.</summary>
public static class PairingSetup
{
    /// <summary>The certificate to serve: --http-cert as before, else the self-signed one, else none.</summary>
    public static X509Certificate2? ResolveCertificate(InventorMcpConfig config)
    {
        if (!string.IsNullOrWhiteSpace(config.HttpCertificatePath))
            // SChannel needs a user key container when serving TLS on Windows.
            return new X509Certificate2(config.HttpCertificatePath!, config.HttpCertificatePassword, X509KeyStorageFlags.UserKeySet);
        return config.HttpSelfSignedCertificate
            ? SelfSignedCertificate.LoadOrCreate(config.HttpSelfSignedPath, CertificateHosts(config))
            : null;
    }

    public static IReadOnlyList<string> CertificateHosts(InventorMcpConfig config)
    {
        var hosts = new List<string> { "localhost", "127.0.0.1", Environment.MachineName };
        hosts.AddRange(LanAddresses());
        if (!string.IsNullOrWhiteSpace(config.PairHost)) hosts.Add(config.PairHost!);
        return hosts;
    }

    /// <summary>IPv4 addresses of the interfaces that are up, excluding loopback and tunnels.</summary>
    public static IEnumerable<string> LanAddresses() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up
                        && n.NetworkInterfaceType != NetworkInterfaceType.Loopback
                        && n.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a.Address))
            .Select(a => a.Address.ToString());

    /// <summary>Port of the first https URL, or null.</summary>
    public static int? HttpsPort(InventorMcpConfig config)
    {
        foreach (var url in config.HttpUrls)
        {
            if (!BindingPolicy.TryParse(url, out var scheme, out _) || scheme != "https") continue;
            var authority = url[(url.IndexOf("://", StringComparison.Ordinal) + 3)..];
            int slash = authority.IndexOf('/');
            if (slash >= 0) authority = authority[..slash];
            int colon = authority.LastIndexOf(':');
            if (colon > authority.LastIndexOf(']') && int.TryParse(authority[(colon + 1)..], out var port)) return port;
            return 443;
        }
        return null;
    }

    public static string QrPayload(string host, int port, string oneTimeToken, string certSha256) =>
        new JObject { ["v"] = 1, ["host"] = host, ["port"] = port, ["ott"] = oneTimeToken, ["cert_sha256"] = certSha256 }
            .ToString(Formatting.None);

    /// <summary>Print the QR code, the manual code and the fingerprint; save the QR as PNG.</summary>
    public static void Announce(TextWriter output, PairingWindow window, string host, int port, string certSha256, string pngPath)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(QrPayload(host, port, window.OneTimeToken, certSha256), QRCodeGenerator.ECCLevel.M);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(pngPath))!);
        File.WriteAllBytes(pngPath, new PngByteQRCode(data).GetGraphic(8));
        output.WriteLine(new AsciiQRCode(data).GetGraphic(1));
        output.WriteLine("Pairing open for '" + window.ClientName + "' until " + window.ExpiresUtc.ToLocalTime().ToString("HH:mm:ss") + ".");
        output.WriteLine("  Scan the QR code (also saved to " + pngPath + ") or enter on the headset:");
        output.WriteLine("  PC: " + host + ":" + port + "   code: " + window.Code);
        output.WriteLine("  Certificate: " + SelfSignedCertificate.Display(certSha256));
    }
}
