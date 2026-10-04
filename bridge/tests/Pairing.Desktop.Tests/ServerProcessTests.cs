using System.Diagnostics;
using Inventor.So.Pairing.Desktop;

namespace Inventor.So.Pairing.Desktop.Tests;

public sealed class ServerProcessTests
{
    [Fact]
    public async Task DisconnectStopsIsolatedRecognizedHttpBackend()
    {
        var host = Path.Combine(AppContext.BaseDirectory, "Inventor.So.Mcp.Http.dll");
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var root = Path.Combine(Path.GetTempPath(), "so-disconnect-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var tokens = Path.Combine(root, "tokens.txt");
        File.WriteAllText(tokens, "disconnect-test:" + new string('a', 64));
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true };
        start.Environment.Remove("INVENTOR_SO_HTTP_CERT");
        foreach (var arg in new[] { host, "--http-urls", "http://127.0.0.1:" + port, "--http-token-file", tokens, "--no-audit" }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        try
        {
            ServerProcess? candidate = null;
            for (var i = 0; i < 30; i++)
            {
                Assert.False(process.HasExited, "Isolated backend exited before discovery");
                candidate = ServerProcesses.Discover().FirstOrDefault(p => p.Pid == process.Id);
                if (candidate != null) break;
                await Task.Delay(100);
            }
            Assert.NotNull(candidate);
            await Assert.ThrowsAsync<InvalidOperationException>(() => ServerProcesses.DisconnectAsync(candidate! with { StartedTicks = candidate!.StartedTicks + 1 }, CancellationToken.None));
            Assert.False(process.HasExited);
            await ServerProcesses.DisconnectAsync(candidate!, CancellationToken.None);
            Assert.True(process.HasExited);
        }
        finally { if (!process.HasExited) { process.Kill(); await process.WaitForExitAsync(); } Directory.Delete(root, true); }
    }

    [Fact]
    public void RecognizesOnlyHttpExecutableOrActualDotnetEntrypoint()
    {
        var assembly = typeof(Bimwright.Ipt.Server.InventorMcpConfig).Assembly.Location;
        var host = Path.Combine(Path.GetDirectoryName(assembly)!, "Inventor.So.Mcp.Http.dll");
        Assert.True(File.Exists(host));
        Assert.True(ServerProcesses.IsHttpHost("C:\\app\\Inventor.So.Mcp.Http.exe", "ignored"));
        Assert.True(ServerProcesses.IsHttpHost("C:\\dotnet.exe", $"dotnet \"{host}\" --http-token secret"));
        Assert.True(ServerProcesses.IsHttpHost("C:\\dotnet.exe", $"dotnet exec \"{host}\""));
        Assert.False(ServerProcesses.IsHttpHost("C:\\Inventor.exe", $"Inventor \"{host}\""));
        Assert.False(ServerProcesses.IsHttpHost("C:\\dotnet.exe", $"dotnet unrelated.dll --argument \"{host}\""));
        Assert.False(ServerProcesses.IsHttpHost("C:\\dotnet.exe", "dotnet Inventor.So.Mcp.Http.dll"));
        Assert.DoesNotContain("secret", new ServerProcess(1, 1, "C:\\dotnet.exe", $"dotnet \"{host}\" --http-token secret").ToString());
    }

    [Fact]
    public void WindowsInventoryNeverIncludesTestRunnerOrInventor()
    {
        var candidates = ServerProcesses.Discover();
        Assert.DoesNotContain(candidates, p => p.Pid == Environment.ProcessId);
        Assert.All(candidates, p => Assert.True(ServerProcesses.IsHttpHost(p.Executable, p.CommandLine)));
    }

    [Fact]
    public async Task DisconnectRejectsUnrecognizedProcessWithoutKillingIt()
    {
        using var process = Process.Start(new ProcessStartInfo("C:\\Windows\\System32\\WindowsPowerShell\\v1.0\\powershell.exe")
        { UseShellExecute = false, CreateNoWindow = true, ArgumentList = { "-NoProfile", "-Command", "Start-Sleep -Seconds 30" } })!;
        try
        {
            var forged = new ServerProcess(process.Id, process.StartTime.ToUniversalTime().Ticks, "C:\\app\\Inventor.So.Mcp.Http.exe", "fake");
            await Assert.ThrowsAsync<InvalidOperationException>(() => ServerProcesses.DisconnectAsync(forged, CancellationToken.None));
            Assert.False(process.HasExited);
        }
        finally { if (!process.HasExited) { process.Kill(); await process.WaitForExitAsync(); } }
    }
}
