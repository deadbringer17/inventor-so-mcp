using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Inventor;

internal static partial class Program
{
    private static void RunGlyphFrames61(bool sheet,JsonArray contexts)
    {
        var context=new JsonObject{["kind"]=sheet ? "sheetmetal" : "part",["scope"]="Raw native facets/frame research; no product renderer or desktop parity."};
        contexts.Add(context);var samples=new JsonArray();context["samples"]=samples;
        var doc=NewDocument(sheet,"GlyphFrames61");doc.DisplayName="S1_GlyphFrames61_"+(sheet ? "Sheet" : "Part")+".ipt";
        try
        {
            var component=doc.ComponentDefinition;var g=App!.TransientGeometry;
            var xy=component.Sketches.Add(component.WorkPlanes[3]);xy.Name="S1_Glyph61_XY";
            double c=Math.Sqrt(.5);
            var plane=component.WorkPlanes.AddFixed(g.CreatePoint(2,3,4),g.CreateUnitVector(1,0,0),g.CreateUnitVector(0,c,c),true);
            var tilted=component.Sketches.Add(plane);tilted.Name="S1_Glyph61_Tilted";doc.Update2(false);
            foreach(var sketch in new[]{xy,tilted})
            {
                Phase=sketch.Name+":baseline";var before=ReadDocument(doc);
                GuardApplication();var transaction=App.TransactionManager.StartTransaction((_Document)doc,"S1 glyph61 frames, abort");
                try
                {
                    var text=sketch.TextBoxes.AddFitted(Point(10,20),"L=22",Type.Missing);
                    foreach(double widthScale in new[]{1.0,1.25})
                    {
                        JsonObject? baseline=null;
                        foreach(int angle in new[]{0,90,180,270})
                        {
                            Phase=sketch.Name+":rotation_"+angle+"_scale_"+widthScale.ToString(CultureInfo.InvariantCulture);
                            text.WidthScale=widthScale;text.Rotation=angle*Math.PI/180;
                            Require(doc.Rebuild2(false),"Native glyph frame rebuild failed.");
                            var sample=CaptureGlyphFrame61(doc,sketch,text);
                            sample["requested_rotation_deg"]=angle;sample["requested_width_scale"]=widthScale;
                            if(baseline==null) baseline=sample;
                            sample["rotation_about_origin_matches_raw_facets"]=RotationMatches61(baseline,sample,angle);
                            samples.Add(sample);
                        }
                    }
                    text.FormattedText="Rotate59";text.WidthScale=1;text.Rotation=0;
                    Require(doc.Rebuild2(false),"Native complex glyph rebuild failed.");
                    Phase=sketch.Name+":rotate59_initialized_arrays";
                    var complex=CaptureGlyphFrame61(doc,sketch,text);complex["purpose"]="Retest previous failed content with initialized output arrays and rebuilt text.";samples.Add(complex);
                }
                finally
                {
                    GuardApplication();transaction.Abort();context[sketch.Name+"_abort_exact"]=JsonNode.DeepEquals(before,ReadDocument(doc));
                    Require((bool?)context[sketch.Name+"_abort_exact"]==true,"Glyph frame transaction did not restore exact native state.");
                }
            }
            context["failed"]=0;context["sample_count"]=samples.Count;context["status"]="PASS collection; coordinate/rotation classifications are explicit observations.";
        }
        finally{CloseOwned((Document)doc);context["fixture_closed_unsaved"]=true;}
    }
    private static JsonObject CaptureGlyphFrame61(PartDocument doc,PlanarSketch sketch,TextBox text)
    {
        GuardApplication();string revision=doc.DatabaseRevisionId;bool dirty=doc.Dirty;string key=Key(text);
        int vertices=0,facets=0;double[] coordinates=Array.Empty<double>();int[] indices=Array.Empty<int>();
        text.GetFacetsInfo(out vertices,out facets,out coordinates,out indices);
        Require(vertices>0&&vertices<=100000&&coordinates.Length==vertices*3&&coordinates.All(double.IsFinite)
            &&facets>0&&indices.Length==facets*3&&indices.All(i=>i>=1&&i<=vertices),"Native glyph mesh contract is invalid.");
        var g=App!.TransientGeometry;
        var origin=sketch.SketchToModelSpace(g.CreatePoint2d(0,0));
        var x=sketch.SketchToModelSpace(g.CreatePoint2d(1,0));var y=sketch.SketchToModelSpace(g.CreatePoint2d(0,1));
        double[] o={origin.X,origin.Y,origin.Z},u={x.X-origin.X,x.Y-origin.Y,x.Z-origin.Z},v={y.X-origin.X,y.Y-origin.Y,y.Z-origin.Z};
        double[] n={u[1]*v[2]-u[2]*v[1],u[2]*v[0]-u[0]*v[2],u[0]*v[1]-u[1]*v[0]};
        var box=text.RangeBox;double tolerance=1e-6,planeDistance=0,zDistance=0;bool insideSketch=true,insideModel=true;
        var mapped=new double[vertices*2];
        for(int i=0;i<vertices;i++)
        {
            double[] d={coordinates[i*3]-o[0],coordinates[i*3+1]-o[1],coordinates[i*3+2]-o[2]};
            double sx=d.Zip(u,(a,b)=>a*b).Sum(),sy=d.Zip(v,(a,b)=>a*b).Sum();mapped[i*2]=sx*10;mapped[i*2+1]=sy*10;
            planeDistance=Math.Max(planeDistance,Math.Abs(d.Zip(n,(a,b)=>a*b).Sum())*10);zDistance=Math.Max(zDistance,Math.Abs(coordinates[i*3+2])*10);
            bool Inside(double a,double b)=>a>=box.MinPoint.X-tolerance&&a<=box.MaxPoint.X+tolerance&&b>=box.MinPoint.Y-tolerance&&b<=box.MaxPoint.Y+tolerance;
            insideSketch&=Inside(coordinates[i*3],coordinates[i*3+1]);insideModel&=Inside(sx,sy);
        }
        var boundary=new JsonArray();
        foreach(SketchEntity entity in text.BoundaryGeometry)
        {
            Require(boundary.Count<16,"Native text boundary exceeds research budget.");
            var row=new JsonObject{["native_type"]=entity.Type.ToString()};
            if(entity is SketchLine line){row["start_mm"]=new JsonArray(line.StartSketchPoint.Geometry.X*10,line.StartSketchPoint.Geometry.Y*10);row["end_mm"]=new JsonArray(line.EndSketchPoint.Geometry.X*10,line.EndSketchPoint.Geometry.Y*10);}
            boundary.Add(row);
        }
        var result=new JsonObject
        {
            ["sketch"]=sketch.Name,["text"]=text.Text,["formatted_text"]=text.FormattedText,["reference_key"]=key,
            ["origin_mm"]=new JsonArray(text.Origin.X*10,text.Origin.Y*10),["rotation_deg"]=text.Rotation*180/Math.PI,["width_scale"]=text.WidthScale,
            ["range_mm"]=new JsonArray(box.MinPoint.X*10,box.MinPoint.Y*10,box.MaxPoint.X*10,box.MaxPoint.Y*10),
            ["font"]=text.Style.Font,["font_size_mm"]=text.Style.FontSize*10,["horizontal"]=text.HorizontalJustification.ToString(),["vertical"]=text.VerticalJustification.ToString(),
            ["frame"]=new JsonObject{["origin_mm"]=JsonSerializer.SerializeToNode(o.Select(a=>a*10).ToArray()),["x_axis"]=JsonSerializer.SerializeToNode(u),["y_axis"]=JsonSerializer.SerializeToNode(v)},
            ["vertices"]=vertices,["facets"]=facets,["raw_vertices_mm"]=JsonSerializer.SerializeToNode(coordinates.Select(a=>a*10).ToArray()),["indices_one_based"]=JsonSerializer.SerializeToNode(indices),
            ["projected_model_vertices_mm"]=JsonSerializer.SerializeToNode(mapped),["max_raw_z_mm"]=zDistance,["max_distance_to_model_sketch_plane_mm"]=planeDistance,
            ["raw_xy_inside_native_range"]=insideSketch,["model_projected_xy_inside_native_range"]=insideModel,
            ["sketch_space_candidate"]=insideSketch&&zDistance<=1e-5,["model_space_candidate"]=insideModel&&planeDistance<=1e-5,
            ["boundary_geometry"]=boundary
        };
        Require(doc.DatabaseRevisionId==revision&&doc.Dirty==dirty&&Key(text)==key,"Glyph frame capture changed persistent native state.");
        result["read_only_exact"]=true;return result;
    }
    private static bool RotationMatches61(JsonObject baseline,JsonObject actual,int angle)
    {
        var source=baseline["raw_vertices_mm"]!.AsArray().Select(x=>(double)x!).ToArray();
        var target=actual["raw_vertices_mm"]!.AsArray().Select(x=>(double)x!).ToArray();
        if(source.Length!=target.Length)return false;
        double ox=(double)baseline["origin_mm"]![0]!,oy=(double)baseline["origin_mm"]![1]!,a=angle*Math.PI/180;
        for(int i=0;i<source.Length;i+=3)
        {
            double x=source[i]-ox,y=source[i+1]-oy;
            if(Math.Abs(target[i]-(ox+x*Math.Cos(a)-y*Math.Sin(a)))>1e-5
                ||Math.Abs(target[i+1]-(oy+x*Math.Sin(a)+y*Math.Cos(a)))>1e-5||Math.Abs(target[i+2]-source[i+2])>1e-5)return false;
        }
        return true;
    }
}
