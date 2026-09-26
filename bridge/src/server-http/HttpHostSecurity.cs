using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using Bimwright.Ipt.Server;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Inventor.So.Mcp.Http;

/// <summary>
/// Bearer tokens the remote host accepts, each with a client name. The name is the identity used for
/// asset ownership, session binding and the audit log; the token itself is never logged or echoed.
/// </summary>
public sealed class TokenRegistry
{
    public const int MinimumTokenLength = 32;
    private static readonly Regex NamePattern = new("^[A-Za-z0-9_.-]{1,40}$", RegexOptions.CultureInvariant);
    private readonly List<(string name, byte[] hash)> _tokens = new();

    public int Count => _tokens.Count;
    public IEnumerable<string> Names => _tokens.Select(t => t.name);

    /// <summary>Tokens from the token file ("name:token" per line, # comments) and INVENTOR_SO_HTTP_TOKEN ("default").</summary>
    public static TokenRegistry Load(InventorMcpConfig config)
    {
        var registry = new TokenRegistry();
        if (!string.IsNullOrWhiteSpace(config.HttpTokenFile))
        {
            int lineNumber = 0;
            foreach (var raw in File.ReadAllLines(config.HttpTokenFile!))
            {
                lineNumber++;
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith('#')) continue;
                int colon = line.IndexOf(':');
                if (colon <= 0) throw new InvalidOperationException("Token file line " + lineNumber + " is not name:token.");
                registry.Add(line[..colon].Trim(), line[(colon + 1)..].Trim(), "token file line " + lineNumber);
            }
        }
        if (!string.IsNullOrWhiteSpace(config.HttpToken)) registry.Add("default", config.HttpToken!, "INVENTOR_SO_HTTP_TOKEN");
        return registry;
    }

    public void Add(string name, string token, string source = "token")
    {
        if (!NamePattern.IsMatch(name)) throw new InvalidOperationException(source + ": client name must be 1-40 letters, digits, '.', '_' or '-'.");
        if (token.Length < MinimumTokenLength) throw new InvalidOperationException(source + ": tokens must be at least " + MinimumTokenLength + " characters (use --generate-token).");
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        if (_tokens.Any(t => t.name == name)) throw new InvalidOperationException(source + ": duplicate client name '" + name + "'.");
        if (_tokens.Any(t => CryptographicOperations.FixedTimeEquals(t.hash, hash))) throw new InvalidOperationException(source + ": duplicate token.");
        _tokens.Add((name, hash));
    }

    /// <summary>
    /// The client name for a presented token, or null. Every stored token is compared, in constant
    /// time over fixed-length hashes, so neither the match position nor the token length leaks.
    /// </summary>
    public string? Authenticate(string? presented)
    {
        if (string.IsNullOrEmpty(presented) || presented.Length > 4096) return null;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(presented));
        string? match = null;
        foreach (var (name, stored) in _tokens)
            if (CryptographicOperations.FixedTimeEquals(stored, hash)) match = name;
        return match;
    }

    public static string Generate() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

/// <summary>Checks the listening configuration before anything is bound.</summary>
public static class BindingPolicy
{
    /// <summary>
    /// Plain HTTP only on loopback unless explicitly opted in; HTTPS needs a certificate. Returns the
    /// problems found (empty when the configuration may start).
    /// </summary>
    public static IReadOnlyList<string> Check(InventorMcpConfig config)
    {
        var problems = new List<string>();
        if (config.HttpUrls.Count == 0) problems.Add("No HTTP URL configured.");
        foreach (var url in config.HttpUrls)
        {
            if (!TryParse(url, out var scheme, out var host))
            {
                problems.Add("'" + url + "' is not an http(s)://host:port URL.");
                continue;
            }
            if (scheme == "https" && string.IsNullOrWhiteSpace(config.HttpCertificatePath))
                problems.Add("'" + url + "' needs a PFX certificate (--http-cert / INVENTOR_SO_HTTP_CERT).");
            if (scheme == "http" && !IsLoopback(host) && !config.HttpAllowInsecureLan)
                problems.Add("'" + url + "' would expose plain HTTP beyond this machine. Use https with a certificate, or opt in with --http-allow-insecure-lan on a trusted network.");
        }
        if (config.HttpRateLimitPerMinute < 1) problems.Add("The rate limit must be at least 1 request per minute.");
        return problems;
    }

    internal static bool TryParse(string url, out string scheme, out string host)
    {
        scheme = host = "";
        int sep = url.IndexOf("://", StringComparison.Ordinal);
        if (sep <= 0) return false;
        scheme = url[..sep].ToLowerInvariant();
        if (scheme is not ("http" or "https")) return false;
        var rest = url[(sep + 3)..];
        int slash = rest.IndexOf('/');
        if (slash >= 0) rest = rest[..slash];
        if (rest.StartsWith('['))
        {
            int close = rest.IndexOf(']');
            if (close < 0) return false;
            host = rest[1..close];
        }
        else
        {
            int colon = rest.LastIndexOf(':');
            host = colon >= 0 ? rest[..colon] : rest;
        }
        return host.Length > 0;
    }

    /// <summary>localhost and loopback addresses only; wildcards (*, +, 0.0.0.0, ::) are not loopback.</summary>
    public static bool IsLoopback(string host)
    {
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return true;
        return IPAddress.TryParse(host, out var ip) && IPAddress.IsLoopback(ip);
    }
}

/// <summary>ASP.NET authentication for the bearer tokens of <see cref="TokenRegistry"/>.</summary>
public sealed class BearerTokenAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "InventorSoBearer";
    private readonly TokenRegistry _tokens;

    public BearerTokenAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger,
        UrlEncoder encoder, TokenRegistry tokens) : base(options, logger, encoder)
    {
        _tokens = tokens;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string? header = Request.Headers.Authorization;
        if (string.IsNullOrEmpty(header)) return Task.FromResult(AuthenticateResult.NoResult());
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(AuthenticateResult.Fail("Only bearer tokens are accepted."));
        var name = _tokens.Authenticate(header["Bearer ".Length..].Trim());
        if (name == null) return Task.FromResult(AuthenticateResult.Fail("Invalid token."));
        // NameIdentifier binds MCP sessions to this client: another token cannot reuse its session id.
        var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, name), new Claim(ClaimTypes.Name, name) }, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = 401;
        Response.Headers.WWWAuthenticate = "Bearer realm=\"inventor-so-mcp\"";
        Response.ContentType = "application/json";
        return Response.WriteAsync("{\"ok\":false,\"error\":{\"code\":\"UNAUTHORIZED\",\"message\":\"A valid bearer token is required.\"}}");
    }
}

/// <summary>The authenticated client of the current HTTP request (tools run in the request's execution context).</summary>
public sealed class HttpCallerIdentity : ICallerIdentity
{
    private readonly IHttpContextAccessor _accessor;
    public HttpCallerIdentity(IHttpContextAccessor accessor) => _accessor = accessor;

    public string Client
    {
        get
        {
            var user = _accessor.HttpContext?.User;
            return user?.Identity?.IsAuthenticated == true ? user.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous" : "anonymous";
        }
    }

    public string? SessionId => _accessor.HttpContext?.Request.Headers["Mcp-Session-Id"].FirstOrDefault();
}
