using InventorXrSo.Core.Backend;

namespace XrSo.Core.Tests.Backend;

public class NativeSketchDimensionRulesTests
{
    private static SketchEntityRef Ref(string id,string type,SketchSubpart part=SketchSubpart.Entity)
        =>new SketchEntityRef("doc","r","ent_sketch","ent_"+id,type,part);
    [Fact]
    public void LineAndItsDistinctEndpointsOfferDistancesButDuplicatesDoNot()
    {
        var line=Ref("a","line");
        Assert.Equal(new[] {"aligned","horizontal","vertical"},NativeSketchDimensionRules.Variants(new[] {line}));
        Assert.Equal(new[] {"aligned","horizontal","vertical"},NativeSketchDimensionRules.Variants(new[] {line.WithSubpart(SketchSubpart.Start),line.WithSubpart(SketchSubpart.End)}));
        Assert.Empty(NativeSketchDimensionRules.Variants(new[] {line,line}));
        var point=Ref("p","point");
        Assert.Empty(NativeSketchDimensionRules.Variants(new[] {point,point.WithSubpart(SketchSubpart.Point)}));
    }
    [Fact]
    public void OffsetRequiresLineFirstAndAngleKeepsThreePointOrder()
    {
        var line=Ref("a","line");var point=Ref("b","point");
        Assert.Contains("offset",NativeSketchDimensionRules.Variants(new[] {line,point}));
        Assert.Empty(NativeSketchDimensionRules.Variants(new[] {point,line}));
        Assert.Contains("angle_three_points",NativeSketchDimensionRules.Variants(new[] {point,Ref("c","point"),Ref("d","point")}));
        Assert.True(NativeSketchDimensionRules.Angular("angle_three_points"));
        Assert.False(NativeSketchDimensionRules.Angular("arc_length"));
    }
    [Fact]
    public void CurvesOfferTheirNativeDimensionsAndNeverApproximateSplinesAsArcs()
    {
        Assert.Equal(new[] {"radius","diameter","arc_length"},NativeSketchDimensionRules.Variants(new[] {Ref("a","arc")}));
        Assert.Equal(new[] {"ellipse_major","ellipse_minor"},NativeSketchDimensionRules.Variants(new[] {Ref("a","ellipse")}));
        Assert.Empty(NativeSketchDimensionRules.Variants(new[] {Ref("a","spline")}));
        Assert.Equal(new[] {"offset_spline"},NativeSketchDimensionRules.Variants(new[] {Ref("a","offset_spline")}));
        Assert.Contains("tangent_distance",NativeSketchDimensionRules.Variants(new[] {Ref("a","line"),Ref("b","circle")}));
        Assert.DoesNotContain("tangent_distance",NativeSketchDimensionRules.Variants(new[] {Ref("a","line"),Ref("b","line")}));
    }
}
