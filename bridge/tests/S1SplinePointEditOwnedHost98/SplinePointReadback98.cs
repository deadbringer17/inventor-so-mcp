using Bimwright.Ipt.Server;
using Bimwright.Ipt.Shared.Contracts;
using Inventor;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

internal static partial class Program
{
    private static JObject Curve98(SketchEntity entity)
    {
        var points=new JArray();Curve2dEvaluator evaluator;bool closed;var result=new JObject { ["key"]=Key73(entity),["native_type"]=entity.Type.ToString(),["construction"]=entity.Construction,["reference"]=entity.Reference,["constraint_status"]=entity.ConstraintStatus.ToString() };
        if(entity is SketchSpline fit)
        {closed=fit.Closed;result["fit_method"]=(int)fit.FitMethod;result["tension"]=fit.Tension;for(int i=1;i<=fit.FitPointCount;i++){var p=fit.FitPoint[i];points.Add(new JObject { ["key"]=Key73(p),["xy"]=new JArray(p.Geometry.X*10,p.Geometry.Y*10) });}evaluator=fit.Geometry.Evaluator;}
        else {var cv=(SketchControlPointSpline)entity;closed=cv.IsClosed;for(int i=1;i<=cv.ControlPointCount;i++){var p=cv.ControlPoint[i];points.Add(new JObject { ["key"]=Key73(p),["xy"]=new JArray(p.Geometry.X*10,p.Geometry.Y*10) });}evaluator=cv.Geometry.Evaluator;}
        result["closed"]=closed;result["points"]=points;
        double min=0,max=0;evaluator.GetParamExtents(out min,out max);Require89(double.IsFinite(min)&&double.IsFinite(max)&&max>min,"Spline98 invalid evaluator interval.");
        double[] parameters=Enumerable.Range(0,101).Select(i=>min+(max-min)*i/100).ToArray(),samples=Array.Empty<double>();evaluator.GetPointAtParam(ref parameters,ref samples);
        int n=0;double[] strokes=Array.Empty<double>();evaluator.GetStrokes(min,max,.0001,out n,out strokes);
        Require89(samples.Length==202&&samples.All(double.IsFinite)&&n>=2&&n<=8192&&strokes.Length==n*2&&strokes.All(double.IsFinite),"Spline98 evaluator read incomplete.");
        result["domain"]=new JArray(min,max);result["samples_mm"]=new JArray(samples.Select(x=>x*10));result["strokes_mm"]=new JArray(strokes.Select(x=>x*10));return result;
    }
    private static void Near98(JArray a,JArray b,double tolerance=1e-5)
    {Require89(a.Count==b.Count,"Spline98 coordinate count differs.");for(int i=0;i<a.Count;i++)Require89(double.IsFinite((double)a[i])&&double.IsFinite((double)b[i])&&Math.Abs((double)a[i]-(double)b[i])<=tolerance,"Spline98 native oracle geometry differs.");}
    private static void Compare98(JObject actual,JObject oracle)
    {
        foreach(string field in new[]{"native_type","construction","reference","constraint_status","closed","fit_method","tension"})Require89(JToken.DeepEquals(actual[field],oracle[field]),"Spline98 native oracle property differs.");
        Near98((JArray)actual["domain"]!,(JArray)oracle["domain"]!,1e-10);Near98((JArray)actual["samples_mm"]!,(JArray)oracle["samples_mm"]!);Near98((JArray)actual["strokes_mm"]!,(JArray)oracle["strokes_mm"]!);
        var a=(JArray)actual["points"]!;var b=(JArray)oracle["points"]!;Require89(a.Count==b.Count,"Spline98 native fit/control point count differs.");
        for(int i=0;i<a.Count;i++)
        {
            Near98((JArray)a[i]["xy"]!,(JArray)b[i]["xy"]!);
            for(int j=0;j<i;j++)Require89(((string?)a[i]["key"]==(string?)a[j]["key"])==((string?)b[i]["key"]==(string?)b[j]["key"]),"Spline98 native point alias topology differs from the independent oracle.");
        }
    }
}
