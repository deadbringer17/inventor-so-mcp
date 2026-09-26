using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Inventor.So.Mcp.Http.Pairing;

/// <summary>
/// The host's own TLS certificate when no PFX is configured: ECDSA P-256, created once and kept,
/// because paired clients pin its SHA-256. Only a certificate about to expire is replaced (which
/// means pairing again). Host names go into the SAN for completeness; clients trust the pin, not names.
/// </summary>
public static class SelfSignedCertificate
{
    public static readonly TimeSpan Validity = TimeSpan.FromDays(5 * 365);
    public static readonly TimeSpan RenewBefore = TimeSpan.FromDays(30);

    public static X509Certificate2 LoadOrCreate(string pfxPath, IEnumerable<string> hostNames, DateTimeOffset? now = null)
    {
        var clock = now ?? DateTimeOffset.UtcNow;
        if (File.Exists(pfxPath))
        {
            var existing = Load(File.ReadAllBytes(pfxPath));
            if (existing.NotAfter.ToUniversalTime() > (clock + RenewBefore).UtcDateTime) return existing;
            existing.Dispose();
        }
        var pfx = Create(hostNames, clock);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(pfxPath))!);
        File.WriteAllBytes(pfxPath, pfx);
        return Load(pfx);
    }

    public static string Sha256Hex(X509Certificate2 certificate) =>
        Convert.ToHexString(SHA256.HashData(certificate.RawData)).ToLowerInvariant();

    /// <summary>"ABCD 0123 …": the form both the PC console and the headset show for a visual check.</summary>
    public static string Display(string sha256Hex) =>
        string.Join(" ", Enumerable.Range(0, sha256Hex.Length / 4).Select(i => sha256Hex.Substring(i * 4, 4).ToUpperInvariant()));

    private static byte[] Create(IEnumerable<string> hostNames, DateTimeOffset now)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=Inventor SO MCP", key, HashAlgorithmName.SHA256);
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("localhost");
        foreach (var host in hostNames.Where(h => !string.IsNullOrWhiteSpace(h) && h != "localhost").Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (IPAddress.TryParse(host, out var ip)) san.AddIpAddress(ip);
            else san.AddDnsName(host);
        }
        request.CertificateExtensions.Add(san.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, false));
        using var certificate = request.CreateSelfSigned(now.AddDays(-1), now + Validity);
        return certificate.Export(X509ContentType.Pkcs12);
    }

    // UserKeySet, not EphemeralKeySet: Windows TLS (SChannel) cannot serve with an ephemeral key.
    private static X509Certificate2 Load(byte[] pfx) => new(pfx, (string?)null, X509KeyStorageFlags.UserKeySet);
}
