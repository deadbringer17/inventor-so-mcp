#if INVENTOR2027 && SO_EXPERIMENTAL
using System;
using System.Linq;
using Bimwright.Ipt.Shared.Handlers.Core;
using Bimwright.Ipt.Shared.Infrastructure;
using Inventor;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Handlers.Experimental;

internal static class NativeSketchTangentArcs
{
    internal static void Apply(InventorCommandContext ctx,Application app,global::Inventor.Document document,
        PlanarSketch sketch,string sketchId,JObject options)
    {
        if(options.Properties().Any(p=>p.Name!="references"&&p.Name!="points_mm")
            ||options["references"] is not JArray references||references.Count<1||references.Count>2
            ||references[0] is not JObject reference
            ||options["points_mm"] is not JArray points||points.Count!=1||points[0] is not JArray end||end.Count!=2
            ||end.Any(v=>v.Type is not (JTokenType.Integer or JTokenType.Float)))
            throw new ArgumentException("Tangent arc requires one source endpoint, one target point and an optional target anchor.");
        string subpart=X.Str(reference,"subpart");
        if(subpart!="Start"&&subpart!="End") throw new ArgumentException("Select a curve endpoint for the tangent arc.");
        var selections=NativeSketchConstraints.ResolveSelections(ctx,document,sketch,sketchId,references);
        if(selections[0] is not SketchPoint start || (selections.Length==2&&selections[1] is not SketchPoint))
            throw new ArgumentException("Tangent arc anchors must be native sketch points.");
        var source=SketchReferences.Resolve(document,X.Str(reference,"entity_id"),"sketch_entity") as SketchEntity
            ??throw new ArgumentException("The tangent source curve is unavailable.");
        NativeSketchImageStructure.RefuseMutation(source);
        var origin=start.Geometry;
        double x=UnitConvert.CmToMm(origin.X),y=UnitConvert.CmToMm(origin.Y),ex=(double)end[0],ey=(double)end[1];
        if(double.IsNaN(ex)||double.IsInfinity(ex)||double.IsNaN(ey)||double.IsInfinity(ey))
            throw new ArgumentException("The tangent arc target must be finite.");
        object target=app.TransientGeometry.CreatePoint2d(UnitConvert.MmToCm(ex),UnitConvert.MmToCm(ey));
        if(selections.Length==2)
        {
            var anchor=(SketchPoint)selections[1];var point=anchor.Geometry;
            if(!Near(UnitConvert.CmToMm(point.X),UnitConvert.CmToMm(point.Y),ex,ey))
                throw new ArgumentException("The target coordinates differ from the selected native anchor.");
            target=anchor;
        }
        X.Deadline(ctx,"reading the source endpoint tangent");
        dynamic geometry=((dynamic)source).Geometry;
        Curve2dEvaluator evaluator=(Curve2dEvaluator)geometry.Evaluator;
        double min=0,max=0;evaluator.GetParamExtents(out min,out max);
        double[] parameters={min,max},coordinates=Array.Empty<double>();
        evaluator.GetPointAtParam(ref parameters,ref coordinates);
        if(parameters.Length!=2||coordinates.Length!=4||coordinates.Any(v=>double.IsNaN(v)||double.IsInfinity(v)))
            throw new ArgumentException("Native curve endpoint evaluation is unavailable.");
        bool first=Near(x,y,UnitConvert.CmToMm(coordinates[0]),UnitConvert.CmToMm(coordinates[1]));
        bool last=Near(x,y,UnitConvert.CmToMm(coordinates[2]),UnitConvert.CmToMm(coordinates[3]));
        if(first==last) throw new ArgumentException("The selected curve has no unambiguous open endpoint.");
        parameters=new[]{first?min:max};double[] tangent=Array.Empty<double>();
        evaluator.GetTangent(ref parameters,ref tangent);
        if(tangent.Length!=2) throw new ArgumentException("Native endpoint tangent is unavailable.");
        double direction=first?-1:1;
        var circle=TangentArcGeometry.Create(x,y,direction*tangent[0],direction*tangent[1],ex,ey);
        X.Deadline(ctx,"creating the associated native tangent arc");
        var center=app.TransientGeometry.CreatePoint2d(UnitConvert.MmToCm(circle.CenterX),UnitConvert.MmToCm(circle.CenterY));
        string nativeStage="prepare_spline_handle";
        try
        {
            SketchEntity tangentSource=source;
            if(source is SketchSpline spline)
            {
                if(!spline.GetHandleStatus(start)) spline.SetHandleStatus(start,true);
                var handle=spline.GetHandle(start);
                nativeStage="verify_spline_handle_owner";
                if(handle==null || !SketchReferences.SameComObject(handle.Spline,source)
                    || !SketchReferences.SameComObject(handle.FitPoint,start) || !SketchReferences.SameComObject(handle.Parent,sketch))
                    throw new CodedFailureException(Bimwright.Ipt.Shared.Contracts.InventorErrorCodes.API_ERROR,
                        "Native tangent handle ownership could not be verified.",new JObject{["native_stage"]=nativeStage});
                tangentSource=(SketchEntity)handle;
            }
            nativeStage="create_shared_arc";
            var arc=sketch.SketchArcs.AddByCenterStartEndPoint(center,start,target,circle.CounterClockwise);
            if(!Shares(arc,start)||(selections.Length==2&&!Shares(arc,(SketchPoint)selections[1])))
                throw new InvalidOperationException("Native tangent arc did not retain its shared endpoint anchors.");
            // Explicitly choose the source endpoint when both points are shared.
            nativeStage="add_tangent";
            sketch.GeometricConstraints.AddTangent(tangentSource,(SketchEntity)arc,origin.Copy());
            if(source.ConstraintStatus==ConstraintStatusEnum.kOverConstrainedConstraintStatus)
                throw new ArgumentException("Native tangent association overconstrains the source curve.");
        }
        catch(System.Runtime.InteropServices.COMException error)
        {
            throw new CodedFailureException(Bimwright.Ipt.Shared.Contracts.InventorErrorCodes.API_ERROR,
                "Inventor rejected the native tangent association.",new JObject{["native_stage"]=nativeStage,["native_hresult"]=error.HResult});
        }
        X.Deadline(ctx,"after native tangent association");
    }
    private static bool Shares(SketchArc arc,SketchPoint point)=>SketchReferences.SameComObject(arc.StartSketchPoint,point)
        ||SketchReferences.SameComObject(arc.EndSketchPoint,point);
    private static bool Near(double x,double y,double a,double b)=>Math.Abs(x-a)<=1e-5&&Math.Abs(y-b)<=1e-5;
}
#endif
