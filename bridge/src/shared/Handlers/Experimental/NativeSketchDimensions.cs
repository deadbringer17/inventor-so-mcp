#if INVENTOR2027 && SO_EXPERIMENTAL
using System;
using System.Linq;
using Inventor;
using Newtonsoft.Json.Linq;
using Bimwright.Ipt.Shared.Infrastructure;
using Bimwright.Ipt.Shared.Handlers.Core;

namespace Bimwright.Ipt.Shared.Handlers.Experimental;

internal static class NativeSketchDimensions
{
    internal static void Edit(InventorCommandContext ctx,global::Inventor.Document document,PlanarSketch sketch,
        string sketchId,string kind,JObject options,TransientGeometry geometry)
    {
        if(options["references"] is not JArray refs || refs.Count!=1 || refs[0] is not JObject reference)
            throw new ArgumentException("Select exactly one whole native dimension.");
        string revision=ctx.Events?.Revision(EntityReferences.DocumentId(document)) ?? "";
        if(X.Str(reference,"document_id")!=EntityReferences.DocumentId(document) || X.Str(reference,"sketch_id")!=sketchId)
            throw new ArgumentException("Dimension belongs to another document or sketch.");
        if(X.Str(reference,"revision")!=revision) throw ConcurrencyFailure.StaleRevision((string?)reference["revision"],revision);
        if(X.Str(reference,"type")!="dimension" || X.Str(reference,"subpart")!="Entity")
            throw new ArgumentException("Select the whole native dimension, not geometry or a subpart.");
        var dimension=SketchReferences.Resolve(document,X.Str(reference,"entity_id"),"sketch_dimension") as DimensionConstraint
            ?? throw new ArgumentException("Native dimension reference is unavailable.");
        if(!SketchReferences.SameComObject(dimension.Parent,sketch)) throw new ArgumentException("Dimension owner does not match the selected sketch.");
        if(kind=="delete")
        {
            if(options.Properties().Any(p=>p.Name!="references")) throw new ArgumentException("Dimension deletion accepts only its reference.");
            dimension.Delete();return;
        }
        if(kind!="update") throw new ArgumentException("Unknown dimension edit.");
        if(!new[] {"driven","text_mm","parameter_name","expression","value_mm","value_deg"}.Any(n=>options[n]!=null))
            throw new ArgumentException("Supply a native dimension change.");
        if(options.TryGetValue("driven",out var drivenToken) && drivenToken.Type!=JTokenType.Boolean)
            throw new ArgumentException("Driven must be a boolean.");
        bool driven=options["driven"]==null ? dimension.Driven : (bool)options["driven"]!;
        if(driven && (options["expression"]!=null || options["value_mm"]!=null || options["value_deg"]!=null))
            throw new ArgumentException("A driven dimension cannot prescribe a value.");
        var text=options["text_mm"]==null ? dimension.TextPoint : Point(options["text_mm"],geometry);
        if(dimension.Driven!=driven) dimension.Driven=driven;
        Configure(document,dimension,options,dimension is TwoLineAngleDimConstraint or ThreePointAngleDimConstraint);
        sketch.Solve();dimension.TextPoint=text;
    }
    internal static void Add(InventorCommandContext ctx,global::Inventor.Document document,PlanarSketch sketch,
        string sketchId,string kind,JObject options,TransientGeometry geometry)
    {
        if(sketch.DimensionConstraints.Count>=2000) throw new ArgumentException("Native dimension budget exceeded (2000).");
        var entities=NativeSketchConstraints.ResolveSelections(ctx,document,sketch,sketchId,options["references"]);
        foreach(string name in new[] {"driven","linear_diameter","positive_side"})
            if(options.TryGetValue(name,out var value) && value.Type!=JTokenType.Boolean) throw new ArgumentException("Dimension choices must be booleans.");
        bool driven=X.Bool(options,"driven",false);
        if(driven && (options["expression"]!=null || options["value_mm"]!=null || options["value_deg"]!=null))
            throw new ArgumentException("A driven dimension reports the native measurement and cannot prescribe a value.");
        var text=Point(options["text_mm"],geometry);
        var c=sketch.DimensionConstraints;
        void Count(int n) {if(entities.Length!=n) throw new ArgumentException("Wrong number of native dimension selections.");}
        SketchPoint P(int i)=>entities[i] as SketchPoint ?? throw new ArgumentException("Select a native point or endpoint.");
        SketchLine L(int i)=>entities[i] as SketchLine ?? throw new ArgumentException("Select the whole native line.");
        object native;
        bool angle=false;
        switch(kind)
        {
            case "aligned": case "horizontal": case "vertical":
                SketchPoint one,two;
                if(entities.Length==1 && entities[0] is SketchLine line) {one=line.StartSketchPoint;two=line.EndSketchPoint;}
                else {Count(2);one=P(0);two=P(1);}
                native=c.AddTwoPointDistance(one,two,kind=="aligned" ? DimensionOrientationEnum.kAlignedDim : kind=="horizontal" ? DimensionOrientationEnum.kHorizontalDim : DimensionOrientationEnum.kVerticalDim,text,driven);break;
            case "angle_two_lines": Count(2);native=c.AddTwoLineAngle(L(0),L(1),text,driven);angle=true;break;
            case "angle_three_points": Count(3);native=c.AddThreePointAngle(P(0),P(1),P(2),text,driven);angle=true;break;
            case "radius": Count(1);native=c.AddRadius(entities[0],text,driven);break;
            case "diameter": Count(1);native=c.AddDiameter(entities[0],text,driven);break;
            case "arc_length": Count(1);native=c.AddArcLength(entities[0],text,driven);break;
            case "ellipse_major": case "ellipse_minor":
                Count(1);object side=options["positive_side"]==null ? Type.Missing : (bool)options["positive_side"]!;
                native=c.AddEllipseRadius(entities[0],kind=="ellipse_major",text,side,driven);break;
            case "offset": Count(2);native=c.AddOffset(L(0),entities[1],text,X.Bool(options,"linear_diameter",false),driven);break;
            case "tangent_distance": Count(2);
                native=c.AddTangentDistance(entities[0],entities[1],Point(options["proximity_one_mm"],geometry),Point(options["proximity_two_mm"],geometry),text,X.Bool(options,"linear_diameter",false),driven);break;
            case "offset_spline": Count(1);
                native=c.AddOffsetSpline(entities[0] as SketchOffsetSpline ?? throw new ArgumentException("Select the native offset spline."),text,driven);break;
            default:throw new ArgumentException("Unknown native dimension variant.");
        }
        var dimension=(DimensionConstraint)native;
        Configure(document,dimension,options,angle);
        // Solving a driving value moves Inventor's initial annotation placement.
        // Apply the requested text position after the geometry reaches its new state.
        sketch.Solve();
        dimension.TextPoint=text;
    }

    internal static Point2d Point(JToken? token,TransientGeometry geometry)
    {
        if(token is not JArray point || point.Count!=2 || point.Any(v=>v.Type is not (JTokenType.Float or JTokenType.Integer)))
            throw new ArgumentException("Sketch coordinates require two finite coordinates in mm.");
        double x=(double)point[0],y=(double)point[1];
        if(!double.IsFinite(x) || !double.IsFinite(y)) throw new ArgumentException("Dimension placement is not finite.");
        return geometry.CreatePoint2d(UnitConvert.MmToCm(x),UnitConvert.MmToCm(y));
    }

    internal static void Configure(global::Inventor.Document document,DimensionConstraint dimension,JObject options,bool angle)
    {
        var parameter=dimension.Parameter;
        if(options.TryGetValue("parameter_name",out var name))
        {
            if(name.Type!=JTokenType.String || string.IsNullOrWhiteSpace((string?)name) || ((string)name!).Length>128 || ((string)name!).Any(char.IsControl))
                throw new ArgumentException("Invalid native dimension parameter name.");
            parameter.Name=(string)name!;
        }
        if(dimension.Driven) return;
        var expression=options["expression"];
        var numeric=options[angle ? "value_deg" : "value_mm"];
        if(options[angle ? "value_mm" : "value_deg"]!=null || (expression!=null && numeric!=null))
            throw new ArgumentException("Supply one dimension expression or a value in the variant's unit.");
        if(expression!=null)
        {
            if(expression.Type!=JTokenType.String || string.IsNullOrWhiteSpace((string?)expression) || ((string)expression!).Length>2048
                || ((string)expression!).Any(char.IsControl) || !document.UnitsOfMeasure.IsExpressionValid((string)expression!,parameter.get_Units()))
                throw new ArgumentException("Native dimension expression is invalid for its units.");
            parameter.Expression=(string)expression!;
        }
        else if(numeric!=null)
        {
            if(numeric.Type is not (JTokenType.Float or JTokenType.Integer)) throw new ArgumentException("Dimension value must be numeric.");
            double value=(double)numeric;
            if(!double.IsFinite(value) || value<=0) throw new ArgumentException("Driving dimension value must be finite and positive.");
            parameter.Value=angle ? value*Math.PI/180 : UnitConvert.MmToCm(value);
        }
    }
}
#endif
