using Inventor.So.RealityCapture;

namespace Inventor.So.Capture.Worker;

public static class Program
{
    public static int Main(string[] args)
    {
        if(args.Length!=1 || !Path.IsPathFullyQualified(args[0])) { Console.Error.WriteLine("CAPTURE_REQUEST_INVALID"); return 2; }
        try { Run(CaptureJson.Read<CaptureSolveInput>(args[0],1024*1024)); return 0; }
        catch(CaptureSolveException failure) { Console.Out.WriteLine(failure.Code); return 3; }
        catch(Exception) { Console.Out.WriteLine("CAPTURE_WORKER_FAILED"); return 4; }
    }
    public static void Run(CaptureSolveInput input,CancellationToken ct=default)
    {
        OpenCvSharp.Cv2.SetNumThreads(Math.Clamp(Environment.ProcessorCount,1,4));
        if(!Guid.TryParseExact(input.ScanId,"N",out _) || input.Revision is <1 or >10000 || !Path.IsPathFullyQualified(input.InputDirectory) ||
            !Path.IsPathFullyQualified(input.OutputDirectory) || Path.GetFullPath(input.InputDirectory)==Path.GetFullPath(input.OutputDirectory) ||
            input.Constraints==null || input.Constraints.Length>256 || input.Artifacts==null || input.Artifacts.Length>16 ||
            input.EvidenceKind is not ("unverified" or "synthetic" or "physical_controlled")) throw new CaptureSolveException("CAPTURE_REQUEST_INVALID");
        foreach(var c in input.Constraints) c.Validate(); foreach(var a in input.Artifacts) a.Validate();
        if(input.Constraints.Select(c=>c.Id).Distinct(StringComparer.Ordinal).Count()!=input.Constraints.Length) throw new CaptureSolveException("CAPTURE_CONSTRAINT_INVALID");
        Directory.CreateDirectory(input.OutputDirectory);
        if(Directory.EnumerateFileSystemEntries(input.OutputDirectory).Any()) throw new CaptureSolveException("CAPTURE_OUTPUT_EXISTS");
        var (session,frames,total)=RawCaptureReader.Read(input);
        var result=FeatureReconstructor.Reconstruct(frames,session.Volume,input.Artifacts,ct);
        var constraints=input.Constraints.Concat(result.ArtifactConstraints).ToArray();
        var points=result.Points; var cameras=frames.Select(f=>f.Camera).ToArray();
        foreach(var c in input.Constraints.Where(c=>c.Enabled))
            if(c.PointIds.Any(id=>points.All(p=>p.Id!=id))) throw new CaptureSolveException("CAPTURE_REFERENCE_NOT_FOUND");
        var optimization=ConstrainedBundleAdjuster.Solve(cameras,points,constraints,ct:ct);
        var geometry=points.ToDictionary(p=>p.Id,p=>new V3(p.Position.X*1000,-p.Position.Y*1000,p.Position.Z*1000),StringComparer.Ordinal);
        bool synthetic=input.EvidenceKind=="synthetic" || session.Device.StartsWith("synthetic",StringComparison.OrdinalIgnoreCase);
        var quality=MetricQualityEvaluator.Evaluate(constraints,geometry,synthetic,optimization.Converged,input.EvidenceKind=="physical_controlled");
        var triangles=ScanExporter.BuildSparseSurface(cameras,points);
        ScanExporter.Export(input.OutputDirectory,points,triangles,session);
        CaptureJson.WriteNew(Path.Combine(input.OutputDirectory,"quality_report.json"),new {
            scan_id=input.ScanId,revision=input.Revision,algorithm="SIFT + reciprocal matching + pose-initialized multi-view triangulation + joint constrained sparse LM",
            evidence_kind=synthetic?"synthetic":input.EvidenceKind,frames_total=total,frames_used=frames.Count,points=points.Count,
            triangles=triangles.Length,matched_pairs=result.MatchedPairs,loop_pairs=result.LoopPairs,optimization,metric=quality,
            coverage_percent=(double?)null,dense_reconstruction=false,depth_used=false,
            limitations=new[] {"Sparse feature surface; dense MVS and geometric coverage are not implemented", "Tracking priors are heuristic (20 mm / 0.03 rad), not sensor-calibrated covariance",
                "Artifact corner distances share measurements; their residuals are correlated", "Physical validation bench pending; synthetic tests do not establish real-world accuracy"} });
        CaptureJson.WriteNew(Path.Combine(input.OutputDirectory,"constraints.json"),constraints);
        CaptureJson.WriteNew(Path.Combine(input.OutputDirectory,"optimized_cameras.json"),new {unit="metre",coordinate_space="CV right-handed Y-down",cameras=cameras.Select(c=>new {sequence=c.Sequence,world_to_camera_rodrigues_translation=c.Pose})});
        // Last file is the commit marker: partial outputs from a crashed worker are never published by the host.
        CaptureJson.WriteNew(Path.Combine(input.OutputDirectory,"result.json"),new {scan_id=input.ScanId,revision=input.Revision,ok=true,
            artifacts=new[] {"pointcloud.ply","scan.obj","scan.glb","points.json","quality_report.json","constraints.json","optimized_cameras.json","units.json"} });
    }
}
