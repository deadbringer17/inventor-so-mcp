#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using Inventor;
using System;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Handlers.Sketch;

internal static class SketchDimensions
{
    public static Point2d Text(Application app, JObject p, Point2d fallback)
    {
        if (p["text_x"] == null && p["text_y"] == null) return fallback;
        if (p["text_x"] == null || p["text_y"] == null) throw new ArgumentException("text_x and text_y must be supplied together.");
        double x = p.Value<double>("text_x"), y = p.Value<double>("text_y");
        if (double.IsNaN(x) || double.IsInfinity(x) || double.IsNaN(y) || double.IsInfinity(y)) throw new ArgumentException("Dimension text coordinates must be finite.");
        return SketchSupport.Pt(app,x,y);
    }
    public static string Length(Application app, PlanarSketch sketch, SketchLine line, JObject p)
    {
        var a = line.StartSketchPoint.Geometry; var b = line.EndSketchPoint.Geometry;
        var text = app.TransientGeometry.CreatePoint2d((a.X+b.X)/2+0.5,(a.Y+b.Y)/2+0.5);
        return sketch.DimensionConstraints.AddTwoPointDistance(line.StartSketchPoint,line.EndSketchPoint,
            DimensionOrientationEnum.kAlignedDim,Text(app,p,text),false).Parameter.Name;
    }
}
#endif
