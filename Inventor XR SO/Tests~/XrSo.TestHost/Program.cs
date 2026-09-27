using Bimwright.Ipt.Server;
using Bimwright.Ipt.Tests;
using Inventor.So.Mcp.Http;
using Inventor.So.Mcp.Http.Pairing;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// Inventor XR SO test host: the real HTTPS host and pairing in front of FakeAddIn (two bolts and a
// plate), so the Unity client and the headset can be developed without Inventor.
bool lan = args.Contains("--lan");
int churnIndex = Array.IndexOf(args, "--churn");
int churnSeconds = churnIndex >= 0 && churnIndex + 1 < args.Length && int.TryParse(args[churnIndex + 1], out var s) ? s : 0;
int stateIndex = Array.IndexOf(args, "--state");
string state = stateIndex >= 0 && stateIndex + 1 < args.Length ? args[stateIndex + 1] : Path.Combine(Path.GetTempPath(), "xrso-testhost");
Directory.CreateDirectory(state);

await using var addIn = new FakeAddIn(Path.Combine(state, "targets-" + Environment.ProcessId));
var tokens = new TokenRegistry();
var editorToken = TokenRegistry.Generate();
tokens.Add("editor", editorToken);
var config = new InventorMcpConfig
{
    // Assignment, not collection-init: InventorMcpConfig.HttpUrls defaults to a non-empty list
    // ("http://127.0.0.1:8787"), so `{ ... }` would append to it instead of replacing it.
    HttpUrls = new List<string> { lan ? "https://0.0.0.0:8443" : "https://127.0.0.1:0" },
    HttpSelfSignedCertificate = true,
    HttpSelfSignedPath = Path.Combine(state, "server.pfx"),
    HttpTokenFile = Path.Combine(state, "tokens-" + Environment.ProcessId + ".txt"),
    DescriptorDirectory = addIn.DescriptorDirectory,
    AssetDirectory = Path.Combine(state, "assets"),
    AuditDirectory = Path.Combine(state, "audit"),
    EnableExperimental = true,
};
var certificate = PairingSetup.ResolveCertificate(config);
var sha = SelfSignedCertificate.Sha256Hex(certificate);
var store = new PairingStore(() => DateTimeOffset.UtcNow);
var window = store.Open("quest3", TimeSpan.FromMinutes(30));
var app = HttpHost.Build(Array.Empty<string>(), config, tokens,
    new HostOptions { Certificate = certificate, Pairing = new PairingEndpoint(store, tokens, config.HttpTokenFile) });
await app.StartAsync();

var address = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>().Addresses.First());
var host = lan ? PairingSetup.LanAddresses().FirstOrDefault() ?? "127.0.0.1" : "127.0.0.1";
var baseUrl = "https://" + host + ":" + address.Port;
Console.OutputEncoding = System.Text.Encoding.UTF8;
PairingSetup.Announce(Console.Error, window, host, address.Port, sha, Path.Combine(state, "pairing-qr.png"));
Console.Out.WriteLine(new JObject
{
    ["ready"] = true, ["base_url"] = baseUrl, ["cert_sha256"] = sha, ["editor_token"] = editorToken,
    ["pair_code"] = window.Code, ["qr_payload"] = PairingSetup.QrPayload(host, address.Port, window.OneTimeToken, sha),
}.ToString(Formatting.None));
Console.Out.Flush();

using var stop = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
_ = Task.Run(() => { while (Console.In.Read() >= 0) { } stop.Cancel(); });
if (churnSeconds > 0)
    _ = Task.Run(async () =>
    {
        while (!stop.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(churnSeconds), stop.Token).ContinueWith(_ => { });
            if (!stop.IsCancellationRequested) addIn.RaiseDocumentChanged(true);
        }
    });
try { await Task.Delay(Timeout.Infinite, stop.Token); } catch (OperationCanceledException) { }
await app.StopAsync();
