using Inventor.So.Capture.Worker;
using Inventor.So.RealityCapture;
using InventorXrSo.Core.Capture;
using Newtonsoft.Json.Linq;
using OpenCvSharp;
using OpenCvSharp.Aruco;

namespace Capture.Worker.Tests;

public sealed class SyntheticReconstructionTests : IDisposable
{
    private readonly string _root=Path.Combine(Path.GetTempPath(),"capture-synthetic-"+Guid.NewGuid().ToString("N"));
    public string Root=>_root;
    [Fact]
    public void RealRgbWithoutDepthProducesMetricSparseGeometryAndExplicitLimitations()
    {
        var input=MakeInput(); Program.Run(input);
        var quality=JObject.Parse(File.ReadAllText(Path.Combine(input.OutputDirectory,"quality_report.json")));
        Assert.Equal("synthetic",(string?)quality["evidence_kind"]);
        Assert.False((bool)quality["depth_used"]!); Assert.False((bool)quality["dense_reconstruction"]!);
        Assert.True((int)quality["points"]!>=12); Assert.True((int)quality["triangles"]!>0);
        Assert.Equal("METRIC",(string?)quality["metric"]?["AccuracyStatus"]);
        Assert.Equal(JTokenType.Null,quality["coverage_percent"]!.Type);
        var points=JObject.Parse(File.ReadAllText(Path.Combine(input.OutputDirectory,"points.json")))["points"]!;
        double medianZ=points.Select(p=>(double)p["position_mm"]![2]!).OrderBy(z=>z).ElementAt(points.Count()/2);
        Assert.InRange(medianZ,1975,2025);
        var glb=File.ReadAllBytes(Path.Combine(input.OutputDirectory,"scan.glb"));
        Assert.Equal(0x46546c67u,BitConverter.ToUInt32(glb,0)); Assert.Equal(glb.Length,(int)BitConverter.ToUInt32(glb,8));
        var parsed=InventorXrSo.Core.Glb.GlbModel.Parse(glb);
        Assert.True(parsed.Primitives.Sum(p=>p.TriangleCount)>0);
        Assert.InRange(parsed.Primitives[0].Positions.Where((v,i)=>i%3==2).Average(),-2.025,-1.975); // glTF metres, Z flipped back on XR import
        Assert.True(File.Exists(Path.Combine(input.OutputDirectory,"result.json")));
        SaveEvidence(input);
    }
    [Fact]
    public void ChangedRawFailsBeforePublishingAResult()
    {
        var input=MakeInput(); File.WriteAllBytes(Path.Combine(input.InputDirectory,"frames","00000000","image.rgb"),new byte[640*480*3]);
        Assert.Equal("CAPTURE_HASH_MISMATCH",Assert.Throws<CaptureSolveException>(()=>Program.Run(input)).Code);
        Assert.False(File.Exists(Path.Combine(input.OutputDirectory,"result.json")));
    }
    [Fact]
    public void MeasuredArucoArtifactAndRedundantScaleBarDistancesEnterMetricSolve()
    {
        var input=MakeInput(true);
        double size=79*2000.0/520,spacing=180*2000.0/520;
        var markers=Enumerable.Range(0,3).Select(i=>new MeasuredMarker(i+2,new[] {
            new[] {i*spacing,0.0,0.0},new[] {i*spacing+size,0.0,0.0},new[] {i*spacing+size,size,0.0},new[] {i*spacing,size,0.0}
        },0.5)).ToArray();
        var artifact=new CalibrationArtifact("measured-bar","1","synthetic ground-truth corner coordinates",DateTimeOffset.UtcNow,markers);
        Program.Run(input with {Artifacts=new[] {artifact}});
        var quality=JObject.Parse(File.ReadAllText(Path.Combine(input.OutputDirectory,"quality_report.json")));
        var constraints=JArray.Parse(File.ReadAllText(Path.Combine(input.OutputDirectory,"constraints.json")));
        Assert.Equal(21,constraints.Count);
        Assert.Equal(3,constraints.Count(c=>c.Value<string>("Id")!.StartsWith("bar:",StringComparison.Ordinal)));
        Assert.DoesNotContain(quality["metric"]!["Constraints"]!,c=>c.Value<string>("Status")=="unresolved");
        Assert.NotEqual("VALIDATED",(string?)quality["metric"]?["AccuracyStatus"]);
    }
    public CaptureSolveInput MakeInput(bool markers=false)
    {
        Directory.CreateDirectory(_root);
        var store=new LocalScanStore(Path.Combine(_root,"raw"),20000000,0,()=>long.MaxValue);
        double[] volume=CaptureTransform.Identity.Values; volume[11]=2000;
        var session=new ScanSession(Guid.NewGuid().ToString("N"),CaptureMode.Object,"synthetic-controlled-plane","1",DateTimeOffset.UtcNow,
            new CaptureVolume(new CaptureTransform(volume),4000,4000,4000),CaptureTransform.Identity);
        var recorder=new ScanRecorder(store,session); recorder.Start();
        using var texture=new Mat(700,1100,MatType.CV_8UC3,new Scalar(180,180,180)); var random=new Random(731);
        for(int i=0;i<1300;i++)
        {
            var color=new Scalar(random.Next(20,240),random.Next(20,240),random.Next(20,240));
            Cv2.Circle(texture,new Point(random.Next(8,1092),random.Next(8,692)),random.Next(2,9),color,-1);
        }
        if(markers)
        {
            using var dictionary=CvAruco.GetPredefinedDictionary(PredefinedDictionaryType.Dict4X4_50);
            for(int i=0;i<3;i++)
            {
                Cv2.Rectangle(texture,new Rect(310+i*180,290,100,100),new Scalar(255,255,255),-1);
                using var image=new Mat();dictionary.GenerateImageMarker(i+2,80,image);
                using var rgb=new Mat();Cv2.CvtColor(image,rgb,ColorConversionCodes.GRAY2RGB);
                using var target=new Mat(texture,new Rect(320+i*180,300,80,80));rgb.CopyTo(target);
            }
        }
        for(int i=0;i<3;i++)
        {
            double x=(i-1)*0.15; int shift=(int)Math.Round(520*x/2);
            using var crop=new Mat(texture,new Rect(200+shift,100,640,480));
            var pixels=new byte[640*480*3];
            for(int row=0;row<480;row++) for(int col=0;col<640;col++)
            {
                var p=crop.At<Vec3b>(row,col); int at=(row*640+col)*3; pixels[at]=p.Item0; pixels[at+1]=p.Item1; pixels[at+2]=p.Item2;
            }
            var pose=CaptureTransform.Identity.Values; pose[3]=x*1000;
            var frame=new CaptureFrame((i+1)*1000000,"synthetic-left",new CaptureIntrinsics(640,480,520,520,320,240),
                new CaptureTransform(pose),new CaptureTransform(pose),CaptureTransform.Identity,new CaptureQuality(120,0.01,100,0.3),true);
            Assert.True(recorder.Record(frame,pixels));
        }
        recorder.Stop();
        return new CaptureSolveInput(session.SessionId,1,Path.Combine(_root,"raw",session.SessionId),Path.Combine(_root,"output"),
            Array.Empty<MetricConstraint>(),Array.Empty<CalibrationArtifact>(),"synthetic");
    }
    public void Dispose(){if(Directory.Exists(_root)) Directory.Delete(_root,true);}
    private static void SaveEvidence(CaptureSolveInput input)
    {
        string? evidence=Environment.GetEnvironmentVariable("XR_SO_CAPTURE_EVIDENCE_DIR"); if(string.IsNullOrWhiteSpace(evidence)) return;
        string root=Path.Combine(Path.GetFullPath(evidence),"synthetic-plane");
        if(Directory.Exists(root)) throw new IOException("Synthetic evidence already exists; select a new evidence directory.");
        void Copy(string source,string target)
        {
            foreach(string file in Directory.EnumerateFiles(source,"*",SearchOption.AllDirectories))
            {
                string destination=Path.Combine(target,Path.GetRelativePath(source,file));Directory.CreateDirectory(Path.GetDirectoryName(destination)!);File.Copy(file,destination);
            }
        }
        Copy(input.InputDirectory,Path.Combine(root,"raw")); Copy(input.OutputDirectory,Path.Combine(root,"output"));
        CaptureJson.WriteNew(Path.Combine(root,"replay-request.json"),input with {InputDirectory=Path.Combine(root,"raw"),OutputDirectory=Path.Combine(root,"replay-output")});
        CaptureJson.WriteNew(Path.Combine(root,"evidence.json"),new {evidence="SYNTHETIC; not physical accuracy validation",ground_truth="textured plane at z=2000 mm; three known camera positions; no depth",
            frames=3,unit="mm",input.ScanId,files=Directory.EnumerateFiles(root,"*",SearchOption.AllDirectories).Select(p=>new {path=Path.GetRelativePath(root,p),
                sha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(p))).ToLowerInvariant()}).ToArray()});
    }
}
