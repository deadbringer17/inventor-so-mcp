using Inventor.So.RealityCapture;
using Inventor.So.RealityCapture.Review;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Inventor.So.Mcp.Http.Capture;

public sealed partial class CaptureReconstructionService
{
    public JObject GetAlignment(string owner,string id,int revision)
    {
        using var lease=_store.AcquireFinalized(owner,id);string directory=ReadyRevision(lease.Directory,revision);
        lock(_gate) {return new JObject {["ok"]=true,["alignment"]=ReadAlignment(directory)};}
    }
    private static JObject? ReadAlignment(string directory)
    {
        string root=Path.Combine(directory,"alignment");
        string? latest=Directory.Exists(root)?Directory.EnumerateFiles(root,"v*.json").OrderBy(p=>p,StringComparer.Ordinal).LastOrDefault():null;
        return latest==null?null:CaptureJson.Read<JObject>(latest,1024*1024);
    }
    public JObject AlignToCad(string owner,string id,int revision,JObject request)
    {
        if(request.ToString(Formatting.None).Length>16384) throw new CaptureSolveException("CAPTURE_ALIGNMENT_INVALID");
        string? doc=request.Value<string>("document_id"),cadRevision=request.Value<string>("cad_revision");
        string method=request.Value<string>("method")??"three_points";
        int expected=request.Value<int?>("expected_alignment_version")??0;
        if(string.IsNullOrWhiteSpace(doc)||doc.Length>160||string.IsNullOrWhiteSpace(cadRevision)||cadRevision.Length>160) throw new CaptureSolveException("CAPTURE_CAD_IDENTITY_INVALID");
        using var lease=_store.AcquireFinalized(owner,id);string directory=ReadyRevision(lease.Directory,revision);
        lock(_gate)
        {
            var review=ReadReview(directory);var previous=ReadAlignment(directory);
            if((previous?.Value<int>("version")??0)!=expected) throw new CaptureTransferException(409,"CAPTURE_ALIGNMENT_VERSION_CHANGED");
            if(review.Version!=request.Value<int?>("review_version")) throw new CaptureTransferException(409,"CAPTURE_REVIEW_VERSION_CHANGED");
            var raw=CaptureJson.Read<JObject>(Path.Combine(directory,"output","points.json"),1024*1024);
            var points=raw["points"]!.ToObject<ScanPoint[]>()!;var refs=request["reference_ids"]?.ToObject<string[]>()??Array.Empty<string>();
            if(method!="scan_basis"&&(refs.Length!=3||refs.Distinct(StringComparer.Ordinal).Count()!=3)) throw new CaptureSolveException("CAPTURE_ALIGNMENT_INVALID");
            if(method=="scan_basis"&&refs.Length!=0) throw new CaptureSolveException("CAPTURE_ALIGNMENT_INVALID");
            double[] matrix;double[] residuals=Array.Empty<double>();
            try
            {
                ScanPoint Point(string name)=>points.FirstOrDefault(p=>p.Id==name)??throw new CaptureSolveException("CAPTURE_REFERENCE_NOT_FOUND");
                if(method=="scan_basis") matrix=ScanRegistration.ScanToCadBasis;
                else if(method=="three_points")
                {
                    var source=refs.Select(r=>Point(r).Position).ToArray();var target=request["cad_points_mm"]!.ToObject<double[][]>()!;
                    matrix=ScanRegistration.ThreePoints(source,target,true,request.Value<double?>("tolerance_mm")??5);residuals=ScanRegistration.Residuals(matrix,source,target);
                }
                else if(method=="plane_axis")
                {
                    var plane=review.Primitives.FirstOrDefault(p=>p.Id==refs[0]&&p.Kind=="plane")??throw new CaptureSolveException("CAPTURE_REFERENCE_NOT_FOUND");
                    var axis=review.Primitives.FirstOrDefault(p=>p.Id==refs[1]&&(p.Kind=="axis"||p.Kind=="cylinder"))??throw new CaptureSolveException("CAPTURE_REFERENCE_NOT_FOUND");
                    matrix=ScanRegistration.PlaneAxis(Point(refs[2]).Position,plane.Direction,axis.Direction,request["cad_origin_mm"]!.ToObject<double[]>()!,request["cad_normal"]!.ToObject<double[]>()!,request["cad_axis"]!.ToObject<double[]>()!);
                }
                else throw new CaptureSolveException("CAPTURE_ALIGNMENT_INVALID");
            }
            catch(Exception ex) when(ex is ArgumentException or NullReferenceException or JsonException) {throw new CaptureSolveException("CAPTURE_ALIGNMENT_INVALID");}
            if(expected>=32) throw new CaptureTransferException(413,"CAPTURE_ALIGNMENT_QUOTA_EXCEEDED");
            var result=new JObject {["version"]=expected+1,["scan_id"]=id,["solve_revision"]=revision,["review_version"]=review.Version,["document_id"]=doc,["cad_revision"]=cadRevision,["method"]=method,
                ["reference_ids"]=JArray.FromObject(refs),["scan_metric_to_inventor_mm"]=JArray.FromObject(matrix),["residuals_mm"]=JArray.FromObject(residuals),["unit"]="mm",
                ["matrix_convention"]="row-major; column vectors; scan LH to Inventor RH (det -1)",["uncertainty_mm"]=null,["status"]="estimated",["created_at"]=DateTimeOffset.UtcNow};
            result["cad_reference_data"]=new JObject {["points_mm"]=request["cad_points_mm"]?.DeepClone(),["origin_mm"]=request["cad_origin_mm"]?.DeepClone(),["normal"]=request["cad_normal"]?.DeepClone(),["axis"]=request["cad_axis"]?.DeepClone(),["tolerance_mm"]=request.Value<double?>("tolerance_mm")??5};
            string root=Path.Combine(directory,"alignment");Directory.CreateDirectory(root);string pending=Path.Combine(root,Guid.NewGuid().ToString("N")+".pending");
            try {CaptureJson.WriteNew(pending,result);File.Move(pending,Path.Combine(root,"v"+(expected+1).ToString("D6")+".json"));}finally {if(File.Exists(pending)) File.Delete(pending);}
            return new JObject {["ok"]=true,["alignment"]=result};
        }
    }
    public JObject PrepareReference(string owner,string id,int revision,int alignmentVersion)
    {
        using var lease=_store.AcquireFinalized(owner,id);string directory=ReadyRevision(lease.Directory,revision);
        lock(_gate)
        {
            var alignment=ReadAlignment(directory)??throw new CaptureSolveException("CAPTURE_ALIGNMENT_REQUIRED");var review=ReadReview(directory);
            if(alignment.Value<int>("version")!=alignmentVersion||alignment.Value<int>("review_version")!=review.Version) throw new CaptureTransferException(409,"CAPTURE_ALIGNMENT_VERSION_CHANGED");
            var points=CaptureJson.Read<JObject>(Path.Combine(directory,"output","points.json"),1024*1024);
            // Use the immutable operator-input snapshot, not source-label heuristics or today's draft constraints.
            var constraints=CaptureJson.Read<CaptureSolveInput>(Path.Combine(directory,"request.json"),1024*1024).Constraints.Where(c=>c.Enabled).ToArray();
            var payload=new JObject {["reference_id"]="RC_"+Guid.NewGuid().ToString("N").Substring(0,12),["source"]=(JObject)alignment.DeepClone(),
                ["points"]=points["points"]!.DeepClone(),["primitives"]=JArray.FromObject(review.Primitives),["known_constraints"]=JArray.FromObject(constraints),
                ["quality_report"]=CaptureJson.Read<JObject>(Path.Combine(directory,"output","quality_report.json"),1024*1024),["units"]=CaptureJson.Read<JObject>(Path.Combine(directory,"output","units.json"),1024*1024),
                ["geometry"]="estimated",["uncertainty_status"]="not_estimated",["unit"]="mm"};
            using(var file=File.OpenRead(Path.Combine(directory,"output","points.json"))) payload["source"]!["points_sha256"]=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(file)).ToLowerInvariant();
            if(System.Text.Encoding.UTF8.GetByteCount(payload.ToString(Formatting.None))>900000) throw new CaptureTransferException(413,"CAPTURE_REFERENCE_TOO_LARGE");
            return new JObject {["ok"]=true,["operations"]=new JArray(new JObject {["command"]="create_reality_reference",["arguments"]=new JObject {["reference"]=payload}}),
                ["next"]="Preview with inventor_plan_change; commit the same approved plan. Creates work geometry and a point-cloud 3D sketch, never B-Rep."};
        }
    }
}
