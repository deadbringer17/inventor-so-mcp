using System;
using System.IO;
using Bimwright.Ipt.Server;
using Bimwright.Ipt.Server.Tools;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Tests;

public sealed class MetaToolsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "inv-meta-" + Guid.NewGuid().ToString("N"));

    public MetaToolsTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private void WriteDescriptor(string file, int year, int pid, DateTimeOffset heartbeat, string token, string transport = "pipe")
    {
        File.WriteAllText(Path.Combine(_dir, file), $$"""
        {
          "target_id": "inventor-{{year}}-{{pid}}",
          "inventor_year": {{year}},
          "process_id": {{pid}},
          "host_app": "Inventor",
          "transport": "{{transport}}",
          "port": 49500,
          "pipe_name": "BimwrightInventor-{{pid}}",
          "auth_token": "{{token}}",
          "document_title": "sample.ipt",
          "document_path": "C:\\secret\\sample.ipt",
          "last_heartbeat_utc": "{{heartbeat.UtcDateTime:O}}"
        }
        """);
    }

    private MetaTools Tools()
    {
        var client = new PluginClient(new InventorMcpConfig { DescriptorDirectory = _dir });
        return new MetaTools(client);
    }

    [Fact]
    public void TargetMetaToolsDoNotExposeAuthToken()
    {
        var token = "abcdefghijklmnopqrstuvwxyz012345";
        WriteDescriptor("live.json", 2025, Environment.ProcessId, DateTimeOffset.UtcNow, token);

        var tools = Tools();
        var listJson = tools.ListAvailableTargets();
        var currentJson = tools.GetCurrentTarget();

        Assert.DoesNotContain("auth_token", listJson);
        Assert.DoesNotContain(token, listJson);
        Assert.DoesNotContain("auth_token", currentJson);
        Assert.DoesNotContain(token, currentJson);
    }

    [Fact]
    public void SwitchTargetAcceptsExactIdYearProcessIdAndPipeName()
    {
        var pid = Environment.ProcessId;
        WriteDescriptor("old.json", 2024, pid, DateTimeOffset.UtcNow.AddSeconds(-10), "tok-old", transport: "tcp");
        WriteDescriptor("new.json", 2025, pid, DateTimeOffset.UtcNow, "tok-new");

        var client = new PluginClient(new InventorMcpConfig { DescriptorDirectory = _dir });

        Assert.True(client.SwitchTarget("inventor-2025-" + pid));
        Assert.Equal(2025, client.CurrentTarget!.InventorYear);

        Assert.True(client.SwitchTarget("2024"));
        Assert.Equal(2024, client.CurrentTarget!.InventorYear);

        // The fixtures share PID and pipe name: ambiguous aliases must fail.
        Assert.False(client.SwitchTarget(pid.ToString()));
        Assert.False(client.SwitchTarget("BimwrightInventor-" + pid));
        Assert.Equal(2024, client.CurrentTarget!.InventorYear);
    }

    [Fact]
    public void MissingConfiguredTargetDoesNotFallback()
    {
        WriteDescriptor("live.json", 2025, Environment.ProcessId, DateTimeOffset.UtcNow, "token");
        var client = new PluginClient(new InventorMcpConfig { DescriptorDirectory = _dir, TargetId = "2027" });
        Assert.Null(client.CurrentTarget);
    }

    [Fact]
    public void SelectedTargetDisappearanceDoesNotRedirect()
    {
        WriteDescriptor("selected.json", 2027, Environment.ProcessId, DateTimeOffset.UtcNow, "token");
        var client = new PluginClient(new InventorMcpConfig { DescriptorDirectory = _dir, TargetId = "2027" });
        Assert.Equal(2027, client.CurrentTarget!.InventorYear);
        File.Delete(Path.Combine(_dir, "selected.json"));
        WriteDescriptor("other.json", 2025, Environment.ProcessId, DateTimeOffset.UtcNow, "other-token");
        Assert.Null(client.CurrentTarget);
        Assert.True(client.SwitchTarget("2025"));
        Assert.Equal(2025, client.CurrentTarget!.InventorYear);
    }

    [Fact]
    public void SelectedTargetRefreshesDescriptor()
    {
        WriteDescriptor("live.json", 2027, Environment.ProcessId, DateTimeOffset.UtcNow, "old-token");
        var client = new PluginClient(new InventorMcpConfig { DescriptorDirectory = _dir });
        Assert.Equal("old-token", client.CurrentTarget!.AuthToken);
        WriteDescriptor("live.json", 2027, Environment.ProcessId, DateTimeOffset.UtcNow, "new-token");
        Assert.Equal("new-token", client.CurrentTarget!.AuthToken);
    }

    [Fact]
    public void MultipleTargetsRequireSelection()
    {
        WriteDescriptor("one.json", 2025, Environment.ProcessId, DateTimeOffset.UtcNow, "a");
        WriteDescriptor("two.json", 2027, Environment.ProcessId, DateTimeOffset.UtcNow, "b");
        var client = new PluginClient(new InventorMcpConfig { DescriptorDirectory = _dir });
        Assert.Null(client.CurrentTarget);
    }
}
