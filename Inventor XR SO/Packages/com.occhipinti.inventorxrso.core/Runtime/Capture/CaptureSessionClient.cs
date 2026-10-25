using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using InventorXrSo.Core.Net;
using InventorXrSo.Core.Pairing;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace InventorXrSo.Core.Capture
{
    public sealed class CaptureTransferFile
    {
        public string Path { get; }
        public long Size { get; }
        public string Sha256 { get; }
        public CaptureTransferFile(string path,long size,string sha256) { Path = path; Size = size; Sha256 = sha256; }
    }

    public sealed class CaptureSessionClient
    {
        private readonly IHttpTransport _transport;
        private readonly string _baseUrl, _token;
        public CaptureSessionClient(IHttpTransport transport,PairedServer server)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _baseUrl = server.BaseUrl.TrimEnd('/') + "/capture/v1/sessions";
            if (!Uri.TryCreate(_baseUrl,UriKind.Absolute,out var uri) || uri.Scheme != "https") throw new ArgumentException("Capture requires HTTPS.");
            _token = server.Token;
        }

        public async Task UploadAsync(LocalScanStore store,string id,Action<int,int> progress,CancellationToken ct)
        {
            var files = await Task.Run(() => store.DescribeTransfer(id),ct);
            ct.ThrowIfCancellationRequested();
            var manifest = new { ScanId = id, Files = files };
            var result = await SendAsync(TransportRequest.Json("POST",_baseUrl,JsonConvert.SerializeObject(manifest)),ct);
            if (result.Value<bool>("finalized")) { progress?.Invoke(files.Count,files.Count); return; }
            var received = new HashSet<int>((result["received"] as JArray ?? new JArray()).Values<int>());
            if (received.Any(i => i < 0 || i >= files.Count)) throw new InvalidDataException("Invalid upload status.");
            int count = received.Count;
            progress?.Invoke(count,files.Count);
            for (int i = 0; i < files.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                if (received.Contains(i)) continue;
                byte[] bytes = await Task.Run(() => store.ReadTransferFile(id,files[i]),ct);
                var request = new TransportRequest("PUT",_baseUrl + "/" + id + "/chunks/" + i) { Body = bytes, Timeout = TimeSpan.FromSeconds(60) };
                request.Headers["Content-Type"] = "application/octet-stream";
                request.Headers["X-Content-SHA256"] = files[i].Sha256;
                await SendAsync(request,ct);
                count++; progress?.Invoke(count,files.Count);
            }
            await SendAsync(TransportRequest.Json("POST",_baseUrl + "/" + id + "/finalize","{}"),ct);
        }

        private async Task<JObject> SendAsync(TransportRequest request,CancellationToken ct)
        {
            request.Headers["Authorization"] = "Bearer " + _token;
            request.MaxResponseBytes = 1024*1024;
            for (int attempt = 0; ; attempt++)
            {
                var response = await _transport.SendAsync(request,ct);
                if (response.Status == 429 && attempt < 3)
                {
                    int seconds = int.TryParse(response.Header("Retry-After"),NumberStyles.Integer,CultureInfo.InvariantCulture,out int retry)
                        ? Math.Max(1,Math.Min(120,retry)) : 60;
                    await Task.Delay(TimeSpan.FromSeconds(seconds),ct);
                    continue;
                }
                if (!response.IsSuccess) throw new IOException("Capture transfer failed (HTTP " + response.Status + ").");
                var result = JObject.Parse(response.Text);
                if (result.Value<bool?>("ok") != true) throw new IOException("Capture transfer rejected.");
                return result;
            }
        }
    }
}
