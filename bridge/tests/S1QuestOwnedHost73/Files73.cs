using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using File = System.IO.File;
using Path = System.IO.Path;

internal static partial class Program
{
    private static void CreatePrivateDirectory73(string path)
    {
        if (Directory.Exists(path) || File.Exists(path)) throw new HostFailure73("Owned directory already exists.");
        var owner = WindowsIdentity.GetCurrent().User ?? throw new HostFailure73("Current owner SID unavailable.");
        var directory = Directory.CreateDirectory(path);
        var acl = new DirectorySecurity(); acl.SetOwner(owner); acl.SetAccessRuleProtection(true, false);
        acl.AddAccessRule(new FileSystemAccessRule(owner, FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        directory.SetAccessControl(acl);
    }
    private static string HashFile73(string path)
    {
        if (!File.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new HostFailure73("Expected regular metadata file is unavailable.");
        using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream));
    }
    private static void CheckInstallation73(string field)
    {
        string addin = HashFile73(Settings.ExpectedAddinPath), manifest = HashFile73(Settings.ManifestPath), installed = HashFile73(Settings.InstalledDllPath);
        if (addin != Settings.ExpectedAddinSha || manifest != Settings.ManifestSha || installed != Settings.InstalledDllSha)
            throw new HostFailure73("Expected stage/manifest/original DLL hash differs.");
        Evidence[field] = new JObject { ["stage_addin_sha256"] = addin, ["manifest_sha256"] = manifest,
            ["original_installed_dll_sha256"] = installed, ["unchanged"] = true };
    }
    private static void WritePublic73(string name, JObject value)
    {
        if (Path.GetFileName(name) != name || name == "bootstrap.json") throw new HostFailure73("Public evidence filename is invalid.");
        string path = Path.Combine(Settings.OutputDirectory, name);
        // Names come from fixed code only. No arbitrary stdio path or content is accepted.
        File.WriteAllText(path, value.ToString(Formatting.Indented), new UTF8Encoding(false));
    }
    private static void WriteBootstrap73()
    {
        GuardDocuments73();
        if (BootstrapWritten || Bearer == null || DateTimeOffset.UtcNow >= ExpiresUtc)
            throw new HostFailure73("Bootstrap is already written, expired or unavailable.");
        string path = Path.Combine(Settings.OutputDirectory, "bootstrap.json");
        var bootstrap = new JObject { ["schema"] = 1, ["run_id"] = Settings.RunId, ["expires_utc"] = ExpiresUtc.ToString("O"),
            ["host"] = Settings.Host, ["port"] = Port, ["cert_sha256"] = CertificateSha,
            ["client_name"] = "s1-qa-" + Settings.RunId, ["token"] = Bearer, ["target_id"] = TargetId,
            ["part_document_id"] = Documents.Single(d => !d.Sheet).DocumentId,
            ["sheet_document_id"] = Documents.Single(d => d.Sheet).DocumentId };
        byte[] bytes = new UTF8Encoding(false).GetBytes(bootstrap.ToString(Formatting.None));
        try
        {
            var owner = WindowsIdentity.GetCurrent().User ?? throw new HostFailure73("Current owner SID unavailable.");
            var acl = new FileSecurity(); acl.SetOwner(owner); acl.SetAccessRuleProtection(true, false);
            acl.AddAccessRule(new FileSystemAccessRule(owner, FileSystemRights.FullControl, AccessControlType.Allow));
            // CreateNew supplies the owner-only DACL atomically and requests
            // FullControl; a Write-only stream handle cannot change its own DACL.
            using var stream = new FileInfo(path).Create(FileMode.CreateNew, FileSystemRights.FullControl,
                FileShare.None, 4096, FileOptions.None, acl);
            BootstrapWritten = true;
            stream.Write(bytes); stream.Flush(true);
        }
        finally { Array.Clear(bytes); }
    }
}
