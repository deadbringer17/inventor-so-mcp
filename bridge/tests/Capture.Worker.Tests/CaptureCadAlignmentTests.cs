using Inventor.So.Mcp.Http.Capture;
using Inventor.So.RealityCapture;
using Newtonsoft.Json.Linq;

namespace Capture.Worker.Tests;

/// <summary>Host contract tests with a tiny synthetic solved fixture. No native worker or Inventor is claimed here.</summary>
public sealed class CaptureCadAlignmentTests:IAsyncDisposable
{
    private readonly string _root=Path.Combine(Path.GetTempPath(),"rc4-host-"+Guid.NewGuid().ToString("N"));
    private readonly string _owner=CaptureTransferStore.TokenOwner("rc4-synthetic-owner"),_id=Guid.NewGuid().ToString("N");
    private CaptureReconstructionService? _service;
    private string _directory="";
    private async Task<CaptureReconstructionService> Setup()
    {
        var store=new CaptureTransferStore(_root);byte[] bytes={1,2,3};store.Create(_owner,new CaptureUpload(_id,new[]{new CaptureFile("capture_manifest.json",3,CaptureTransferStore.Hash(bytes))}));
        await store.PutAsync(_owner,_id,0,CaptureTransferStore.Hash(bytes),3,new MemoryStream(bytes),CancellationToken.None);store.Finalize(_owner,_id);
        _directory=Path.Combine(_root,_owner,_id,"solves","r000001");Directory.CreateDirectory(Path.Combine(_directory,"output"));
        CaptureJson.WriteNew(Path.Combine(_directory,"status.json"),new CaptureSolveSummary(_id,1,"succeeded",null,DateTimeOffset.UtcNow,DateTimeOffset.UtcNow,0));
        CaptureJson.WriteNew(Path.Combine(_directory,"request.json"),new CaptureSolveInput(_id,1,Path.Combine(_directory,"input"),Path.Combine(_directory,"output"),new[]{new MetricConstraint("external","distance",new[]{"p0","p1"},100,.1,Source:"caliper")},Array.Empty<CalibrationArtifact>()));
        CaptureJson.WriteNew(Path.Combine(_directory,"output","points.json"),new {unit="mm",coordinate_space="scan_metric",points=new[]{new {id="p0",position_mm=new[]{0d,0,100},observations=3},new {id="p1",position_mm=new[]{100d,0,100},observations=3},new {id="p2",position_mm=new[]{0d,100,100},observations=3}}});
        CaptureJson.WriteNew(Path.Combine(_directory,"output","constraints.json"),Array.Empty<MetricConstraint>());
        CaptureJson.WriteNew(Path.Combine(_directory,"output","quality_report.json"),new {evidence_kind="synthetic",metric=new {AccuracyStatus="UNSCALED"}});
        CaptureJson.WriteNew(Path.Combine(_directory,"output","units.json"),new {scan_glb="metre",pointcloud_ply="mm"});
        _service=new CaptureReconstructionService(store,new NoWorker(),new CaptureWorkerSettings("unused",TimeSpan.FromSeconds(1)));
        _service.FitPrimitive(_owner,_id,1,0,"plane","plane",new[]{"p0","p1","p2"});_service.FitPrimitive(_owner,_id,1,1,"axis","axis",new[]{"p0","p1"});return _service;
    }
    private static JObject Request(string method,int version=0)=>new(){["document_id"]="cad",["cad_revision"]="cad-r1",["review_version"]=2,["expected_alignment_version"]=version,["method"]=method,["reference_ids"]=new JArray("p0","p1","p2"),["cad_points_mm"]=new JArray(new JArray(10,20,-70),new JArray(110,20,-70),new JArray(10,120,-70))};
    [Fact] public async Task ThreePointRegistrationPersistsTheFrameAndRejectsMissingReferencesWithoutChangingIt()
    {
        var service=await Setup();var aligned=service.AlignToCad(_owner,_id,1,Request("three_points"));var matrix=aligned["alignment"]!["scan_metric_to_inventor_mm"]!.ToObject<double[]>()!;
        Assert.Equal(10,matrix[3],6);Assert.Equal(20,matrix[7],6);Assert.Equal(30,matrix[11],6);Assert.Equal(-1,matrix[10],6);
        var bad=Request("three_points",1);bad["reference_ids"]=new JArray("missing","p1","p2");
        Assert.Equal("CAPTURE_REFERENCE_NOT_FOUND",Assert.Throws<CaptureSolveException>(()=>service.AlignToCad(_owner,_id,1,bad)).Code);
        Assert.Equal(1,service.GetAlignment(_owner,_id,1)["alignment"]!.Value<int>("version"));Assert.Single(Directory.GetFiles(Path.Combine(_directory,"alignment"),"*.json"));
        var prepared=service.PrepareReference(_owner,_id,1,1);Assert.Equal(3,prepared["operations"]![0]!["arguments"]!["reference"]!["points"]!.Count());
        Assert.Equal("synthetic",prepared["operations"]![0]!["arguments"]!["reference"]!["quality_report"]!.Value<string>("evidence_kind"));
        Assert.Equal("caliper",prepared["operations"]![0]!["arguments"]!["reference"]!["known_constraints"]![0]!.Value<string>("Source"));
    }
    [Fact] public async Task PlaneAxisUsesOwnedPrimitivesAndKeepsEarlierAlignmentSnapshots()
    {
        var service=await Setup();service.AlignToCad(_owner,_id,1,Request("three_points"));string first=File.ReadAllText(Path.Combine(_directory,"alignment","v000001.json"));
        var request=Request("plane_axis",1);request["reference_ids"]=new JArray("plane","axis","p0");request["cad_origin_mm"]=new JArray(10,20,30);request["cad_normal"]=new JArray(0,0,-1);request["cad_axis"]=new JArray(1,0,0);
        var result=service.AlignToCad(_owner,_id,1,request);Assert.Equal(2,result["alignment"]!.Value<int>("version"));
        Assert.Equal(first,File.ReadAllText(Path.Combine(_directory,"alignment","v000001.json")));Assert.Equal(2,Directory.GetFiles(Path.Combine(_directory,"alignment"),"*.json").Length);
        var bad=(JObject)request.DeepClone();bad["expected_alignment_version"]=2;bad["cad_axis"]=new JArray(0,0,-1);
        Assert.Equal("CAPTURE_ALIGNMENT_INVALID",Assert.Throws<CaptureSolveException>(()=>service.AlignToCad(_owner,_id,1,bad)).Code);
        Assert.Equal(2,service.GetAlignment(_owner,_id,1)["alignment"]!.Value<int>("version"));
    }
    [Fact] public async Task ForeignOwnerStaleReviewAndStaleAlignmentAreRefused()
    {
        var service=await Setup();var request=Request("three_points");request["review_version"]=1;
        Assert.Equal("CAPTURE_REVIEW_VERSION_CHANGED",Assert.Throws<CaptureTransferException>(()=>service.AlignToCad(_owner,_id,1,request)).Code);
        Assert.Equal("CAPTURE_NOT_FOUND",Assert.Throws<CaptureTransferException>(()=>service.GetAlignment(CaptureTransferStore.TokenOwner("foreign"),_id,1)).Code);
        service.AlignToCad(_owner,_id,1,Request("three_points"));Assert.Equal("CAPTURE_ALIGNMENT_VERSION_CHANGED",Assert.Throws<CaptureTransferException>(()=>service.AlignToCad(_owner,_id,1,Request("three_points"))).Code);
        service.RemovePrimitive(_owner,_id,1,2,"axis");Assert.Equal("CAPTURE_ALIGNMENT_VERSION_CHANGED",Assert.Throws<CaptureTransferException>(()=>service.PrepareReference(_owner,_id,1,1)).Code);
    }
    private sealed class NoWorker:ICaptureWorker
    {public bool Available=>false;public Task RunAsync(string requestPath,string outputDirectory,CancellationToken ct)=>throw new InvalidOperationException("This test must not reconstruct.");}
    public async ValueTask DisposeAsync() {if(_service!=null) await _service.DisposeAsync();if(Directory.Exists(_root)) Directory.Delete(_root,true);}
}
