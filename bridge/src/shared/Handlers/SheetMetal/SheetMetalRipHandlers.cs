#if INVENTOR2027
using System;
using System.Linq;
using Inventor;
using Newtonsoft.Json.Linq;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;

namespace Bimwright.Ipt.Shared.Handlers.SheetMetal;

/// <summary>
/// <c>sheet_metal_rip</c> — cut a gap in a wall so a closed profile can be unfolded. Three rip types,
/// each with its own inputs: <c>face_extents</c> rips the whole face and takes no gap, while
/// <c>single_point</c> and <c>point_to_point</c> rip from sketch points and take a gap and the side it
/// opens on. The type is explicit because Inventor's default depends on what happens to be selected.
/// </summary>
public sealed class SheetMetalRipHandler : HandlerBase, IInventorCommand
{
    public string Name => "sheet_metal_rip";
    public bool IsReadOnly => false;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        if (!SheetMetalSupport.TryGetDefinition(ctx, Name, out _, out var def, out var failure)) return failure!;
        string faceId = (string?)p["face_id"] ?? "";
        if (string.IsNullOrWhiteSpace(faceId))
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT,
                "face_id is required: the wall face to rip, as a portable id from inventor_list_topology.");
        string type = ((string?)p["rip_type"] ?? "face_extents").Trim().ToLowerInvariant();
        if (type != "face_extents" && type != "single_point" && type != "point_to_point")
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "rip_type must be face_extents, single_point or point_to_point.");
        var side = ((string?)p["gap_side"] ?? "positive").Trim().ToLowerInvariant() switch
        {
            "positive" => PartFeatureExtentDirectionEnum.kPositiveExtentDirection,
            "negative" => PartFeatureExtentDirectionEnum.kNegativeExtentDirection,
            "symmetric" => PartFeatureExtentDirectionEnum.kSymmetricExtentDirection,
            _ => (PartFeatureExtentDirectionEnum)0
        };
        if (side == 0)
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "gap_side must be positive, negative or symmetric.");
        try
        {
            var doc = SheetMetalSupport.OwningDocument(def);
            var face = Core.EntityReferences.ResolvePlanarPartFace(doc, faceId);
            var rips = SheetMetalSupport.Features(def).RipFeatures;
            var definition = rips.CreateRipDefinition(face);
            var applied = new JObject { ["rip_type"] = type, ["face_id"] = faceId };

            if (type == "face_extents")
            {
                // The face-extents rip takes no gap: the whole face is separated.
                definition.SetFaceExtentsRipType(face);
            }
            else
            {
                string sketchName = (string?)p["sketch_name"] ?? "";
                if (string.IsNullOrWhiteSpace(sketchName))
                    throw new ArgumentException("sketch_name is required for a point rip: the sketch holding the rip points.");
                var sketch = SheetMetalSupport.FindSketch(def, sketchName);
                var points = sketch.SketchPoints.Cast<SketchPoint>().ToArray();
                int needed = type == "single_point" ? 1 : 2;
                if (points.Length < needed)
                    throw new ArgumentException("Sketch '" + sketchName + "' needs " + needed +
                        " sketch point(s) for a " + type + " rip but has " + points.Length + ".");
                double gap = SheetMetalSupport.PositiveLength(p, "gap_mm", 0.01, 100);
                if (type == "single_point")
                    definition.SetSinglePointRipType(face, points[0], UnitConvert.MmToCm(gap), side);
                else
                    definition.SetPointToPointRipType(face, points[0], points[1], UnitConvert.MmToCm(gap), side);
                applied["gap_mm"] = gap;
                applied["gap_side"] = side.ToString();
                applied["sketch_name"] = sketchName;
                applied["points_used"] = needed;
            }

            RipFeature feature;
            try { feature = rips.Add(definition); }
            catch (Exception ex)
            {
                throw new ArgumentException("RIP_REJECTED: Inventor refused this rip. A rip needs a wall face of a " +
                    "closed sheet-metal shape, and point rips need their points on that face. Inventor reported: " + ex.Message);
            }
            SheetMetalSupport.RequireHealthy(feature.HealthStatus, "Rip");
            applied["feature_name"] = feature.Name;
            return Ok(ctx, applied);
        }
        catch (ArgumentException ex) { return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, ex.Message); }
    }
}

/// <summary>
/// <c>sheet_metal_lofted_flange</c> — a wall between two open profiles, which is how a transition
/// (round-to-square and similar) is built in sheet metal. Both profiles are named sketches; each is
/// turned into a path from its first curve, the same way the contour flange works. The output is
/// either die formed or press-brake facets, and facets need their chord tolerance.
/// </summary>
public sealed class SheetMetalLoftedFlangeHandler : HandlerBase, IInventorCommand
{
    public string Name => "sheet_metal_lofted_flange";
    public bool IsReadOnly => false;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        if (!SheetMetalSupport.TryGetDefinition(ctx, Name, out _, out var def, out var failure)) return failure!;
        string first = (string?)p["sketch_one"] ?? "";
        string second = (string?)p["sketch_two"] ?? "";
        if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second))
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "sketch_one and sketch_two are required: the two open profiles.");
        if (string.Equals(first, second, StringComparison.OrdinalIgnoreCase))
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "sketch_one and sketch_two must be different sketches.");
        string output = ((string?)p["output"] ?? "die_formed").Trim().ToLowerInvariant();
        if (output != "die_formed" && output != "press_brake")
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "output must be die_formed or press_brake.");
        try
        {
            double? tolerance = null;
            if (output == "press_brake")
                tolerance = SheetMetalSupport.PositiveLength(p, "facet_tolerance_mm", 0.001, 100);

            var features = SheetMetalSupport.Features(def);
            var pathOne = features.CreatePath(SheetMetalSupport.FirstSketchCurve(def, first));
            var pathTwo = features.CreatePath(SheetMetalSupport.FirstSketchCurve(def, second));
            var lofted = features.LoftedFlangeFeatures;
            var definition = lofted.CreateLoftedFlangeDefinition(pathOne, pathTwo);
            try
            {
                if (output == "die_formed")
                    definition.SetOutputType(LoftedFlangeOutputTypeEnum.kDieFormedLoftedFlange);
                else
                    definition.SetOutputType(LoftedFlangeOutputTypeEnum.kPressBrakeChordToleranceLoftedFlange,
                        UnitConvert.MmToCm(tolerance!.Value));
            }
            catch (Exception ex)
            {
                throw new ArgumentException("LOFTED_FLANGE_OUTPUT_REJECTED: Inventor refused the '" + output +
                    "' output type for these profiles: " + ex.Message);
            }
            LoftedFlangeFeature feature;
            try { feature = lofted.Add(definition); }
            catch (Exception ex)
            {
                throw new ArgumentException("LOFTED_FLANGE_REJECTED: Inventor refused this pair of profiles. Both must be " +
                    "open profiles on different planes, each one connected chain. Inventor reported: " + ex.Message);
            }
            SheetMetalSupport.RequireHealthy(feature.HealthStatus, "Lofted flange");
            return Ok(ctx, new JObject
            {
                ["feature_name"] = feature.Name,
                ["sketch_one"] = first,
                ["sketch_two"] = second,
                ["output"] = output,
                ["facet_tolerance_mm"] = tolerance,
                ["thickness_mm"] = SheetMetalSupport.EffectiveThicknessMm(def),
            });
        }
        catch (ArgumentException ex) { return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, ex.Message); }
    }
}
#endif
