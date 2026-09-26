using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bimwright.Ipt.Server;
using Bimwright.Ipt.Server.Events;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Xunit;

#pragma warning disable MCPEXP002 // subclassing McpServer is the only way to reproduce the SDK's per-request wrappers

namespace Bimwright.Ipt.Tests;

public class EventSubscriptionTests
{
    /// <summary>Stands in for the per-request wrapper the SDK hands each handler: a new instance every time.</summary>
    private sealed class RequestServer : McpServer
    {
        private readonly string? _session;
        public RequestServer(string? session) => _session = session;
        public override string? SessionId => _session;
        public override ClientCapabilities? ClientCapabilities => null;
        public override Implementation? ClientInfo => null;
        public override McpServerOptions ServerOptions => new();
        public override IServiceProvider? Services => null;
        public override LoggingLevel? LoggingLevel => null;
        public override string? NegotiatedProtocolVersion => null;
        public override Task RunAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public override Task<JsonRpcResponse> SendRequestAsync(JsonRpcRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task SendMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public override IAsyncDisposable RegisterNotificationHandler(string method, Func<JsonRpcNotification, CancellationToken, ValueTask> handler) => throw new NotSupportedException();
        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static EventSubscriptionService Service() => new(new PluginClient(new InventorMcpConfig { DescriptorDirectory = System.IO.Path.GetTempPath() }));

    [Theory]
    [InlineData(null)]        // stdio: one session, no id
    [InlineData("session-a")]  // HTTP
    public void UnsubscribeFromAnotherRequestOfTheSameSessionRemovesTheSubscription(string? session)
    {
        var service = Service();
        service.Subscribe(new RequestServer(session), "inventor://events");
        Assert.Equal(1, service.SubscriberCount);
        service.Unsubscribe(new RequestServer(session), "inventor://events");
        Assert.Equal(0, service.SubscriberCount);
    }

    [Fact]
    public void SessionsAreIndependent()
    {
        var service = Service();
        service.Subscribe(new RequestServer("a"), "inventor://events");
        service.Subscribe(new RequestServer("b"), "inventor://events");
        service.Unsubscribe(new RequestServer("a"), "inventor://events");
        Assert.Equal(1, service.SubscriberCount);
    }
}
