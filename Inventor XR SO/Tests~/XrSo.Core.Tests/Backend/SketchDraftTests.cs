using InventorXrSo.Core.Backend;
using Newtonsoft.Json.Linq;

namespace InventorXrSo.Core.Tests.Backend;

public class SketchDraftTests
{
    [Fact]
    public void RectangleSidesHaveDistinctConstraintReferencesAndCascadeOnRemoval()
    {
        var draft=new SketchDraft("3",null,"XR");
        draft.Add(new SketchElement(SketchShape.Circle,default,default,2));
        draft.Add(new SketchElement(SketchShape.Rectangle,new CadPoint(10,20),new CadPoint(40,60)));
        draft.Add(new SketchElement(SketchShape.Line,new CadPoint(70,20),new CadPoint(80,50)));
        Assert.Equal(new[]{0,4,5,6,7,8},draft.ConstraintEntityKeys);
        var side=draft.ConstraintEntity(5);
        Assert.Equal(40,side.A.X); Assert.Equal(20,side.A.Y); Assert.Equal(60,side.B.Y);
        draft.AddEntityConstraint("equal",5,8);
        Assert.Equal(new[]{"line:2","line:5"},draft.Operations().Last!["arguments"]!["entity_ids"]!.Values<string>());
        Assert.Throws<ArgumentException>(()=>draft.ConstraintEntity(1));
        draft.RemoveLast(); Assert.Equal(0,draft.ConstraintCount);
    }
    [Fact]
    public void ConfirmedConstraintsUseTypedReferencesAndPrecedeFeature()
    {
        var draft=new SketchDraft("3",null,"XR");
        draft.Add(new SketchElement(SketchShape.Rectangle,default,new CadPoint(2,3)));
        draft.Add(new SketchElement(SketchShape.Line,new CadPoint(10,0),new CadPoint(20,3)));
        draft.Add(new SketchElement(SketchShape.Circle,new CadPoint(20,8),default,5));
        draft.AddConstraint("tangent",1,2);
        var operations=draft.Operations(DesignOperations.Extrude("XR",10,"new_body","positive"));
        Assert.Equal("add_sketch_constraint",(string)operations[4]!["command"]!);
        Assert.Equal(new[]{"line:5","circle:1"},operations[4]!["arguments"]!["entity_ids"]!.Values<string>());
        Assert.Equal("extrude",(string)operations[5]!["command"]!);
        Assert.Throws<ArgumentException>(()=>draft.AddConstraint("tangent",2,1));
        draft.RemoveLast(); Assert.Equal(0,draft.ConstraintCount);
    }
    [Fact]
    public void ConstraintValidationRejectsIncompatiblePairsAndRequiresSymmetryAxis()
    {
        var draft=new SketchDraft("3",null,"XR");
        for(int i=0;i<3;i++) draft.Add(new SketchElement(SketchShape.Line,new CadPoint(i*10,0),new CadPoint(i*10+3,5)));
        Assert.Throws<ArgumentException>(()=>draft.AddConstraint("tangent",0,1));
        Assert.Throws<ArgumentException>(()=>draft.AddConstraint("symmetric",0,1));
        draft.AddConstraint("symmetric",0,1,2);
        Assert.Equal(new[]{"line:1","line:2","line:3"},draft.Operations().Last!["arguments"]!["entity_ids"]!.Values<string>());
        draft.Replace(2,new SketchElement(SketchShape.Circle,default,default,2));
        Assert.Equal(0,draft.ConstraintCount);
    }
    [Fact]
    public void EqualCirclesUseRadiusConstraintAndBudgetIncludesRelations()
    {
        var draft=new SketchDraft("3",null,"XR");
        for(int i=0;i<29;i++) draft.Add(new SketchElement(SketchShape.Circle,new CadPoint(i*10,0),default,2));
        draft.AddConstraint("equal",0,1);
        Assert.Equal("equal_radius",(string)draft.Operations().Last!["arguments"]!["type"]!);
        Assert.Throws<InvalidOperationException>(()=>draft.AddConstraint("equal",1,2));
        Assert.Throws<InvalidOperationException>(()=>draft.Add(new SketchElement(SketchShape.Circle,default,default,2)));
        draft.RemoveLastConstraint(); Assert.Equal(0,draft.ConstraintCount);
    }
    [Fact]
    public void ConstraintSelectionKeepsRelationsOfOnlyThePickedNativeEntity()
    {
        var snapshot=SketchSnapshot.Parse(JObject.Parse(@"{""sketch_name"":""XR"",""frame"":{""origin_mm"":[0,0,0],""x_axis"":[1,0,0],""y_axis"":[0,1,0]},
            ""entities"":[{""type"":""line"",""start_mm"":[0,0],""end_mm"":[10,0],""constraints"":[""kHorizontalConstraintObject""]},
            {""type"":""circle"",""center_mm"":[20,20],""radius_mm"":4,""constraints"":[""kRadiusDimConstraintObject""]}],
            ""geometric_constraints"":[{""type"":""kHorizontalConstraintObject""}]}"));
        Assert.Equal("kHorizontalConstraintObject",Assert.Single(snapshot.Pick(new CadPoint(4,0.2),1).Constraints));
        Assert.Equal("kRadiusDimConstraintObject",Assert.Single(snapshot.Pick(new CadPoint(24,20),1).Constraints));
        Assert.Null(snapshot.Pick(new CadPoint(20,20),1));
        Assert.Equal("kHorizontalConstraintObject",Assert.Single(snapshot.Constraints));
    }
    [Fact]
    public void NativeDimensionSnapshotPreservesExpressionAndTextCoordinates()
    {
        var json=JObject.Parse(@"{""sketch_name"":""XR"",""frame"":{""origin_mm"":[10,20,30],""x_axis"":[0,1,0],""y_axis"":[0,0,1]},
            ""entities"":[],""dimensions"":[{""name"":""d1"",""expression"":""Width / 2"",""text_mm"":[6,-8],""driven"":true}]}" );
        var snapshot=SketchSnapshot.Parse(json); var dimension=Assert.Single(snapshot.Dimensions);
        Assert.Equal("Width / 2",dimension.Expression); Assert.True(dimension.Driven);
        Assert.Equal(26,snapshot.Frame.ToModel(dimension.TextPoint).Y);
        Assert.Equal(22,snapshot.Frame.ToModel(dimension.TextPoint).Z);
    }
    [Fact]
    public void DimensionSelectionFindsClosestGeometryAndKeepsPlacementInSketchSpace()
    {
        var draft=new SketchDraft("3",Frame,"XR_Pick");
        draft.Add(new SketchElement(SketchShape.Line,new CadPoint(0,0),new CadPoint(10,0)));
        draft.Add(new SketchElement(SketchShape.Rectangle,new CadPoint(20,20),new CadPoint(30,30)));
        draft.Add(new SketchElement(SketchShape.Circle,new CadPoint(50,50),default,5));
        Assert.Equal(0,draft.Pick(new CadPoint(5,0.5),1));
        Assert.Equal(1,draft.Pick(new CadPoint(25,20.2),1));
        Assert.Equal(2,draft.Pick(new CadPoint(55.4,50),1));
        Assert.Equal(-1,draft.Pick(new CadPoint(25,25),1));
        Assert.Equal(-1,draft.Pick(new CadPoint(50,50),1));
        draft.Replace(0,new SketchElement(SketchShape.Line,new CadPoint(0,0),new CadPoint(12,0),dimensioned:true,dimensionText:new CadPoint(6,-8)));
        var args=draft.Operations()[1]!["arguments"]!;
        Assert.Equal(6,(double)args["text_x"]!); Assert.Equal(-8,(double)args["text_y"]!);
    }
    [Theory]
    [InlineData(SketchShape.Line)][InlineData(SketchShape.Rectangle)][InlineData(SketchShape.Circle)]
    public void QuotedElementRequestsPersistentDrivingDimensionsWithoutExtraBatchSteps(SketchShape shape)
    {
        var draft = new SketchDraft("3",Frame,"XR_Dimension");
        draft.Add(new SketchElement(shape,new CadPoint(1,2),new CadPoint(9,12),4,true));
        var ops = draft.Operations(); Assert.Equal(2,ops.Count);
        Assert.True((bool)ops[1]!["arguments"]!["add_dimensions"]!);
        Assert.Null(new SketchElement(shape,new CadPoint(1,2),new CadPoint(9,12),4).Operation("XR")["arguments"]!["add_dimensions"]);
    }
    [Fact]
    public void FaceProjectionRemovesOnlyNormalErrorOnOffsetRotatedPlane()
    {
        var face = new DesignFace("ent_face",new CadPoint(10,20,30),new CadPoint(0.6,0.8,0));
        var projected = face.Project(new CadPoint(17,21,42));
        Assert.Equal(0,(projected-face.PointMm).Dot(face.Normal),10);
        Assert.Equal(42,projected.Z);
        Assert.Equal(-5,(projected-face.PointMm).Dot(new CadPoint(-0.8,0.6,0)),10);
        Assert.Throws<FormatException>(() => new DesignFace("ent_face",default,default));
        Assert.Throws<FormatException>(() => new DesignFace("ent_face",default,new CadPoint(2,0,0)));
    }
    private static SketchFrame Frame => new(new CadPoint(10,20,30),new CadPoint(0,1,0),new CadPoint(0,0,1));
    [Fact]
    public void RayAndNumericInputShareExactRotatedModelCoordinates()
    {
        var model = Frame.ToModel(new CadPoint(5,7));
        Assert.Equal(10,model.X); Assert.Equal(25,model.Y); Assert.Equal(37,model.Z);
        Assert.True(Frame.IntersectRay(new CadPoint(50,25,37),new CadPoint(-1,0,0),out var sketch));
        Assert.Equal(5,sketch.X); Assert.Equal(7,sketch.Y);
        Assert.False(Frame.IntersectRay(new CadPoint(50,25,37),new CadPoint(1,0,0),out _));
        Assert.False(Frame.IntersectRay(new CadPoint(50,25,37),new CadPoint(0,1,0),out _));
    }
    [Fact]
    public void NonOrthonormalFramesAreRejected()
    { Assert.Throws<FormatException>(() => new SketchFrame(default,new CadPoint(1,0,0),new CadPoint(1,0,0))); }

    [Fact]
    public void NamedSketchAndFeatureAreOneBoundedIndependentBatch()
    {
        var draft = new SketchDraft("3",Frame,"XR_Test");
        draft.Add(new SketchElement(SketchShape.Rectangle,new CadPoint(0,0),new CadPoint(20,30)));
        var ops = draft.Operations(DesignOperations.Extrude(draft.Name,40,"join","positive"));
        Assert.Equal(3,ops.Count); Assert.Equal("XR_Test",(string)ops[0]!["arguments"]!["name"]);
        Assert.Equal("XR_Test",(string)ops[2]!["arguments"]!["sketch_name"]);
        ops[1]!["arguments"]!["x2"] = 999;
        Assert.Equal(20,(double)draft.Operations()[1]!["arguments"]!["x2"]!);
    }

    [Fact]
    public void SnapUsesEndpointsMidpointsAndCentersBeforeAxisInference()
    {
        var draft = new SketchDraft("XY",Frame);
        draft.Add(new SketchElement(SketchShape.Rectangle,new CadPoint(0,0),new CadPoint(20,30)));
        Assert.Equal("Estremo",draft.Snap(new CadPoint(20.3,30.2),null,1).Kind);
        var middle = draft.Snap(new CadPoint(10.2,0.1),null,1);
        Assert.Equal("Medio",middle.Kind); Assert.Equal(10,middle.Point.X); Assert.Equal(0,middle.Point.Y);
        var horizontal = draft.Snap(new CadPoint(40,6.2),new CadPoint(35,6),1);
        Assert.Equal("Orizzontale",horizontal.Kind); Assert.Equal(6,horizontal.Point.Y);
        draft.Add(new SketchElement(SketchShape.Circle,new CadPoint(50,50),default,3));
        Assert.Equal("Centro",draft.Snap(new CadPoint(50.5,50),null,1).Kind);
    }

    [Fact]
    public void DraftLimitReservesOperationsForCreationAndExtrusion()
    {
        var draft = new SketchDraft("XY",Frame);
        for (int i=0;i<30;i++) draft.Add(new SketchElement(SketchShape.Circle,new CadPoint(i,0),default,1));
        Assert.Equal(32,draft.Operations(DesignOperations.Extrude(draft.Name,2,"join","positive")).Count);
        Assert.Throws<InvalidOperationException>(() => draft.Add(new SketchElement(SketchShape.Circle,default,default,1)));
        draft.RemoveLast(); Assert.Equal(29,draft.Elements.Count);
    }
    [Fact]
    public void ParsedPreviewPreservesSketchFrameAndGeometryWithoutSolidBodies()
    {
        var json = JObject.Parse("{\"sketch_name\":\"XR\",\"visible\":true,\"frame\":{\"origin_mm\":[10,20,30],\"x_axis\":[0,1,0],\"y_axis\":[0,0,1]},\"entities\":[{\"type\":\"circle\",\"center_mm\":[5,7],\"radius_mm\":3}]}");
        var snapshot = SketchSnapshot.Parse(json);
        var element = Assert.Single(snapshot.Elements);
        Assert.Equal(3,element.Radius); Assert.Equal(25,snapshot.Frame.ToModel(element.A).Y);
        Assert.Equal(65,element.Outline().Length);
    }
}
