using System;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Infrastructure;

public sealed class ConstraintCreateRequest
{
    public string Type { get; private set; } = "";
    public string FaceA { get; private set; } = "";
    public string FaceB { get; private set; } = "";
    public double OffsetMm { get; private set; }
    public double ClearanceMm { get; private set; }
    public bool Preview { get; private set; }

    public static ConstraintCreateRequest Parse(JObject p)
    {
        string Text(string key) => p[key]?.Type == JTokenType.String && !string.IsNullOrWhiteSpace((string?)p[key])
            ? (string)p[key]! : throw new ArgumentException(key + " must be a nonempty string.");
        double Number(string key)
        {
            var t = p[key];
            if (t == null || (t.Type != JTokenType.Float && t.Type != JTokenType.Integer)) throw new ArgumentException(key + " must be numeric.");
            double value = (double)t;
            if (double.IsNaN(value) || double.IsInfinity(value)) throw new ArgumentException(key + " must be finite.");
            return value;
        }
        var type = Text("type");
        if (type != "mate" && type != "flush") throw new ArgumentException("Only planar mate and flush creation is supported.");
        var preview = p["preview"];
        if (preview != null && preview.Type != JTokenType.Boolean) throw new ArgumentException("preview must be boolean.");
        var result = new ConstraintCreateRequest { Type = type, FaceA = Text("face_a_id"), FaceB = Text("face_b_id"),
            OffsetMm = Number("offset_mm"), ClearanceMm = Number("minimum_clearance_mm"), Preview = (bool?)preview ?? true };
        if (result.ClearanceMm < 0) throw new ArgumentException("minimum_clearance_mm cannot be negative.");
        if (result.FaceA == result.FaceB) throw new ArgumentException("Two distinct face references are required.");
        return result;
    }
}
