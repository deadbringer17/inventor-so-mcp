using System;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Infrastructure;

/// <summary>
/// The sidecar that declares what a company drawing template leaves free for views.
///
/// The usable area is declared rather than measured. A title block is a sketched symbol whose range
/// box says where its graphics are, not which part of the sheet the drafting standard actually
/// reserves — parts lists, revision tables and note columns are placed later into space a range box
/// cannot see. Declaring it makes the reserved space a property of the company standard instead of
/// an inference this tool re-derives per template.
///
/// The manifest is DATA. It carries numbers and two enumerations only: no paths, no commands, no
/// free text that reaches Inventor. A manifest that disagrees with the template's own sheet is a
/// hard failure, never a silent correction — see <see cref="Verify"/>.
/// </summary>
public sealed class DrawingTemplateManifest
{
    /// <summary>Smallest usable area worth planning into; below this no view plus its margins fits.</summary>
    public const double MinUsableMm = 50;

    /// <summary>Tolerance when comparing the declared sheet with the template's real one.</summary>
    public const double SheetToleranceMm = 0.5;

    private DrawingTemplateManifest(string sheetSize, string orientation,
        double xMinMm, double yMinMm, double xMaxMm, double yMaxMm)
    {
        SheetSize = sheetSize;
        Orientation = orientation;
        XMinMm = xMinMm; YMinMm = yMinMm; XMaxMm = xMaxMm; YMaxMm = yMaxMm;
    }

    public string SheetSize { get; }
    public string Orientation { get; }
    public double XMinMm { get; }
    public double YMinMm { get; }
    public double XMaxMm { get; }
    public double YMaxMm { get; }

    /// <summary>The declared sheet, in centimetres, as <see cref="SheetSizes"/> resolves it.</summary>
    public void DeclaredSheetCm(out double widthCm, out double heightCm)
        => SheetSizes.Resolve(SheetSize, Orientation, out widthCm, out heightCm);

    /// <summary>
    /// Parses and fully validates a manifest. Every failure is an <see cref="ArgumentException"/>
    /// whose message names the offending field, because the caller sees it as the message of a
    /// TEMPLATE_MANIFEST_INVALID result and has to be able to fix the file from it.
    /// </summary>
    public static DrawingTemplateManifest Parse(string json)
    {
        JObject root;
        try { root = JObject.Parse(json); }
        catch (Exception ex) { throw new ArgumentException("The template manifest is not valid JSON: " + ex.Message); }

        string sheetSize = ((string?)root["sheet_size"] ?? "").Trim().ToUpperInvariant();
        string orientation = ((string?)root["orientation"] ?? "").Trim().ToLowerInvariant();
        if (sheetSize.Length == 0) throw new ArgumentException("The template manifest must declare sheet_size.");
        if (orientation.Length == 0) throw new ArgumentException("The template manifest must declare orientation.");
        // Resolve validates both against the supported sheets and raises the same wording the tool
        // uses elsewhere for a bad sheet_size or orientation.
        SheetSizes.Resolve(sheetSize, orientation, out double declaredWidthCm, out double declaredHeightCm);

        if (root["usable_area_mm"] is not JObject area)
            throw new ArgumentException("The template manifest must declare usable_area_mm with x_min, y_min, x_max and y_max.");
        double xMin = Number(area, "x_min"), yMin = Number(area, "y_min");
        double xMax = Number(area, "x_max"), yMax = Number(area, "y_max");

        if (xMin < 0 || yMin < 0)
            throw new ArgumentException("usable_area_mm x_min and y_min must not be negative; the origin is the bottom-left sheet corner.");
        if (xMax - xMin < MinUsableMm || yMax - yMin < MinUsableMm)
            throw new ArgumentException("usable_area_mm must be at least " + MinUsableMm + " mm wide and tall.");
        double declaredWidthMm = declaredWidthCm * 10, declaredHeightMm = declaredHeightCm * 10;
        if (xMax > declaredWidthMm + SheetToleranceMm || yMax > declaredHeightMm + SheetToleranceMm)
            throw new ArgumentException("usable_area_mm falls outside the declared " + sheetSize + " " + orientation
                + " sheet (" + declaredWidthMm.ToString("F0") + " x " + declaredHeightMm.ToString("F0") + " mm).");
        return new DrawingTemplateManifest(sheetSize, orientation, xMin, yMin, xMax, yMax);
    }

    private static double Number(JObject area, string field)
    {
        var token = area[field];
        if (token == null || (token.Type != JTokenType.Float && token.Type != JTokenType.Integer))
            throw new ArgumentException("usable_area_mm." + field + " must be a number, in millimetres.");
        double value = (double)token;
        if (double.IsNaN(value) || double.IsInfinity(value))
            throw new ArgumentException("usable_area_mm." + field + " must be finite.");
        return value;
    }

    /// <summary>
    /// Turns the declared area into a usable rectangle for the sheet the template ACTUALLY produced,
    /// refusing when the two disagree. Without this check a manifest copied from the A3 template to
    /// the A2 one would place views straight over the title block, which is exactly the failure the
    /// declared area exists to prevent.
    /// </summary>
    public UsableArea Verify(double actualWidthCm, double actualHeightCm)
    {
        DeclaredSheetCm(out double declaredWidthCm, out double declaredHeightCm);
        double toleranceCm = SheetToleranceMm / 10;
        if (Math.Abs(actualWidthCm - declaredWidthCm) > toleranceCm ||
            Math.Abs(actualHeightCm - declaredHeightCm) > toleranceCm)
            throw new ArgumentException("The manifest declares a " + SheetSize + " " + Orientation + " sheet ("
                + (declaredWidthCm * 10).ToString("F0") + " x " + (declaredHeightCm * 10).ToString("F0")
                + " mm) but the template's sheet measures "
                + (actualWidthCm * 10).ToString("F0") + " x " + (actualHeightCm * 10).ToString("F0")
                + " mm. Fix sheet_size/orientation, or point at the manifest that belongs to this template.");
        return new UsableArea(actualWidthCm, actualHeightCm,
            UnitConvertMm(XMinMm), UnitConvertMm(YMinMm), UnitConvertMm(XMaxMm), UnitConvertMm(YMaxMm));
    }

    /// <summary>Millimetres to Inventor's internal centimetres. Local so this type stays free of the
    /// handler-side unit helper, which lives behind an Inventor compile symbol.</summary>
    private static double UnitConvertMm(double mm) => mm / 10;

    public JObject ToJson() => new JObject
    {
        ["sheet_size"] = SheetSize,
        ["orientation"] = Orientation,
        ["usable_area_mm"] = new JObject
        {
            ["x_min"] = XMinMm, ["y_min"] = YMinMm, ["x_max"] = XMaxMm, ["y_max"] = YMaxMm
        }
    };
}
