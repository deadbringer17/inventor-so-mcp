using System;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Infrastructure;

/// <summary>
/// A requested assembly constraint. Each type needs its own measurement, and the type decides what
/// kind of geometry the ids must resolve to, so the parsing is explicit rather than inferred:
/// Inventor will happily infer an axis from a face and produce a constraint nobody asked for.
/// </summary>
public sealed class ConstraintCreateRequest
{
    /// <summary>mate and flush join planes; mate_axis and insert join axes; angle and tangent orient.</summary>
    public static readonly string[] Types = { "mate", "flush", "mate_axis", "insert", "angle", "tangent" };

    public string Type { get; private set; } = "";
    public string FaceA { get; private set; } = "";
    public string FaceB { get; private set; } = "";
    public double OffsetMm { get; private set; }
    public double ClearanceMm { get; private set; }
    public bool Preview { get; private set; }
    public double AngleDegrees { get; private set; }
    public bool AxesOpposed { get; private set; }
    public bool InsideTangency { get; private set; }

    /// <summary>True when the two ids must be edges (circles) rather than faces.</summary>
    public bool NeedsEdges => Type == "insert";

    /// <summary>True when the faces must be planar; the axis types need cylinders instead.</summary>
    public bool NeedsPlanarFaces => Type == "mate" || Type == "flush";

    public static ConstraintCreateRequest Parse(JObject p)
    {
        string Text(string key, string? alternate = null)
        {
            foreach (var candidate in alternate == null ? new[] { key } : new[] { key, alternate })
                if (p[candidate]?.Type == JTokenType.String && !string.IsNullOrWhiteSpace((string?)p[candidate]))
                    return (string)p[candidate]!;
            throw new ArgumentException(key + " must be a nonempty string.");
        }
        double Number(string key)
        {
            var t = p[key];
            if (t == null || (t.Type != JTokenType.Float && t.Type != JTokenType.Integer)) throw new ArgumentException(key + " must be numeric.");
            double value = (double)t;
            if (double.IsNaN(value) || double.IsInfinity(value)) throw new ArgumentException(key + " must be finite.");
            return value;
        }
        bool Flag(string key, bool fallback)
        {
            var t = p[key];
            if (t == null || t.Type == JTokenType.Null) return fallback;
            if (t.Type != JTokenType.Boolean) throw new ArgumentException(key + " must be boolean.");
            return (bool)t;
        }

        var type = Text("type");
        if (Array.IndexOf(Types, type) < 0)
            throw new ArgumentException("type must be one of " + string.Join(", ", Types) + ".");
        var preview = p["preview"];
        if (preview != null && preview.Type != JTokenType.Boolean) throw new ArgumentException("preview must be boolean.");

        var result = new ConstraintCreateRequest
        {
            Type = type,
            // entity_a_id/entity_b_id name the general case; face_a_id/face_b_id stay valid.
            FaceA = Text("face_a_id", "entity_a_id"),
            FaceB = Text("face_b_id", "entity_b_id"),
            ClearanceMm = Number("minimum_clearance_mm"),
            Preview = (bool?)preview ?? true,
            AxesOpposed = Flag("axes_opposed", true),
            InsideTangency = Flag("inside", false),
        };

        if (type == "angle")
        {
            result.AngleDegrees = Number("angle_degrees");
            if (result.AngleDegrees <= -360 || result.AngleDegrees >= 360)
                throw new ArgumentException("angle_degrees must be between -360 and 360, exclusive.");
            if (p["offset_mm"] != null && p["offset_mm"]!.Type != JTokenType.Null)
                throw new ArgumentException("angle takes angle_degrees, not offset_mm.");
        }
        else
        {
            result.OffsetMm = Number("offset_mm");
            if (p["angle_degrees"] != null && p["angle_degrees"]!.Type != JTokenType.Null)
                throw new ArgumentException("angle_degrees applies to the angle constraint only.");
        }

        if (result.ClearanceMm < 0) throw new ArgumentException("minimum_clearance_mm cannot be negative.");
        if (result.FaceA == result.FaceB) throw new ArgumentException("Two distinct entity references are required.");
        return result;
    }
}
