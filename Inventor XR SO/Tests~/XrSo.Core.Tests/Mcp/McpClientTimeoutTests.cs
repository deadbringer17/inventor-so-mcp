using InventorXrSo.Core.Mcp;
using InventorXrSo.Core.Net;
using Newtonsoft.Json.Linq;

namespace InventorXrSo.Core.Tests.Mcp;

public class McpClientTimeoutTests
{
    private sealed class RecordingTransport : IHttpTransport
    {
        public readonly List<TimeSpan> Timeouts = new();
        public Exception? Throw;

        public Task<TransportResponse> SendAsync(TransportRequest request, CancellationToken ct)
        {
            Timeouts.Add(request.Timeout);
            if (Throw != null) return Task.FromException<TransportResponse>(Throw);
            var payload = new JObject { ["ok"] = true };
            var reply = new JObject
            {
                ["jsonrpc"] = "2.0", ["id"] = 1,
                ["result"] = new JObject { ["content"] = new JArray(new JObject { ["type"] = "text", ["text"] = payload.ToString() }) },
            };
            return Task.FromResult(new TransportResponse(200, new Dictionary<string, string> { ["Content-Type"] = "application/json" },
                System.Text.Encoding.UTF8.GetBytes(reply.ToString())));
        }

        public Task<int> StreamLinesAsync(TransportRequest request, Action<string> onLine, CancellationToken ct) => throw new NotSupportedException();
    }

    [Fact]
    public async Task ACallWithoutATimeoutKeepsTheTransportDefaultAndAPerCallTimeoutIsApplied()
    {
        var transport = new RecordingTransport();
        var client = new McpClient(transport, "https://pc", "token");
        await client.CallToolAsync("t", new JObject(), default);
        await client.CallToolAsync("t", new JObject(), default, TimeSpan.FromSeconds(75));
        Assert.Equal(new[] { TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(75) }, transport.Timeouts);
    }

    [Fact]
    public async Task ATransportTimeoutBecomesATimeoutMcpException()
    {
        var transport = new RecordingTransport { Throw = new TransportTimeoutException("Request timeout") };
        var client = new McpClient(transport, "https://pc", "token");
        var ex = await Assert.ThrowsAsync<McpException>(() => client.CallToolAsync("t", new JObject(), default, TimeSpan.FromSeconds(75)));
        Assert.Equal("TIMEOUT", ex.Code);
    }

    [Fact]
    public async Task ARequestCancelledByTheTransportItselfIsATimeoutButTheCallersCancellationIsNot()
    {
        var transport = new RecordingTransport { Throw = new TaskCanceledException("timed out") };
        var client = new McpClient(transport, "https://pc", "token");
        var ex = await Assert.ThrowsAsync<McpException>(() => client.CallToolAsync("t", new JObject(), default, TimeSpan.FromSeconds(75)));
        Assert.Equal("TIMEOUT", ex.Code);
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.CallToolAsync("t", new JObject(), cts.Token, TimeSpan.FromSeconds(75)));
    }

    [Fact]
    public async Task AnotherTransportFailureIsNotATimeout()
    {
        var transport = new RecordingTransport { Throw = new TransportException("unreachable") };
        var client = new McpClient(transport, "https://pc", "token");
        await Assert.ThrowsAsync<TransportException>(() => client.CallToolAsync("t", new JObject(), default, TimeSpan.FromSeconds(75)));
    }
}
