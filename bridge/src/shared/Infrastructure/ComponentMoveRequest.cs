using System;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Infrastructure;

public sealed class ComponentMoveRequest
{
    public double X { get; private set; }
    public double Y { get; private set; }
    public double Z { get; private set; }
    public double Clearance { get; private set; }
    public bool Preview { get; private set; }
    public double[]? RotationAxis { get; private set; }
    public double[]? RotationCenterMm { get; private set; }
    public double RotationDegrees { get; private set; }
    public static ComponentMoveRequest Parse(JObject p)
    {
        if (p["translation_mm"] is not JArray a || a.Count != 3) throw new ArgumentException("translation_mm requires [x,y,z].");
        double Number(JToken? t)
        {
            if (t == null || (t.Type != JTokenType.Float && t.Type != JTokenType.Integer)) throw new ArgumentException("Numeric values required.");
            double value = (double)t;
            if (double.IsNaN(value) || double.IsInfinity(value)) throw new ArgumentException("Finite values required.");
            return value;
        }
        var result = new ComponentMoveRequest { X = Number(a[0]), Y = Number(a[1]), Z = Number(a[2]),
            Clearance = Number(p["minimum_clearance_mm"]), Preview = (bool?)p["preview"] ?? true };
        if (result.Clearance < 0) throw new ArgumentException("minimum_clearance_mm cannot be negative.");
        bool Present(string key) => p[key] != null && p[key]!.Type != JTokenType.Null;
        if (Present("rotation_axis") || Present("rotation_center_mm") || Present("rotation_degrees"))
        {
            if (p["rotation_axis"] is not JArray axis || axis.Count != 3 ||
                p["rotation_center_mm"] is not JArray center || center.Count != 3)
                throw new ArgumentException("Rotation requires axis[3], center_mm[3] and degrees.");
            result.RotationAxis = new[] { Number(axis[0]), Number(axis[1]), Number(axis[2]) };
            result.RotationCenterMm = new[] { Number(center[0]), Number(center[1]), Number(center[2]) };
            result.RotationDegrees = Number(p["rotation_degrees"]);
            var r = result.RotationAxis;
            double scale = Math.Max(Math.Abs(r[0]), Math.Max(Math.Abs(r[1]), Math.Abs(r[2])));
            if (scale == 0) throw new ArgumentException("Rotation axis cannot be zero.");
            for (int i = 0; i < 3; i++) r[i] /= scale;
            double length = Math.Sqrt(r[0]*r[0] + r[1]*r[1] + r[2]*r[2]);
            for (int i = 0; i < 3; i++) r[i] /= length;
        }
        return result;
    }
}
