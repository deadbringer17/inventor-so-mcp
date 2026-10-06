#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Handlers;
using Inv = global::Inventor;

namespace Bimwright.Ipt.Shared.Handlers.Assembly;

public static class FaceSelector
{
    private static readonly Dictionary<string, (double x, double y, double z)> Directions = new(StringComparer.OrdinalIgnoreCase)
    {
        { "+X", (1, 0, 0) }, { "-X", (-1, 0, 0) },
        { "+Y", (0, 1, 0) }, { "-Y", (0, -1, 0) },
        { "+Z", (0, 0, 1) }, { "-Z", (0, 0, -1) }
    };

    public static Inv.Face SelectFace(Inv.PartComponentDefinition def, FaceSelectorSpec spec, out JArray candidatesJson)
    {
        candidatesJson = new JArray();
        var candidates = new List<Inv.Face>();

        foreach (Inv.SurfaceBody sb in def.SurfaceBodies)
        {
            foreach (Inv.Face f in sb.Faces)
            {
                if (spec.Kind == "planar" && f.SurfaceType == Inv.SurfaceTypeEnum.kPlaneSurface)
                {
                    candidates.Add(f);
                }
                else if (spec.Kind == "cylindrical" && f.SurfaceType == Inv.SurfaceTypeEnum.kCylinderSurface)
                {
                    candidates.Add(f);
                }
            }
        }

        // Apply filters depending on kind
        if (spec.Kind == "planar")
        {
            candidates = FilterPlanar(candidates, spec);
        }
        else
        {
            candidates = FilterCylindrical(candidates, spec);
        }

        // Each centroid costs several COM reads; the tie-break and the candidate list share one read.
        var centroids = new Dictionary<Inv.Face, double[]>();
        double[] CentroidOf(Inv.Face face)
        {
            if (!centroids.TryGetValue(face, out var value)) centroids[face] = value = GetCentroidMm(face);
            return value;
        }

        // Apply near_mm tie-break if more than one remains and near_mm is provided
        if (candidates.Count > 1 && spec.NearMm != null)
        {
            double minDistance = double.MaxValue;
            Inv.Face? bestFace = null;

            foreach (var f in candidates)
            {
                var centroid = CentroidOf(f);
                double dx = centroid[0] - spec.NearMm[0];
                double dy = centroid[1] - spec.NearMm[1];
                double dz = centroid[2] - spec.NearMm[2];
                double dist = Math.Sqrt(dx * dx + dy * dy + dz * dz);

                if (dist < minDistance)
                {
                    minDistance = dist;
                    bestFace = f;
                }
            }

            if (bestFace != null)
            {
                candidates = new List<Inv.Face> { bestFace };
            }
        }

        // If not exactly 1 candidate, serialize candidate list for error payload
        if (candidates.Count != 1)
        {
            foreach (var f in candidates)
            {
                var centroid = CentroidOf(f);
                double area = UnitConvert.Cm2ToMm2(f.Evaluator.Area);
                var item = new JObject
                {
                    ["kind"] = spec.Kind,
                    ["centroid_mm"] = new JArray(centroid[0], centroid[1], centroid[2]),
                    ["area_mm2"] = area
                };

                if (spec.Kind == "planar")
                {
                    var normal = GetPlanarNormal(f, (Inv.Plane)f.Geometry);
                    item["normal"] = new JArray(normal.x, normal.y, normal.z);
                }
                else
                {
                    var cyl = (Inv.Cylinder)f.Geometry;
                    var axis = cyl.AxisVector;
                    item["radius_mm"] = UnitConvert.CmToMm(cyl.Radius);
                    item["axis"] = new JArray(axis.X, axis.Y, axis.Z);
                }

                candidatesJson.Add(item);
            }
            return null!;
        }

        return candidates[0];
    }

    private static List<Inv.Face> FilterPlanar(List<Inv.Face> faces, FaceSelectorSpec spec)
    {
        var targetDir = Directions[spec.Direction!];
        var matchedNormals = new List<(Inv.Face face, double posMm)>();

        // 1. Filter by Normal within tolerance
        foreach (var f in faces)
        {
            var plane = (Inv.Plane)f.Geometry;
            var normal = GetPlanarNormal(f, plane);
            double dot = normal.x * targetDir.x + normal.y * targetDir.y + normal.z * targetDir.z;
            double angleRad = Math.Acos(Math.Max(-1.0, Math.Min(1.0, dot)));
            double angleDeg = UnitConvert.RadToDeg(angleRad);

            if (angleDeg <= spec.ToleranceDeg)
            {
                var basePt = plane.RootPoint;
                double posCm = basePt.X * targetDir.x + basePt.Y * targetDir.y + basePt.Z * targetDir.z;
                matchedNormals.Add((f, UnitConvert.CmToMm(posCm)));
            }
        }

        if (matchedNormals.Count == 0) return new List<Inv.Face>();

        // 2. Filter by Extreme position along normal axis within 0.001 mm
        double extremeVal = spec.Extreme == "max"
            ? matchedNormals.Max(x => x.posMm)
            : matchedNormals.Min(x => x.posMm);

        return matchedNormals
            .Where(x => Math.Abs(x.posMm - extremeVal) <= 0.001)
            .Select(x => x.face)
            .ToList();
    }

    private static List<Inv.Face> FilterCylindrical(List<Inv.Face> faces, FaceSelectorSpec spec)
    {
        var matched = new List<Inv.Face>();

        foreach (var f in faces)
        {
            var cyl = (Inv.Cylinder)f.Geometry;
            double radiusMm = UnitConvert.CmToMm(cyl.Radius);

            if (Math.Abs(radiusMm - spec.RadiusMm!.Value) <= spec.RadiusTolMm)
            {
                // Optional axis filter
                if (spec.Direction != null)
                {
                    var targetDir = Directions[spec.Direction];
                    var axis = cyl.AxisVector;
                    double dot = Math.Abs(axis.X * targetDir.x + axis.Y * targetDir.y + axis.Z * targetDir.z);
                    double angleRad = Math.Acos(Math.Max(0.0, Math.Min(1.0, dot)));
                    double angleDeg = UnitConvert.RadToDeg(angleRad);

                    if (angleDeg <= spec.ToleranceDeg)
                    {
                        matched.Add(f);
                    }
                }
                else
                {
                    matched.Add(f);
                }
            }
        }

        return matched;
    }

    private static (double x, double y, double z) GetPlanarNormal(Inv.Face f, Inv.Plane plane)
    {
        var normalVector = plane.Normal;
        double nx = normalVector.X;
        double ny = normalVector.Y;
        double nz = normalVector.Z;

        if (f.IsParamReversed)
        {
            nx = -nx;
            ny = -ny;
            nz = -nz;
        }

        return (nx, ny, nz);
    }

    private static double[] GetCentroidMm(Inv.Face f)
    {
        var range = f.Evaluator.RangeBox;
        var min = range.MinPoint;
        var max = range.MaxPoint;
        return new double[]
        {
            UnitConvert.CmToMm((min.X + max.X) / 2.0),
            UnitConvert.CmToMm((min.Y + max.Y) / 2.0),
            UnitConvert.CmToMm((min.Z + max.Z) / 2.0)
        };
    }
}
#endif
