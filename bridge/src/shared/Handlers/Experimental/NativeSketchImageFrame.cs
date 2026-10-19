#if INVENTOR2027 && SO_EXPERIMENTAL
using System;
using System.Collections.Generic;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;
using Inventor;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Handlers.Experimental;

/// <summary>
/// Persistent original UV-corner identities for our inserted images. Write must run immediately
/// after initial sizing and before rotation, inside the caller's owned transaction. Read binds actual
/// native point keys and never sorts corners by coordinates or trusts boundary enumeration order.
/// Save-copy key compatibility and native bitmap replacement/flip need separate verification.
/// </summary>
internal static class NativeSketchImageFrame
{
    internal const string AttributeSetName = "InventorSO_SketchImageFrame";
    private const double ToleranceMm = 1e-5;
    private static readonly string[] CornerNames = { "top_left", "top_right", "bottom_right", "bottom_left" };

    internal static void Write(SketchImage image)
    {
        try
        {
            if (image == null || image.LinkedToFile || FindSet(image) != null) throw Invalid();
            List<SketchLine> edges = Edges(image);
            // API research45 establishes the first directed raw-Add edge as original TL -> TR.
            // This is used only during capture, and verified against raw Position/Width/Height.
            SketchPoint tl = edges[0].StartSketchPoint, tr = edges[0].EndSketchPoint;
            SketchPoint Next(SketchPoint start)
            {
                SketchPoint? end = null;
                foreach (SketchLine edge in edges)
                    if (Same(edge.StartSketchPoint, start))
                    { if (end != null) throw Invalid(); end = edge.EndSketchPoint; }
                return end ?? throw Invalid();
            }
            SketchPoint br = Next(tr), bl = Next(br);
            if (!Same(Next(bl), tl)) throw Invalid();
            var corners = new[] { tl, tr, br, bl };
            Validate(image, edges, corners);
            double[] origin = Point(tl), right = Point(tr), bottom = Point(bl);
            var position = image.Position;
            Near(origin[0], position.X * 10); Near(origin[1], position.Y * 10);
            Near(right[0] - origin[0], image.Width * 10); Near(right[1], origin[1]);
            Near(bottom[0], origin[0]); Near(bottom[1] - origin[1], -image.Height * 10);
            var keys = new string[4];
            for (int i = 0; i < corners.Length; i++)
            {
                byte[] key = Array.Empty<byte>(); corners[i].GetReferenceKey(ref key, 0);
                if (key.Length < 1 || key.Length > 3072) throw Invalid();
                keys[i] = Convert.ToBase64String(key);
                for (int j = 0; j < i; j++) if (keys[j] == keys[i]) throw Invalid();
            }
            AttributeSet set = image.AttributeSets.Add(AttributeSetName, true);
            set.Add("schema", ValueTypeEnum.kIntegerType, 1);
            for (int i = 0; i < keys.Length; i++) set.Add(CornerNames[i], ValueTypeEnum.kStringType, keys[i]);
        }
        catch (CodedFailureException) { throw; }
        catch (Exception error) { throw Api(error); }
    }

    internal static JObject? Read(global::Inventor.Document doc, SketchImage image)
    {
        try
        {
            if (doc == null || image == null) throw Invalid();
            AttributeSet? set = FindSet(image);
            if (set == null) return null;
            if (image.LinkedToFile || set.Transient || set.Count != 5) throw Invalid();
            var fields = new Dictionary<string, Inventor.Attribute>(StringComparer.Ordinal);
            foreach (Inventor.Attribute field in set) if (!fields.TryAdd(field.Name, field)) throw Invalid();
            if (!fields.TryGetValue("schema", out Inventor.Attribute? schema)
                || schema.ValueType != ValueTypeEnum.kIntegerType || schema.Value is not int version || version != 1) throw Invalid();
            var corners = new SketchPoint[4];
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var edges = Edges(image);
            for (int i = 0; i < corners.Length; i++)
            {
                if (!fields.TryGetValue(CornerNames[i], out Inventor.Attribute? field)
                    || field.ValueType != ValueTypeEnum.kStringType || field.Value is not string encoded
                    || encoded.Length < 1 || encoded.Length > 4096 || !seen.Add(encoded)) throw Invalid();
                byte[] key;
                try { key = Convert.FromBase64String(encoded); }
                catch (FormatException) { throw Invalid(); }
                if (key.Length == 0 || Convert.ToBase64String(key) != encoded) throw Invalid();
                // Inventor exposes keys for image-boundary points but its global resolver rejects
                // them (E_INVALIDARG, native probe49). Match only exact current native keys within
                // this image's bounded boundary; never substitute a point by position or index.
                SketchPoint? matched = null;
                foreach (SketchLine edge in edges)
                    foreach (SketchPoint candidate in new[] { edge.StartSketchPoint, edge.EndSketchPoint })
                    {
                        byte[] current = Array.Empty<byte>(); candidate.GetReferenceKey(ref current, 0);
                        if (Convert.ToBase64String(current) != encoded) continue;
                        if (matched != null && !Same(matched, candidate)) throw Invalid();
                        matched = candidate;
                    }
                corners[i] = matched ?? throw Invalid();
            }
            Validate(image, edges, corners);
            var coordinates = new JArray();
            foreach (SketchPoint point in corners) coordinates.Add(new JArray(Point(point)));
            return new JObject { ["schema"] = 1, ["provenance"] = "native_corner_reference_keys",
                ["corner_order"] = new JArray("top_left", "top_right", "bottom_right", "bottom_left"),
                ["corners_mm"] = coordinates };
        }
        catch (CodedFailureException) { throw; }
        catch (Exception error) { throw Api(error); }
    }

    private static List<SketchLine> Edges(SketchImage image)
    {
        var edges = new List<SketchLine>();
        foreach (SketchEntity entity in image.BoundaryGeometry)
        {
            if (entity is not SketchLine line || edges.Count >= 4) throw Invalid();
            foreach (SketchLine previous in edges) if (Same(previous, line)) throw Invalid();
            edges.Add(line);
        }
        if (edges.Count != 4) throw Invalid();
        return edges;
    }

    private static void Validate(SketchImage image, List<SketchLine> edges, SketchPoint[] corners)
    {
        var coordinates = new double[4][];
        for (int i = 0; i < 4; i++)
        {
            for (int j = 0; j < i; j++) if (Same(corners[i], corners[j])) throw Invalid();
            coordinates[i] = Point(corners[i]);
            int matches = 0;
            foreach (SketchLine edge in edges)
            {
                SketchPoint next = corners[(i + 1) % 4];
                if ((Same(edge.StartSketchPoint, corners[i]) && Same(edge.EndSketchPoint, next))
                    || (Same(edge.EndSketchPoint, corners[i]) && Same(edge.StartSketchPoint, next))) matches++;
            }
            // Every bound point and side must be part of THIS image's exact native boundary.
            if (matches != 1) throw Invalid();
        }
        double ux = coordinates[1][0] - coordinates[0][0], uy = coordinates[1][1] - coordinates[0][1];
        double vx = coordinates[3][0] - coordinates[0][0], vy = coordinates[3][1] - coordinates[0][1];
        double width = Math.Sqrt(ux * ux + uy * uy), height = Math.Sqrt(vx * vx + vy * vy);
        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0) throw Invalid();
        Near(width, image.Width * 10); Near(height, image.Height * 10);
        Near(coordinates[2][0], coordinates[0][0] + ux + vx);
        Near(coordinates[2][1], coordinates[0][1] + uy + vy);
        // Projection in mm avoids a scale-dependent squared-length tolerance.
        Near(ux / width * vx + uy / width * vy, 0);
        Near(vx / height * ux + vy / height * uy, 0);
    }

    private static double[] Point(SketchPoint point)
    {
        var geometry = point.Geometry;
        double x = geometry.X * 10, y = geometry.Y * 10;
        if (!double.IsFinite(x) || !double.IsFinite(y)) throw Invalid();
        return new[] { x, y };
    }
    private static AttributeSet? FindSet(SketchImage image)
    {
        AttributeSet? found = null;
        foreach (AttributeSet candidate in image.AttributeSets)
        {
            if (!string.Equals(candidate.Name, AttributeSetName, StringComparison.OrdinalIgnoreCase)) continue;
            if (found != null || !string.Equals(candidate.Name, AttributeSetName, StringComparison.Ordinal)) throw Invalid();
            found = candidate;
        }
        return found;
    }
    private static bool Same(object left, object right) => SketchReferences.SameComObject(left, right);
    private static void Near(double actual, double expected)
    { if (!double.IsFinite(actual) || !double.IsFinite(expected) || Math.Abs(actual - expected) > ToleranceMm) throw Invalid(); }
    private static CodedFailureException Invalid() => new(InventorErrorCodes.INVALID_ARGUMENT,
        "Native image corner identities or frame geometry are invalid or unavailable.");
    private static CodedFailureException Api(Exception error) => new(InventorErrorCodes.API_ERROR,
        "The persistent native image corner frame could not be accessed.", inner: error);
}
#endif
