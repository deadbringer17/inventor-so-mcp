using Bimwright.Ipt.Shared.Handlers.Core;

namespace Bimwright.Ipt.Tests;

public sealed class TangentArcGeometryTests
{
    [Theory]
    [InlineData(10,10,0,10,10,true)]
    [InlineData(10,-10,0,-10,10,false)]
    [InlineData(20,10,0,25,25,true)]
    [InlineData(-10,10,0,10,10,true)]
    public void CirclePassesThroughTargetWithCorrectOutgoingTangent(double endX,double endY,double cx,double cy,double radius,bool ccw)
    {
        var arc=TangentArcGeometry.Create(0,0,1,0,endX,endY);
        Assert.Equal(cx,arc.CenterX,10);Assert.Equal(cy,arc.CenterY,10);
        Assert.Equal(radius,arc.Radius,10);Assert.Equal(ccw,arc.CounterClockwise);
    }
    [Theory]
    [InlineData(0,.01)] [InlineData(37,1)] [InlineData(90,250)] [InlineData(180,1)] [InlineData(-122,.5)]
    public void TranslatedRotatedAndScaledGeometryPreservesEndpointsAndTangent(double degrees,double scale)
    {
        double angle=degrees*Math.PI/180,tx=Math.Cos(angle),ty=Math.Sin(angle),x=123.4,y=-42.7;
        double qx=x+scale*(7*tx-11*ty),qy=y+scale*(7*ty+11*tx);
        var arc=TangentArcGeometry.Create(x,y,tx,ty,qx,qy);
        double dx=x-arc.CenterX,dy=y-arc.CenterY;
        Assert.InRange(Math.Abs(Math.Sqrt(dx*dx+dy*dy)-arc.Radius),0,1e-8);
        Assert.InRange(Math.Abs(Math.Sqrt(Math.Pow(qx-arc.CenterX,2)+Math.Pow(qy-arc.CenterY,2))-arc.Radius),0,1e-8);
        double sign=arc.CounterClockwise?1:-1;
        Assert.InRange((-dy*tx+dx*ty)*sign/arc.Radius,1-1e-10,1+1e-10);
    }
    [Fact]
    public void TangentNormalizationDoesNotOverflow()
    {
        var arc=TangentArcGeometry.Create(0,0,1e308,1e308,1,0);
        Assert.Equal(.5,arc.CenterX,10);Assert.Equal(-.5,arc.CenterY,10);
        Assert.False(arc.CounterClockwise);
    }
    [Theory]
    [InlineData(0,0,1,0,0,0)] [InlineData(0,0,0,0,10,10)]
    [InlineData(0,0,1,0,10,0)] [InlineData(0,0,1,0,-10,0)]
    [InlineData(0,0,1,0,10,1e-12)] [InlineData(0,0,1,0,1e-9,1e-9)]
    [InlineData(-1e308,0,1,0,1e308,10)] [InlineData(0,0,1,0,1e308,1e300)]
    public void DegenerateOrOverflowingGeometryIsRejected(double x,double y,double tx,double ty,double qx,double qy)
        =>Assert.Throws<ArgumentException>(()=>TangentArcGeometry.Create(x,y,tx,ty,qx,qy));
    [Fact]
    public void EveryInputRejectsNonFiniteValues()
    {
        foreach(double bad in new[]{double.NaN,double.PositiveInfinity,double.NegativeInfinity})
        for(int index=0;index<6;index++)
        {
            double[] values={0,0,1,0,10,10};values[index]=bad;
            Assert.Throws<ArgumentException>(()=>TangentArcGeometry.Create(values[0],values[1],values[2],values[3],values[4],values[5]));
        }
    }
}
