#if INVENTOR2027 && SO_EXPERIMENTAL
using System;
using System.Linq;
using Bimwright.Ipt.Shared.Handlers.Core;
using Bimwright.Ipt.Shared.Infrastructure;
using Inventor;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Handlers.Experimental;

internal static class NativeSketchConstraints
{
    internal static SketchEntity[]? Entities(GeometricConstraint constraint)=>constraint switch
    {
        CoincidentConstraint c=>new[] {c.EntityOne,c.EntityTwo},
        MidpointConstraint c=>new SketchEntity[] {(SketchEntity)c.Point,(SketchEntity)c.Line},
        GroundConstraint c=>new[] {c.Entity}, HorizontalConstraint c=>new[] {c.Entity}, VerticalConstraint c=>new[] {c.Entity},
        ParallelConstraint c=>new[] {c.EntityOne,c.EntityTwo}, CollinearConstraint c=>new[] {c.EntityOne,c.EntityTwo},
        PerpendicularConstraint c=>new[] {c.EntityOne,c.EntityTwo}, ConcentricConstraint c=>new[] {c.EntityOne,c.EntityTwo},
        TangentSketchConstraint c=>new[] {c.EntityOne,c.EntityTwo}, SmoothConstraint c=>new[] {c.EntityOne,c.EntityTwo},
        EqualLengthConstraint c=>new SketchEntity[] {(SketchEntity)c.LineOne,(SketchEntity)c.LineTwo}, EqualRadiusConstraint c=>new[] {c.EntityOne,c.EntityTwo},
        HorizontalAlignConstraint c=>new SketchEntity[] {(SketchEntity)c.PointOne,(SketchEntity)c.PointTwo}, VerticalAlignConstraint c=>new SketchEntity[] {(SketchEntity)c.PointOne,(SketchEntity)c.PointTwo},
        SymmetryConstraint c=>new SketchEntity[] {c.EntityOne,c.EntityTwo,(SketchEntity)c.SymmetryLine}, _=>null
    };
    internal static (SketchPoint point,string subpart)[] Anchors(SketchEntity entity)=>entity switch
    {
        SketchPoint p=>new[] {(p,"Point")},
        SketchLine e=>new[] {(e.StartSketchPoint,"Start"),(e.EndSketchPoint,"End")},
        SketchCircle e=>new[] {(e.CenterSketchPoint,"Center")},
        SketchArc e=>new[] {(e.StartSketchPoint,"Start"),(e.EndSketchPoint,"End"),(e.CenterSketchPoint,"Center")},
        SketchEllipse e=>new[] {(e.CenterSketchPoint,"Center")},
        SketchEllipticalArc e=>new[] {(e.StartSketchPoint,"Start"),(e.EndSketchPoint,"End")},
        SketchSpline e=>new[] {(e.StartSketchPoint,"Start"),(e.EndSketchPoint,"End")}.Where(p=>p.Item1!=null).ToArray(),
        SketchControlPointSpline e=>new[] {(e.StartSketchPoint,"Start"),(e.EndSketchPoint,"End")}.Where(p=>p.Item1!=null).ToArray(),
        _=>Array.Empty<(SketchPoint,string)>()
    };
    internal static void Delete(InventorCommandContext ctx,global::Inventor.Document document,PlanarSketch sketch,string sketchId,JObject options)
    {
        if(options.Properties().Any(p=>p.Name!="references") || options["references"] is not JArray refs || refs.Count!=1 || refs[0] is not JObject reference)
            throw new ArgumentException("Constraint deletion requires exactly one whole native constraint reference.");
        string documentId=EntityReferences.DocumentId(document),revision=ctx.Events?.Revision(documentId) ?? "";
        if(X.Str(reference,"document_id")!=documentId || X.Str(reference,"sketch_id")!=sketchId)
            throw new ArgumentException("Constraint belongs to another document or sketch.");
        if(X.Str(reference,"revision")!=revision) throw ConcurrencyFailure.StaleRevision((string?)reference["revision"],revision);
        if(X.Str(reference,"type")!="constraint" || X.Str(reference,"subpart")!="Entity")
            throw new ArgumentException("Select the whole native geometric constraint.");
        var constraint=SketchReferences.Resolve(document,X.Str(reference,"entity_id"),"sketch_constraint") as GeometricConstraint
            ?? throw new ArgumentException("Native constraint reference is unavailable.");
        if(!SketchReferences.SameComObject(constraint.Parent,sketch)) throw new ArgumentException("Constraint owner does not match the selected sketch.");
        if(!constraint.Deletable) throw new ArgumentException("Inventor does not permit deleting this constraint.");
        X.Deadline(ctx,"deleting the selected native geometric constraint");constraint.Delete();
    }

    internal static SketchEntity[] ResolveSelections(InventorCommandContext ctx,global::Inventor.Document document,PlanarSketch sketch,
        string sketchId,JToken? references)
    {
        if(references is not JArray refs || refs.Count<1 || refs.Count>3 || refs.Any(r=>r is not JObject))
            throw new ArgumentException("Operation requires 1–3 ordered native references.");
        string documentId=EntityReferences.DocumentId(document),revision=ctx.Events?.Revision(documentId) ?? "";
        var entities=new System.Collections.Generic.List<SketchEntity>();
        foreach(JObject reference in refs)
        {
            X.Deadline(ctx,"resolving the native constraint selections");
            if(X.Str(reference,"document_id")!=documentId || X.Str(reference,"sketch_id")!=sketchId)
                throw new ArgumentException("Constraint reference belongs to another document or sketch.");
            if(X.Str(reference,"revision")!=revision) throw ConcurrencyFailure.StaleRevision((string?)reference["revision"],revision);
            var entity=SketchReferences.Resolve(document,X.Str(reference,"entity_id"),"sketch_entity") as SketchEntity
                ?? throw new ArgumentException("Select native sketch geometry.");
            if(X.Str(reference,"type")!=SketchEditorXrHandler.Kind(entity) || !SketchReferences.SameComObject(entity.Parent,sketch))
                throw new ArgumentException("Constraint reference does not match its native type or owner.");
            NativeSketchSplineHandleStructure.RefuseMutation(entity);
            object? nativeSelected=X.Str(reference,"subpart") switch
            {
                "Entity"=>entity,
                "Point" when entity is SketchPoint=>entity,
                "Start"=>entity switch {SketchLine e=>e.StartSketchPoint,SketchArc e=>e.StartSketchPoint,SketchEllipticalArc e=>e.StartSketchPoint,SketchSpline e=>e.StartSketchPoint,SketchControlPointSpline e=>e.StartSketchPoint,_=>null!},
                "End"=>entity switch {SketchLine e=>e.EndSketchPoint,SketchArc e=>e.EndSketchPoint,SketchEllipticalArc e=>e.EndSketchPoint,SketchSpline e=>e.EndSketchPoint,SketchControlPointSpline e=>e.EndSketchPoint,_=>null!},
                "Center"=>entity switch {SketchCircle e=>e.CenterSketchPoint,SketchArc e=>e.CenterSketchPoint,SketchEllipse e=>e.CenterSketchPoint,_=>null!},
                _=>throw new ArgumentException("Unknown native selection subpart.")
            };
            var selected=nativeSelected as SketchEntity;
            if(selected==null || entities.Any(e=>SketchReferences.SameComObject(e,selected)))
                throw new ArgumentException("Select distinct, available native entities or subparts.");
            NativeSketchImageStructure.RefuseMutation(selected);
            entities.Add(selected);
        }
        return entities.ToArray();
    }

    internal static void Add(InventorCommandContext ctx,global::Inventor.Document document,PlanarSketch sketch,
        string sketchId,string kind,JObject options,TransientGeometry geometry)
    {
        var selectedEntities=ResolveSelections(ctx,document,sketch,sketchId,options["references"]); var c=sketch.GeometricConstraints;
        void Count(int count) { if(selectedEntities.Length!=count) throw new ArgumentException("Wrong number of constraint selections."); }
        SketchEntity One()=>selectedEntities[0]; SketchEntity Two()=>selectedEntities[1];
        foreach(string name in new[] {"ellipse_one_major_axis","ellipse_two_major_axis"})
            if(options.TryGetValue(name,out var axisValue) && axisValue.Type!=JTokenType.Boolean) throw new ArgumentException("Ellipse axis choices must be explicit booleans.");
        bool axisOne=X.Bool(options,"ellipse_one_major_axis",true),axisTwo=X.Bool(options,"ellipse_two_major_axis",true);
        object proximity=Type.Missing;
        if(options.TryGetValue("proximity_mm",out var proximityToken))
        {
            if(proximityToken is not JArray p || p.Count!=2) throw new ArgumentException("Proximity requires two finite coordinates in mm.");
            proximity=geometry.CreatePoint2d(UnitConvert.MmToCm(X.Num(new JObject {["x"]=p[0]},"x")),UnitConvert.MmToCm(X.Num(new JObject {["y"]=p[1]},"y")));
        }
        switch(kind)
        {
            case "coincident":
                Count(2);
                if(One() is SketchPoint pointOne && Two() is SketchPoint pointTwo) pointOne.Merge(pointTwo);
                else { if(One() is not SketchPoint && Two() is not SketchPoint) throw new ArgumentException("Coincidence requires at least one native point."); c.AddCoincident(One(),Two()); }
                break;
            case "parallel": Count(2); c.AddParallel(One(),Two(),axisOne,axisTwo); break;
            case "collinear": Count(2); c.AddCollinear(One(),Two(),axisOne,axisTwo); break;
            case "perpendicular": Count(2); c.AddPerpendicular(One(),Two(),axisOne,axisTwo); break;
            case "concentric": Count(2); c.AddConcentric(One(),Two()); break;
            case "tangent": Count(2); c.AddTangent(One(),Two(),proximity); break;
            case "smooth": Count(2); c.AddSmooth(One(),Two(),proximity); break;
            case "equal": Count(2); if(One() is SketchLine a && Two() is SketchLine b) c.AddEqualLength(a,b); else c.AddEqualRadius(One(),Two()); break;
            case "ground": Count(1); c.AddGround(One()); break;
            case "horizontal":
                if(selectedEntities.Length==2 && One() is SketchPoint hp && Two() is SketchPoint hq) c.AddHorizontalAlign(hp,hq);
                else { Count(1); c.AddHorizontal(One(),axisOne); } break;
            case "vertical":
                if(selectedEntities.Length==2 && One() is SketchPoint vp && Two() is SketchPoint vq) c.AddVerticalAlign(vp,vq);
                else { Count(1); c.AddVertical(One(),axisOne); } break;
            case "symmetry": Count(3); if(selectedEntities[2] is not SketchLine axis) throw new ArgumentException("Select the symmetry axis last, as a whole line."); c.AddSymmetry(One(),Two(),axis); break;
            default: throw new ArgumentException("Unknown native geometric constraint.");
        }
    }
}
#endif
