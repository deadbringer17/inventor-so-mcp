using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Glb;
using Newtonsoft.Json.Linq;

namespace InventorXrSo.Core.Tests.Backend;

public class NativeSketchMutationCompletenessTests
{
    private static readonly DocumentState State=new("doc","r1","v1");
    public static JObject SnapshotJson()
    {
        var corners=new[]{new JArray(0,10),new JArray(20,10),new JArray(20,0),new JArray(0,0)};
        var entities=new JArray(new JObject { ["id"]="ent_line",["type"]="line",["supported"]=true,["construction"]=false,
            ["strokes_mm"]=new JArray(100,0,110,0),["start_mm"]=new JArray(100,0),["end_mm"]=new JArray(110,0) },
            new JObject { ["id"]="ent_image",["type"]="image",["supported"]=false,["selection_state"]="read_only_image",["linked_to_file"]=false,
                ["image_current"]=new JObject { ["schema"]=1,["source_kind"]="native_bitmap",["asset_state"]="deferred",["visible"]=true,["transparent"]=false,["width_mm"]=20,["height_mm"]=10,
                    ["frame"]=new JObject { ["schema"]=1,["provenance"]="native_boundary_roles",["corner_order"]=new JArray("top_left","top_right","bottom_right","bottom_left"),["corners_mm"]=new JArray(corners.Select(x=>x.DeepClone())) } } });
        for(int i=0;i<4;i++)
        {
            var a=corners[i];var b=corners[(i+1)%4];
            entities.Add(new JObject { ["id"]="ent_edge"+i,["type"]="line",["supported"]=true,["selection_state"]="image_boundary",["structural_owner_image_id"]="ent_image",
                ["start_mm"]=a.DeepClone(),["end_mm"]=b.DeepClone(),["strokes_mm"]=new JArray(a[0],a[1],b[0],b[1]) });
        }
        for(int i=0;i<4;i++) entities.Add(new JObject { ["id"]="ent_corner"+i,["type"]="point",["supported"]=true,["selection_state"]="image_boundary",["structural_owner_image_id"]="ent_image",["point_mm"]=corners[i].DeepClone() });
        JObject Count(int count)=>new() { ["native_count"]=count,["represented_count"]=count };
        return new JObject { ["schema_version"]=1,["document_id"]="doc",["revision"]="r1",["sketch_id"]="ent_sketch",["sketch_name"]="Images",["partial"]=true,
            ["supported_variants"]=new JArray("line","format_construction","transform_move","transform_stretch"),
            ["frame"]=new JObject { ["origin_mm"]=new JArray(0,0,0),["x_axis"]=new JArray(1,0,0),["y_axis"]=new JArray(0,1,0) },
            ["entities"]=entities,["dimensions"]=new JArray(),["constraints"]=new JArray(),
            ["mutation_completeness"]=new JObject { ["schema"]=1,["state"]="read_only_image_backgrounds",["omitted_count"]=0,["read_error_count"]=0,
                ["collections"]=new JObject { ["entities"]=Count(10),["dimensions"]=Count(0),["constraints"]=Count(0) },
                ["image_boundaries"]=new JArray(new JObject { ["image_slot"]=1,["edge_slots"]=new JArray(2,3,4,5),["point_slots"]=new JArray(6,7,8,9) }) } };
    }
    public static JObject PreviewJson(JObject snapshot)
    {
        var native=(JArray)snapshot["entities"]!.DeepClone();
        foreach(JObject item in native) { item.Remove("id");item.Remove("structural_owner_image_id");item.Remove("structural_subparts"); }
        return new JObject { ["sketch_name"]=snapshot["sketch_name"],["frame"]=snapshot["frame"]!.DeepClone(),["entities"]=new JArray(),["native_entities"]=native,
            ["dimensions"]=new JArray(),["native_constraints"]=new JArray(),["geometric_constraints"]=new JArray(),["partial"]=true,["mutation_completeness"]=snapshot["mutation_completeness"]!.DeepClone() };
    }
    private sealed class Backend : INativeSketchBackend
    {
        public JObject Json=SnapshotJson();public int Plans,Commits;
        public Task<NativeSketchSnapshot> GetNativeSketchAsync(DocumentState state,string id,CancellationToken ct)=>Task.FromResult(NativeSketchSnapshot.Parse(Json,state,id));
        public Task<DesignPreview> PreviewDesignAsync(DocumentState state,JArray operations,CancellationToken ct)
        { Plans++;return Task.FromResult(new DesignPreview("plan",state.DocumentId,state.Revision,DateTimeOffset.UtcNow.AddMinutes(10),new GlbModel(state.DocumentId,Array.Empty<GlbPrimitive>()),new[]{SketchSnapshot.Parse(PreviewJson(Json))})); }
        public Task<DocumentState> CommitDesignAsync(DesignPreview preview,CancellationToken ct) { Commits++;return Task.FromResult(new DocumentState("doc","r2","v2")); }
    }
    [Fact]
    public async Task ReadOnlyImagesPermitOrdinaryGeometryPreviewAndApplyWithoutClaimingImageEditing()
    {
        var backend=new Backend();using var session=new NativeSketchSession(backend);session.SetEnvironment(State,true,true);await session.OpenAsync("ent_sketch");
        Assert.True(session.Snapshot.Partial);Assert.True(session.Snapshot.NativeMutationComplete);Assert.True(session.CanEdit);
        Assert.False(session.Snapshot.SupportsVariant("insert_image"));Assert.False(session.Snapshot.Entities[1].Selectable);
        session.SetOperation("format_construction",new JObject { ["value"]=true,["references"]=new JArray(session.Snapshot.Entities[0].Reference.ToJson()) });
        await session.PreviewAsync();Assert.True(session.Mutation.Preview.Sketches.Single().NativePartial);Assert.True(session.Mutation.Preview.Sketches.Single().NativeMutationComplete);
        session.Mutation.ConfirmRendered("plan");Assert.True(session.Mutation.CanApply);await session.ApplyAsync();Assert.Equal(1,backend.Plans);Assert.Equal(1,backend.Commits);
    }
    [Theory]
    [InlineData("missing")][InlineData("incomplete")][InlineData("unknown")][InlineData("linked")][InlineData("transparent")]
    [InlineData("missing_frame")][InlineData("omitted")][InlineData("read_error")][InlineData("missing_edge")][InlineData("duplicate_slot")]
    [InlineData("wrong_owner")][InlineData("foreign_subpart")][InlineData("state")][InlineData("counts")][InlineData("unclassified")]
    public void UnsafeOrIncompleteImageSnapshotsNeverUnlockMutation(string failure)
    {
        var json=SnapshotJson();var proof=json["mutation_completeness"]!;var image=json["entities"]![1]!;
        switch(failure)
        {
            case "missing":json.Remove("mutation_completeness");break;
            case "incomplete":proof["state"]="incomplete";break;
            case "unknown":((JObject)image).Remove("image_current");image["type"]="unknown";image["selection_state"]="unavailable";break;
            case "linked":image["linked_to_file"]=true;break;
            case "transparent":image["image_current"]!["transparent"]=true;break;
            case "missing_frame":((JObject)image).Remove("image_current");break;
            case "omitted":proof["omitted_count"]=1;proof["collections"]!["entities"]!["native_count"]=11;break;
            case "read_error":proof["read_error_count"]=1;break;
            case "missing_edge":((JArray)proof["image_boundaries"]![0]!["edge_slots"]!).RemoveAt(0);break;
            case "duplicate_slot":proof["image_boundaries"]![0]!["edge_slots"]![1]=2;break;
            case "wrong_owner":json["entities"]![2]!["structural_owner_image_id"]="ent_other";break;
            case "foreign_subpart":json["entities"]![0]!["structural_subparts"]=new JArray(new JObject { ["subpart"]="Start",["owner_image_id"]="ent_foreign" });break;
            case "state":proof["state"]="complete";break;
            case "counts":proof["collections"]!["entities"]!["represented_count"]=9;break;
            case "unclassified":json["entities"]![2]!["selection_state"]="editable";((JObject)json["entities"]![2]!).Remove("structural_owner_image_id");break;
        }
        try { Assert.False(NativeSketchSnapshot.Parse(json,State,"ent_sketch").NativeMutationComplete); }
        catch(FormatException) { }
    }
    [Fact]
    public async Task StructuralAndSharedAnchorReferencesRemainRefusedAndRevisionChangeRevokesEligibility()
    {
        var backend=new Backend();using var session=new NativeSketchSession(backend);session.SetEnvironment(State,true,true);await session.OpenAsync("ent_sketch");
        Assert.Throws<InvalidOperationException>(()=>session.SetOperation("transform_move",new JObject { ["references"]=new JArray(session.Snapshot.Entities[2].Reference.ToJson()),["delta_mm"]=new JArray(1,0) }));
        session.SetEnvironment(new DocumentState("doc","r2","v2"),true,true);Assert.False(session.CanEdit);Assert.Equal(0,backend.Plans);Assert.Equal(0,backend.Commits);
    }
    [Theory]
    [InlineData("id")][InlineData("owner_ref")][InlineData("unknown")][InlineData("missing_contract")][InlineData("incomplete")]
    public void TentativePreviewCannotGainSelectableIdentitiesOrBypassMissingCompleteness(string failure)
    {
        var preview=PreviewJson(SnapshotJson());
        switch(failure)
        {
            case "id":preview["native_entities"]![0]!["id"]="ent_tentative";break;
            case "owner_ref":preview["native_entities"]![2]!["structural_owner_image_id"]="ent_image";break;
            case "unknown":preview["native_entities"]![1]!["type"]="unknown";break;
            case "missing_contract":preview.Remove("mutation_completeness");break;
            case "incomplete":preview["mutation_completeness"]!["state"]="incomplete";break;
        }
        try { Assert.False(SketchSnapshot.Parse(preview).NativeMutationComplete); }
        catch(FormatException) { }
    }

    [Theory]
    [InlineData("unknown_supported")][InlineData("missing_strokes")][InlineData("nonfinite_strokes")]
    [InlineData("unexplained_partial")][InlineData("anchor_not_array")][InlineData("duplicate_anchor")]
    [InlineData("anchor_extra")][InlineData("anchor_wrong_position")][InlineData("anchor_wrong_part")]
    public void StrictAuthorityRejectsUnreadableGeometryAndMalformedStructuralAnchorsInBothRuntimes(string failure)
    {
        var json=SnapshotJson();var line=(JObject)json["entities"]![0]!;
        var binding=new JObject { ["subpart"]="Start",["owner_image_id"]="ent_image" };
        if(failure.StartsWith("anchor",StringComparison.Ordinal) || failure=="duplicate_anchor") line["structural_subparts"]=new JArray(binding);
        switch(failure)
        {
            case "unknown_supported":line["type"]="unknown_future_curve";break;
            case "missing_strokes":line.Remove("strokes_mm");break;
            case "nonfinite_strokes":line["strokes_mm"]![0]=double.PositiveInfinity;break;
            case "unexplained_partial":
                json["entities"]=new JArray(line.DeepClone());json["mutation_completeness"]!["state"]="complete";
                json["mutation_completeness"]!["collections"]!["entities"]!["native_count"]=1;
                json["mutation_completeness"]!["collections"]!["entities"]!["represented_count"]=1;
                json["mutation_completeness"]!["image_boundaries"]=new JArray();break;
            case "anchor_not_array":line["structural_subparts"]=binding;break;
            case "duplicate_anchor":((JArray)line["structural_subparts"]!).Add(binding.DeepClone());break;
            case "anchor_extra":binding["editable"]=true;break;
            case "anchor_wrong_position":break;
            case "anchor_wrong_part":binding["subpart"]="Entity";break;
        }
        var rejected=Record.Exception(()=>NativeSketchSnapshot.Parse(json,State,"ent_sketch"));
        Assert.True(rejected is FormatException or ArgumentException,"Malformed geometry must be rejected before mutation admission.");
        Assert.Throws<FormatException>(()=>Bimwright.Ipt.Shared.Contracts.SketchMutationCompleteness.Validate((JObject)json["mutation_completeness"]!,
            (JArray)json["entities"]!,(JArray)json["dimensions"]!,(JArray)json["constraints"]!,false,true));
    }
}
