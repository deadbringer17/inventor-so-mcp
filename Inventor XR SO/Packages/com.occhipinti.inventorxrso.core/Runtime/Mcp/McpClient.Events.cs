using System;
using System.Threading;
using System.Threading.Tasks;
using InventorXrSo.Core.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace InventorXrSo.Core.Mcp
{
    public sealed partial class McpClient
    {
        public const string ResourceUpdated = "notifications/resources/updated";

        /// <summary>
        /// Listen on the session's GET stream until the server closes it. Resource updates are reported
        /// by URI; other server messages are ignored (M1 needs none).
        /// </summary>
        public async Task RunEventStreamAsync(Action<string> onResourceUpdated, CancellationToken ct)
        {
            var request = NewRequest("GET", "text/event-stream");
            request.Timeout = Timeout.InfiniteTimeSpan;
            var parser = new SseParser(data =>
            {
                JObject message;
                try { message = JObject.Parse(data); }
                catch (JsonReaderException) { return; }
                if ((string)message["method"] == ResourceUpdated)
                {
                    var uri = (string)message["params"]?["uri"];
                    if (uri != null) onResourceUpdated(uri);
                }
            });
            int status = await _transport.StreamLinesAsync(request, parser.Feed, ct);
            parser.Flush();
            if (status == 405) throw new McpException("EVENT_STREAM_UNSUPPORTED", "The server does not offer an event stream.");
            ThrowForStatus(status, null);
        }
    }
}
