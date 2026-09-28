#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using Newtonsoft.Json.Linq;
using Inventor;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;

namespace Bimwright.Ipt.Shared.Handlers.Sketch;

/// <summary>
/// <c>draw_line</c> — add a two-point line to the target sketch (mm in, cm to the API). Returns the
/// 1-based index of the new entity within the sketch so it can be referenced by later tools.
/// </summary>
public sealed class DrawLineHandler : HandlerBase, IInventorCommand
{
    public string Name => "draw_line";
    public bool IsReadOnly => false;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        if (!ActiveDocumentSupport.TryGetActivePart(ctx, "draw_line", out var app, out var part, out var failure))
            return failure!;

        if (p["x1"] is null || p["y1"] is null || p["x2"] is null || p["y2"] is null)
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "x1,y1,x2,y2 (mm) are required");

        try
        {
            var def = part.ComponentDefinition;
            var sketch = SketchSupport.ResolveTargetSketch(def, (string?)p["sketch_name"]);
            var start = SketchSupport.Pt(app, p.Value<double>("x1"), p.Value<double>("y1"));
            var end = SketchSupport.Pt(app, p.Value<double>("x2"), p.Value<double>("y2"));
            bool infer = (bool?)p["infer_constraints"] ?? false;
            object From(Point2d point)
            {
                if (infer)
                    foreach (SketchPoint existing in sketch.SketchPoints)
                        if (existing.Geometry.DistanceTo(point) < 0.000001) return existing;
                return point;
            }
            var line = sketch.SketchLines.AddByTwoPoints(From(start), From(end));
            if (infer)
            {
                if (Math.Abs(start.Y - end.Y) < 0.000001) sketch.GeometricConstraints.AddHorizontal((SketchEntity)line, false);
                else if (Math.Abs(start.X - end.X) < 0.000001) sketch.GeometricConstraints.AddVertical((SketchEntity)line, false);
            }
            var dimensions = new JArray();
            if ((bool?)p["add_dimensions"] == true) dimensions.Add(SketchDimensions.Length(app,sketch,line,p));
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
