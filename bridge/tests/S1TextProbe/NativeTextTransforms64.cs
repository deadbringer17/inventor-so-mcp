using System.Text.Json.Nodes;
using Inventor;

internal static partial class Program
{
    private static void RunTextTransforms64(bool sheet,JsonArray contexts)
    {
        var context=new JsonObject{["kind"]=sheet ? "sheetmetal" : "part",["scope"]="Native RotateSketchObjects text glyph research; no product capability enabled."};contexts.Add(context);
        var cases=new JsonArray();context["cases"]=cases;
        foreach(bool includeOrigin in new[]{false,true})
        foreach(int angle in new[]{37,90,180})
        {
            RunCase(sheet,"text_rotate64_"+angle+"_origin_"+includeOrigin,cases,false,(doc,sketch,row)=>
            {
                var text=sketch.TextBoxes.AddFitted(Point(10,20),"Rotate64",Type.Missing);
                Require(doc.Rebuild2(false),"Initial glyph rebuild failed.");
                var before=CaptureGlyphFrame61(doc,sketch,text);string key=Key(text);
                var objects=App!.TransientObjects.CreateObjectCollection();objects.Add(text);if(includeOrigin)objects.Add(text.OriginSketchPoint);
                sketch.RotateSketchObjects(objects,Point(5,7),angle*Math.PI/180,false,false);
                Require(doc.Rebuild2(false),"Rotated glyph rebuild failed.");
                var after=CaptureGlyphFrame61(doc,sketch,text);
                row["before_text"]=before;row["after_text"]=after;row["angle_deg"]=angle;row["include_origin"]=includeOrigin;
                var a=(JsonArray)before["projected_model_vertices_mm"]!;var b=(JsonArray)after["projected_model_vertices_mm"]!;
                Require(a.Count==b.Count&&a.Count>0,"Native rotated glyph vertex count differs.");
                double c=Math.Cos(angle*Math.PI/180),s=Math.Sin(angle*Math.PI/180),maximum=0;
                for(int i=0;i<a.Count;i+=2)
                {
                    double x=(double)a[i]!-5,y=(double)a[i+1]!-7;double ex=5+c*x-s*y,ey=7+s*x+c*y,best=double.PositiveInfinity;
                    for(int j=0;j<b.Count;j+=2){double dx=ex-(double)b[j]!,dy=ey-(double)b[j+1]!;best=Math.Min(best,Math.Sqrt(dx*dx+dy*dy));}
                    maximum=Math.Max(maximum,best);
                }
                row["max_independent_glyph_rotation_error_mm"]=maximum;row["same_native_key"]=Key(text)==key;
                Require(maximum<=1e-5&&Key(text)==key,"Native glyphs did not rotate around the requested center.");
                double ox=5+c*5-s*13,oy=7+s*5+c*13;
                Require(Math.Abs(text.Origin.X*10-ox)<=1e-5&&Math.Abs(text.Origin.Y*10-oy)<=1e-5,"Native text origin did not rotate exactly once.");
                row["origin_rotated_once"]=true;
            });
        }
        context["failed"]=cases.OfType<JsonObject>().Count(c=>(string?)c["status"]=="FAIL");
        context["passed"]=cases.OfType<JsonObject>().Count(c=>(string?)c["status"]=="PASS");
    }
}
