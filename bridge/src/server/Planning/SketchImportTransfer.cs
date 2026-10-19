using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Bimwright.Ipt.Shared.Contracts;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Server.Planning;

/// <summary>Internal bounded transfer adapter. Staging does not enable native image import.</summary>
internal static class SketchImportTransfer
{
    internal const int ChunkBytes = 192 * 1024;

    internal static async Task<Batch> StageAsync(PluginClient client, SketchImportAttachments attachments,
        string owner, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(attachments);
        if (string.IsNullOrWhiteSpace(owner)) throw new ArgumentException("An authenticated import owner is required.", nameof(owner));
        ct.ThrowIfCancellationRequested();
        var batch = new Batch(client, attachments.PinTarget(client), owner);
        try
        {
            foreach (var resource in attachments.Resources)
            {
                ct.ThrowIfCancellationRequested();
                var begun = await batch.Stage("begin", new JObject { ["byte_length"] = resource.Size }, ct).ConfigureAwait(false);
                string id = TransferId(begun);
                // Journal immediately: later response validation and source reads may fail.
                batch.Ids.Add(id);
                byte[]? bytes = null;
                try
                {
                    bytes = attachments.Read(resource.Id);
                    if (bytes.LongLength != resource.Size) throw Protocol();
                    for (int offset = 0; offset < bytes.Length;)
                    {
                        ct.ThrowIfCancellationRequested();
                        int length = Math.Min(ChunkBytes, bytes.Length - offset);
                        var reply = await batch.Stage("chunk", new JObject
                        {
                            ["transfer_id"] = id, ["offset"] = offset,
                            ["data_base64"] = Convert.ToBase64String(bytes, offset, length),
                        }, ct).ConfigureAwait(false);
                        offset += length;
                        if (TransferId(reply) != id || !IntegerEquals(reply["received_bytes"], offset)) throw Protocol();
                    }
                    var sealedReply = await batch.Stage("seal", new JObject { ["transfer_id"] = id, ["sha256"] = resource.Sha256 }, ct).ConfigureAwait(false);
                    if (TransferId(sealedReply) != id || sealedReply["sealed"]?.Type != JTokenType.Boolean || !(bool)sealedReply["sealed"]!) throw Protocol();
                }
                finally { if (bytes != null) CryptographicOperations.ZeroMemory(bytes); }
                batch.Resources.Add(new JObject
                {
                    ["asset_id"] = resource.Id, ["transfer_id"] = id, ["sha256"] = resource.Sha256,
                    ["byte_length"] = resource.Size, ["format"] = resource.Format, ["metadata"] = resource.Metadata,
                });
            }
            ct.ThrowIfCancellationRequested();
            return batch;
        }
        catch
        {
            await batch.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static string TransferId(JObject reply)
    {
        if (reply["transfer_id"]?.Type != JTokenType.String) throw Protocol();
        string id = (string)reply["transfer_id"]!;
        if (id.Length is < 1 or > 128 || id.Any(c => !(c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_' or '-'))) throw Protocol();
        return id;
    }
    private static bool IntegerEquals(JToken? token, long expected) => token?.Type == JTokenType.Integer
        && long.TryParse(token.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long actual) && actual == expected;
    private static InventorGatewayException Protocol() => new(InventorErrorCodes.API_ERROR, "Invalid image staging response.");

    internal sealed class Batch : IAsyncDisposable
    {
        private readonly PluginClient _client;
        private readonly PluginClient.PinnedTarget _target;
        private readonly string _owner, _session = Guid.NewGuid().ToString("N");
        private readonly SemaphoreSlim _sendGate = new(1, 1);
        private int _disposed;
        internal readonly List<string> Ids = new();
        internal readonly JArray Resources = new();
        internal Batch(PluginClient client, PluginClient.PinnedTarget target, string owner)
        { _client = client; _target = target; _owner = owner; }

        internal async Task<JObject> Stage(string action, JObject arguments, CancellationToken ct)
        {
            arguments["action"] = action; arguments["owner"] = _owner; arguments["session"] = _session;
            return await _client.SendPinnedAsync(_target, "stage_sketch_import", arguments, ct).ConfigureAwait(false) as JObject ?? throw Protocol();
        }

        internal void ApplyTo(JObject request)
        {
            ArgumentNullException.ThrowIfNull(request);
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            request["import_attachments"] = new JObject
            {
                ["owner"] = _owner, ["session"] = _session, ["resources"] = Resources.DeepClone(),
            };
        }

        internal async Task<JToken> SendBatchAsync(JObject request, CancellationToken ct = default)
        {
            await _sendGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                ApplyTo(request);
                return await _client.SendPinnedAsync(_target, "atomic_batch", request, ct).ConfigureAwait(false);
            }
            finally { _sendGate.Release(); }
        }

        public async ValueTask DisposeAsync()
        {
            await _sendGate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
                // Independent from cancellation of the primary request, bounded for the entire journal.
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                for (int i = Ids.Count - 1; i >= 0; i--)
                {
                    try { await Stage("discard", new JObject { ["transfer_id"] = Ids[i] }, cleanup.Token).ConfigureAwait(false); }
                    catch (Exception) { /* TTL remains the fallback; never replace the primary outcome. */ }
                }
                Ids.Clear(); Resources.Clear();
            }
            finally { _sendGate.Release(); }
        }
    }
}
