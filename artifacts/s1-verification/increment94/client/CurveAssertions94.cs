using InventorXrSo.Core.Backend;
using Newtonsoft.Json.Linq;

internal static partial class Program
{
    private static void CheckOld94(NativeSketchSnapshot before,NativeSketchSnapshot after)
    {
        Require(after.Entities.Select(e=>e.Reference.EntityId).Distinct(StringComparer.Ordinal).Count()==after.Entities.Count,"Duplicate represented entity ID.");
        foreach(var old in before.Entities){var matches=after.Entities.Where(e=>e.Reference.EntityId==old.Reference.EntityId).ToArray();Require(matches.Length==1&&Exact94(old.Data,matches[0].Data),"Source/prior-copy/structural point properties or glyphs changed.");}
        Require(after.Dimensions.Count==before.Dimensions.Count,"Copy introduced a dimension.");
        foreach(var old in before.Dimensions){var matches=after.Dimensions.Where(e=>e.Reference.EntityId==old.Reference.EntityId).ToArray();Require(matches.Length==1&&Exact94(old.Data,matches[0].Data),"Existing dimension changed.");}
        foreach(var old in before.Constraints){var matches=after.Constraints.Where(e=>e.Reference.EntityId==old.Reference.EntityId).ToArray();Require(matches.Length==1&&Exact94(old.Data,matches[0].Data),"Existing constraint changed.");}
    }
    private static void Shift94(JArray source,JArray copy,int stride,double dx,double dy)
    {
        Require(source.Count>0&&source.Count==copy.Count&&source.Count%stride==0,"Translated array dimensions differ.");
        for(int i=0;i<source.Count;i++){double a=(double)source[i],b=(double)copy[i];Require(double.IsFinite(a)&&double.IsFinite(b)&&Math.Abs(b-a-(i%stride==0?dx:i%stride==1?dy:0))<=Tol,"Copied coordinate is not pure translation.");}
    }
    private static void CurveLocus94(JObject data,string type)
    {
        var points=(JArray?)data["strokes_mm"];Require(points!=null&&points.Count>=4&&points.Count%2==0,"Curve strokes incomplete.");
        if(type=="line")
        {
            var a=(JArray)data["start_mm"]!;var b=(JArray)data["end_mm"]!;double dx=(double)b[0]-(double)a[0],dy=(double)b[1]-(double)a[1],length=Math.Sqrt(dx*dx+dy*dy);Require(double.IsFinite(length)&&length>0,"Copied line is degenerate.");
            for(int i=0;i<points!.Count;i+=2){double x=(double)points[i],y=(double)points[i+1];Require(double.IsFinite(x)&&double.IsFinite(y)&&Math.Abs((x-(double)a[0])*dy-(y-(double)a[1])*dx)/length<=Tol,"Stroke leaves native line locus.");}
        }
        else
        {
            var center=(JArray)data["center_mm"]!;double radius=(double)data["radius_mm"]!;Require(double.IsFinite(radius)&&radius>0&&center.Count==2,"Copied circle/arc radius invalid.");
            void OnCircle(double x,double y){double dx=x-(double)center[0],dy=y-(double)center[1];Require(double.IsFinite(x)&&double.IsFinite(y)&&Math.Abs(Math.Sqrt(dx*dx+dy*dy)-radius)<=Tol,"Stroke/endpoint leaves native circle locus.");}
            for(int i=0;i<points!.Count;i+=2)OnCircle((double)points[i],(double)points[i+1]);
            if(type=="arc")foreach(string field in new[]{"start_mm","end_mm"}){var p=(JArray)data[field]!;OnCircle((double)p[0],(double)p[1]);}
        }
    }
    private static void CheckCopy94(NativeSketchObject source,NativeSketchObject copy,double dx,double dy)
    {
        Require(source.Type==copy.Type&&source.Reference.EntityId!=copy.Reference.EntityId&&copy.Supported,"Copied native type/identity differs.");var a=source.Data;var b=copy.Data;
        var invariantA=(JObject)a.DeepClone();var invariantB=(JObject)b.DeepClone();invariantA.Remove("id");invariantB.Remove("id");
        if(source.Type=="text")
        {
            foreach(string field in new[]{"text_mm","text_bounds_mm"}){Shift94((JArray)a[field]!, (JArray)b[field]!,2,dx,dy);invariantA.Remove(field);invariantB.Remove(field);}
            var ag=(JObject)a["text_glyphs"]!;var bg=(JObject)b["text_glyphs"]!;Require((string?)ag["coordinate_space"]=="model_mm"&&(string?)bg["coordinate_space"]=="model_mm","Glyph coordinate space differs.");
            Shift94((JArray)ag["vertices_mm"]!,(JArray)bg["vertices_mm"]!,3,dx,dy);var ia=(JObject)ag.DeepClone();var ib=(JObject)bg.DeepClone();ia.Remove("vertices_mm");ib.Remove("vertices_mm");Require(Exact94(ia,ib),"Copied text glyph topology/color/metadata differs.");invariantA.Remove("text_glyphs");invariantB.Remove("text_glyphs");
        }
        else
        {
            Require(source.Type is "line" or "circle" or "arc","Unsupported whole copy type.");
            foreach(string field in new[]{"strokes_mm","start_mm","end_mm","center_mm"}){Require((a[field]!=null)==(b[field]!=null),"Curve property presence differs.");if(a[field]!=null){Shift94((JArray)a[field]!, (JArray)b[field]!,2,dx,dy);invariantA.Remove(field);invariantB.Remove(field);}}
            if(source.Type is "circle" or "arc"){double r=(double)a["radius_mm"]!,s=(double)b["radius_mm"]!;Require(double.IsFinite(r)&&double.IsFinite(s)&&r>0&&Math.Abs(r-s)<=1e-8,"Copied radius differs.");invariantA.Remove("radius_mm");invariantB.Remove("radius_mm");}
            // Current Core reader emits center/radius/endpoints/strokes, not
            // native angles/domain. Native94 supplies that independent oracle.
            // If these named analytic fields are present, never compare them
            // via exact float serialization: the native Copy tolerance is1e-10.
            foreach(string field in new[]{"start_angle_rad","sweep_angle_rad","geometry_start_angle_rad","geometry_sweep_angle_rad"})
            {
                Require((a[field]!=null)==(b[field]!=null),"Native angle property presence differs.");
                if(a[field]!=null){Require((a[field]!.Type is JTokenType.Float or JTokenType.Integer)&&(b[field]!.Type is JTokenType.Float or JTokenType.Integer),"Native angle has nonnumeric shape.");double x=(double)a[field]!,y=(double)b[field]!;Require(double.IsFinite(x)&&double.IsFinite(y)&&Math.Abs(x-y)<=1e-10,"Native copied angle differs.");invariantA.Remove(field);invariantB.Remove(field);}
            }
            Require((a["domain"]!=null)==(b["domain"]!=null),"Native domain property presence differs.");
            if(a["domain"]!=null){Require(a["domain"] is JArray ad&&ad.Count==2&&b["domain"] is JArray bd&&bd.Count==2,"Native domain shape differs.");for(int i=0;i<2;i++){double x=(double)a["domain"]![i]!,y=(double)b["domain"]![i]!;Require(double.IsFinite(x)&&double.IsFinite(y)&&Math.Abs(x-y)<=1e-10,"Native copied parameter domain differs.");}invariantA.Remove("domain");invariantB.Remove("domain");}
            CurveLocus94(a,source.Type);CurveLocus94(b,copy.Type);
        }
        Require(Exact94(invariantA,invariantB),"Copied native known non-coordinate Data differs.");
    }
}
