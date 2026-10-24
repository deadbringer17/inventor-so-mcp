using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Path = System.IO.Path;

internal sealed record Options73(string RunId, string Host, string OutputDirectory, string DescriptorDirectory,
    string ExpectedAddinPath, string ExpectedAddinSha, string ManifestPath, string ManifestSha,
    string InstalledDllPath, string InstalledDllSha)
{
    internal static Options73 Parse(string[] args)
    {
        var known = new HashSet<string>(StringComparer.Ordinal) { "--run-id", "--host", "--output-dir", "--descriptor-dir",
            "--expected-addin-path", "--expected-addin-sha256", "--manifest-path", "--manifest-sha256",
            "--installed-dll-path", "--installed-dll-sha256" };
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (args.Length % 2 != 0) throw new HostFailure73("Argument pairs are required.");
        for (int i = 0; i < args.Length; i += 2)
            if (!known.Contains(args[i]) || !values.TryAdd(args[i], args[i + 1]))
                throw new HostFailure73("Unknown or duplicate option.");
        string Required(string key) => values.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value)
            ? value : throw new HostFailure73("A required public metadata option is absent.");
        string Hash(string key)
        {
            string value = Required(key);
            if (!Regex.IsMatch(value, "\\A[0-9a-fA-F]{64}\\z", RegexOptions.CultureInvariant))
                throw new HostFailure73("SHA256 metadata is invalid.");
            return value.ToUpperInvariant();
        }
        string run = Required("--run-id");
        if (!Regex.IsMatch(run, "\\A[0-9a-f]{32}\\z", RegexOptions.CultureInvariant))
            throw new HostFailure73("Run id must contain 32 lowercase hexadecimal characters.");
        string host = Required("--host");
        if (!IPAddress.TryParse(host, out var address) || address.AddressFamily != AddressFamily.InterNetwork || address.ToString() != host)
            throw new HostFailure73("A canonical loopback or private IPv4 address is required.");
        byte[] octets = address.GetAddressBytes();
        bool accepted = host == "127.0.0.1" || octets[0] == 10 || octets[0] == 172 && octets[1] is >= 16 and <= 31
            || octets[0] == 192 && octets[1] == 168;
        if (!accepted) throw new HostFailure73("Host must be loopback or a private IPv4 address.");
        string addin = Path.GetFullPath(Required("--expected-addin-path"));
        if (!string.Equals(Path.GetFileName(addin), "Inventor.So.AddIn.dll", StringComparison.OrdinalIgnoreCase))
            throw new HostFailure73("Expected add-in filename differs.");
        string descriptors = values.TryGetValue("--descriptor-dir", out var directory) ? Path.GetFullPath(directory)
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "InventorSO", "inventor-so-mcp");
        return new(run, host, Path.GetFullPath(Required("--output-dir")), descriptors, addin, Hash("--expected-addin-sha256"),
            Path.GetFullPath(Required("--manifest-path")), Hash("--manifest-sha256"),
            Path.GetFullPath(Required("--installed-dll-path")), Hash("--installed-dll-sha256"));
    }
}

internal sealed class HostFailure73(string assertion) : Exception(assertion);
