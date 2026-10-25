using System.Text;
using Newtonsoft.Json;

namespace Inventor.So.Mcp.Http.Capture;

public static class CaptureEndpoint
{
    public const string Route = "/capture/v1/sessions";
    public static void Map(WebApplication app)
    {
        var group = app.MapGroup(Route).RequireAuthorization().RequireRateLimiting(HttpHost.RatePolicy);
        group.MapPost("", (HttpContext http,CaptureTransferStore store) => Guard(http,async () =>
        {
            if (http.Request.ContentLength > 1024*1024) throw new CaptureTransferException(413,"CAPTURE_MANIFEST_TOO_LARGE");
            using var memory = new MemoryStream();
            var buffer = new byte[65536];
            while (true)
            {
                int n = await http.Request.Body.ReadAsync(buffer,http.RequestAborted); if (n == 0) break;
                if (memory.Length + n > 1024*1024) throw new CaptureTransferException(413,"CAPTURE_MANIFEST_TOO_LARGE");
                memory.Write(buffer,0,n);
            }
            var manifest = JsonConvert.DeserializeObject<CaptureUpload>(Encoding.UTF8.GetString(memory.ToArray()),new JsonSerializerSettings { MaxDepth = 16 });
            return Results.Json(store.Create(Owner(http),manifest!));
        }));
        group.MapGet("/{id}",(string id,HttpContext http,CaptureTransferStore store) => Guard(http,() => Task.FromResult(Results.Json(store.Describe(Owner(http),id)))));
        group.MapPut("/{id}/chunks/{sequence:int}",(string id,int sequence,HttpContext http,CaptureTransferStore store) => Guard(http,async () =>
        {
            if (!string.Equals(http.Request.ContentType,"application/octet-stream",StringComparison.OrdinalIgnoreCase)) throw new CaptureTransferException(415,"CAPTURE_BINARY_REQUIRED");
            await store.PutAsync(Owner(http),id,sequence,http.Request.Headers["X-Content-SHA256"].ToString(),http.Request.ContentLength,http.Request.Body,http.RequestAborted);
            return Results.Json(new { ok = true, sequence });
        }));
        group.MapPost("/{id}/finalize",(string id,HttpContext http,CaptureTransferStore store) => Guard(http,() => Task.FromResult(Results.Json(store.Finalize(Owner(http),id)))));
        group.MapDelete("/{id}",(string id,HttpContext http,CaptureTransferStore store) => Guard(http,() =>
        { store.Delete(Owner(http),id); return Task.FromResult(Results.Json(new { ok = true })); }));
        group.MapGet("/{id}/solutions",(string id,HttpContext http,CaptureReconstructionService service)=>Guard(http,
            ()=>Task.FromResult(Json(service.ListSolutions(Owner(http),id)))));
        group.MapGet("/{id}/solutions/{revision:int}/review",(string id,int revision,HttpContext http,CaptureReconstructionService service)=>Guard(http,
            ()=>Task.FromResult(Json(service.GetReview(Owner(http),id,revision)))));
        group.MapGet("/{id}/solutions/{revision:int}",(string id,int revision,HttpContext http,CaptureReconstructionService service)=>Guard(http,
            ()=>Task.FromResult(Json(service.GetSolution(Owner(http),id,revision)))));
        group.MapGet("/{id}/solutions/{revision:int}/quality",(string id,int revision,HttpContext http,CaptureReconstructionService service)=>Guard(http,
            ()=>Task.FromResult(Json(service.GetQuality(Owner(http),id,revision)))));
        group.MapGet("/{id}/solutions/{revision:int}/assets/{asset}",(string id,int revision,string asset,HttpContext http,CaptureReconstructionService service)=>Guard(http,()=>
        {
            byte[] bytes=service.ReadAsset(Owner(http),id,revision,asset); http.Response.Headers["X-Content-SHA256"]=CaptureTransferStore.Hash(bytes);
            return Task.FromResult(Results.Bytes(bytes,asset.EndsWith(".glb",StringComparison.Ordinal)?"model/gltf-binary":"application/octet-stream"));
        }));
    }
    private static string Owner(HttpContext http) => CaptureTransferStore.TokenOwner(http.Request.Headers.Authorization.ToString()["Bearer ".Length..].Trim());
    private static async Task<IResult> Guard(HttpContext http,Func<Task<IResult>> action)
    {
        http.Response.Headers.CacheControl = "no-store";
        try { return await action(); }
        catch (CaptureTransferException ex) { return Error(ex.Status,ex.Code); }
        catch (Inventor.So.RealityCapture.CaptureSolveException ex) { return Error(400,ex.Code); }
        catch (JsonException) { return Error(400,"CAPTURE_MANIFEST_INVALID"); }
        catch (IOException) { return Error(507,"CAPTURE_STORAGE_ERROR"); }
        catch (UnauthorizedAccessException) { return Error(507,"CAPTURE_STORAGE_ERROR"); }
    }
    private static IResult Error(int status,string code) => Results.Json(new { ok = false, code, error = new { code, message = "Capture request could not be completed." } },statusCode:status);
    private static IResult Json(Newtonsoft.Json.Linq.JObject value)=>Results.Content(value.ToString(Formatting.None),"application/json");
}
