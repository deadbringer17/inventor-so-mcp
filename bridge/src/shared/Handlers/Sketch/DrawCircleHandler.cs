#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using Newtonsoft.Json.Linq;
using Inventor;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;

namespace Bimwright.Ipt.Shared.Handlers.Sketch;

/// <summary>
/// <c>draw_circle</c> — add a center/radius circle to the target sketch (mm in, cm to the API).
/// </summary>
public sealed class DrawCircleHandler : HandlerBase, IInventorCommand
{
    public string Name => "draw_circle";
    public bool IsReadOnly => false;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        if (!ActiveDocumentSupport.TryGetActivePart(ctx, "draw_circle", out var app, out var part, out var failure))
            return failure!;

        if (p["cx"] is null || p["cy"] is null || p["radius"] is null)
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "cx,cy,radius (mm) are required");
        var radiusMm = p.Value<double>("radius");
        if (radiusMm <= 0)
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "radius must be greater than 0");

        try
        {
            var def = part.ComponentDefinition;
            var sketch = SketchSupport.ResolveTargetSketch(def, (string?)p["sketch_name"]);
            var center = SketchSupport.Pt(app, p.Value<double>("cx"), p.Value<double>("cy"));
            var circle = sketch.SketchCircles.AddByCenterRadius(center, UnitConvert.MmToCm(radiusMm));
            var dimensions = new JArray();
            if ((bool?)p["add_dimensions"] == true)
                dimensions.Add(sketch.DimensionConstraints.AddRadius((SketchEntity)circle,
                    SketchDimensions.Text(app,p,app.TransientGeometry.CreatePoint2d(center.X+circle.Radius+0.5,center.Y+0.5)),false).Parameter.Name);
            return Ok(ctx, new JObject
            {
                ["sketch_name"] = sketch.Name,
                ["entity_id"] = sketch.SketchEntities.Count.ToString(),
                ["dimensions"] = dimensions,
            });
        }
        catch (ArgumentException ex) { return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, ex.Message); }
        catch (Exception ex) { return Fail(ctx, InventorErrorCodes.API_ERROR, ex.Message); }
    }
}
#endif
