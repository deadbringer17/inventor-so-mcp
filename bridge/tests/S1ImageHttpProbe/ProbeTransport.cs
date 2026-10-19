using InventorXrSo.Core.Net;

/// <summary>Bounded real HTTPS adapter, pinned before requests. Logs no bodies, tokens or headers.</summary>
internal sealed class ProbeTransport : IHttpTransport, IDisposable
{
    private readonly HttpClient _http;
    internal int AssetGets { get; private set; }
    internal string LastAssetUrl { get; private set; } = "";
    internal ProbeTransport(ServerTrust trust)
    {
        _http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false,
            ServerCertificateCustomValidationCallback = (_, certificate, _, _) => certificate != null && trust.Validate(certificate.RawData) })
            { Timeout = Timeout.InfiniteTimeSpan };
    }
    public async Task<TransportResponse> SendAsync(TransportRequest request, CancellationToken ct)
    {
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (request.Timeout != Timeout.InfiniteTimeSpan) cancel.CancelAfter(request.Timeout);
        using var message = Message(request);
        using var response = await _http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancel.Token);
        long max = request.MaxResponseBytes > 0 ? request.MaxResponseBytes : 4 * 1024 * 1024;
        if (response.Content.Headers.ContentLength > max) throw new InvalidOperationException("HTTPS response exceeds requested bound.");
        using var stream = await response.Content.ReadAsStreamAsync(cancel.Token); using var output = new MemoryStream();
        byte[] buffer = new byte[8192];
        try
        {
            int count;
            while ((count = await stream.ReadAsync(buffer, cancel.Token)) != 0)
            { if (output.Length + count > max) throw new InvalidOperationException("HTTPS response exceeds requested bound."); output.Write(buffer, 0, count); }
            if (request.Method == "GET" && new Uri(request.Url).AbsolutePath.StartsWith("/assets/", StringComparison.Ordinal))
            { AssetGets++; LastAssetUrl = request.Url; }
            return new TransportResponse((int)response.StatusCode, Headers(response), output.ToArray());
        }
        finally { Array.Clear(buffer); if (output.TryGetBuffer(out var data)) Array.Clear(data.Array!, data.Offset, data.Count); }
    }
    public async Task<int> StreamLinesAsync(TransportRequest request, Action<string> onLine, CancellationToken ct)
    {
        using var message = Message(request); using var response = await _http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, ct);
        using var stream = await response.Content.ReadAsStreamAsync(ct); using var reader = new StreamReader(stream);
        string? line; while ((line = await reader.ReadLineAsync(ct)) != null) onLine(line);
        return (int)response.StatusCode;
    }
    private static HttpRequestMessage Message(TransportRequest request)
    {
        var message = new HttpRequestMessage(new HttpMethod(request.Method), request.Url);
        if (request.Body != null) { message.Content = new ByteArrayContent(request.Body);
            if (request.Headers.TryGetValue("Content-Type", out var contentType)) message.Content.Headers.TryAddWithoutValidation("Content-Type", contentType); }
        foreach (var header in request.Headers) if (!header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)) message.Headers.TryAddWithoutValidation(header.Key, header.Value);
        return message;
    }
    private static Dictionary<string, string> Headers(HttpResponseMessage response)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in response.Headers) headers[header.Key] = string.Join(", ", header.Value);
        foreach (var header in response.Content.Headers) headers[header.Key] = string.Join(", ", header.Value);
        return headers;
    }
    public void Dispose() => _http.Dispose();
}
