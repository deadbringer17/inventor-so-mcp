using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Inventor.So.Mcp.Http;
using Inventor.So.Mcp.Http.Pairing;
using Inventor.So.Pairing.Control;

namespace Bimwright.Ipt.Tests;

public sealed class WindowsPairingProcessFactAttribute : FactAttribute
{
    public WindowsPairingProcessFactAttribute()
    {
        if (!OperatingSystem.IsWindows()) { Skip = "Windows process/ACL gate."; return; }
        using var probe = new System.IO.Pipes.NamedPipeClientStream(".", ControlNames.HostPipe, System.IO.Pipes.PipeDirection.InOut);
        try { probe.Connect(50); Skip = "Managed user host already running; isolated probe must not administrate it."; }
        catch (TimeoutException) { }
        if (LocalPairingController.LanAddresses().Length == 0) Skip = "No LAN IPv4 address available for the process pairing gate.";
    }
}

public sealed class PairingHostProcessTests
{
    [Fact]
    public async Task InsecureManagedLanIsRefusedBeforeCreatingCredentialDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "so-insecure-refused-" + Guid.NewGuid().ToString("N"));
        var exitCode = await HttpProgram.Main(["--pair-control", "--http-token-file", Path.Combine(root, "tokens.txt"),
            "--http-urls", "http://0.0.0.0:8443", "--http-allow-insecure-lan"]);
        Assert.Equal(2, exitCode);
        Assert.False(Directory.Exists(root));
    }

    [WindowsPairingProcessFact]
    public async Task ManagedProcessStartsWithEmptyRegistryAndRedeemsViaPinnedHttps()
    {
        // Never administrate an existing user's host. This probe needs the managed pipe to be free.
        try
        {
            await ControlWire.CallAsync(ControlNames.HostPipe, new ControlRequest(), CancellationToken.None, 100);
            Assert.Fail("A user's host started after test discovery. Probe cancelled before any mutation.");
        }
        catch (OperationCanceledException) { }
        var root = Path.Combine(Path.GetTempPath(), "so-pair-process-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var pfx = Path.Combine(root, "server.pfx");
        using var cert = SelfSignedCertificate.LoadOrCreate(pfx, ["localhost", "127.0.0.1"]);
        var sha = SelfSignedCertificate.Sha256Hex(cert);
        var configPath = Path.Combine(root, "host.json");
        File.WriteAllText(configPath, JsonSerializer.Serialize(new { assetDirectory = Path.Combine(root, "assets"),
            auditDirectory = Path.Combine(root, "audit") }));
        // Random unused HTTPS port; the host's bound port is returned by its local controller.
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start(); var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        var entry = typeof(HttpProgram).Assembly.Location;
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var name in new[] { "INVENTOR_SO_HTTP_TOKEN", "INVENTOR_SO_HTTP_CERT", "INVENTOR_SO_HTTP_CERT_PASSWORD" }) start.Environment.Remove(name);
        foreach (var arg in new[] { entry, "--pair-control", "--config", configPath, "--http-cert", pfx,
            "--http-token-file", Path.Combine(root, "tokens.txt"), "--http-urls", "https://0.0.0.0:" + port,
            "--target", "inventor-2027-2147483647", "--read-only", "--no-audit" }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        ControlResponse? status = null;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try
        {
            while (status == null)
            {
                Assert.False(process.HasExited, "Managed HTTPS process exited during startup.");
                try { status = await ControlWire.CallAsync(ControlNames.HostPipe, new ControlRequest(), deadline.Token, 250); }
                catch (OperationCanceledException) when (!deadline.IsCancellationRequested) { }
            }
            Assert.Equal(process.Id, status.ServerPid); // Fail before issuing mutations if somebody else's host won the race.
            Assert.Equal("ready", status.State); Assert.Null(status.TargetId); Assert.Equal(port, status.Port);
            Assert.Equal(sha, status.CertSha256);
            Assert.False(File.Exists(Path.Combine(root, "tokens.txt")));
            Assert.NotEmpty(status.Addresses);
            var open = await ControlWire.CallAsync(ControlNames.HostPipe, new ControlRequest(Operation: "open", Host: status.Addresses[0].Address), deadline.Token);
            Assert.True(open.Ok);
            using var client = new HttpClient(new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, presented, _, _) =>
                presented != null && SelfSignedCertificate.Sha256Hex(presented) == sha })
                { BaseAddress = new Uri("https://127.0.0.1:" + port), Timeout = TimeSpan.FromSeconds(5) };
            var response = await client.PostAsync("/pair", new StringContent(JsonSerializer.Serialize(new { secret = open.Code, device_name = "Windows isolated probe" }), Encoding.UTF8, "application/json"));
            response.EnsureSuccessStatusCode();
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var token = body.RootElement.GetProperty("token").GetString()!;
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            Assert.Equal("paired", (await ControlWire.CallAsync(ControlNames.HostPipe, new ControlRequest(), deadline.Token)).State);
            var replacement = await ControlWire.CallAsync(ControlNames.HostPipe, new ControlRequest(Operation: "open", Host: status.Addresses[0].Address), deadline.Token);
            Assert.Equal(process.Id, replacement.ServerPid);
            Assert.True((await ControlWire.CallAsync(ControlNames.HostPipe, new ControlRequest(Operation: "cancel", WindowId: replacement.WindowId), deadline.Token)).Ok);
            Assert.False(File.Exists(Path.Combine(root, "pairing-qr.png")));
            Assert.Contains(token, File.ReadAllText(Path.Combine(root, "tokens.txt")));
            Assert.True((await client.GetAsync("/healthz")).IsSuccessStatusCode);
            // Administration has no HTTP route, even to a bearer-authenticated client.
            Assert.Equal(System.Net.HttpStatusCode.NotFound, (await client.PostAsync("/pair/open", new StringContent("{}"))).StatusCode);
        }
        finally
        {
            if (!process.HasExited) { process.Kill(); await process.WaitForExitAsync(); }
            var output = await stdout + await stderr;
            Assert.DoesNotContain("\"secret\"", output);
            Directory.Delete(root, true);
        }
    }
}
