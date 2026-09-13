#if INVENTOR2027
using System;
using System.Linq;
using Inventor;
using Newtonsoft.Json.Linq;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Handlers;
using Bimwright.Ipt.Shared.Infrastructure;

namespace Bimwright.Ipt.Shared.Handlers.SheetMetal;

/// <summary>
/// Shared sheet-metal helpers. A sheet-metal part is a part document whose component definition is a
/// <see cref="SheetMetalComponentDefinition"/>; an ordinary part is never converted implicitly,
/// because converting changes how every later feature behaves.
/// </summary>
internal static class SheetMetalSupport
{
    /// <summary>Inventor's sheet-metal part sub-type, used to resolve the sheet-metal template.</summary>
    public const string SheetMetalSubType = "{9C464203-9BAE-11D3-8BAD-0060B0CE6BB4}";

    public static bool TryGetDefinition(InventorCommandContext ctx, string commandName,
        out Application app, out SheetMetalComponentDefinition def, out InventorCommandResult? failure)
    {
        def = null!;
        if (!ActiveDocumentSupport.TryGetActivePart(ctx, commandName, out app, out var part, out failure))
            return false;
        if (part.ComponentDefinition is not SheetMetalComponentDefinition sheetMetal)
        {
            failure = HandlerBase.FailForSupport(ctx, InventorErrorCodes.WRONG_DOCUMENT_TYPE,
                commandName + " requires an active sheet-metal part. Create one with kind='sheet_metal'; an ordinary part is never converted automatically.");
            return false;
        }
        def = sheetMetal;
        return true;
    }

    /// <summary>
    /// Length expressions accepted by a style, most likely first. Inventor's own
    /// <c>GetStringFromValue</c> is deliberately not used here: on a localized installation it returns
    /// a display string (for example "0.200 su") the style refuses. Invariant "2.5 mm" is accepted on
    /// the tested installation; the comma form is kept as a fallback for locales that require it.
    /// </summary>
    public static string[] LengthExpressions(double millimetres)
        => SheetMetalLengthExpression.Forms(millimetres);

    /// <summary>Writes the thickness onto a style, trying each accepted expression form.</summary>
    public static string ApplyThickness(SheetMetalStyle style, double millimetres)
    {
        Exception? last = null;
        foreach (var expression in LengthExpressions(millimetres))
        {
            try { style.Thickness = expression; return expression; }
            catch (Exception ex) { last = ex; }
        }
        throw new InvalidOperationException("THICKNESS_REJECTED: Inventor refused every thickness expression form. " +
            (last?.Message ?? ""));
    }

    public static double ExpressionToMm(global::Inventor.Document doc, string expression)
        => UnitConvert.CmToMm(Convert.ToDouble(
            doc.UnitsOfMeasure.GetValueFromExpression(expression, UnitsTypeEnum.kMillimeterLengthUnits)));

    /// <summary>The document behind a sheet-metal definition, typed.</summary>
    public static global::Inventor.Document OwningDocument(SheetMetalComponentDefinition def)
        => (global::Inventor.Document)def.Document;

    /// <summary>
    /// Sheet-metal feature collections (face, flange, cut, ...) live on <c>SheetMetalFeatures</c>;
    /// the definition exposes the collection through the plain <c>PartFeatures</c> interface.
    /// </summary>
    public static SheetMetalFeatures Features(SheetMetalComponentDefinition def)
        => (SheetMetalFeatures)def.Features;

    /// <summary>Closed solid profile from a named sketch on a sheet-metal part.</summary>
    public static Profile SolidProfile(SheetMetalComponentDefinition def, string sketchName)
    {
        var sketch = FindSketch(def, sketchName);
        if (sketch.Profiles.Count > 0) return sketch.Profiles[1];
        try { return sketch.Profiles.AddForSolid(true, null, null); }
        catch (Exception ex) { throw new ArgumentException("Sketch '" + sketchName + "' has no closed profile to use: " + ex.Message); }
    }

    /// <summary>
    /// Edge collection from portable entity ids (as returned by list_topology or get_selection).
    /// Positional indices are refused: an index silently points at different geometry after a rebuild.
    /// </summary>
    public static EdgeCollection EdgeCollection(Application app, SheetMetalComponentDefinition def, JArray edgeIds)
    {
        var doc = OwningDocument(def);
        var collection = app.TransientObjects.CreateEdgeCollection();
        foreach (var token in edgeIds)
        {
            string id = token.ToString();
            if (!id.StartsWith("ent_", StringComparison.Ordinal))
                throw new ArgumentException("edge_ids must be portable entity ids from inventor_list_topology or inventor_get_selection, not indices.");
            collection.Add(Core.EntityReferences.ResolvePartEdge(doc, id));
        }
        if (collection.Count == 0) throw new ArgumentException("No edges resolved.");
        return collection;
    }

    /// <summary>
    /// Millimetre value of a feature extent's distance parameter, or null when Inventor does not
    /// expose one. Read late-bound: casting the extent to its concrete interface fails on this
    /// interop even though the property chain resolves.
    /// </summary>
    public static double? DistanceExtentMm(object? extent)
    {
        try
        {
            if (extent == null) return null;
            object? distance = extent.GetType().InvokeMember("Distance",
                System.Reflection.BindingFlags.GetProperty, null, extent, null);
            if (distance == null) return null;
            object? value = distance.GetType().InvokeMember("Value",
                System.Reflection.BindingFlags.GetProperty, null, distance, null);
            return value == null ? null : UnitConvert.CmToMm(Convert.ToDouble(value));
        }
        catch { return null; }
    }

    /// <summary>
    /// How the flat pattern is rotated relative to the model, which decides how the blank sits on the
    /// sheet. Reported as Inventor holds it; null when no explicit alignment was set.
    /// </summary>
    public static JToken DescribeAlignment(FlatPattern flat)
    {
        try
        {
            AlignmentTypeEnum type;
            object alignedTo;
            bool reversed;
            flat.GetAlignment(out type, out alignedTo, out reversed);
            return new JObject
            {
                ["type"] = type.ToString(),
                ["reversed"] = reversed,
                ["aligned_to_set"] = alignedTo != null,
            };
        }
        catch { return JValue.CreateNull(); }
    }

    /// <summary>
    /// Catalog sizes of a table-driven punch, so a caller can pick a standard row instead of retyping
    /// its measurements. Bounded; a punch without a table reports an empty list.
    /// </summary>
    public static JArray PunchTableRows(iFeatureDefinition definition)
    {
        var rows = new JArray();
        try
        {
            if (!definition.IsTableDriven) return rows;
            int index = 0;
            foreach (iFeatureTableRow row in definition.iFeatureTable.iFeatureTableRows)
            {
                if (++index > 100) break;
                var values = new JObject();
                try
                {
                    for (int column = 1; column <= row.Count; column++)
                    {
                        var cell = row[column];
                        values[cell.Column.Heading] = Try(() => cell.Value);
                    }
                }
                catch { /* a row that cannot be read is still listed by index */ }
                rows.Add(new JObject { ["row"] = index, ["member"] = Try(() => row.MemberName), ["values"] = values });
            }
        }
        catch { /* a punch without a readable table reports what resolved */ }
        return rows;
    }

    /// <summary>
    /// The bend that owns a face, or null. Bends have no reference key, so this is how a caller can
    /// name one bend out of many without relying on a positional index.
    /// </summary>
    public static Bend? FindBendByFace(SheetMetalComponentDefinition def, Face face)
    {
        try
        {
            foreach (Bend bend in def.Bends)
            {
                foreach (Face candidate in bend.FrontFaces) if (ReferenceEquals(candidate, face)) return bend;
                foreach (Face candidate in bend.BackFaces) if (ReferenceEquals(candidate, face)) return bend;
            }
        }
        catch { /* an unreadable bend simply does not match */ }
        return null;
    }

    /// <summary>Edge length in millimetres, or null when Inventor cannot evaluate the curve.</summary>
    public static double? EdgeLengthMm(Edge edge)
    {
        try
        {
            var evaluator = edge.Evaluator;
            evaluator.GetParamExtents(out double min, out double max);
            evaluator.GetLengthAtParam(min, max, out double length);
            return UnitConvert.CmToMm(length);
        }
        catch { return null; }
    }

    /// <summary>Positive millimetre input inside an explicit range; a missing value is an error.</summary>
    public static double PositiveLength(JObject p, string field, double minimum, double maximum)
    {
        if (p[field] == null || p[field]!.Type == JTokenType.Null)
            throw new ArgumentException(field + " is required.");
        double value = p.Value<double>(field);
        if (double.IsNaN(value) || double.IsInfinity(value) || value < minimum || value > maximum)
            throw new ArgumentException(field + " must be between " + minimum + " and " + maximum + " mm.");
        return value;
    }

    /// <summary>A feature that is not up to date has produced wrong geometry, so it fails the batch.</summary>
    public static void RequireHealthy(HealthStatusEnum status, string what)
    {
        if (status != HealthStatusEnum.kUpToDateHealth)
            throw new InvalidOperationException(what + " feature is not healthy: " + status);
    }

    /// <summary>One sketch line by 1-based index, for features driven by a bend line.</summary>
    public static global::Inventor.SketchLine SketchLine(SheetMetalComponentDefinition def, string sketchName, int index)
    {
        var sketch = FindSketch(def, sketchName);
        if (index < 1 || index > sketch.SketchLines.Count)
            throw new ArgumentException("line_index " + index + " is out of range; sketch '" + sketchName +
                "' has " + sketch.SketchLines.Count + " lines.");
        return sketch.SketchLines[index];
    }

    /// <summary>First curve of a sketch, used as the seed of a swept path.</summary>
    public static object FirstSketchCurve(SheetMetalComponentDefinition def, string sketchName)
    {
        var sketch = FindSketch(def, sketchName);
        if (sketch.SketchLines.Count > 0) return sketch.SketchLines[1];
        if (sketch.SketchArcs.Count > 0) return sketch.SketchArcs[1];
        if (sketch.SketchSplines.Count > 0) return sketch.SketchSplines[1];
        throw new ArgumentException("Sketch '" + sketchName + "' has no line, arc or spline to sweep.");
    }

    public static PlanarSketch FindSketch(SheetMetalComponentDefinition def, string sketchName)
    {
        foreach (PlanarSketch candidate in def.Sketches)
            if (string.Equals(candidate.Name, sketchName, StringComparison.OrdinalIgnoreCase)) return candidate;
        throw new ArgumentException("No sketch named '" + sketchName + "' on the active sheet-metal part.");
    }

    public static double ValidatedThickness(JObject p, string field = "thickness_mm")
    {
        double value = p.Value<double>(field);
        if (double.IsNaN(value) || double.IsInfinity(value) || value < 0.05 || value > 50)
            throw new ArgumentException(field + " must be between 0.05 and 50 mm.");
        return value;
    }

    /// <summary>
    /// Thickness actually driving the model: the active style's, unless the definition overrides it.
    /// Reported in millimetres, or null when Inventor cannot evaluate it.
    /// </summary>
    public static double? EffectiveThicknessMm(SheetMetalComponentDefinition def)
    {
        // The definition's thickness parameter is what drives the model, whether it comes from the
        // active rule or from a document override, and it is a number rather than a localized string.
        try { return UnitConvert.CmToMm(Convert.ToDouble(def.Thickness.Value)); }
        catch { }
        try { return ExpressionToMm(OwningDocument(def), def.ActiveSheetMetalStyle.Thickness); }
        catch { return null; }
    }

    /// <summary>
    /// Reads one Inventor property, reporting null when the API refuses it. Sheet-metal definitions
    /// expose members that fail on an empty part, and a missing value must not look like a wrong one.
    /// </summary>
    public static JToken Try<T>(Func<T> read)
    {
        try
        {
            var value = read();
            return value == null ? JValue.CreateNull() : JToken.FromObject(value);
        }
        catch { return JValue.CreateNull(); }
    }

    public static JObject DescribeStyle(SheetMetalComponentDefinition def)
    {
        var result = new JObject();
        SheetMetalStyle? style = null;
        try { style = def.ActiveSheetMetalStyle; } catch { /* reported as nulls below */ }
        result["rule"] = style == null ? JValue.CreateNull() : Try(() => style.Name);
        result["style_location"] = style == null ? JValue.CreateNull() : Try(() => style.StyleLocation.ToString());
        result["thickness_expression"] = style == null ? JValue.CreateNull() : Try(() => style.Thickness);
        result["bend_radius_expression"] = style == null ? JValue.CreateNull() : Try(() => style.BendRadius);
        result["unfold_rule"] = style == null ? JValue.CreateNull() : Try(() => style.UnfoldMethod.Name);
        // Manufacturing values of the rule: reported, never judged. A part that satisfies them is not
        // thereby manufacturable, and the bridge makes no such claim.
        result["manufacturing"] = style == null ? (JToken)JValue.CreateNull() : new JObject
        {
            ["minimum_remnant"] = Try(() => style.MinimumRemnant),
            ["bend_relief_width"] = Try(() => style.BendReliefWidth),
            ["bend_relief_depth"] = Try(() => style.BendReliefDepth),
            ["bend_relief_shape"] = Try(() => style.BendReliefShape.ToString()),
            ["bend_transition"] = Try(() => style.BendTransition.ToString()),
            ["corner_relief_size"] = Try(() => style.CornerReliefSize),
            ["corner_relief_shape"] = Try(() => style.CornerReliefShape.ToString()),
            ["gap_size"] = Try(() => style.GapSize),
            ["material"] = Try(() => style.Material.Name),
        };
        result["uses_style_thickness"] = Try(() => def.UseSheetMetalStyleThickness);
        result["uses_style_unfold_rule"] = Try(() => def.UseSheetMetalStyleUnfoldMethod);
        result["k_factor"] = style == null ? JValue.CreateNull() : Try(() => style.UnfoldMethod.kFactor);
        result["thickness_mm"] = EffectiveThicknessMm(def);
        return result;
    }

    public static JArray AvailableRules(SheetMetalComponentDefinition def)
    {
        var rules = new JArray();
        string? active = null;
        try { active = def.ActiveSheetMetalStyle.Name; } catch { /* active rule reported separately */ }
        try
        {
            foreach (SheetMetalStyle style in def.SheetMetalStyles)
                rules.Add(new JObject { ["rule"] = Try(() => style.Name),
                    ["style_location"] = Try(() => style.StyleLocation.ToString()),
                    ["thickness_expression"] = Try(() => style.Thickness),
                    ["active"] = active != null && style.Name == active });
        }
        catch { /* a partial list is still useful; the caller sees what resolved */ }
        return rules;
    }

    /// <summary>
    /// Unfold rules present in the document, with the K-factor or equation that decides how much
    /// material the bends consume. Two parts with the same geometry and different unfold rules produce
    /// different blanks, so the active one is always reported.
    /// </summary>
    public static JArray AvailableUnfoldRules(SheetMetalComponentDefinition def)
    {
        var rules = new JArray();
        string? active = null;
        try { active = def.ActiveSheetMetalStyle.UnfoldMethod.Name; } catch { /* reported separately */ }
        try
        {
            foreach (UnfoldMethod method in def.UnfoldMethods)
                rules.Add(new JObject
                {
                    ["unfold_rule"] = Try(() => method.Name),
                    ["type"] = Try(() => method.UnfoldMethodType.ToString()),
                    ["k_factor"] = Try(() => method.kFactor),
                    ["active"] = active != null && method.Name == active,
                });
        }
        catch { /* a partial list still shows the caller what resolved */ }
        return rules;
    }

    public static UnfoldMethod FindUnfoldMethod(SheetMetalComponentDefinition def, string name)
    {
        var match = def.UnfoldMethods.Cast<UnfoldMethod>()
            .FirstOrDefault(method => string.Equals(method.Name, name, StringComparison.OrdinalIgnoreCase));
        if (match == null)
            throw new ArgumentException("No unfold rule named '" + name + "' in this document. Available: " +
                string.Join(", ", def.UnfoldMethods.Cast<UnfoldMethod>().Select(method => method.Name)));
        return match;
    }

    public static SheetMetalStyle FindStyle(SheetMetalComponentDefinition def, string rule)
    {
        var match = def.SheetMetalStyles.Cast<SheetMetalStyle>()
            .FirstOrDefault(style => string.Equals(style.Name, rule, StringComparison.OrdinalIgnoreCase));
        if (match == null)
            throw new ArgumentException("No sheet-metal rule named '" + rule + "' in this document. Available: " +
                string.Join(", ", def.SheetMetalStyles.Cast<SheetMetalStyle>().Select(style => style.Name)));
        return match;
    }

    /// <summary>Flat-pattern summary. Extents and area are reported only when Inventor supplies them.</summary>
    public static JObject DescribeFlatPattern(SheetMetalComponentDefinition def)
    {
        if (!def.HasFlatPattern) return new JObject { ["exists"] = false };
        var flat = def.FlatPattern;
        return new JObject
        {
            ["exists"] = true,
            ["length_mm"] = Try(() => UnitConvert.CmToMm(flat.Length)),
            ["width_mm"] = Try(() => UnitConvert.CmToMm(flat.Width)),
            ["area_mm2"] = Try(() => UnitConvert.Cm2ToMm2(flat.MassProperties.Area)),
            ["bend_count"] = Try(() => flat.FlatBendResults.Count),
            ["punch_count"] = Try(() => flat.FlatPunchResults.Count),
            ["alignment"] = DescribeAlignment(flat),
            ["bends"] = DescribeBends(flat),
        };
    }

    /// <summary>
    /// Per-bend data behind the blank: angle, inner radius, direction and the K-factor actually used.
    /// This is the bend table a press brake operator needs; it is read from Inventor's own flat-pattern
    /// results, never computed here. Bounded so a large part cannot flood one response.
    /// </summary>
    public static JToken DescribeBends(FlatPattern flat)
    {
        try
        {
            var bends = new JArray();
            int index = 0;
            foreach (FlatBendResult bend in flat.FlatBendResults)
            {
                if (++index > 200) break;
                bends.Add(new JObject
                {
                    ["internal_name"] = Try(() => bend.InternalName),
                    ["angle_degrees"] = Try(() => UnitConvert.RadToDeg(bend.Angle)),
                    ["inner_radius_mm"] = Try(() => UnitConvert.CmToMm(bend.InnerRadius)),
                    ["direction_up"] = Try(() => bend.IsDirectionUp),
                    ["on_bottom_face"] = Try(() => bend.IsOnBottomFace),
                    ["k_factor"] = Try(() => bend.kFactor),
                });
            }
            return bends;
        }
        catch { return JValue.CreateNull(); }
    }
}
#endif
