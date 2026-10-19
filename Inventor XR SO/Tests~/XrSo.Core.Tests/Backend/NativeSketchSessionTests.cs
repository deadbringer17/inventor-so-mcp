using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Glb;
using Newtonsoft.Json.Linq;

namespace InventorXrSo.Core.Tests.Backend;

public class NativeSketchSessionTests
{
    private static readonly DocumentState First = new("doc", "r1", "v1"), Next = new("doc", "r2", "v2");
    private static NativeSketchSnapshot Snapshot(DocumentState state, string id = "ent_sketch", bool partial = false,bool dimensions=false,bool copy=true,string entityType="line",bool anchors=false) => NativeSketchSnapshot.Parse(new JObject
    {
        ["schema_version"] = 1, ["document_id"] = state.DocumentId, ["revision"] = state.Revision,
        ["sketch_id"] = id, ["sketch_name"] = "Existing", ["partial"] = partial,
        ["supported_variants"]=new JArray(new[] {"line","line_midpoint","circle_center","format_construction","dimension_update","dimension_delete","constraint_delete","transform_move","transform_copy","transform_rotate","transform_stretch","offset_distance","offset_point","pattern_rectangular","pattern_circular"}.Where(variant=>copy || variant!="transform_copy")),
        ["frame"] = new JObject { ["origin_mm"] = new JArray(0,0,0), ["x_axis"] = new JArray(1,0,0), ["y_axis"] = new JArray(0,1,0) },
        ["entities"] = new JArray(EntityData(entityType,anchors)),
        ["dimensions"] = dimensions ? new JArray(new JObject {["id"]="ent_dimension",["type"]="dimension",["supported"]=true,["name"]="d0",["expression"]="10 mm",["text_mm"]=new JArray(2,3),["value_mm"]=10,["driven"]=false}) : new JArray(), ["constraints"] = new JArray()
    }, state, id);
    private static JObject EntityData(string type,bool anchors)
    {
        var data=new JObject {["id"]="ent_line",["type"]=type,["supported"]=true,["strokes_mm"]=new JArray(0,0,10,0)};
        if(anchors) {data["start_mm"]=new JArray(0,0);data["end_mm"]=new JArray(10,0);}
        return data;
    }
    private static NativeSketchSnapshot WithConstraint(DocumentState state,string id)
    {
        // Construct the constraint fixture separately so ordinary geometry tests retain their baseline.
        var data=new JObject {["schema_version"]=1,["document_id"]=state.DocumentId,["revision"]=state.Revision,["sketch_id"]=id,["sketch_name"]="Existing",
            ["partial"]=false,["supported_variants"]=new JArray("constraint_delete"),
            ["frame"]=new JObject {["origin_mm"]=new JArray(0,0,0),["x_axis"]=new JArray(1,0,0),["y_axis"]=new JArray(0,1,0)},
            ["entities"]=new JArray(),["dimensions"]=new JArray(),["constraints"]=new JArray(new JObject {["id"]="ent_constraint",["type"]="constraint",["supported"]=true,["deletable"]=true})};
        return NativeSketchSnapshot.Parse(data,state,id);
    }
    private sealed class Backend : INativeSketchBackend
    {
        public Func<DocumentState, string, Task<NativeSketchSnapshot>> Read = (state, id) => Task.FromResult(Snapshot(state,id));
        public Func<Task<DocumentState>> Commit = () => Task.FromResult(Next);
        public int Commits, Plans;
        public SketchSnapshot[] PreviewSketches=Array.Empty<SketchSnapshot>();
        public JArray Operations;
        public Task<NativeSketchSnapshot> GetNativeSketchAsync(DocumentState state, string id, CancellationToken ct) => Read(state,id);
        public Task<DesignPreview> PreviewDesignAsync(DocumentState state, JArray ops, CancellationToken ct)
        { Plans++; Operations = ops; return Task.FromResult(new DesignPreview("plan", state.DocumentId, state.Revision, DateTimeOffset.UtcNow.AddMinutes(10), new GlbModel(state.DocumentId, Array.Empty<GlbPrimitive>()),PreviewSketches)); }
        public Task<DocumentState> CommitDesignAsync(DesignPreview preview, CancellationToken ct) { Commits++; return Commit(); }
    }
    private static JObject Options() => new() { ["points_mm"] = new JArray(new JArray(0,0),new JArray(10,0)) };
    [Fact]
    public async Task ReferenceOnlyReconfirmationCannotCarryOldCornerPointsIntoANewRevision()
    {
        var backend=new Backend();using var editor=new NativeSketchSession(backend);
        editor.SetEnvironment(First,true,true);await editor.OpenAsync("ent_sketch");
        var options=new JObject {["value"]=true,["references"]=new JArray(editor.Snapshot.Entities.Single().Reference.ToJson()),
            ["pick_points_mm"]=new JArray(new JArray(2,0),new JArray(7,0))};
        // Exercise the generic preserved-options guard without claiming a Fillet capability.
        editor.SetOperation("format_construction",options);
        editor.SetEnvironment(Next,true,true);await editor.OpenAsync("ent_sketch");
        Assert.True(editor.NeedsReferenceSelection);var before=editor.Draft;
        Assert.Throws<InvalidOperationException>(()=>editor.ReconfirmSelection(new[] {editor.Snapshot.Entities.Single().Reference}));
        Assert.True(JToken.DeepEquals(before,editor.Draft));Assert.False(editor.CanConfirmDraft);
        Assert.Equal(0,backend.Plans);Assert.Equal(0,backend.Commits);
    }
    private static JObject SplineSnapshotJson(int method) => new()
    {
        ["schema_version"]=1,["document_id"]=First.DocumentId,["revision"]=First.Revision,
        ["sketch_id"]="ent_sketch",["sketch_name"]="Spline defaults",["partial"]=false,
        ["supported_variants"]=new JArray("spline_fit","spline_control"),["spline_fit_method"]=method,
        ["frame"]=new JObject {["origin_mm"]=new JArray(0,0,0),["x_axis"]=new JArray(1,0,0),["y_axis"]=new JArray(0,1,0)},
        ["entities"]=new JArray(),["dimensions"]=new JArray(),["constraints"]=new JArray()
    };
    [Theory]
    [InlineData(26369,false)][InlineData(26370,false)][InlineData(26371,false)]
    [InlineData(26369,true)][InlineData(26370,true)][InlineData(26371,true)]
    public async Task SplineMethodIsFrozenAcrossOptionMutationAndPreferenceRefresh(int method,bool explicitChoice)
    {
        int preference=26369;
        var backend=new Backend {Read=(_,_)=>Task.FromResult(NativeSketchSnapshot.Parse(SplineSnapshotJson(explicitChoice ? preference : method),First,"ent_sketch"))};
        using var editor=new NativeSketchSession(backend);editor.SetEnvironment(First,true,true);await editor.OpenAsync("ent_sketch");
        var options=Options();if(explicitChoice) options["fit_method"]=method;
        editor.SetOperation("spline_fit",options);options["fit_method"]=26371;
        await editor.PreviewAsync();Assert.Equal(method,(int)backend.Operations[0]["arguments"]!["options"]!["fit_method"]!);
        backend.Read=(_,_)=>Task.FromResult(NativeSketchSnapshot.Parse(SplineSnapshotJson(26370),First,"ent_sketch"));
        await editor.OpenAsync("ent_sketch");editor.ConfirmDraftOnCurrentRevision();await editor.PreviewAsync();
        Assert.Equal(method,(int)backend.Operations[0]["arguments"]!["options"]!["fit_method"]!);
    }
    [Fact]
    public void MissingSplinePreferenceDisablesFitWithoutDisablingCv()
    {
        var json=SplineSnapshotJson(26369);json.Remove("spline_fit_method");
        var snapshot=NativeSketchSnapshot.Parse(json,First,"ent_sketch");
        Assert.False(snapshot.SupportsVariant("spline_fit"));Assert.True(snapshot.SupportsVariant("spline_control"));
    }
    [Theory][InlineData("26369")][InlineData("\"26369\"")][InlineData("true")][InlineData("26372")]
    public void SplinePreferenceMustBeAnInstalledIntegerEnum(string value)
    {
        var json=SplineSnapshotJson(26369);json["spline_fit_method"]=JToken.Parse(value);
        if(value=="26369") Assert.Equal(26369,NativeSketchSnapshot.Parse(json,First,"ent_sketch").SplineFitMethod);
        else Assert.Throws<FormatException>(()=>NativeSketchSnapshot.Parse(json,First,"ent_sketch"));
    }
    [Theory][InlineData(0)][InlineData(1)][InlineData(2)][InlineData(3)][InlineData(4)]
    public async Task OnlyExplicitlyCompleteNativePreviewCanBeAppliedAfterRendering(int mode)
    {
        var entity=new JObject {["type"]="control_point_spline",["supported"]=true,["strokes_mm"]=new JArray(0,0,5,8,10,0)};
        var json=new JObject {["sketch_name"]="Mixed",["partial"]=mode==0,
            ["frame"]=new JObject {["origin_mm"]=new JArray(0,0,0),["x_axis"]=new JArray(1,0,0),["y_axis"]=new JArray(0,1,0)},
            ["native_entities"]=new JArray(entity)};
        if(mode==1) json.Remove("partial");
        if(mode==2) entity["supported"]=false;
        if(mode==3) entity.Remove("supported");
        var ghost=SketchSnapshot.Parse(json);Assert.Equal(mode!=4,ghost.NativePartial);Assert.Single(ghost.NativePaths);
        var backend=new Backend {PreviewSketches=new[] {ghost}};
        using var editor=new NativeSketchSession(backend);editor.SetEnvironment(First,true,true);await editor.OpenAsync("ent_sketch");
        editor.SetOperation("line",Options());await editor.PreviewAsync();editor.Mutation.ConfirmRendered("plan");
        Assert.Equal(mode==4,editor.Mutation.CanApply);
        if(mode==4) {await editor.ApplyAsync();Assert.Equal(1,backend.Commits);}
        else {await Assert.ThrowsAsync<InvalidOperationException>(()=>editor.ApplyAsync());Assert.Equal(0,backend.Commits);}
    }
    [Fact]
    public async Task UnavailablePatternAssociationsRetainValidCvGeometryButBlockMutation()
    {
        var curve=EntityData("control_point_spline",true);
        curve["pattern_membership_state"]="unavailable";curve["pattern_membership_reason"]="Native getter failed.";
        var json=new JObject {["schema_version"]=1,["document_id"]=First.DocumentId,["revision"]=First.Revision,
            ["sketch_id"]="ent_sketch",["sketch_name"]="Mixed CV",["partial"]=true,["supported_variants"]=new JArray("line"),
            ["frame"]=new JObject {["origin_mm"]=new JArray(0,0,0),["x_axis"]=new JArray(1,0,0),["y_axis"]=new JArray(0,1,0)},
            ["entities"]=new JArray(curve),["dimensions"]=new JArray(),
            ["constraints"]=new JArray(new JObject {["id"]="ent_pattern",["type"]="constraint",["supported"]=true,
                ["native_type"]="kPatternConstraintObject",["binding_state"]="unavailable",["binding_reason"]="Membership could not be read."})};
        var snapshot=NativeSketchSnapshot.Parse(json,First,"ent_sketch");
        var backend=new Backend {Read=(_,_)=>Task.FromResult(snapshot)};
        using var editor=new NativeSketchSession(backend);editor.SetEnvironment(First,true,true);await editor.OpenAsync("ent_sketch");
        Assert.True(editor.Snapshot.Partial);Assert.True(Assert.Single(editor.Snapshot.Entities).Supported);
        Assert.Equal("ent_line",Assert.Single(editor.Snapshot.PickAll(new CadPoint(5,0),.01)).Reference.EntityId);
        var pattern=Assert.Single(editor.Snapshot.Constraints);Assert.True(pattern.Supported);Assert.Equal("unavailable",pattern.BindingState);
        Assert.Empty(pattern.RelatedEntities);Assert.NotNull(pattern.Data["binding_reason"]);
        Assert.False(editor.CanEdit);Assert.Throws<InvalidOperationException>(()=>editor.SetOperation("line",Options()));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>editor.PreviewAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(()=>editor.ApplyAsync());
        Assert.Equal(0,backend.Plans);Assert.Equal(0,backend.Commits);
    }
    [Theory][InlineData(0)][InlineData(1)][InlineData(2)][InlineData(3)]
    public async Task MidpointLineKeepsNumericCoordinatesAndReferenceRolesAcrossRevisionReview(int mode)
    {
        var backend=new Backend {Read=(state,id)=>Task.FromResult(Snapshot(state,id,anchors:true))};using var editor=new NativeSketchSession(backend);editor.SetEnvironment(First,true,true);await editor.OpenAsync("ent_sketch");
        var reference=editor.Snapshot.Entities.Single().Reference;var options=Options();var refs=new JArray();var roles=new JArray();
        if((mode&1)!=0){refs.Add(reference.WithSubpart(SketchSubpart.Start).ToJson());roles.Add("midpoint");}
        if((mode&2)!=0){refs.Add(reference.WithSubpart(SketchSubpart.End).ToJson());roles.Add("end");}
        if(mode!=0){options["references"]=refs;options["reference_roles"]=roles;}
        editor.SetOperation("line_midpoint",options);Assert.Equal(0,backend.Plans);Assert.Equal(0,backend.Commits);
        editor.SetEnvironment(Next,true,true);await editor.OpenAsync("ent_sketch");
        if(mode==0) editor.ConfirmDraftOnCurrentRevision();else
        {
            Assert.True(editor.NeedsReferenceSelection);Assert.Throws<InvalidOperationException>(editor.ConfirmDraftOnCurrentRevision);
            editor.ReconfirmSelection(refs.Select(r=>reference.WithSubpart(Enum.Parse<SketchSubpart>((string)r["subpart"]!))).Select(r=>new SketchEntityRef(Next.DocumentId,Next.Revision,"ent_sketch",r.EntityId,r.Type,r.Subpart)));
        }
        await editor.PreviewAsync();Assert.Equal(1,backend.Plans);Assert.Equal(0,backend.Commits);
        Assert.True(JToken.DeepEquals(options["points_mm"],backend.Operations[0]["arguments"]!["options"]!["points_mm"]));
        Assert.True(JToken.DeepEquals(options["reference_roles"],backend.Operations[0]["arguments"]!["options"]!["reference_roles"]));
    }
    [Fact]
    public async Task MidpointLineRefusesAmbiguousRolesAndWholeCurvesBeforePreview()
    {
        var backend=new Backend {Read=(state,id)=>Task.FromResult(Snapshot(state,id,anchors:true))};using var editor=new NativeSketchSession(backend);editor.SetEnvironment(First,true,true);await editor.OpenAsync("ent_sketch");
        var reference=editor.Snapshot.Entities.Single().Reference;var options=Options();options["references"]=new JArray(reference.ToJson());options["reference_roles"]=new JArray("midpoint");
        Assert.Throws<InvalidOperationException>(()=>editor.SetOperation("line_midpoint",options));
        options["references"]=new JArray(reference.WithSubpart(SketchSubpart.Start).ToJson());options["reference_roles"]=new JArray("middle");
        Assert.Throws<ArgumentException>(()=>editor.SetOperation("line_midpoint",options));
        options["reference_roles"]=new JArray("midpoint","midpoint");Assert.Throws<ArgumentException>(()=>editor.SetOperation("line_midpoint",options));
        options.Remove("references");options.Remove("reference_roles");options["unknown"]=true;Assert.Throws<ArgumentException>(()=>editor.SetOperation("line_midpoint",options));
        Assert.Equal("Punto medio",NativeSketchConstraintRules.Label("kMidpointConstraintObject"));Assert.Equal(0,backend.Plans);
    }
    [Fact]
    public async Task StretchPreservesDistinctSubpartsAndDeltaAcrossExplicitReselection()
    {
        var backend=new Backend {Read=(state,id)=>Task.FromResult(Snapshot(state,id,anchors:true))};using var editor=new NativeSketchSession(backend);editor.SetEnvironment(First,true,true);await editor.OpenAsync("ent_sketch");
        var reference=editor.Snapshot.Entities.Single().Reference;
        Assert.Throws<InvalidOperationException>(()=>editor.SetOperation("transform_stretch",new JObject()));
        var start=reference.WithSubpart(SketchSubpart.Start);var end=reference.WithSubpart(SketchSubpart.End);
        Assert.True(NativeSketchStretchRules.Accepts(new[] {start,end}));Assert.False(NativeSketchStretchRules.Accepts(new[] {start,start}));
        Assert.Throws<InvalidOperationException>(()=>editor.SetOperation("transform_stretch",new JObject {["references"]=new JArray(start.ToJson(),start.ToJson())}));
        editor.SetOperation("transform_stretch",new JObject {["references"]=new JArray(start.ToJson(),end.ToJson()),["delta_mm"]=new JArray(12,-8),["remove_constraints"]=false});
        editor.SetEnvironment(Next,true,true);await editor.OpenAsync("ent_sketch");Assert.False(editor.CanConfirmDraft);
        reference=editor.Snapshot.Entities.Single().Reference;editor.ReconfirmSelection(new[] {reference.WithSubpart(SketchSubpart.Start),reference.WithSubpart(SketchSubpart.End)});
        await editor.PreviewAsync();var options=backend.Operations[0]["arguments"]["options"];
        Assert.Equal("r2",(string)options["references"]![1]!["revision"]!);Assert.Equal("Start",(string)options["references"]![0]!["subpart"]!);
        Assert.Equal(-8,(double)options["delta_mm"]![1]!);Assert.False((bool)options["remove_constraints"]!);Assert.Equal(0,backend.Commits);
    }
    [Theory]
    [InlineData("text","Entity")][InlineData("image","Entity")][InlineData("circle","End")][InlineData("line","Center")]
    public void StretchRefusesUnavailableNativeTypeSubpartPairs(string type,string subpart)=>Assert.False(NativeSketchStretchRules.IsSelection(type,subpart));
    [Fact]
    public async Task UnvalidatedNativeTextRotationCannotReachPreviewWhileMoveRemainsAvailable()
    {
        var backend=new Backend {Read=(state,id)=>Task.FromResult(Snapshot(state,id,entityType:"text"))};
        using var editor=new NativeSketchSession(backend);editor.SetEnvironment(First,true,true);await editor.OpenAsync("ent_sketch");
        var options=new JObject {["references"]=new JArray(editor.Snapshot.Entities.Single().Reference.ToJson()),
            ["center_mm"]=new JArray(5,6),["angle_deg"]=-45,["copy"]=false};
        Assert.Throws<InvalidOperationException>(()=>editor.SetOperation("transform_rotate",options));
        Assert.False(editor.HasDraft);Assert.Equal(0,backend.Plans);
        options.Remove("center_mm");options.Remove("angle_deg");options.Remove("copy");options["delta_mm"]=new JArray(12,-8);
        editor.SetOperation("transform_move",options);await editor.PreviewAsync();Assert.Equal(1,backend.Plans);Assert.Equal(0,backend.Commits);
    }
    [Fact]
    public async Task CopyCapabilityLossRefusesFreshAndPreservedRotationCopiesBeforePreview()
    {
        bool copy=true;var backend=new Backend {Read=(state,id)=>Task.FromResult(Snapshot(state,id,copy:copy))};
        using var editor=new NativeSketchSession(backend);editor.SetEnvironment(First,true,true);await editor.OpenAsync("ent_sketch");
        var options=new JObject {["references"]=new JArray(editor.Snapshot.Entities.Single().Reference.ToJson()),
            ["center_mm"]=new JArray(5,6),["angle_deg"]=-45,["copy"]=true};
        editor.SetOperation("transform_rotate",options);copy=false;await editor.OpenAsync("ent_sketch");
        Assert.False(editor.CanConfirmDraft);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>editor.PreviewAsync());
        Assert.Throws<InvalidOperationException>(()=>editor.ReconfirmSelection(new[] {editor.Snapshot.Entities.Single().Reference}));
        Assert.Throws<InvalidOperationException>(()=>editor.SetOperation("transform_copy",options));
        Assert.Throws<InvalidOperationException>(()=>editor.SetOperation("transform_rotate",options));
        Assert.Equal(0,backend.Plans);Assert.Equal(0,backend.Commits);
        options["copy"]=false;editor.SetOperation("transform_rotate",options);await editor.PreviewAsync();Assert.Equal(1,backend.Plans);
    }
    [Fact]
    public async Task NativePatternCanReuseItsSourceAsDirectionAndRequiresExplicitReselection()
    {
        var backend=new Backend();using var editor=new NativeSketchSession(backend);editor.SetEnvironment(First,true,true);await editor.OpenAsync("ent_sketch");
        var reference=editor.Snapshot.Entities.Single().Reference;
        Assert.True(NativeSketchPatternRules.Accepts("pattern_rectangular",new[] {reference,reference}));
        Assert.False(NativeSketchPatternRules.Accepts("pattern_circular",new[] {reference,reference}));
        Assert.Throws<InvalidOperationException>(()=>editor.SetOperation("pattern_rectangular",new JObject()));
        editor.SetOperation("pattern_rectangular",new JObject {["references"]=new JArray(reference.ToJson(),reference.ToJson()),
            ["x_count"]=3,["x_spacing_mm"]=12,["two_directions"]=false,["associative"]=true});
        editor.SetEnvironment(Next,true,true);await editor.OpenAsync("ent_sketch");Assert.True(editor.NeedsReferenceSelection);
        var current=editor.Snapshot.Entities.Single().Reference;editor.ReconfirmSelection(new[] {current,current});
        Assert.True(editor.CanConfirmDraft);var options=editor.Draft[0]["arguments"]["options"];
        Assert.Equal(12,(double)options["x_spacing_mm"]!);Assert.True((bool)options["associative"]!);
        Assert.Equal("r2",(string)options["references"]![1]!["revision"]!);Assert.Equal(0,backend.Plans);Assert.Equal(0,backend.Commits);
    }
    [Theory]
    [InlineData("offset_distance")][InlineData("offset_point")]
    public async Task OffsetPreservesNativeOptionsUntilExplicitCurrentCurveReselection(string variant)
    {
        var backend=new Backend();using var editor=new NativeSketchSession(backend);editor.SetEnvironment(First,true,true);await editor.OpenAsync("ent_sketch");
        var reference=editor.Snapshot.Entities.Single().Reference;
        Assert.Throws<InvalidOperationException>(()=>editor.SetOperation(variant,new JObject()));
        Assert.Throws<InvalidOperationException>(()=>editor.SetOperation(variant,new JObject {["references"]=new JArray(reference.WithSubpart(SketchSubpart.Start).ToJson())}));
        Assert.Throws<InvalidOperationException>(()=>editor.SetOperation(variant,new JObject {["references"]=new JArray(reference.ToJson(),reference.ToJson())}));
        var options=new JObject {["references"]=new JArray(reference.ToJson()),["include_connected"]=true,["create_constraints"]=false};
        if(variant=="offset_distance") {options["distance_mm"]=7;options["natural_direction"]=false;}else options["point_mm"]=new JArray(5,-7);
        editor.SetOperation(variant,options);editor.SetEnvironment(Next,true,true);await editor.OpenAsync("ent_sketch");
        Assert.True(editor.NeedsReferenceSelection);Assert.False(editor.CanConfirmDraft);
        editor.ReconfirmSelection(new[] {editor.Snapshot.Entities.Single().Reference});
        var actual=editor.Draft[0]["arguments"]["options"];Assert.True((bool)actual["include_connected"]!);Assert.False((bool)actual["create_constraints"]!);
        Assert.Equal(variant=="offset_distance" ? 7 : -7,variant=="offset_distance" ? (double)actual["distance_mm"]! : (double)actual["point_mm"]![1]!);
        Assert.Equal("r2",(string)actual["references"]![0]!["revision"]!);Assert.Equal(0,backend.Plans);Assert.Equal(0,backend.Commits);
    }
    [Fact]
    public async Task TransformRequiresDistinctWholeCurrentObjectsAndRetainsOptionsOnReselection()
    {
        var backend=new Backend();using var editor=new NativeSketchSession(backend);editor.SetEnvironment(First,true,true);await editor.OpenAsync("ent_sketch");
        var reference=editor.Snapshot.Entities.Single().Reference;
        foreach(string variant in new[] {"transform_move","transform_copy","transform_rotate"})
        {
            Assert.Throws<InvalidOperationException>(()=>editor.SetOperation(variant,new JObject()));
            Assert.Throws<InvalidOperationException>(()=>editor.SetOperation(variant,new JObject {["references"]=new JArray(reference.WithSubpart(SketchSubpart.Start).ToJson())}));
            Assert.Throws<InvalidOperationException>(()=>editor.SetOperation(variant,new JObject {["references"]=new JArray(reference.ToJson(),reference.ToJson())}));
        }
        editor.SetOperation("transform_rotate",new JObject {["references"]=new JArray(reference.ToJson()),["center_mm"]=new JArray(5,6),["angle_deg"]=-45,["copy"]=true,["remove_constraints"]=false});
        editor.SetEnvironment(Next,true,true);await editor.OpenAsync("ent_sketch");Assert.False(editor.CanConfirmDraft);
        editor.ReconfirmSelection(new[] {editor.Snapshot.Entities.Single().Reference});await editor.PreviewAsync();
        var options=backend.Operations[0]["arguments"]["options"];Assert.Equal(-45,(double)options["angle_deg"]);Assert.True((bool)options["copy"]);
        Assert.False((bool)options["remove_constraints"]);Assert.Equal("r2",(string)options["references"][0]["revision"]);Assert.Equal(0,backend.Commits);
    }
    [Fact]
    public async Task ConstraintDeletionRequiresOneCurrentConstraintAndExplicitReselection()
    {
        var backend=new Backend {Read=(state,id)=>Task.FromResult(WithConstraint(state,id))};using var editor=new NativeSketchSession(backend);
        editor.SetEnvironment(First,true,true);await editor.OpenAsync("ent_sketch");var reference=editor.Snapshot.Constraints.Single().Reference;
        foreach(var bad in new[] {reference.WithSubpart(SketchSubpart.Point),new SketchEntityRef("doc","r0","ent_sketch","ent_constraint","constraint"),new SketchEntityRef("doc","r1","ent_sketch","ent_constraint","line")})
            Assert.Throws<InvalidOperationException>(()=>editor.SetOperation("constraint_delete",new JObject {["references"]=new JArray(bad.ToJson())}));
        Assert.Throws<InvalidOperationException>(()=>editor.SetOperation("constraint_delete",new JObject()));
        Assert.Throws<InvalidOperationException>(()=>editor.SetOperation("constraint_delete",new JObject {["references"]=new JArray(reference.ToJson(),reference.ToJson())}));
        editor.SetOperation("constraint_delete",new JObject {["references"]=new JArray(reference.ToJson())});
        editor.SetEnvironment(Next,true,true);await editor.OpenAsync("ent_sketch");Assert.False(editor.CanConfirmDraft);
        editor.ReconfirmSelection(new[] {editor.Snapshot.Constraints.Single().Reference});await editor.PreviewAsync();
        Assert.Equal("r2",(string)backend.Operations[0]["arguments"]["options"]["references"][0]["revision"]);Assert.Equal(0,backend.Commits);
    }
    [Fact]
    public async Task ExistingDimensionMustBeWholeCurrentAndInTheDimensionCollection()
    {
        var backend=new Backend {Read=(state,id)=>Task.FromResult(Snapshot(state,id,dimensions:true))};using var editor=new NativeSketchSession(backend);
        editor.SetEnvironment(First,true,true);await editor.OpenAsync("ent_sketch");var dimension=editor.Snapshot.Dimensions.Single().Reference;
        foreach(var bad in new[] {dimension.WithSubpart(SketchSubpart.Point),editor.Snapshot.Entities.Single().Reference,new SketchEntityRef("doc","r0","ent_sketch","ent_dimension","dimension")})
            Assert.Throws<InvalidOperationException>(()=>editor.SetOperation("dimension_delete",new JObject {["references"]=new JArray(bad.ToJson())}));
        Assert.Throws<InvalidOperationException>(()=>editor.SetOperation("dimension_delete",new JObject()));
        Assert.Throws<InvalidOperationException>(()=>editor.SetOperation("line",new JObject {["references"]=new JArray(dimension.ToJson())}));
        editor.SetOperation("dimension_update",new JObject {["value_mm"]=25,["references"]=new JArray(dimension.ToJson())});
        await editor.PreviewAsync();Assert.Equal(1,backend.Plans);Assert.Equal(0,backend.Commits);
    }
    [Fact]
    public async Task ExistingDimensionDraftRequiresExplicitReselectionAfterRevisionChange()
    {
        var backend=new Backend {Read=(state,id)=>Task.FromResult(Snapshot(state,id,dimensions:true))};using var editor=new NativeSketchSession(backend);
        editor.SetEnvironment(First,true,true);await editor.OpenAsync("ent_sketch");
        editor.SetOperation("dimension_update",new JObject {["text_mm"]=new JArray(5,6),["references"]=new JArray(editor.Snapshot.Dimensions.Single().Reference.ToJson())});
        editor.SetEnvironment(Next,true,true);await editor.OpenAsync("ent_sketch");Assert.False(editor.CanConfirmDraft);
        Assert.True(editor.NeedsReferenceSelection);Assert.Throws<InvalidOperationException>(()=>editor.ReconfirmSelection(new[] {editor.Snapshot.Entities.Single().Reference}));
        editor.ReconfirmSelection(new[] {editor.Snapshot.Dimensions.Single().Reference});
        Assert.Equal(5,(double)editor.Draft[0]["arguments"]["options"]["text_mm"][0]);Assert.Equal("r2",(string)editor.Draft[0]["arguments"]["options"]["references"][0]["revision"]);
        Assert.True(editor.CanConfirmDraft);await editor.PreviewAsync();Assert.Equal(1,backend.Plans);
    }

    [Fact]
    public async Task UnadvertisedOperationCannotCreateOrRestoreAnExecutableDraft()
    {
        using var editor=new NativeSketchSession(new Backend()); editor.SetEnvironment(First,true,true); await editor.OpenAsync("ent_sketch");
        Assert.Throws<InvalidOperationException>(()=>editor.SetOperation("constraint_smooth",new JObject()));
        Assert.False(editor.HasDraft); Assert.Equal(DesignStatus.Empty,editor.Mutation.Status);
    }
    [Fact]
    public async Task MissingSubpartCannotBecomeARevisionBoundOperation()
    {
        using var editor=new NativeSketchSession(new Backend()); editor.SetEnvironment(First,true,true); await editor.OpenAsync("ent_sketch");
        var reference=editor.Snapshot.Entities.Single().Reference.WithSubpart(SketchSubpart.Start);
        Assert.Throws<InvalidOperationException>(()=>editor.SetOperation("format_construction",new JObject {["value"]=true,["references"]=new JArray(reference.ToJson())}));
        Assert.False(editor.HasDraft);
    }

    [Fact]
    public async Task PreservedReferenceOperationCannotRebindItsEntityRevisionSilently()
    {
        var backend=new Backend(); using var editor=new NativeSketchSession(backend);
        editor.SetEnvironment(First,true,true); await editor.OpenAsync("ent_sketch");
        editor.SetOperation("format_construction",new JObject { ["value"]=true,["references"]=new JArray(
            new SketchEntityRef("doc","r1","ent_sketch","ent_line","line").ToJson()) });
        editor.SetEnvironment(Next,true,true); await editor.OpenAsync("ent_sketch");
        Assert.True(editor.HasDraft); Assert.False(editor.CanConfirmDraft);
        Assert.Throws<InvalidOperationException>(()=>editor.ConfirmDraftOnCurrentRevision());
        Assert.Equal("r1",(string)editor.Draft[0]["arguments"]["options"]["references"][0]["revision"]);
        Assert.Equal(0,backend.Plans); Assert.Equal(0,backend.Commits);
        Assert.True(editor.NeedsReferenceSelection);
        editor.ReconfirmSelection(new[] {new SketchEntityRef("doc","r2","ent_sketch","ent_line","line")});
        Assert.True((bool)editor.Draft[0]["arguments"]["options"]["value"]);
        Assert.Equal("r2",(string)editor.Draft[0]["arguments"]["options"]["references"][0]["revision"]);
        Assert.False(editor.NeedsReferenceSelection); await editor.PreviewAsync(); Assert.Equal(1,backend.Plans);
    }
    [Fact]
    public async Task IncrementalApplyKeepsEditorOpenAndRequiresFreshSnapshotBeforeNextOperation()
    {
        var backend = new Backend(); using var editor = new NativeSketchSession(backend);
        editor.SetEnvironment(First,true,true); await editor.OpenAsync("ent_sketch");
        editor.SetOperation("line", Options());
        Assert.Equal("edit_sketch", (string)editor.Draft[0]["command"]);
        Assert.Equal("ent_sketch", (string)editor.Draft[0]["arguments"]["sketch_id"]);
        await Assert.ThrowsAsync<InvalidOperationException>(() => editor.ApplyAsync());
        await editor.PreviewAsync(); Assert.False(editor.Mutation.CanApply);
        editor.Mutation.ConfirmRendered("plan"); await editor.ApplyAsync();
        Assert.Equal(1,backend.Commits); Assert.Equal("ent_sketch",editor.SketchId);
        Assert.True(editor.NeedsRefresh); Assert.False(editor.CanEdit); Assert.False(editor.HasDraft);
        editor.SetEnvironment(Next,true,true); editor.AcknowledgeMutationRefresh(Next);
        await editor.OpenAsync("ent_sketch"); Assert.True(editor.CanEdit);
        Assert.Equal("r2",editor.Snapshot.State.Revision);
        editor.SetOperation("circle_center", new JObject { ["radius_mm"] = 5 });
        Assert.Equal(1,backend.Commits);
    }

    [Fact]
    public async Task OfflineAndDesktopRevisionPreserveDraftButRequireExplicitReconfirmation()
    {
        var backend = new Backend(); using var editor = new NativeSketchSession(backend);
        editor.SetEnvironment(First,true,true); await editor.OpenAsync("ent_sketch");
        var options = Options(); editor.SetOperation("line", options); options["points_mm"][0][0] = 999;
        editor.Draft[0]["arguments"]["variant"] = "mutated";
        editor.SetEnvironment(First,false,true);
        Assert.True(editor.HasDraft); Assert.NotNull(editor.Snapshot); Assert.False(editor.CanEdit);
        Assert.Throws<InvalidOperationException>(() => editor.Close());
        editor.SetEnvironment(Next,true,true); await editor.OpenAsync("ent_sketch");
        await Assert.ThrowsAsync<InvalidOperationException>(() => editor.PreviewAsync());
        editor.ConfirmDraftOnCurrentRevision(); await editor.PreviewAsync();
        Assert.Equal("line", (string)backend.Operations[0]["arguments"]["variant"]);
        Assert.Equal(0, (int)backend.Operations[0]["arguments"]["options"]["points_mm"][0][0]);
        Assert.Equal(0,backend.Commits);
    }

    [Fact]
    public async Task LateReadCannotReplaceFrozenSnapshotAfterDocumentChanges()
    {
        var backend = new Backend(); using var editor = new NativeSketchSession(backend);
        editor.SetEnvironment(First,true,true); await editor.OpenAsync("ent_sketch");
        var frozen = editor.Snapshot; var pending = new TaskCompletionSource<NativeSketchSnapshot>(); backend.Read = (_,_) => pending.Task;
        var read = editor.OpenAsync("ent_sketch"); editor.SetEnvironment(Next,true,true);
        pending.SetResult(Snapshot(First)); await read;
        Assert.Same(frozen,editor.Snapshot); Assert.False(editor.CanEdit); Assert.False(editor.Loading);
        Assert.True(editor.NeedsRefresh);
    }

    [Fact]
    public async Task PartialNativeGeometryBlocksMutationAndLostCommitBlocksAutomaticRetry()
    {
        var backend = new Backend { Read = (state,id) => Task.FromResult(Snapshot(state,id,true)) };
        using var editor = new NativeSketchSession(backend); editor.SetEnvironment(First,true,true);
        await editor.OpenAsync("ent_sketch"); Assert.False(editor.CanEdit);
        Assert.Throws<InvalidOperationException>(() => editor.SetOperation("line",Options()));
        backend.Read = (state,id) => Task.FromResult(Snapshot(state,id)); await editor.OpenAsync("ent_sketch");
        editor.SetOperation("line",Options()); await editor.PreviewAsync(); editor.Mutation.ConfirmRendered("plan");
        backend.Commit = () => throw new IOException("Response lost after send"); await editor.ApplyAsync();
        Assert.True(editor.Mutation.CommitOutcomeUnknown);
        await Assert.ThrowsAsync<InvalidOperationException>(() => editor.OpenAsync("ent_sketch"));
        Assert.Throws<InvalidOperationException>(() => editor.AcknowledgeMutationRefresh(First));
        editor.AcknowledgeMutationRefresh(First,true); await editor.OpenAsync("ent_sketch");
        Assert.True(editor.CanEdit); Assert.Equal(1,backend.Commits);
    }
    [Fact]
    public async Task RebindCheckpointPreservesDraftButNeverRestoresExecutablePreview()
    {
        var backend=new Backend(); NativeSketchCheckpoint checkpoint;
        using (var editor=new NativeSketchSession(backend))
        {
            editor.SetEnvironment(First,true,true); await editor.OpenAsync("ent_sketch");
            editor.SetOperation("line",Options()); await editor.PreviewAsync(); editor.Mutation.ConfirmRendered("plan");
            checkpoint=editor.Checkpoint();
        }
        using var restored=new NativeSketchSession(backend); restored.Restore(checkpoint);
        restored.SetEnvironment(First,true,true);
        Assert.True(restored.HasDraft); Assert.False(restored.CanEdit); Assert.Null(restored.Mutation.Preview);
        await restored.OpenAsync("ent_sketch");
        await Assert.ThrowsAsync<InvalidOperationException>(()=>restored.PreviewAsync());
        restored.ConfirmDraftOnCurrentRevision(); await restored.PreviewAsync();
        Assert.False(restored.Mutation.CanApply); Assert.Equal(0,backend.Commits);
    }
    [Fact]
    public async Task CheckpointDuringCommitRequiresReviewAndDoesNotRestoreReplayableOperation()
    {
        var backend=new Backend(); using var editor=new NativeSketchSession(backend);
        editor.SetEnvironment(First,true,true); await editor.OpenAsync("ent_sketch");
        editor.SetOperation("line",Options()); await editor.PreviewAsync(); editor.Mutation.ConfirmRendered("plan");
        var pending=new TaskCompletionSource<DocumentState>(); backend.Commit=()=>pending.Task;
        var applying=editor.ApplyAsync(); var checkpoint=editor.Checkpoint();
        Assert.True(checkpoint.RequiresCadReview); Assert.Null(checkpoint.Draft);
        using var restored=new NativeSketchSession(backend); restored.Restore(checkpoint);
        restored.SetEnvironment(Next,true,true); Assert.False(restored.HasDraft);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>restored.OpenAsync("ent_sketch"));
        Assert.Throws<InvalidOperationException>(()=>restored.AcknowledgeMutationRefresh(Next));
        pending.SetResult(Next); await applying;
        restored.AcknowledgeMutationRefresh(Next,true); await restored.OpenAsync("ent_sketch");
        Assert.True(restored.CanEdit); Assert.Equal(1,backend.Commits);
    }
    [Fact]
    public async Task RemovingRenderingProofPreservesOperationButRevokesApplyAndCancelsLatePreview()
    {
        var backend=new Backend(); using var editor=new NativeSketchSession(backend);
        editor.SetEnvironment(First,true,true); await editor.OpenAsync("ent_sketch");
        editor.SetOperation("line",Options()); await editor.PreviewAsync(); editor.Mutation.ConfirmRendered("plan");
        editor.InvalidatePresentation(); Assert.True(editor.HasDraft); Assert.False(editor.Mutation.CanApply);
        Assert.Equal(DesignStatus.Empty,editor.Mutation.Status);
        editor.ConfirmDraftOnCurrentRevision(); await editor.PreviewAsync(); Assert.False(editor.Mutation.CanApply);
    }
    [Fact]
    public void DisposingBorrowedNativeSessionDoesNotDisposeWorkspaceMutationGuard()
    {
        var backend=new Backend(); using var owner=new DesignSession(backend);
        owner.SetContext(First,true,true);
        new NativeSketchSession(backend,mutation:owner).Dispose();
        Assert.True(owner.CanEdit); owner.SetDraft(new JArray(new JObject { ["command"]="edit_sketch" }));
        Assert.Equal(DesignStatus.Draft,owner.Status);
    }
}
