using Bimwright.Ipt.Shared.Contracts;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Tests;

public sealed class NativeSketchCurveCopyPolicy93Tests
{
    private static NativeSketchCopyPolicy.Request Request(string kind="arc")=>NativeSketchCopyPolicy.Parse(new JObject {
        ["references"]=new JArray(new JObject { ["document_id"]="doc",["revision"]="rev",["sketch_id"]="sketch",
            ["entity_id"]="curve",["type"]=kind,["subpart"]="Entity" }),["delta_mm"]=new JArray(12,-8),["remove_constraints"]=false });
    private static NativeSketchCurveCopyPolicy93.Relation Edge(string kind,string point,bool deletable=false,bool intrinsic=true)
        =>new(kind,deletable,intrinsic,"curve",point);
    private static NativeSketchCurveCopyPolicy93.Relation[] Arc()=>new[]{Edge("concentric","center"),Edge("coincident","start"),Edge("coincident","end")};
    private static void Graph(IEnumerable<NativeSketchCurveCopyPolicy93.Relation> edges)
        =>NativeSketchCurveCopyPolicy93.ValidateIntrinsicGraph("arc","curve","center","start","end",edges);
    [Theory]
    [InlineData("circle")][InlineData("arc")]
    public void WholeCurveReferencesRequireCurrentOwnerRevisionAndSketch(string kind)
    {
        var request=Request(kind);
        NativeSketchCopyPolicy.ValidateBinding(request,"doc","rev","sketch");
        Assert.Throws<ArgumentException>(()=>NativeSketchCopyPolicy.ValidateBinding(request,"foreign","rev","sketch"));
        Assert.Throws<ArgumentException>(()=>NativeSketchCopyPolicy.ValidateBinding(request,"doc","stale","sketch"));
        Assert.Throws<ArgumentException>(()=>NativeSketchCopyPolicy.ValidateBinding(request,"doc","rev","foreign"));
    }
    [Fact]
    public void ExactRolesRatherThanCountsProveIntrinsicGraph()
    {
        Graph(Arc());
        NativeSketchCurveCopyPolicy93.ValidateIntrinsicGraph("circle","curve","center",null,null,new[]{Edge("concentric","center")});
        Graph(Arc().Reverse().Select(e=>new NativeSketchCurveCopyPolicy93.Relation(e.Kind,e.Deletable,e.IntrinsicFlag,e.Two,e.One)));
        Assert.Throws<ArgumentException>(()=>Graph(new[]{Edge("concentric","start"),Edge("coincident","center"),Edge("coincident","end")}));
        Assert.Throws<ArgumentException>(()=>Graph(new[]{Edge("concentric","foreign"),Edge("coincident","start"),Edge("coincident","end")}));
        Assert.Throws<ArgumentException>(()=>Graph(new[]{Edge("concentric","center"),Edge("coincident","start"),Edge("coincident","start")}));
        Assert.Throws<ArgumentException>(()=>Graph(Arc().Take(2)));
        Assert.Throws<ArgumentException>(()=>Graph(Arc().Append(Edge("coincident","foreign"))));
    }
    [Theory]
    [InlineData(true,true)][InlineData(false,false)]
    public void NativeIntrinsicFlagAndNondeletabilityAreBothNecessary(bool deletable,bool intrinsic)
    { var edges=Arc();edges[1]=Edge("coincident","start",deletable,intrinsic);Assert.Throws<ArgumentException>(()=>Graph(edges)); }
    [Fact]
    public void StructuralPointIdentityMustBeDistinctEvenIfCoordinatesWouldCoincide()
    {
        Assert.Throws<ArgumentException>(()=>NativeSketchCurveCopyPolicy93.ValidateIntrinsicGraph("arc","curve","center","center","end",Arc()));
        Assert.Throws<ArgumentException>(()=>NativeSketchCurveCopyPolicy93.ValidateIntrinsicGraph("circle","curve","curve",null,null,new[]{Edge("concentric","curve")}));
    }
    [Theory]
    [InlineData(0)][InlineData(1)][InlineData(2)][InlineData(3)][InlineData(4)][InlineData(5)][InlineData(6)][InlineData(7)]
    public void EveryUnverifiedFormatOrOwnershipAttributeRefusesBeforeCreator(int bad)
        =>Assert.Throws<ArgumentException>(()=>NativeSketchCurveCopyPolicy93.ValidateFormat(bad==0,bad==1,bad==2,bad==3,bad!=4,bad!=5,bad==6?1:0,bad==7?1:0,37648,0,1,49921));
    [Fact]
    public void DefaultMeansObservedEnumValuesAndZeroWeight()
    {
        NativeSketchCurveCopyPolicy93.ValidateFormat(false,false,false,false,true,true,0,0,37648,0,1,49921);
        Assert.Throws<ArgumentException>(()=>NativeSketchCurveCopyPolicy93.ValidateFormat(false,false,false,false,true,true,0,0,37649,0,1,49921));
        Assert.Throws<ArgumentException>(()=>NativeSketchCurveCopyPolicy93.ValidateFormat(false,false,false,false,true,true,0,0,37648,0,1,49922));
        Assert.Throws<ArgumentException>(()=>NativeSketchCurveCopyPolicy93.ValidateFormat(false,false,false,false,true,true,0,0,37648,1,1,49921));
        Assert.Throws<ArgumentException>(()=>NativeSketchCurveCopyPolicy93.ValidateFormat(false,false,false,false,true,true,0,0,37648,0,2,49921));
    }
    [Theory]
    [InlineData(double.NaN)][InlineData(double.PositiveInfinity)][InlineData(double.NegativeInfinity)]
    public void NonfiniteGeometryFormatAndComparisonsCannotPass(double invalid)
    {
        var request=Request();
        Assert.Throws<ArgumentException>(()=>NativeSketchCurveCopyPolicy93.ValidateGeometry("arc",0,0,invalid,0,1,new[]{(0d,0d)},request));
        Assert.Throws<ArgumentException>(()=>NativeSketchCurveCopyPolicy93.ValidateGeometry("arc",invalid,0,10,0,1,new[]{(0d,0d)},request));
        Assert.Throws<ArgumentException>(()=>NativeSketchCurveCopyPolicy93.ValidateGeometry("arc",0,0,10,invalid,1,new[]{(0d,0d)},request));
        Assert.Throws<ArgumentException>(()=>NativeSketchCurveCopyPolicy93.ValidateGeometry("arc",0,0,10,0,invalid,new[]{(0d,0d)},request));
        Assert.Throws<ArgumentException>(()=>NativeSketchCurveCopyPolicy93.ValidateGeometry("arc",0,0,10,0,1,new[]{(0d,invalid)},request));
        Assert.Throws<ArgumentException>(()=>NativeSketchCurveCopyPolicy93.ValidateFormat(false,false,false,false,true,true,0,0,37648,invalid,1,49921));
        Assert.Throws<ArgumentException>(()=>NativeSketchCurveCopyPolicy93.ValidateFormat(false,false,false,false,true,true,0,0,37648,0,invalid,49921));
        Assert.False(NativeSketchCurveCopyPolicy93.RadiusMatches(invalid,10));Assert.False(NativeSketchCurveCopyPolicy93.RadiusMatches(10,invalid));
        Assert.False(NativeSketchCurveCopyPolicy93.AngleMatches(invalid,1));Assert.False(NativeSketchCurveCopyPolicy93.AngleMatches(1,invalid));
    }
    [Theory]
    [InlineData(0d)][InlineData(-1d)][InlineData(6.283185307179586)]
    public void FiniteArcExtentMustRemainAnActualNondegenerateArc(double sweep)
        =>Assert.Throws<ArgumentException>(()=>NativeSketchCurveCopyPolicy93.ValidateGeometry("arc",0,0,10,0,sweep,new[]{(10d,0d)},Request()));
    [Fact]
    public void CircleAndMajorWrapArcHaveFiniteTranslatableGeometry()
    {
        NativeSketchCurveCopyPolicy93.ValidateGeometry("circle",-4,7,3,0,2*Math.PI,new[]{(-4d,7d)},Request("circle"));
        NativeSketchCurveCopyPolicy93.ValidateGeometry("arc",-4,7,3,170*Math.PI/180,270*Math.PI/180,new[]{(-4d,7d),(-7d,7d)},Request());
        Assert.Throws<ArgumentException>(()=>NativeSketchCurveCopyPolicy93.ValidateGeometry("circle",double.MaxValue,0,double.MaxValue,0,2*Math.PI,new[]{(0d,0d)},Request("circle")));
        Assert.Throws<ArgumentException>(()=>NativeSketchCurveCopyPolicy93.ValidateGeometry("circle",0,0,0,0,2*Math.PI,new[]{(0d,0d)},Request("circle")));
        Assert.True(NativeSketchCurveCopyPolicy93.RadiusMatches(10+1e-9,10));Assert.False(NativeSketchCurveCopyPolicy93.RadiusMatches(10+1e-6,10));
        Assert.True(NativeSketchCurveCopyPolicy93.AngleMatches(1+1e-11,1));Assert.False(NativeSketchCurveCopyPolicy93.AngleMatches(1+1e-8,1));
    }
}
