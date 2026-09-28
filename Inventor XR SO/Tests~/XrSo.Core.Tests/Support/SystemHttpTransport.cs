using System.Net.Http;
using InventorXrSo.Core.Net;

namespace InventorXrSo.Core.Tests.Support;

/// <summary>HttpClient implementation of the core transport, for tests only (Unity uses UnityWebRequest).</summary>
public sealed class SystemHttpTransport : IHttpTransport, IDisposable
{
    private readonly ServerTrust _trust;
    private readonly HttpClient _http;

    public SystemHttpTransport(ServerTrust trust)
    {
        _trust = trust;
        var handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, cert, _, _) => cert != null && trust.Validate(cert.RawData) };
        _http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
    }

    public async Task<TransportResponse> SendAsync(TransportRequest request, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (request.Timeout != Timeout.InfiniteTimeSpan) timeout.CancelAfter(request.Timeout);
        int before = _trust.RejectionCount;
        try
        {
            using var message = ToMessage(request);
            using var response = await _http.SendAsync(message, timeout.Token);
            var body = await response.Content.ReadAsByteArrayAsync(timeout.Token);
            return new TransportResponse((int)response.StatusCode, Headers(response), body);
        }
        catch (HttpRequestException ex) { throw Wrap(ex, before); }
    }

    public async Task<int> StreamLinesAsync(TransportRequest request, Action<string> onLine, CancellationToken ct)
    {
        int before = _trust.RejectionCount;
        try
        {
            using var message = ToMessage(request);
            using var response = await _http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, ct);
            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream);
            string line;
            while ((line = await reader.ReadLineAsync(ct)) != null) onLine(line);
            return (int)response.StatusCode;
        }
        catch (HttpRequestException ex) { throw Wrap(ex, before); }
        catch (IOException ex) when (!ct.IsCancellationRequested) { throw new TransportException(ex.Message, ex); }
    }

    public void Dispose() => _http.Dispose();

    /// <summary>
    /// A pin rejection is only this request's if <see cref="ServerTrust.RejectionCount"/> moved during
    /// it; a stale or concurrent rejection on a long-lived trust must not be misreported as this
    /// request's certificate having changed.
    /// </summary>
    private Exception Wrap(HttpRequestException ex, int rejectionsBefore) =>
        _trust.RejectionCount != rejectionsBefore ? new CertificateRejectedException(_trust.LastPresentedSha256) : new TransportException(ex.Message, ex);

    private static HttpRequestMessage ToMessage(TransportRequest request)
    {
        var message = new HttpRequestMessage(new HttpMethod(request.Method), request.Url);
        if (request.Body != null)
        {
            message.Content = new ByteArrayContent(request.Body);
            if (request.Headers.TryGetValue("Content-Type", out var type)) message.Content.Headers.TryAddWithoutValidation("Content-Type", type);
        }
        foreach (var header in request.Headers)
            if (!header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                message.Headers.TryAddWithoutValidation(header.Key, header.Value);
        return message;
    }

    private static Dictionary<string, string> Headers(HttpResponseMessage response)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var h in response.Headers) headers[h.Key] = string.Join(", ", h.Value);
        foreach (var h in response.Content.Headers) headers[h.Key] = string.Join(", ", h.Value);
        return headers;
    }
}
