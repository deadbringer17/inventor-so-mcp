#if INVENTOR2027
using System;
using System.Linq;
using Inventor;
using Newtonsoft.Json.Linq;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;

namespace Bimwright.Ipt.Shared.Handlers.SheetMetal;

/// <summary>
/// <c>set_sheet_metal_rule</c> — activate a sheet-metal rule already present in the document and/or
/// set the driving thickness. A global (library) style is converted to a local copy first, so the
/// user's style library is never edited. Thickness is written as a locale-correct expression.
/// </summary>
public sealed class SetSheetMetalRuleHandler : HandlerBase, IInventorCommand
{
    public string Name => "set_sheet_metal_rule";
    public bool IsReadOnly => false;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        if (!SheetMetalSupport.TryGetDefinition(ctx, Name, out _, out var def, out var failure)) return failure!;
        string? rule = (string?)p["rule"];
        string? unfoldRule = (string?)p["unfold_rule"];
        bool hasThickness = p["thickness_mm"] != null && p["thickness_mm"]!.Type != JTokenType.Null;
        if (string.IsNullOrWhiteSpace(rule) && !hasThickness && string.IsNullOrWhiteSpace(unfoldRule))
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "Provide rule, thickness_mm, unfold_rule, or a combination.");
        bool convertedToLocal = false;
        try
        {
            if (!string.IsNullOrWhiteSpace(rule))
            {
                var style = SheetMetalSupport.FindStyle(def, rule!);
                style.Activate();
            }
            string? appliedExpression = null;
            if (hasThickness)
            {
                double thickness = SheetMetalSupport.ValidatedThickness(p);
                var active = def.ActiveSheetMetalStyle;
                if (active.StyleLocation == StyleLocationEnum.kLibraryStyleLocation)
                {
                    // A library-only style must gain a document copy before it is edited; a style that
                    // already exists in both places is edited locally and the library keeps its values.
                    active.ConvertToLocal();
                    convertedToLocal = true;
                    active = def.ActiveSheetMetalStyle;
                }
                appliedExpression = SheetMetalSupport.ApplyThickness(active, thickness);
                def.UseSheetMetalStyleThickness = true;
                double? applied = SheetMetalSupport.EffectiveThicknessMm(def);
                if (applied == null || Math.Abs(applied.Value - thickness) > 1e-6)
                    throw new InvalidOperationException("THICKNESS_NOT_APPLIED: Inventor reports " +
                        (applied?.ToString() ?? "no value") + " mm instead of " + thickness + " mm.");
            }
            if (!string.IsNullOrWhiteSpace(unfoldRule))
            {
                var method = SheetMetalSupport.FindUnfoldMethod(def, unfoldRule!);
                var active = def.ActiveSheetMetalStyle;
                if (active.StyleLocation == StyleLocationEnum.kLibraryStyleLocation)
                {
                    active.ConvertToLocal();
                    convertedToLocal = true;
                    active = def.ActiveSheetMetalStyle;
                }
                // The unfold rule decides the developed length, so it is set on the rule that drives
                // the part and the document is put back on style-driven unfolding.
                active.UnfoldMethod = method;
                def.UseSheetMetalStyleUnfoldMethod = true;
                string? applied = null;
                try { applied = def.ActiveSheetMetalStyle.UnfoldMethod.Name; } catch { /* verified below */ }
                if (!string.Equals(applied, method.Name, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("UNFOLD_RULE_NOT_APPLIED: Inventor reports " +
                        (applied ?? "no rule") + " instead of " + method.Name + ".");
            }
            var result = SheetMetalSupport.DescribeStyle(def);
            result["available_unfold_rules"] = SheetMetalSupport.AvailableUnfoldRules(def);
            result["converted_library_style_to_local"] = convertedToLocal;
            result["library_styles_modified"] = false;
            result["applied_expression"] = appliedExpression;
            return Ok(ctx, result);
        }
        catch (ArgumentException ex) { return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, ex.Message); }
    }
}

/// <summary>
/// <c>sheet_metal_face</c> — base panel from a closed sketch profile. Thickness comes from the active
/// sheet-metal rule, never from a caller-supplied depth, which is what keeps the part parametric.
/// </summary>
public sealed class SheetMetalFaceHandler : HandlerBase, IInventorCommand
{
    public string Name => "sheet_metal_face";
    public bool IsReadOnly => false;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        if (!SheetMetalSupport.TryGetDefinition(ctx, Name, out _, out var def, out var failure)) return failure!;
        string sketchName = (string?)p["sketch_name"] ?? "";
        if (string.IsNullOrWhiteSpace(sketchName))
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "sketch_name is required.");
        try
        {
            var profile = SheetMetalSupport.SolidProfile(def, sketchName);
            var faces = SheetMetalSupport.Features(def).FaceFeatures;
            var feature = faces.Add(faces.CreateFaceFeatureDefinition(profile));
            if (feature.HealthStatus != HealthStatusEnum.kUpToDateHealth)
                throw new InvalidOperationException("Face feature is not healthy: " + feature.HealthStatus);
            return Ok(ctx, new JObject
            {
                ["feature_name"] = feature.Name,
                ["sketch_name"] = sketchName,
                ["thickness_mm"] = SheetMetalSupport.EffectiveThicknessMm(def),
                ["body_count"] = def.SurfaceBodies.Count,
            });
        }
        catch (ArgumentException ex) { return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, ex.Message); }
    }
}

/// <summary>
/// <c>sheet_metal_flange</c> — flange on one or more persistent model edges.
/// Angle is given in degrees and converted to radians (the API's unit; a degree value passed straight
/// through becomes a nonsense angle). Height is set through <c>SetDistanceHeightExtent</c> because the
/// distance argument of <c>CreateFlangeDefinition</c> is ignored.
/// </summary>
public sealed class SheetMetalFlangeHandler : HandlerBase, IInventorCommand
{
    public string Name => "sheet_metal_flange";
    public bool IsReadOnly => false;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        if (!SheetMetalSupport.TryGetDefinition(ctx, Name, out var app, out var def, out var failure)) return failure!;
        if (p["edge_ids"] is not JArray edgeIds || edgeIds.Count == 0)
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "edge_ids[] is required and must be non-empty.");
        if (p["height_mm"] == null) return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "height_mm is required.");
        double height = p.Value<double>("height_mm");
        if (double.IsNaN(height) || double.IsInfinity(height) || height <= 0 || height > 10000)
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "height_mm must be greater than 0 and at most 10000.");
        double angle = p["angle_degrees"] == null ? 90 : p.Value<double>("angle_degrees");
        if (double.IsNaN(angle) || angle <= 0 || angle >= 360)
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "angle_degrees must be between 0 and 360, exclusive.");
        var datum = ((string?)p["height_datum"] ?? "outer").Trim().ToLowerInvariant() switch
        {
            "outer" => HeightDatumTypeEnum.kHeightDatumOuter,
            "inner" => HeightDatumTypeEnum.kHeightDatumInner,
            "tangent" => HeightDatumTypeEnum.kHeightDatumTangent,
            _ => (HeightDatumTypeEnum)0
        };
        if (datum == 0)
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "height_datum must be outer, inner or tangent.");
        try
        {
            var edges = SheetMetalSupport.EdgeCollection(app, def, edgeIds);
            var flanges = SheetMetalSupport.Features(def).FlangeFeatures;
            // Radians: the API multiplies by 180/pi for display, so a degree value here is silently wrong.
            var definition = flanges.CreateFlangeDefinition(edges, UnitConvert.DegToRad(angle), UnitConvert.MmToCm(height));
            definition.SetDistanceHeightExtent(UnitConvert.MmToCm(height),
                PartFeatureExtentDirectionEnum.kPositiveExtentDirection, datum);
            var feature = flanges.Add(definition);
            if (feature.HealthStatus != HealthStatusEnum.kUpToDateHealth)
                throw new InvalidOperationException("Flange feature is not healthy: " + feature.HealthStatus);
            // The height extent is reached late-bound: a strict interface cast to DistanceExtent fails
            // on this interop, while the same property chain resolves through IDispatch.
            double? applied = SheetMetalSupport.DistanceExtentMm(feature.Definition.HeightExtent);
            if (applied != null && Math.Abs(applied.Value - height) > 1e-4)
                throw new InvalidOperationException("FLANGE_HEIGHT_NOT_APPLIED: Inventor stored " + applied.Value +
                    " mm instead of " + height + " mm.");
            return Ok(ctx, new JObject
            {
                ["feature_name"] = feature.Name,
                ["edge_count"] = edgeIds.Count,
                ["height_mm"] = applied ?? height,
                ["height_confirmed"] = applied != null,
                ["angle_degrees"] = angle,
                ["height_datum"] = datum.ToString(),
            });
        }
        catch (ArgumentException ex) { return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, ex.Message); }
    }
}

/// <summary>
/// <c>sheet_metal_cut</c> — cut through the sheet from a sketch profile.
/// The default extent follows the thickness parameter, so a cut sketched on a panel face stays
/// correct when the rule changes; an extrude-cut would freeze the depth. A sketch on a work plane has
/// no material in that direction and produces a driverless feature, so those call with
/// <c>extent='through_all'</c>. <c>across_bends=true</c> wraps the cut around bends.
/// </summary>
public sealed class SheetMetalCutHandler : HandlerBase, IInventorCommand
{
    public string Name => "sheet_metal_cut";
    public bool IsReadOnly => false;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        if (!SheetMetalSupport.TryGetDefinition(ctx, Name, out _, out var def, out var failure)) return failure!;
        string sketchName = (string?)p["sketch_name"] ?? "";
        if (string.IsNullOrWhiteSpace(sketchName))
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "sketch_name is required.");
        bool acrossBends = p["across_bends"]?.Type == JTokenType.Boolean && (bool)p["across_bends"]!;
        string extent = ((string?)p["extent"] ?? "thickness").Trim().ToLowerInvariant();
        if (extent != "thickness" && extent != "through_all")
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "extent must be 'thickness' or 'through_all'.");
        var direction = ((string?)p["direction"] ?? "positive").Trim().ToLowerInvariant() switch
        {
            "positive" => PartFeatureExtentDirectionEnum.kPositiveExtentDirection,
            "negative" => PartFeatureExtentDirectionEnum.kNegativeExtentDirection,
            "symmetric" => PartFeatureExtentDirectionEnum.kSymmetricExtentDirection,
            _ => (PartFeatureExtentDirectionEnum)0
        };
        if (direction == 0)
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "direction must be positive, negative or symmetric.");
        if (acrossBends && extent == "through_all")
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "across_bends uses its own extent; do not combine it with through_all.");
        try
        {
            var profile = SheetMetalSupport.SolidProfile(def, sketchName);
            var cuts = SheetMetalSupport.Features(def).CutFeatures;
            var definition = cuts.CreateCutDefinition(profile);
            // Default extent already follows the thickness parameter; across-bends needs its own extent.
            if (acrossBends)
                definition.SetCutAcrossBendsExtent(
                    SheetMetalSupport.LengthExpressions(SheetMetalSupport.EffectiveThicknessMm(def)
                        ?? throw new InvalidOperationException("THICKNESS_UNKNOWN: cannot size an across-bends cut."))[0]);
            else if (extent == "through_all") definition.SetThroughAllExtent(direction);
            var feature = cuts.Add(definition);
            if (feature.HealthStatus != HealthStatusEnum.kUpToDateHealth)
                throw new InvalidOperationException("Cut feature is not healthy: " + feature.HealthStatus);
            return Ok(ctx, new JObject
            {
                ["feature_name"] = feature.Name,
                ["sketch_name"] = sketchName,
                ["across_bends"] = acrossBends,
                ["extent"] = acrossBends ? "across_bends" : extent,
                ["direction"] = extent == "through_all" ? direction.ToString() : null,
                ["thickness_mm"] = SheetMetalSupport.EffectiveThicknessMm(def),
            });
        }
        catch (ArgumentException ex) { return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, ex.Message); }
    }
}

/// <summary>
/// <c>create_flat_pattern</c> — unfold the sheet-metal part and return to the folded model. Unfolding
/// is the real test of sheet-metal geometry: walls built as extrusions look identical and do not
/// unfold. An existing flat pattern is reported, not silently rebuilt. With <c>align_to_edge_id</c> the
/// blank is rotated so that model edge runs horizontally or vertically, which is what decides how it
/// sits on the sheet for nesting.
/// </summary>
public sealed class FlatPatternHandler : HandlerBase, IInventorCommand
{
    public string Name => "create_flat_pattern";
    public bool IsReadOnly => false;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        if (!SheetMetalSupport.TryGetDefinition(ctx, Name, out _, out var def, out var failure)) return failure!;
        if (def.SurfaceBodies.Count != 1)
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT,
                "A flat pattern requires exactly one solid body; this part has " + def.SurfaceBodies.Count + ".");
        bool existed = def.HasFlatPattern;
        if (!existed)
        {
            if (ctx.IsDeadlineExceeded?.Invoke() == true) throw new TimeoutException("Expired before unfolding.");
            def.Unfold();
            try { def.FlatPattern.ExitEdit(); }
            catch { /* the folded model is restored below; report rather than assume */ }
        }
        if (!def.HasFlatPattern)
            throw new InvalidOperationException("UNFOLD_FAILED: the part did not produce a flat pattern.");

        string? alignEdge = (string?)p["align_to_edge_id"];
        string alignment = ((string?)p["alignment"] ?? "horizontal").Trim().ToLowerInvariant();
        bool alignmentApplied = false;
        if (!string.IsNullOrWhiteSpace(alignEdge))
        {
            var type = alignment switch
            {
                "horizontal" => AlignmentTypeEnum.kHorizontalAlignment,
                "vertical" => AlignmentTypeEnum.kVerticalAlignment,
                _ => (AlignmentTypeEnum)0
            };
            if (type == 0) return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "alignment must be horizontal or vertical.");
            bool reversed = p["alignment_reversed"]?.Type == JTokenType.Boolean && (bool)p["alignment_reversed"]!;
            try
            {
                // The edge is a model edge; the flat pattern is rotated so that edge runs along the axis.
                var edge = Core.EntityReferences.ResolvePartEdge(SheetMetalSupport.OwningDocument(def), alignEdge!);
                def.FlatPattern.SetAlignment(type, edge, reversed);
                alignmentApplied = true;
            }
            catch (ArgumentException ex) { return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, ex.Message); }
            catch (Exception ex)
            {
                return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT,
                    "FLAT_PATTERN_ALIGNMENT_REJECTED: Inventor refused to align the flat pattern to that edge: " + ex.Message);
            }
        }

        var result = SheetMetalSupport.DescribeFlatPattern(def);
        result["alignment_applied"] = alignmentApplied;
        result["created"] = !existed;
        result["already_existed"] = existed;
        result["thickness_mm"] = SheetMetalSupport.EffectiveThicknessMm(def);
        return Ok(ctx, result);
    }
}

/// <summary>
/// <c>get_sheet_metal_info</c> — read-only rule, thickness and flat-pattern state of the active part.
/// Reports whether the part is sheet metal at all instead of failing, so a caller can branch.
/// </summary>
public sealed class SheetMetalInfoHandler : HandlerBase, IInventorCommand
{
    public string Name => "get_sheet_metal_info";
    public bool IsReadOnly => true;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        if (!ActiveDocumentSupport.TryGetActivePart(ctx, Name, out _, out var part, out var failure)) return failure!;
        if (part.ComponentDefinition is not SheetMetalComponentDefinition def)
            return Ok(ctx, new JObject { ["is_sheet_metal"] = false, ["sub_type"] = part.SubType });
        var result = SheetMetalSupport.DescribeStyle(def);
        result["is_sheet_metal"] = true;
        result["available_rules"] = SheetMetalSupport.AvailableRules(def);
        result["available_unfold_rules"] = SheetMetalSupport.AvailableUnfoldRules(def);
        result["flat_pattern"] = SheetMetalSupport.DescribeFlatPattern(def);
        result["body_count"] = SheetMetalSupport.Try(() => def.SurfaceBodies.Count);
        result["bend_count"] = SheetMetalSupport.Try(() => def.Bends.Count);
        result["feature_count"] = SheetMetalSupport.Try(() => def.Features.Count);
        return Ok(ctx, result);
    }
}
#endif
