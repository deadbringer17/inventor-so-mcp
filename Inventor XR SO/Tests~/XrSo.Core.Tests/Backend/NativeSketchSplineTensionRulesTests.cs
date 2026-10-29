using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Glb;
using Newtonsoft.Json.Linq;

namespace InventorXrSo.Core.Tests.Backend;

public class NativeSketchSplineTensionRulesTests
{
    private static readonly DocumentState First = new("doc", "r1", "v1"), Next = new("doc", "r2", "v2");
    private static NativeSketchSnapshot Snapshot(DocumentState state, JToken tension = null, bool capability = true, string type = "spline") => NativeSketchSnapshot.Parse(new JObject {
        ["schema_version"]=1,["document_id"]=state.DocumentId,["revision"]=state.Revision,["sketch_id"]="ent_sketch",["sketch_name"]="Spline",["partial"]=false,
        ["supported_variants"]=capability ? new JArray(NativeSketchSplineTensionRules.Variant) : new JArray(),
        ["frame"]=new JObject {["origin_mm"]=new JArray(0,0,0),["x_axis"]=new JArray(1,0,0),["y_axis"]=new JArray(0,1,0)},
        ["entities"]=new JArray(new JObject {["id"]="ent_curve",["type"]=type,["supported"]=true,["strokes_mm"]=new JArray(0,0,10,10,20,0),["start_mm"]=new JArray(0,0),["end_mm"]=new JArray(20,0),["tension"]=tension ?? new JValue(50)}),
        ["dimensions"]=new JArray(),["constraints"]=new JArray()
    },state,"ent_sketch");
    private static JObject Options(NativeSketchSnapshot snapshot, JToken tension = null) => new() {
        ["references"]=new JArray(snapshot.Entities.Single().Reference.ToJson()),["tension"]=tension ?? new JValue(75) };

    [Theory][InlineData(0)][InlineData(50)][InlineData(100)]
    public void NativeIntegerTensionIsReadFromTheCurrentWholeFitSpline(int value)
    {
        var snapshot=Snapshot(First,new JValue(value));
        Assert.True(NativeSketchSplineTensionRules.TryGetTension(snapshot,snapshot.Entities.Single().Reference,out int actual));
        Assert.Equal(value,actual);Assert.True(NativeSketchSplineTensionRules.OptionsCurrent(snapshot,Options(snapshot,new JValue(value))));
    }
    [Theory][InlineData("null")][InlineData("\"50\"")][InlineData("50.0")][InlineData("-1")][InlineData("101")][InlineData("99999999999999999999999")]
    public void MissingOrMalformedNativeTensionDisablesEditing(string value)
    {
        var snapshot=Snapshot(First,JToken.Parse(value));
        Assert.False(NativeSketchSplineTensionRules.TryGetTension(snapshot,snapshot.Entities.Single().Reference,out _));
    }
    [Theory][InlineData("\"75\"")][InlineData("75.5")][InlineData("-1")][InlineData("101")][InlineData("true")][InlineData("null")]
    public void InvalidRequestedTensionIsRejectedBeforePlanning(string value)
    {
        var snapshot=Snapshot(First);Assert.False(NativeSketchSplineTensionRules.OptionsCurrent(snapshot,Options(snapshot,JToken.Parse(value))));
    }
    [Theory][InlineData("revision","old")][InlineData("document_id","other")][InlineData("sketch_id","ent_other")]
    [InlineData("entity_id","ent_other")][InlineData("type","control_point_spline")][InlineData("subpart","Start")]
    public void ForeignStaleOrNonWholeSelectionsAreRejected(string field,string value)
    {
        var snapshot=Snapshot(First);var options=Options(snapshot);options["references"]![0]![field]=value;
        Assert.False(NativeSketchSplineTensionRules.OptionsCurrent(snapshot,options));
    }
    [Fact]
    public void CapabilityAndNativeSplineTypeAreRequired()
    {
        foreach(var snapshot in new[]{Snapshot(First,capability:false),Snapshot(First,type:"control_point_spline"),Snapshot(First,type:"line")})
            Assert.False(NativeSketchSplineTensionRules.TryGetTension(snapshot,snapshot.Entities.Single().Reference,out _));
        var valid=Snapshot(First);var options=Options(valid);options["remove_constraints"]=true;
        Assert.False(NativeSketchSplineTensionRules.OptionsCurrent(valid,options));
        options=Options(valid);((JArray)options["references"]!).Add(valid.Entities.Single().Reference.ToJson());
        Assert.False(NativeSketchSplineTensionRules.OptionsCurrent(valid,options));
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
    public async Task SessionFreezesOptionsAndRequiresFreshSelectionAfterRevisionChange()
    {
        var backend=new Backend();using var session=new NativeSketchSession(backend);session.SetEnvironment(First,true,true);await session.OpenAsync("ent_sketch");
        var options=Options(session.Snapshot);session.SetOperation(NativeSketchSplineTensionRules.Variant,options);options["tension"]=5;
        session.SetEnvironment(Next,true,true);await session.OpenAsync("ent_sketch");
        Assert.False(session.CanConfirmDraft);Assert.True(session.NeedsReferenceSelection);Assert.Equal(0,backend.Plans);
        session.ReconfirmSelection(new[]{session.Snapshot.Entities.Single().Reference});await session.PreviewAsync();
        Assert.Equal(75,(int)backend.Operations[0]["arguments"]!["options"]!["tension"]!);
        Assert.Equal("r2",(string)backend.Operations[0]["arguments"]!["options"]!["references"]![0]!["revision"]!);
    }
    [Fact]
    public async Task SessionRejectsMissingReferencesWithoutPublishingADraft()
    {
        var backend=new Backend();using var session=new NativeSketchSession(backend);session.SetEnvironment(First,true,true);await session.OpenAsync("ent_sketch");
        Assert.Throws<ArgumentException>(()=>session.SetOperation(NativeSketchSplineTensionRules.Variant,new JObject{["tension"]=50}));
        Assert.False(session.HasDraft);Assert.Equal(0,backend.Plans);
    }
}
