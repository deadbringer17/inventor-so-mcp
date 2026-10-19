using InventorXrSo.Core.Backend;
using Newtonsoft.Json.Linq;

namespace InventorXrSo.Core.Tests.Backend;

public class NativeSketchImageCurrentTests
{
    private static readonly DocumentState State = new("part", "r7", "v7");
    private static JObject Image() => new()
    {
        ["id"]="ent_image",["type"]="image",["supported"]=false,["selection_state"]="read_only_image",
        // Legacy native picture dimensions intentionally differ from current UV dimensions.
        ["width_mm"]=10,["height_mm"]=20,
        ["image_current"]=new JObject
        {
            ["schema"]=1,["source_kind"]="native_bitmap",["asset_state"]="deferred",["visible"]=true,["transparent"]=false,
            ["width_mm"]=20,["height_mm"]=10,
            ["frame"]=new JObject { ["schema"]=1,["provenance"]="native_boundary_roles",
                ["corner_order"]=new JArray("top_left","top_right","bottom_right","bottom_left"),
                ["corners_mm"]=new JArray(new JArray(3,4),new JArray(15,20),new JArray(23,14),new JArray(11,-2)) }
        }
    };
    private static JObject Line(string id="ent_line")=>new()
    { ["id"]=id,["type"]="line",["supported"]=true,["strokes_mm"]=new JArray(3,4,15,20),["start_mm"]=new JArray(3,4),["end_mm"]=new JArray(15,20) };
    private static JObject Snapshot(params JObject[] entities)=>new()
    {
        ["schema_version"]=1,["document_id"]="part",["revision"]="r7",["sketch_id"]="ent_sketch",["sketch_name"]="Existing",
        ["frame"]=new JObject { ["origin_mm"]=new JArray(0,0,0),["x_axis"]=new JArray(1,0,0),["y_axis"]=new JArray(0,1,0) },
        ["entities"]=new JArray(entities),["dimensions"]=new JArray(),["constraints"]=new JArray(),["partial"]=false
    };
    private static NativeSketchSnapshot Parse(JObject snapshot)=>NativeSketchSnapshot.Parse(snapshot,State,"ent_sketch");

    [Fact]
    public void LegacyImageUsesCurrentFrameWithoutOriginalPayloadOrNativePictureDimensions()
    {
        var image=Image();var parsed=Parse(Snapshot(image));var item=Assert.Single(parsed.Entities);
        Assert.Null(item.Image);Assert.NotNull(item.CurrentImage);Assert.False(item.Supported);Assert.False(item.Selectable);Assert.True(parsed.Partial);
        Assert.Equal(20,item.CurrentImage.WidthMm);Assert.Equal(10,item.CurrentImage.HeightMm);Assert.Equal("deferred",item.CurrentImage.AssetState);
        image["image_current"]!["frame"]!["corners_mm"]![0]![0]=999;
        Assert.Equal(new CadPoint(3,4),item.CurrentImage.CornersMm[0]);
        Assert.Throws<NotSupportedException>(()=>((IList<CadPoint>)item.CurrentImage.CornersMm)[0]=new CadPoint(0,0));
    }

    [Theory]
    [InlineData("schema","2")][InlineData("schema","999999999999999999999")][InlineData("schema","1.0")]
    [InlineData("source_kind","\"managed_original\"")][InlineData("asset_state","\"ready\"")]
    [InlineData("visible","1")][InlineData("transparent","true")][InlineData("transparent","\"false\"")]
    [InlineData("width_mm","0")][InlineData("width_mm","-1")][InlineData("height_mm","\"10\"")]
    [InlineData("extra","1")]
    public void InvalidCurrentDescriptionFailsClosed(string field,string json)
    { var image=Image();image["image_current"]![field]=JToken.Parse(json);Assert.Throws<FormatException>(()=>Parse(Snapshot(image))); }

    [Theory]
    [InlineData("provenance")][InlineData("order")][InlineData("count")][InlineData("affine")][InlineData("skew")]
    [InlineData("dimension")][InlineData("empty")][InlineData("schema")][InlineData("extra")]
    public void InvalidCurrentFramesFailClosed(string error)
    {
        var image=Image();var current=image["image_current"]!;var frame=current["frame"]!;
        switch(error)
        {
            case "provenance":frame["provenance"]="native_corner_reference_keys";break;
            case "order":frame["corner_order"]=new JArray("bottom_left","top_right","bottom_right","top_left");break;
            case "count":((JArray)frame["corners_mm"]!).RemoveAt(3);break;
            case "affine":frame["corners_mm"]![2]![0]=24;break;
            case "skew":frame["corners_mm"]=new JArray(new JArray(0,0),new JArray(20,0),new JArray(21,10),new JArray(1,10));current["height_mm"]=Math.Sqrt(101);break;
            case "dimension":current["width_mm"]=21;break;
            case "empty":frame["corners_mm"]=new JArray(Enumerable.Range(0,4).Select(_=>new JArray(0,0)));break;
            case "schema":frame["schema"]=1.0;break;
            case "extra":frame["asset_url"]="https://invalid/";break;
        }
        Assert.Throws<FormatException>(()=>Parse(Snapshot(image)));
    }

    [Theory]
    [InlineData("supported")][InlineData("type")][InlineData("selection_state")]
    public void CurrentFrameDoesNotAuthorizeImageEdits(string field)
    {
        var image=Image();image[field]=field=="supported" ? new JValue(true) : new JValue(field=="type" ? "line" : "editable");
        Assert.Throws<FormatException>(()=>Parse(Snapshot(image)));
    }

    [Fact]
    public void StructuralGeometryRetainsConstraintAssociationsButNeverCreatesPickTargets()
    {
        var boundary=Line("ent_boundary");boundary["selection_state"]="image_boundary";boundary["structural_owner_image_id"]="ent_image";
        var regular=Line();regular["strokes_mm"]=new JArray(30,40,40,40);regular["start_mm"]=new JArray(30,40);regular["end_mm"]=new JArray(40,40);
        var json=Snapshot(Image(),boundary,regular);
        json["constraints"]=new JArray(new JObject { ["id"]="ent_constraint",["type"]="constraint",["supported"]=true,["binding_state"]="complete",
            ["related_entities"]=new JArray(new JObject { ["entity_id"]="ent_boundary",["type"]="line",["subpart"]="Start" }) });
        var snapshot=Parse(json);var structural=snapshot.Entities[1];
        Assert.True(structural.Supported);Assert.False(structural.Selectable);Assert.True(structural.TryGetSubpartPoint(SketchSubpart.Start,out _));
        Assert.Single(snapshot.Constraints[0].RelatedEntities);
        Assert.Empty(snapshot.PickAll(new CadPoint(3,4),1));Assert.Empty(snapshot.PickTargets(new CadPoint(3,4),1));
        var hits=NativeSketchWindow.Query(snapshot,new CadPoint(-100,-100),new CadPoint(100,100),NativeSketchWindowMode.Contained);
        Assert.Equal("ent_line",Assert.Single(hits).Reference.EntityId);
    }

    [Theory]
    [InlineData("missing")][InlineData("foreign")][InlineData("wrong_type")][InlineData("unexpected")]
    public void StructuralOwnershipMustResolveToAnImageInThisSnapshot(string failure)
    {
        var boundary=Line();boundary["selection_state"]="image_boundary";boundary["structural_owner_image_id"]="ent_image";
        var image=Image();
        if(failure=="missing") boundary.Remove("structural_owner_image_id");
        if(failure=="foreign") boundary["structural_owner_image_id"]="ent_other_image";
        if(failure=="wrong_type") { image.Remove("image_current");image["type"]="line";image["selection_state"]="unavailable"; }
        if(failure=="unexpected") boundary["selection_state"]="editable";
        Assert.Throws<FormatException>(()=>Parse(Snapshot(image,boundary)));
    }

    [Fact]
    public void SharedStructuralAnchorIsReadableButCannotBePickedAsACurveEndpoint()
    {
        var line=Line();line["structural_subparts"]=new JArray(new JObject { ["subpart"]="Start",["owner_image_id"]="ent_image" });
        var snapshot=Parse(Snapshot(Image(),line));var geometry=snapshot.Entities[1];
        Assert.True(geometry.TryGetSubpartPoint(SketchSubpart.Start,out _));Assert.False(geometry.CanSelectSubpart(SketchSubpart.Start));
        Assert.True(geometry.CanSelectSubpart(SketchSubpart.End));
        Assert.Empty(snapshot.PickTargets(new CadPoint(3,4),1,"line",SketchSubpart.Start));
        Assert.Empty(NativeSketchWindow.Query(snapshot,new CadPoint(2,3),new CadPoint(4,5),NativeSketchWindowMode.Contained,"line",SketchSubpart.Start));
    }

    [Theory]
    [InlineData("unknown_part")][InlineData("whole_entity")][InlineData("unavailable_anchor")][InlineData("foreign_owner")]
    [InlineData("missing_owner")][InlineData("extra_field")][InlineData("duplicate")][InlineData("empty")]
    public void StructuralSubpartsCannotSmuggleUnboundOrAmbiguousSelections(string failure)
    {
        var line=Line();var binding=new JObject { ["subpart"]="Start",["owner_image_id"]="ent_image" };
        var bindings=new JArray(binding);line["structural_subparts"]=bindings;
        switch(failure)
        {
            case "unknown_part":binding["subpart"]="99";break;
            case "whole_entity":binding["subpart"]="Entity";break;
            case "unavailable_anchor":binding["subpart"]="Center";break;
            case "foreign_owner":binding["owner_image_id"]="ent_other_image";break;
            case "missing_owner":binding.Remove("owner_image_id");break;
            case "extra_field":binding["editable"]=true;break;
            case "duplicate":bindings.Add(binding.DeepClone());break;
            case "empty":bindings.Clear();break;
        }
        Assert.Throws<FormatException>(()=>Parse(Snapshot(Image(),line)));
    }
}
