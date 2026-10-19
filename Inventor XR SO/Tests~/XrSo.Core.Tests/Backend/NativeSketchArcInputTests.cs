using InventorXrSo.Core.Backend;

namespace XrSo.Core.Tests.Backend;

public class NativeSketchArcInputTests
{
    private static CadPoint At(double angle,double radius=10)=>new(13+radius*Math.Cos(angle*Math.PI/180),21+radius*Math.Sin(angle*Math.PI/180));
    private static NativeSketchArcInput Arc(double start=0)=>new(new CadPoint(13,21),At(start));
    [Theory][InlineData(1,90)][InlineData(-1,90)][InlineData(1,270)][InlineData(-1,270)]
    public void SweepFollowsMotionInBothDirectionsBeyondHalfTurn(int sign,int extent)
    {
        var arc=Arc();for(int angle=90;angle<extent;angle+=90) Assert.True(arc.Observe(At(sign*angle)));
        Assert.True(arc.TryEnd(At(sign*extent,3),out double sweep));Assert.Equal(sign*extent,sweep,8);
        Assert.Equal(10,arc.RadiusMm,8);Assert.Equal(0,arc.StartDegrees,8);
    }
    [Theory][InlineData(170,-170,20)][InlineData(-170,170,-20)]
    public void CrossesAtanBranchWithoutChangingDirection(double start,double end,double expected)
    {var arc=Arc(start);Assert.True(arc.TryEnd(At(end),out var sweep));Assert.Equal(expected,sweep,8);}
    [Fact]
    public void ReversingMotionCanReturnAcrossTheStart()
    {var arc=Arc();arc.Observe(At(90));arc.Observe(At(45));Assert.True(arc.TryEnd(At(-10),out var sweep));Assert.Equal(-10,sweep,8);}
    [Theory][InlineData(1)][InlineData(-1)]
    public void SuspendedInputPreservesDirectionWithoutCountingAnUnobservedJump(int sign)
    {var arc=Arc();arc.Observe(At(sign*90));arc.Suspend();Assert.True(arc.TryEnd(At(sign*270),out var sweep));Assert.Equal(sign*270,sweep,8);}
    [Fact]
    public void CenterAndZeroSweepDoNotFinishAndAFullTurnCanBeReduced()
    {
        var arc=Arc();Assert.False(arc.TryEnd(new CadPoint(13,21),out _));Assert.False(arc.TryEnd(At(0),out _));
        arc.Observe(At(90));arc.Observe(At(180));arc.Observe(At(270));Assert.False(arc.TryEnd(At(360),out _));
        Assert.True(arc.TryEnd(At(270),out var sweep));Assert.Equal(270,sweep,8);
    }
    [Theory][InlineData(0)][InlineData(100001)]
    public void InvalidStartRadiusIsRefused(double radius)
    {Assert.Throws<ArgumentException>(()=>new NativeSketchArcInput(new CadPoint(0,0),new CadPoint(radius,0)));}
}
