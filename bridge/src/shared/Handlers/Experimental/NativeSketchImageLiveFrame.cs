#if INVENTOR2027 && SO_EXPERIMENTAL
using System;
using System.Collections.Generic;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;
using Inventor;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Handlers.Experimental;

/// <summary>
/// Current UV corner frame from Inventor 2027 boundary edge roles, observed in native research50.
/// Edge roles 0/1/2/3 are bitmap top/bottom/left/right even after mirror and picture rotation.
/// This empirical correspondence requires live verification for each supported native operation;
/// it is independent of persistent original-source corners and of Width/Height picture properties.
/// </summary>
internal static class NativeSketchImageLiveFrame
{
    private const double ToleranceMm = 1e-5;

    internal static JObject CaptureCurrent(SketchImage image)
    {
        try
        {
            if (image == null || image.LinkedToFile) throw Invalid();
            var edges = new List<SketchLine>();
            foreach (SketchEntity entity in image.BoundaryGeometry)
            {
                if (entity is not SketchLine line || edges.Count >= 4) throw Invalid();
                foreach (SketchLine previous in edges) if (Same(previous, line)) throw Invalid();
                if (Same(line.StartSketchPoint, line.EndSketchPoint)) throw Invalid();
                edges.Add(line);
            }
            if (edges.Count != 4) throw Invalid();
            SketchPoint Intersection(int a, int b)
            {
                SketchPoint? intersection = null;
                foreach (SketchPoint point in new[] { edges[a].StartSketchPoint, edges[a].EndSketchPoint })
                    if (Same(point, edges[b].StartSketchPoint) || Same(point, edges[b].EndSketchPoint))
                    {
                        if (intersection != null) throw Invalid();
                        intersection = point;
                    }
                return intersection ?? throw Invalid();
            }
            var corners = new[] { Intersection(0, 2), Intersection(0, 3), Intersection(1, 3), Intersection(1, 2) };
            var coordinates = new double[4][];
            for (int i = 0; i < 4; i++)
            {
                for (int j = 0; j < i; j++) if (Same(corners[i], corners[j])) throw Invalid();
                var geometry = corners[i].Geometry;
                double x = geometry.X * 10, y = geometry.Y * 10;
                if (!double.IsFinite(x) || !double.IsFinite(y)) throw Invalid();
                coordinates[i] = new[] { x, y };
            }
            // All endpoints must belong to those exact four corners, with degree two.
            foreach (SketchPoint point in corners)
            {
                int incidence = 0;
                foreach (SketchLine edge in edges)
                    if (Same(point, edge.StartSketchPoint) || Same(point, edge.EndSketchPoint)) incidence++;
                if (incidence != 2) throw Invalid();
            }
            foreach (SketchLine edge in edges)
            {
                int endpoints = 0;
                foreach (SketchPoint corner in corners)
                {
                    if (Same(corner, edge.StartSketchPoint)) endpoints++;
                    if (Same(corner, edge.EndSketchPoint)) endpoints++;
                }
                if (endpoints != 2) throw Invalid();
            }
            double ux = coordinates[1][0] - coordinates[0][0], uy = coordinates[1][1] - coordinates[0][1];
            double vx = coordinates[3][0] - coordinates[0][0], vy = coordinates[3][1] - coordinates[0][1];
            double width = Math.Sqrt(ux * ux + uy * uy), height = Math.Sqrt(vx * vx + vy * vy);
            if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0) throw Invalid();
            Near(coordinates[2][0], coordinates[0][0] + ux + vx);
            Near(coordinates[2][1], coordinates[0][1] + uy + vy);
            Near(ux / width * vx + uy / width * vy, 0);
            Near(vx / height * ux + vy / height * uy, 0);
            var points = new JArray();
            foreach (double[] point in coordinates) points.Add(new JArray(point));
            var frame = new JObject { ["schema"] = 1, ["provenance"] = "native_boundary_roles",
                ["corner_order"] = new JArray("top_left", "top_right", "bottom_right", "bottom_left"),
                ["corners_mm"] = points };
            return new JObject { ["frame"] = frame, ["width_mm"] = width, ["height_mm"] = height };
        }
        catch (CodedFailureException) { throw; }
        catch (Exception error) { throw new CodedFailureException(InventorErrorCodes.API_ERROR,
            "The current native image boundary frame could not be read.", inner: error); }
    }

    private static bool Same(object left, object right) => SketchReferences.SameComObject(left, right);
    private static void Near(double actual, double expected)
    { if (!double.IsFinite(actual) || !double.IsFinite(expected) || Math.Abs(actual - expected) > ToleranceMm) throw Invalid(); }
    private static CodedFailureException Invalid() => new(InventorErrorCodes.INVALID_ARGUMENT,
        "The current native image boundary roles or rectangular frame are unavailable.");
}
#endif
