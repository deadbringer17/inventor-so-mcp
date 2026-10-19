#if INVENTOR2027 && SO_EXPERIMENTAL
using System;
using System.Collections.Generic;
using System.Linq;
using Bimwright.Ipt.Shared.Handlers.Core;
using Bimwright.Ipt.Shared.Infrastructure;
using Inventor;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Handlers.Experimental;

internal static class NativeSketchTransforms
{
    internal static void Apply(InventorCommandContext ctx,Application app,global::Inventor.Document document,PlanarSketch sketch,
        string sketchId,string kind,JObject options)
    {
        bool rotate=kind=="rotate",stretch=kind=="stretch";
        if(!rotate && !stretch && kind!="move" && kind!="copy") throw new ArgumentException("Unknown native sketch transform.");
        string[] allowed=rotate ? new[] {"references","center_mm","angle_deg","copy","remove_constraints"}
            : new[] {"references","delta_mm","remove_constraints"};
        if(options.Properties().Any(p=>!allowed.Contains(p.Name,StringComparer.Ordinal))) throw new ArgumentException("Unknown native transform option.");
        foreach(string name in new[] {"copy","remove_constraints"})
            if(options.TryGetValue(name,out var value) && value.Type!=JTokenType.Boolean) throw new ArgumentException("Transform options must be explicit booleans.");
        bool copy=kind=="copy" || rotate && X.Bool(options,"copy",false);
        // A direct native copy oracle terminated Inventor. Refuse before touching
        // COM until the failing input has been isolated and copying revalidated.
        if(copy) throw new ArgumentException("Native sketch copying is temporarily unavailable pending isolation of an Inventor failure.");
        if(options["references"] is not JArray refs || refs.Count<1 || refs.Count>2000 || refs.Any(r=>r is not JObject))
            throw new ArgumentException("Select 1–2000 current native sketch objects or stretch subparts.");
        string documentId=EntityReferences.DocumentId(document),revision=ctx.Events?.Revision(documentId) ?? "";
        var seen=new HashSet<string>(StringComparer.Ordinal);var objects=app.TransientObjects.CreateObjectCollection();
        var included=new HashSet<string>(StringComparer.Ordinal);
        void Include(object native,string id)
        {
            if(!included.Add(id)) return;
            if(included.Count>2000) throw new ArgumentException("Whole sketch objects and their defining points exceed the native transform budget (2000).");
            objects.Add(native);
        }
        foreach(JObject reference in refs)
        {
            X.Deadline(ctx,"resolving native sketch transform objects");
            if(X.Str(reference,"document_id")!=documentId || X.Str(reference,"sketch_id")!=sketchId)
                throw new ArgumentException("Transform selection belongs to another document or sketch.");
            if(X.Str(reference,"revision")!=revision) throw ConcurrencyFailure.StaleRevision((string?)reference["revision"],revision);
            string type=X.Str(reference,"type"),id=X.Str(reference,"entity_id"),subpart=X.Str(reference,"subpart");
            bool whole=subpart=="Entity" || type=="point" && subpart=="Point";
            if(!seen.Add(stretch ? id+"|"+subpart : id) || !stretch && !whole)
                throw new ArgumentException("Select each native object or available stretch subpart once.");
            var native=SketchReferences.Resolve(document,id,type=="text" ? "sketch_text" : type=="image" ? "sketch_image" : "sketch_entity");
            object owner;string actualType;
            switch(native)
            {
                case SketchEntity entity: owner=entity.Parent;actualType=SketchEditorXrHandler.Kind(entity);break;
                case TextBox text: owner=text.Parent;actualType="text";break;
                case SketchImage image: owner=image.Parent;actualType="image";break;
                default:throw new ArgumentException("Select native sketch geometry, text or an image.");
            }
            if(type!=actualType || !SketchReferences.SameComObject(owner,sketch)) throw new ArgumentException("Transform selection has the wrong native type or owner.");
            if(stretch && (native is not SketchEntity || !new[] {"line","circle","arc","ellipse","elliptical_arc","spline","control_point_spline","offset_spline","point"}.Contains(type,StringComparer.Ordinal)))
                throw new ArgumentException("Stretch requires native sketch curves or points.");
            if(stretch && !whole)
            {
                var selectedPoint=NativeSketchConstraints.Anchors((SketchEntity)native).FirstOrDefault(a=>a.subpart==subpart).point;
                if(selectedPoint==null) throw new ArgumentException("This native sketch subpart is unavailable for stretching.");
                NativeSketchImageStructure.RefuseMutation((SketchEntity)selectedPoint);
                Include(selectedPoint,SketchReferences.Describe(document,selectedPoint,"sketch_entity"));
                continue;
            }
            if(rotate && native is TextBox) throw new ArgumentException("Native sketch text rotation is temporarily unavailable pending validation of glyph orientation.");
            if(copy || actualType!="control_point_spline") Include(native,id);
            // Curve-only transforms can move a line normally or deform an arc.
            // Transform its defining native points together with the whole curve.
            if(!copy) foreach(var definingPoint in DefiningPoints(native))
                {
                    NativeSketchImageStructure.RefuseMutation((SketchEntity)definingPoint);
                    Include(definingPoint,SketchReferences.Describe(document,definingPoint,"sketch_entity"));
                }
        }
        bool remove=X.Bool(options,"remove_constraints",false);
        var point=NativeSketchDimensions.Point(options[rotate ? "center_mm" : "delta_mm"],app.TransientGeometry);
        if(rotate)
            sketch.RotateSketchObjects(objects,point,UnitConvert.DegToRad(X.Num(options,"angle_deg")),copy,remove);
        else
            sketch.MoveSketchObjects(objects,app.TransientGeometry.CreateVector2d(point.X,point.Y),kind=="copy",remove);
    }
    private static IEnumerable<SketchPoint> DefiningPoints(object native)
    {
        switch(native)
        {
            case SketchLine line: yield return line.StartSketchPoint;yield return line.EndSketchPoint;break;
            case SketchArc arc: yield return arc.CenterSketchPoint;yield return arc.StartSketchPoint;yield return arc.EndSketchPoint;break;
            case SketchCircle circle: yield return circle.CenterSketchPoint;break;
            case SketchEllipse ellipse: yield return ellipse.CenterSketchPoint;break;
            case SketchEllipticalArc arc: yield return arc.CenterSketchPoint;yield return arc.StartSketchPoint;yield return arc.EndSketchPoint;break;
            // Move the native control polygon directly: moving the spline object
            // itself can displace its endpoints twice and deform the curve.
            case SketchControlPointSpline spline: for(int i=1;i<=spline.ControlPointCount;i++) yield return spline.get_ControlPoint(i);break;
            case SketchSpline spline: for(int i=1;i<=spline.FitPointCount;i++) yield return spline.get_FitPoint(i);break;
            case TextBox text: yield return text.OriginSketchPoint;break;
        }
    }
}
#endif
