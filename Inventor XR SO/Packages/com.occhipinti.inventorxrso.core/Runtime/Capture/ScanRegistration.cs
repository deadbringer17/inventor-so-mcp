using System;
using System.Linq;

#if CAPTURE_HOST
namespace Inventor.So.RealityCapture.Review
#else
namespace InventorXrSo.Core.Capture
#endif
{
    /// <summary>Row-major matrices, column vectors, translations in mm. CAD is right-handed; scan/tracking are Unity left-handed.</summary>
    public static class ScanRegistration
    {
        public static double[] Identity=>new double[]{1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1};
        public static double[] ScanToCadBasis=>new double[]{1,0,0,0,0,1,0,0,0,0,-1,0,0,0,0,1};
        private static bool Finite(double n)=>!double.IsNaN(n)&&!double.IsInfinity(n);
        public static double[] Vector(double[] p)
        {if(p==null||p.Length!=3||p.Any(n=>!Finite(n)||Math.Abs(n)>1e7)) throw new ArgumentException("Invalid coordinate.");return (double[])p.Clone();}
        private static double[] Add(double[] a,double[] b)=>new[]{a[0]+b[0],a[1]+b[1],a[2]+b[2]};
        private static double[] Sub(double[] a,double[] b)=>new[]{a[0]-b[0],a[1]-b[1],a[2]-b[2]};
        private static double[] Scale(double[] a,double b)=>a.Select(x=>x*b).ToArray();
        private static double Dot(double[] a,double[] b)=>a[0]*b[0]+a[1]*b[1]+a[2]*b[2];
        private static double[] Cross(double[] a,double[] b)=>new[]{a[1]*b[2]-a[2]*b[1],a[2]*b[0]-a[0]*b[2],a[0]*b[1]-a[1]*b[0]};
        private static double[] Unit(double[] a) {Vector(a);double n=Math.Sqrt(Dot(a,a));if(n<1e-8) throw new ArgumentException("Degenerate direction.");return Scale(a,1/n);}
        private static double[] Basis(double[] origin,double[] x,double[] normal)
        {var z=Unit(normal);var projected=Sub(x,Scale(z,Dot(x,z)));if(Dot(projected,projected)<.0001*Dot(x,x)) throw new ArgumentException("Plane and axis do not determine stable yaw.");x=Unit(projected);var y=Unit(Cross(z,x));return new[]{x[0],y[0],z[0],origin[0],x[1],y[1],z[1],origin[1],x[2],y[2],z[2],origin[2],0d,0,0,1};}
        private static double[] Frame(double[][] p)
        {var x=Sub(p[1],p[0]);var y=Sub(p[2],p[0]);if(Math.Sqrt(Dot(x,x))<1||Math.Sqrt(Dot(y,y))<1||Math.Sqrt(Dot(Cross(Unit(x),Unit(y)),Cross(Unit(x),Unit(y))))<.01) throw new ArgumentException("Three separated non-collinear points required.");return Basis(p[0],x,Cross(x,y));}
        public static double[] ThreePoints(double[][] scan,double[][] cad,bool toCad=true,double toleranceMm=5)
        {
            if(scan==null||cad==null||scan.Length!=3||cad.Length!=3||!Finite(toleranceMm)||toleranceMm<.001||toleranceMm>50) throw new ArgumentException("Invalid registration.");
            var source=scan.Select(Vector).ToArray();var target=cad.Select(Vector).ToArray();
            var handedness=toCad?ScanToCadBasis:Identity;
            var canonical=source.Select(p=>Point(handedness,p)).ToArray();
            var result=Multiply(Multiply(Frame(target),Inverse(Frame(canonical))),handedness);
            if(Residuals(result,source,target).Max()>toleranceMm) throw new ArgumentException("Point distances disagree; scale is never fitted.");
            Validate(result,toCad?-1:1);return result;
        }
        public static double[] PlaneAxis(double[] scanOrigin,double[] scanNormal,double[] scanAxis,double[] cadOrigin,double[] cadNormal,double[] cadAxis)
        {
            if(Math.Abs(Dot(Unit(scanNormal),Unit(scanAxis))-Dot(Unit(cadNormal),Unit(cadAxis)))>.01) throw new ArgumentException("Plane-axis angles disagree.");
            var h=ScanToCadBasis;
            var result=Multiply(Multiply(Basis(Vector(cadOrigin),Unit(cadAxis),Unit(cadNormal)),Inverse(Basis(Point(h,Vector(scanOrigin)),Direction(h,Unit(scanAxis)),Direction(h,Unit(scanNormal))))),h);
            Validate(result,-1);return result;
        }
        public static double[] Residuals(double[] matrix,double[][] source,double[][] target)=>source.Select((p,i)=>{var d=Sub(Point(matrix,p),target[i]);return Math.Sqrt(Dot(d,d));}).ToArray();
        public static double[] Point(double[] m,double[] p)=>new[]{m[0]*p[0]+m[1]*p[1]+m[2]*p[2]+m[3],m[4]*p[0]+m[5]*p[1]+m[6]*p[2]+m[7],m[8]*p[0]+m[9]*p[1]+m[10]*p[2]+m[11]};
        public static double[] Direction(double[] m,double[] p)=>new[]{m[0]*p[0]+m[1]*p[1]+m[2]*p[2],m[4]*p[0]+m[5]*p[1]+m[6]*p[2],m[8]*p[0]+m[9]*p[1]+m[10]*p[2]};
        public static double[] Multiply(double[] a,double[] b)
        {var c=new double[16];for(int i=0;i<4;i++) for(int j=0;j<4;j++) for(int k=0;k<4;k++) c[4*i+j]+=a[4*i+k]*b[4*k+j];return c;}
        public static double[] Inverse(double[] m)
        {Validate(m);var r=Identity;for(int i=0;i<3;i++) for(int j=0;j<3;j++) r[4*i+j]=m[4*j+i];var t=Direction(r,new[]{m[3],m[7],m[11]});r[3]=-t[0];r[7]=-t[1];r[11]=-t[2];return r;}
        public static void Validate(double[] m,int determinant=0)
        {
            if(m==null||m.Length!=16||m.Any(n=>!Finite(n)||Math.Abs(n)>1e7)||Math.Abs(m[12])+Math.Abs(m[13])+Math.Abs(m[14])+Math.Abs(m[15]-1)>1e-8) throw new ArgumentException("Invalid rigid matrix.");
            for(int i=0;i<3;i++) for(int j=0;j<3;j++) {double dot=0;for(int k=0;k<3;k++) dot+=m[4*k+i]*m[4*k+j];if(Math.Abs(dot-(i==j?1:0))>1e-6) throw new ArgumentException("Scale or shear refused.");}
            double d=m[0]*(m[5]*m[10]-m[6]*m[9])-m[1]*(m[4]*m[10]-m[6]*m[8])+m[2]*(m[4]*m[9]-m[5]*m[8]);
            if(determinant!=0&&Math.Abs(d-determinant)>1e-6) throw new ArgumentException("Wrong coordinate handedness.");
        }
        public static double[] InMetres(double[] m) {Validate(m);var r=(double[])m.Clone();r[3]/=1000;r[7]/=1000;r[11]/=1000;return r;}
    }
}
