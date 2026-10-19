using InventorXrSo.Core.Backend;
using Newtonsoft.Json.Linq;

namespace InventorXrSo.Core.Tests.Backend;

public class NativeSketchPreviewTests
{
    private static JObject Preview() => new()
    {
        ["sketch_name"] = "Existing", ["entities"] = new JArray(),
        ["frame"] = new JObject { ["origin_mm"] = new JArray(0,0,0), ["x_axis"] = new JArray(1,0,0), ["y_axis"] = new JArray(0,1,0) },
        ["native_entities"] = new JArray(
            new JObject { ["type"] = "control_point_spline", ["strokes_mm"] = new JArray(0,0,1,2,3,0) },
            new JObject { ["type"] = "point", ["point_mm"] = new JArray(5,6) },
            new JObject { ["type"] = "text", ["text_mm"] = new JArray(7,8), ["text"] = "Native text" })
    };
    [Fact]
    public void ConstraintGhostHasNativeAnnotationButNeverSelectableAssociations()
    {
        var json=Preview();json["geometric_constraints"]=new JArray(new JObject {["type"]="kTangentSketchConstraintObject",["annotation_mm"]=new JArray(3,4)});
        var parsed=SketchSnapshot.Parse(json);Assert.Contains(parsed.NativeAnnotations,a=>a.Text=="Tangente" && a.Point.Equals(new CadPoint(3,4)));
        json["geometric_constraints"][0]["related_entities"]=new JArray();Assert.Throws<FormatException>(()=>SketchSnapshot.Parse(json));
        ((JObject)json["geometric_constraints"][0]).Remove("related_entities");json["geometric_constraints"][0]["id"]="ent_aborted";
        Assert.Throws<FormatException>(()=>SketchSnapshot.Parse(json));
        json["geometric_constraints"]=new JArray(Enumerable.Range(0,2001).Select(_=>new JObject {["type"]="kGroundConstraintObject"}));
        Assert.Throws<FormatException>(()=>SketchSnapshot.Parse(json));
    }
    [Fact]
    public void GhostPreservesCurvePathsPointsAndTextWithoutMakingThemAuthoringReferences()
    {
        var snapshot = SketchSnapshot.Parse(Preview());
        Assert.Equal(3,snapshot.NativePaths.Count);
        Assert.Equal("control_point_spline",snapshot.NativePaths[0].Kind);
        Assert.Equal(3,snapshot.NativePaths[0].PointsMm.Count);
        Assert.Equal("Native text",Assert.Single(snapshot.NativeAnnotations).Text);
        Assert.All(snapshot.NativePaths,path => Assert.StartsWith("profile:",path.Id));
        Assert.Null(snapshot.Pick(new CadPoint(0,0),1));
    }
    [Fact]
    public void NativeFormatPropertiesAreVisibleInTheAbortedPreview()
    {
        var json=Preview(); json["native_entities"][0]["construction"]=true;
        json["native_entities"][0]["start_mm"]=new JArray(0,0);
        json["native_entities"][1]["hole_center"]=true;
        var parsed=SketchSnapshot.Parse(json);
        Assert.Contains(parsed.NativeAnnotations,a=>a.Text=="Costruzione");
        Assert.Contains(parsed.NativeAnnotations,a=>a.Text=="Punto centrale");
        Assert.Equal("Linea d'asse",SketchSnapshot.NativeFormatLabel(new JObject {["centerline"]=true}));
    }
    [Fact]
    public void RefusesSelectableNativeIdentityInAbortedPreview()
    {
        var json = Preview(); json["native_entities"][0]["id"] = "ent_tentative";
        Assert.Throws<FormatException>(() => SketchSnapshot.Parse(json));
    }
    [Fact]
    public void RefusesTruncatedCurveCoordinates()
    {
        var json = Preview(); json["native_entities"][0]["strokes_mm"] = new JArray(0,0,1);
        Assert.Throws<FormatException>(() => SketchSnapshot.Parse(json));
    }
}
