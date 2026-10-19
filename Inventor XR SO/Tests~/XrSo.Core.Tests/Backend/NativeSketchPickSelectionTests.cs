using InventorXrSo.Core.Backend;
using Newtonsoft.Json.Linq;

namespace InventorXrSo.Core.Tests.Backend;

public class NativeSketchPickSelectionTests
{
    private static NativeSketchSnapshot Snapshot(string revision="r1",string document="part",string sketch="ent_sketch",bool partial=false,string secondType="arc")
    {
        JObject Curve(string id,string type,double y)=>new()
        {
            ["id"]=id,["type"]=type,["supported"]=true,
            ["strokes_mm"]=new JArray(-10,y,10,y),["start_mm"]=new JArray(-10,y),["end_mm"]=new JArray(10,y)
        };
        var state=new DocumentState(document,revision,"v1");
        return NativeSketchSnapshot.Parse(new JObject
        {
            ["schema_version"]=1,["document_id"]=document,["revision"]=revision,["sketch_id"]=sketch,["sketch_name"]="Pick fixture",
            ["frame"]=new JObject {["origin_mm"]=new JArray(0,0,0),["x_axis"]=new JArray(1,0,0),["y_axis"]=new JArray(0,1,0)},
            ["partial"]=partial,["entities"]=new JArray(Curve("ent_first","line",0),Curve("ent_second",secondType,10)),
            ["dimensions"]=new JArray(),["constraints"]=new JArray()
        },state,sketch);
    }
    private static NativeSketchHit[] Picks(NativeSketchSnapshot snapshot)=>new[]
    {
        Assert.Single(snapshot.PickTargets(new CadPoint(7,.2),.3,part:SketchSubpart.Entity)),
        Assert.Single(snapshot.PickTargets(new CadPoint(-8,9.8),.3,part:SketchSubpart.Entity))
    };

    [Fact]
    public void PointAndAnchorHitsRetainActualPointerRatherThanSnappedCoordinates()
    {
        var snapshot=Snapshot();var pointer=new CadPoint(-9.9,.1);
        var hits=snapshot.PickTargets(pointer,.2);
        Assert.Equal(2,hits.Count);
        Assert.Contains(hits,hit=>hit.Reference.Subpart==SketchSubpart.Entity);
        Assert.Contains(hits,hit=>hit.Reference.Subpart==SketchSubpart.Start);
        Assert.All(hits,hit=>Assert.Equal(pointer,hit.PickPointMm));
        Assert.All(hits,hit=>Assert.True(hit.Reference.Matches(snapshot.State,snapshot.SketchId)));
    }
    [Fact]
    public void FrozenPairsKeepSelectionOrderAndMillimetresAndDoNotAliasInputOrPreviousOutput()
    {
        var snapshot=Snapshot();var picks=Picks(snapshot);Array.Reverse(picks);
        var options=new JObject {["radius_mm"]=3,["nested"]=new JObject {["flag"]=true}};
        var result=NativeSketchPickSelection.FreezeTwoCurveOptions(snapshot,picks,options);
        Assert.Equal(new[] {"ent_second","ent_first"},result["references"]!.Select(reference=>(string)reference["entity_id"]!));
        Assert.True(JToken.DeepEquals(new JArray(new JArray(-8d,9.8),new JArray(7d,.2)),result["pick_points_mm"]));
        Assert.Null(options["references"]);Assert.Null(options["pick_points_mm"]);
        options["nested"]!["flag"]=false;
        Assert.True((bool)result["nested"]!["flag"]!);
        result["references"]![0]!["revision"]="tampered";result["pick_points_mm"]![0]![0]=999;
        var fresh=NativeSketchPickSelection.FreezeTwoCurveOptions(snapshot,picks,options);
        Assert.Equal("r1",(string)fresh["references"]![0]!["revision"]!);
        Assert.Equal(-8,(double)fresh["pick_points_mm"]![0]![0]!);
        Assert.Empty(snapshot.SupportedVariants); // A prepared input never enables a command.
    }
    [Theory]
    [InlineData("r2","part","ent_sketch")]
    [InlineData("r1","other","ent_sketch")]
    [InlineData("r1","part","ent_other")]
    [InlineData("r1","part","ent_sketch")]
    public void PicksCannotBeSilentlyReboundToARefreshedSnapshotEvenWithMatchingKeys(string revision,string document,string sketch)
    {
        var picks=Picks(Snapshot());var refreshed=Snapshot(revision,document,sketch);
        Assert.Throws<InvalidOperationException>(()=>NativeSketchPickSelection.FreezeTwoCurveOptions(refreshed,picks,new JObject()));
    }
    [Fact]
    public void WindowPicksHaveNoCornerPointAndCannotBeFrozenAsIndividualPicks()
    {
        var snapshot=Snapshot();var window=NativeSketchWindow.Query(snapshot,new CadPoint(-11,-1),new CadPoint(11,11),NativeSketchWindowMode.Contained);
        Assert.Equal(2,window.Count);Assert.All(window,hit=>Assert.Null(hit.PickPointMm));
        Assert.Throws<InvalidOperationException>(()=>NativeSketchPickSelection.FreezeTwoCurveOptions(snapshot,window,new JObject()));
    }
    [Fact]
    public void RejectsDuplicatesSubpartsAndUnsupportedCurveTypes()
    {
        var snapshot=Snapshot();var picks=Picks(snapshot);
        Assert.Throws<InvalidOperationException>(()=>NativeSketchPickSelection.FreezeTwoCurveOptions(snapshot,new[] {picks[0],picks[0]},new JObject()));
        var endpoint=Assert.Single(snapshot.PickTargets(new CadPoint(-10,0),.1,"line",SketchSubpart.Start));
        Assert.Throws<InvalidOperationException>(()=>NativeSketchPickSelection.FreezeTwoCurveOptions(snapshot,new[] {endpoint,picks[1]},new JObject()));
        var spline=Snapshot(secondType:"spline");
        Assert.Throws<InvalidOperationException>(()=>NativeSketchPickSelection.FreezeTwoCurveOptions(spline,Picks(spline),new JObject()));
    }
    [Fact]
    public void RejectsPartialSnapshotsMissingPicksAndNonPlanarPointer()
    {
        var partial=Snapshot(partial:true);
        Assert.Throws<InvalidOperationException>(()=>NativeSketchPickSelection.FreezeTwoCurveOptions(partial,Picks(partial),new JObject()));
        var snapshot=Snapshot();
        Assert.Throws<ArgumentException>(()=>NativeSketchPickSelection.FreezeTwoCurveOptions(snapshot,Array.Empty<NativeSketchHit>(),new JObject()));
        Assert.Throws<ArgumentException>(()=>snapshot.PickTargets(new CadPoint(0,0,.1),1));
        Assert.Throws<ArgumentNullException>(()=>NativeSketchPickSelection.FreezeTwoCurveOptions(snapshot,Picks(snapshot),null!));
    }
}
