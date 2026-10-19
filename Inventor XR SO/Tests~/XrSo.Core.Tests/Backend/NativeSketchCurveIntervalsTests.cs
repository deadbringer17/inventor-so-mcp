using System;
using System.Linq;
using InventorXrSo.Core.Backend;
using Xunit;
using Planner=InventorXrSo.Core.Backend.NativeSketchCurveIntervals;
using Curve=InventorXrSo.Core.Backend.NativeSketchCurveIntersections.Curve;
using Status=InventorXrSo.Core.Backend.NativeSketchCurveIntervals.PlanStatus;
using Reason=InventorXrSo.Core.Backend.NativeSketchCurveIntervals.PlanReason;

namespace XrSo.Core.Tests.Backend;

public sealed class NativeSketchCurveIntervalsTests
{
    private const double Tol=1e-7;
    private static CadPoint P(double x,double y)=>new CadPoint(x,y);
    private static Curve Vertical(double x)=>Curve.Segment(P(x,-20),P(x,20));
    private static void Interval(double start,double end,Planner.Interval actual)
    { Assert.Equal(start,actual.StartParameter,8);Assert.Equal(end,actual.EndParameter,8);Assert.True(actual.ParameterLength>0); }
    private static void Safe(Planner.Plan plan)
    { Assert.Empty(plan.Removed);Assert.Empty(plan.Retained); }

    [Fact]
    public void TrimLineSelectsOnlyTheHitIntervalAndKeepsBothOutsidePieces()
    {
        var line=Curve.Segment(P(0,0),P(10,0));
        var result=Planner.TrimAtPoint(line,new[]{Vertical(7),Vertical(3)},P(5,0),Tol);
        Assert.Equal(Status.ReadyGeometry,result.Status);Assert.Equal(Reason.None,result.Reason);
        Assert.Equal(new[]{.3,.7},result.Cuts.Select(c=>c.Parameter).ToArray());
        Interval(.3,.7,Assert.Single(result.Removed));Assert.Equal(2,result.Retained.Count);
        Interval(0,.3,result.Retained[0]);Interval(.7,1,result.Retained[1]);
        var reverse=Planner.Trim(line,new[]{Vertical(3),Vertical(7)},.5,Tol);
        Assert.Equal(result.Cuts.Select(c=>c.Parameter),reverse.Cuts.Select(c=>c.Parameter));
        Interval(.3,.7,Assert.Single(reverse.Removed));
    }
    [Theory]
    [InlineData(.1,0,.3)] [InlineData(.9,.7,1)]
    public void AnOutsideHitSelectsAnOutsideFinitePiece(double hit,double start,double end)
    {
        var result=Planner.Trim(Curve.Segment(P(0,0),P(10,0)),new[]{Vertical(3),Vertical(7)},hit,Tol);
        Assert.Equal(Status.ReadyGeometry,result.Status);Interval(start,end,Assert.Single(result.Removed));
        Assert.Equal(1d,result.Removed.Concat(result.Retained).Sum(i=>i.ParameterLength),9);
    }
    [Theory]
    [InlineData(1e-4,1e-12)] [InlineData(1,1e-7)] [InlineData(1e8,1e-3)]
    public void TrimIntervalsAreInvariantUnderScaleRotationAndTranslation(double scale,double tolerance)
    {
        const double angle=.9272952180016122;
        CadPoint Transform(double x,double y)=>P(scale*(13+x*Math.Cos(angle)-y*Math.Sin(angle)),scale*(-27+x*Math.Sin(angle)+y*Math.Cos(angle)));
        Curve Cutter(double x)=>Curve.Segment(Transform(x,-4),Transform(x,4));
        var target=Curve.Segment(Transform(0,0),Transform(10,0));
        var result=Planner.TrimAtPoint(target,new[]{Cutter(7),Cutter(3)},Transform(5,0),tolerance);
        Assert.Equal(Status.ReadyGeometry,result.Status);Interval(.3,.7,Assert.Single(result.Removed));
        var split=Planner.SplitAtPoint(target,Transform(4,0),tolerance);
        Assert.Equal(Status.ReadyGeometry,split.Status);Interval(0,.4,split.Retained[0]);Interval(.4,1,split.Retained[1]);
    }
    [Theory]
    [InlineData(90)] [InlineData(-90)] [InlineData(270)] [InlineData(-270)]
    public void ArcIntervalsFollowTheSignedFiniteSweep(double sweep)
    {
        var target=Curve.Arc(P(0,0),10,17,sweep);
        Curve Radial(double parameter)
        {
            var point=target.PointAt(parameter);
            return Curve.Segment(P(point.X*.8,point.Y*.8),P(point.X*1.2,point.Y*1.2));
        }
        var result=Planner.TrimAtPoint(target,new[]{Radial(.25),Radial(.75)},target.PointAt(.5),Tol);
        Assert.Equal(Status.ReadyGeometry,result.Status);Interval(.25,.75,Assert.Single(result.Removed));
        var split=Planner.SplitAtPoint(target,target.PointAt(.6),Tol);
        Assert.Equal(Status.ReadyGeometry,split.Status);Interval(0,.6,split.Retained[0]);Interval(.6,1,split.Retained[1]);
    }
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(.95)]
    public void CircleTrimCrossesTheSeamAsOneDirectedInterval(double hit)
    {
        var result=Planner.Trim(Curve.Circle(P(0,0),10),new[]{Curve.Segment(P(0,-20),P(0,20))},hit,Tol);
        Assert.Equal(Status.ReadyGeometry,result.Status);
        Interval(.75,1.25,Assert.Single(result.Removed));Assert.True(result.Removed[0].WrapsSeam);
        Interval(.25,.75,Assert.Single(result.Retained));
        Assert.Equal(1d,result.Removed[0].ParameterLength+result.Retained[0].ParameterLength,9);
    }
    [Fact]
    public void CircleNonSeamHitKeepsTheWrappedComplement()
    {
        var result=Planner.TrimAtPoint(Curve.Circle(P(0,0),10),new[]{Curve.Segment(P(0,-20),P(0,20))},P(-10,0),Tol);
        Assert.Equal(Status.ReadyGeometry,result.Status);Interval(.25,.75,Assert.Single(result.Removed));
        Interval(.75,1.25,Assert.Single(result.Retained));
    }
    [Fact]
    public void DuplicateCuttersAndEndpointCutsDoNotCreateZeroLengthPieces()
    {
        var result=Planner.Trim(Curve.Segment(P(0,0),P(10,0)),new[]{Vertical(0),Vertical(7),Vertical(3),Vertical(3),Vertical(10)},.5,Tol);
        Assert.Equal(Status.ReadyGeometry,result.Status);Assert.Equal(4,result.Cuts.Count);
        Interval(.3,.7,Assert.Single(result.Removed));Assert.Equal(2,result.Retained.Count);
        Assert.All(result.Removed.Concat(result.Retained),i=>Assert.True(i.ParameterLength>0));
    }
    [Fact]
    public void NearDuplicateCutsUseAnAbsoluteMillimetreBudget()
    {
        var line=Curve.Segment(P(0,0),P(10,0));
        var result=Planner.Trim(line,new[]{Vertical(3),Vertical(3+Tol/2),Vertical(7)},.5,Tol);
        Assert.Equal(Status.ReadyGeometry,result.Status);Assert.Equal(2,result.Cuts.Count);Interval(.3,.7,Assert.Single(result.Removed));
        var distinct=Planner.Trim(line,new[]{Vertical(3),Vertical(3+2*Tol),Vertical(7)},.5,Tol);
        Assert.Equal(3,distinct.Cuts.Count);
    }
    [Fact]
    public void CircleSeamDuplicateCutsAreCanonicalizedToZero()
    {
        var circle=Curve.Circle(P(0,0),10);
        var result=Planner.Trim(circle,new[]{Curve.Segment(P(0,0),P(20,0)),Curve.Segment(P(0,0),P(20,-Tol/2)),Curve.Segment(P(0,0),P(-20,0))},.25,Tol);
        Assert.Equal(Status.ReadyGeometry,result.Status);Assert.Equal(2,result.Cuts.Count);
        Assert.Equal(0,result.Cuts[0].Parameter);Interval(0,.5,Assert.Single(result.Removed));
    }
    [Fact]
    public void DistinctCutsAcrossTheCircleSeamAreNotMergedByIndependentEndpointSnapping()
    {
        var circle=Curve.Circle(P(0,0),10);double angle=.75*Tol/10;
        Curve Ray(double a)=>Curve.Segment(P(0,0),P(20*Math.Cos(a),20*Math.Sin(a)));
        var result=Planner.Trim(circle,new[]{Ray(angle),Ray(-angle),Ray(Math.PI)},.25,Tol);
        Assert.Equal(Status.ReadyGeometry,result.Status);Assert.Equal(3,result.Cuts.Count);
        Assert.True(result.Cuts[0].Parameter>0);Assert.True(result.Cuts[2].Parameter<1);
        var seamInterval=result.Retained.Single(i=>i.WrapsSeam);
        Assert.InRange(seamInterval.ParameterLength*20*Math.PI,1.4*Tol,1.6*Tol);
    }
    [Theory]
    [InlineData(0)] [InlineData(1)]
    public void ZeroOrOneUsefulIntersectionRequiresAnOracle(int count)
    {
        var line=Curve.Segment(P(0,0),P(10,0));
        var result=Planner.Trim(line,count==0 ? Array.Empty<Curve>() : new[]{Vertical(5)},.3,Tol);
        Assert.Equal(Status.RequiresOracle,result.Status);Assert.Equal(Reason.TooFewCuts,result.Reason);Safe(result);
        var circle=Planner.Trim(Curve.Circle(P(0,0),10),count==0 ? Array.Empty<Curve>() : new[]{Curve.Segment(P(0,0),P(20,0))},.3,Tol);
        Assert.Equal(Status.RequiresOracle,circle.Status);Assert.Equal(Reason.TooFewCuts,circle.Reason);Safe(circle);
    }
    [Fact]
    public void TangencyIsNotSilentlyUsedAsATrimBoundary()
    {
        var circle=Curve.Circle(P(0,0),10);
        var result=Planner.Trim(circle,new[]{Curve.Segment(P(-20,10),P(20,10)),Curve.Segment(P(-20,0),P(20,0))},.3,Tol);
        Assert.Equal(Status.RequiresOracle,result.Status);Assert.Equal(Reason.TangentContact,result.Reason);Safe(result);
        Assert.Equal(3,result.Cuts.Count);Assert.Single(result.Cuts.Where(c=>c.IsTangent));
    }
    [Fact]
    public void CoincidentAndOverlappingCuttersAreExplicitAndNeverProduceArbitraryBoundaries()
    {
        var line=Curve.Segment(P(0,0),P(10,0));
        foreach(var cutter in new[]{Curve.Segment(P(10,0),P(0,0)),Curve.Segment(P(3,0),P(7,0))})
        {
            var result=Planner.Trim(line,new[]{Vertical(3),cutter,Vertical(7)},.5,Tol);
            Assert.Equal(Status.AmbiguousLocus,result.Status);Assert.Equal(Reason.OverlapOrCoincidence,result.Reason);Safe(result);
            Assert.Equal(new[]{1},result.AmbiguousCutterIndices.ToArray());Assert.Equal(2,result.Cuts.Count);
        }
        var circle=Planner.Trim(Curve.Circle(P(0,0),10),new[]{Curve.Arc(P(0,0),10,0,90)},.5,Tol);
        Assert.Equal(Status.AmbiguousLocus,circle.Status);Assert.Empty(circle.Cuts);Safe(circle);
    }
    [Theory]
    [InlineData(0)] [InlineData(.3)] [InlineData(.7)] [InlineData(1)]
    public void AHitAtAnEndpointOrCutDoesNotPickAnArbitrarySide(double hit)
    {
        var result=Planner.Trim(Curve.Segment(P(0,0),P(10,0)),new[]{Vertical(3),Vertical(7)},hit,Tol);
        Assert.Equal(Status.AmbiguousHit,result.Status);Assert.Equal(Reason.HitOnBoundary,result.Reason);Safe(result);
    }
    [Fact]
    public void CircleBoundaryHitRespectsTheSeam()
    {
        var result=Planner.Trim(Curve.Circle(P(0,0),10),new[]{Curve.Segment(P(-20,0),P(20,0))},1,Tol);
        Assert.Equal(Status.AmbiguousHit,result.Status);Safe(result);
    }
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(1e-10)] [InlineData(.9999999999)]
    public void SplitAtAnEndpointIsAnExplicitNoChange(double parameter)
    {
        var result=Planner.Split(Curve.Segment(P(0,0),P(10,0)),parameter,Tol);
        Assert.Equal(Status.NoChange,result.Status);Assert.Equal(Reason.EndpointSplit,result.Reason);
        Assert.Empty(result.Removed);Interval(0,1,Assert.Single(result.Retained));
    }
    [Fact]
    public void SplitLineReturnsTwoPiecesWithoutRemovingGeometry()
    {
        var result=Planner.SplitAtPoint(Curve.Segment(P(0,0),P(10,0)),P(4,0),Tol);
        Assert.Equal(Status.ReadyGeometry,result.Status);Assert.Empty(result.Removed);Assert.Equal(2,result.Retained.Count);
        Interval(0,.4,result.Retained[0]);Interval(.4,1,result.Retained[1]);
        var circle=Planner.Split(Curve.Circle(P(0,0),10),.25,Tol);
        Assert.Equal(Status.RequiresOracle,circle.Status);Assert.Equal(Reason.ClosedCurveSingleSplit,circle.Reason);Safe(circle);
    }
    [Fact]
    public void PointValidationRejectsExtendedSupportsAndOffCurvePoints()
    {
        var line=Curve.Segment(P(0,0),P(10,0));var arc=Curve.Arc(P(0,0),10,0,90);
        Assert.ThrowsAny<ArgumentException>(()=>Planner.SplitAtPoint(line,P(11,0),Tol));
        Assert.ThrowsAny<ArgumentException>(()=>Planner.SplitAtPoint(line,P(5,.1),Tol));
        Assert.ThrowsAny<ArgumentException>(()=>Planner.SplitAtPoint(arc,P(-10,0),Tol));
        Assert.ThrowsAny<ArgumentException>(()=>Planner.TrimAtPoint(arc,Array.Empty<Curve>(),P(0,-10),Tol));
        Assert.ThrowsAny<ArgumentException>(()=>Planner.SplitAtPoint(Curve.Circle(P(0,0),10),P(0,0),Tol));
        Assert.ThrowsAny<ArgumentException>(()=>Planner.SplitAtPoint(arc,P(double.NaN,0),Tol));
    }
    [Fact]
    public void NearlyClosedArcPointNeedsExplicitEndpointDisambiguation()
    {
        var arc=Curve.Arc(P(0,0),10,0,359.9999999);
        Assert.ThrowsAny<ArgumentException>(()=>Planner.SplitAtPoint(arc,P(10,0),Tol));
        Assert.Equal(Status.NoChange,Planner.Split(arc,0,Tol).Status);
        Assert.Equal(Status.NoChange,Planner.Split(arc,1,Tol).Status);
    }
    [Fact]
    public void InvalidParametersToleranceAndDegenerateGeometryAreRefused()
    {
        var line=Curve.Segment(P(0,0),P(10,0));
        foreach(double bad in new[]{-.1,1.1,double.NaN,double.PositiveInfinity})
        {
            Assert.ThrowsAny<ArgumentException>(()=>Planner.Trim(line,Array.Empty<Curve>(),bad,Tol));
            Assert.ThrowsAny<ArgumentException>(()=>Planner.Split(line,bad,Tol));
        }
        foreach(double bad in new[]{0d,-1,double.NaN,double.PositiveInfinity}) Assert.ThrowsAny<ArgumentException>(()=>Planner.Split(line,.5,bad));
        Assert.ThrowsAny<ArgumentException>(()=>Planner.Trim(line,null!,.5,Tol));
        Assert.ThrowsAny<ArgumentException>(()=>Planner.Trim(line,new Curve[]{null!},.5,Tol));
        Assert.ThrowsAny<ArgumentException>(()=>Planner.Split(null!,.5,Tol));
        Assert.ThrowsAny<ArgumentException>(()=>Planner.Split(Curve.Segment(P(0,0),P(1e-8,0)),.5,Tol));
    }
}
