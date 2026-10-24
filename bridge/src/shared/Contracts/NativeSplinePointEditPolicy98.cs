using System;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Contracts;

/// <summary>API-agnostic, frozen preflight for editing one existing native spline point.
/// This is neither a capability declaration nor proof of native solver behavior.</summary>
public static class NativeSplinePointEditPolicy98
{
    public sealed class Reference
    {
        internal Reference(string document,string revision,string sketch,string entity,string type,string subpart)
        { DocumentId=document;Revision=revision;SketchId=sketch;EntityId=entity;Type=type;Subpart=subpart; }
        public string DocumentId { get; }
        public string Revision { get; }
        public string SketchId { get; }
        public string EntityId { get; }
        public string Type { get; }
        public string Subpart { get; }
    }

    public sealed class Request
    {
        internal Request(Reference curve,Reference point,double x,double y)
        { Curve=curve;Point=point;Xmm=x;Ymm=y; }
        public Reference Curve { get; }
        public Reference Point { get; }
        public double Xmm { get; }
        public double Ymm { get; }
    }

    public static Request Parse(JObject options)
    {
        if(options==null || options.Count!=2 || options.Properties().Any(p=>p.Name!="references" && p.Name!="position_mm")
            || options["references"] is not JArray references || references.Count!=2
            || options["position_mm"] is not JArray xy || xy.Count!=2)
            throw new ArgumentException("Spline point editing requires two ordered typed references and one planar position.");
        var curve=ParseReference(references[0]);var point=ParseReference(references[1]);
        if((curve.Type!="spline" && curve.Type!="control_point_spline") || curve.Subpart!="Entity"
            || point.Type!="point" || (point.Subpart!="Point" && point.Subpart!="Entity"))
            throw new ArgumentException("Select the whole native fit/control spline first and an actual native point second.");
        if(curve.DocumentId!=point.DocumentId || curve.SketchId!=point.SketchId
            || PersistentEntityReference.Decode(curve.EntityId).Key.SequenceEqual(PersistentEntityReference.Decode(point.EntityId).Key))
            throw new ArgumentException("Spline and point references must be distinct and share document and sketch.");
        return new Request(curve,point,Number(xy[0]),Number(xy[1]));
    }

    /// <summary>Pure binding check. The API helper additionally emits the shared structured stale failure.</summary>
    public static void ValidateBinding(Request request,string documentId,string revision,string sketchId)
    {
        if(request==null || string.IsNullOrWhiteSpace(documentId) || string.IsNullOrWhiteSpace(revision)
            || string.IsNullOrWhiteSpace(sketchId)
            || request.Curve.DocumentId!=documentId || request.Point.DocumentId!=documentId
            || request.Curve.Revision!=revision || request.Point.Revision!=revision
            || request.Curve.SketchId!=sketchId || request.Point.SketchId!=sketchId)
            throw new ArgumentException("Spline point references do not match the current document, sketch and revision.");
    }

    internal static Reference ParseReference(JToken value)
    {
        string[] fields={"document_id","revision","sketch_id","entity_id","type","subpart"};
        if(value is not JObject reference || reference.Count!=fields.Length
            || reference.Properties().Any(p=>!fields.Contains(p.Name,StringComparer.Ordinal)))
            throw new ArgumentException("Spline point editing requires complete closed typed references.");
        string Text(string name,int maximum)
        {
            if(reference[name]?.Type!=JTokenType.String || string.IsNullOrWhiteSpace((string?)reference[name])
                || ((string)reference[name]!).Length>maximum || ((string)reference[name]!).Any(char.IsControl))
                throw new ArgumentException("Spline point reference text is invalid.");
            return (string)reference[name]!;
        }
        string document=Text("document_id",256),revision=Text("revision",2048),
            sketch=Text("sketch_id",PersistentEntityReference.MaximumEncodedLength),
            entity=Text("entity_id",PersistentEntityReference.MaximumEncodedLength),type=Text("type",128),subpart=Text("subpart",32);
        var sketchReference=PersistentEntityReference.Decode(sketch);var entityReference=PersistentEntityReference.Decode(entity);
        if(sketchReference.DocumentId!=document || sketchReference.EntityType!="sketch" || sketchReference.Context.Length!=0
            || entityReference.DocumentId!=document || entityReference.EntityType!="sketch_entity" || entityReference.Context.Length!=0)
            throw new ArgumentException("Spline point opaque references have the wrong document, type or key context.");
        return new Reference(document,revision,sketch,entity,type,subpart);
    }

    private static double Number(JToken value)
    {
        if(value.Type is not (JTokenType.Integer or JTokenType.Float))
            throw new ArgumentException("Spline point coordinates must be numeric millimetres.");
        double result=(double)value;
        if(double.IsNaN(result) || double.IsInfinity(result))
            throw new ArgumentException("Spline point coordinates must be finite.");
        return result;
    }
}
