#if INVENTOR2027 && SO_EXPERIMENTAL
using System;
using System.Linq;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;
using Inventor;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Handlers.Experimental;

internal static class NativeSketchTextGlyphs
{
    // RangeBox is unrotated in sketch coordinates, unlike GetFacetsInfo's model vertices.
    internal static JArray Bounds(TextBox text)
    {
        var box=text.RangeBox;var origin=text.Origin;double angle=text.Rotation;
        double c=Math.Cos(angle),s=Math.Sin(angle);var result=new JArray();
        foreach(var point in new[]{box.MinPoint,newPoint(box.MaxPoint.X,box.MinPoint.Y),box.MaxPoint,newPoint(box.MinPoint.X,box.MaxPoint.Y)})
        {
            double x=point.X-origin.X,y=point.Y-origin.Y;
            result.Add(UnitConvert.CmToMm(origin.X+c*x-s*y));result.Add(UnitConvert.CmToMm(origin.Y+s*x+c*y));
        }
        SketchTextGlyphMeshContract.ValidateBounds(result);return result;
        Point2d newPoint(double x,double y){var p=box.MinPoint.Copy();p.X=x;p.Y=y;return p;}
    }
    internal static JObject Capture(InventorCommandContext ctx,global::Inventor.Document doc,TextBox text,ref int points,ref int triangles)
    {
        X.Deadline(ctx,"capturing native text glyphs");
        string revision=doc.DatabaseRevisionId;bool dirty=doc.Dirty;
        if(text.UseBackgroundColor) throw new InvalidOperationException("Native text background presentation is not supported yet.");
        int count=0,faces=0;double[] coordinates=Array.Empty<double>();int[] indices=Array.Empty<int>();
        text.GetFacetsInfo(out count,out faces,out coordinates,out indices);
        if(count<0||count>8192||faces<0||faces>32768||coordinates.Length!=count*3||indices.Length!=faces*3
            ||count>100000-points||faces>200000-triangles||coordinates.Any(v=>double.IsNaN(v)||double.IsInfinity(v))
            ||indices.Any(i=>i<1||i>count)) throw new InvalidOperationException("Native text glyph budget or geometry is invalid.");
        byte red=0,green=0,blue=0;text.GetColor(out red,out green,out blue);
        var mesh=new JObject{["schema"]=1,["coordinate_space"]="model_mm",["vertices_mm"]=new JArray(coordinates.Select(UnitConvert.CmToMm)),
            ["indices"]=new JArray(indices.Select(i=>i-1)),["color_rgb"]=new JArray((int)red,(int)green,(int)blue)};
        SketchTextGlyphMeshContract.Validate(mesh);
        X.Deadline(ctx,"validating native text glyphs");
        if(doc.DatabaseRevisionId!=revision||doc.Dirty!=dirty) throw new InvalidOperationException("Native glyph capture changed the document.");
        points+=count;triangles+=faces;return mesh;
    }
}
#endif
