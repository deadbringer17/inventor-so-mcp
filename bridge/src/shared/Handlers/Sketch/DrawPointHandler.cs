#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Handlers;
using Bimwright.Ipt.Shared.Infrastructure;
using Newtonsoft.Json.Linq;
using Inventor;

namespace Bimwright.Ipt.Shared.Handlers.Sketch;

/// <summary>
/// <c>draw_point</c> — a sketch point at (x, y) in millimetres. Points are what hole and punch
/// features consume as centres, so they are drawn as hole centres by default.
/// </summary>
public sealed class DrawPointHandler : HandlerBase, IInventorCommand
{
    public string Name => "draw_point";
    public bool IsReadOnly => false;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        if (!ActiveDocumentSupport.TryGetActivePart(ctx, "draw_point", out var app, out var part, out var failure))
            return failure!;
        var modelPoint = p["model_point_mm"] as JArray;
        if (modelPoint != null && modelPoint.Count != 3)
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "model_point_mm must be [x, y, z] in millimetres");
        if (modelPoint == null && (p["x"] is null || p["y"] is null))
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT,
                "x,y (mm) in sketch space, or model_point_mm [x,y,z] in model space, are required");
        bool holeCentre = p["hole_center"]?.Type != JTokenType.Boolean || (bool)p["hole_center"]!;
        try
        {
            var def = part.ComponentDefinition;
            var sketch = SketchSupport.ResolveTargetSketch(def, (string?)p["sketch_name"]);
            // A sketch on a face has its own origin and axes, so a model-space point is mapped by
            // Inventor rather than guessed; sketch-space x,y stays available for work-plane sketches.
            var position = modelPoint != null
                ? sketch.ModelToSketchSpace(app.TransientGeometry.CreatePoint(
                    UnitConvert.MmToCm((double)modelPoint[0]!),
                    UnitConvert.MmToCm((double)modelPoint[1]!),
                    UnitConvert.MmToCm((double)modelPoint[2]!)))
                : SketchSupport.Pt(app, p.Value<double>("x"), p.Value<double>("y"));
            var point = sketch.SketchPoints.Add(position, holeCentre);
            return Ok(ctx, new JObject
            {
                ["sketch_name"] = sketch.Name,
                ["sketch_point_mm"] = new JArray(UnitConvert.CmToMm(position.X), UnitConvert.CmToMm(position.Y)),
                ["from_model_point"] = modelPoint != null,
                ["hole_center"] = point.HoleCenter,
                ["point_count"] = sketch.SketchPoints.Count,
            });
        }
        catch (ArgumentException ex) { return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, ex.Message); }
        catch (Exception ex) { return Fail(ctx, InventorErrorCodes.API_ERROR, ex.Message); }
    }
}
#endif
