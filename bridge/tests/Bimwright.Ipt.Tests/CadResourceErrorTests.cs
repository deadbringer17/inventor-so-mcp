using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Bimwright.Ipt.Server;
using Bimwright.Ipt.Server.Resources;
using Xunit;

namespace Bimwright.Ipt.Tests;

public sealed class CadResourceErrorTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "so-resource-errors-" + Guid.NewGuid().ToString("N"));

    public CadResourceErrorTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    [Fact]
    public async Task FailedReadReachesTheClientWithItsCode()
    {
        // No descriptor: no Inventor to read from. The SDK replaces any other exception type with a
        // bare "An error occurred.", so the resource must raise an McpException carrying the code.
        var resources = new CadResources(new PluginClient(new InventorMcpConfig { DescriptorDirectory = _dir }));
        var error = await Assert.ThrowsAsync<ModelContextProtocol.McpException>(() => resources.ActiveDocument(CancellationToken.None));
        Assert.StartsWith("NO_TARGET", error.Message);
    }
}
