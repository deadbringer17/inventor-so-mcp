using Inventor;
using Newtonsoft.Json.Linq;

internal static partial class Program
{
    // Independent theory: raw Add's Position/Width/Height define the source rectangle. This
    // oracle never calls the product role-intersection helper or sorts its resulting corners.
    private static double[][] RawImageCorners51(SketchImage image)
    {
        double x = image.Position.X * 10, y = image.Position.Y * 10;
        double width = image.Width * 10, height = image.Height * 10;
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(width)
            || !double.IsFinite(height) || width <= 0 || height <= 0)
            throw new InvalidOperationException("Raw fixture image dimensions are invalid.");
        return new[] { new[] { x, y }, new[] { x + width, y }, new[] { x + width, y - height }, new[] { x, y - height } };
    }

    private static double[][] ExpectedImageCorners51(double[][] raw, string operation)
    {
        int[] permutation = operation switch
        {
            "raw" or "geometry_rotate37" => new[] { 0, 1, 2, 3 },
            "mirror_horizontal" or "geometry37_mirrorH" => new[] { 1, 0, 3, 2 },
            "mirror_vertical" => new[] { 3, 2, 1, 0 },
            "rotate_left" => new[] { 3, 0, 1, 2 },
            "mirror_hv" or "rotate_left_twice" => new[] { 2, 3, 0, 1 },
            _ => throw new InvalidOperationException("Unknown independent image frame operation.")
        };
        var expected = permutation.Select(index => (double[])raw[index].Clone()).ToArray();
        if (operation is "geometry_rotate37" or "geometry37_mirrorH")
        {
            double angle = 37 * Math.PI / 180, cosine = Math.Cos(angle), sine = Math.Sin(angle);
            foreach (double[] point in expected)
            {
                double x = point[0] - raw[0][0], y = point[1] - raw[0][1];
                point[0] = raw[0][0] + x * cosine - y * sine;
                point[1] = raw[0][1] + x * sine + y * cosine;
            }
        }
        return expected;
    }

    private static void VerifyLiveImageFrame51(JObject manifest, double[][] raw, string operation, bool visible, JObject row)
    {
        double[][] expected = ExpectedImageCorners51(raw, operation);
        var expectedJson = new JArray(); foreach (double[] point in expected) expectedJson.Add(new JArray(point));
        row["frame_expected_corners_mm"] = expectedJson;
        if (manifest["image_frame"] is not JObject frame || (int?)frame["schema"] != 1
            || (string?)frame["provenance"] != "native_boundary_roles"
            || !JToken.DeepEquals(frame["corner_order"], new JArray("top_left", "top_right", "bottom_right", "bottom_left"))
            || frame["corners_mm"] is not JArray corners || corners.Count != 4)
            throw new InvalidOperationException("Native image live frame schema or provenance differs.");
        void Near(double actual, double target)
        {
            if (!double.IsFinite(actual) || !double.IsFinite(target) || Math.Abs(actual - target) > 1e-5)
                throw new InvalidOperationException("Native image live frame differs from independent operation theory.");
        }
        for (int i = 0; i < 4; i++)
        {
            if (corners[i] is not JArray point || point.Count != 2
                || point.Any(value => value.Type is not (JTokenType.Integer or JTokenType.Float)))
                throw new InvalidOperationException("Native image live frame coordinate schema differs.");
            Near((double)point[0], expected[i][0]); Near((double)point[1], expected[i][1]);
        }
        double Length(int a, int b) => Math.Sqrt(Math.Pow(expected[b][0] - expected[a][0], 2)
            + Math.Pow(expected[b][1] - expected[a][1], 2));
        if (manifest["width_mm"]?.Type is not (JTokenType.Float or JTokenType.Integer)
            || manifest["height_mm"]?.Type is not (JTokenType.Float or JTokenType.Integer))
            throw new InvalidOperationException("Native image semantic dimensions are unavailable.");
        Near((double)manifest["width_mm"]!, Length(0, 1)); Near((double)manifest["height_mm"]!, Length(0, 3));
        if ((string?)manifest["source_mode"] != "native_bitmap" || manifest["visible"]?.Type != JTokenType.Boolean
            || (bool)manifest["visible"]! != visible || manifest["transparent"]?.Type != JTokenType.Boolean
            || (bool)manifest["transparent"]!) throw new InvalidOperationException("Native image bitmap/frame flags differ.");
        row["frame_theory_match"] = true;
        row["opaque_metadata_match"] = true;
    }
}
