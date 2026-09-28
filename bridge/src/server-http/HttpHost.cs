using Inventor.So.Mcp.Http.Pairing;
using System.Threading.RateLimiting;
using Bimwright.Ipt.Server;
using Bimwright.Ipt.Server.Assets;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Net.Http.Headers;
using HostOptions = Inventor.So.Mcp.Http.Pairing.HostOptions;
using PairingEndpoint = Inventor.So.Mcp.Http.Pairing.PairingEndpoint;

namespace Inventor.So.Mcp.Http;

/// <summary>
/// The remote host (plan §21-22): Streamable HTTP MCP on <c>/mcp</c>, <c>GET /assets/{id}</c>, the
/// WebXR viewer on <c>/viewer/</c> and an anonymous <c>/healthz</c>. The named pipe to Inventor
/// stays local; only this process listens on the network, and only as configured.
/// </summary>
public static class HttpHost
{
    public const string RatePolicy = "per-client";
    public const long MaxRequestBodyBytes = 4 * 1024 * 1024;

    public static WebApplication Build(string[] args, InventorMcpConfig config, TokenRegistry tokens, HostOptions? options = null)
    {
        config.Transport = "http";
        var problems = BindingPolicy.Check(config);
        if (problems.Count > 0) throw new InvalidOperationException(string.Join(Environment.NewLine, problems));
        if (tokens.Count == 0 && options?.Pairing == null)
            throw new InvalidOperationException("No client token configured. Create one with --generate-token <name> and pass the file with --http-token-file, or pair a headset with --pair <name>.");
        // Without a configured public URL, asset URLs are relative to the MCP endpoint's origin.
        config.PublicBaseUrl ??= "";

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, ContentRootPath = AppContext.BaseDirectory });
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
        builder.WebHost.UseUrls(config.HttpUrls.ToArray());
        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.AddServerHeader = false;
            kestrel.Limits.MaxRequestBodySize = MaxRequestBodyBytes;
            var certificate = options?.Certificate;
            if (certificate == null && !string.IsNullOrWhiteSpace(config.HttpCertificatePath))
                certificate = PairingSetup.ResolveCertificate(config);
            if (certificate != null) kestrel.ConfigureHttpsDefaults(https => https.ServerCertificate = certificate);
        });

        var services = builder.Services;
        services.AddSingleton(tokens);
        services.AddHttpContextAccessor();
        services.AddSingleton<ICallerIdentity, HttpCallerIdentity>();
        services.AddInventorServices(config);
        services.AddAuthentication(BearerTokenAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, BearerTokenAuthenticationHandler>(BearerTokenAuthenticationHandler.SchemeName, _ => { });
        services.AddAuthorization();
        services.AddCors(cors => cors.AddDefaultPolicy(policy =>
        {
            // No origin is allowed unless listed: the viewer is same-origin and needs none.
            if (config.HttpAllowedOrigins.Count > 0)
                policy.WithOrigins(config.HttpAllowedOrigins.ToArray())
                    .WithHeaders("authorization", "content-type", "mcp-session-id", "mcp-protocol-version", "last-event-id")
                    .WithExposedHeaders("mcp-session-id", "etag")
                    .WithMethods("GET", "POST", "DELETE");
        }));
        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.OnRejected = async (context, ct) =>
            {
                context.HttpContext.Response.ContentType = "application/json";
                await context.HttpContext.Response.WriteAsync(
                    "{\"ok\":false,\"error\":{\"code\":\"RATE_LIMITED\",\"message\":\"Too many requests; retry later.\"}}", ct);
            };
            limiter.AddPolicy(RatePolicy, http => RateLimitPartition.GetFixedWindowLimiter(
                // Authenticated clients get their own budget; anything else shares one per address.
                http.User.Identity?.IsAuthenticated == true ? "client:" + http.User.Identity.Name : "ip:" + http.Connection.RemoteIpAddress,
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = config.HttpRateLimitPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }));
        });
        services.AddMcpServer(o => o.ServerInstructions = ServerInstructions.Text)
            .WithHttpTransport()
            .AddInventorMcp(config);

        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            var headers = context.Response.Headers;
            headers["X-Content-Type-Options"] = "nosniff";
            headers["Referrer-Policy"] = "no-referrer";
            headers["X-Frame-Options"] = "DENY";
            headers["Content-Security-Policy"] = context.Request.Path.StartsWithSegments("/viewer")
                ? ViewerCsp
                : "default-src 'none'; frame-ancestors 'none'";
            if (context.Request.IsHttps) headers["Strict-Transport-Security"] = "max-age=31536000";
            await next();
        });
        app.UseCors();
        app.UseAuthentication();
        app.UseRateLimiter();
        app.UseAuthorization();

        app.MapGet("/healthz", () => Results.Json(new { status = "ok" }));
        if (options?.Pairing is { } pairing)
            app.MapPost("/pair", async (HttpContext http) => { return await pairing.HandleAsync(http); }).RequireRateLimiting(RatePolicy);
        app.MapMcp("/mcp").RequireAuthorization().RequireRateLimiting(RatePolicy);
        app.MapGet("/assets/{assetId}", (string assetId, HttpContext http, AssetStore store, ICallerIdentity caller) =>
            {
                if (!store.TryRead(assetId, caller.Client, out var record, out var bytes))
                    return Results.Json(new { ok = false, error = new { code = "ASSET_NOT_FOUND", message = "Unknown, expired or foreign asset id." } },
                        statusCode: StatusCodes.Status404NotFound);
                var etag = new EntityTagHeaderValue("\"" + record!.Sha256 + "\"");
                if (http.Request.Headers.IfNoneMatch.Any(v => v == etag.ToString()))
                    return Results.StatusCode(StatusCodes.Status304NotModified);
                http.Response.Headers.CacheControl = "private, max-age=3600, immutable";
                http.Response.Headers["X-Content-SHA256"] = record.Sha256;
                return Results.Bytes(bytes!, record.MimeType, entityTag: etag);
            })
            .RequireAuthorization()
            .RequireRateLimiting(RatePolicy);
        MapViewer(app);
        return app;
    }

    /// <summary>
    /// The WebXR viewer: static files embedded in this assembly, served without authentication (they
    /// contain no data; the page asks for a token and uses it for /mcp and /assets).
    /// </summary>
    private static void MapViewer(WebApplication app)
    {
        var assembly = typeof(HttpHost).Assembly;
        // Routing treats "/viewer" and "/viewer/" alike; relative module URLs need the slash.
        app.MapGet("/viewer/{**file}", (string? file, HttpContext http) =>
        {
            if (string.IsNullOrEmpty(file) && !(http.Request.Path.Value ?? "").EndsWith('/')) return Results.Redirect("/viewer/");
            file = string.IsNullOrEmpty(file) ? "index.html" : file;
            if (!ViewerFiles.TryGetValue(file, out var entry)) return Results.NotFound();
            using var stream = assembly.GetManifestResourceStream(entry.resource);
            if (stream == null) return Results.NotFound();
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            return Results.Bytes(memory.ToArray(), entry.mime);
        });
    }

    /// <summary>Allowlist of viewer files: no path from the request ever reaches the filesystem.</summary>
    internal static readonly IReadOnlyDictionary<string, (string resource, string mime)> ViewerFiles =
        new[]
        {
            ("index.html", "text/html; charset=utf-8"),
            ("viewer.js", "text/javascript; charset=utf-8"),
            ("vendor/three.module.min.js", "text/javascript; charset=utf-8"),
            ("vendor/THREE-LICENSE.txt", "text/plain; charset=utf-8"),
            ("vendor/jsm/loaders/GLTFLoader.js", "text/javascript; charset=utf-8"),
            ("vendor/jsm/utils/BufferGeometryUtils.js", "text/javascript; charset=utf-8"),
            ("vendor/jsm/controls/OrbitControls.js", "text/javascript; charset=utf-8"),
            ("vendor/jsm/webxr/VRButton.js", "text/javascript; charset=utf-8"),
        }.ToDictionary(f => f.Item1, f => ("viewer/" + f.Item1, f.Item2), StringComparer.Ordinal);

    /// <summary>
    /// The viewer's CSP: its own scripts only, plus the SHA-256 of the inline import map (update it
    /// with the page; HttpHostTests checks they agree). Network access only back to this host.
    /// </summary>
    internal const string ImportMapSha256 = "kQvfkhXWSfZg9XP6nk6ENzR2KAyx3ZanVeMXlckJiJ8=";
    internal const string ViewerCsp = "default-src 'self'; script-src 'self' 'sha256-" + ImportMapSha256 + "'; connect-src 'self'; " +
        "img-src 'self' blob: data:; style-src 'self' 'unsafe-inline'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";
}
