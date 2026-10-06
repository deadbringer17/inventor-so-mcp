using System.Text;
using InventorXrSo.Core.Mcp;
using InventorXrSo.Core.Net;
using Newtonsoft.Json.Linq;

namespace InventorXrSo.Core.Tests.Mcp;

public class McpClientReplyTests
{
    private sealed class ScriptedTransport : IHttpTransport
    {
        private readonly string _contentType, _body;
        public ScriptedTransport(string contentType, string body) { _contentType = contentType; _body = body; }
        public Task<TransportResponse> SendAsync(TransportRequest request, CancellationToken ct) =>
            Task.FromResult(new TransportResponse(200, new Dictionary<string, string> { ["Content-Type"] = _contentType }, Encoding.UTF8.GetBytes(_body)));
        public Task<int> StreamLinesAsync(TransportRequest request, Action<string> onLine, CancellationToken ct) => throw new NotSupportedException();
    }

    private static string ToolReply(JToken content) =>
        new JObject { ["jsonrpc"] = "2.0", ["id"] = 1, ["result"] = new JObject { ["content"] = content } }.ToString(Newtonsoft.Json.Formatting.None);

    private static Task<JObject> Call(string contentType, string body) =>
        new McpClient(new ScriptedTransport(contentType, body), "https://pc", "token").CallToolAsync("t", new JObject(), default);

    [Theory]
    [InlineData("[]")]
    [InlineData("{\"type\":\"text\",\"text\":\"{}\"}")]
    [InlineData("[{\"type\":\"image\",\"data\":\"x\"}]")]
    [InlineData("null")]
    public async Task ContentWithoutTextIsToolNoContent(string content)
    {
        var ex = await Assert.ThrowsAsync<McpToolException>(() => Call("application/json", ToolReply(JToken.Parse(content))));
        Assert.Equal("TOOL_NO_CONTENT", ex.Code);
    }

    [Fact]
    public async Task TheFirstTextItemWinsEvenAfterOtherContent()
    {
        var content = JToken.Parse("[{\"type\":\"image\",\"data\":\"x\"},{\"type\":\"text\",\"text\":\"{\\\"ok\\\":true,\\\"n\\\":7}\"}]");
        Assert.Equal(7, (int)(await Call("application/json", ToolReply(content)))["n"]);
    }

    [Fact]
    public async Task AnItemWithTextButNoTypeIsTheFallback()
    {
        var content = JToken.Parse("[{\"text\":\"{\\\"ok\\\":true,\\\"n\\\":3}\"}]");
        Assert.Equal(3, (int)(await Call("application/json", ToolReply(content)))["n"]);
    }

    [Fact]
    public async Task SseSkipsPingsServerRequestsAndForeignIdsUntilTheRealReply()
    {
        var real = ToolReply(JToken.Parse("[{\"type\":\"text\",\"text\":\"{\\\"ok\\\":true,\\\"n\\\":9}\"}]"));
        var body = "data: ping\n\n" +
                   "data: [1,2]\n\n" +
                   "data: {\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"ping\"}\n\n" +
                   "data: {\"jsonrpc\":\"2.0\",\"id\":\"abc\",\"result\":{}}\n\n" +
                   "data: {\"jsonrpc\":\"2.0\",\"method\":\"notifications/x\"}\n\n" +
                   "data: " + real + "\n\n";
        Assert.Equal(9, (int)(await Call("text/event-stream", body))["n"]);
    }

    [Fact]
    public async Task SseWithoutARealReplyIsNoReply()
    {
        var ex = await Assert.ThrowsAsync<McpException>(() => Call("text/event-stream", "data: ping\n\ndata: {\"id\":1,\"method\":\"ping\"}\n\n"));
        Assert.Equal("RPC_NO_REPLY", ex.Code);
    }
}
