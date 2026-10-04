using System.Security.AccessControl;
using System.Security.Principal;

namespace Inventor.So.Pairing.Control;

public static class OwnerStorage
{
    /// <summary>Pairing secrets live below an owner-only Windows directory, including existing files.</summary>
    public static void ProtectDirectory(string path)
    {
        Directory.CreateDirectory(path);
        if (!OperatingSystem.IsWindows()) return;
        using var current = WindowsIdentity.GetCurrent();
        var owner = current.User!;
        var acl = new DirectorySecurity();
        acl.SetOwner(owner);
        acl.SetAccessRuleProtection(true, false);
        acl.AddAccessRule(new FileSystemAccessRule(owner, FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(path).SetAccessControl(acl);
        foreach (var file in Directory.EnumerateFiles(path))
        {
            var fileAcl = new FileSecurity();
            fileAcl.SetOwner(owner);
            fileAcl.SetAccessRuleProtection(true, false);
            fileAcl.AddAccessRule(new FileSystemAccessRule(owner, FileSystemRights.FullControl, AccessControlType.Allow));
            new FileInfo(file).SetAccessControl(fileAcl);
        }
    }
}
