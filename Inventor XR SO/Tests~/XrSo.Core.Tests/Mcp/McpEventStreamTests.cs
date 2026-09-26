using InventorXrSo.Core.Mcp;
using InventorXrSo.Core.Tests.Support;

namespace InventorXrSo.Core.Tests.Mcp;

public class McpEventStreamTests : IClassFixture<BackendFixture>
{
    private readonly BackendFixture _f;
    public McpEventStreamTests(BackendFixture fixture) { _f = fixture; }

    [Fact]
    public async Task ADocumentChangeInInventorArrivesAsAResourceUpdate()
    {
        var client = new McpClient(_f.Transport(), _f.BaseUrl, _f.EditorToken);
        await client.InitializeAsync(CancellationToken.None);
        await client.SubscribeAsync("inventor://active-document", CancellationToken.None);

        var updated = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var stream = client.RunEventStreamAsync(uri => updated.TrySetResult(uri), cts.Token);
        await Task.Delay(500);   // let the GET stream open before the change
        _f.AddIn.RaiseDocumentChanged(geometry: true);

        var winner = await Task.WhenAny(updated.Task, Task.Delay(TimeSpan.FromSeconds(15)));
        Assert.Same(updated.Task, winner);
        Assert.Equal("inventor://active-document", await updated.Task);

        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => stream);
    }
}
