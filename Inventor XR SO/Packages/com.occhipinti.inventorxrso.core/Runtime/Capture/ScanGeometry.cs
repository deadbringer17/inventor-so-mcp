#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

#if CAPTURE_HOST
namespace Inventor.So.RealityCapture.Review
#else
namespace InventorXrSo.Core.Capture
#endif
{
    /// <summary>All coordinates and residuals are in scan_metric millimetres, independent of display scale.</summary>
    public sealed class ScanPoint
    {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("position_mm")] public double[] Position { get; set; }
        [JsonProperty("observations")] public int Observations { get; set; }
    }
    public sealed class ScanPrimitive
    {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("kind")] public string Kind { get; set; }
        [JsonProperty("origin_mm")] public double[] Origin { get; set; }
        [JsonProperty("direction")] public double[] Direction { get; set; }
        [JsonProperty("radius_mm")] public double Radius { get; set; }
        [JsonProperty("length_mm")] public double Length { get; set; }
        [JsonProperty("support_ids")] public string[] SupportIds { get; set; }
        [JsonProperty("constraint_ids")] public string[] ConstraintIds { get; set; } = Array.Empty<string>();
        [JsonProperty("fit_rms_mm")] public double FitRms { get; set; }
        [JsonProperty("uncertainty_mm")] public double? Uncertainty => null;
        [JsonProperty("status")] public string Status => "estimated";
        [JsonProperty("coordinate_space")] public string CoordinateSpace => "scan_metric";
        [JsonProperty("unit")] public string Unit => "mm";
    }

    /// <summary>Least squares fits of an explicitly selected region. RMS describes support scatter, not measurement uncertainty.</summary>
    public static class ScanGeometry
    {
        private struct V
        {
            public double X,Y,Z;
            public V(double x,double y,double z) { X=x;Y=y;Z=z; }
            public V(double[] p) : this(p[0],p[1],p[2]) { }
            public double Norm => Math.Sqrt(Dot(this,this));
            public V Unit => Norm>1e-12 ? this/Norm : throw new ArgumentException("Degenerate geometry.");
            public double[] Array => new[]{X,Y,Z};
            public static V operator +(V a,V b)=>new V(a.X+b.X,a.Y+b.Y,a.Z+b.Z);
            public static V operator -(V a,V b)=>new V(a.X-b.X,a.Y-b.Y,a.Z-b.Z);
            public static V operator *(V a,double b)=>new V(a.X*b,a.Y*b,a.Z*b);
            public static V operator /(V a,double b)=>a*(1/b);
        }
        private static double Dot(V a,V b)=>a.X*b.X+a.Y*b.Y+a.Z*b.Z;
        private static V Cross(V a,V b)=>new V(a.Y*b.Z-a.Z*b.Y,a.Z*b.X-a.X*b.Z,a.X*b.Y-a.Y*b.X);
        public static bool Finite(double n)=>!double.IsNaN(n)&&!double.IsInfinity(n);
        public static void Validate(IReadOnlyList<ScanPoint> points)
        {
            if(points==null || points.Count<1 || points.Count>2000 || points.Any(p=>p==null || string.IsNullOrEmpty(p.Id) || p.Id.Length>120 ||
                p.Position==null || p.Position.Length!=3 || p.Position.Any(n=>!Finite(n)||Math.Abs(n)>1e7)) || points.Select(p=>p.Id).Distinct().Count()!=points.Count)
                throw new ArgumentException("Invalid scan points.");
        }
        public static double Distance(double[] a,double[] b) => (new V(a)-new V(b)).Norm;
        public static double PointPlane(double[] point,ScanPrimitive plane)
        { if(plane.Kind!="plane") throw new ArgumentException("Plane required."); return Math.Abs(Dot(new V(point)-new V(plane.Origin),new V(plane.Direction).Unit)); }
        public static double AngleDegrees(ScanPrimitive a,ScanPrimitive b)
        { return Math.Acos(Math.Min(1,Math.Abs(Dot(new V(a.Direction).Unit,new V(b.Direction).Unit))))*180/Math.PI; }
        public static double ParallelPlanes(ScanPrimitive a,ScanPrimitive b)
        { if(a.Kind!="plane"||b.Kind!="plane"||AngleDegrees(a,b)>1) throw new ArgumentException("Parallel planes required (within 1 degree)."); return PointPlane(a.Origin,b); }

        public static ScanPrimitive Fit(string id,string kind,IReadOnlyList<ScanPoint> points)
        {
            Validate(points);
            int minimum=kind=="point"?1:kind=="axis"?2:kind=="plane"||kind=="circle"?3:kind=="cylinder"?12:int.MaxValue;
            if(points.Count<minimum) throw new ArgumentException("Insufficient support points.");
            var p=points.Select(s=>new V(s.Position)).ToArray();
            V c=p.Aggregate(new V(),(a,b)=>a+b)/p.Length;
            var axes=Principal(p,c,out var eigen);
            V origin=c, direction=axes[2]; double radius=0,length=0;
            Func<V,double> residual;
            if(kind=="point") { if(p.Length!=1) throw new ArgumentException("Select exactly one point."); direction=new V(0,0,1); residual=q=>(q-c).Norm; }
            else if(kind=="axis")
            { if(eigen[2]<1e-8 || eigen[1]>.25*eigen[2]) throw new ArgumentException("Support is not linear."); direction=axes[2]; residual=q=>Cross(q-c,direction).Norm; }
            else if(kind=="plane")
            { if(eigen[1]<1e-8 || eigen[0]>.15*eigen[1]) throw new ArgumentException("Support is not planar."); direction=axes[0]; residual=q=>Math.Abs(Dot(q-c,direction)); }
            else if(kind=="circle")
            {
                if(eigen[1]<1e-8 || eigen[0]>.05*eigen[1]) throw new ArgumentException("Rim is not planar.");
                direction=axes[0]; Circle(p,c,direction,out origin,out radius,out _);
                var o=origin; var d=direction;
                residual=q=>Math.Sqrt(Math.Pow((q-o-d*Dot(q-o,d)).Norm-radius,2)+Math.Pow(Dot(q-o,d),2));
            }
            else
            {
                if(eigen[0]<1e-8) throw new ArgumentException("Cylinder needs spatial support.");
                double best=double.MaxValue; V bestOrigin=c,bestDirection=axes[2]; double bestRadius=0;
                foreach(var candidate in axes)
                {
                    V d=candidate,localOrigin=c;double localBest=double.MaxValue,localRadius=0;
                    for(double step=.2;step>.0005;step*=.4)
                    {
                        bool improved;
                        int iterations=0;
                        do
                        {
                            improved=false; Basis(d,out var u,out var v);
                            foreach(var trial in new[]{d,(d+u*step).Unit,(d-u*step).Unit,(d+v*step).Unit,(d-v*step).Unit})
                            {
                                try { Circle(p,c,trial,out var fittedOrigin,out double r,out double error);
                                    if(error<localBest) {localBest=error;localOrigin=fittedOrigin;localRadius=r;d=trial;improved=true;} }
                                catch(ArgumentException) { }
                            }
                        } while(improved && ++iterations<20);
                    }
                    if(localBest<best) {best=localBest;bestOrigin=localOrigin;bestDirection=d;bestRadius=localRadius;}
                }
                if(best==double.MaxValue) throw new ArgumentException("Cylinder fit failed.");
                origin=bestOrigin;direction=bestDirection;radius=bestRadius;
                var o=origin;var d0=direction;
                residual=q=>Math.Abs((q-o-d0*Dot(q-o,d0)).Norm-radius);
            }
            var offsets=p.Select(q=>Dot(q-origin,direction)).ToArray();
            length=offsets.Max()-offsets.Min();
            if(kind=="axis"||kind=="cylinder") origin+=direction*((offsets.Max()+offsets.Min())/2);
            double rms=Math.Sqrt(p.Average(q=>Math.Pow(residual(q),2)));
            if((kind=="circle"||kind=="cylinder") && (rms>radius*.1 || !Finite(radius) || radius<.001 || radius>1e7)) throw new ArgumentException("Poor circular support.");
            if(kind=="cylinder" && length<radius*.2) throw new ArgumentException("Cylinder axial extent is insufficient.");
            return new ScanPrimitive { Id=id,Kind=kind,Origin=origin.Array,Direction=direction.Array,Radius=radius,Length=length,
                SupportIds=points.Select(s=>s.Id).ToArray(),FitRms=rms };
        }
        private static void Basis(V n,out V u,out V v)
        { u=Cross(n,Math.Abs(n.X)<.8?new V(1,0,0):new V(0,1,0)).Unit;v=Cross(n,u).Unit; }
        private static void Circle(V[] points,V c,V direction,out V origin,out double radius,out double rms)
        {
            Basis(direction,out var u,out var v);
            var a=new double[3,3];var b=new double[3];
            foreach(var p in points)
            {
                double x=Dot(p-c,u),y=Dot(p-c,v); var row=new[]{2*x,2*y,1d}; double rhs=x*x+y*y;
                for(int i=0;i<3;i++) {b[i]+=row[i]*rhs;for(int j=0;j<3;j++) a[i,j]+=row[i]*row[j];}
            }
            var solution=Solve(a,b); origin=c+u*solution[0]+v*solution[1];
            radius=Math.Sqrt(solution[2]+solution[0]*solution[0]+solution[1]*solution[1]);
            var o=origin;double r=radius;
            if(!Finite(r)||r<1e-6) throw new ArgumentException("Degenerate circle.");
            var angles=points.Select(p=>Math.Atan2(Dot(p-o,v),Dot(p-o,u))).OrderBy(x=>x).ToArray();
            double maxGap=angles[0]+2*Math.PI-angles[angles.Length-1];
            for(int i=1;i<angles.Length;i++) maxGap=Math.Max(maxGap,angles[i]-angles[i-1]);
            if(maxGap>Math.PI*1.5) throw new ArgumentException("Insufficient angular coverage (less than 90 degrees).");
            rms=Math.Sqrt(points.Average(p=>Math.Pow((p-o-direction*Dot(p-o,direction)).Norm-r,2)));
        }
        private static double[] Solve(double[,] a,double[] b)
        {
            for(int i=0;i<3;i++)
            {
                int pivot=i; for(int j=i+1;j<3;j++) if(Math.Abs(a[j,i])>Math.Abs(a[pivot,i])) pivot=j;
                if(Math.Abs(a[pivot,i])<1e-10) throw new ArgumentException("Degenerate fit.");
                for(int k=0;k<3;k++) {double t=a[i,k];a[i,k]=a[pivot,k];a[pivot,k]=t;} double s=b[i];b[i]=b[pivot];b[pivot]=s;
                double scale=a[i,i];for(int k=0;k<3;k++) a[i,k]/=scale;b[i]/=scale;
                for(int j=0;j<3;j++) if(j!=i) { double f=a[j,i];for(int k=0;k<3;k++) a[j,k]-=f*a[i,k];b[j]-=f*b[i]; }
            }
            return b;
        }
        private static V[] Principal(V[] points,V center,out double[] eigen)
        {
            var a=new double[3,3];var vectors=new double[3,3];for(int i=0;i<3;i++) vectors[i,i]=1;
            foreach(var point in points) {var p=(point-center).Array;for(int i=0;i<3;i++) for(int j=0;j<3;j++) a[i,j]+=p[i]*p[j]/points.Length;}
            for(int iteration=0;iteration<40;iteration++)
            {
                int p=0,q=1; for(int i=0;i<3;i++) for(int j=i+1;j<3;j++) if(Math.Abs(a[i,j])>Math.Abs(a[p,q])) {p=i;q=j;}
                if(Math.Abs(a[p,q])<1e-12) break;
                double angle=.5*Math.Atan2(2*a[p,q],a[q,q]-a[p,p]),c=Math.Cos(angle),s=Math.Sin(angle);
                for(int k=0;k<3;k++) {double x=a[k,p],y=a[k,q];a[k,p]=c*x-s*y;a[k,q]=s*x+c*y;}
                for(int k=0;k<3;k++) {double x=a[p,k],y=a[q,k];a[p,k]=c*x-s*y;a[q,k]=s*x+c*y;}
                for(int k=0;k<3;k++) {double x=vectors[k,p],y=vectors[k,q];vectors[k,p]=c*x-s*y;vectors[k,q]=s*x+c*y;}
            }
            var order=Enumerable.Range(0,3).OrderBy(i=>a[i,i]).ToArray();eigen=order.Select(i=>Math.Max(0,a[i,i])).ToArray();
            return order.Select(i=>new V(vectors[0,i],vectors[1,i],vectors[2,i]).Unit).ToArray();
        }
    }
}
