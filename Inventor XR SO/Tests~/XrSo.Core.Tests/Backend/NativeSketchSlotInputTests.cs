using InventorXrSo.Core.Backend;
using Xunit;

namespace XrSo.Core.Tests.Backend;

public sealed class NativeSketchSlotInputTests
{
    [Theory]
    [InlineData(90,24,-3,10)] [InlineData(-90,24,3,10)]
    [InlineData(90,-3,24,10)] [InlineData(-90,-3,-24,10)]
    [InlineData(270,0,-23,6)] [InlineData(-270,0,23,6)]
    [InlineData(90,0,0,40)] [InlineData(90,23,0,6)]
    public void ArcWidthUsesTheFiniteArcAndBothCaps(double sweep,double x,double y,double expected)
    {
        // The first two points are beyond the start cap; the next two beyond the end cap.
        foreach(double angle in new[] {0d,.9272952180016122,2.7})
        {
            double ux=System.Math.Cos(angle),uy=System.Math.Sin(angle);var center=new CadPoint(13,21);
            var start=center+new CadPoint(20*ux,20*uy);
            var point=center+new CadPoint(x*ux-y*uy,x*uy+y*ux);
            Assert.True(NativeSketchSlotInput.TryArcWidth(center,start,sweep,point,out var width));
            Assert.Equal(expected,width,8);
        }
    }
    [Theory]
    [InlineData(90)] [InlineData(-90)] [InlineData(180)] [InlineData(-180)] [InlineData(270)] [InlineData(-270)]
    public void ThreePointArcPreservesEndpointOrderAndTheSideOfTheThroughPoint(double expected)
    {
        foreach(double angle in new[] {0d,.9272952180016122,2.7})
        {
            var center=new CadPoint(13,21);double sweep=expected*System.Math.PI/180;
            CadPoint Point(double offset)=>center+new CadPoint(20*System.Math.Cos(angle+offset),20*System.Math.Sin(angle+offset));
            Assert.True(NativeSketchSlotInput.TryThreePointArc(Point(0),Point(sweep),Point(sweep/2),out var actual,out var actualSweep));
            Assert.Equal(center.X,actual.X,8);Assert.Equal(center.Y,actual.Y,8);Assert.Equal(expected,actualSweep,8);
        }
    }
    [Fact]
    public void InvalidArcAndInvalidWidthRemainUnaccepted()
    {
        var center=new CadPoint(0,0);var start=new CadPoint(20,0);
        foreach(double sweep in new[] {0d,360d,-360d,double.NaN,double.PositiveInfinity})
            Assert.False(NativeSketchSlotInput.TryArcWidth(center,start,sweep,new CadPoint(23,0),out _));
        Assert.False(NativeSketchSlotInput.TryArcWidth(center,center,90,start,out _));
        Assert.False(NativeSketchSlotInput.TryArcWidth(center,start,90,start,out _));
        Assert.False(NativeSketchSlotInput.TryArcWidth(center,start,90,new CadPoint(100000,100000),out _));
        Assert.False(NativeSketchSlotInput.TryThreePointArc(center,start,new CadPoint(10,0),out _,out _));
        Assert.False(NativeSketchSlotInput.TryThreePointArc(center,center,start,out _,out _));
        Assert.False(NativeSketchSlotInput.TryThreePointArc(center,start,start,out _,out _));
    }
    [Theory]
    [InlineData("slot_center_to_center",20,3,6)]
    [InlineData("slot_center_to_center",20,-3,6)]
    [InlineData("slot_center_to_center",44,3,10)]
    [InlineData("slot_center_to_center",-4,-3,10)]
    [InlineData("slot_center",-20,3,6)]
    [InlineData("slot_center",-44,3,10)]
    [InlineData("slot_center",44,-3,10)]
    [InlineData("slot_overall",2,4,8)]
    [InlineData("slot_overall",-4,-3,6)]
    [InlineData("slot_overall",44,3,6)]
    public void WidthFollowsNativeVariantAndIsInvariantUnderRotation(string variant,double along,double across,double expected)
    {
        foreach(double angle in new[] {0d,.9272952180016122,2.7})
        {
            double ux=System.Math.Cos(angle),uy=System.Math.Sin(angle);
            var first=new CadPoint(13,21);var second=first+new CadPoint(40*ux,40*uy);
            var point=first+new CadPoint(along*ux-across*uy,along*uy+across*ux);
            Assert.True(NativeSketchSlotInput.TryWidth(variant,first,second,point,out var width));
            Assert.Equal(expected,width,8);
        }
    }
    [Theory]
    [InlineData("slot_center_to_center")][InlineData("slot_center")][InlineData("slot_overall")]
    public void DegenerateAxisZeroWidthAndWidthBudgetAreRefused(string variant)
    {
        Assert.False(NativeSketchSlotInput.TryWidth(variant,new CadPoint(0,0),new CadPoint(0,0),new CadPoint(0,3),out _));
        Assert.False(NativeSketchSlotInput.TryWidth(variant,new CadPoint(0,0),new CadPoint(40,0),new CadPoint(20,0),out _));
        Assert.False(NativeSketchSlotInput.TryWidth(variant,new CadPoint(0,0),new CadPoint(40,0),new CadPoint(20,50001),out _));
        Assert.True(NativeSketchSlotInput.TryWidth(variant,new CadPoint(0,0),new CadPoint(40,0),new CadPoint(20,50000),out var width));
        Assert.Equal(100000,width);
    }
    [Fact]
    public void UnrelatedVariantDoesNotInterpretAWidth()
    {Assert.False(NativeSketchSlotInput.TryWidth("ellipse",new CadPoint(0,0),new CadPoint(40,0),new CadPoint(20,3),out _));}
}
