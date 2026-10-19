using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Glb;
using Newtonsoft.Json.Linq;

namespace InventorXrSo.Core.Tests.Backend;

public sealed class NativeSketchTangentArcRulesTests
{
    private static readonly DocumentState State=new("doc","r1","v1"),Next=new("doc","r2","v2");
    internal static NativeSketchSnapshot Snapshot(DocumentState state,string source="line",bool disclose=true,double targetX=40)=>NativeSketchSnapshot.Parse(new JObject
    {
        ["schema_version"]=1,["document_id"]=state.DocumentId,["revision"]=state.Revision,["sketch_id"]="ent_sketch",["sketch_name"]="Tangent",["partial"]=false,
        ["supported_variants"]=disclose ? new JArray("arc_tangent") : new JArray("line"),
        ["frame"]=new JObject{["origin_mm"]=new JArray(0,0,0),["x_axis"]=new JArray(1,0,0),["y_axis"]=new JArray(0,1,0)},
        ["entities"]=new JArray(new JObject{["id"]="ent_source",["type"]=source,["supported"]=true,["strokes_mm"]=new JArray(0,0,20,0),["start_mm"]=new JArray(0,0),["end_mm"]=new JArray(20,0)},
            new JObject{["id"]="ent_target",["type"]="point",["supported"]=true,["point_mm"]=new JArray(targetX,20)}),
        ["dimensions"]=new JArray(),["constraints"]=new JArray()
    },state,"ent_sketch");
    private static SketchEntityRef Source(NativeSketchSnapshot snapshot,SketchSubpart part=SketchSubpart.Start)=>snapshot.Entities[0].Reference.WithSubpart(part);
    private static SketchEntityRef Target(NativeSketchSnapshot snapshot)=>snapshot.Entities[1].Reference;
    [Fact] public void SerializedWireContainsExactlyOneNestedXyArray()
    {
        var snapshot=Snapshot(State);
        var options=NativeSketchTangentArcRules.CreateOptions(snapshot,new[]{Source(snapshot)},new CadPoint(31.25,-12.5));
        var wire=JObject.Parse(options.ToString(Newtonsoft.Json.Formatting.None));
        // Assert the independent backend wire shape, not a builder/validator roundtrip.
        Assert.Equal("[[31.25,-12.5]]",wire["points_mm"]!.ToString(Newtonsoft.Json.Formatting.None));
        var outer=Assert.IsType<JArray>(wire["points_mm"]);Assert.Single(outer);
        var xy=Assert.IsType<JArray>(outer[0]);Assert.Equal(2,xy.Count);
        Assert.Equal(JTokenType.Float,xy[0].Type);Assert.Equal(JTokenType.Float,xy[1].Type);
        Assert.Equal(31.25,(double)xy[0]);Assert.Equal(-12.5,(double)xy[1]);
    }
    [Theory][InlineData("line")][InlineData("arc")][InlineData("elliptical_arc")][InlineData("spline")][InlineData("control_point_spline")]
    public void SourceIsAnActualTypedCurveEndpointAndDestinationIsOneNestedPoint(string type)
    {
        var snapshot=Snapshot(State,type);var options=NativeSketchTangentArcRules.CreateOptions(snapshot,new[]{Source(snapshot,SketchSubpart.End)},new CadPoint(31,16));
        NativeSketchTangentArcRules.ValidateOptions(snapshot,options);
        Assert.Single((JArray)options["points_mm"]!);Assert.Equal(31,(double)options["points_mm"]![0]![0]!);
        Assert.Equal("End",(string)options["references"]![0]!["subpart"]!);
    }
    [Theory][InlineData("circle")][InlineData("ellipse")][InlineData("offset_spline")][InlineData("text")][InlineData("spline_fit")]
    public void SourceFamiliesOutsideTheNativeContractAreRefused(string type)
    {var snapshot=Snapshot(State,type);Assert.False(NativeSketchTangentArcRules.Accepts(snapshot,new[]{Source(snapshot)}));}
    [Fact] public void ImagesCannotBeSourceCurves()
    {Assert.False(NativeSketchTangentArcRules.IsSource(new SketchEntityRef("doc","r1","ent_sketch","ent_image","image",SketchSubpart.Start)));}
    [Theory][InlineData(SketchSubpart.Entity)][InlineData(SketchSubpart.Point)][InlineData(SketchSubpart.Center)]
    public void WholeCurvesAndNonEndpointSubpartsCannotSupplyTheSource(SketchSubpart part)
    {var snapshot=Snapshot(State);Assert.False(NativeSketchTangentArcRules.Accepts(snapshot,new[]{Source(snapshot,part)}));}
    [Fact] public void TargetAnchorIsDerivedFromSnapshotInsteadOfPointerOrSuppliedCoordinates()
    {
        var snapshot=Snapshot(State);var options=NativeSketchTangentArcRules.CreateOptions(snapshot,new[]{Source(snapshot),Target(snapshot)},new CadPoint(99,99));
        Assert.Equal(40,(double)options["points_mm"]![0]![0]!);Assert.Equal(20,(double)options["points_mm"]![0]![1]!);
        options["points_mm"]![0]![0]=41;Assert.Throws<ArgumentException>(()=>NativeSketchTangentArcRules.ValidateOptions(snapshot,options));
    }
    [Fact] public void ExactDuplicateAnchorIsRefusedButOtherEndpointIsADistinctReference()
    {
        var snapshot=Snapshot(State,"arc");var source=Source(snapshot);
        Assert.False(NativeSketchTangentArcRules.Accepts(snapshot,new[]{source,source}));
        Assert.True(NativeSketchTangentArcRules.Accepts(snapshot,new[]{source,Source(snapshot,SketchSubpart.End)}));
        Assert.False(NativeSketchTangentArcRules.Accepts(snapshot,new[]{Target(snapshot),source}));
    }
    [Fact] public void StaleWrongOwnerAndForgedTypeAreRejectedEvenWithAnExistingEntityId()
    {
        var snapshot=Snapshot(State);var good=Source(snapshot);
        foreach(var bad in new[]{new SketchEntityRef("doc","old","ent_sketch",good.EntityId,"line",SketchSubpart.Start),
            new SketchEntityRef("other","r1","ent_sketch",good.EntityId,"line",SketchSubpart.Start),
            new SketchEntityRef("doc","r1","ent_other",good.EntityId,"line",SketchSubpart.Start),
            new SketchEntityRef("doc","r1","ent_sketch",good.EntityId,"arc",SketchSubpart.Start)})
            Assert.False(NativeSketchTangentArcRules.Accepts(snapshot,new[]{bad}));
    }
    [Fact] public void RequiredReferenceAndPlanarFiniteEndpointCannotBeReplacedWithCoordinatesOnly()
    {
        var snapshot=Snapshot(State);Assert.Throws<ArgumentException>(()=>NativeSketchTangentArcRules.ValidateOptions(snapshot,new JObject{["points_mm"]=new JArray{new JArray(20,10)}}));
        foreach(var point in new[]{new CadPoint(1,2,3),new CadPoint(0,0)})
            Assert.Throws<ArgumentException>(()=>NativeSketchTangentArcRules.CreateOptions(snapshot,new[]{Source(snapshot)},point));
        foreach(var value in new[]{double.NaN,double.PositiveInfinity,double.NegativeInfinity})
        {
            var nonfinite=NativeSketchTangentArcRules.CreateOptions(snapshot,new[]{Source(snapshot)},new CadPoint(20,10));
            nonfinite["points_mm"]![0]![0]=value;
            Assert.Throws<ArgumentException>(()=>NativeSketchTangentArcRules.ValidateOptions(snapshot,nonfinite));
        }
        var options=NativeSketchTangentArcRules.CreateOptions(snapshot,new[]{Source(snapshot)},new CadPoint(20,10));
        options["references"]![0]!["extra"]="invalid";Assert.False(NativeSketchTangentArcRules.OptionsCurrent(snapshot,options));
        options=NativeSketchTangentArcRules.CreateOptions(snapshot,new[]{Source(snapshot)},new CadPoint(20,10));options["points_mm"]![0]![0]="20";
        Assert.False(NativeSketchTangentArcRules.OptionsCurrent(snapshot,options));
    }
    private sealed class Backend : INativeSketchBackend
    {
        internal bool Disclose=true;internal int Plans,Commits;
        public Task<NativeSketchSnapshot> GetNativeSketchAsync(DocumentState state,string id,CancellationToken ct)=>Task.FromResult(Snapshot(state,disclose:Disclose,targetX:state.Revision=="r2" ? 60 : 40));
        public Task<DesignPreview> PreviewDesignAsync(DocumentState state,JArray operations,CancellationToken ct){Plans++;throw new NotSupportedException();}
        public Task<DocumentState> CommitDesignAsync(DesignPreview preview,CancellationToken ct){Commits++;throw new NotSupportedException();}
    }
    [Fact] public async Task SessionRejectsCoordinateOnlyAndMalformedReferenceOptionsBeforeAnyPreview()
    {
        var backend=new Backend();using var session=new NativeSketchSession(backend);session.SetEnvironment(State,true,true);await session.OpenAsync("ent_sketch");
        var options=new JObject{["points_mm"]=new JArray{new JArray(20,10)}};Assert.Throws<ArgumentException>(()=>session.SetOperation("arc_tangent",options));
        options=NativeSketchTangentArcRules.CreateOptions(session.Snapshot,new[]{Source(session.Snapshot)},new CadPoint(20,10));options["references"]![0]!["subpart"]="Entity";
        Assert.Throws<ArgumentException>(()=>session.SetOperation("arc_tangent",options));Assert.False(session.HasDraft);Assert.Equal(0,backend.Plans);Assert.Equal(0,backend.Commits);
    }
    [Fact] public async Task SessionFrozenDraftRequiresFreshSourceAndReconfirmationDerivesTheNewTargetAnchor()
    {
        var backend=new Backend();using var session=new NativeSketchSession(backend);session.SetEnvironment(State,true,true);await session.OpenAsync("ent_sketch");
        var options=NativeSketchTangentArcRules.CreateOptions(session.Snapshot,new[]{Source(session.Snapshot),Target(session.Snapshot)});
        session.SetOperation("arc_tangent",options);options["points_mm"]![0]![0]=900;
        Assert.Equal(40,(double)session.Draft[0]!["arguments"]!["options"]!["points_mm"]![0]![0]!);
        session.SetEnvironment(Next,true,true);await session.OpenAsync("ent_sketch");Assert.False(session.CanConfirmDraft);Assert.True(session.NeedsReferenceSelection);
        session.ReconfirmSelection(new[]{Source(session.Snapshot),Target(session.Snapshot)});
        Assert.Equal(60,(double)session.Draft[0]!["arguments"]!["options"]!["points_mm"]![0]![0]!);
        Assert.Equal("r2",(string)session.Draft[0]!["arguments"]!["options"]!["references"]![0]!["revision"]!);
        Assert.Equal(0,backend.Plans);Assert.Equal(0,backend.Commits);
    }
    [Fact] public async Task RulesNeverAuthorizeAnUndisclosedNativeCapability()
    {
        var backend=new Backend{Disclose=false};using var session=new NativeSketchSession(backend);session.SetEnvironment(State,true,true);await session.OpenAsync("ent_sketch");
        var options=NativeSketchTangentArcRules.CreateOptions(session.Snapshot,new[]{Source(session.Snapshot)},new CadPoint(20,10));
        Assert.Throws<InvalidOperationException>(()=>session.SetOperation("arc_tangent",options));Assert.False(session.HasDraft);
    }
}
