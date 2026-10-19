#if INVENTOR2027 && SO_EXPERIMENTAL
using System;
using System.Collections.Generic;
using System.Linq;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Handlers.Core;
using Bimwright.Ipt.Shared.Infrastructure;
using Inventor;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Handlers.Experimental;

/// <summary>Undisclosed experimental copy foundation. Native tests65/72 motivate its primitives,
/// not general copy parity. Parameter fields, rotated/multiline/rectangular text, constrained
/// geometry, consumed profiles and other types remain mandatory backlog, explicitly refused.
/// Native TextBox Copy=true crashed Inventor70 and must never be called here.
/// https://help.autodesk.com/cloudhelp/2027/ENU/Inventor-API/files/TextBoxes_AddFitted.htm</summary>
internal static class NativeSketchCopies
{
    private sealed class Frozen
    {
        internal object Source = null!;
        internal JObject Before = null!;
        internal TextStyle? Style;
        internal NativeSketchCurveCopies93.Frozen? Curve;
        internal double X,Y,EndX,EndY;
    }

    internal static void Apply(InventorCommandContext ctx,Application app,global::Inventor.Document document,
        PlanarSketch sketch,string sketchId,JObject options)
    {
        if(ctx.ReadOnly) throw new CodedFailureException(InventorErrorCodes.READ_ONLY,"Native copying requires write permission.");
        if(ctx.OwnedBatchTransaction==null || app.TransactionManager.CurrentTransaction==null
            || !SketchReferences.SameComObject(ctx.OwnedBatchTransaction,app.TransactionManager.CurrentTransaction))
            throw new CodedFailureException(InventorErrorCodes.ATOMIC_REQUIRED,"Native copying requires the owned atomic batch transaction.");
        if(document is not PartDocument part || !SketchReferences.SameComObject(app.ActiveDocument,document)
            || !SketchReferences.SameComObject(sketch.Parent,part.ComponentDefinition)
            || !SketchReferences.SameComObject(SketchReferences.Resolve(document,sketchId,"sketch"),sketch))
            throw new ArgumentException("Copy requires the active owning part/lamiera native sketch.");
        var request=NativeSketchCopyPolicy.Parse(options);
        string documentId=EntityReferences.DocumentId(document), revision=ctx.Events?.Revision(documentId) ?? "";
        // Keep the shared structured stale refusal, before any native resolution or creator.
        foreach(var reference in request.References)
            if(reference.Revision!=revision) throw ConcurrencyFailure.StaleRevision(reference.Revision,revision);
        NativeSketchCopyPolicy.ValidateBinding(request,documentId,revision,sketchId);
        if(sketch.Consumed || sketch.Dependents.Count!=0)
            throw new ArgumentException("Copying consumed/dependent/block sketch profiles is not implemented.");
        long represented=(long)sketch.SketchEntities.Count+sketch.TextBoxes.Count+sketch.SketchImages.Count;
        long additions=request.References.Sum(r=>r.Type=="arc" ? 4L : r.Type=="line" ? 3L : 2L);
        if(represented+additions>NativeSketchCopyPolicy.MaxSelections)
            throw new ArgumentException("Copied native objects exceed the existing sketch representation budget.");
        var frozen=new List<Frozen>();
        // Freeze the whole selection, destinations, markup, style and every property
        // used below BEFORE the first creator. A later invalid member cannot copy an
        // earlier valid member. No setter, Solve, Update, Rebuild or style Copy here.
        foreach(var reference in request.References)
        {
            X.Deadline(ctx,"preflighting the complete native copy selection");
            object source=SketchReferences.Resolve(document,reference.EntityId,reference.Type=="text" ? "sketch_text" : "sketch_entity");
            if(frozen.Any(item=>SketchReferences.SameComObject(item.Source,source))) throw new ArgumentException("Copy aliases select the same native object twice.");
            var entry=new Frozen { Source=source };
            if(reference.Type=="text")
            {
                if(source is not TextBox text || !SketchReferences.SameComObject(text.Parent,sketch) || text.SketchBlockPath.Count!=0)
                    throw new ArgumentException("Copy text type or native owner differs from the selected sketch.");
                NativeSketchImageStructure.RefuseMutation((SketchEntity)text.OriginSketchPoint);
                if(text.OriginSketchPoint.Constraints.Count!=0 || text.OriginSketchPoint.Reference || (text.OriginSketchPoint.OwnedBy?.Count ?? 0)!=0
                    || sketch.GeometricConstraints.Cast<GeometricConstraint>().OfType<TextBoxConstraint>().Any(c=>SketchReferences.SameComObject(c.TextBox,text)))
                    throw new ArgumentException("Copying constrained or projected text origins is not implemented.");
                var style=text.Style;
                bool owned=part.TextStyles.Cast<TextStyle>().Any(candidate=>SketchReferences.SameComObject(candidate,style));
                entry.Before=TextState(text,style);
                bool valid=Enum.IsDefined(typeof(HorizontalTextAlignmentEnum),text.HorizontalJustification)
                    && Enum.IsDefined(typeof(VerticalTextAlignmentEnum),text.VerticalJustification)
                    && Enum.IsDefined(typeof(TextLineSpacingTypeEnum),text.LineSpacingType)
                    && Enum.IsDefined(typeof(ColorSourceTypeEnum),text.Color.ColorSourceType)
                    && Enum.IsDefined(typeof(HorizontalTextAlignmentEnum),style.HorizontalJustification)
                    && Enum.IsDefined(typeof(VerticalTextAlignmentEnum),style.VerticalJustification)
                    && Enum.IsDefined(typeof(TextLineSpacingTypeEnum),style.LineSpacingType)
                    && Enum.IsDefined(typeof(ColorSourceTypeEnum),style.Color.ColorSourceType);
                NativeSketchCopyPolicy.ValidateTextProfile(text.Fitted,text.SingleLineText,text.Rotation,text.WidthScale,text.LineSpacing,
                    UnitConvert.CmToMm(style.FontSize),owned && style.StyleLocation==StyleLocationEnum.kLocalStyleLocation,valid,text.UseBackgroundColor);
                string literal=NativeSketchCopyPolicy.ValidateLiteralMarkup((string)entry.Before["formatted_text"]!);
                if(literal!=(string)entry.Before["text"]!) throw new ArgumentException("Native literal text has unresolved or nonliteral content.");
                FiniteStyle(entry.Before["style"] as JObject ?? throw new ArgumentException("Native text style is unavailable."));
                CheckColor((JObject)entry.Before["color"]!); CheckColor((JObject)entry.Before["style"]!["color"]!);
                var destination=NativeSketchCopyPolicy.Translate(UnitConvert.CmToMm(text.Origin.X),UnitConvert.CmToMm(text.Origin.Y),request);
                entry.X=destination.X; entry.Y=destination.Y; entry.Style=style;
            }
            else if(reference.Type is "circle" or "arc")
            {
                entry.Curve=NativeSketchCurveCopies93.Prepare(ctx,sketch,source,reference.Type,request);
                entry.Before=entry.Curve.Before;
            }
            else
            {
                if(source is not SketchLine line || source is SketchSplineHandle || !SketchReferences.SameComObject(line.Parent,sketch)
                    || SketchEditorXrHandler.Kind((SketchEntity)line)!="line" || line.SketchBlockPath.Count!=0)
                    throw new ArgumentException("Copy line type or native owner differs from the selected sketch.");
                NativeSketchImageStructure.RefuseMutation((SketchEntity)line);
                NativeSketchImageStructure.RefuseMutation((SketchEntity)line.StartSketchPoint);
                NativeSketchImageStructure.RefuseMutation((SketchEntity)line.EndSketchPoint);
                bool shared=PointShared(sketch,line.StartSketchPoint,line) || PointShared(sketch,line.EndSketchPoint,line);
                NativeSketchCopyPolicy.ValidateLineProfile(line.Reference,line.Construction,line.Centerline,ExternalConstraints((SketchEntity)line,line),
                    ExternalConstraints((SketchEntity)line.StartSketchPoint,line),ExternalConstraints((SketchEntity)line.EndSketchPoint,line),(line.OwnedBy?.Count ?? 0)!=0,shared);
                entry.Before=LineState(line);
                var start=NativeSketchCopyPolicy.Translate(UnitConvert.CmToMm(line.StartSketchPoint.Geometry.X),UnitConvert.CmToMm(line.StartSketchPoint.Geometry.Y),request);
                var end=NativeSketchCopyPolicy.Translate(UnitConvert.CmToMm(line.EndSketchPoint.Geometry.X),UnitConvert.CmToMm(line.EndSketchPoint.Geometry.Y),request);
                if(start==end) throw new ArgumentException("Copy source line is degenerate.");
                entry.X=start.X; entry.Y=start.Y; entry.EndX=end.X; entry.EndY=end.Y;
            }
            frozen.Add(entry);
        }
        X.Deadline(ctx,"after preflighting all native copy objects");
        if(EntityReferences.DocumentId(document)!=documentId || ctx.Events?.Revision(documentId)!=revision)
            throw ConcurrencyFailure.DocumentChanged(documentId,EntityReferences.DocumentId(document),"native copy preflight");
        // Revalidate the frozen originals before creating anything. Readers may fail,
        // but never repair or change them to make the profile admissible.
        foreach(var entry in frozen) OriginalUnchanged(entry);
        // The conservative context profile applies ONLY to the new curve path.
        // Line/text-only requests retain the previously exercised88 behavior.
        var context=request.References.Any(r=>r.Type is "circle" or "arc") ? FreezeCurveContext(ctx,part,sketch) : null;
        context?.Invoke();
        var oldPointKeys=new HashSet<string>(sketch.SketchPoints.Cast<SketchPoint>().Select(Key),StringComparer.Ordinal);
        foreach(var entry in frozen)
        {
            X.Deadline(ctx,"creating the preflighted native copies");
            NativeSketchCurveCopies93.RequireOwned(ctx,app);
            if(entry.Curve!=null) NativeSketchCurveCopies93.Create(ctx,app,sketch,entry.Curve,oldPointKeys);
            else if(entry.Source is TextBox)
            {
                var before=entry.Before;
                var target=sketch.TextBoxes.AddFitted(app.TransientGeometry.CreatePoint2d(UnitConvert.MmToCm(entry.X),UnitConvert.MmToCm(entry.Y)),
                    (string)before["formatted_text"]!,entry.Style!);
                target.Rotation=(double)before["rotation"]!; target.WidthScale=(double)before["width_scale"]!;
                target.HorizontalJustification=(HorizontalTextAlignmentEnum)(int)before["horizontal"]!;
                target.VerticalJustification=(VerticalTextAlignmentEnum)(int)before["vertical"]!;
                target.SingleLineText=(bool)before["single_line"]!;
                target.LineSpacingType=(TextLineSpacingTypeEnum)(int)before["spacing_type"]!; target.LineSpacing=(double)before["spacing"]!;
                var color=(JObject)before["color"]!;
                var copiedColor=app.TransientObjects.CreateColor((byte)(int)color["r"]!,(byte)(int)color["g"]!,(byte)(int)color["b"]!,(double)color["opacity"]!);
                copiedColor.ColorSourceType=(ColorSourceTypeEnum)(int)color["source"]!; target.Color=copiedColor;
                var actual=TextState(target,target.Style);
                if(Key(target)==(string)before["key"]! || oldPointKeys.Contains(Key(target.OriginSketchPoint))
                    || entry.Style==null || !SketchReferences.SameComObject(target.Style,entry.Style)) throw new ArgumentException("Native text copy did not create distinct identities with its owned style.");
                CompareCopiedText(before,actual,entry.X,entry.Y);
                oldPointKeys.Add(Key(target.OriginSketchPoint));
            }
            else
            {
                var previous=new HashSet<string>(sketch.SketchLines.Cast<SketchLine>().Select(Key),StringComparer.Ordinal);
                var objects=app.TransientObjects.CreateObjectCollection(); objects.Add(entry.Source);
                // EXACTLY line-only, one source per invocation as native65. Never add
                // TextBox, origin, handle, image or a mixed collection to this API.
                sketch.MoveSketchObjects(objects,app.TransientGeometry.CreateVector2d(UnitConvert.MmToCm(request.DeltaXmm),UnitConvert.MmToCm(request.DeltaYmm)),true,false);
                var added=sketch.SketchLines.Cast<SketchLine>().Where(line=>!previous.Contains(Key(line))).ToArray();
                if(added.Length!=1) throw new ArgumentException("Native line copy did not create exactly one new line.");
                var copy=added[0]; var start=copy.StartSketchPoint.Geometry; var end=copy.EndSketchPoint.Geometry;
                if(Key(copy.StartSketchPoint)==Key(copy.EndSketchPoint) || oldPointKeys.Contains(Key(copy.StartSketchPoint)) || oldPointKeys.Contains(Key(copy.EndSketchPoint))
                    || !NativeSketchCopyPolicy.CopiedPointMatches(UnitConvert.CmToMm(start.X),UnitConvert.CmToMm(start.Y),entry.X,entry.Y)
                    || !NativeSketchCopyPolicy.CopiedPointMatches(UnitConvert.CmToMm(end.X),UnitConvert.CmToMm(end.Y),entry.EndX,entry.EndY))
                    throw new ArgumentException("Native copied line has shared original/prior-copy points or incorrect geometry.");
                oldPointKeys.Add(Key(copy.StartSketchPoint)); oldPointKeys.Add(Key(copy.EndSketchPoint));
            }
        }
        foreach(var entry in frozen) OriginalUnchanged(entry);
        context?.Invoke();
        // No private transaction/Abort, no IDs for tentative objects, no capability
        // publication. The owning batch solves/captures/commits or rolls back all copies.
    }

    private static int ExternalConstraints(SketchEntity entity,SketchLine source)
    {
        int external=0;
        foreach(object constraint in entity.Constraints)
        {
            if(constraint is CoincidentConstraint coincidence && !coincidence.Deletable)
            {
                object one=coincidence.EntityOne,two=coincidence.EntityTwo;
                bool OwnPoint(object candidate)=>SketchReferences.SameComObject(candidate,source.StartSketchPoint)
                    || SketchReferences.SameComObject(candidate,source.EndSketchPoint);
                if((SketchReferences.SameComObject(one,source) && OwnPoint(two)
                    || SketchReferences.SameComObject(two,source) && OwnPoint(one))
                    && NativeSketchCopyPolicy.IsIntrinsicLineCoincidence(false,Key(one),Key(two),Key(source),Key(source.StartSketchPoint),Key(source.EndSketchPoint))) continue;
            }
            external++;
        }
        return external;
    }
    private static bool PointShared(PlanarSketch sketch,SketchPoint point,SketchLine source)
    {
        // Native owner relationships, not coordinate coincidence. Origin points of
        // text and endpoints/centres of other curves are rejected even without a constraint.
        var owners=point.OwnedBy;
        if(owners!=null) foreach(object owner in owners)
            if(!SketchReferences.SameComObject(owner,source)) return true;
        foreach(TextBox text in sketch.TextBoxes)
            if(SketchReferences.SameComObject(text.OriginSketchPoint,point)) return true;
        return false;
    }
    private static void OriginalUnchanged(Frozen entry)
    {
        if(entry.Curve!=null) { NativeSketchCurveCopies93.OriginalUnchanged(entry.Curve); return; }
        JObject current=entry.Source is TextBox text ? TextState(text,text.Style) : LineState((SketchLine)entry.Source);
        if(!JToken.DeepEquals(entry.Before,current)) throw new ArgumentException("Native copying changed an original object or its style.");
    }
    private static Action FreezeCurveContext(InventorCommandContext ctx,PartDocument part,PlanarSketch sketch)
    {
        if(sketch.SketchImages.Count!=0 || sketch.DimensionConstraints.Count!=0 || part.TextStyles.Count>NativeSketchCopyPolicy.MaxSelections)
            throw new ArgumentException("Curve-copy context with images or dimensions is not yet capturable.");
        var reads=new List<Func<JObject>>();
        foreach(SketchEntity entity in sketch.SketchEntities)
        {
            X.Deadline(ctx,"freezing the original native curve-copy context");
            if(entity.AttributeSets.Count!=0 || entity.SketchBlockPath.Count!=0)
                throw new ArgumentException("Annotated/block curve-copy context is not yet capturable.");
            if(entity is SketchCircle or SketchArc) reads.Add(()=>NativeSketchCurveCopies93.State(entity,ctx));
            else if(entity is SketchLine line && entity is not SketchSplineHandle)
                reads.Add(()=>new JObject { ["line"]=LineState(line),["flags"]=NativeSketchCurveCopies93.ContextLineFlags(line) });
            else if(entity is SketchPoint point) reads.Add(()=>NativeSketchCurveCopies93.ContextPointState(point));
            else throw new ArgumentException("Curve-copy context contains unsupported geometry; complete copy coverage remains open.");
        }
        foreach(TextBox text in sketch.TextBoxes) reads.Add(()=>TextState(text,text.Style));
        foreach(TextStyle style in part.TextStyles) reads.Add(()=>StyleState(style));
        int styleCount=part.TextStyles.Count;
        var baseline=reads.Select(read=> { X.Deadline(ctx,"capturing the original copy-context state"); return (JObject)read().DeepClone(); }).ToArray();
        return ()=> {
            if(part.TextStyles.Count!=styleCount) throw new ArgumentException("Native copying changed the style inventory.");
            for(int i=0;i<reads.Count;i++) {
                X.Deadline(ctx,"verifying original native copy-context preservation");
                if(!JToken.DeepEquals(baseline[i],reads[i]()))
                    throw new ArgumentException("Native copying changed an original context object, point, relation or style.");
            }
        };
    }
    private static string Key(object value)
    { byte[] bytes=Array.Empty<byte>(); ((dynamic)value).GetReferenceKey(ref bytes,0); if(bytes.Length==0) throw new ArgumentException("Native copy object has no persistent key."); return Convert.ToBase64String(bytes); }
    private static JObject LineState(SketchLine line)=>new JObject { ["key"]=Key(line),["start_key"]=Key(line.StartSketchPoint),["end_key"]=Key(line.EndSketchPoint),
        ["start_mm"]=new JArray(UnitConvert.CmToMm(line.StartSketchPoint.Geometry.X),UnitConvert.CmToMm(line.StartSketchPoint.Geometry.Y)),
        ["end_mm"]=new JArray(UnitConvert.CmToMm(line.EndSketchPoint.Geometry.X),UnitConvert.CmToMm(line.EndSketchPoint.Geometry.Y)),
        ["construction"]=line.Construction,["centerline"]=line.Centerline,
        ["constraints"]=new JArray(line.Constraints.Cast<object>().Select(ConstraintState)),
        ["start_constraints"]=new JArray(line.StartSketchPoint.Constraints.Cast<object>().Select(ConstraintState)),
        ["end_constraints"]=new JArray(line.EndSketchPoint.Constraints.Cast<object>().Select(ConstraintState)) };
    private static JObject ConstraintState(object constraint)
    {
        if(constraint is not CoincidentConstraint native || native.Deletable)
            throw new ArgumentException("Free line constraint profile changed after preflight.");
        return new JObject { ["key"]=Key(native),["one"]=Key(native.EntityOne),["two"]=Key(native.EntityTwo),["deletable"]=native.Deletable };
    }
    private static JObject ColorState(Color color)=>new JObject { ["r"]=(int)color.Red,["g"]=(int)color.Green,["b"]=(int)color.Blue,
        ["opacity"]=color.Opacity,["source"]=(int)color.ColorSourceType };
    private static JObject TextState(TextBox text,TextStyle style)=>new JObject {
        ["key"]=Key(text),["origin_key"]=Key(text.OriginSketchPoint),["origin_mm"]=new JArray(UnitConvert.CmToMm(text.Origin.X),UnitConvert.CmToMm(text.Origin.Y)),
        ["formatted_text"]=text.FormattedText,["text"]=text.Text,["fitted"]=text.Fitted,["rotation"]=text.Rotation,["use_background_color"]=text.UseBackgroundColor,
        ["width_scale"]=text.WidthScale,["horizontal"]=(int)text.HorizontalJustification,["vertical"]=(int)text.VerticalJustification,
        ["single_line"]=text.SingleLineText,["spacing_type"]=(int)text.LineSpacingType,["spacing"]=text.LineSpacing,["color"]=ColorState(text.Color),
        ["style"]=StyleState(style) };
    private static JObject StyleState(TextStyle style)=>new JObject { ["key"]=Key(style),["name"]=style.Name,["font"]=style.Font,["font_size_mm"]=UnitConvert.CmToMm(style.FontSize),
            ["bold"]=style.Bold,["italic"]=style.Italic,["underline"]=style.Underline,["rotation"]=style.Rotation,["width_scale"]=style.WidthScale,
            ["horizontal"]=(int)style.HorizontalJustification,["vertical"]=(int)style.VerticalJustification,["spacing_type"]=(int)style.LineSpacingType,
            ["spacing"]=style.LineSpacing,["color"]=ColorState(style.Color),["location"]=(int)style.StyleLocation };
    private static void CompareCopiedText(JObject before,JObject after,double x,double y)
    {
        var origin=(JArray)after["origin_mm"]!;
        if(!NativeSketchCopyPolicy.CopiedPointMatches((double)origin[0],(double)origin[1],x,y))
            throw new ArgumentException("Native copied text origin differs from its frozen translation.");
        var expected=(JObject)before.DeepClone();var actual=(JObject)after.DeepClone();
        foreach(string name in new[]{"key","origin_key","origin_mm"}) {expected.Remove(name);actual.Remove(name);}
        if(!JToken.DeepEquals(expected,actual)) throw new ArgumentException("Native copied text properties, literal content, color or style differ.");
    }
    private static void FiniteStyle(JObject style)
    {
        foreach(string name in new[]{"rotation","width_scale","spacing","font_size_mm"})
        { double value=(double)style[name]!; if(double.IsNaN(value)||double.IsInfinity(value) || name!="rotation" && value<=0) throw new ArgumentException("Native copy style contains invalid numeric properties."); }
        if(string.IsNullOrWhiteSpace((string?)style["font"]) || string.IsNullOrWhiteSpace((string?)style["name"])) throw new ArgumentException("Native copy style identity/font is unavailable.");
    }
    private static void CheckColor(JObject color)
    { double value=(double)color["opacity"]!; if(double.IsNaN(value)||double.IsInfinity(value)||value<0||value>1) throw new ArgumentException("Native copy color opacity is invalid."); }
}
#endif
