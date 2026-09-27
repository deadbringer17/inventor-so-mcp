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
            var tcs = new TaskCompletionSource<TransportResponse>();
            if (ct.IsCancellationRequested)
            {
                tcs.TrySetCanceled(ct);
                return tcs.Task;
            }
            int before = _trust.RejectionCount;
            var web = Create(request, new DownloadHandlerBuffer());
            var done = new int[1];
            var context = SynchronizationContext.Current;
            var registration = ct.Register(() => AbortSafely(web, context, done));
            web.SendWebRequest().completed += _ =>
            {
                Interlocked.Exchange(ref done[0], 1);
                registration.Dispose();
                try
                {
                    if (ct.IsCancellationRequested) tcs.TrySetCanceled(ct);
                    else if (IsFailure(web)) tcs.TrySetException(Failure(web, before));
                    else tcs.TrySetResult(new TransportResponse((int)web.responseCode, Headers(web), web.downloadHandler.data));
                }
                finally { web.Dispose(); }
            };
            return tcs.Task;
        }

        public Task<int> StreamLinesAsync(TransportRequest request, Action<string> onLine, CancellationToken ct)
        {
            var tcs = new TaskCompletionSource<int>();
            if (ct.IsCancellationRequested)
            {
                tcs.TrySetCanceled(ct);
                return tcs.Task;
            }
            int before = _trust.RejectionCount;
            var web = Create(request, new LineDownloadHandler(onLine));
            var done = new int[1];
            var context = SynchronizationContext.Current;
            var registration = ct.Register(() => AbortSafely(web, context, done));
            web.SendWebRequest().completed += _ =>
            {
                Interlocked.Exchange(ref done[0], 1);
                registration.Dispose();
                try
                {
                    if (ct.IsCancellationRequested) tcs.TrySetCanceled(ct);
                    else if (IsFailure(web)) tcs.TrySetException(Failure(web, before));
                    else tcs.TrySetResult((int)web.responseCode);
                }
                finally { web.Dispose(); }
            };
            return tcs.Task;
        }

        /// <summary>
        /// Cancellation can run this on whatever thread called <c>Cancel()</c> (or synchronously on the
        /// registering thread if the token is already cancelled). <see cref="UnityWebRequest.Abort"/>
        /// must run on the thread that owns it, so when the current thread isn't the one the request was
        /// created on, hop back via the captured <see cref="SynchronizationContext"/>. <paramref
        /// name="done"/>[0] — set to 1 by the completion handler before it disposes the request — is
        /// checked both before posting and again once the posted callback actually runs, so a late abort
        /// never touches a disposed <see cref="UnityWebRequest"/>. A one-element array (rather than a
        /// <c>ref</c> parameter) carries the flag so it can be captured by the <see
        /// cref="SynchronizationContext.Post"/> delegate.
        /// </summary>
        private static void AbortSafely(UnityWebRequest web, SynchronizationContext context, int[] done)
        {
            void AbortIfNotDone()
            {
                if (Volatile.Read(ref done[0]) == 0) web.Abort();
            }
            if (context != null && SynchronizationContext.Current != context) context.Post(_ => AbortIfNotDone(), null);
            else AbortIfNotDone();
        }

        private static bool IsFailure(UnityWebRequest web) =>
            web.result == UnityWebRequest.Result.ConnectionError || web.result == UnityWebRequest.Result.DataProcessingError;

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
