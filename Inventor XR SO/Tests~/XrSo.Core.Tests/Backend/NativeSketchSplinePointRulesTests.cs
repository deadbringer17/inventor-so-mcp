using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Glb;
using Newtonsoft.Json.Linq;

namespace InventorXrSo.Core.Tests.Backend;

public class NativeSketchSplinePointRulesTests
{
    private static readonly DocumentState First = new("doc", "r1", "v1"), Next = new("doc", "r2", "v2");
    private static NativeSketchSnapshot Snapshot(DocumentState state, string type = "spline", bool capability = true, string pointState = "editable") => NativeSketchSnapshot.Parse(new JObject {
        ["schema_version"]=1,["document_id"]=state.DocumentId,["revision"]=state.Revision,["sketch_id"]="ent_sketch",["sketch_name"]="Spline",["partial"]=false,
        ["supported_variants"]=capability ? new JArray(NativeSketchSplinePointRules.Variant) : new JArray(),
        ["frame"]=new JObject {["origin_mm"]=new JArray(0,0,0),["x_axis"]=new JArray(1,0,0),["y_axis"]=new JArray(0,1,0)},
        ["entities"]=new JArray(
            new JObject {["id"]="ent_curve",["type"]=type,["supported"]=true,["strokes_mm"]=new JArray(0,0,10,10,20,0)},
            new JObject {["id"]="ent_point",["type"]="point",["supported"]=true,["selection_state"]=pointState,["point_mm"]=new JArray(10,10)}),
        ["dimensions"]=new JArray(),["constraints"]=new JArray()
    },state,"ent_sketch");
    private static JObject Options(NativeSketchSnapshot snapshot) => new() {
        ["references"]=new JArray(snapshot.Entities[0].Reference.ToJson(),snapshot.Entities[1].Reference.WithSubpart(SketchSubpart.Point).ToJson()),
        ["position_mm"]=new JArray(15,-7) };

    [Theory][InlineData("spline")][InlineData("control_point_spline")]
    public void SelectionAcceptsBothSplineTypesAndEitherPickingOrder(string type)
    {
        var snapshot=Snapshot(First,type);var curve=snapshot.Entities[0].Reference;var point=snapshot.Entities[1].Reference.WithSubpart(SketchSubpart.Point);
        Assert.True(NativeSketchSplinePointRules.TrySelection(snapshot,new[]{point,curve},out var selectedCurve,out var selectedPoint));
        Assert.Same(curve,selectedCurve);Assert.Same(point,selectedPoint);
        Assert.True(NativeSketchSplinePointRules.OptionsCurrent(snapshot,Options(snapshot)));
        Assert.False(NativeSketchSplinePointRules.TrySelection(snapshot,new[]{curve,curve},out _,out _));
    }
    [Theory][InlineData("revision","old")][InlineData("document_id","other")][InlineData("sketch_id","ent_other")]
    [InlineData("entity_id","ent_other")][InlineData("type","line")][InlineData("subpart","Start")]
    public void ForeignOrNonPointReferencesAreRejected(string key,string value)
    {
        var snapshot=Snapshot(First);var options=Options(snapshot);options["references"]![1]![key]=value;
        Assert.False(NativeSketchSplinePointRules.OptionsCurrent(snapshot,options));
    }
    [Theory][InlineData("null")][InlineData("[1]")][InlineData("[1,2,3]")][InlineData("[true,2]")][InlineData("[\"1\",2]")][InlineData("[NaN,2]")][InlineData("[Infinity,2]")]
    public void InvalidDestinationsCannotBePublished(string json)
    {
        var snapshot=Snapshot(First);var options=Options(snapshot);options["position_mm"]=JToken.Parse(json);
        Assert.False(NativeSketchSplinePointRules.OptionsCurrent(snapshot,options));
    }
    [Fact]
    public void WrongTypeCapabilityStructuralPointsAndReversedWireOrderAreRejected()
    {
        foreach(var snapshot in new[]{Snapshot(First,"line"),Snapshot(First,capability:false),Snapshot(First,pointState:"read_only_spline_handle")})
            Assert.False(NativeSketchSplinePointRules.OptionsCurrent(snapshot,Options(snapshot)));
        var valid=Snapshot(First);var options=Options(valid);var refs=(JArray)options["references"]!;var first=refs[0];refs.RemoveAt(0);refs.Add(first);
        Assert.False(NativeSketchSplinePointRules.OptionsCurrent(valid,options));
        options=Options(valid);options["extra"]=true;Assert.False(NativeSketchSplinePointRules.OptionsCurrent(valid,options));
    }
    private sealed class Backend : INativeSketchBackend
    {
        public JArray Operations;public int Plans;
        public Task<NativeSketchSnapshot> GetNativeSketchAsync(DocumentState state,string id,CancellationToken ct)=>Task.FromResult(Snapshot(state));
        public Task<DesignPreview> PreviewDesignAsync(DocumentState state,JArray operations,CancellationToken ct)
        {Plans++;Operations=(JArray)operations.DeepClone();return Task.FromResult(new DesignPreview("plan",state.DocumentId,state.Revision,DateTimeOffset.UtcNow.AddMinutes(5),new GlbModel(state.DocumentId,Array.Empty<GlbPrimitive>())));}
        public Task<DocumentState> CommitDesignAsync(DesignPreview preview,CancellationToken ct)=>Task.FromResult(Next);
    }
    [Fact]
    public async Task SessionFreezesDestinationAndReconfirmsBothReferencesInCanonicalOrder()
    {
        var backend=new Backend();using var session=new NativeSketchSession(backend);session.SetEnvironment(First,true,true);await session.OpenAsync("ent_sketch");
        var options=Options(session.Snapshot);session.SetOperation(NativeSketchSplinePointRules.Variant,options);options["position_mm"]![0]=999;
        session.SetEnvironment(Next,true,true);await session.OpenAsync("ent_sketch");Assert.False(session.CanConfirmDraft);Assert.True(session.NeedsReferenceSelection);
        session.ReconfirmSelection(session.Snapshot.Entities.Reverse().Select(e=>e.Reference));await session.PreviewAsync();
        Assert.Equal(15,(double)backend.Operations[0]["arguments"]!["options"]!["position_mm"]![0]!);
        Assert.Equal("ent_curve",(string)backend.Operations[0]["arguments"]!["options"]!["references"]![0]!["entity_id"]!);
        Assert.Equal("r2",(string)backend.Operations[0]["arguments"]!["options"]!["references"]![1]!["revision"]!);
    }
    [Fact]
    public async Task InvalidOptionsLeaveSessionWithoutDraftOrPlan()
    {
        var backend=new Backend();using var session=new NativeSketchSession(backend);session.SetEnvironment(First,true,true);await session.OpenAsync("ent_sketch");
        Assert.Throws<ArgumentException>(()=>session.SetOperation(NativeSketchSplinePointRules.Variant,new JObject{["position_mm"]=new JArray(1,2)}));
        Assert.False(session.HasDraft);Assert.Equal(0,backend.Plans);
    }
}
