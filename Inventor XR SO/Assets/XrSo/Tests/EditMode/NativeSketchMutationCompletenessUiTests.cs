using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Glb;
using InventorXrSo.Unity.Scene;
using InventorXrSo.Unity.Ui;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
namespace InventorXrSo.Tests
{
public class NativeSketchMutationCompletenessUiTests
{
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
        internal JObject Json=SnapshotJson();
        public Task<NativeSketchSnapshot> GetNativeSketchAsync(DocumentState state,string id,CancellationToken ct)=>Task.FromResult(NativeSketchSnapshot.Parse(Json,state,id));
        public Task<DesignPreview> PreviewDesignAsync(DocumentState state,JArray operations,CancellationToken ct)=>throw new NotSupportedException();
        public Task<DocumentState> CommitDesignAsync(DesignPreview preview,CancellationToken ct)=>throw new NotSupportedException();
    }
    [TestCase(true)][TestCase(false)]
    public void EditorSeparatesReadOnlyImageNoticeFromOrdinaryGeometryEligibility(bool explicitProof)
    {
        var model=new GameObject("structural completeness editor");var backend=new Backend();
        if(!explicitProof) backend.Json.Remove("mutation_completeness");
        var view=model.AddComponent<DesignGeometryView>();view.Initialize(null);string notice="";
        var state=new DocumentState("doc","r1","v1");
        using(var mutation=new DesignSession(backend))
        using(var editor=new NativeSketchEditorUi(backend,mutation,()=>null,()=>true,()=>view,()=>null,()=>{},text=>notice=text,_=>{},()=>{}))
        {
        try
        {
            editor.SetEnvironment(state,true,true);editor.OpenAsync("ent_sketch").GetAwaiter().GetResult();
            string openingNotice=notice;
            Assert.AreEqual(explicitProof,editor.CanCreate);Assert.True(editor.Session.Snapshot.Partial);
            Assert.AreEqual(explicitProof,editor.Pick(new CadPoint(105,0)));
            Assert.AreEqual(explicitProof ? 1 : 0,editor.Selected.Count);
            Assert.False(editor.Pick(new CadPoint(10,10)));Assert.IsEmpty(editor.Selected);
            Assert.False(editor.Session.Snapshot.SupportsVariant("insert_image"));
            if(explicitProof)
            {
                StringAssert.Contains("Immagini di sola lettura",openingNotice);
                Assert.True(model.GetComponentsInChildren<TMPro.TMP_Text>().Any(t=>t.text.Contains("altre geometrie modificabili")));
            }
            else StringAssert.Contains("modifica disabilitata",openingNotice);
            editor.SetEnvironment(new DocumentState("doc","r2","v2"),true,true);Assert.False(editor.CanCreate);
        }
        finally { editor.Dispose();UnityEngine.Object.DestroyImmediate(model); }
        }
    }
    [TestCase("linked")][TestCase("missing_frame")][TestCase("read_error")][TestCase("unknown")]
    public void UnsafeImageOrOtherGeometryCannotEnableEditorCommands(string failure)
    {
        var backend=new Backend();var image=backend.Json["entities"][1];
        if(failure=="linked") image["linked_to_file"]=true;
        if(failure=="missing_frame") ((JObject)image).Remove("image_current");
        if(failure=="read_error") backend.Json["mutation_completeness"]["read_error_count"]=1;
        if(failure=="unknown") { image["type"]="unknown";image["selection_state"]="unavailable";((JObject)image).Remove("image_current"); }
        using(var mutation=new DesignSession(backend))
        using(var editor=new NativeSketchEditorUi(backend,mutation,()=>null,()=>true,()=>null,()=>null,()=>{},_=>{},_=>{},()=>{}))
        {
            editor.SetEnvironment(new DocumentState("doc","r1","v1"),true,true);editor.OpenAsync("ent_sketch").GetAwaiter().GetResult();
            Assert.False(editor.CanCreate);Assert.False(editor.Pick(new CadPoint(105,0)));Assert.False(editor.Session.HasDraft);Assert.False(mutation.CanApply);
        }
    }
}
}
