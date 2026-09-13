#if INVENTOR2027
using System;
using System.Linq;
using Inventor;
using Newtonsoft.Json.Linq;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;

namespace Bimwright.Ipt.Shared.Handlers.SheetMetal;

/// <summary>
/// <c>sheet_metal_hem</c> — hem on open edges. Four hem types, each with its own measurements:
/// single and double take gap and length, teardrop and rolled take radius and angle. The type is
/// explicit because a hem made with the wrong one is valid CAD and wrong sheet metal.
/// </summary>
public sealed class SheetMetalHemHandler : HandlerBase, IInventorCommand
{
    public string Name => "sheet_metal_hem";
    public bool IsReadOnly => false;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        if (!SheetMetalSupport.TryGetDefinition(ctx, Name, out var app, out var def, out var failure)) return failure!;
        if (p["edge_ids"] is not JArray edgeIds || edgeIds.Count == 0)
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "edge_ids[] is required and must be non-empty.");
        string type = ((string?)p["hem_type"] ?? "single").Trim().ToLowerInvariant();
        if (type != "single" && type != "double" && type != "teardrop" && type != "rolled")
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "hem_type must be single, double, teardrop or rolled.");
        try
        {
            // Measurements are validated before any geometry is touched, so a bad request fails with
            // the argument error rather than with whatever the CAD call happens to say.
            var applied = new JObject { ["hem_type"] = type };
            double gap = 0, length = 0, radius = 0, angle = 0;
            bool sized = type == "single" || type == "double";
            if (sized)
            {
                gap = SheetMetalSupport.PositiveLength(p, "gap_mm", 1.0, 1000);
                length = SheetMetalSupport.PositiveLength(p, "length_mm", 1.0, 1000);
                applied["gap_mm"] = gap;
                applied["length_mm"] = length;
            }
            else
            {
                radius = SheetMetalSupport.PositiveLength(p, "radius_mm", 1.0, 1000);
                angle = p["angle_degrees"] == null ? 190 : p.Value<double>("angle_degrees");
                if (double.IsNaN(angle) || angle <= 0 || angle > 360)
                    throw new ArgumentException("angle_degrees must be greater than 0 and at most 360.");
                applied["radius_mm"] = radius;
                applied["angle_degrees"] = angle;
            }
            var edges = SheetMetalSupport.EdgeCollection(app, def, edgeIds);
            var hems = SheetMetalSupport.Features(def).HemFeatures;
            var definition = hems.CreateHemDefinition(edges);
            if (type == "single") definition.SetSingleHemType(UnitConvert.MmToCm(gap), UnitConvert.MmToCm(length));
            else if (type == "double") definition.SetDoubleHemType(UnitConvert.MmToCm(gap), UnitConvert.MmToCm(length));
            else if (type == "teardrop") definition.SetTeardropHemType(UnitConvert.MmToCm(radius), UnitConvert.DegToRad(angle));
            else definition.SetRolledHemType(UnitConvert.MmToCm(radius), UnitConvert.DegToRad(angle));
            var feature = hems.Add(definition);
            SheetMetalSupport.RequireHealthy(feature.HealthStatus, "Hem");
            applied["feature_name"] = feature.Name;
            applied["edge_count"] = edgeIds.Count;
            applied["thickness_mm"] = SheetMetalSupport.EffectiveThicknessMm(def);
            return Ok(ctx, applied);
        }
        catch (ArgumentException ex) { return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, ex.Message); }
    }
}

/// <summary>
/// <c>sheet_metal_fold</c> — fold the sheet along a sketch line that crosses the face.
/// The line is addressed by its 1-based index inside a named sketch, which is deterministic for the
/// caller that drew it in the same batch. Its endpoints must land exactly on the edges of the face:
/// Inventor refuses a line that stops short or overshoots, with an error that does not say so.
/// Angle is in degrees; bend side and direction are explicit flags because Inventor's default picks
/// one of four possible results.
/// </summary>
public sealed class SheetMetalFoldHandler : HandlerBase, IInventorCommand
{
    public string Name => "sheet_metal_fold";
    public bool IsReadOnly => false;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        if (!SheetMetalSupport.TryGetDefinition(ctx, Name, out _, out var def, out var failure)) return failure!;
        string sketchName = (string?)p["sketch_name"] ?? "";
        if (string.IsNullOrWhiteSpace(sketchName))
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "sketch_name is required.");
        int lineIndex = p["line_index"] == null ? 1 : p.Value<int>("line_index");
        double angle = p["angle_degrees"] == null ? 90 : p.Value<double>("angle_degrees");
        if (double.IsNaN(angle) || angle <= 0 || angle >= 360)
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "angle_degrees must be between 0 and 360, exclusive.");
        var location = ((string?)p["bend_location"] ?? "centerline").Trim().ToLowerInvariant() switch
        {
            "centerline" => BendLocationEnum.kCenterlineOfBend,
            "start" => BendLocationEnum.kStartOfBend,
            "end" => BendLocationEnum.kEndOfBend,
            _ => (BendLocationEnum)0
        };
        if (location == 0)
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "bend_location must be centerline, start or end.");
        bool flipDirection = p["flip_direction"]?.Type == JTokenType.Boolean && (bool)p["flip_direction"]!;
        bool flipSide = p["flip_side"]?.Type == JTokenType.Boolean && (bool)p["flip_side"]!;
        try
        {
            var line = SheetMetalSupport.SketchLine(def, sketchName, lineIndex);
            var folds = SheetMetalSupport.Features(def).FoldFeatures;
            var definition = folds.CreateFoldDefinition(line, UnitConvert.DegToRad(angle));
            definition.BendLocation = location;
            if (flipDirection) definition.IsPositiveBendDirection = !definition.IsPositiveBendDirection;
            if (flipSide) definition.IsPositiveBendSide = !definition.IsPositiveBendSide;
            FoldFeature feature;
            try { feature = folds.Add(definition); }
            catch (Exception ex)
            {
                // Inventor answers E_INVALIDARG for every bad bend line without saying why. Verified
                // on 2027: the line must end exactly on the face boundary — shorter or longer both fail.
                throw new ArgumentException("BEND_LINE_REJECTED: the bend line must be a single line whose " +
                    "endpoints lie exactly on the edges of the face being folded; a line that stops short of " +
                    "the boundary or runs past it is refused. Inventor reported: " + ex.Message);
            }
            SheetMetalSupport.RequireHealthy(feature.HealthStatus, "Fold");
            return Ok(ctx, new JObject
            {
                ["feature_name"] = feature.Name,
                ["sketch_name"] = sketchName,
                ["line_index"] = lineIndex,
                ["angle_degrees"] = angle,
                ["bend_location"] = location.ToString(),
                ["flipped_direction"] = flipDirection,
                ["flipped_side"] = flipSide,
                ["bend_count"] = SheetMetalSupport.Try(() => def.Bends.Count),
            });
        }
        catch (ArgumentException ex) { return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, ex.Message); }
    }
}

/// <summary>
/// <c>sheet_metal_contour_flange</c> — sweep an open sketch profile along one or more edges, or to a
/// given width when no edge is named. This is how a whole wall profile is built in one feature
/// instead of stacking flanges.
/// </summary>
public sealed class SheetMetalContourFlangeHandler : HandlerBase, IInventorCommand
{
    public string Name => "sheet_metal_contour_flange";
    public bool IsReadOnly => false;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        if (!SheetMetalSupport.TryGetDefinition(ctx, Name, out var app, out var def, out var failure)) return failure!;
        string sketchName = (string?)p["sketch_name"] ?? "";
        if (string.IsNullOrWhiteSpace(sketchName))
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "sketch_name is required: the open profile to sweep.");
        var edgeIds = p["edge_ids"] as JArray;
        bool hasWidth = p["width_mm"] != null && p["width_mm"]!.Type != JTokenType.Null;
        if ((edgeIds == null || edgeIds.Count == 0) && !hasWidth)
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT,
                "Provide edge_ids[] to follow, width_mm to extrude the profile, or both.");
        try
        {
            var features = SheetMetalSupport.Features(def);
            var path = features.CreatePath(SheetMetalSupport.FirstSketchCurve(def, sketchName));
            var definition = edgeIds != null && edgeIds.Count > 0
                ? features.ContourFlangeFeatures.CreateContourFlangeDefinition(path,
                    SheetMetalSupport.EdgeCollection(app, def, edgeIds))
                : features.ContourFlangeFeatures.CreateContourFlangeDefinition(path);
            double? width = null;
            if (hasWidth)
            {
                width = SheetMetalSupport.PositiveLength(p, "width_mm", 0.1, 10000);
                definition.SetDistanceExtent(UnitConvert.MmToCm(width.Value),
                    PartFeatureExtentDirectionEnum.kPositiveExtentDirection);
            }
            var feature = features.ContourFlangeFeatures.Add(definition);
            SheetMetalSupport.RequireHealthy(feature.HealthStatus, "Contour flange");
            return Ok(ctx, new JObject
            {
                ["feature_name"] = feature.Name,
                ["sketch_name"] = sketchName,
                ["edge_count"] = edgeIds?.Count ?? 0,
                ["width_mm"] = width,
                ["thickness_mm"] = SheetMetalSupport.EffectiveThicknessMm(def),
            });
        }
        catch (ArgumentException ex) { return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, ex.Message); }
    }
}

/// <summary>
/// <c>sheet_metal_corner</c> — round or chamfer the corner edges left where flanges meet.
/// Corner edges are portable ids of the vertical edges at the corner, not the flange faces.
/// </summary>
public sealed class SheetMetalCornerHandler : HandlerBase, IInventorCommand
{
    private readonly bool _chamfer;
    public SheetMetalCornerHandler(bool chamfer) => _chamfer = chamfer;
    public string Name => _chamfer ? "sheet_metal_corner_chamfer" : "sheet_metal_corner_round";
    public bool IsReadOnly => false;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        if (!SheetMetalSupport.TryGetDefinition(ctx, Name, out var app, out var def, out var failure)) return failure!;
        if (p["edge_ids"] is not JArray edgeIds || edgeIds.Count == 0)
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "edge_ids[] is required and must be non-empty.");
        string field = _chamfer ? "distance_mm" : "radius_mm";
        try
        {
            double size = SheetMetalSupport.PositiveLength(p, field, 0.01, 1000);   // before any geometry
            var edges = SheetMetalSupport.EdgeCollection(app, def, edgeIds);
            // Inventor answers E_INVALIDARG for anything that is not a corner edge, which tells the
            // caller nothing. A corner edge runs through the material, so its length is the thickness.
            double? thickness = SheetMetalSupport.EffectiveThicknessMm(def);
            if (thickness != null)
                foreach (Edge edge in edges)
                {
                    double? length = SheetMetalSupport.EdgeLengthMm(edge);
                    if (length != null && Math.Abs(length.Value - thickness.Value) > thickness.Value * 0.25)
                        throw new ArgumentException("NOT_A_CORNER_EDGE: a corner edge runs through the material, " +
                            "so its length is the sheet thickness (" + thickness.Value + " mm); the supplied edge is " +
                            length.Value + " mm. List edges with inventor_list_topology and pick the short vertical one at the corner.");
                }
            var features = SheetMetalSupport.Features(def);
            // These feature interfaces are unrelated to PartFeature in this interop, so each branch
            // reports its own name and health rather than being widened to a common type.
            string featureName;
            if (_chamfer)
            {
                var chamfers = features.CornerChamferFeatures;
                var chamfer = chamfers.Add(chamfers.CreateCornerChamferDefinition(edges, UnitConvert.MmToCm(size)));
                SheetMetalSupport.RequireHealthy(chamfer.HealthStatus, "Corner chamfer");
                featureName = chamfer.Name;
            }
            else
            {
                var rounds = features.CornerRoundFeatures;
                var round = rounds.Add(rounds.CreateCornerRoundDefinition(edges, UnitConvert.MmToCm(size)));
                SheetMetalSupport.RequireHealthy(round.HealthStatus, "Corner round");
                featureName = round.Name;
            }
            return Ok(ctx, new JObject
            {
                ["feature_name"] = featureName,
                ["edge_count"] = edgeIds.Count,
                [field] = size,
            });
        }
        catch (ArgumentException ex) { return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, ex.Message); }
    }
}

/// <summary>
/// <c>sheet_metal_unfold</c> / <c>sheet_metal_refold</c> — flatten or restore selected bends around a
/// stationary face, which is how a feature is placed across a bend. These are model features, not the
/// flat pattern: the flat pattern stays a separate derived representation.
/// </summary>
public sealed class SheetMetalUnfoldHandler : HandlerBase, IInventorCommand
{
    private readonly bool _refold;
    public SheetMetalUnfoldHandler(bool refold) => _refold = refold;
    public string Name => _refold ? "sheet_metal_refold" : "sheet_metal_unfold";
    public bool IsReadOnly => false;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        if (!SheetMetalSupport.TryGetDefinition(ctx, Name, out var app, out var def, out var failure)) return failure!;
        string faceId = (string?)p["stationary_face_id"] ?? "";
        if (string.IsNullOrWhiteSpace(faceId))
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT,
                "stationary_face_id is required: the face that must not move, as a portable id from inventor_list_topology.");
        try
        {
            var doc = SheetMetalSupport.OwningDocument(def);
            var face = Core.EntityReferences.ResolvePlanarPartFace(doc, faceId);
            var features = SheetMetalSupport.Features(def);
            // Bends carry no reference key of their own, so a caller names the bend by one of its
            // cylindrical faces, which does have one. Without bend_face_ids every reachable bend moves.
            ObjectCollection? selected = null;
            var bendFaceIds = p["bend_face_ids"] as JArray;
            if (bendFaceIds != null && bendFaceIds.Count > 0)
            {
                selected = app.TransientObjects.CreateObjectCollection();
                foreach (var token in bendFaceIds)
                {
                    var bendFace = Core.EntityReferences.ResolvePartEntityFace(doc, token.ToString());
                    var bend = SheetMetalSupport.FindBendByFace(def, bendFace)
                        ?? throw new ArgumentException("NOT_A_BEND_FACE: '" + token +
                            "' is not a face of any bend in this part. List faces with inventor_list_topology; a bend face is cylindrical.");
                    selected.Add(bend);
                }
            }
            int before = SheetMetalSupport.Try(() => def.Bends.Count).Type == JTokenType.Integer
                ? (int)SheetMetalSupport.Try(() => def.Bends.Count)! : -1;
            string featureName;
            if (_refold)
            {
                var refold = selected == null
                    ? features.RefoldFeatures.Add(face)
                    : features.RefoldFeatures.Add(face, selected);
                SheetMetalSupport.RequireHealthy(refold.HealthStatus, "Refold");
                featureName = refold.Name;
            }
            else
            {
                var unfold = selected == null
                    ? features.UnfoldFeatures.Add(face)
                    : features.UnfoldFeatures.Add(face, selected);
                SheetMetalSupport.RequireHealthy(unfold.HealthStatus, "Unfold");
                featureName = unfold.Name;
            }
            return Ok(ctx, new JObject
            {
                ["feature_name"] = featureName,
                ["stationary_face_id"] = faceId,
                ["all_bends"] = selected == null,
                ["bends_selected"] = selected?.Count ?? 0,
                ["bend_count_before"] = before < 0 ? null : before,
                ["bend_count_after"] = SheetMetalSupport.Try(() => def.Bends.Count),
            });
        }
        catch (ArgumentException ex) { return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, ex.Message); }
    }
}
#endif
