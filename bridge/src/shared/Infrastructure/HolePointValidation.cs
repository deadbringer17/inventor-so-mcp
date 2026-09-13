using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Infrastructure;

public static class HolePointValidation
{
    public static void Validate(JArray points)
    {
        if (points.Count < 1 || points.Count > 256)
            throw new ArgumentException("points_mm must contain 1 to 256 points.");
        var seen = new HashSet<(double, double, double)>();
        foreach (var token in points)
        {
            if (token is not JArray point || point.Count != 3)
                throw new ArgumentException("Each point must be a numeric [x,y,z] array in mm.");
            var values = new double[3];
            for (int i = 0; i < 3; i++)
            {
                if (point[i].Type != JTokenType.Integer && point[i].Type != JTokenType.Float)
                    throw new ArgumentException("Point coordinates must be numbers, not strings or null.");
                values[i] = (double)point[i];
                if (double.IsNaN(values[i]) || double.IsInfinity(values[i]))
                    throw new ArgumentException("Point coordinates must be finite.");
            }
            if (!seen.Add((values[0], values[1], values[2])))
                throw new ArgumentException("Duplicate hole centers are not allowed.");
        }
    }
}
