using Inventor.So.RealityCapture;

namespace Inventor.So.Capture.Worker;

public sealed record Observation(int Camera, double U, double V);
public sealed class BundlePoint(string id,V3 position,byte[] color,List<Observation> observations)
{
    public string Id { get; } = id;
    public V3 Position { get; set; } = position;
    public byte[] Color { get; } = color;
    public List<Observation> Observations { get; } = observations;
}
public sealed class BundleCamera(int sequence,int width,int height,double fx,double fy,double cx,double cy,double[] pose)
{
    public int Sequence { get; } = sequence;
    public int Width { get; } = width;
    public int Height { get; } = height;
    public double Fx { get; } = fx;
    public double Fy { get; } = fy;
    public double Cx { get; } = cx;
    public double Cy { get; } = cy;
    public double[] Pose { get; set; } = pose;
    public double[] Prior { get; } = (double[])pose.Clone();
    public V3 Centre => -Rotation.ApplyTranspose(Rotation.FromVector(Pose),new V3(Pose[3],Pose[4],Pose[5]));
    public V3 Ray(double u,double v) => Rotation.ApplyTranspose(Rotation.FromVector(Pose),new V3((u-Cx)/Fx,(v-Cy)/Fy,1)).Unit;
    public (double u,double v,double z) Project(V3 point)
    {
        var p = Rotation.Apply(Rotation.FromVector(Pose),point)+new V3(Pose[3],Pose[4],Pose[5]);
        return (Fx*p.X/p.Z+Cx,Fy*p.Y/p.Z+Cy,p.Z);
    }
}

public static class Rotation
{
    public static double[] FromVector(double[] pose)
    {
        var w = new V3(pose[0],pose[1],pose[2]); double angle=w.Norm;
        if (angle < 1e-12) return new double[] { 1,-w.Z,w.Y,w.Z,1,-w.X,-w.Y,w.X,1 };
        var n=w/angle; double c=Math.Cos(angle),s=Math.Sin(angle),k=1-c;
        return new[] { c+n.X*n.X*k,n.X*n.Y*k-n.Z*s,n.X*n.Z*k+n.Y*s,
            n.Y*n.X*k+n.Z*s,c+n.Y*n.Y*k,n.Y*n.Z*k-n.X*s,n.Z*n.X*k-n.Y*s,n.Z*n.Y*k+n.X*s,c+n.Z*n.Z*k };
    }
    public static V3 ToVector(double[] m)
    {
        double a=Math.Acos(Math.Clamp((m[0]+m[4]+m[8]-1)/2,-1,1));
        if (a<1e-10) return new V3((m[7]-m[5])/2,(m[2]-m[6])/2,(m[3]-m[1])/2);
        if (Math.Abs(Math.PI-a)<1e-5)
        {
            var axis=new V3(Math.Sqrt(Math.Max(0,(m[0]+1)/2)),Math.CopySign(Math.Sqrt(Math.Max(0,(m[4]+1)/2)),m[1]+m[3]),
                Math.CopySign(Math.Sqrt(Math.Max(0,(m[8]+1)/2)),m[2]+m[6]));
            return axis.Unit*a;
        }
        return new V3(m[7]-m[5],m[2]-m[6],m[3]-m[1])*(a/(2*Math.Sin(a)));
    }
    public static V3 Apply(double[] m,V3 v) => new(m[0]*v.X+m[1]*v.Y+m[2]*v.Z,m[3]*v.X+m[4]*v.Y+m[5]*v.Z,m[6]*v.X+m[7]*v.Y+m[8]*v.Z);
    public static V3 ApplyTranspose(double[] m,V3 v) => new(m[0]*v.X+m[3]*v.Y+m[6]*v.Z,m[1]*v.X+m[4]*v.Y+m[7]*v.Z,m[2]*v.X+m[5]*v.Y+m[8]*v.Z);
}
