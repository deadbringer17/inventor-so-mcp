using Inventor.So.Pairing.Control;

namespace Inventor.So.Pairing.Desktop;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        var index = Array.IndexOf(args, "--target");
        var target = index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        using var mutex = new Mutex(true, ControlNames.DesktopMutex, out var first);
        if (!first)
        {
            try
            {
                ControlWire.CallAsync(ControlNames.DesktopPipe, new ControlRequest(Operation: "activate", TargetId: target),
                    CancellationToken.None, 5000).GetAwaiter().GetResult();
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or OperationCanceledException)
            { MessageBox.Show("La finestra di connessione è già aperta ma non risponde. Riprova tra pochi secondi.", "Inventor SO"); }
            return;
        }
        try { Application.Run(new PairingForm(target)); }
        finally { mutex.ReleaseMutex(); }
    }
}
