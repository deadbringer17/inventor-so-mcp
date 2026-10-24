using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Contracts;

/// <summary>Preflight of native spline creator input, before any document mutation.</summary>
public static class NativeSplineCreationPolicy96
{
    public sealed class Request
    {
        internal Request(bool closed,bool specified,int? method,IEnumerable<(double X,double Y)> points)
        { Closed=closed;ClosureSpecified=specified;FitMethod=method;PointsMm=Array.AsReadOnly(points.ToArray()); }
        public bool Closed { get; }
        public bool ClosureSpecified { get; }
        public int? FitMethod { get; }
        public IReadOnlyList<(double X,double Y)> PointsMm { get; }
    }
    public static Request Parse(JObject options,bool control)
    {
        if(options==null||options.Properties().Any(p=>p.Name!="points_mm"&&p.Name!="closed"&&(control||p.Name!="fit_method")))
            throw new ArgumentException("Unknown native spline creation option.");
        bool closed=false;
        bool specified=options.TryGetValue("closed",out var close);
        if(specified)
        {
            if(close==null||close.Type!=JTokenType.Boolean)throw new ArgumentException("Spline closure must be an explicit boolean.");
            closed=(bool)close;
        }
        int? method=null;
        if(!control)
        {
            var token=options["fit_method"];
            if(token?.Type!=JTokenType.Integer||(double)token<26369||(double)token>26371)
                throw new ArgumentException("Spline fit method must be an explicit native enum value.");
            method=(int)token;
        }
        if(options["points_mm"] is not JArray data||data.Count<2||data.Count>64)
            throw new ArgumentException("Spline needs 2–64 input points.");
        var points=new List<(double X,double Y)>();
        foreach(var item in data)
        {
            if(item is not JArray xy||xy.Count!=2||xy.Any(v=>v.Type is not (JTokenType.Float or JTokenType.Integer)))
                throw new ArgumentException("Spline points require two finite millimetre coordinates.");
            double x=(double)xy[0],y=(double)xy[1];
            if(double.IsNaN(x)||double.IsInfinity(x)||double.IsNaN(y)||double.IsInfinity(y))
                throw new ArgumentException("Spline point is not finite.");
            points.Add((x,y));
        }
        // Native CV closure is a repeated first point, not a writable IsClosed
        // property. Preserve explicitly closed input without a second repetition.
        if(control&&specified&&!closed&&points[0]==points[points.Count-1])
            throw new ArgumentException("An explicitly open control spline must not repeat its first point.");
        if(control&&closed&&points[0]!=points[points.Count-1])points.Add(points[0]);
        return new Request(closed,specified,method,points);
    }
}
