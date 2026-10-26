using Inventor.So.RealityCapture;
using Inventor.So.RealityCapture.Review;
using Newtonsoft.Json.Linq;

namespace Inventor.So.Mcp.Http.Capture;

public sealed partial class CaptureReconstructionService
{
    private sealed record ReviewSnapshot(int Version,ScanPrimitive[] Primitives);
    private readonly SemaphoreSlim _reviewFitting=new(1,1);
    public JObject ListSolutions(string owner,string id)
    {
        using var lease=_store.AcquireFinalized(owner,id);
        string root=Path.Combine(lease.Directory,"solves");
        return JObject.FromObject(new {ok=true,solutions=Directory.Exists(root)?RevisionDirectories(root).Select(ReadStatus).ToArray():Array.Empty<CaptureSolveSummary>()});
    }
    public JObject GetReview(string owner,string id,int revision)
    {
        using var lease=_store.AcquireFinalized(owner,id);
        string directory=ReadyRevision(lease.Directory,revision);
        lock(_gate) {return ReviewJson(ReadReview(directory),id,revision);}
    }
    public JObject FitPrimitive(string owner,string id,int revision,int expectedVersion,string primitiveId,string kind,string[] pointIds)
    {
        if(!MetricConstraint.ValidName(primitiveId)||pointIds==null||pointIds.Length<1||pointIds.Length>2000||pointIds.Distinct(StringComparer.Ordinal).Count()!=pointIds.Length)
            throw new CaptureSolveException("CAPTURE_PRIMITIVE_INVALID");
        using var lease=_store.AcquireFinalized(owner,id);
        string directory=ReadyRevision(lease.Directory,revision);
        if(!_reviewFitting.Wait(0)) throw new CaptureTransferException(409,"CAPTURE_REVIEW_BUSY");
        try
        {
            lock(_gate) {if(ReadReview(directory).Version!=expectedVersion) throw new CaptureTransferException(409,"CAPTURE_REVIEW_VERSION_CHANGED");}
            var raw=CaptureJson.Read<JObject>(Path.Combine(directory,"output","points.json"),1024*1024);
            if(raw.Value<string>("unit")!="mm"||raw.Value<string>("coordinate_space")!="scan_metric") throw new CaptureSolveException("CAPTURE_RESULT_INVALID");
            var all=raw["points"]?.ToObject<ScanPoint[]>() ?? Array.Empty<ScanPoint>();
            try {ScanGeometry.Validate(all);} catch(ArgumentException) {throw new CaptureSolveException("CAPTURE_RESULT_INVALID");}
            var byId=all.ToDictionary(p=>p.Id,StringComparer.Ordinal);
            if(pointIds.Any(p=>p==null||!byId.ContainsKey(p))) throw new CaptureSolveException("CAPTURE_REFERENCE_NOT_FOUND");
            ScanPrimitive primitive;
            try {primitive=ScanGeometry.Fit(primitiveId,kind,pointIds.Select(p=>byId[p]).ToArray());}
            catch(ArgumentException) {throw new CaptureSolveException("CAPTURE_PRIMITIVE_DEGENERATE");}
            var frozen=CaptureJson.Read<MetricConstraint[]>(Path.Combine(directory,"output","constraints.json"),1024*1024);
            primitive.ConstraintIds=frozen.Where(c=>c.Enabled&&c.PointIds.Any(pointIds.Contains)).Select(c=>c.Id).ToArray();
            lock(_gate)
            {
                var previous=ReadReview(directory);
                if(previous.Version!=expectedVersion) throw new CaptureTransferException(409,"CAPTURE_REVIEW_VERSION_CHANGED");
                var list=previous.Primitives.Where(p=>p.Id!=primitiveId).Append(primitive).ToArray();
                return WriteReview(directory,new(previous.Version+1,list),id,revision);
            }
        }
        finally {_reviewFitting.Release();}
    }
    public JObject RemovePrimitive(string owner,string id,int revision,int expectedVersion,string primitiveId)
    {
        if(!MetricConstraint.ValidName(primitiveId)) throw new CaptureSolveException("CAPTURE_PRIMITIVE_INVALID");
        using var lease=_store.AcquireFinalized(owner,id);string directory=ReadyRevision(lease.Directory,revision);
        lock(_gate)
        {
            var previous=ReadReview(directory);
            if(previous.Version!=expectedVersion) throw new CaptureTransferException(409,"CAPTURE_REVIEW_VERSION_CHANGED");
            return WriteReview(directory,new(previous.Version+1,previous.Primitives.Where(p=>p.Id!=primitiveId).ToArray()),id,revision);
        }
    }
    private string ReadyRevision(string root,int revision)
    {string directory=FindRevision(root,revision);if(ReadStatus(directory).State!="succeeded") throw new CaptureTransferException(409,"CAPTURE_SOLUTION_NOT_READY");return directory;}
    private static ReviewSnapshot ReadReview(string directory)
    {
        string root=Path.Combine(directory,"review");
        string? latest=Directory.Exists(root)?Directory.EnumerateFiles(root,"v*.json").OrderBy(p=>p,StringComparer.Ordinal).LastOrDefault():null;
        return latest==null?new(0,Array.Empty<ScanPrimitive>()):CaptureJson.Read<ReviewSnapshot>(latest,1024*1024);
    }
    private static JObject ReviewJson(ReviewSnapshot snapshot,string id,int revision)=>JObject.FromObject(new {ok=true,scan_id=id,revision,version=snapshot.Version,primitives=snapshot.Primitives,uncertainty_status="not_estimated"});
    private static JObject WriteReview(string directory,ReviewSnapshot snapshot,string id,int revision)
    {
        if(snapshot.Version>64||snapshot.Primitives.Length>32) throw new CaptureTransferException(413,"CAPTURE_REVIEW_QUOTA_EXCEEDED");
        string root=Path.Combine(directory,"review");Directory.CreateDirectory(root);
        long size=System.Text.Encoding.UTF8.GetByteCount(Newtonsoft.Json.JsonConvert.SerializeObject(snapshot,Newtonsoft.Json.Formatting.Indented,CaptureJson.Settings));
        if(size>1024*1024||Directory.EnumerateFiles(root,"*.json").Sum(p=>new FileInfo(p).Length)+size>8L*1024*1024) throw new CaptureTransferException(413,"CAPTURE_REVIEW_QUOTA_EXCEEDED");
        string pending=Path.Combine(root,Guid.NewGuid().ToString("N")+".pending");
        try {CaptureJson.WriteNew(pending,snapshot);File.Move(pending,Path.Combine(root,"v"+snapshot.Version.ToString("D6")+".json"));}
        finally {if(File.Exists(pending)) File.Delete(pending);}
        return ReviewJson(snapshot,id,revision);
    }
}
