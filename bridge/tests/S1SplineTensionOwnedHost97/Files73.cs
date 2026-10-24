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
        string temporary=path+".publish-"+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            File.WriteAllText(temporary,value.ToString(Formatting.Indented),new UTF8Encoding(false));
            for(int attempt=0;;attempt++)
            {
                try { File.Move(temporary,path,true); break; }
                catch(IOException error) when(attempt<50 && (error.HResult==unchecked((int)0x80070020)||error.HResult==unchecked((int)0x80070021)))
                { Thread.Sleep(20); } // Metadata publication only; never repeat a CAD operation.
                catch(UnauthorizedAccessException error) when(attempt<50 && error.HResult==unchecked((int)0x80070005)
                    && File.Exists(path) && (File.GetAttributes(path)&FileAttributes.ReparsePoint)==0)
                { Thread.Sleep(20); } // Windows can report a reader denying replacement as access denied.
            }
        }
        finally { if(File.Exists(temporary)) File.Delete(temporary); }
    }
}
