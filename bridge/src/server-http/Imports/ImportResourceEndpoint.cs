using System.Security.Cryptography;
using Bimwright.Ipt.Server;
using Bimwright.Ipt.Server.Imports;
using Microsoft.AspNetCore.Http.Features;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Inventor.So.Mcp.Http.Imports;

/// <summary>Authenticated in-memory resource exchange. This endpoint neither imports into CAD nor creates persistent links.</summary>
public static class ImportResourceEndpoint
{
    public const string Route = "/imports/resources";
    public static void MapImportResources(WebApplication app, ImportHttpPolicy policy)
    {
        app.MapPost(Route + "/{format}", (string format, HttpContext http, ImportResourceStore store, ICallerIdentity caller)
                => Upload(format, http, store, caller, policy))
            .RequireAuthorization().RequireRateLimiting(HttpHost.RatePolicy);
        app.MapGet(Route, (HttpContext http, ImportResourceStore store, ICallerIdentity caller) => List(http, store, caller, policy))
            .RequireAuthorization().RequireRateLimiting(HttpHost.RatePolicy);
        app.MapGet(Route + "/{assetId}", (string assetId, HttpContext http, ImportResourceStore store, ICallerIdentity caller) =>
            {
                NoCache(http);
                var record = store.Describe(caller.Client, assetId);
                return record == null ? Missing() : Json(Envelope(record, policy));
            }).RequireAuthorization().RequireRateLimiting(HttpHost.RatePolicy);
        app.MapGet(Route + "/{assetId}/preview", (string assetId, HttpContext http, ImportResourceStore store, ICallerIdentity caller)
            => Preview(assetId, http, store, caller, policy))
            .RequireAuthorization().RequireRateLimiting(HttpHost.RatePolicy);
        app.MapGet(Route + "/{assetId}/content", (string assetId, HttpContext http, ImportResourceStore store, ICallerIdentity caller) =>
            {
                NoCache(http);
                if (!ImportResourceStore.IsValidId(assetId)) return Missing();
                using var lease = store.Acquire(caller.Client, new[] { assetId });
                if (lease == null) return Missing();
                try
                {
                    byte[] bytes = lease.Read(assetId);
                    http.Response.Headers["X-Content-SHA256"] = lease.Records[0].Sha256;
                    // The detached copy is owned by this response; the store lease may now be released.
                    return Results.Bytes(bytes, "application/octet-stream");
                }
                catch (InvalidOperationException) { return Missing(); }
            }).RequireAuthorization().RequireRateLimiting(HttpHost.RatePolicy);
    }

    private static IResult Json(JObject value, int status = 200) => Results.Content(value.ToString(Formatting.None), "application/json", statusCode: status);
    private static IResult Error(int status, string code, string message) => Json(new JObject
        { ["ok"] = false, ["code"] = code, ["error"] = new JObject { ["code"] = code, ["message"] = message } }, status);
    private static IResult Missing() => Error(404, "ASSET_NOT_FOUND", "Unknown, expired or foreign import resource.");
    private static void NoCache(HttpContext http) => http.Response.Headers.CacheControl = "no-store";
    private static JObject Resource(ImportResourceRecord record)
    {
        var data = record.Metadata;
        data["asset_id"] = record.Id; data["format"] = record.Format; data["byte_length"] = record.Size;
        return data;
    }
    private static bool PreviewAvailable(ImportResourceRecord record, ImportHttpPolicy policy)
        => policy.PreviewRenderer != null && record.Format == "bmp";
    private static JObject Envelope(ImportResourceRecord record, ImportHttpPolicy policy) => new()
    {
        ["ok"] = true, ["storage"] = "memory", ["cad_import_available"] = false,
        ["inspection_state"] = "verified_metadata", ["resource"] = Resource(record),
        ["sha256"] = record.Sha256, ["expires_utc"] = record.ExpiresUtc.ToString("O"),
        ["preview_available"] = PreviewAvailable(record, policy),
    };
    private static async Task<IResult> Preview(string assetId, HttpContext http, ImportResourceStore store, ICallerIdentity caller, ImportHttpPolicy policy)
    {
        NoCache(http);
        if (http.Request.Query.Count != 0) return Error(400, "INVALID_ARGUMENT", "Preview accepts no query arguments.");
        if (!ImportResourceStore.IsValidId(assetId)) return Missing();
        using var lease = store.Acquire(caller.Client, new[] { assetId });
        if (lease == null) return Missing();
        var record = lease.Records[0];
        if (!PreviewAvailable(record, policy)) return Error(415, "IMPORT_PREVIEW_UNAVAILABLE", "No preview renderer is configured for this resource format.");
        byte[]? source = null;
        try
        {
            source = lease.Read(assetId);
            byte[] png = await policy.PreviewRenderer!.RenderAsync(source, http.RequestAborted);
            http.RequestAborted.ThrowIfCancellationRequested();
            http.Response.Headers["X-Content-SHA256"] = Convert.ToHexString(SHA256.HashData(png)).ToLowerInvariant();
            http.Response.Headers["X-Source-SHA256"] = record.Sha256;
            return Results.Bytes(png, "image/png");
        }
        catch (OperationCanceledException) when (http.RequestAborted.IsCancellationRequested) { return Results.StatusCode(499); }
        catch (Exception ex) when (ex is ArgumentException or OverflowException)
        { return Error(422, "IMPORT_PREVIEW_INVALID", "Resource content or configured preview budget prevents rendering."); }
        catch (InvalidOperationException) { return Missing(); }
        catch (Exception) { return Error(500, "IMPORT_PREVIEW_FAILED", "The configured preview renderer could not render this resource."); }
        finally { if (source != null) CryptographicOperations.ZeroMemory(source); }
    }
    private static IResult List(HttpContext http, ImportResourceStore store, ICallerIdentity caller, ImportHttpPolicy policy)
    {
        NoCache(http);
        // Offset is only navigation over the current catalog, never a stable snapshot or an authorization token.
        int offset = 0;
        if (http.Request.Query.TryGetValue("offset", out var offsetValue)
            && (offsetValue.Count != 1 || !int.TryParse(offsetValue[0], out offset) || offset < 0))
            return Error(400, "INVALID_ARGUMENT", "offset must be a nonnegative integer.");
        if (http.Request.Query.Keys.Any(key => key != "offset")) return Error(400, "INVALID_ARGUMENT", "Unknown list argument.");
        var all = store.List(caller.Client);
        var selected = all.Skip(offset).Take(policy.ListLimit).ToArray();
        long next = (long)offset + selected.Length;
        return Json(new JObject { ["ok"] = true, ["storage"] = "memory", ["cad_import_available"] = false,
            ["resources"] = new JArray(selected.Select(record => Envelope(record, policy))), ["next_offset"] = next < all.Count ? next : JValue.CreateNull() });
    }

    private static async Task<IResult> Upload(string format, HttpContext http, ImportResourceStore store, ICallerIdentity caller, ImportHttpPolicy policy)
    {
        NoCache(http);
        if (!policy.TryKind(format, out string kind)) return Error(415, "IMPORT_UNSUPPORTED_FORMAT", "This format has no configured inspector policy.");
        if (http.Request.Query.Count != 0) return Error(400, "INVALID_ARGUMENT", "Upload accepts no owner, path or metadata query arguments.");
        if (!string.Equals(http.Request.ContentType, "application/octet-stream", StringComparison.OrdinalIgnoreCase))
            return Error(415, "IMPORT_UNSUPPORTED_FORMAT", "Upload requires application/octet-stream.");
        if (http.Request.ContentLength is { } declared)
        {
            if (declared == 0) return Error(400, "INVALID_ARGUMENT", "Import content is empty.");
            if (declared > policy.MaxBodyBytes) return Error(413, "IMPORT_TOO_LARGE", "Import content exceeds the configured HTTP limit.");
        }
        var feature = http.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (feature is { IsReadOnly: false }) feature.MaxRequestBodySize = policy.MaxBodyBytes;
        byte[] scratch = new byte[Math.Min(policy.MaxBodyBytes, 81920)];
        using var content = new MemoryStream();
        byte[]? bytes = null;
        try
        {
            while (true)
            {
                int count = await http.Request.Body.ReadAsync(scratch, http.RequestAborted);
                if (count == 0) break;
                if (count > policy.MaxBodyBytes - content.Length)
                    return Error(413, "IMPORT_TOO_LARGE", "Import content exceeds the configured HTTP limit.");
                await content.WriteAsync(scratch.AsMemory(0, count), http.RequestAborted);
            }
            if (content.Length == 0) return Error(400, "INVALID_ARGUMENT", "Import content is empty.");
            if (http.Request.ContentLength.HasValue && http.Request.ContentLength.Value != content.Length)
                return Error(400, "INVALID_ARGUMENT", "Import content length is incomplete.");
            bytes = content.ToArray();
            // Inspector is a trusted host service, not a client-provided metadata body. Supply a separate
            // buffer so even a buggy inspector cannot mutate the bytes later stored under their hash.
            byte[] inspected = (byte[])bytes.Clone();
            JObject metadata;
            try
            {
                metadata = await policy.Inspector.InspectAsync(format, inspected, http.RequestAborted);
                if (!inspected.AsSpan().SequenceEqual(bytes)) throw new ArgumentException("Inspector mutated source bytes.");
            }
            finally { CryptographicOperations.ZeroMemory(inspected); }
            var frozen = ValidateMetadata(metadata, kind);
            http.RequestAborted.ThrowIfCancellationRequested();
            ImportResourceRecord record;
            try { record = store.Put(caller.Client, bytes, format, frozen); }
            catch (InvalidOperationException)
            { return Error(409, "IMPORT_CAPACITY_EXHAUSTED", "Import storage is currently unavailable."); }
            return Json(Envelope(record, policy), 201);
        }
        catch (BadHttpRequestException ex) when (ex.StatusCode == 413)
        { return Error(413, "IMPORT_TOO_LARGE", "Import content exceeds the configured HTTP limit."); }
        catch (BadHttpRequestException) { return Error(400, "INVALID_ARGUMENT", "Import content could not be read completely."); }
        catch (OperationCanceledException) when (http.RequestAborted.IsCancellationRequested) { return Results.StatusCode(499); }
        catch (Exception ex) when (ex is ArgumentException or OverflowException or JsonException)
        { return Error(422, "IMPORT_CONTENT_INVALID", "Import content or inspected metadata does not satisfy host policy."); }
        catch (Exception) { return Error(500, "IMPORT_INSPECTION_FAILED", "The configured import inspector could not validate this resource."); }
        finally
        {
            CryptographicOperations.ZeroMemory(scratch);
            if (bytes != null) CryptographicOperations.ZeroMemory(bytes);
            if (content.TryGetBuffer(out var buffer)) CryptographicOperations.ZeroMemory(buffer.AsSpan());
        }
    }

    private static JObject ValidateMetadata(JObject? metadata, string kind)
    {
        if (metadata == null || metadata.Properties().Any(p => p.Name is not ("kind" or "dependencies" or "image" or "geometry"))
            || metadata["kind"]?.Type != JTokenType.String || (string?)metadata["kind"] != kind
            || metadata["dependencies"] is not JArray dependencies)
            throw new ArgumentException("Invalid inspected resource shape.");
        // External resources require the later bundle registration/resolution workflow; never silently drop them.
        if (dependencies.Count != 0) throw new ArgumentException("External dependencies require a resolved import bundle.");
        static bool Null(JToken? token) => token == null || token.Type == JTokenType.Null;
        static long Integer(JToken? token) => token?.Type == JTokenType.Integer ? (long)token : throw new ArgumentException("Expected integer.");
        static double Number(JToken? token)
        {
            if (token?.Type is not (JTokenType.Integer or JTokenType.Float)) throw new ArgumentException("Expected number.");
            double value = (double)token;
            return double.IsNaN(value) || double.IsInfinity(value) ? throw new ArgumentException("Expected finite number.") : value;
        }
        if (kind == "Image")
        {
            if (metadata["image"] is not JObject image || !Null(metadata["geometry"])
                || image.Properties().Any(p => p.Name is not ("pixel_width" or "pixel_height"))
                || Integer(image["pixel_width"]) is < 1 or > int.MaxValue || Integer(image["pixel_height"]) is < 1 or > int.MaxValue)
                throw new ArgumentException("Expected raster metadata.");
        }
        else
        {
            if (!Null(metadata["image"]) || metadata["geometry"] is not JObject geometry
                || geometry.Properties().Any(p => p.Name is not ("unit" or "millimeters_per_unit" or "point_count" or "bounds"))
                || geometry["unit"]?.Type != JTokenType.String || string.IsNullOrEmpty((string?)geometry["unit"])
                || !((string)geometry["unit"]!).All(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_' or '-')
                || Number(geometry["millimeters_per_unit"]) <= 0)
                throw new ArgumentException("Expected resolved geometry units.");
            if (kind == "Points" ? Integer(geometry["point_count"]) < 1 : !Null(geometry["point_count"]))
                throw new ArgumentException("Inconsistent point metadata.");
            if (!Null(geometry["bounds"]))
            {
                if (geometry["bounds"] is not JObject bounds || bounds.Properties().Any(p => p.Name is not ("min_x" or "min_y" or "max_x" or "max_y")))
                    throw new ArgumentException("Invalid resource bounds.");
                double minX = Number(bounds["min_x"]), minY = Number(bounds["min_y"]), maxX = Number(bounds["max_x"]), maxY = Number(bounds["max_y"]);
                if (minX > maxX || minY > maxY) throw new ArgumentException("Reversed resource bounds.");
                foreach (double value in new[] { minX, minY, maxX, maxY })
                    if (double.IsInfinity(value * Number(geometry["millimeters_per_unit"]))) throw new ArgumentException("Converted resource bounds overflow.");
            }
        }
        // Enforce JSON-only values; never retain CLR mutable values returned by an inspector.
        return JObject.Parse(metadata.ToString(Formatting.None));
    }
}
