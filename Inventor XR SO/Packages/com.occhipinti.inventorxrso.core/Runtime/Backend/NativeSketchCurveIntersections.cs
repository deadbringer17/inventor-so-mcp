using System;
using System.Collections.Generic;

namespace InventorXrSo.Core.Backend
{
    /// <summary>
    /// Analytic, finite 2D geometry in millimetres. This is not a CAD mutation or
    /// a reconstruction of native constraints. Overlapping loci are deliberately
    /// ambiguous and never returned as an arbitrary collection of cut points.
    /// </summary>
    public static class NativeSketchCurveIntersections
    {
        private const double Tau = 2 * Math.PI;
        private const double Roundoff = 32 * 2.2204460492503131e-16;
        public enum CurveKind { Segment, Circle, Arc }
        public enum IntersectionKind { None, Points, Overlap, Coincident }

        public sealed class Curve
        {
            private Curve(CurveKind kind, CadPoint origin, CadPoint end, double radius, double start, double sweep)
            { Kind=kind; Origin=origin; End=end; RadiusMm=radius; StartRadians=start; SweepRadians=sweep; }
            public CurveKind Kind { get; }
            /// <summary>Segment start, or circle/arc centre.</summary>
            public CadPoint Origin { get; }
            public CadPoint End { get; }
            public double RadiusMm { get; }
            public double StartRadians { get; }
            public double SweepRadians { get; }
            public static Curve Segment(CadPoint start, CadPoint end)
            {
                CheckPoint(start); CheckPoint(end);
                if (Distance(start,end)<=0) throw new ArgumentException("A segment needs distinct finite endpoints.");
                return new Curve(CurveKind.Segment,start,end,0,0,0);
            }
            public static Curve Circle(CadPoint centre, double radiusMm)
            { CheckRound(centre,radiusMm); return new Curve(CurveKind.Circle,centre,default,radiusMm,0,Tau); }
            /// <summary>Positive sweep is CCW; negative sweep is CW. Full circles use Circle.</summary>
            public static Curve Arc(CadPoint centre, double radiusMm, double startDegrees, double sweepDegrees)
            {
                CheckRound(centre,radiusMm);
                if (!Finite(startDegrees) || !Finite(sweepDegrees) || sweepDegrees==0 || Math.Abs(sweepDegrees)>=360)
                    throw new ArgumentException("An arc needs finite angles and a nonzero signed sweep smaller than 360 degrees.");
                return new Curve(CurveKind.Arc,centre,default,radiusMm,Positive((startDegrees%360)*Math.PI/180),sweepDegrees*Math.PI/180);
            }
            /// <summary>
            /// Parameter 0..1 follows segment start/end or the signed arc sweep.
            /// Circles use CCW angle from +X, with the seam at parameter zero.
            /// </summary>
            public CadPoint PointAt(double parameter)
            {
                if (!Finite(parameter) || parameter<0 || parameter>1) throw new ArgumentOutOfRangeException(nameof(parameter));
                CadPoint point;
                if (Kind==CurveKind.Segment)
                {
                    // Preserve exact endpoints, and avoid subtracting two large
                    // nearly equal values when evaluating near the midpoint.
                    if (parameter==0) return Origin;
                    if (parameter==1) return End;
                    point=new CadPoint(Origin.X/2+End.X/2+(End.X-Origin.X)*(parameter-.5),
                        Origin.Y/2+End.Y/2+(End.Y-Origin.Y)*(parameter-.5));
                }
                else point=new CadPoint(Origin.X+RadiusMm*Math.Cos(StartRadians+SweepRadians*parameter),Origin.Y+RadiusMm*Math.Sin(StartRadians+SweepRadians*parameter));
                CheckPoint(point); return point;
            }
        }

        public readonly struct IntersectionPoint
        {
            internal IntersectionPoint(CadPoint point,double first,double second,bool tangent)
            { PointMm=point; FirstParameter=first; SecondParameter=second; IsTangent=tangent; }
            public CadPoint PointMm { get; }
            public double FirstParameter { get; }
            public double SecondParameter { get; }
            /// <summary>True for tangent/collinear contact; endpoint membership alone does not imply tangency.</summary>
            public bool IsTangent { get; }
        }
        public sealed class Result
        {
            internal Result(IntersectionKind kind,List<IntersectionPoint> points)
            { Kind=kind; Points=Array.AsReadOnly(points.ToArray()); }
            public IntersectionKind Kind { get; }
            /// <summary>Sorted by FirstParameter, then SecondParameter. Empty for ambiguous loci.</summary>
            public IReadOnlyList<IntersectionPoint> Points { get; }
        }

        /// <summary>
        /// Absolute distance tolerance, in mm, governs membership, endpoint contact,
        /// duplicate points and near-coincident loci. Geometry smaller than this
        /// tolerance is rejected. No tessellation or native entity IDs are involved.
        /// </summary>
        public static Result Intersect(Curve first,Curve second,double toleranceMm)
        {
            if (first==null || second==null) throw new ArgumentNullException(first==null ? nameof(first) : nameof(second));
            if (!Finite(toleranceMm) || toleranceMm<=0) throw new ArgumentOutOfRangeException(nameof(toleranceMm));
            ValidateSize(first,toleranceMm); ValidateSize(second,toleranceMm);
            var points=new List<IntersectionPoint>();
            var kind=IntersectionKind.Points;
            if (first.Kind==CurveKind.Segment && second.Kind==CurveKind.Segment)
                kind=Segments(first,second,toleranceMm,points);
            else if (first.Kind==CurveKind.Segment || second.Kind==CurveKind.Segment)
                SegmentRound(first,second,toleranceMm,points);
            else kind=Rounds(first,second,toleranceMm,points);
            if (kind==IntersectionKind.Overlap || kind==IntersectionKind.Coincident) points.Clear();
            else kind=points.Count==0 ? IntersectionKind.None : IntersectionKind.Points;
            points.Sort((a,b)=>{int c=a.FirstParameter.CompareTo(b.FirstParameter);return c!=0 ? c : a.SecondParameter.CompareTo(b.SecondParameter);});
            return new Result(kind,points);
        }

        private static IntersectionKind Segments(Curve a,Curve b,double tol,List<IntersectionPoint> points)
        {
            double la=Distance(a.Origin,a.End),lb=Distance(b.Origin,b.End);
            double ux=(a.End.X-a.Origin.X)/la,uy=(a.End.Y-a.Origin.Y)/la;
            double vx=(b.End.X-b.Origin.X)/lb,vy=(b.End.Y-b.Origin.Y)/lb;
            double qx=b.Origin.X-a.Origin.X,qy=b.Origin.Y-a.Origin.Y,den=Cross(ux,uy,vx,vy);
            if (!Finite(Hypot(qx,qy))) throw new ArgumentException("Geometry exceeds finite arithmetic range.");
            double firstOffset=Cross(qx,qy,ux,uy),lastOffset=firstOffset-lb*den;
            // A small angle alone does not make long segments coincident: test
            // the distance along their entire finite extent before using overlap.
            bool nearCollinear=Math.Abs(firstOffset)<=tol && Math.Abs(lastOffset)<=tol;
            if (Math.Abs(den)>Roundoff || den!=0 && !nearCollinear)
            {
                // Solve about the segment centres. Dividing a distance of order
                // 1e15 into a parameter then subtracting the segment origin can
                // otherwise displace a central crossing by tenths of a mm.
                double mx=a.Origin.X/2+a.End.X/2,my=a.Origin.Y/2+a.End.Y/2;
                double dx=b.Origin.X/2+b.End.X/2-mx,dy=b.Origin.Y/2+b.End.Y/2-my;
                double alongA=Cross(dx,dy,vx,vy)/den,alongB=Cross(dx,dy,ux,uy)/den;
                if (!Finite(alongA) || !Finite(alongB)) throw new ArgumentException("Geometry exceeds finite arithmetic range.");
                if (alongA>=-la/2-tol && alongA<=la/2+tol && alongB>=-lb/2-tol && alongB<=lb/2+tol)
                {
                    alongA=Math.Max(-la/2,Math.Min(la/2,alongA));
                    Add(a,b,new CadPoint(mx+alongA*ux,my+alongA*uy),false,tol,points);
                }
                return IntersectionKind.Points;
            }
            if (!nearCollinear) return IntersectionKind.None;
            double b0=qx*ux+qy*uy,b1=b0+(b.End.X-b.Origin.X)*ux+(b.End.Y-b.Origin.Y)*uy;
            double low=Math.Max(0,Math.Min(b0,b1)),high=Math.Min(la,Math.Max(b0,b1));
            if (high<low-tol) return IntersectionKind.None;
            if (high-low>tol)
                return Math.Abs(Math.Min(b0,b1))<=tol && Math.Abs(Math.Max(b0,b1)-la)<=tol ? IntersectionKind.Coincident : IntersectionKind.Overlap;
            Add(a,b,a.PointAt(Clamp((low+high)/2/la)),true,tol,points);
            return IntersectionKind.Points;
        }

        private static void SegmentRound(Curve a,Curve b,double tol,List<IntersectionPoint> points)
        {
            Curve line=a.Kind==CurveKind.Segment ? a : b,round=ReferenceEquals(line,a) ? b : a;
            double length=Distance(line.Origin,line.End),ux=(line.End.X-line.Origin.X)/length,uy=(line.End.Y-line.Origin.Y)/length;
            double cx=round.Origin.X-line.Origin.X,cy=round.Origin.Y-line.Origin.Y;
            double along=cx*ux+cy*uy,across=Cross(cx,cy,ux,uy),distance=Math.Abs(across),radius=round.RadiusMm;
            if (!Finite(along) || !Finite(distance)) throw new ArgumentException("Geometry exceeds finite arithmetic range.");
            if (distance>radius+tol) return;
            bool tangent=Math.Abs(distance-radius)<=tol;
            double half=tangent ? 0 : radius*Math.Sqrt(Math.Max(0,(1-distance/radius)*(1+distance/radius)));
            AddAt(along-half); if (!tangent) AddAt(along+half);
            void AddAt(double value)
            {
                if (value < -tol || value > length+tol) return;
                Add(a,b,line.PointAt(Clamp(value/length)),tangent,tol,points);
            }
        }

        private static IntersectionKind Rounds(Curve a,Curve b,double tol,List<IntersectionPoint> points)
        {
            double d=Distance(a.Origin,b.Origin),ra=a.RadiusMm,rb=b.RadiusMm;
            if (d+Math.Abs(ra-rb)<=tol) return SameSupport(a,b,tol,points);
            if (d==0 || d>ra+rb+tol || d<Math.Abs(ra-rb)-tol) return IntersectionKind.None;
            double scale=Math.Max(d,Math.Max(ra,rb)),dn=d/scale,an=ra/scale,bn=rb/scale;
            double along=scale*(((an-bn)*(an+bn)+dn*dn)/(2*dn));
            if (!Finite(along)) throw new ArgumentException("Geometry exceeds finite arithmetic range.");
            double ux=(b.Origin.X-a.Origin.X)/d,uy=(b.Origin.Y-a.Origin.Y)/d;
            bool tangent=Math.Abs(d-ra-rb)<=tol || Math.Abs(d-Math.Abs(ra-rb))<=tol;
            // For a near tangency use an actual point on the first circle.
            // The radical axis can move much farther than tol for almost
            // concentric internal tangencies, even though the gap is <= tol.
            if (tangent) along=(along<0 ? -1 : 1)*ra;
            double ratio=along/ra,half=tangent ? 0 : ra*Math.Sqrt(Math.Max(0,(1-ratio)*(1+ratio)));
            Add(a,b,new CadPoint(a.Origin.X+along*ux-half*uy,a.Origin.Y+along*uy+half*ux),tangent,tol,points);
            if (!tangent) Add(a,b,new CadPoint(a.Origin.X+along*ux+half*uy,a.Origin.Y+along*uy-half*ux),false,tol,points);
            return IntersectionKind.Points;
        }

        private static IntersectionKind SameSupport(Curve a,Curve b,double tol,List<IntersectionPoint> points)
        {
            if (a.Kind==CurveKind.Circle && b.Kind==CurveKind.Circle) return IntersectionKind.Coincident;
            // Split loci at the angular seam, not at arbitrary sample points.
            var ia=Intervals(a); var ib=Intervals(b); double shared=0,angularTolerance=tol/Math.Max(a.RadiusMm,b.RadiusMm);
            foreach (var x in ia) foreach (var y in ib) shared+=Math.Max(0,Math.Min(x.Item2,y.Item2)-Math.Max(x.Item1,y.Item1));
            if (shared>angularTolerance)
                return Math.Abs(shared-Math.Abs(a.SweepRadians))<=angularTolerance && Math.Abs(shared-Math.Abs(b.SweepRadians))<=angularTolerance
                    ? IntersectionKind.Coincident : IntersectionKind.Overlap;
            // Disjoint arcs can still meet at an endpoint (including the 0/2pi seam).
            if (a.Kind==CurveKind.Arc) { Add(a,b,a.PointAt(0),true,tol,points); Add(a,b,a.PointAt(1),true,tol,points); }
            if (b.Kind==CurveKind.Arc) { Add(a,b,b.PointAt(0),true,tol,points); Add(a,b,b.PointAt(1),true,tol,points); }
            return IntersectionKind.Points;
        }
        private static List<Tuple<double,double>> Intervals(Curve c)
        {
            if (c.Kind==CurveKind.Circle) return new List<Tuple<double,double>> {Tuple.Create(0d,Tau)};
            double low=Positive(c.StartRadians+Math.Min(0,c.SweepRadians)),high=low+Math.Abs(c.SweepRadians);
            return high<=Tau ? new List<Tuple<double,double>> {Tuple.Create(low,high)}
                : new List<Tuple<double,double>> {Tuple.Create(low,Tau),Tuple.Create(0d,high-Tau)};
        }

        private static void Add(Curve a,Curve b,CadPoint point,bool tangent,double tol,List<IntersectionPoint> points)
        {
            if (!Parameter(a,point,tol,out double t) || !Parameter(b,point,tol,out double s)) return;
            foreach (var existing in points) if (Distance(existing.PointMm,point)<=tol) return;
            points.Add(new IntersectionPoint(point,t,s,tangent));
        }
        private static bool Parameter(Curve c,CadPoint p,double tol,out double parameter)
        {
            CheckPoint(p);parameter=0;
            if (c.Kind==CurveKind.Segment)
            {
                double length=Distance(c.Origin,c.End),ux=(c.End.X-c.Origin.X)/length,uy=(c.End.Y-c.Origin.Y)/length;
                double x=p.X-c.Origin.X,y=p.Y-c.Origin.Y,along=x*ux+y*uy;
                if (!Finite(along) || !Finite(Hypot(x,y))) throw new ArgumentException("Geometry exceeds finite arithmetic range.");
                if (Math.Abs(Cross(x,y,ux,uy))>tol || along < -tol || along > length+tol) return false;
                parameter=Clamp(along/length); return Distance(c.PointAt(parameter),p)<=tol;
            }
            double dx=p.X-c.Origin.X,dy=p.Y-c.Origin.Y;
            if (Math.Abs(Distance(p,c.Origin)-c.RadiusMm)>tol) return false;
            double travel=Positive(Math.Sign(c.SweepRadians)*(Math.Atan2(dy,dx)-c.StartRadians));
            if (c.Kind==CurveKind.Circle) { parameter=travel/Tau; return true; }
            double sweep=Math.Abs(c.SweepRadians),angularTolerance=tol/c.RadiusMm;
            if (travel>sweep)
            {
                if (Tau-travel<=angularTolerance) parameter=0;
                else if (travel-sweep<=angularTolerance) parameter=1;
                else return false;
                return Distance(c.PointAt(parameter),p)<=tol;
            }
            parameter=travel/sweep;return true;
        }
        private static void ValidateSize(Curve c,double tol)
        {
            double size=c.Kind==CurveKind.Segment ? Distance(c.Origin,c.End) : c.RadiusMm;
            if (size<=tol || c.Kind==CurveKind.Arc && c.RadiusMm*Math.Abs(c.SweepRadians)<=tol)
                throw new ArgumentException("Curve is degenerate at the supplied millimetre tolerance.");
        }
        private static void CheckRound(CadPoint centre,double radius)
        { CheckPoint(centre);if (!Finite(radius) || radius<=0) throw new ArgumentOutOfRangeException(nameof(radius)); }
        private static void CheckPoint(CadPoint point)
        { if (!Finite(point.X) || !Finite(point.Y)) throw new ArgumentException("Coordinates must be finite millimetres."); }
        private static double Distance(CadPoint a,CadPoint b)
        { double value=Hypot(a.X-b.X,a.Y-b.Y);if (!Finite(value)) throw new ArgumentException("Geometry exceeds finite arithmetic range.");return value; }
        private static double Hypot(double x,double y)
        { x=Math.Abs(x);y=Math.Abs(y);double m=Math.Max(x,y);return m==0 ? 0 : m*Math.Sqrt((x/m)*(x/m)+(y/m)*(y/m)); }
        private static double Cross(double x,double y,double u,double v)=>x*v-y*u;
        private static double Positive(double angle) { angle%=Tau;return angle<0 ? angle+Tau : angle; }
        private static double Clamp(double t)=>Math.Max(0,Math.Min(1,t));
        private static bool Finite(double value)=>!double.IsNaN(value) && !double.IsInfinity(value);
    }
}
