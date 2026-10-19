using InventorXrSo.Core.Backend;
using Newtonsoft.Json.Linq;
using Xunit;

namespace XrSo.Core.Tests.Backend;

public sealed class NativeSketchTextGlyphMeshTests
{
    private static JObject Mesh()=>new(){["schema"]=1,["coordinate_space"]="model_mm",["vertices_mm"]=new JArray(30,40,50,40,40,50,30,50,50),["indices"]=new JArray(0,1,2),["color_rgb"]=new JArray(17,34,51)};
    private static JObject Text(bool reference)=>new(){["type"]="text",["supported"]=true,["text"]="Native",["text_mm"]=new JArray(10,20),["text_glyphs"]=Mesh()};
    private static JObject Snapshot()
    {
        var text=Text(true);text["id"]="ent_text";
        return new JObject{["schema_version"]=1,["document_id"]="doc",["revision"]="r1",["sketch_id"]="ent_sketch",["sketch_name"]="Glyphs",["partial"]=false,
            ["frame"]=new JObject{["origin_mm"]=new JArray(200,300,400),["x_axis"]=new JArray(1,0,0),["y_axis"]=new JArray(0,0,1)},
            ["entities"]=new JArray(text),["dimensions"]=new JArray(),["constraints"]=new JArray()};
    }
    [Fact]
    public void CurrentAndPreviewKeepModelCoordinatesAndOwnTheirMeshData()
    {
        var json=Snapshot();var parsed=NativeSketchSnapshot.Parse(json,new DocumentState("doc","r1","v"),"ent_sketch");
        Assert.Equal(new CadPoint(30,40,50),parsed.Entities[0].TextGlyphs.VerticesMm[0]);
        json["entities"]![0]!["text_glyphs"]!["vertices_mm"]![0]=900;
        Assert.Equal(30,parsed.Entities[0].TextGlyphs.VerticesMm[0].X);
        var preview=SketchSnapshot.Parse(new JObject{["sketch_name"]="Glyphs",["frame"]=json["frame"]!.DeepClone(),["partial"]=false,
            ["entities"]=new JArray(),["native_entities"]=new JArray(Text(false)),["dimensions"]=new JArray(),["geometric_constraints"]=new JArray()});
        Assert.Empty(preview.NativeAnnotations);Assert.Single(preview.NativeTextGlyphs);
        Assert.Equal(new CadPoint(30,40,50),preview.NativeTextGlyphs[0].VerticesMm[0]);
    }
    [Theory]
    [InlineData("space")][InlineData("index")][InlineData("negative")][InlineData("index_float")][InlineData("color")][InlineData("color_float")][InlineData("vertex_nan")][InlineData("vertex_string")][InlineData("length")][InlineData("cad_id")][InlineData("empty_mismatch")]
    public void MalformedMeshCannotBecomeRenderableOrMutationComplete(string kind)
    {
        var mesh=Mesh();switch(kind)
        {
            case "space":mesh["coordinate_space"]="sketch_mm";break;
            case "index":mesh["indices"]![0]=3;break;
            case "negative":mesh["indices"]![0]=-1;break;
            case "index_float":mesh["indices"]![0]=0.0;break;
            case "color":mesh["color_rgb"]![0]=256;break;
            case "color_float":mesh["color_rgb"]![0]=17.0;break;
            case "vertex_nan":mesh["vertices_mm"]![0]=double.NaN;break;
            case "vertex_string":mesh["vertices_mm"]![0]="30";break;
            case "length":((JArray)mesh["vertices_mm"]!).RemoveAt(0);break;
            case "cad_id":mesh["id"]="ent_tentative";break;
            case "empty_mismatch":mesh["vertices_mm"]=new JArray();break;
        }
        Assert.Throws<FormatException>(()=>NativeSketchTextGlyphMesh.Parse(mesh));
        var snapshot=Snapshot();snapshot["entities"]![0]!["text_glyphs"]=mesh;
        Assert.Throws<FormatException>(()=>NativeSketchSnapshot.Parse(snapshot,new DocumentState("doc","r1","v"),"ent_sketch"));
    }
    [Fact]
    public void WhitespaceMeshAndGlobalVertexBudgetAreExplicit()
    {
        var empty=Mesh();empty["vertices_mm"]=new JArray();empty["indices"]=new JArray();Assert.Empty(NativeSketchTextGlyphMesh.Parse(empty).VerticesMm);
        var snapshot=Snapshot();var entities=(JArray)snapshot["entities"]!;entities.Clear();
        for(int i=0;i<13;i++)
        {
            var text=Text(true);text["id"]="ent_text"+i;var mesh=Mesh();mesh["vertices_mm"]=new JArray(Enumerable.Repeat(1.0,8192*3));text["text_glyphs"]=mesh;entities.Add(text);
        }
        Assert.Throws<FormatException>(()=>NativeSketchSnapshot.Parse(snapshot,new DocumentState("doc","r1","v"),"ent_sketch"));
    }
}
