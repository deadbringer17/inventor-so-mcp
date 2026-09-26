using System;
using System.Threading;
using System.Threading.Tasks;
using InventorXrSo.Core.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace InventorXrSo.Core.Mcp
{
    /// <summary>
    /// Minimal MCP client over Streamable HTTP: JSON-RPC requests on POST /mcp (answered as JSON or as
    /// an SSE body), tool calls whose text content is Inventor SO's JSON payload, resource subscriptions
    /// and the GET event stream (Task B4).
    /// </summary>
    public sealed partial class McpClient
    {
        public const string ProtocolVersion = "2025-06-18";
        public const string ClientName = "inventor-xr-so";
        public const string ClientVersion = "0.1.0";

        private readonly IHttpTransport _transport;
        private readonly string _endpoint;
        private readonly string _token;
        private int _nextId;
        private string _sessionId;

        public McpClient(IHttpTransport transport, string baseUrl, string token)
        {
            _transport = transport;
            _endpoint = baseUrl.TrimEnd('/') + "/mcp";
            _token = token;
        }

        public string SessionId => _sessionId;

        public async Task InitializeAsync(CancellationToken ct)
        {
            _sessionId = null;
            await RequestAsync("initialize", new JObject
            {
                ["protocolVersion"] = ProtocolVersion,
                ["capabilities"] = new JObject(),
                ["clientInfo"] = new JObject { ["name"] = ClientName, ["version"] = ClientVersion },
            }, ct);
            var notification = new JObject { ["jsonrpc"] = "2.0", ["method"] = "notifications/initialized" };
            await PostAsync(notification, ct);
        }

        public async Task<JObject> CallToolAsync(string name, JObject arguments, CancellationToken ct)
        {
            var result = await RequestAsync("tools/call", new JObject { ["name"] = name, ["arguments"] = arguments ?? new JObject() }, ct);
            var text = (string)result["content"]?[0]?["text"];
            bool isError = (bool?)result["isError"] ?? false;
            if (text == null) throw new McpToolException(name, "TOOL_NO_CONTENT", name + " returned no text content.", null);
            JObject payload;
            try { payload = JObject.Parse(text); }
            catch (JsonReaderException) { throw new McpToolException(name, "TOOL_ERROR", text, null); }
            if (payload["error"] is JObject error && ((bool?)payload["ok"] ?? false) == false)
                throw new McpToolException(name, (string)error["code"] ?? "TOOL_ERROR", (string)error["message"] ?? "", error);
            if (isError) throw new McpToolException(name, "TOOL_ERROR", text, payload);
            return payload;
        }

        public Task SubscribeAsync(string uri, CancellationToken ct) =>
            RequestAsync("resources/subscribe", new JObject { ["uri"] = uri }, ct);

        public async Task<JObject> RequestAsync(string method, JObject parameters, CancellationToken ct)
        {
            int id = Interlocked.Increment(ref _nextId);
            var message = new JObject { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method };
            if (parameters != null) message["params"] = parameters;
            var reply = ReadReply(await PostAsync(message, ct), id);
            if (reply["error"] is JObject rpcError)
                throw new McpException("RPC_" + ((int?)rpcError["code"] ?? 0), (string)rpcError["message"] ?? method + " failed.");
            return reply["result"] as JObject ?? new JObject();
        }

        private TransportRequest NewRequest(string method, string accept)
        {
            var request = new TransportRequest(method, _endpoint);
            request.Headers["Accept"] = accept;
            request.Headers["Authorization"] = "Bearer " + _token;
            if (_sessionId != null)
            {
                request.Headers["Mcp-Session-Id"] = _sessionId;
                request.Headers["MCP-Protocol-Version"] = ProtocolVersion;
            }
            return request;
        }

        private async Task<TransportResponse> PostAsync(JObject message, CancellationToken ct)
        {
            var request = NewRequest("POST", "application/json, text/event-stream");
            request.Body = System.Text.Encoding.UTF8.GetBytes(message.ToString(Formatting.None));
            request.Headers["Content-Type"] = "application/json";
            var response = await _transport.SendAsync(request, ct);
            ThrowForStatus(response.Status, response.Text);
            var session = response.Header("Mcp-Session-Id");
            if (!string.IsNullOrEmpty(session)) _sessionId = session;
            return response;
        }

        private void ThrowForStatus(int status, string body)
        {
            if (status == 401) throw new McpUnauthorizedException();
            if (status == 404 && _sessionId != null) throw new McpSessionExpiredException();
            if (status == 429) throw new McpException("RATE_LIMITED", "The PC is rate limiting this headset.");
            if (status < 200 || status >= 300)
                throw new McpException("HTTP_" + status, body == null || body.Length <= 200 ? body : body.Substring(0, 200));
        }

        internal static JObject ReadReply(TransportResponse response, int id)
        {
            var type = response.Header("Content-Type") ?? "";
            if (type.StartsWith("text/event-stream", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var data in SseParser.DataPayloads(response.Text))
                {
                    var message = JObject.Parse(data);
                    if ((int?)message["id"] == id) return message;
                }
                throw new McpException("RPC_NO_REPLY", "The server closed the stream without answering request " + id + ".");
            }
            return JObject.Parse(response.Text);
        }
    }
}
