using InventorXrSo.Core.Backend;
using Newtonsoft.Json.Linq;
using Xunit;

namespace XrSo.Core.Tests.Backend;

public sealed class NativeSketchTextBoundsTests
{
    private static JObject Snapshot(JArray bounds,string type="text")=>new(){["schema_version"]=1,["document_id"]="doc",["revision"]="r1",["sketch_id"]="ent_sketch",["sketch_name"]="Bounds",["partial"]=false,
        ["frame"]=new JObject{["origin_mm"]=new JArray(200,300,400),["x_axis"]=new JArray(1,0,0),["y_axis"]=new JArray(0,0,1)},
        ["entities"]=new JArray(new JObject{["id"]="ent_text",["type"]=type,["supported"]=true,["text"]="A B",["text_mm"]=new JArray(0,0),["text_bounds_mm"]=bounds}),["dimensions"]=new JArray(),["constraints"]=new JArray()};
    private static NativeSketchSnapshot Parse(JObject json)=>NativeSketchSnapshot.Parse(json,new DocumentState("doc","r1","v"),"ent_sketch");
    [Theory]
    [InlineData(0)][InlineData(37)][InlineData(90)][InlineData(180)][InlineData(270)]
    public void PicksInteriorWhitespaceAndEdgesInRotatedSketchCoordinates(double degrees)
    {
        double c=Math.Cos(degrees*Math.PI/180),s=Math.Sin(degrees*Math.PI/180);
        CadPoint Rotate(double x,double y)=>new(10+c*x-s*y,20+s*x+c*y);
        var bounds=new JArray();foreach(var p in new[]{Rotate(0,0),Rotate(10,0),Rotate(10,4),Rotate(0,4)}){bounds.Add(p.X);bounds.Add(p.Y);}
        var json=Snapshot(bounds);var snapshot=Parse(json);var entity=Assert.Single(snapshot.Entities);
        Assert.True(entity.DistanceTo(Rotate(5,2))<1e-10);
        Assert.Equal(2,entity.DistanceTo(Rotate(12,2)),9);
        Assert.Equal(Math.Sqrt(5),entity.DistanceTo(Rotate(-1,-2)),9);
        Assert.Single(snapshot.PickTargets(Rotate(5,2),.01,"text",SketchSubpart.Entity));
        Assert.Empty(snapshot.PickTargets(Rotate(12,2),1.99,"text"));
        Assert.Equal("ent_text",Assert.Single(snapshot.PickTargets(Rotate(10,2),.01)).Reference.EntityId);
        Assert.False(entity.TryGetSubpartPoint(SketchSubpart.Center,out _));
        bounds[0]=999;Assert.NotEqual(999,entity.TextBoundsMm[0].X);
        Assert.Throws<ArgumentException>(()=>new CadPoint(double.NaN,0));
    }
    [Theory]
    [InlineData("length")][InlineData("nan")][InlineData("string")][InlineData("zero")][InlineData("skew")][InlineData("open")][InlineData("huge")][InlineData("type")]
    public void MalformedBoundsCannotBePicked(string kind)
    {
        var b=new JArray(0,0,10,0,10,4,0,4);
        switch(kind){case "length":b.RemoveAt(0);break;case "nan":b[0]=double.NaN;break;case "string":b[0]="0";break;case "zero":b=new JArray(0,0,0,0,0,4,0,4);break;case "skew":b=new JArray(0,0,10,0,12,4,2,4);break;case "open":b[4]=11;break;case "huge":b[0]=1e10;break;}
        Assert.Throws<FormatException>(()=>Parse(Snapshot(b,kind=="type" ? "line" : "text")));
    }
    [Fact]
    public void WindowSelectsTextInteriorCrossingsAndContainmentWithoutInventingAnchors()
    {
        var snapshot=Parse(Snapshot(new JArray(0,0,10,0,10,4,0,4)));
        Assert.Single(NativeSketchWindow.Query(snapshot,new CadPoint(2,1),new CadPoint(3,2),NativeSketchWindowMode.Crossing));
        Assert.Empty(NativeSketchWindow.Query(snapshot,new CadPoint(2,1),new CadPoint(3,2),NativeSketchWindowMode.Contained));
        Assert.Single(NativeSketchWindow.Query(snapshot,new CadPoint(-1,-1),new CadPoint(11,5),NativeSketchWindowMode.Contained));
        Assert.Single(NativeSketchWindow.Query(snapshot,new CadPoint(9,-1),new CadPoint(11,5),NativeSketchWindowMode.Crossing));
        Assert.Empty(NativeSketchWindow.Query(snapshot,new CadPoint(11,1),new CadPoint(12,2),NativeSketchWindowMode.Crossing));
        Assert.Empty(NativeSketchWindow.Query(snapshot,new CadPoint(-1,-1),new CadPoint(11,5),NativeSketchWindowMode.Crossing,part:SketchSubpart.Center));
    }
}


