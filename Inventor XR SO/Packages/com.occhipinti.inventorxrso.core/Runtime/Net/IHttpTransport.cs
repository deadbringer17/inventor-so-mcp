using System;
using System.Threading;
using System.Threading.Tasks;

namespace InventorXrSo.Core.Net
{
    /// <summary>
    /// HTTPS as the core needs it. Unity implements it with UnityWebRequest, tests with HttpClient.
    /// Both validate the server certificate with a <see cref="ServerTrust"/>.
    /// </summary>
    public interface IHttpTransport
    {
        Task<TransportResponse> SendAsync(TransportRequest request, CancellationToken ct);

        /// <summary>
        /// Send the request and hand each line of the response body to <paramref name="onLine"/> as it
        /// arrives (without the trailing newline). Completes with the HTTP status when the server closes.
        /// </summary>
        Task<int> StreamLinesAsync(TransportRequest request, Action<string> onLine, CancellationToken ct);
    }
}
