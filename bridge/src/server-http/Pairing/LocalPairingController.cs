using System.Net.NetworkInformation;
using System.Net.Sockets;
using Bimwright.Ipt.Server;
using Inventor.So.Pairing.Control;

namespace Inventor.So.Mcp.Http.Pairing;

/// <summary>Local administration only; never mapped to an HTTP route.</summary>
public sealed class LocalPairingController
{
    private readonly object _gate = new();
    private readonly PairingStore _store;
    private readonly InventorMcpConfig _config;
    private readonly PluginClient _plugin;
    private readonly string _sha;
    private readonly Func<NetworkAddress[]> _addresses;
    private string? _windowHost;

    public LocalPairingController(PairingStore store, InventorMcpConfig config, PluginClient plugin,
        string sha, Func<NetworkAddress[]>? addresses = null)
    {
        _store = store; _config = config; _plugin = plugin; _sha = sha;
        _addresses = addresses ?? LanAddresses;
    }

    public static NetworkAddress[] LanAddresses() => NetworkInterface.GetAllNetworkInterfaces()
        .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback
            && n.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
        .SelectMany(n => n.GetIPProperties().UnicastAddresses
            .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork && !System.Net.IPAddress.IsLoopback(a.Address))
            .Select(a => new NetworkAddress(a.Address.ToString(), n.Name)))
        .DistinctBy(a => a.Address).ToArray();

    public ControlResponse Handle(ControlRequest request)
    {
        lock (_gate)
        {
            try
            {
                var addresses = _addresses();
                var before = _store.Status();
                if (before.State == "waiting" && _windowHost != null && !addresses.Any(a => a.Address == _windowHost))
                    _store.Cancel(before.Window!.Id);
                switch (request.Operation)
                {
                    case "status": return Status(addresses);
                    case "open":
                        if (!addresses.Any(a => a.Address == request.Host) || !CanAdvertise(request.Host!))
                            return ControlResponse.Error("NETWORK_UNAVAILABLE", "Seleziona un indirizzo LAN sul quale il server HTTPS è in ascolto.");
                        _windowHost = request.Host;
                        var window = _store.Open("quest-" + Guid.NewGuid().ToString("N"), PairingStore.DefaultTtl);
                        return Status(addresses) with
                        {
                            Code = window.Code,
                            QrPayload = PairingSetup.QrPayload(request.Host!, PairingSetup.HttpsPort(_config)!.Value, window.OneTimeToken, _sha)
                        };
                    case "cancel":
                        if (!_store.Cancel(request.WindowId))
                            return ControlResponse.Error("WINDOW_STALE", "Il codice è già stato sostituito. Aggiorna lo stato.");
                        return Status(addresses);
                    default: return ControlResponse.Error("UNKNOWN_OPERATION", "Operazione non supportata.");
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NetworkInformationException)
            { return ControlResponse.Error("LOCAL_STATE_UNAVAILABLE", "Impossibile leggere lo stato locale. Riprova."); }
        }
    }

    private bool CanAdvertise(string host) => _config.HttpUrls.Any(url =>
        BindingPolicy.TryParse(url, out var scheme, out var bound) && scheme == "https" &&
        (bound is "0.0.0.0" or "*" or "+" or "::" || bound == host));

    private ControlResponse Status(NetworkAddress[] addresses)
    {
        var status = _store.Status();
        var target = _plugin.CurrentTarget;
        return new ControlResponse
        {
            ServerPid = Environment.ProcessId, TargetRule = _config.TargetId,
            TargetId = target?.TargetId, Document = target?.DocumentTitle,
            Targets = _plugin.ListTargets().Where(t => t.InventorYear == 2027)
                .Select(t => new InventorTarget(t.TargetId, t.DocumentTitle)).ToArray(),
            Addresses = addresses, Port = PairingSetup.HttpsPort(_config) ?? 0, CertSha256 = _sha,
            State = status.State, WindowId = status.Window?.Id, ExpiresUtc = status.Window?.ExpiresUtc,
            RemainingSeconds = status.RemainingSeconds, ClientName = status.Window?.ClientName
        };
    }
}
