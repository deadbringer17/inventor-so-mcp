#nullable disable
using System;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Contracts;

public static class SketchTextGlyphMeshContract
{
    /// <summary>Four rotated corners in sketch-local millimetres, in cyclic order.</summary>
    public static void ValidateBounds(JArray bounds)
    {
        if(bounds==null||bounds.Count!=8||bounds.Any(v=>(v.Type!=JTokenType.Integer&&v.Type!=JTokenType.Float)
            ||double.IsNaN((double)v)||double.IsInfinity((double)v)||Math.Abs((double)v)>1e9)) throw Invalid();
        double x=(double)bounds[2]-(double)bounds[0],y=(double)bounds[3]-(double)bounds[1];
        double u=(double)bounds[6]-(double)bounds[0],v2=(double)bounds[7]-(double)bounds[1];
        double width=Math.Sqrt(x*x+y*y),height=Math.Sqrt(u*u+v2*v2);
        if(width<=0||height<=0||Math.Abs((x*u+y*v2)/width/height)>1e-8
            ||Math.Abs((double)bounds[4]-(double)bounds[0]-x-u)>1e-5
            ||Math.Abs((double)bounds[5]-(double)bounds[1]-y-v2)>1e-5) throw Invalid();
    }
    public static void Validate(JObject mesh)
    {
        var fields=new[]{"schema","coordinate_space","vertices_mm","indices","color_rgb"};
        if(mesh==null||mesh.Count!=5||mesh.Properties().Any(p=>!fields.Contains(p.Name,StringComparer.Ordinal))
            ||mesh["schema"]?.Type!=JTokenType.Integer||(string)mesh["schema"]!="1"||mesh["coordinate_space"]?.Type!=JTokenType.String
            ||(string)mesh["coordinate_space"]!="model_mm"||!(mesh["vertices_mm"] is JArray vertices)||vertices.Count%3!=0||vertices.Count>24576
            ||!(mesh["indices"] is JArray indices)||indices.Count%3!=0||indices.Count>98304||vertices.Count==0!= (indices.Count==0)
            ||!(mesh["color_rgb"] is JArray color)||color.Count!=3) throw Invalid();
        foreach(var value in vertices)
            if((value.Type!=JTokenType.Integer&&value.Type!=JTokenType.Float)||double.IsNaN((double)value)||double.IsInfinity((double)value)) throw Invalid();
        int count=vertices.Count/3;
        foreach(var value in indices)
            if(value.Type!=JTokenType.Integer||!int.TryParse(value.ToString(),out int index)||index<0||index>=count) throw Invalid();
        foreach(var value in color)
            if(value.Type!=JTokenType.Integer||!int.TryParse(value.ToString(),out int channel)||channel<0||channel>255) throw Invalid();
    }
    private static FormatException Invalid()=>new FormatException("Native text glyph mesh is invalid.");
}
