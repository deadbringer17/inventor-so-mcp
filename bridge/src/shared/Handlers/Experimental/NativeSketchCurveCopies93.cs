#if INVENTOR2027 && SO_EXPERIMENTAL
using System;
using System.Collections.Generic;
using System.Linq;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;
using Inventor;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Handlers.Experimental;

// Free default circle/arc foundation from92, not complete S1 Copy. Uncapturable
// contexts, other curves/formats/constraints, source updates, persistence and XR
// remain mandatory backlog. No private transaction, repair or tentative IDs.
internal static class NativeSketchCurveCopies93
{
    internal sealed class Frozen
    {
        internal SketchEntity Source=null!;
        internal JObject Before=null!;
        internal NativeSketchCopyPolicy.Request Request=null!;
        internal InventorCommandContext Context=null!;
    }
    private static string Key(object value)
    { byte[] key=Array.Empty<byte>();((dynamic)value).GetReferenceKey(ref key,0);if(key.Length is <1 or >65536)throw new ArgumentException("Native curve-copy key unavailable.");return Convert.ToBase64String(key); }
    internal static Frozen Prepare(InventorCommandContext ctx,PlanarSketch sketch,object value,string kind,NativeSketchCopyPolicy.Request request)
    {
        if(value is not SketchEntity source||kind=="circle"&&source is not SketchCircle||kind=="arc"&&source is not SketchArc
            ||kind!="circle"&&kind!="arc"||!SketchReferences.SameComObject(source.Parent,sketch)) throw new ArgumentException("Curve copy native type or sketch owner differs.");
        X.Deadline(ctx,"preflighting native curve ownership");
        NativeSketchImageStructure.RefuseMutation(source);
        var points=Points(source);bool shared=false;
        foreach(var point in points.Values)
        {
            NativeSketchImageStructure.RefuseMutation((SketchEntity)point);
            if(!SketchReferences.SameComObject(point.Parent,sketch)||point.Reference||point.HasReferenceComponent||point.AttributeSets.Count!=0||point.SketchBlockPath.Count!=0)
                throw new ArgumentException("Projected/annotated/block curve points are not supported.");
            var owners=point.OwnedBy;
            if(owners!=null)foreach(SketchEntity owner in owners) if(!SketchReferences.SameComObject(owner,source))shared=true;
            foreach(TextBox text in sketch.TextBoxes)if(SketchReferences.SameComObject(text.OriginSketchPoint,point))shared=true;
            foreach(SketchEntity other in sketch.SketchEntities)
            {
                X.Deadline(ctx,"checking genuine shared native points");
                if(SketchReferences.SameComObject(other,source)||other is SketchPoint)continue;
                foreach(var candidate in NeighborPoints(other))if(SketchReferences.SameComObject(candidate,point))shared=true;
            }
        }
        var state=State(source,ctx);var format=(JObject)state["format"]!;
        NativeSketchCurveCopyPolicy93.ValidateFormat(source.Reference,source.Construction,source.HasReferenceComponent||(source.OwnedBy?.Count??0)!=0,shared,
            format["layer"]!.Type==JTokenType.Null,format["override"]!.Type==JTokenType.Null,(int)format["attributes"]!,(int)format["blocks"]!,
            (int)format["line_type"]!,(double)format["weight"]!,(double)format["scale"]!,(int)format["definition"]!);
        var center=points["center"].Geometry;
        NativeSketchCurveCopyPolicy93.ValidateGeometry(kind,center.X*10,center.Y*10,(double)state["radius"]!,
            (double?)state["start_angle"]??0,(double?)state["sweep_angle"]??(2*Math.PI),points.Values.Select(p=>(p.Geometry.X*10,p.Geometry.Y*10)),request);
        return new Frozen {Source=source,Before=state,Request=request,Context=ctx};
    }
    internal static void OriginalUnchanged(Frozen frozen)
    { if(!JToken.DeepEquals(frozen.Before,State(frozen.Source,frozen.Context)))throw new ArgumentException("Native curve copying changed an original geometry, format, point or relation."); }
    internal static void Create(InventorCommandContext ctx,Application app,PlanarSketch sketch,Frozen frozen,HashSet<string> oldPoints)
    {
        RequireOwned(ctx,app);
        var oldEntities=new HashSet<string>(sketch.SketchEntities.Cast<SketchEntity>().Select(Key).Concat(sketch.TextBoxes.Cast<TextBox>().Select(Key)),StringComparer.Ordinal);
        var oldRelations=new HashSet<string>(sketch.GeometricConstraints.Cast<GeometricConstraint>().Select(Key),StringComparer.Ordinal);
        var objects=app.TransientObjects.CreateObjectCollection();objects.Add(frozen.Source);
        // A single verified whole native curve only. TextBox is NEVER sent here.
        sketch.MoveSketchObjects(objects,app.TransientGeometry.CreateVector2d(frozen.Request.DeltaXmm/10,frozen.Request.DeltaYmm/10),true,false);
        RequireOwned(ctx,app);
        var newEntities=sketch.SketchEntities.Cast<SketchEntity>().Where(e=>!oldEntities.Contains(Key(e))).ToArray();
        var curves=newEntities.Where(e=>e is not SketchPoint).ToArray();
        if(curves.Length!=1||curves[0].Type!=frozen.Source.Type||!SketchReferences.SameComObject(curves[0].Parent,sketch))throw new ArgumentException("Native curve copy inventory/type/owner differs.");
        var copy=curves[0];var actual=State(copy,ctx);var expected=frozen.Before;
        if((copy.OwnedBy?.Count??0)!=0)throw new ArgumentException("Native copied curve has a structural owner.");
        var originalPoints=Points(frozen.Source);var copiedPoints=Points(copy);
        var pointMap=new Dictionary<string,string>(StringComparer.Ordinal) { [Key(frozen.Source)]=Key(copy) };
        var newPoints=new HashSet<string>(StringComparer.Ordinal);
        foreach(var point in copiedPoints)
        {
            string key=Key(point.Value);
            if(oldPoints.Contains(key)||!newPoints.Add(key)||!SketchReferences.SameComObject(point.Value.Parent,sketch)) throw new ArgumentException("Native copy reused original/prior-copy/aliased structural point.");
            pointMap.Add(Key(originalPoints[point.Key]),key);
            var p=originalPoints[point.Key].Geometry;var q=point.Value.Geometry;
            var target=NativeSketchCopyPolicy.Translate(p.X*10,p.Y*10,frozen.Request);
            if(!NativeSketchCopyPolicy.CopiedPointMatches(q.X*10,q.Y*10,target.X,target.Y)) throw new ArgumentException("Native copied center/endpoint is not the frozen translation.");
            if(point.Value.Reference||point.Value.AttributeSets.Count!=0||point.Value.SketchBlockPath.Count!=0
                ||(point.Value.OwnedBy!=null&&point.Value.OwnedBy.Cast<SketchEntity>().Any(o=>!SketchReferences.SameComObject(o,copy))))
                throw new ArgumentException("Copied point has unsupported structural ownership or attributes.");
            foreach(string flag in new[]{"reference","has_reference_component","construction","hole_center","attributes","blocks"})
                if(!JToken.DeepEquals(expected["points"]![point.Key]![flag],actual["points"]![point.Key]![flag]))
                    throw new ArgumentException("Native copied structural point flags differ from the source role.");
        }
        if(newEntities.Any(e=>!SketchReferences.SameComObject(e,copy)&&!newPoints.Contains(Key(e))))throw new ArgumentException("Unexplained new native entity after curve copy.");
        foreach(string field in new[]{"native_type","reference","has_reference_component","construction","owned_by","format"})
            if(!JToken.DeepEquals(expected[field],actual[field]))throw new ArgumentException("Native curve copy format differs.");
        if(!NativeSketchCurveCopyPolicy93.RadiusMatches((double)actual["radius"]!,(double)expected["radius"]!))throw new ArgumentException("Native copied radius differs.");
        foreach(string field in new[]{"start_angle","sweep_angle","geometry_start","geometry_sweep"})
            if(expected[field]!=null&&!NativeSketchCurveCopyPolicy93.AngleMatches((double)actual[field]!,(double)expected[field]!))throw new ArgumentException("Native copied arc orientation/extent differs.");
        TranslateSamples((JArray)expected["strokes"]!,(JArray)actual["strokes"]!,frozen.Request);
        var oldGraph=(JArray)expected["relations"]!;var newGraph=(JArray)actual["relations"]!;
        var mapped=new JArray();
        foreach(JObject edge in oldGraph)
        {var translated=(JObject)edge.DeepClone();translated.Remove("key");translated["one"]=pointMap[(string)edge["one"]!];translated["two"]=pointMap[(string)edge["two"]!];mapped.Add(translated);}
        var remaining=mapped.OfType<JObject>().ToList();
        foreach(JObject edge in newGraph)
        {if(oldRelations.Contains((string)edge["key"]!))throw new ArgumentException("Copied graph reused original relation.");var stripped=(JObject)edge.DeepClone();stripped.Remove("key");int match=remaining.FindIndex(e=>JToken.DeepEquals(e,stripped));if(match<0)throw new ArgumentException("Native copied intrinsic graph differs.");remaining.RemoveAt(match);}
        if(remaining.Count!=0)throw new ArgumentException("Native copied graph omitted a source relation.");
        var graphKeys=new HashSet<string>(newGraph.Select(e=>(string)e["key"]!),StringComparer.Ordinal);
        if(sketch.GeometricConstraints.Cast<GeometricConstraint>().Any(c=>!oldRelations.Contains(Key(c))&&!graphKeys.Contains(Key(c))))throw new ArgumentException("Unexplained new relation after curve copy.");
        foreach(string key in newPoints)oldPoints.Add(key);
        OriginalUnchanged(frozen);
    }
    internal static void RequireOwned(InventorCommandContext ctx,Application app)
    {
        X.Deadline(ctx,"checking owned curve-copy transaction");
        if(ctx.OwnedBatchTransaction==null||app.TransactionManager.CurrentTransaction==null||!SketchReferences.SameComObject(ctx.OwnedBatchTransaction,app.TransactionManager.CurrentTransaction))
            throw new CodedFailureException(InventorErrorCodes.ATOMIC_REQUIRED,"Native copying requires the current owned batch transaction.");
    }
    private static Dictionary<string,SketchPoint> Points(SketchEntity curve)=>curve switch {
        SketchCircle c=>new Dictionary<string,SketchPoint>{{"center",c.CenterSketchPoint}},
        SketchArc a=>new Dictionary<string,SketchPoint>{{"center",a.CenterSketchPoint},{"start",a.StartSketchPoint},{"end",a.EndSketchPoint}},
        _=>throw new ArgumentException("Only native circle/arc points are supported by this helper.") };
    private static IEnumerable<SketchPoint> NeighborPoints(SketchEntity entity)
    {
        switch(entity)
        {
            case SketchLine l:return new[]{l.StartSketchPoint,l.EndSketchPoint};
            case SketchCircle c:return new[]{c.CenterSketchPoint};
            case SketchArc a:return new[]{a.CenterSketchPoint,a.StartSketchPoint,a.EndSketchPoint};
            case SketchEllipse e:return new[]{e.CenterSketchPoint};
            case SketchEllipticalArc e:return new[]{e.CenterSketchPoint,e.StartSketchPoint,e.EndSketchPoint};
            // These contexts are rejected by the context capture too. Refuse now,
            // before enumerating thousands of COM fit/control points needlessly.
            default:throw new ArgumentException("Uncapturable neighboring geometry prevents curve-point ownership proof.");
        }
    }
    internal static JObject State(SketchEntity source,InventorCommandContext ctx)
    {
        var points=Points(source);string kind=source is SketchCircle?"circle":"arc";
        var graph=new Dictionary<string,JObject>(StringComparer.Ordinal);var nativeGraph=new Dictionary<string,object>(StringComparer.Ordinal);
        var relations=new List<NativeSketchCurveCopyPolicy93.Relation>();
        // Same native global collection and typed member route exercised by92.
        foreach(GeometricConstraint relation in ((PlanarSketch)source.Parent).GeometricConstraints)
        {
            X.Deadline(ctx,"reading the native curve-copy relation graph");
            string key=Key(relation);if(nativeGraph.TryGetValue(key,out var seen)){if(!SketchReferences.SameComObject(seen,relation))throw new ArgumentException("Aliased native relation key.");continue;}nativeGraph.Add(key,relation);
            SketchEntity one,two;bool deletable,flag;string type;
            if(relation is ConcentricConstraint concentric){one=concentric.EntityOne;two=concentric.EntityTwo;deletable=concentric.Deletable;flag=concentric.CenterPointConstraint;type="concentric";}
            else if(relation is CoincidentConstraint coincidence){one=coincidence.EntityOne;two=coincidence.EntityTwo;deletable=coincidence.Deletable;flag=coincidence.StartOrEndPointConstraint;type="coincident";}
            else throw new ArgumentException("Curve/point relation is outside the free native intrinsic profile.");
            bool Touches(object member)=>SketchReferences.SameComObject(member,source)||points.Values.Any(p=>SketchReferences.SameComObject(member,p));
            if(!Touches(one)&&!Touches(two))continue;
            bool ownOne=SketchReferences.SameComObject(one,source),ownTwo=SketchReferences.SameComObject(two,source);
            object other=ownOne?two:one;
            bool realRole=type=="concentric"?SketchReferences.SameComObject(other,points["center"]):kind=="arc"&&(SketchReferences.SameComObject(other,points["start"])||SketchReferences.SameComObject(other,points["end"]));
            if(ownOne==ownTwo||!realRole)throw new ArgumentException("Relation does not bind the genuine native curve and its correct own point role.");
            var edge=new NativeSketchCurveCopyPolicy93.Relation(type,deletable,flag,Key(one),Key(two));relations.Add(edge);
            graph.Add(key,new JObject{["key"]=key,["kind"]=type,["deletable"]=deletable,["flag"]=flag,["one"]=edge.One,["two"]=edge.Two});
        }
        NativeSketchCurveCopyPolicy93.ValidateIntrinsicGraph(kind,Key(source),Key(points["center"]),kind=="arc"?Key(points["start"]):null,kind=="arc"?Key(points["end"]):null,relations);
        Curve2dEvaluator evaluator=source is SketchCircle circle?circle.Geometry.Evaluator:((SketchArc)source).Geometry.Evaluator;
        double min=0,max=0;int count=0;double[] coordinates=Array.Empty<double>();
        evaluator.GetParamExtents(out min,out max);
        if(!Finite(min)||!Finite(max)||max<=min)throw new ArgumentException("Native curve evaluator interval is invalid.");
        evaluator.GetStrokes(min,max,0.0001,out count,out coordinates);
        if(count is <2 or >8192||coordinates.Length!=2*count||coordinates.Any(v=>!Finite(v)))throw new ArgumentException("Native curve strokes are unavailable or nonfinite.");
        var state=new JObject { ["key"]=Key(source),["native_type"]=source.Type.ToString(),["reference"]=source.Reference,["construction"]=source.Construction,
            ["has_reference_component"]=source.HasReferenceComponent,["owned_by"]=OwnerKeys(source),["format"]=Format(source),["points"]=new JObject(points.Select(p=>new JProperty(p.Key,PointState(p.Value)))),
            ["relations"]=new JArray(graph.OrderBy(p=>p.Key,StringComparer.Ordinal).Select(p=>p.Value)),["strokes"]=new JArray(coordinates.Select(v=>v*10)) };
        if(source is SketchCircle c)state["radius"]=c.Radius*10;
        else {var a=(SketchArc)source;state["radius"]=a.Radius*10;state["start_angle"]=a.StartAngle;state["sweep_angle"]=a.SweepAngle;state["geometry_start"]=a.Geometry.StartAngle;state["geometry_sweep"]=a.Geometry.SweepAngle;
            if(!Finite(a.Geometry.StartAngle)||!Finite(a.Geometry.SweepAngle))throw new ArgumentException("Native arc evaluator angles are nonfinite.");}
        return state;
    }
    private static JObject PointState(SketchPoint p)=>new JObject { ["key"]=Key(p),["xy"]=new JArray(p.Geometry.X*10,p.Geometry.Y*10),["reference"]=p.Reference,["has_reference_component"]=p.HasReferenceComponent,["construction"]=p.Construction,["hole_center"]=p.HoleCenter,["attributes"]=p.AttributeSets.Count,["blocks"]=p.SketchBlockPath.Count };
    private static JArray OwnerKeys(SketchEntity entity)=>new JArray(entity.OwnedBy==null?Array.Empty<string>():entity.OwnedBy.Cast<SketchEntity>().Select(Key).OrderBy(k=>k,StringComparer.Ordinal));
    internal static JObject ContextPointState(SketchPoint point)
    {
        if(!Finite(point.Geometry.X*10)||!Finite(point.Geometry.Y*10))throw new ArgumentException("Curve-copy context point is nonfinite.");
        return new JObject {
        ["point"]=PointState(point),["owned_by"]=OwnerKeys((SketchEntity)point),
        ["relations"]=new JArray(point.Constraints.Cast<object>().Select(Key).OrderBy(k=>k,StringComparer.Ordinal)) };
    }
    internal static JObject ContextLineFlags(SketchLine line)=>new JObject {
        ["reference"]=line.Reference,["owned_by"]=OwnerKeys((SketchEntity)line),["format"]=Format((SketchEntity)line) };
    internal static JObject Format(SketchEntity source)
    {
        Color? color;int type,definition;double weight,scale;
        if(source is SketchCircle c){color=c.OverrideColor;type=(int)c.LineType;definition=(int)c.LineDefinitionSpace;weight=c.LineWeight;scale=c.LineScale;}
        else if(source is SketchArc a){color=a.OverrideColor;type=(int)a.LineType;definition=(int)a.LineDefinitionSpace;weight=a.LineWeight;scale=a.LineScale;}
        else {var l=(SketchLine)source;color=l.OverrideColor;type=(int)l.LineType;definition=(int)l.LineDefinitionSpace;weight=l.LineWeight;scale=l.LineScale;}
        return new JObject { ["line_type"]=type,["definition"]=definition,["weight"]=weight,["scale"]=scale,["attributes"]=source.AttributeSets.Count,
            ["blocks"]=source.SketchBlockPath.Count,["layer"]=source.Layer==null?JValue.CreateNull():new JValue(source.Layer.InternalName),
            ["override"]=color==null?JValue.CreateNull():ColorState(color) };
    }
    private static JObject ColorState(Color c)=>new JObject { ["r"]=(int)c.Red,["g"]=(int)c.Green,["b"]=(int)c.Blue,["opacity"]=c.Opacity,["source"]=(int)c.ColorSourceType };
    private static bool Finite(double v)=>!double.IsNaN(v)&&!double.IsInfinity(v);
    private static void TranslateSamples(JArray source,JArray copy,NativeSketchCopyPolicy.Request request)
    {
        if(source.Count!=copy.Count||source.Count%2!=0)throw new ArgumentException("Native copied curve stroke shape differs.");
        for(int i=0;i<source.Count;i+=2){var expected=NativeSketchCopyPolicy.Translate((double)source[i],(double)source[i+1],request);if(!NativeSketchCopyPolicy.CopiedPointMatches((double)copy[i],(double)copy[i+1],expected.X,expected.Y))throw new ArgumentException("Copied curve locus/orientation differs from frozen translation.");}
    }
}
#endif
