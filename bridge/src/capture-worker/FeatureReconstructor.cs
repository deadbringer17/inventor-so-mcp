using Inventor.So.RealityCapture;
using OpenCvSharp;
using OpenCvSharp.Aruco;
using OpenCvSharp.Features2D;

namespace Inventor.So.Capture.Worker;

public sealed record FeatureReconstruction(List<BundlePoint> Points,List<MetricConstraint> ArtifactConstraints,int MatchedPairs,int LoopPairs);
public static class FeatureReconstructor
{
    private sealed class Features : IDisposable
    {
        public required KeyPoint[] Keys;
        public required byte[] Rgb;
        public required Mat Descriptors;
        public void Dispose()=>Descriptors.Dispose();
    }
    public static FeatureReconstruction Reconstruct(List<InputFrame> frames,RawVolume volume,CalibrationArtifact[] artifacts,CancellationToken ct=default)
    {
        var features=new List<Features>(); var groups=new Dictionary<(int frame,int key),int>();
        var tracks=new List<List<Observation>>(); int matchedPairs=0,loopPairs=0;
        try
        {
            using var sift=SIFT.Create(nFeatures:1500); using var matcher=new BFMatcher(NormTypes.L2);
            for(int i=0;i<frames.Count;i++)
            {
                ct.ThrowIfCancellationRequested(); var camera=frames[i].Camera;
                byte[] rgb=File.ReadAllBytes(frames[i].ImagePath);
                using var image=Mat.FromPixelData(camera.Height,camera.Width,MatType.CV_8UC3,rgb);
                using var grey=new Mat(); Cv2.CvtColor(image,grey,ColorConversionCodes.RGB2GRAY);
                var descriptors=new Mat(); sift.DetectAndCompute(grey,null,out var keys,descriptors);
                features.Add(new Features { Keys=keys,Rgb=rgb,Descriptors=descriptors });
            }
            var parents=new List<int>();
            int Root(int i) { while(parents[i]!=i) { parents[i]=parents[parents[i]]; i=parents[i]; } return i; }
            int Group(int frame,int key)
            {
                if(groups.TryGetValue((frame,key),out int known)) return Root(known);
                int id=tracks.Count; groups.Add((frame,key),id); parents.Add(id);
                var p=features[frame].Keys[key].Pt; tracks.Add(new List<Observation> {new(frame,p.X,p.Y)}); return id;
            }
            for(int a=0;a<frames.Count;a++)
            for(int b=a+1;b<frames.Count;b++)
            {
                ct.ThrowIfCancellationRequested();
                bool loop=b-a>6;
                if(loop && a%10!=0 && b!=frames.Count-1) continue;
                if(features[a].Descriptors.Empty() || features[b].Descriptors.Empty() || (frames[a].Camera.Centre-frames[b].Camera.Centre).Norm<0.025) continue;
                var forward=matcher.KnnMatch(features[a].Descriptors,features[b].Descriptors,2);
                var backward=matcher.KnnMatch(features[b].Descriptors,features[a].Descriptors,2);
                int accepted=0;
                foreach(var pair in forward)
                {
                    if(pair.Length<2 || pair[0].Distance>=0.72*pair[1].Distance) continue;
                    var m=pair[0]; var reverse=backward[m.TrainIdx];
                    if(reverse.Length<2 || reverse[0].TrainIdx!=m.QueryIdx || reverse[0].Distance>=0.72*reverse[1].Distance) continue;
                    var pa=features[a].Keys[m.QueryIdx].Pt; var pb=features[b].Keys[m.TrainIdx].Pt;
                    if(!TryTriangulate(frames[a].Camera,pa.X,pa.Y,frames[b].Camera,pb.X,pb.Y,out _)) continue;
                    int ga=Group(a,m.QueryIdx),gb=Group(b,m.TrainIdx);
                    if(ga!=gb)
                    {
                        if(tracks[ga].Any(o=>tracks[gb].Any(p=>p.Camera==o.Camera))) continue;
                        parents[gb]=ga; tracks[ga].AddRange(tracks[gb]); tracks[gb].Clear();
                    }
                    accepted++;
                }
                if(accepted>=8) { matchedPairs++; if(loop) loopPairs++; }
            }
            var points=new List<BundlePoint>();
            for(int i=0;i<tracks.Count;i++)
            {
                if(tracks[i].Count<2) continue;
                var obs=tracks[i].OrderBy(o=>o.Camera).ToList();
                var point=TriangulateTrack(frames,obs);
                if(point==null || !RawCaptureReader.Inside(volume,point.Value)) continue;
                var first=obs[0]; var camera=frames[first.Camera].Camera;
                int pixel=(Math.Clamp((int)Math.Round(first.V),0,camera.Height-1)*camera.Width+Math.Clamp((int)Math.Round(first.U),0,camera.Width-1))*3;
                byte[] color=features[first.Camera].Rgb.Skip(pixel).Take(3).ToArray();
                // Stable image-pixel identity survives repeated solves of unchanged immutable raw.
                string id=$"f{camera.Sequence:D8}.u{first.U:F3}.v{first.V:F3}".Replace(',','.');
                points.Add(new BundlePoint(id,point.Value,color,obs));
            }
            var artifactConstraints=AddMarkers(frames,features,artifacts,points,ct);
            points=points.GroupBy(p=>p.Id,StringComparer.Ordinal).Select(g=>g.OrderByDescending(p=>p.Observations.Count).First()).ToList();
            var selected=points.Where(p=>p.Id.StartsWith("marker:",StringComparison.Ordinal)).Concat(points.Where(p=>!p.Id.StartsWith("marker:",StringComparison.Ordinal))
                .OrderByDescending(p=>p.Observations.Count).ThenBy(p=>p.Id,StringComparer.Ordinal)).Take(2000).ToList();
            if(selected.Count<12 || matchedPairs==0) throw new CaptureSolveException("CAPTURE_INSUFFICIENT_FEATURES");
            return new(selected,artifactConstraints,matchedPairs,loopPairs);
        }
        finally { foreach(var f in features) f.Dispose(); }
    }

    private static List<MetricConstraint> AddMarkers(List<InputFrame> frames,List<Features> features,CalibrationArtifact[] artifacts,
        List<BundlePoint> points,CancellationToken ct)
    {
        var constraints=new List<MetricConstraint>();
        if(artifacts.Length==0) return constraints;
        foreach(var artifact in artifacts) artifact.Validate();
        var ids=artifacts.SelectMany(a=>a.Markers.Select(m=>(artifact:a,marker:m))).ToArray();
        if(ids.Select(m=>m.marker.MarkerId).Distinct().Count()!=ids.Length) throw new CaptureSolveException("CAPTURE_MARKER_ID_CONFLICT");
        using var dictionary=CvAruco.GetPredefinedDictionary(PredefinedDictionaryType.Dict4X4_50);
        using var detector=new ArucoDetector(dictionary);
        var observations=new Dictionary<int,List<Observation>[]>();
        foreach(var item in ids) observations.Add(item.marker.MarkerId,Enumerable.Range(0,4).Select(_=>new List<Observation>()).ToArray());
        for(int i=0;i<frames.Count;i++)
        {
            ct.ThrowIfCancellationRequested(); var camera=frames[i].Camera;
            using var rgb=Mat.FromPixelData(camera.Height,camera.Width,MatType.CV_8UC3,features[i].Rgb);
            using var grey=new Mat(); Cv2.CvtColor(rgb,grey,ColorConversionCodes.RGB2GRAY);
            detector.DetectMarkers(grey,out var corners,out var detected,out _);
            for(int j=0;j<detected.Length;j++)
                if(observations.TryGetValue(detected[j],out var list))
                    for(int k=0;k<4;k++) list[k].Add(new Observation(i,corners[j][k].X,corners[j][k].Y));
        }
        foreach(var item in ids)
        {
            string Name(int corner)=>$"marker:{item.artifact.Id}:{item.marker.MarkerId}:{corner}";
            for(int k=0;k<4;k++)
            {
                var track=observations[item.marker.MarkerId][k]; var point=TriangulateTrack(frames,track);
                if(point.HasValue) points.Add(new BundlePoint(Name(k),point.Value,new byte[] {255,180,0},track));
            }
            // Actual measured corner coordinates have priority. No nominal printed marker size is used.
            foreach(var pair in new[] {(0,1),(1,2),(2,3),(3,0),(0,2),(1,3)})
            {
                double length=(V3.From(item.marker.CornersMm[pair.Item1])-V3.From(item.marker.CornersMm[pair.Item2])).Norm;
                constraints.Add(new MetricConstraint($"cal:{item.artifact.Id}:{item.marker.MarkerId}:{pair.Item1}{pair.Item2}","distance",
                    new[] {Name(pair.Item1),Name(pair.Item2)},length,item.marker.UncertaintyMm*Math.Sqrt(2),item.artifact.Role,true,item.artifact.Independent,
                    "artifact:"+item.artifact.Id,"artifact:"+item.artifact.Id));
            }
        }
        foreach(var artifact in artifacts)
        for(int a=0;a<artifact.Markers.Length;a++)
        for(int b=a+1;b<artifact.Markers.Length;b++)
        {
            var left=artifact.Markers[a]; var right=artifact.Markers[b];
            double distance=(V3.From(left.CornersMm[0])-V3.From(right.CornersMm[0])).Norm;
            constraints.Add(new MetricConstraint($"bar:{artifact.Id}:{left.MarkerId}:{right.MarkerId}","distance",
                new[] {$"marker:{artifact.Id}:{left.MarkerId}:0",$"marker:{artifact.Id}:{right.MarkerId}:0"},distance,
                Math.Sqrt(left.UncertaintyMm*left.UncertaintyMm+right.UncertaintyMm*right.UncertaintyMm),artifact.Role,true,artifact.Independent,
                "artifact:"+artifact.Id,"artifact:"+artifact.Id));
        }
        return constraints;
    }
    private static V3? TriangulateTrack(List<InputFrame> frames,List<Observation> obs)
    {
        double baseline=0; V3? point=null;
        for(int a=0;a<obs.Count;a++) for(int b=a+1;b<obs.Count;b++)
        {
            var ca=frames[obs[a].Camera].Camera; var cb=frames[obs[b].Camera].Camera;
            double distance=(ca.Centre-cb.Centre).Norm;
            if(distance>baseline && TryTriangulate(ca,obs[a].U,obs[a].V,cb,obs[b].U,obs[b].V,out var value)) { baseline=distance; point=value; }
        }
        if(point==null) return null;
        foreach(var o in obs)
        {
            var p=frames[o.Camera].Camera.Project(point.Value);
            if(p.z<=0.005 || Math.Sqrt((p.u-o.U)*(p.u-o.U)+(p.v-o.V)*(p.v-o.V))>4) return null;
        }
        return point;
    }
    public static bool TryTriangulate(BundleCamera a,double au,double av,BundleCamera b,double bu,double bv,out V3 point)
    {
        point=default; var u=a.Ray(au,av); var v=b.Ray(bu,bv); var w=a.Centre-b.Centre;
        double dot=V3.Dot(u,v),denom=1-dot*dot;
        if(denom<0.0001) return false;
        double s=(dot*V3.Dot(v,w)-V3.Dot(u,w))/denom,t=(V3.Dot(v,w)-dot*V3.Dot(u,w))/denom;
        if(s<0.02 || t<0.02 || s>100 || t>100) return false;
        var p=a.Centre+u*s; var q=b.Centre+v*t; point=(p+q)/2;
        if((p-q).Norm>0.02) return false;
        var ap=a.Project(point); var bp=b.Project(point);
        return ap.z>0.005 && bp.z>0.005 && Math.Abs(ap.u-au)+Math.Abs(ap.v-av)<3 && Math.Abs(bp.u-bu)+Math.Abs(bp.v-bv)<3;
    }
}
