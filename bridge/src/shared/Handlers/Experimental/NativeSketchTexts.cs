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

/// <summary>Native text mutation foundation; undisclosed until native glyph preview and XR editing are integrated.</summary>
internal static class NativeSketchTexts
{
    internal static void Apply(InventorCommandContext ctx,Application app,PartDocument part,PlanarSketch sketch,
        string sketchId,string kind,JObject options)
    {
        var document=(global::Inventor.Document)part;
        bool creating=kind=="create",deleting=kind=="delete";
        if(!creating && !deleting && kind!="update") throw new ArgumentException("Unknown native text mutation.");
        string[] allowed=creating ? new[]{"content","position_mm","opposite_corner_mm","style_name"}
            : deleting ? new[]{"references"} : new[]{"references","content","position_mm"};
        if(options.Properties().Any(p=>!allowed.Contains(p.Name,StringComparer.Ordinal))) throw new ArgumentException("Unknown native text option.");
        TextBox? text=null;
        if(!creating)
        {
            if(options["references"] is not JArray references || references.Count!=1 || references[0] is not JObject reference
                || reference.Count!=6) throw new ArgumentException("Select exactly one whole native text reference.");
            string documentId=EntityReferences.DocumentId(document),revision=ctx.Events?.Revision(documentId) ?? "";
            if(X.Str(reference,"document_id")!=documentId || X.Str(reference,"sketch_id")!=sketchId)
                throw new ArgumentException("Text belongs to another document or sketch.");
            if(X.Str(reference,"revision")!=revision) throw ConcurrencyFailure.StaleRevision((string?)reference["revision"],revision);
            if(X.Str(reference,"type")!="text" || X.Str(reference,"subpart")!="Entity") throw new ArgumentException("Select whole native text.");
            text=SketchReferences.Resolve(document,X.Str(reference,"entity_id"),"sketch_text") as TextBox
                ?? throw new ArgumentException("Native text reference is unavailable.");
            if(!SketchReferences.SameComObject(text.Parent,sketch)) throw new ArgumentException("Native text owner differs from the selected sketch.");
        }
        if(deleting){text!.Delete();return;}
        if(creating && sketch.SketchEntities.Count+sketch.TextBoxes.Count+sketch.SketchImages.Count>=2000)
            throw new ArgumentException("Native sketch representation budget exceeded.");
        if(!creating && options["content"]==null && options["position_mm"]==null) throw new ArgumentException("Supply a native text change.");
        SketchTextContent? content=null;
        if(creating || options["content"]!=null)
        {
            content=SketchTextContent.Parse(options["content"] as JObject ?? throw new ArgumentException("Structured text content is required."));
            var unresolved=new HashSet<string>(content.ParameterNames,StringComparer.Ordinal);
            foreach(Parameter parameter in part.ComponentDefinition.Parameters)
            {
                if(unresolved.Count==0) break;
                X.Deadline(ctx,"resolving local native text parameters");
                // Resolve every name in this document before creating or editing any text.
                unresolved.Remove(parameter.Name);
            }
            if(unresolved.Count!=0) throw new ArgumentException("A local text parameter is unavailable.");
        }
        Point2d? position=creating || options["position_mm"]!=null ? Point(app,options["position_mm"]) : null;
        Point2d? opposite=options["opposite_corner_mm"]!=null ? Point(app,options["opposite_corner_mm"]) : null;
        object styleArgument=Type.Missing;
        if(options["style_name"]!=null)
        {
            if(options["style_name"]?.Type!=JTokenType.String) throw new ArgumentException("Native style name must be text.");
            string name=(string)options["style_name"]!;
            if(string.IsNullOrWhiteSpace(name)||name.Length>256||name.Any(char.IsControl)) throw new ArgumentException("Native style name is invalid.");
            bool found=false;
            foreach(TextStyle style in part.TextStyles)
            {X.Deadline(ctx,"resolving the native text style");if(style.Name==name){found=true;break;}}
            if(!found) throw new ArgumentException("Native text style is unavailable in the owning document.");
            styleArgument=name;
        }
        if(opposite!=null && (Math.Abs(position!.X-opposite.X)<1e-9 || Math.Abs(position.Y-opposite.Y)<1e-9))
            throw new ArgumentException("Text rectangle must have positive width and height.");
        X.Deadline(ctx,"applying the native text change");
        if(creating)
        {
            if(opposite==null) sketch.TextBoxes.AddFitted(position!,content!.FormattedText,styleArgument);
            else sketch.TextBoxes.AddByRectangle(position!,opposite,content!.FormattedText,styleArgument);
        }
        else
        {
            if(content!=null) text!.FormattedText=content.FormattedText;
            if(position!=null) text!.Origin=position;
            if(content!=null)
            {
                // Update2 alone leaves newly inserted parameter fields with stale text and
                // glyphs. Rebuild inside this same owned transaction registers their native
                // dependencies; subsequent source changes then update the existing object.
                X.Deadline(ctx,"regenerating native text dependencies");
                if(!part.Rebuild2(false)) throw new CodedFailureException(InventorErrorCodes.VALIDATION_FAILED,"Native text regeneration failed.");
            }
        }
    }
    private static Point2d Point(Application app,JToken? token)
    {
        if(token is not JArray point || point.Count!=2 || point.Any(x=>x.Type is not (JTokenType.Integer or JTokenType.Float)))
            throw new ArgumentException("Text positions require two finite millimetre coordinates.");
        double x=(double)point[0],y=(double)point[1];
        if(double.IsNaN(x)||double.IsInfinity(x)||double.IsNaN(y)||double.IsInfinity(y)) throw new ArgumentException("Text position is not finite.");
        return app.TransientGeometry.CreatePoint2d(UnitConvert.MmToCm(x),UnitConvert.MmToCm(y));
    }
}
#endif
