using Bimwright.Ipt.Server.RealityCapture;
using Inventor.So.RealityCapture;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Inventor.So.Mcp.Http.Capture;

public sealed class HttpCaptureControl(CaptureTransferStore store,CaptureReconstructionService reconstruction,IHttpContextAccessor context) : ICaptureControl
{
    public Task<JObject> InvokeAsync(string operation,JObject args,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var http=context.HttpContext;
        if(http?.User.Identity?.IsAuthenticated!=true) return Task.FromResult(Error("UNAUTHORIZED"));
        string owner=CaptureTransferStore.TokenOwner(http.Request.Headers.Authorization.ToString()["Bearer ".Length..].Trim());
        try
        {
            string id=args.Value<string>("session_id") ?? "";
            int version=args.Value<int?>("expected_version") ?? 0,revision=args.Value<int?>("revision") ?? 1;
            JObject result=operation switch {
                "create_session"=>JObject.FromObject(store.Create(owner,Parse<CaptureUpload>(args.Value<string>("manifest_json")))),
                "get_session"=>JObject.FromObject(store.Describe(owner,id)),
                "add_constraint"=>reconstruction.SetConstraint(owner,id,Parse<MetricConstraint>(args.Value<string>("constraint_json")),null,version),
                "remove_constraint"=>reconstruction.SetConstraint(owner,id,null,args.Value<string>("constraint_id") ?? "",version),
                "list_constraints"=>reconstruction.GetConstraints(owner,id),
                "set_artifact"=>reconstruction.SetArtifact(owner,id,Parse<CalibrationArtifact>(args.Value<string>("artifact_json")),version),
                "start_solve"=>reconstruction.StartSolve(owner,id,version),
                "get_solution"=>reconstruction.GetSolution(owner,id,revision),
                "get_quality"=>reconstruction.GetQuality(owner,id,revision),
                "get_review"=>reconstruction.GetReview(owner,id,revision),
                "get_alignment"=>reconstruction.GetAlignment(owner,id,revision),
                "align_to_cad"=>reconstruction.AlignToCad(owner,id,revision,Parse<JObject>(args.Value<string>("registration_json"))),
                "create_reference"=>reconstruction.PrepareReference(owner,id,revision,args.Value<int?>("alignment_version")??0),
                "fit_primitive"=>reconstruction.FitPrimitive(owner,id,revision,version,args.Value<string>("primitive_id") ?? "",args.Value<string>("kind") ?? "",Parse<string[]>(args.Value<string>("point_ids_json"))),
                "remove_primitive"=>reconstruction.RemovePrimitive(owner,id,revision,version,args.Value<string>("primitive_id") ?? ""),
                "cancel_solve"=>reconstruction.Cancel(owner,id,revision),
                "export"=>Export(owner,id,revision,args.Value<string>("format") ?? "glb"),
                _=>throw new CaptureSolveException("CAPTURE_OPERATION_INVALID")
            };
            return Task.FromResult(result);
        }
        catch(CaptureTransferException ex) { return Task.FromResult(Error(ex.Code)); }
        catch(CaptureSolveException ex) { return Task.FromResult(Error(ex.Code)); }
        catch(JsonException) { return Task.FromResult(Error("CAPTURE_ARGUMENT_INVALID")); }
        catch(FormatException) { return Task.FromResult(Error("CAPTURE_ARGUMENT_INVALID")); }
        catch(OverflowException) { return Task.FromResult(Error("CAPTURE_ARGUMENT_INVALID")); }
        catch(InvalidCastException) { return Task.FromResult(Error("CAPTURE_ARGUMENT_INVALID")); }
        catch(IOException) { return Task.FromResult(Error("CAPTURE_STORAGE_ERROR")); }
        catch(UnauthorizedAccessException) { return Task.FromResult(Error("CAPTURE_STORAGE_ERROR")); }
    }
    private JObject Export(string owner,string id,int revision,string format)
    {
        string name=format switch { "glb"=>"scan.glb","ply"=>"pointcloud.ply","obj"=>"scan.obj","points"=>"points.json",_=>throw new CaptureSolveException("CAPTURE_EXPORT_FORMAT_INVALID") };
        var solution=reconstruction.GetSolution(owner,id,revision);
        var asset=solution["assets"]?.FirstOrDefault(a=>a.Value<string>("name")==name);
        if(asset==null) throw new CaptureTransferException(409,"CAPTURE_SOLUTION_NOT_READY");
        return new JObject { ["ok"]=true,["asset"]=asset.DeepClone(),["units_metadata_url"]=$"/capture/v1/sessions/{id}/solutions/{revision}/assets/units.json" };
    }
    private static T Parse<T>(string? json)
    {
        if(string.IsNullOrWhiteSpace(json) || json.Length>1024*1024) throw new CaptureSolveException("CAPTURE_ARGUMENT_INVALID");
        return JsonConvert.DeserializeObject<T>(json,CaptureJson.Settings) ?? throw new CaptureSolveException("CAPTURE_ARGUMENT_INVALID");
    }
    private static JObject Error(string code)=>new() { ["ok"]=false,["error"]=new JObject { ["code"]=code,["message"]="Capture operation could not be completed." } };
}
