using System.IO;
using System.Runtime.InteropServices;

namespace InventorSo;

[ComVisible(true)]
[Guid("7EE3B69B-8A24-42DD-9422-50E8C5279F40")]
[ProgId("InventorSo.AddIn2027")]
public sealed class InventorAddInServer : Bimwright.Ipt.Shared.Plugin.InventorAddInServerBase
{
    private PairingRibbon? _pairingRibbon;
    private System.Windows.Forms.Timer? _pairingDocumentTimer;
    protected override void OnActivated(global::Inventor.Application application, string targetId)
    {
        _pairingDocumentTimer = new System.Windows.Forms.Timer { Interval = 2000 };
        _pairingDocumentTimer.Tick += (_, _) => RefreshDocumentDescriptor();
        _pairingDocumentTimer.Start();
        try { _pairingRibbon = new PairingRibbon(application, targetId); }
        catch (System.Exception ex) { System.Diagnostics.Trace.WriteLine("Inventor SO pairing ribbon unavailable: " + ex.GetType().Name); }
    }
    protected override void OnDeactivating()
    {
        _pairingDocumentTimer?.Dispose();
        _pairingDocumentTimer = null;
        _pairingRibbon?.Dispose();
        _pairingRibbon = null;
    }
    protected override string ProductDirectory => Path.Combine("InventorSO", "inventor-so-mcp");
    protected override string PipePrefix => "InventorSO";
    protected override bool RequireAtomicWrites => !FullAccessEnabled();

    private static bool FullAccessEnabled()
    {
        var value = System.Environment.GetEnvironmentVariable("BIMWRIGHT_INVENTOR_PLUGIN_FULL_ACCESS");
        return value?.Trim().ToLowerInvariant() is "1" or "true" or "yes" or "on";
    }
}
