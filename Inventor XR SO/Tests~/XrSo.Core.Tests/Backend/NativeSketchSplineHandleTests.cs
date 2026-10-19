using InventorXrSo.Core.Backend;
using Newtonsoft.Json.Linq;

namespace InventorXrSo.Core.Tests.Backend;

public class NativeSketchSplineHandleTests
{
    private static JObject Snapshot()
    {
        var json=NativeSketchMutationCompletenessTests.SnapshotJson();
        JObject Point(string id,int x,int y,bool structural)=>new() { ["id"]=id,["type"]="point",["supported"]=true,
            ["point_mm"]=new JArray(x,y),["selection_state"]=structural ? "read_only_spline_handle" : "editable" };
        json["entities"]=new JArray(
            new JObject { ["id"]="ent_spline",["type"]="spline",["supported"]=true,["strokes_mm"]=new JArray(0,0,10,0),["start_mm"]=new JArray(0,0),["end_mm"]=new JArray(10,0) },
            Point("ent_fit",0,0,false),Point("ent_start",-1,0,true),Point("ent_end",1,0,true),
            new JObject { ["id"]="ent_handle",["type"]="spline_handle",["native_type"]="kSketchSplineHandleObject",["supported"]=true,
                ["selection_state"]="read_only_spline_handle",["strokes_mm"]=new JArray(-1,0,1,0),["start_mm"]=new JArray(-1,0),["end_mm"]=new JArray(1,0),
                ["spline_handle"]=new JObject { ["schema"]=1,["owner_slot"]=0,["fit_point_slot"]=1,["start_slot"]=2,["end_slot"]=3,["fit_point_role"]="Start" } });
        json["partial"]=false;json["mutation_completeness"]!["state"]="complete";
        json["mutation_completeness"]!["image_boundaries"]=new JArray();
        json["mutation_completeness"]!["collections"]!["entities"]!["native_count"]=5;
        json["mutation_completeness"]!["collections"]!["entities"]!["represented_count"]=5;
        return json;
    }
    [Fact]
    public void HandleAndItsEndpointsAreProtectedButSplineFitPointRemainsSelectable()
    {
        var json=Snapshot();var result=NativeSketchSnapshot.Parse(json,new DocumentState("doc","r1","v1"),"ent_sketch");
        Assert.True(result.NativeMutationComplete);
        Assert.True(result.Entities[0].Selectable);Assert.True(result.Entities[1].Selectable);
        Assert.All(result.Entities.Skip(2),e=>Assert.False(e.Selectable));
        var preview=NativeSketchMutationCompletenessTests.PreviewJson(json);preview["partial"]=false;
        Assert.True(SketchSnapshot.Parse(preview).NativeMutationComplete);
    }
    [Theory]
    [InlineData("line_alias")][InlineData("missing_owner")][InlineData("wrong_owner")][InlineData("fit_alias")]
    [InlineData("wrong_role")][InlineData("fit_position")][InlineData("start_position")][InlineData("editable_handle")]
    [InlineData("editable_point")][InlineData("extra_field")][InlineData("unclaimed_point")][InlineData("fake_native")]
    public void InvalidStructuralOwnershipNeverAdmitsMutation(string failure)
    {
        var json=Snapshot();var entities=(JArray)json["entities"]!;var handle=(JObject)entities[4];var map=(JObject)handle["spline_handle"]!;
        switch(failure)
        {
            case "line_alias":handle["type"]="line";break;
            case "missing_owner":map.Remove("owner_slot");break;
            case "wrong_owner":map["owner_slot"]=1;break;
            case "fit_alias":map["fit_point_slot"]=2;break;
            case "wrong_role":map["fit_point_role"]="Endpoint";break;
            case "fit_position":entities[1]["point_mm"]=new JArray(99,0);break;
            case "start_position":handle["start_mm"]=new JArray(99,0);break;
            case "editable_handle":handle["selection_state"]="editable";break;
            case "editable_point":entities[2]["selection_state"]="editable";break;
            case "extra_field":map["owner_id"]="ent_temp";break;
            case "unclaimed_point":entities[1]["selection_state"]="read_only_spline_handle";break;
            case "fake_native":handle["native_type"]="kSketchLineObject";break;
        }
        Assert.Throws<FormatException>(()=>SketchMutationCompleteness.Validate((JObject)json["mutation_completeness"]!,entities,new JArray(),new JArray(),false,false));
        Assert.Throws<FormatException>(()=>Bimwright.Ipt.Shared.Contracts.SketchMutationCompleteness.Validate((JObject)json["mutation_completeness"]!,entities,new JArray(),new JArray(),false,false));
        Assert.Throws<FormatException>(()=>NativeSketchSnapshot.Parse(json,new DocumentState("doc","r1","v1"),"ent_sketch"));
        var preview=NativeSketchMutationCompletenessTests.PreviewJson(json);preview["partial"]=false;
        Assert.Throws<FormatException>(()=>SketchSnapshot.Parse(preview));
    }
    [Fact]
    public void TangencyKeepsActualHandleMemberDistinctFromItsOwningSpline()
    {
        var json=Snapshot();var entities=(JArray)json["entities"]!;
        entities.Add(new JObject { ["id"]="ent_arc",["type"]="arc",["supported"]=true,["strokes_mm"]=new JArray(0,0,10,10),
            ["start_mm"]=new JArray(0,0),["end_mm"]=new JArray(10,10) });
        json["constraints"]=new JArray(new JObject { ["id"]="ent_tangent",["type"]="constraint",["supported"]=true,
            ["native_type"]="kTangentSketchConstraintObject",["binding_state"]="complete",["related_entities"]=new JArray(
                new JObject { ["entity_id"]="ent_handle",["type"]="spline_handle",["subpart"]="Entity" },
                new JObject { ["entity_id"]="ent_arc",["type"]="arc",["subpart"]="Entity" }) });
        json["mutation_completeness"]!["collections"]!["entities"]!["native_count"]=6;
        json["mutation_completeness"]!["collections"]!["entities"]!["represented_count"]=6;
        json["mutation_completeness"]!["collections"]!["constraints"]!["native_count"]=1;
        json["mutation_completeness"]!["collections"]!["constraints"]!["represented_count"]=1;
        var parsed=NativeSketchSnapshot.Parse(json,new DocumentState("doc","r1","v1"),"ent_sketch");
        Assert.True(parsed.NativeMutationComplete);
        Assert.Equal(new[]{"ent_handle","ent_arc"},parsed.Constraints.Single().RelatedEntities.Select(r=>r.EntityId));
        Assert.DoesNotContain(parsed.Constraints.Single().RelatedEntities,r=>r.EntityId=="ent_spline");
    }
}
