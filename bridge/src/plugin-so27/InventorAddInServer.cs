using System.IO;
using System.Runtime.InteropServices;

namespace InventorSo;

[ComVisible(true)]
[Guid("7EE3B69B-8A24-42DD-9422-50E8C5279F40")]
[ProgId("InventorSo.AddIn2027")]
public sealed class InventorAddInServer : Bimwright.Ipt.Shared.Plugin.InventorAddInServerBase
{
    protected override string ProductDirectory => Path.Combine("InventorSO", "inventor-so-mcp");
    protected override string PipePrefix => "InventorSO";
    protected override bool RequireAtomicWrites => !FullAccessEnabled();

    private static bool FullAccessEnabled()
    {
        var value = System.Environment.GetEnvironmentVariable("BIMWRIGHT_INVENTOR_PLUGIN_FULL_ACCESS");
        return value?.Trim().ToLowerInvariant() is "1" or "true" or "yes" or "on";
    }
}
