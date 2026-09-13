#if INVENTOR2027
using System;
using System.IO;
using File = System.IO.File;
using System.Linq;
using Inventor;
using Newtonsoft.Json.Linq;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;

namespace Bimwright.Ipt.Shared.Handlers.SheetMetal;

/// <summary>
/// <c>sheet_metal_punch</c> — place a punch from Inventor's own punch catalog at the sketch points of a
/// named sketch. The punch is named by catalog file name only: an iFeature is executable content, so an
/// arbitrary path is refused. Punch parameters (slot length, diameter, …) are set by name before the
/// feature is created, and every unset input keeps the catalog default.
/// </summary>
public sealed class SheetMetalPunchHandler : HandlerBase, IInventorCommand
{
    public string Name => "sheet_metal_punch";
    public bool IsReadOnly => false;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        if (!SheetMetalSupport.TryGetDefinition(ctx, Name, out var app, out var def, out var failure)) return failure!;
        string sketchName = (string?)p["sketch_name"] ?? "";
        if (string.IsNullOrWhiteSpace(sketchName))
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "sketch_name is required: the sketch holding the punch centres.");
        double? angle = p["angle_degrees"]?.Type is JTokenType.Float or JTokenType.Integer
            ? p.Value<double>("angle_degrees") : null;
        if (angle != null && (double.IsNaN(angle.Value) || angle.Value < -360 || angle.Value > 360))
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "angle_degrees must be between -360 and 360.");
        bool acrossBends = p["across_bends"]?.Type == JTokenType.Boolean && (bool)p["across_bends"]!;
        try
        {
            string punchName = PunchCatalogPolicy.ValidateName((string?)p["punch"]);
            var roots = PunchCatalogPolicy.CandidateRoots(
                SheetMetalSupport.Try(() => app.FileLocations.TemplatesPath).ToString(),
                SheetMetalSupport.Try(() => app.FileLocations.DesignDataPath).ToString(),
                ctx.InventorYear == 0 ? 2027 : ctx.InventorYear);
            string punchPath = PunchCatalogPolicy.Resolve(roots, punchName, File.Exists);

            var sketch = SheetMetalSupport.FindSketch(def, sketchName);
            // Verified on 2027: a punch placed from a work-plane sketch fails with a bare E_FAIL.
            if (sketch.PlanarEntity is not Face)
                throw new ArgumentException("PUNCH_NEEDS_FACE_SKETCH: punch centres must be sketched on the sheet face, " +
                    "not on a work plane. Create the sketch on a face id from inventor_list_topology and place points " +
                    "with draw_point model_point_mm so they land where you intend.");
            if (sketch.SketchPoints.Count == 0)
                throw new ArgumentException("Sketch '" + sketchName + "' has no sketch point to punch; draw one point per punch centre.");
            var centres = app.TransientObjects.CreateObjectCollection();
            foreach (SketchPoint point in sketch.SketchPoints) centres.Add(point);

            var punches = SheetMetalSupport.Features(def).PunchToolFeatures;
            var definition = punches.CreateiFeatureDefinition(punchPath);
            var applied = new JObject();

            // A table-driven punch carries the catalog sizes as table rows; selecting a row is how a
            // standard size is used instead of re-entering its measurements by hand.
            var rows = SheetMetalSupport.PunchTableRows(definition);
            int? requestedRow = p["table_row"]?.Type is JTokenType.Integer ? p.Value<int>("table_row") : null;
            if (requestedRow != null)
            {
                if (!definition.IsTableDriven)
                    throw new ArgumentException("PUNCH_NOT_TABLE_DRIVEN: '" + punchName + "' has no size table; set its parameters instead.");
                int count = definition.iFeatureTable.iFeatureTableRows.Count;
                if (requestedRow.Value < 1 || requestedRow.Value > count)
                    throw new ArgumentException("table_row must be between 1 and " + count + " for '" + punchName + "'.");
                definition.ActiveTableRow = definition.iFeatureTable.iFeatureTableRows[requestedRow.Value];
            }
            if (p["parameters"] is JObject requested)
                foreach (var property in requested.Properties())
                {
                    var input = definition.iFeatureInputs.Cast<object>()
                        .OfType<iFeatureParameterInput>()
                        .FirstOrDefault(candidate => string.Equals(candidate.Name, property.Name, StringComparison.OrdinalIgnoreCase));
                    if (input == null)
                        throw new ArgumentException("Punch '" + punchName + "' has no parameter named '" + property.Name +
                            "'. Available: " + string.Join(", ", definition.iFeatureInputs.Cast<object>()
                                .OfType<iFeatureParameterInput>().Select(candidate => candidate.Name)));
                    // A number is a millimetre length; a string is passed through as an Inventor expression.
                    input.Expression = property.Value.Type is JTokenType.Float or JTokenType.Integer
                        ? SheetMetalLengthExpression.Forms(property.Value.Value<double>())[0]
                        : property.Value.ToString();
                    applied[property.Name] = input.Expression;
                }

            var feature = angle != null
                ? punches.Add(centres, definition, UnitConvert.DegToRad(angle.Value), acrossBends)
                : punches.Add(centres, definition, System.Type.Missing, acrossBends);
            SheetMetalSupport.RequireHealthy(feature.HealthStatus, "Punch");
            return Ok(ctx, new JObject
            {
                ["feature_name"] = feature.Name,
                ["punch"] = punchName,
                ["punch_path"] = punchPath,
                ["sketch_name"] = sketchName,
                ["punch_count"] = centres.Count,
                ["angle_degrees"] = angle,
                ["across_bends"] = acrossBends,
                ["parameters_applied"] = applied,
                ["table_driven"] = SheetMetalSupport.Try(() => definition.IsTableDriven),
                ["table_row"] = requestedRow,
                ["table_rows"] = rows,
            });
        }
        catch (ArgumentException ex) { return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, ex.Message); }
        catch (FileNotFoundException ex) { return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, ex.Message); }
    }
}
#endif
