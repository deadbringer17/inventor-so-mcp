using System.Diagnostics;
using System.Net.Sockets;
using System.Text.Json;
using Inventor.So.Pairing.Control;

namespace Inventor.So.Pairing.Desktop;

internal static class HostLauncher
{
    public static string ProfileDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "InventorSO", "inventor-so-mcp", "http");

    public static InventorTarget[] DiscoverTargets()
    {
        var directory = Path.GetDirectoryName(ProfileDirectory)!;
        if (!Directory.Exists(directory)) return [];
        var targets = new List<InventorTarget>();
        foreach (var file in Directory.EnumerateFiles(directory, "inventor-2027-*.json"))
        {
            try
            {
                using var json = JsonDocument.Parse(File.ReadAllText(file));
                var root = json.RootElement;
                if (root.GetProperty("host_app").GetString() != "Inventor" || root.GetProperty("inventor_year").GetInt32() != 2027) continue;
                if (DateTimeOffset.UtcNow - root.GetProperty("last_heartbeat_utc").GetDateTimeOffset() > TimeSpan.FromSeconds(120)) continue;
                using var process = Process.GetProcessById(root.GetProperty("process_id").GetInt32());
                if (process.HasExited) continue;
                var document = root.TryGetProperty("document_title", out var title) ? title.GetString() : null;
                targets.Add(new InventorTarget(root.GetProperty("target_id").GetString()!, document));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException
                or InvalidOperationException or KeyNotFoundException) { }
        }
        return targets.ToArray();
    }

    public static async Task<ControlResponse?> TryStatusAsync(CancellationToken ct)
    {
        try { return await ControlWire.CallAsync(ControlNames.HostPipe, new ControlRequest(), ct, 500); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return null; }
    }

    public static async Task<ControlResponse> StartAsync(string target, CancellationToken ct)
    {
        var existing = await TryStatusAsync(ct);
        if (existing != null) return existing;
        // An occupied port is not evidence of our managed host. Do not start a duplicate.
        using (var tcp = new TcpClient())
        {
            try
            {
                await tcp.ConnectAsync("127.0.0.1", 8443, ct);
                throw new InvalidOperationException("La porta 8443 è occupata da un server senza controllo locale compatibile. Premi ‘Scollega altri server’ per rimuovere un vecchio backend Inventor SO, poi riprova. Se il programma non compare nell'elenco, la porta appartiene a un'altra applicazione.");
            }
            catch (SocketException) { }
        }
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".."));
        var host = Path.Combine(root, "server-http", "Inventor.So.Mcp.Http.exe");
        if (!File.Exists(host))
        {
            // Source checkout build layout, useful without installing a package.
            host = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..",
                "server-http", "bin", "Debug", "net8.0", "Inventor.So.Mcp.Http.exe"));
        }
        if (!File.Exists(host)) throw new FileNotFoundException("Server HTTP mancante. Ricrea o reinstalla il pacchetto Inventor SO.");
        OwnerStorage.ProtectDirectory(ProfileDirectory);
        var start = new ProcessStartInfo(host)
        {
            UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = Path.GetDirectoryName(host)!
        };
        // Suppress native missing-runtime dialogs; report failures in the connection window.
        start.Environment["DOTNET_DISABLE_GUI_ERRORS"] = "1";
        foreach (var argument in new[] { "--pair-control", "--http-urls", "https://0.0.0.0:8443", "--http-self-signed",
            "--http-token-file", Path.Combine(ProfileDirectory, "tokens.txt"), "--target", target, "--enable-experimental" })
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Impossibile avviare il server HTTP.");
        // No redirected output pipes: the host must outlive the desktop window.
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromSeconds(20))
        {
            if (process.HasExited)
                throw new InvalidOperationException("Il server non si è avviato. Verifica .NET 8 ASP.NET Core Runtime x64, porta 8443, certificato e permessi della cartella locale.");
            var status = await TryStatusAsync(ct);
            if (status != null) return status;
            await Task.Delay(250, ct);
        }
        throw new TimeoutException("Il server non risponde entro 20 secondi. Riprova: un avvio lento verrà riutilizzato.");
    }
}
