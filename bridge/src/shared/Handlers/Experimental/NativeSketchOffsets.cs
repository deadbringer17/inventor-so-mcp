#if INVENTOR2027 && SO_EXPERIMENTAL
using System;
using System.Collections.Generic;
using System.Linq;
using Bimwright.Ipt.Shared.Handlers.Core;
using Bimwright.Ipt.Shared.Infrastructure;
using Inventor;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Handlers.Experimental;

internal static class NativeSketchOffsets
{
    internal static void Apply(InventorCommandContext ctx,Application app,global::Inventor.Document document,
        PlanarSketch sketch,string sketchId,bool byPoint,JObject options)
    {
        string[] allowed=byPoint ? new[] {"references","point_mm","include_connected","create_constraints"}
            : new[] {"references","distance_mm","natural_direction","include_connected","create_constraints"};
        if(options.Properties().Any(p=>!allowed.Contains(p.Name,StringComparer.Ordinal))) throw new ArgumentException("Unknown native offset option.");
        foreach(string name in new[] {"natural_direction","include_connected","create_constraints"})
            if(options.TryGetValue(name,out var value) && value.Type!=JTokenType.Boolean) throw new ArgumentException("Offset options must be booleans.");
        if(options["references"] is not JArray refs || refs.Count<1 || refs.Count>2000 || refs.Any(r=>r is not JObject))
            throw new ArgumentException("Select 1–2000 whole native curves in connected order.");
        bool connected=X.Bool(options,"include_connected",false);
        if(connected && refs.Count!=1) throw new ArgumentException("Automatic chain inclusion requires exactly one base curve.");
        string documentId=EntityReferences.DocumentId(document),revision=ctx.Events?.Revision(documentId) ?? "";
        var seen=new HashSet<string>(StringComparer.Ordinal);var curves=app.TransientObjects.CreateObjectCollection();
        foreach(JObject reference in refs)
        {
            X.Deadline(ctx,"resolving native offset curves");
            if(X.Str(reference,"document_id")!=documentId || X.Str(reference,"sketch_id")!=sketchId)
                throw new ArgumentException("Offset selection belongs to another document or sketch.");
            if(X.Str(reference,"revision")!=revision) throw ConcurrencyFailure.StaleRevision((string?)reference["revision"],revision);
            string id=X.Str(reference,"entity_id");
            if(!seen.Add(id) || X.Str(reference,"subpart")!="Entity") throw new ArgumentException("Select each whole offset curve once.");
            var native=SketchReferences.Resolve(document,id,"sketch_entity") as SketchEntity;
            if(native==null || native is SketchPoint || !SketchReferences.SameComObject(native.Parent,sketch)
                || X.Str(reference,"type")!=SketchEditorXrHandler.Kind(native))
                throw new ArgumentException("Offset requires native curves of the declared type and actual sketch owner.");
            curves.Add(native);
        }
        bool constraints=X.Bool(options,"create_constraints",true);
        try
        {
            if(byPoint) sketch.OffsetSketchEntitiesUsingPoint(curves,NativeSketchDimensions.Point(options["point_mm"],app.TransientGeometry),connected,constraints);
            else sketch.OffsetSketchEntitiesUsingDistance(curves,UnitConvert.MmToCm(X.Positive(options,"distance_mm")),
                X.Bool(options,"natural_direction",true),connected,constraints);
        }
        catch(System.Runtime.InteropServices.COMException ex)
        {
            throw new CodedFailureException(Bimwright.Ipt.Shared.Contracts.InventorErrorCodes.API_ERROR,
                "Inventor rejected the requested native sketch offset.",new JObject {["native_hresult"]=ex.HResult});
        }
        // Native API decides connectivity, side and offset constraints; no approximated reconstruction.
    }
}
#endif
