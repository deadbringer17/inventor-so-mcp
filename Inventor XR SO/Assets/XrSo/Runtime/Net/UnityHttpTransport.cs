using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using InventorXrSo.Core.Net;
using UnityEngine.Networking;

namespace InventorXrSo.Unity.Net
{
    /// <summary>
    /// The core's HTTPS transport on UnityWebRequest (works on Quest/IL2CPP), pinned by a
    /// <see cref="ServerTrust"/>. Call from the main thread; completions come back on it.
    /// </summary>
    public sealed class UnityHttpTransport : IHttpTransport
    {
        private readonly ServerTrust _trust;

        public UnityHttpTransport(ServerTrust trust) { _trust = trust; }

        public Task<TransportResponse> SendAsync(TransportRequest request, CancellationToken ct)
        {
            int before = _trust.RejectionCount;
            var web = Create(request, new DownloadHandlerBuffer());
            var tcs = new TaskCompletionSource<TransportResponse>();
            var registration = ct.Register(() => web.Abort());
            web.SendWebRequest().completed += _ =>
            {
                registration.Dispose();
                try
                {
                    if (ct.IsCancellationRequested) tcs.TrySetCanceled(ct);
                    else if (web.result == UnityWebRequest.Result.ConnectionError) tcs.TrySetException(Failure(web, before));
                    else tcs.TrySetResult(new TransportResponse((int)web.responseCode, Headers(web), web.downloadHandler.data));
                }
                finally { web.Dispose(); }
            };
            return tcs.Task;
        }

        public Task<int> StreamLinesAsync(TransportRequest request, Action<string> onLine, CancellationToken ct)
        {
            int before = _trust.RejectionCount;
            var web = Create(request, new LineDownloadHandler(onLine));
            var tcs = new TaskCompletionSource<int>();
            var registration = ct.Register(() => web.Abort());
            web.SendWebRequest().completed += _ =>
            {
                registration.Dispose();
                try
                {
                    if (ct.IsCancellationRequested) tcs.TrySetCanceled(ct);
                    else if (web.result == UnityWebRequest.Result.ConnectionError) tcs.TrySetException(Failure(web, before));
                    else tcs.TrySetResult((int)web.responseCode);
                }
                finally { web.Dispose(); }
            };
            return tcs.Task;
        }

        private UnityWebRequest Create(TransportRequest request, DownloadHandler download)
        {
            var web = new UnityWebRequest(request.Url, request.Method)
            {
                downloadHandler = download,
                certificateHandler = new PinningCertificateHandler(_trust),
                disposeCertificateHandlerOnDispose = true,
                disposeDownloadHandlerOnDispose = true,
                disposeUploadHandlerOnDispose = true,
                timeout = request.Timeout == Timeout.InfiniteTimeSpan ? 0 : Math.Max(1, (int)Math.Ceiling(request.Timeout.TotalSeconds)),
            };
            if (request.Body != null) web.uploadHandler = new UploadHandlerRaw(request.Body);
            foreach (var header in request.Headers) web.SetRequestHeader(header.Key, header.Value);
            return web;
        }

        private Exception Failure(UnityWebRequest web, int before) =>
            _trust.RejectionCount != before
                ? new CertificateRejectedException(_trust.LastPresentedSha256)
                : (Exception)new TransportException(web.error);

        private static IDictionary<string, string> Headers(UnityWebRequest web) =>
            web.GetResponseHeaders() ?? new Dictionary<string, string>();
    }
}
