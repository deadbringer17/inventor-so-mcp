using System;
using System.Collections.Generic;
using Curve = InventorXrSo.Core.Backend.NativeSketchCurveIntersections.Curve;
using CurveKind = InventorXrSo.Core.Backend.NativeSketchCurveIntersections.CurveKind;
using IntersectionKind = InventorXrSo.Core.Backend.NativeSketchCurveIntersections.IntersectionKind;

namespace InventorXrSo.Core.Backend
{
    /// <summary>
    /// Pure geometric partitions for a future trim/split command. No native
    /// topology, constraints, dependencies or desktop command behaviour is inferred.
    /// </summary>
    public static class NativeSketchCurveIntervals
    {
        private const double Tau=2*Math.PI;
        public enum PlanStatus { ReadyGeometry, RequiresOracle, AmbiguousLocus, AmbiguousHit, NoChange }
        public enum PlanReason { None, TooFewCuts, TangentContact, OverlapOrCoincidence, HitOnBoundary, ClosedCurveSingleSplit, EndpointSplit }

        public readonly struct Cut
        {
            internal Cut(double parameter,CadPoint point,bool tangent)
            { Parameter=parameter;PointMm=point;IsTangent=tangent; }
            public double Parameter { get; }
            public CadPoint PointMm { get; }
            public bool IsTangent { get; }
        }
        /// <summary>
        /// Directed interval on the target's normalized parameter. A circular
        /// interval may end above 1 to cross the seam (e.g. .75 to 1.25).
        /// Endpoints are boundaries; adjacent intervals may share them.
        /// </summary>
        public readonly struct Interval
        {
            internal Interval(double start,double end) { StartParameter=start;EndParameter=end; }
            public double StartParameter { get; }
            public double EndParameter { get; }
            public double ParameterLength=>EndParameter-StartParameter;
            public bool WrapsSeam=>EndParameter>1;
        }
        public sealed class Plan
        {
            internal Plan(PlanStatus status,PlanReason reason,List<Cut> cuts,List<Interval> removed,List<Interval> retained,List<int> ambiguous)
            {
                Status=status;Reason=reason;Cuts=Array.AsReadOnly(cuts.ToArray());
                Removed=Array.AsReadOnly(removed.ToArray());Retained=Array.AsReadOnly(retained.ToArray());
                AmbiguousCutterIndices=Array.AsReadOnly(ambiguous.ToArray());
            }
            public PlanStatus Status { get; }
            public PlanReason Reason { get; }
            public IReadOnlyList<Cut> Cuts { get; }
            public IReadOnlyList<Interval> Removed { get; }
            public IReadOnlyList<Interval> Retained { get; }
            /// <summary>Zero-based indices of cutters with overlapping/coincident loci.</summary>
            public IReadOnlyList<int> AmbiguousCutterIndices { get; }
        }

        /// <summary>
        /// Finds the interval containing the hit. Tangencies and fewer than two
        /// distinct interior cuts require a desktop oracle before choosing semantics.
        /// ReadyGeometry certifies only this mathematical partition.
        /// </summary>
        public static Plan Trim(Curve target,IEnumerable<Curve> cutters,double hitParameter,double toleranceMm)
        {
            double length=Validate(target,toleranceMm);double hit=HitParameter(target,hitParameter);
            if (cutters==null) throw new ArgumentNullException(nameof(cutters));
            var cuts=new List<Cut>();var ambiguous=new List<int>();int index=0;
            foreach(var cutter in cutters)
            {
                var intersection=NativeSketchCurveIntersections.Intersect(target,cutter,toleranceMm);
                if (intersection.Kind==IntersectionKind.Overlap || intersection.Kind==IntersectionKind.Coincident) ambiguous.Add(index);
                else foreach(var point in intersection.Points)
                    cuts.Add(new Cut(NormalizeCut(target,point.FirstParameter,length,toleranceMm),point.PointMm,point.IsTangent));
                index++;
            }
            cuts=Deduplicate(target,cuts,length,toleranceMm);
            if (ambiguous.Count>0) return Empty(PlanStatus.AmbiguousLocus,PlanReason.OverlapOrCoincidence,cuts,ambiguous);
            foreach(var cut in cuts) if (cut.IsTangent) return Empty(PlanStatus.RequiresOracle,PlanReason.TangentContact,cuts,ambiguous);
            var boundaries=new List<double>();
            foreach(var cut in cuts)
                if (target.Kind==CurveKind.Circle || cut.Parameter>0 && cut.Parameter<1) boundaries.Add(cut.Parameter);
            if (boundaries.Count<2) return Empty(PlanStatus.RequiresOracle,PlanReason.TooFewCuts,cuts,ambiguous);
            foreach(double boundary in boundaries)
                if (ParameterDistance(target,hit,boundary)*length<=toleranceMm)
                    return Empty(PlanStatus.AmbiguousHit,PlanReason.HitOnBoundary,cuts,ambiguous);
            if (target.Kind!=CurveKind.Circle && Math.Min(hit,1-hit)*length<=toleranceMm)
                return Empty(PlanStatus.AmbiguousHit,PlanReason.HitOnBoundary,cuts,ambiguous);

            var intervals=new List<Interval>();
            if (target.Kind==CurveKind.Circle)
            {
                for(int i=0;i<boundaries.Count;i++)
                    intervals.Add(new Interval(boundaries[i],i+1<boundaries.Count ? boundaries[i+1] : boundaries[0]+1));
            }
            else
            {
                double start=0;foreach(double end in boundaries) { intervals.Add(new Interval(start,end));start=end; }
                intervals.Add(new Interval(start,1));
            }
            var removed=new List<Interval>();var retained=new List<Interval>();
            foreach(var interval in intervals)
            {
                double unwrapped=target.Kind==CurveKind.Circle && hit<interval.StartParameter ? hit+1 : hit;
                if (unwrapped>interval.StartParameter && unwrapped<interval.EndParameter) removed.Add(interval);
                else retained.Add(interval);
            }
            if (removed.Count!=1) return Empty(PlanStatus.AmbiguousHit,PlanReason.HitOnBoundary,cuts,ambiguous);
            return new Plan(PlanStatus.ReadyGeometry,PlanReason.None,cuts,removed,retained,ambiguous);
        }
        public static Plan TrimAtPoint(Curve target,IEnumerable<Curve> cutters,CadPoint hitMm,double toleranceMm)
            =>Trim(target,cutters,ParameterAtPoint(target,hitMm,toleranceMm),toleranceMm);

        /// <summary>Partition an open finite curve at a validated point; endpoints create no zero-length piece.</summary>
        public static Plan Split(Curve target,double splitParameter,double toleranceMm)
        {
            double length=Validate(target,toleranceMm);double parameter=HitParameter(target,splitParameter);
            parameter=NormalizeCut(target,parameter,length,toleranceMm);
            var cuts=new List<Cut> {new Cut(parameter,target.PointAt(parameter),false)};
            if (target.Kind==CurveKind.Circle) return Empty(PlanStatus.RequiresOracle,PlanReason.ClosedCurveSingleSplit,cuts,new List<int>());
            if (parameter==0 || parameter==1)
                return new Plan(PlanStatus.NoChange,PlanReason.EndpointSplit,cuts,new List<Interval>(),new List<Interval>{new Interval(0,1)},new List<int>());
            return new Plan(PlanStatus.ReadyGeometry,PlanReason.None,cuts,new List<Interval>(),
                new List<Interval>{new Interval(0,parameter),new Interval(parameter,1)},new List<int>());
        }
        public static Plan SplitAtPoint(Curve target,CadPoint pointMm,double toleranceMm)
            =>Split(target,ParameterAtPoint(target,pointMm,toleranceMm),toleranceMm);

        /// <summary>
        /// Validates a point against the finite curve, never its extended support.
        /// If both endpoints of an almost closed arc are within tolerance, callers
        /// must supply an explicit parameter rather than accepting an arbitrary end.
        /// </summary>
        public static double ParameterAtPoint(Curve target,CadPoint pointMm,double toleranceMm)
        {
            double length=Validate(target,toleranceMm);
            if (!Finite(pointMm.X) || !Finite(pointMm.Y)) throw new ArgumentException("Hit coordinates must be finite millimetres.");
            double parameter;
            if (target.Kind==CurveKind.Segment)
            {
                double mx=target.Origin.X/2+target.End.X/2,my=target.Origin.Y/2+target.End.Y/2;
                double ux=(target.End.X-target.Origin.X)/length,uy=(target.End.Y-target.Origin.Y)/length;
                parameter=.5+((pointMm.X-mx)*ux+(pointMm.Y-my)*uy)/length;
                if (!Finite(parameter) || parameter< -toleranceMm/length || parameter>1+toleranceMm/length)
                    throw new ArgumentException("Point is outside the finite segment.");
                parameter=Math.Max(0,Math.Min(1,parameter));
            }
            else
            {
                double x=pointMm.X-target.Origin.X,y=pointMm.Y-target.Origin.Y;
                if (Math.Abs(Distance(pointMm,target.Origin)-target.RadiusMm)>toleranceMm)
                    throw new ArgumentException("Point is not on the circular support.");
                if (target.Kind==CurveKind.Circle) parameter=Positive(Math.Atan2(y,x))/Tau;
                else
                {
                    bool start=Distance(pointMm,target.PointAt(0))<=toleranceMm,end=Distance(pointMm,target.PointAt(1))<=toleranceMm;
                    if (start && end) throw new ArgumentException("Point identifies both arc endpoints; supply an explicit parameter.");
                    if (start) parameter=0;
                    else if (end) parameter=1;
                    else
                    {
                        parameter=Positive(Math.Sign(target.SweepRadians)*(Math.Atan2(y,x)-target.StartRadians))/Math.Abs(target.SweepRadians);
                        if (parameter>1) throw new ArgumentException("Point is outside the finite arc.");
                    }
                }
            }
            if (Distance(pointMm,target.PointAt(parameter))>toleranceMm) throw new ArgumentException("Point is not on the finite curve within tolerance.");
            return NormalizeCut(target,parameter,length,toleranceMm);
        }

        private static List<Cut> Deduplicate(Curve target,List<Cut> cuts,double length,double tol)
        {
            cuts.Sort((a,b)=>a.Parameter.CompareTo(b.Parameter));var unique=new List<Cut>();
            foreach(var cut in cuts)
            {
                if (unique.Count>0 && (cut.Parameter-unique[unique.Count-1].Parameter)*length<=tol)
                {
                    var previous=unique[unique.Count-1];
                    unique[unique.Count-1]=new Cut(previous.Parameter,target.PointAt(previous.Parameter),previous.IsTangent || cut.IsTangent);
                }
                else unique.Add(new Cut(cut.Parameter,target.PointAt(cut.Parameter),cut.IsTangent));
            }
            if (target.Kind==CurveKind.Circle && unique.Count>1)
            {
                var first=unique[0];var last=unique[unique.Count-1];
                if ((first.Parameter+1-last.Parameter)*length<=tol)
                {
                    unique[0]=new Cut(first.Parameter,first.PointMm,first.IsTangent || last.IsTangent);
                    unique.RemoveAt(unique.Count-1);
                }
            }
            return unique;
        }
        private static double NormalizeCut(Curve target,double parameter,double length,double tol)
        {
            // A circle has no endpoint at the seam. Snapping both sides to zero
            // would merge two cuts as far as 2*tol apart; deduplicate cyclically.
            if (target.Kind==CurveKind.Circle) return parameter==1 ? 0 : parameter;
            if (parameter*length<=tol) return 0;
            if ((1-parameter)*length<=tol) return 1;
            return parameter;
        }
        private static double HitParameter(Curve target,double parameter)
        {
            if (!Finite(parameter) || parameter<0 || parameter>1) throw new ArgumentOutOfRangeException(nameof(parameter));
            return target.Kind==CurveKind.Circle && parameter==1 ? 0 : parameter;
        }
        private static double Validate(Curve target,double tol)
        {
            // Reuse the intersection engine's finite and tolerance-degeneracy guards.
            NativeSketchCurveIntersections.Intersect(target,target,tol);
            double length=target.Kind==CurveKind.Segment ? Distance(target.Origin,target.End) : target.RadiusMm*Math.Abs(target.SweepRadians);
            if (!Finite(length)) throw new ArgumentException("Curve length exceeds finite arithmetic range.");
            return length;
        }
        private static double ParameterDistance(Curve target,double a,double b)
        { double distance=Math.Abs(a-b);return target.Kind==CurveKind.Circle ? Math.Min(distance,1-distance) : distance; }
        private static Plan Empty(PlanStatus status,PlanReason reason,List<Cut> cuts,List<int> ambiguous)
            =>new Plan(status,reason,cuts,new List<Interval>(),new List<Interval>(),ambiguous);
        private static double Distance(CadPoint a,CadPoint b)
        {
            double x=Math.Abs(a.X-b.X),y=Math.Abs(a.Y-b.Y),m=Math.Max(x,y);
            double distance=m==0 ? 0 : m*Math.Sqrt((x/m)*(x/m)+(y/m)*(y/m));
            if (!Finite(distance)) throw new ArgumentException("Geometry exceeds finite arithmetic range.");return distance;
        }
        private static double Positive(double angle) { angle%=Tau;return angle<0 ? angle+Tau : angle; }
        private static bool Finite(double value)=>!double.IsNaN(value) && !double.IsInfinity(value);
    }
}
