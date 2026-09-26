namespace InventorXrSo.Core.Tests.Support;

public static class RepoPaths
{
    /// <summary>The "Inventor XR SO" folder, found by walking up from the test binaries.</summary>
    public static string XrProject
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Packages", "com.occhipinti.inventorxrso.core"))) dir = dir.Parent;
            return dir?.FullName ?? throw new DirectoryNotFoundException("Inventor XR SO project not found above " + AppContext.BaseDirectory);
        }
    }
}
