using Inventor;
using Newtonsoft.Json.Linq;

internal static partial class Program
{
    private sealed class UnknownCurve92(string message):Exception(message);
    private static string CurveKey92(SketchEntity entity) { byte[] key=Array.Empty<byte>();entity.GetReferenceKey(ref key,0);Require89(key.Length is >0 and <=65536,"Invalid native curve/point key.");return Convert.ToBase64String(key); }
    private static JObject Error92(string stage,Exception error)=>new(){["status"]="UNKNOWN",["stage"]=stage,["type"]=error.GetType().FullName,["hresult"]=error.HResult};
    private static JArray XY92(Point2d point)=>new(point.X*10,point.Y*10);
    private static JObject ColorState92(Color? color)=>color==null?new JObject{["null"]=true}:new JObject { ["r"]=color.Red,["g"]=color.Green,["b"]=color.Blue,["opacity"]=color.Opacity,["source"]=(int)color.ColorSourceType };
    private static JObject EntityState92(SketchEntity entity)
    {
        Phase="curve92_read_entity_state";
        var row=new JObject { ["key"]=CurveKey92(entity),["native_type"]=entity.Type.ToString(),["parent_key"]=Key73(entity.Parent),
            ["construction"]=entity.Construction,["reference"]=entity.Reference,["constraint_status"]=entity.ConstraintStatus.ToString(),
            ["has_reference_component"]=entity.HasReferenceComponent,["owned_by"]=new JArray((entity.OwnedBy?.Cast<SketchEntity>()??Enumerable.Empty<SketchEntity>()).Select(CurveKey92)) };
        var flags=new JObject();row["style_flags"]=flags;
        void Optional(string name,Func<JToken> read){try{flags[name]=read();}catch(Exception error){flags[name]=Error92(name,error);}}
        Optional("attribute_set_count",()=>new JValue(entity.AttributeSets.Count));
        if(flags["attribute_set_count"]?.Type==JTokenType.Integer&&(int)flags["attribute_set_count"]!>0) flags["attribute_values"]=new JObject { ["status"]="UNKNOWN",["reason"]="Nonempty attributes are outside the first empty-attribute fixture profile" };
        Optional("block_path_count",()=>new JValue(entity.SketchBlockPath.Count));
        Optional("layer",()=>entity.Layer==null?JValue.CreateNull():new JObject { ["name"]=entity.Layer.Name,["internal_name"]=entity.Layer.InternalName });
        if(entity is SketchPoint point) { row["point_mm"]=XY92(point.Geometry);row["hole_center"]=point.HoleCenter;return row; }
        Curve2dEvaluator evaluator;
        var points=new JObject();row["structural_points"]=points;
        void Point(string role,SketchPoint point)=>points[role]=new JObject { ["key"]=CurveKey92((SketchEntity)point),["point_mm"]=XY92(point.Geometry) };
        if(entity is SketchLine line)
        {
            row["kind"]="line";Point("start",line.StartSketchPoint);Point("end",line.EndSketchPoint);row["centerline"]=line.Centerline;
            evaluator=line.Geometry.Evaluator;
            Optional("line_type",()=>new JValue((int)line.LineType));Optional("line_weight",()=>new JValue(line.LineWeight));Optional("line_scale",()=>new JValue(line.LineScale));
            Optional("definition_space",()=>new JValue((int)line.LineDefinitionSpace));Optional("override_color",()=>ColorState92(line.OverrideColor));
        }
        else if(entity is SketchCircle circle)
        {
            row["kind"]="circle";Point("center",circle.CenterSketchPoint);row["radius_mm"]=circle.Radius*10;evaluator=circle.Geometry.Evaluator;
            Optional("line_type",()=>new JValue((int)circle.LineType));Optional("line_weight",()=>new JValue(circle.LineWeight));Optional("line_scale",()=>new JValue(circle.LineScale));
            Optional("definition_space",()=>new JValue((int)circle.LineDefinitionSpace));Optional("override_color",()=>ColorState92(circle.OverrideColor));
        }
        else if(entity is SketchArc arc)
        {
            row["kind"]="arc";Point("center",arc.CenterSketchPoint);Point("start",arc.StartSketchPoint);Point("end",arc.EndSketchPoint);
            row["radius_mm"]=arc.Radius*10;row["start_angle_rad"]=arc.StartAngle;row["sweep_angle_rad"]=arc.SweepAngle;
            row["geometry_start_angle_rad"]=arc.Geometry.StartAngle;row["geometry_sweep_angle_rad"]=arc.Geometry.SweepAngle;evaluator=arc.Geometry.Evaluator;
            Optional("line_type",()=>new JValue((int)arc.LineType));Optional("line_weight",()=>new JValue(arc.LineWeight));Optional("line_scale",()=>new JValue(arc.LineScale));
            Optional("definition_space",()=>new JValue((int)arc.LineDefinitionSpace));Optional("override_color",()=>ColorState92(arc.OverrideColor));
        }
        else throw new UnknownCurve92("Unsupported native entity in first circle/arc/line fixture graph.");
        Phase="curve92_read_curve_domain";double min=0,max=0;
        evaluator.GetParamExtents(out min,out max);Require89(double.IsFinite(min)&&double.IsFinite(max)&&max>min,"Native curve domain invalid.");
        Phase="curve92_read_curve_strokes";int count=0;double[] strokes=Array.Empty<double>();
        evaluator.GetStrokes(min,max,0.0001,out count,out strokes);
        Require89(count is >=2 and <=8192&&strokes.Length==count*2&&strokes.All(double.IsFinite),"Native stroke budget/shape invalid.");
        double[] parameters=Enumerable.Range(0,101).Select(i=>min+(max-min)*i/100).ToArray(),samples=Array.Empty<double>();
        Phase="curve92_read_curve_samples";
        evaluator.GetPointAtParam(ref parameters,ref samples);Require89(samples.Length==202&&samples.All(double.IsFinite),"Native evaluator sample shape invalid.");
        row["domain"]=new JArray(min,max);row["strokes_mm"]=new JArray(strokes.Select(v=>v*10));row["samples_mm"]=new JArray(samples.Select(v=>v*10));
        return row;
    }
    private static JObject ConstraintState92(GeometricConstraint constraint)
    {
        Phase="curve92_read_constraint_state";
        byte[] key=Array.Empty<byte>();constraint.GetReferenceKey(ref key,0);Require89(key.Length>0,"Native constraint key empty.");
        var row=new JObject { ["key"]=Convert.ToBase64String(key),["native_type"]=constraint.Type.ToString(),["deletable"]=constraint.Deletable,["members"]=new JArray() };
        var members=(JArray)row["members"]!;
        void Member(string role,SketchEntity entity)=>members.Add(new JObject { ["role"]=role,["key"]=CurveKey92(entity),["type"]=entity.Type.ToString() });
        switch(constraint)
        {
            case CoincidentConstraint coincident:Member("EntityOne",coincident.EntityOne);Member("EntityTwo",coincident.EntityTwo);row["endpoint_constraint"]=coincident.StartOrEndPointConstraint;break;
            case ConcentricConstraint concentric:Member("EntityOne",concentric.EntityOne);Member("EntityTwo",concentric.EntityTwo);row["center_constraint"]=concentric.CenterPointConstraint;break;
            case TangentSketchConstraint tangent:Member("EntityOne",tangent.EntityOne);Member("EntityTwo",tangent.EntityTwo);break;
            case GroundConstraint ground:Member("Entity",ground.Entity);break;
            default:row["binding_status"]="UNKNOWN";return row;
        }
        row["binding_status"]="COMPLETE_TYPED_MEMBERS";return row;
    }
    private static JObject SketchState92(PlanarSketch sketch)
    {
        Require89(sketch.SketchEntities.Count<=2000&&sketch.SketchPoints.Count<=2000&&sketch.GeometricConstraints.Count<=4000,"Curve92 snapshot graph budget exceeded.");
        var entities=sketch.SketchEntities.Cast<SketchEntity>().ToList();var keys=entities.Select(CurveKey92).ToHashSet(StringComparer.Ordinal);
        foreach(SketchPoint point in sketch.SketchPoints) if(keys.Add(CurveKey92((SketchEntity)point))) entities.Add((SketchEntity)point);
        Require89(entities.Count<=2000,"Curve92 combined curve/point inventory exceeds budget.");
        return new JObject{["key"]=Key73(sketch),["name"]=sketch.Name,["constraint_status"]=sketch.ConstraintStatus.ToString(),
            ["sketch_entities_keys"]=new JArray(sketch.SketchEntities.Cast<SketchEntity>().Select(CurveKey92)),
            ["sketch_point_keys"]=new JArray(sketch.SketchPoints.Cast<SketchPoint>().Select(p=>CurveKey92((SketchEntity)p))),
            ["entities"]=new JArray(entities.Select(EntityState92)),
            ["constraints"]=new JArray(sketch.GeometricConstraints.Cast<GeometricConstraint>().Select(ConstraintState92)),["dimension_count"]=sketch.DimensionConstraints.Count};
    }
    private static JObject FullState92()
    {
        GuardDocuments73();var before=Documents.ToDictionary(d=>d.InternalName,d=>(d.Document.DatabaseRevisionId,d.Document.Dirty));
        var full=CopyFullNative88();full["curve_point_graph92"]=new JArray(Documents.Select(d=>new JObject { ["document_id"]=d.DocumentId,
            ["sketches"]=new JArray(d.Document.ComponentDefinition.Sketches.Cast<PlanarSketch>().Select(SketchState92)) }));
        Require89(Documents.All(d=>d.Document.DatabaseRevisionId==before[d.InternalName].DatabaseRevisionId&&d.Document.Dirty==before[d.InternalName].Dirty),"Curve92 read changed native Dirty/DBRevision.");
        GuardDocuments73();return full;
    }
}
