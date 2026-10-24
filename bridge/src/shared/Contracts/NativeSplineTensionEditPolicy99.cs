using System;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Contracts;

/// <summary>Frozen input for one native fit-spline tension edit. No global defaults.</summary>
public static class NativeSplineTensionEditPolicy99
{
    public sealed class Request
    {
        internal Request(NativeSplinePointEditPolicy98.Reference curve,int tension)
        { Curve=curve;Tension=tension; }
        public NativeSplinePointEditPolicy98.Reference Curve { get; }
        public int Tension { get; }
    }

    public static Request Parse(JObject options)
    {
        if(options==null || options.Count!=2 || options.Properties().Any(p=>p.Name!="references"&&p.Name!="tension")
            || options["references"] is not JArray references || references.Count!=1)
            throw new ArgumentException("Spline tension editing requires one whole typed spline reference and an explicit tension.");
        var curve=NativeSplinePointEditPolicy98.ParseReference(references[0]);
        if(curve.Type!="spline" || curve.Subpart!="Entity")
            throw new ArgumentException("Tension applies to the whole existing native fit spline.");
        var value=options["tension"];
        // UI-compatible integer domain. The native97 observation matrix tests
        // five points of this domain; it is not a proof of every integer.
        if(value?.Type!=JTokenType.Integer || (double)value<0 || (double)value>100)
            throw new ArgumentException("Spline tension must be an integer from 0 through 100.");
        return new Request(curve,(int)value);
    }

    public static void ValidateBinding(Request request,string document,string revision,string sketch)
    {
        if(request==null || string.IsNullOrWhiteSpace(document) || string.IsNullOrWhiteSpace(revision)
            || string.IsNullOrWhiteSpace(sketch) || request.Curve.DocumentId!=document
            || request.Curve.Revision!=revision || request.Curve.SketchId!=sketch)
            throw new ArgumentException("Spline tension reference differs from the current document, sketch or revision.");
    }
}
