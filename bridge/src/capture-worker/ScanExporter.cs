using System.Globalization;
using System.Text;
using Inventor.So.RealityCapture;
using Newtonsoft.Json;
using OpenCvSharp;

namespace Inventor.So.Capture.Worker;

public static class ScanExporter
{
    public static int[][] BuildSparseSurface(IReadOnlyList<BundleCamera> cameras,IReadOnlyList<BundlePoint> points)
    {
        var triangles=new List<int[]>(); var seen=new HashSet<string>(StringComparer.Ordinal);
        for(int ci=0;ci<cameras.Count;ci++)
        {
            using var subdivision=new Subdiv2D(new Rect(0,0,cameras[ci].Width,cameras[ci].Height));
            var pixelIndex=new Dictionary<(int,int),int>();
            for(int pi=0;pi<points.Count;pi++)
            {
                var o=points[pi].Observations.FirstOrDefault(o=>o.Camera==ci);
                if(o==null || o.U<1 || o.V<1 || o.U>=cameras[ci].Width-1 || o.V>=cameras[ci].Height-1) continue;
                var key=((int)Math.Round(o.U*10),(int)Math.Round(o.V*10));
                if(pixelIndex.TryAdd(key,pi)) subdivision.Insert(new Point2f((float)o.U,(float)o.V));
            }
            if(pixelIndex.Count<3) continue;
            var candidates=new List<(int[] ids,double edge)>();
            foreach(var triangle in subdivision.GetTriangleList())
            {
                if(!pixelIndex.TryGetValue(((int)Math.Round(triangle.Item0*10),(int)Math.Round(triangle.Item1*10)),out int a) ||
                    !pixelIndex.TryGetValue(((int)Math.Round(triangle.Item2*10),(int)Math.Round(triangle.Item3*10)),out int b) ||
                    !pixelIndex.TryGetValue(((int)Math.Round(triangle.Item4*10),(int)Math.Round(triangle.Item5*10)),out int c) || a==b || b==c || a==c) continue;
                var pa=points[a].Position; var pb=points[b].Position; var pc=points[c].Position;
                double ab=(pa-pb).Norm,bc=(pb-pc).Norm,ca=(pc-pa).Norm,edge=Math.Max(ab,Math.Max(bc,ca));
                if(Math.Min(ab,Math.Min(bc,ca))<0.0001 || V3.Cross(pb-pa,pc-pa).Norm/(edge*edge)<0.05) continue;
                var normal=V3.Cross(pb-pa,pc-pa).Unit;
                // Reject grazing triangles and large depth discontinuities instead of filling an unobserved hull.
                if(Math.Abs(V3.Dot(normal,(cameras[ci].Centre-pa).Unit))<0.2) continue;
                if(V3.Dot(normal,cameras[ci].Centre-pa)<0) (b,c)=(c,b);
                candidates.Add((new[] {a,b,c},edge));
            }
            if(candidates.Count==0) continue;
            double median=candidates.Select(t=>t.edge).OrderBy(x=>x).ElementAt(candidates.Count/2);
            foreach(var triangle in candidates.Where(t=>t.edge<=Math.Min(0.3,median*2.5)))
            {
                string key=string.Join(",",triangle.ids.OrderBy(v=>v));
                if(seen.Add(key)) triangles.Add(triangle.ids);
            }
        }
        return triangles.ToArray();
    }
    public static void Export(string directory,IReadOnlyList<BundlePoint> points,int[][] triangles,RawSession session)
    {
        string Number(double v)=>v.ToString("R",CultureInfo.InvariantCulture);
        V3 Mm(BundlePoint p)=>new(p.Position.X*1000,-p.Position.Y*1000,p.Position.Z*1000);
        using(var ply=new StreamWriter(new FileStream(Path.Combine(directory,"pointcloud.ply"),FileMode.CreateNew),Encoding.ASCII))
        {
            ply.WriteLine($"ply\nformat ascii 1.0\ncomment unit millimetre; Unity left-handed Y-up; geometry estimated\nelement vertex {points.Count}\nproperty double x\nproperty double y\nproperty double z\nproperty uchar red\nproperty uchar green\nproperty uchar blue\nend_header");
            foreach(var p in points) { var v=Mm(p); ply.WriteLine($"{Number(v.X)} {Number(v.Y)} {Number(v.Z)} {p.Color[0]} {p.Color[1]} {p.Color[2]}"); }
        }
        using(var obj=new StreamWriter(new FileStream(Path.Combine(directory,"scan.obj"),FileMode.CreateNew),Encoding.ASCII))
        {
            obj.WriteLine("# unit millimetre; Unity left-handed Y-up; sparse observed surface, not dense reconstruction");
            foreach(var p in points) { var v=Mm(p); obj.WriteLine($"v {Number(v.X)} {Number(v.Y)} {Number(v.Z)}"); }
            foreach(var t in triangles) obj.WriteLine($"f {t[0]+1} {t[2]+1} {t[1]+1}");
        }
        WriteGlb(Path.Combine(directory,"scan.glb"),points,triangles);
        CaptureJson.WriteNew(Path.Combine(directory,"points.json"),new { unit="mm",coordinate_space="scan_metric",handedness="Unity left-handed Y-up",
            points=points.Select(p=>new {id=p.Id,position_mm=Mm(p).Array,observations=p.Observations.Count,geometry="estimated"}) });
        CaptureJson.WriteNew(Path.Combine(directory,"units.json"),new { pointcloud_ply="mm; Unity left-handed Y-up",scan_obj="mm; Unity left-handed Y-up",
            scan_glb="metre; glTF right-handed Y-up",surface="sparse observed triangles; no watertight or dense coverage claim",
            matrix_convention="row-major; column vectors",
            scan_metric_to_capture_mm=new double[] {1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1},
            capture_to_tracking_mm=session.CaptureToTrackingMm.Values,
            scan_metric_mm_to_gltf_metres=new double[] {.001,0,0,0,0,.001,0,0,0,0,-.001,0,0,0,0,1} });
    }
    private static void WriteGlb(string path,IReadOnlyList<BundlePoint> points,int[][] triangles)
    {
        using var binary=new MemoryStream(); using var data=new BinaryWriter(binary,Encoding.UTF8,true);
        var min=new[] {double.MaxValue,double.MaxValue,double.MaxValue}; var max=new[] {double.MinValue,double.MinValue,double.MinValue};
        foreach(var p in points)
        {
            // CV world Y-down -> glTF Y-up and Z-back; two sign changes preserve handedness.
            double[] vertex={p.Position.X,-p.Position.Y,-p.Position.Z};
            for(int k=0;k<3;k++) { float value=(float)vertex[k];data.Write(value);min[k]=Math.Min(min[k],value);max[k]=Math.Max(max[k],value); }
        }
        int positionBytes=(int)binary.Length;
        foreach(var t in triangles) foreach(int i in t) data.Write((uint)i);
        int indexBytes=(int)binary.Length-positionBytes;
        var views=new List<object> {new {buffer=0,byteOffset=0,byteLength=positionBytes,target=34962}};
        var accessors=new List<object> {new {bufferView=0,componentType=5126,count=points.Count,type="VEC3",min,max}};
        var primitive=new Dictionary<string,object> { ["attributes"]=new {POSITION=0},["mode"]=triangles.Length==0?0:4 };
        if(triangles.Length>0)
        {
            views.Add(new {buffer=0,byteOffset=positionBytes,byteLength=indexBytes,target=34963});
            accessors.Add(new {bufferView=1,componentType=5125,count=triangles.Length*3,type="SCALAR"}); primitive["indices"]=1;
        }
        var document=new { asset=new {version="2.0",generator="Inventor SO Reality Capture experimental"},scene=0,scenes=new[] {new {nodes=new[] {0}}},
            nodes=new[] {new {mesh=0}},meshes=new[] {new {primitives=new[] {primitive}}},buffers=new[] {new {byteLength=(int)binary.Length}},bufferViews=views,accessors };
        byte[] json=Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(document)); int padded=(json.Length+3)&~3;
        using var output=new BinaryWriter(new FileStream(path,FileMode.CreateNew));
        output.Write(0x46546c67u); output.Write(2u); output.Write((uint)(12+8+padded+8+binary.Length));
        output.Write((uint)padded); output.Write(0x4e4f534au); output.Write(json); for(int i=json.Length;i<padded;i++) output.Write((byte)32);
        output.Write((uint)binary.Length); output.Write(0x004e4942u); output.Write(binary.ToArray());
    }
}
