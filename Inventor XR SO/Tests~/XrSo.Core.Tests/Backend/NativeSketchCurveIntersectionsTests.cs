using System;
using System.Linq;
using InventorXrSo.Core.Backend;
using Xunit;
using Geometry = InventorXrSo.Core.Backend.NativeSketchCurveIntersections;
using Curve = InventorXrSo.Core.Backend.NativeSketchCurveIntersections.Curve;
using Kind = InventorXrSo.Core.Backend.NativeSketchCurveIntersections.IntersectionKind;

namespace XrSo.Core.Tests.Backend;

public sealed class NativeSketchCurveIntersectionsTests
{
    private const double Tol=1e-7;
    private static CadPoint P(double x,double y)=>new CadPoint(x,y);
    private static void Near(CadPoint expected,CadPoint actual,double tolerance=Tol)
    { Assert.InRange(Math.Abs(actual.X-expected.X),0,tolerance);Assert.InRange(Math.Abs(actual.Y-expected.Y),0,tolerance); }
    private static Geometry.Result Both(Curve a,Curve b,Kind expected,int count,double tolerance=Tol)
    {
        var result=Geometry.Intersect(a,b,tolerance);var reverse=Geometry.Intersect(b,a,tolerance);
        Assert.Equal(expected,result.Kind);Assert.Equal(expected,reverse.Kind);
        Assert.Equal(count,result.Points.Count);Assert.Equal(count,reverse.Points.Count);
        Assert.True(result.Points.Select(p=>p.FirstParameter).SequenceEqual(result.Points.Select(p=>p.FirstParameter).OrderBy(p=>p)));
        foreach(var point in result.Points)
        {
            Assert.InRange(point.FirstParameter,0,1);Assert.InRange(point.SecondParameter,0,1);
            Near(point.PointMm,a.PointAt(point.FirstParameter),tolerance*1.1);
            Near(point.PointMm,b.PointAt(point.SecondParameter),tolerance*1.1);
            var peer=Assert.Single(reverse.Points.Where(p=>Math.Abs(p.PointMm.X-point.PointMm.X)<=tolerance*1.1 && Math.Abs(p.PointMm.Y-point.PointMm.Y)<=tolerance*1.1));
            Assert.Equal(point.IsTangent,peer.IsTangent);
            Assert.InRange(Math.Abs(point.FirstParameter-peer.SecondParameter),0,1e-6);
            Assert.InRange(Math.Abs(point.SecondParameter-peer.FirstParameter),0,1e-6);
        }
        return result;
    }

    [Theory]
    [InlineData(0,0,0)] [InlineData(53.13,13,-21)] [InlineData(173,-1200,3400)]
    public void SegmentCrossingIsInvariantUnderRigidMotion(double degrees,double dx,double dy)
    {
        double angle=degrees*Math.PI/180;
        CadPoint Transform(double x,double y)=>P(dx+x*Math.Cos(angle)-y*Math.Sin(angle),dy+x*Math.Sin(angle)+y*Math.Cos(angle));
        var result=Both(Curve.Segment(Transform(-10,0),Transform(10,0)),Curve.Segment(Transform(0,-3),Transform(0,7)),Kind.Points,1);
        Near(Transform(0,0),result.Points[0].PointMm);
        Assert.Equal(.5,result.Points[0].FirstParameter,9);Assert.Equal(.3,result.Points[0].SecondParameter,9);
        Assert.False(result.Points[0].IsTangent);
    }
    [Theory]
    [InlineData(1)] [InlineData(-1)]
    public void SegmentDirectionChangesParameterWithoutChangingThePoint(int sign)
    {
        var a=Curve.Segment(P(-4*sign,0),P(8*sign,0));var b=Curve.Segment(P(0,-5),P(0,5));
        var r=Both(a,b,Kind.Points,1);Near(P(0,0),r.Points[0].PointMm);Assert.Equal(1d/3,r.Points[0].FirstParameter,9);
    }
    [Fact]
    public void SegmentEndpointsAndDisjointInfiniteLineIntersectionsAreDistinct()
    {
        var a=Curve.Segment(P(0,0),P(10,0));
        var r=Both(a,Curve.Segment(P(10,0),P(10,5)),Kind.Points,1);
        Assert.Equal(1,r.Points[0].FirstParameter);Assert.Equal(0,r.Points[0].SecondParameter);
        Both(a,Curve.Segment(P(11,-5),P(11,5)),Kind.None,0);
        Both(a,Curve.Segment(P(0,1),P(10,1)),Kind.None,0);
    }
    [Fact]
    public void CollinearOverlapAndCoincidenceNeverInventCutPoints()
    {
        var a=Curve.Segment(P(0,0),P(10,0));
        Both(a,Curve.Segment(P(4,0),P(15,0)),Kind.Overlap,0);
        Both(a,Curve.Segment(P(10,0),P(0,0)),Kind.Coincident,0);
        var contact=Both(a,Curve.Segment(P(10,0),P(12,0)),Kind.Points,1);
        Near(P(10,0),contact.Points[0].PointMm);Assert.True(contact.Points[0].IsTangent);
        Both(a,Curve.Segment(P(11,0),P(12,0)),Kind.None,0);
    }
    [Fact]
    public void NearlyParallelSegmentsStillFindTheirActualCrossing()
    {
        var result=Both(Curve.Segment(P(-100000,0),P(100000,0)),Curve.Segment(P(-100000,-.0001),P(100000,.0001)),Kind.Points,1,1e-9);
        Near(P(0,0),result.Points[0].PointMm,1e-8);
        var extreme=Both(Curve.Segment(P(-1e15,0),P(1e15,0)),Curve.Segment(P(-1e15,-1),P(1e15,1)),Kind.Points,1,1e-5);
        Near(P(0,0),extreme.Points[0].PointMm,1e-5);
        var shifted=Both(Curve.Segment(P(13-1e15,-27),P(13+1e15,-27)),Curve.Segment(P(13-1e15,-28),P(13+1e15,-26)),Kind.Points,1,1e-5);
        Near(P(13,-27),shifted.Points[0].PointMm,1e-5);
    }
    [Theory]
    [InlineData(1e-4,1e-12)] [InlineData(1,1e-7)] [InlineData(1e8,1e-3)]
    public void SegmentCircleHasTwoOrderedAnalyticIntersectionsAtDifferentScales(double scale,double tolerance)
    {
        var r=Both(Curve.Segment(P(-10*scale,0),P(10*scale,0)),Curve.Circle(P(0,0),5*scale),Kind.Points,2,tolerance);
        Near(P(-5*scale,0),r.Points[0].PointMm,tolerance);Near(P(5*scale,0),r.Points[1].PointMm,tolerance);
        Assert.Equal(.25,r.Points[0].FirstParameter,9);Assert.Equal(.75,r.Points[1].FirstParameter,9);
        Assert.All(r.Points,p=>Assert.False(p.IsTangent));
    }
    [Fact]
    public void LineCircleTangentAndFiniteSegmentMembershipAreExplicit()
    {
        var circle=Curve.Circle(P(0,0),5);
        var tangent=Both(Curve.Segment(P(-10,5),P(10,5)),circle,Kind.Points,1);
        Near(P(0,5),tangent.Points[0].PointMm);Assert.True(tangent.Points[0].IsTangent);
        Both(Curve.Segment(P(6,0),P(10,0)),circle,Kind.None,0);
        var end=Both(Curve.Segment(P(5,0),P(10,0)),circle,Kind.Points,1);
        Near(P(5,0),end.Points[0].PointMm);Assert.False(end.Points[0].IsTangent);
    }
    [Fact]
    public void CircleCircleIntersectionsMatchTheIndependentThreeFourFiveTriangle()
    {
        var r=Both(Curve.Circle(P(0,0),5),Curve.Circle(P(6,0),5),Kind.Points,2);
        Near(P(3,4),r.Points[0].PointMm);Near(P(3,-4),r.Points[1].PointMm);
        Assert.All(r.Points,p=>Assert.False(p.IsTangent));
    }
    [Theory]
    [InlineData(23,300,-450)] [InlineData(134,-2300,517)]
    public void RoundIntersectionsRemainCorrectUnderRigidMotion(double degrees,double x,double y)
    {
        double angle=degrees*Math.PI/180;
        CadPoint Transform(double a,double b)=>P(x+a*Math.Cos(angle)-b*Math.Sin(angle),y+a*Math.Sin(angle)+b*Math.Cos(angle));
        var r=Both(Curve.Arc(Transform(0,0),5,degrees,180),Curve.Circle(Transform(6,0),5),Kind.Points,1);
        Near(Transform(3,4),r.Points[0].PointMm);
        var line=Both(Curve.Segment(Transform(-10,0),Transform(10,0)),Curve.Circle(Transform(0,0),5),Kind.Points,2);
        Near(Transform(-5,0),line.Points[0].PointMm);Near(Transform(5,0),line.Points[1].PointMm);
    }
    [Fact]
    public void DistinctSupportArcsFilterBothIntersectionsBySignedTravel()
    {
        var r=Both(Curve.Arc(P(0,0),5,0,270),Curve.Arc(P(6,0),5,270,-270),Kind.Points,1);
        Near(P(3,4),r.Points[0].PointMm);
        Both(Curve.Arc(P(0,0),5,90,-270),Curve.Arc(P(6,0),5,180,180),Kind.Points,1);
    }
    [Theory]
    [InlineData(10,5,5)] [InlineData(3,5,2)]
    public void ExternalAndInternalCircleTangenciesReturnOnePoint(double distance,double firstRadius,double secondRadius)
    {
        var r=Both(Curve.Circle(P(0,0),firstRadius),Curve.Circle(P(distance,0),secondRadius),Kind.Points,1);
        Near(P(5,0),r.Points[0].PointMm);Assert.True(r.Points[0].IsTangent);
    }
    [Fact]
    public void ConcentricAndContainedCirclesHaveNoArbitraryIntersections()
    {
        var a=Curve.Circle(P(3,4),5);
        Both(a,Curve.Circle(P(3,4),5),Kind.Coincident,0);
        Both(a,Curve.Circle(P(3,4),4),Kind.None,0);
        Both(a,Curve.Circle(P(4,4),2),Kind.None,0);
        Both(a,Curve.Circle(P(30,4),5),Kind.None,0);
    }
    [Theory]
    [InlineData(270,0,.6666666666666666)] [InlineData(-270,0,.6666666666666666)]
    [InlineData(90,0,0)] [InlineData(-90,0,0)]
    public void SignedFiniteArcsFilterAndOrderIntersections(double sweep,double start,double lastParameter)
    {
        int count=Math.Abs(sweep)>180 ? 2 : 1;
        var arc=Curve.Arc(P(0,0),5,start,sweep);
        var r=Both(arc,Curve.Segment(P(-10,0),P(10,0)),Kind.Points,count);
        Near(P(5,0),r.Points[0].PointMm);Assert.Equal(0,r.Points[0].FirstParameter,9);
        Assert.Equal(lastParameter,r.Points[count-1].FirstParameter,9);
        if(count==2) Near(P(-5,0),r.Points[1].PointMm);
    }
    [Fact]
    public void ArcCircleIntersectionFiltersOneOfTheTwoSupportIntersections()
    {
        var r=Both(Curve.Arc(P(0,0),5,0,180),Curve.Circle(P(6,0),5),Kind.Points,1);
        Near(P(3,4),r.Points[0].PointMm);
        Both(Curve.Arc(P(0,0),5,100,60),Curve.Circle(P(6,0),5),Kind.None,0);
    }
    [Fact]
    public void SameSupportArcsDistinguishOverlapCoincidenceAndSeamContacts()
    {
        var upper=Curve.Arc(P(0,0),10,0,180);
        Both(upper,Curve.Arc(P(0,0),10,180,-180),Kind.Coincident,0);
        Both(upper,Curve.Arc(P(0,0),10,90,180),Kind.Overlap,0);
        Both(upper,Curve.Circle(P(0,0),10),Kind.Overlap,0);
        var ends=Both(upper,Curve.Arc(P(0,0),10,180,180),Kind.Points,2);
        Near(P(10,0),ends.Points[0].PointMm);Near(P(-10,0),ends.Points[1].PointMm);
        Assert.All(ends.Points,p=>Assert.True(p.IsTangent));
        Both(Curve.Arc(P(0,0),10,350,20),Curve.Arc(P(0,0),10,355,5),Kind.Overlap,0);
        Both(Curve.Arc(P(0,0),10,10,20),Curve.Arc(P(0,0),10,50,20),Kind.None,0);
    }
    [Theory]
    [InlineData(270)] [InlineData(-270)]
    public void OverlappingMajorArcsAreAmbiguousEvenAcrossTheAngularSeam(double sweep)
    { Both(Curve.Arc(P(12,-4),3,350,sweep),Curve.Arc(P(12,-4),3,170,sweep),Kind.Overlap,0); }
    [Fact]
    public void ArcEndpointParameterIsOneRatherThanWrappingToZero()
    {
        var r=Both(Curve.Arc(P(0,0),5,270,90),Curve.Segment(P(5,0),P(10,0)),Kind.Points,1);
        Assert.Equal(1,r.Points[0].FirstParameter,9);Near(P(5,0),r.Points[0].PointMm);
    }
    [Fact]
    public void MillimetreToleranceControlsEndpointContactWithoutExtendingTheSegmentIndefinitely()
    {
        var a=Curve.Segment(P(0,0),P(10,0));var b=Curve.Segment(P(10+1e-5,-1),P(10+1e-5,1));
        Both(a,b,Kind.None,0,1e-6);Both(a,b,Kind.Points,1,1e-4);
    }
    [Fact]
    public void NearTangenciesUseTheDistanceToleranceEvenForAlmostConcentricCircles()
    {
        var circle=Curve.Circle(P(0,0),5);
        var line=Both(circle,Curve.Segment(P(-10,5+2e-6),P(10,5+2e-6)),Kind.Points,1,1e-5);
        Assert.True(line.Points[0].IsTangent);
        var outside=Both(circle,Curve.Circle(P(10+2e-6,0),5),Kind.Points,1,1e-5);
        Assert.True(outside.Points[0].IsTangent);
        var inside=Both(Curve.Circle(P(0,0),100),Curve.Circle(P(.001,0),99.999+2e-7),Kind.Points,1,1e-6);
        Assert.True(inside.Points[0].IsTangent);
        Near(P(100,0),inside.Points[0].PointMm,1e-6);
        Both(circle,Curve.Circle(P(10+2e-6,0),5),Kind.None,0,1e-7);
    }
    [Theory]
    [InlineData(-2,2,false)] [InlineData(-.5,1,true)]
    [InlineData(.5,1,true)] [InlineData(2,0,false)]
    public void PerturbedExternalTangenciesRespectBothSidesOfTheTolerance(double offset,int count,bool tangent)
    {
        const double tolerance=1e-5;
        var circle=Curve.Circle(P(0,0),5);var expected=count==0 ? Kind.None : Kind.Points;
        var line=Both(Curve.Segment(P(-10,5+offset*tolerance),P(10,5+offset*tolerance)),circle,expected,count,tolerance);
        var round=Both(circle,Curve.Circle(P(10+offset*tolerance,0),5),expected,count,tolerance);
        Assert.All(line.Points,p=>Assert.Equal(tangent,p.IsTangent));
        Assert.All(round.Points,p=>Assert.Equal(tangent,p.IsTangent));
    }
    [Theory]
    [InlineData(-2,0,false)] [InlineData(-.5,1,true)]
    [InlineData(.5,1,true)] [InlineData(2,2,false)]
    public void PerturbedInternalTangenciesDistinguishContainmentAndCrossing(double offset,int count,bool tangent)
    {
        const double tolerance=1e-5;
        var result=Both(Curve.Circle(P(0,0),5),Curve.Circle(P(3+offset*tolerance,0),2),count==0 ? Kind.None : Kind.Points,count,tolerance);
        Assert.All(result.Points,p=>Assert.Equal(tangent,p.IsTangent));
    }
    [Fact]
    public void InvalidAndToleranceDegenerateInputsAreRefused()
    {
        var good=Curve.Circle(P(0,0),5);
        foreach(double invalid in new[] {double.NaN,double.PositiveInfinity,double.NegativeInfinity})
        {
            Assert.ThrowsAny<ArgumentException>(()=>Curve.Segment(P(invalid,0),P(1,2)));
            Assert.ThrowsAny<ArgumentException>(()=>Curve.Circle(P(0,invalid),1));
            Assert.ThrowsAny<ArgumentException>(()=>Curve.Circle(P(0,0),invalid));
            Assert.ThrowsAny<ArgumentException>(()=>Curve.Arc(P(0,0),1,invalid,20));
            Assert.ThrowsAny<ArgumentException>(()=>Curve.Arc(P(0,0),1,0,invalid));
            Assert.ThrowsAny<ArgumentException>(()=>Geometry.Intersect(good,good,invalid));
        }
        Assert.ThrowsAny<ArgumentException>(()=>Curve.Segment(P(1,2),P(1,2)));
        Assert.ThrowsAny<ArgumentException>(()=>Curve.Circle(P(0,0),0));
        Assert.ThrowsAny<ArgumentException>(()=>Curve.Circle(P(0,0),-1));
        foreach(double sweep in new[] {0d,360d,-360d,720d}) Assert.ThrowsAny<ArgumentException>(()=>Curve.Arc(P(0,0),1,0,sweep));
        Assert.ThrowsAny<ArgumentException>(()=>Geometry.Intersect(good,good,0));
        Assert.ThrowsAny<ArgumentException>(()=>Geometry.Intersect(good,good,-1));
        Assert.ThrowsAny<ArgumentException>(()=>Geometry.Intersect(Curve.Segment(P(0,0),P(1e-8,0)),good,Tol));
        Assert.ThrowsAny<ArgumentException>(()=>Geometry.Intersect(Curve.Circle(P(0,0),1e-8),good,Tol));
        Assert.ThrowsAny<ArgumentException>(()=>Geometry.Intersect(Curve.Arc(P(0,0),1,0,1e-8),good,Tol));
        Assert.ThrowsAny<ArgumentException>(()=>Geometry.Intersect(null!,good,Tol));
        Assert.ThrowsAny<ArgumentException>(()=>good.PointAt(double.NaN));
        Assert.ThrowsAny<ArgumentException>(()=>good.PointAt(-.1));
        Assert.ThrowsAny<ArgumentException>(()=>good.PointAt(1.1));
        Assert.ThrowsAny<ArgumentException>(()=>Curve.Segment(P(-double.MaxValue,0),P(double.MaxValue,0)));
    }
}
