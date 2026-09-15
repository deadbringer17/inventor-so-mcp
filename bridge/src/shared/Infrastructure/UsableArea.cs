using System;

namespace Bimwright.Ipt.Shared.Infrastructure;

/// <summary>
/// The rectangle of a drawing sheet the views may occupy, in sheet centimetres, together with the
/// sheet it belongs to.
///
/// The original layout rule was one number — a reserved band along the bottom for the title block —
/// which only describes a title block that spans the full sheet width at the bottom. A company IDW
/// template puts its title block wherever its drafting standard says: bottom right inside a border,
/// a full-height column on the right, occasionally along the top. Those cannot be expressed as a
/// bottom reserve, so the usable region is carried as an explicit rectangle instead and the bottom
/// reserve becomes one way of constructing it (<see cref="FromReservedBottom"/>).
/// </summary>
public sealed class UsableArea
{
    public UsableArea(double sheetWidthCm, double sheetHeightCm,
                      double xMinCm, double yMinCm, double xMaxCm, double yMaxCm)
    {
        if (!ViewExtent.IsFinite(sheetWidthCm) || !ViewExtent.IsFinite(sheetHeightCm) ||
            sheetWidthCm <= 0 || sheetHeightCm <= 0)
            throw new ArgumentException("Sheet size must be finite and positive.");
        if (!ViewExtent.IsFinite(xMinCm) || !ViewExtent.IsFinite(yMinCm) ||
            !ViewExtent.IsFinite(xMaxCm) || !ViewExtent.IsFinite(yMaxCm))
            throw new ArgumentException("The usable area must be finite.");
        if (xMaxCm <= xMinCm || yMaxCm <= yMinCm)
            throw new ArgumentException("The usable area must have a positive width and height.");
        // Tolerance absorbs the millimetre-to-centimetre conversion of a manifest that states the
        // area as the full sheet; anything beyond that is a genuine out-of-sheet rectangle.
        const double slack = 1e-6;
        if (xMinCm < -slack || yMinCm < -slack || xMaxCm > sheetWidthCm + slack || yMaxCm > sheetHeightCm + slack)
            throw new ArgumentException("The usable area must lie inside the sheet.");
        SheetWidthCm = sheetWidthCm;
        SheetHeightCm = sheetHeightCm;
        XMinCm = Math.Max(0, xMinCm);
        YMinCm = Math.Max(0, yMinCm);
        XMaxCm = Math.Min(sheetWidthCm, xMaxCm);
        YMaxCm = Math.Min(sheetHeightCm, yMaxCm);
    }

    public double SheetWidthCm { get; }
    public double SheetHeightCm { get; }
    public double XMinCm { get; }
    public double YMinCm { get; }
    public double XMaxCm { get; }
    public double YMaxCm { get; }

    public double WidthCm => XMaxCm - XMinCm;
    public double HeightCm => YMaxCm - YMinCm;

    /// <summary>Sheet area the views may never use, in each axis. Used to size a sheet suggestion.</summary>
    public double ReservedWidthCm => SheetWidthCm - WidthCm;
    public double ReservedHeightCm => SheetHeightCm - HeightCm;

    /// <summary>
    /// The full sheet minus a band of <paramref name="reservedBottomCm"/> along the bottom: the rule
    /// that applies when the drawing comes from the host default template, where the title block is
    /// measured from <c>Sheet.TitleBlock.RangeBox</c> and always sits at the bottom.
    /// </summary>
    public static UsableArea FromReservedBottom(double sheetWidthCm, double sheetHeightCm, double reservedBottomCm)
    {
        if (!ViewExtent.IsFinite(reservedBottomCm) || reservedBottomCm < SheetPlanner.MinReservedBottomCm)
            throw new ArgumentException("reservedBottom must be at least " + SheetPlanner.MinReservedBottomCm + " cm.");
        if (!ViewExtent.IsFinite(sheetHeightCm) || reservedBottomCm >= sheetHeightCm)
            throw new ArgumentException("reservedBottom must be smaller than the sheet height.");
        return new UsableArea(sheetWidthCm, sheetHeightCm, 0, reservedBottomCm, sheetWidthCm, sheetHeightCm);
    }
}
