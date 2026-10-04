using Microsoft.Extensions.DependencyInjection;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Inventor.So.Mcp.Http.Pairing;

/// <summary>Optional parts of the remote host that the CLI and the test host decide on.</summary>
public sealed class HostOptions
{
    /// <summary>TLS certificate to serve; overrides loading --http-cert.</summary>
    public X509Certificate2? Certificate { get; init; }
    /// <summary>When set, POST /pair is mapped (anonymous, rate limited).</summary>
    public PairingEndpoint? Pairing { get; init; }
    /// <summary>Extra service registrations made before the defaults (tests and embedders: a speech engine, log providers).</summary>
    public Action<IServiceCollection>? ConfigureServices { get; init; }
}

/// <summary>
/// POST /pair: trades the one-time secret of the open <see cref="PairingStore"/> window for a new
/// permanent bearer token, registered live and appended to the token file.
/// </summary>
public sealed class PairingEndpoint
{
    public const int MaxBodyChars = 4096;
    private readonly PairingStore _store;
    private readonly TokenRegistry _tokens;
    private readonly string _tokenFile;

    public PairingEndpoint(PairingStore store, TokenRegistry tokens, string tokenFile)
    {
        _store = store;
        _tokens = tokens;
        _tokenFile = tokenFile;
    }

    /// <summary>Raised after a successful pairing with the client name and the device name it sent.</summary>
    public event Action<string, string?>? Paired;

    public async Task<IResult> HandleAsync(HttpContext http)
    {
        string body;
        using (var reader = new StreamReader(http.Request.Body, Encoding.UTF8))
        {
            var buffer = new char[MaxBodyChars + 1];
            int read = await reader.ReadBlockAsync(buffer, 0, buffer.Length);
            if (read > MaxBodyChars) return Error(StatusCodes.Status400BadRequest, PairingStore.Invalid, "Request too large.");
            body = new string(buffer, 0, read);
        }
        JObject? request = null;
        try { request = JObject.Parse(body); }
        catch (JsonReaderException) { }
        var secret = (request?["secret"] as JValue)?.Value as string;
        if (string.IsNullOrEmpty(secret))
            return Error(StatusCodes.Status400BadRequest, PairingStore.Invalid, "The body must be {\"secret\": \"...\", \"device_name\": \"...\"}.");

        var token = TokenRegistry.Generate();
        var outcome = _store.Redeem(secret, name => _tokens.AddPersisted(name, token,
            () => TokenFile.Append(_tokenFile, name, token)));
        if (!outcome.Ok) return Error(outcome.ErrorCode == "PAIRING_PERSIST_FAILED" ? 503 : 403, outcome.ErrorCode!, outcome.Message);
        var device = (request!["device_name"] as JValue)?.Value as string;
        // An observer failure cannot turn durable pairing into a failed HTTP response.
        if (Paired != null)
            foreach (Action<string, string?> observer in Paired.GetInvocationList())
                try { observer(outcome.ClientName!, device == null ? null : device[..Math.Min(device.Length, 80)]); }
                catch { }
        return Results.Json(new { ok = true, client_name = outcome.ClientName, token });
    }

    private static IResult Error(int status, string code, string message) =>
        Results.Json(new { ok = false, error = new { code, message } }, statusCode: status);
}
