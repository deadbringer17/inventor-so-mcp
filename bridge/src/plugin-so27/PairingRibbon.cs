using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Inv = global::Inventor;

namespace InventorSo;

/// <summary>UI entry point only: no server, secrets or network work inside Inventor.</summary>
internal sealed class PairingRibbon : IDisposable
{
    private const string ClientId = "{7EE3B69B-8A24-42DD-9422-50E8C5279F40}";
    private const string ButtonId = "InventorSO.Pairing.Open";
    private readonly Inv.Application _app;
    private readonly string _targetId;
    private readonly Inv.ButtonDefinition _button;
    private readonly Inv.UserInterfaceEvents _events;
    private readonly List<Inv.CommandControl> _controls = new();
    private readonly List<Inv.RibbonTab> _tabs = new();

    public PairingRibbon(Inv.Application app, string targetId)
    {
        _app = app; _targetId = targetId;
        var definitions = app.CommandManager.ControlDefinitions;
        try { _button = (Inv.ButtonDefinition)definitions[ButtonId]; }
        catch
        {
            _button = definitions.AddButtonDefinition("Associa visore", ButtonId, Inv.CommandTypesEnum.kQueryOnlyCmdType,
                ClientId, "Collega Inventor XR SO al PC tramite QR o codice manuale.", "Associa visore",
                Type.Missing, Type.Missing, Inv.ButtonDisplayEnum.kAlwaysDisplayText);
        }
        _events = app.UserInterfaceManager.UserInterfaceEvents;
        try
        {
            _button.OnExecute += Execute;
            _events.OnResetRibbonInterface += Reset;
            InstallControls();
        }
        catch { Dispose(); throw; }
    }

    private void InstallControls()
    {
        foreach (Inv.Ribbon ribbon in _app.UserInterfaceManager.Ribbons)
        {
            var suffix = ribbon.InternalName;
            Inv.RibbonTab tab;
            try { tab = ribbon.RibbonTabs["InventorSO.Pairing.Tab." + suffix]; }
            catch { tab = ribbon.RibbonTabs.Add("Inventor SO", "InventorSO.Pairing.Tab." + suffix, ClientId); }
            _tabs.Add(tab);
            Inv.RibbonPanel panel;
            try { panel = tab.RibbonPanels["InventorSO.Pairing.Panel." + suffix]; }
            catch { panel = tab.RibbonPanels.Add("Visore XR", "InventorSO.Pairing.Panel." + suffix, ClientId); }
            var exists = false;
            foreach (Inv.CommandControl control in panel.CommandControls)
                if (control.ControlDefinition.InternalName == ButtonId) { exists = true; _controls.Add(control); break; }
            if (!exists) _controls.Add(panel.CommandControls.AddButton(_button, true, true));
        }
    }

    private void Reset(Inv.NameValueMap context)
    {
        _controls.Clear(); _tabs.Clear();
        InstallControls();
    }

    private void Execute(Inv.NameValueMap context)
    {
        // Capture only strings before leaving STA; the worker never touches Inventor COM.
        var directory = Path.GetDirectoryName(typeof(PairingRibbon).Assembly.Location)!;
        var targetId = _targetId;
        _ = Task.Run(() =>
        {
            try
            {
                var executable = Path.GetFullPath(Path.Combine(directory, "..", "pairing-desktop", "Inventor.So.Pairing.exe"));
                if (!File.Exists(executable)) throw new FileNotFoundException("Applicazione di connessione non installata. Installa un pacchetto Inventor SO completo.");
                var start = new ProcessStartInfo(executable) { UseShellExecute = false };
                start.ArgumentList.Add("--target"); start.ArgumentList.Add(targetId);
                using var process = Process.Start(start);
            }
            catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
            { MessageBox.Show(ex.Message, "Inventor SO — Connessione visore", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        });
    }

    public void Dispose()
    {
        try { _button.OnExecute -= Execute; } catch { }
        try { _events.OnResetRibbonInterface -= Reset; } catch { }
        foreach (var control in _controls) try { control.Delete(); } catch { }
        foreach (var tab in _tabs) try { tab.Delete(); } catch { }
        _controls.Clear(); _tabs.Clear();
        try { _button.Delete(); } catch { }
    }
}
