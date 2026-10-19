using InventorXrSo.Core.Backend;

namespace XrSo.Core.Tests.Backend;

public class NativeSketchConstraintRulesTests
{
    private static SketchEntityRef Ref(string id,string type,SketchSubpart part=SketchSubpart.Entity)
        =>new SketchEntityRef("doc","r","ent_sketch","ent_"+id,type,part);
    [Theory]
    [InlineData("parallel")][InlineData("collinear")][InlineData("perpendicular")][InlineData("equal")]
    public void DirectionalPairsRequireDistinctWholeEntities(string kind)
    {
        var a=Ref("a","line"); var b=Ref("b","line");
        Assert.True(NativeSketchConstraintRules.Accepts(kind,new[] {a,b}));
        Assert.False(NativeSketchConstraintRules.Accepts(kind,new[] {a,a}));
        Assert.False(NativeSketchConstraintRules.Accepts(kind,new[] {a,b.WithSubpart(SketchSubpart.End)}));
        Assert.False(NativeSketchConstraintRules.Accepts(kind,new[] {a}));
    }
    [Fact]
    public void PointRelationsAcceptOrderedEndpointsAndDistinctSubparts()
    {
        var line=Ref("a","line"); var start=line.WithSubpart(SketchSubpart.Start); var end=line.WithSubpart(SketchSubpart.End);
        foreach(var kind in new[] {"coincident","horizontal","vertical"})
            Assert.True(NativeSketchConstraintRules.Accepts(kind,new[] {start,end}));
        Assert.True(NativeSketchConstraintRules.Accepts("coincident",new[] {start,Ref("b","circle")}));
        Assert.False(NativeSketchConstraintRules.Accepts("coincident",new[] {line,Ref("b","circle")}));
        var point=Ref("p","point");
        Assert.False(NativeSketchConstraintRules.Accepts("coincident",new[] {point,point.WithSubpart(SketchSubpart.Point)}));
    }
    [Fact]
    public void CurvatureRequiresSplineAndDoesNotAliasTangent()
    {
        var line=Ref("a","line"); var circle=Ref("b","circle"); var spline=Ref("c","spline");
        Assert.True(NativeSketchConstraintRules.Accepts("tangent",new[] {line,circle}));
        Assert.False(NativeSketchConstraintRules.Accepts("smooth",new[] {line,circle}));
        Assert.True(NativeSketchConstraintRules.Accepts("smooth",new[] {line,spline}));
        Assert.False(NativeSketchConstraintRules.Accepts("tangent",new[] {line,Ref("d","line")}));
    }
    [Fact]
    public void SymmetryRequiresAxisLastAndMatchingPairTypes()
    {
        var a=Ref("a","point"); var b=Ref("b","point"); var axis=Ref("c","line");
        Assert.True(NativeSketchConstraintRules.Accepts("symmetry",new[] {a,b,axis}));
        Assert.False(NativeSketchConstraintRules.Accepts("symmetry",new[] {axis,a,b}));
        Assert.False(NativeSketchConstraintRules.Accepts("symmetry",new[] {a,b,axis.WithSubpart(SketchSubpart.Start)}));
    }
    [Fact]
    public void RoundAndGroundRulesRefuseUnsupportedSelections()
    {
        Assert.True(NativeSketchConstraintRules.Accepts("concentric",new[] {Ref("a","ellipse"),Ref("b","arc")}));
        Assert.False(NativeSketchConstraintRules.Accepts("equal",new[] {Ref("a","ellipse"),Ref("b","arc")}));
        Assert.True(NativeSketchConstraintRules.Accepts("ground",new[] {Ref("a","line",SketchSubpart.End)}));
        Assert.False(NativeSketchConstraintRules.Accepts("ground",new[] {Ref("a","text")}));
        Assert.False(NativeSketchConstraintRules.Accepts("unknown",new[] {Ref("a","line")}));
    }
}
