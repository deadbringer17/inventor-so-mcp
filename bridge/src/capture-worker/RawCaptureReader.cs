using System.Security.Cryptography;
using Inventor.So.RealityCapture;

namespace Inventor.So.Capture.Worker;

public sealed record RawTransform(double[] Values);
public sealed record RawVolume(RawTransform VolumeToCaptureMm,double WidthMm,double HeightMm,double DepthMm);
public sealed record RawSession(int SchemaVersion,string SessionId,RawVolume Volume,RawTransform CaptureToTrackingMm,string Device);
public sealed record RawIntrinsics(int Width,int Height,double Fx,double Fy,double Cx,double Cy);
public sealed record RawFrame(long TimestampUnixMicroseconds,RawIntrinsics Intrinsics,RawTransform CameraToCaptureMm,bool TrackingValid,string PixelConvention);
public sealed record RawStoredFrame(int Sequence,RawFrame Frame,string Sha256,long Size,string Encoding);
public sealed record InputFrame(BundleCamera Camera,string ImagePath,RawStoredFrame Metadata);

public static class RawCaptureReader
{
    public static (RawSession session,List<InputFrame> frames,int totalFrames) Read(CaptureSolveInput input)
    {
        var session=CaptureJson.Read<RawSession>(Path.Combine(input.InputDirectory,"capture_manifest.json"),1024*1024);
        if(session.SchemaVersion!=1 || session.SessionId!=input.ScanId || session.Volume==null) throw new CaptureSolveException("CAPTURE_SCHEMA_UNSUPPORTED");
        ValidateTransform(session.CaptureToTrackingMm); ValidateTransform(session.Volume.VolumeToCaptureMm);
        if(new[] {session.Volume.WidthMm,session.Volume.HeightMm,session.Volume.DepthMm}.Any(v=>!double.IsFinite(v)||v<10||v>100000))
            throw new CaptureSolveException("CAPTURE_METADATA_INVALID");
        var directories=Directory.EnumerateDirectories(Path.Combine(input.InputDirectory,"frames")).OrderBy(p=>p,StringComparer.Ordinal).ToArray();
        if(directories.Length<2 || directories.Length>5000) throw new CaptureSolveException("CAPTURE_FRAME_COUNT_INVALID");
        var selected=Enumerable.Range(0,Math.Min(60,directories.Length)).Select(i=>(int)Math.Round(i*(directories.Length-1.0)/(Math.Min(60,directories.Length)-1))).ToHashSet();
        var frames=new List<InputFrame>(); long previous=0;
        for(int i=0;i<directories.Length;i++)
        {
            if(Path.GetFileName(directories[i])!=i.ToString("D8")) throw new CaptureSolveException("CAPTURE_SEQUENCE_INVALID");
            var stored=CaptureJson.Read<RawStoredFrame>(Path.Combine(directories[i],"frame.json"),1024*1024);
            var raw=stored.Frame; var k=raw?.Intrinsics;
            if(stored.Sequence!=i || raw==null || k==null || stored.Encoding!="rgb8" || !raw.TrackingValid || raw.TimestampUnixMicroseconds<=previous ||
                k.Width is <3 or >2048 || k.Height is <3 or >2048 || new[] {k.Fx,k.Fy,k.Cx,k.Cy}.Any(v=>!double.IsFinite(v)) || k.Fx<=0 || k.Fy<=0 ||
                k.Cx<0 || k.Cx>k.Width || k.Cy<0 || k.Cy>k.Height || stored.Size!=k.Width*k.Height*3L ||
                raw.PixelConvention!="RGB8; top-left origin; x right, y down; camera pose x right, y up, z forward")
                throw new CaptureSolveException("CAPTURE_METADATA_INVALID");
            previous=raw.TimestampUnixMicroseconds; ValidateTransform(raw.CameraToCaptureMm);
            string image=Path.Combine(directories[i],"image.rgb");
            using(var file=File.OpenRead(image))
                if(file.Length!=stored.Size || Convert.ToHexString(SHA256.HashData(file)).ToLowerInvariant()!=stored.Sha256)
                    throw new CaptureSolveException("CAPTURE_HASH_MISMATCH");
            if(!selected.Contains(i)) continue;
            var m=raw.CameraToCaptureMm.Values;
            // Change both world and camera basis: Unity left-handed Y-up -> CV right-handed Y-down.
            double[] sign={1,-1,1}; var r=new double[9];
            for(int row=0;row<3;row++) for(int col=0;col<3;col++) r[row*3+col]=m[col*4+row]*sign[row]*sign[col];
            var centre=new V3(m[3]/1000,-m[7]/1000,m[11]/1000); var t=-Rotation.Apply(r,centre); var w=Rotation.ToVector(r);
            frames.Add(new InputFrame(new BundleCamera(i,k.Width,k.Height,k.Fx,k.Fy,k.Cx,k.Cy,new[] {w.X,w.Y,w.Z,t.X,t.Y,t.Z}),image,stored));
        }
        return(session,frames,directories.Length);
    }
    public static bool Inside(RawVolume volume,V3 cvPointMetres)
    {
        var p=new V3(cvPointMetres.X*1000,-cvPointMetres.Y*1000,cvPointMetres.Z*1000); var m=volume.VolumeToCaptureMm.Values;
        var delta=p-new V3(m[3],m[7],m[11]);
        double x=m[0]*delta.X+m[4]*delta.Y+m[8]*delta.Z, y=m[1]*delta.X+m[5]*delta.Y+m[9]*delta.Z,z=m[2]*delta.X+m[6]*delta.Y+m[10]*delta.Z;
        return Math.Abs(x)<=volume.WidthMm/2 && Math.Abs(y)<=volume.HeightMm/2 && Math.Abs(z)<=volume.DepthMm/2;
    }
    public static void ValidateTransform(RawTransform? transform)
    {
        var m=transform?.Values;
        if(m==null || m.Length!=16 || m.Any(v=>!double.IsFinite(v)) || Math.Abs(m[12])+Math.Abs(m[13])+Math.Abs(m[14])+Math.Abs(m[15]-1)>1e-6)
            throw new CaptureSolveException("CAPTURE_TRANSFORM_INVALID");
        for(int a=0;a<3;a++) for(int b=0;b<3;b++)
        {
            double dot=0; for(int row=0;row<3;row++) dot+=m[row*4+a]*m[row*4+b];
            if(Math.Abs(dot-(a==b?1:0))>1e-4) throw new CaptureSolveException("CAPTURE_TRANSFORM_INVALID");
        }
        double det=m[0]*(m[5]*m[10]-m[6]*m[9])-m[1]*(m[4]*m[10]-m[6]*m[8])+m[2]*(m[4]*m[9]-m[5]*m[8]);
        if(Math.Abs(det-1)>1e-4) throw new CaptureSolveException("CAPTURE_TRANSFORM_INVALID");
    }
}
