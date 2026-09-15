using System;
using System.Collections.Generic;

namespace Bimwright.Ipt.Shared.Infrastructure;

/// <summary>Extent of one view at scale 1, in sheet centimetres.</summary>
public sealed class ViewExtent
{
    public ViewExtent(ViewKind kind, double width, double height)
    {
        if (!IsFinite(width) || !IsFinite(height) || width <= 0 || height <= 0)
            throw new ArgumentException("View extent must be finite and positive.");
        Kind = kind; Width = width; Height = height;
    }
    public ViewKind Kind { get; }
    public double Width { get; }
    public double Height { get; }
    internal static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}

/// <summary>Centre point of one view on the sheet, in sheet centimetres.</summary>
public sealed class PlannedView
{
    public PlannedView(ViewKind kind, double centerX, double centerY)
    { Kind = kind; CenterX = centerX; CenterY = centerY; }
    public ViewKind Kind { get; }
    public double CenterX { get; }
    public double CenterY { get; }
}

/// <summary>Outcome of a layout attempt. A layout that does not fit is a result, not an exception.</summary>
public sealed class SheetPlanResult
{
    public double Scale { get; set; }
    public IReadOnlyList<PlannedView>? Views { get; set; }
    public bool Fits { get { return Views != null; } }
    public double RequiredWidthCm { get; set; }
    public double RequiredHeightCm { get; set; }
    public string? SuggestedSheetSize { get; set; }
}

/// <summary>
/// Chooses the drawing scale and places the views.
/// View extents scale linearly with view scale, so the caller measures each view once at any
/// reference scale, normalises to scale 1, and this planner answers every candidate scale
/// without touching Inventor again.
/// </summary>
public static class SheetPlanner
{
    /// <summary>ISO 5455 normalised scales, largest first.</summary>
    public static readonly double[] Ladder = { 10, 5, 2, 1, 0.5, 0.2, 0.1, 0.05, 0.02, 0.01, 0.005, 0.002 };

    /// <summary>Smallest ladder step. Views are first created here, so they always fit while being measured.</summary>
    public static double ReferenceScale { get { return Ladder[Ladder.Length - 1]; } }

    /// <summary>Matches the overlap rule in <see cref="DrawingLayout"/>.</summary>
    public const double MinGutterCm = 0.2;

    /// <summary>Matches the sheet-edge rule in <see cref="DrawingLayout"/>.</summary>
    public const double MinMarginCm = 1.0;

    /// <summary>Matches the title-block reserve floor in <see cref="DrawingLayout"/>.</summary>
    public const double MinReservedBottomCm = 4.0;

    /// <param name="fixedScale">
    /// When set, the planner tries only this scale instead of searching the ladder. The handler needs
    /// it for an explicitly requested scale, and to re-plan positions against views it already scaled.
    /// </param>
    public static SheetPlanResult Plan(double sheetWidth, double sheetHeight, double reservedBottom,
        IReadOnlyList<ViewExtent> extents, ProjectionAngle projection, double gutter, double? fixedScale = null)
        => Plan(UsableArea.FromReservedBottom(sheetWidth, sheetHeight, reservedBottom),
                extents, projection, gutter, fixedScale);

    /// <summary>
    /// Layout inside an arbitrary usable rectangle. A template-driven drawing states its usable area
    /// explicitly, because its title block need not be a band along the bottom of the sheet.
    /// </summary>
    public static SheetPlanResult Plan(UsableArea area,
        IReadOnlyList<ViewExtent> extents, ProjectionAngle projection, double gutter, double? fixedScale = null)
    {
        if (area == null) throw new ArgumentException("A usable area is required.");
        if (extents == null || extents.Count == 0)
            throw new ArgumentException("At least one view extent is required.");
        if (!ViewExtent.IsFinite(gutter) || gutter < MinGutterCm)
            throw new ArgumentException("gutter must be at least " + MinGutterCm + " cm.");
        if (fixedScale.HasValue && (!ViewExtent.IsFinite(fixedScale.Value) || fixedScale.Value <= 0))
            throw new ArgumentException("fixedScale must be finite and greater than zero.");

        var kinds = new List<ViewKind>();
        foreach (var extent in extents) kinds.Add(extent.Kind);

        double outer = Math.Max(gutter, MinMarginCm);
        // Every side of the usable rectangle gets the same margin. Spec decision 2 calls the corridor
        // "attorno a ciascuna" (around each view), not around three sides of it: without the second
        // `outer` per axis, a view block that exactly fills the usable height sits flush against the
        // title block while the other sides keep their margin.
        double usableWidth = area.WidthCm - 2 * outer;
        double usableHeight = area.HeightCm - 2 * outer;
        var result = new SheetPlanResult();
        double blockWidth = 0, blockHeight = 0;

        double[] candidates = fixedScale.HasValue ? new[] { fixedScale.Value } : Ladder;
        foreach (double scale in candidates)
        {
            IReadOnlyList<PlannedView>? views;
            bool fits = TryLayout(extents, kinds, projection, scale, gutter, usableWidth, usableHeight,
                area.XMinCm + outer, area.YMinCm + outer, out views, out blockWidth, out blockHeight);
            if (fits)
            {
                result.Scale = scale;
                result.Views = views;
                result.RequiredWidthCm = blockWidth;
                result.RequiredHeightCm = blockHeight;
                return result;
            }
        }

        result.Scale = candidates[candidates.Length - 1];
        result.RequiredWidthCm = blockWidth;
        result.RequiredHeightCm = blockHeight;
        // The sheet a caller should retry on has to carry the same non-usable margins and title-block
        // area this one does, so they are added back to the block the views actually need.
        result.SuggestedSheetSize = SheetSizes.SmallestContaining(
            blockWidth + 2 * outer + area.ReservedWidthCm,
            blockHeight + 2 * outer + area.ReservedHeightCm);
        return result;
    }

    private static bool TryLayout(IReadOnlyList<ViewExtent> extents, IReadOnlyList<ViewKind> kinds,
        ProjectionAngle projection, double scale, double gutter, double usableWidth, double usableHeight,
        double originX, double originY,
        out IReadOnlyList<PlannedView>? views, out double blockWidth, out double blockHeight)
    {
        views = null;
        var slots = new Dictionary<ViewKind, ViewSlot>();
        foreach (var kind in kinds) slots[kind] = DrawingViewSet.Slot(kind, projection, kinds);

        var columnWidth = new Dictionary<int, double>();
        var rowHeight = new Dictionary<int, double>();
        foreach (var extent in extents)
        {
            var slot = slots[extent.Kind];
            double width = extent.Width * scale, height = extent.Height * scale;
            if (!columnWidth.ContainsKey(slot.Column) || columnWidth[slot.Column] < width)
                columnWidth[slot.Column] = width;
            if (!rowHeight.ContainsKey(slot.Row) || rowHeight[slot.Row] < height)
                rowHeight[slot.Row] = height;
        }

        var columns = new List<int>(columnWidth.Keys); columns.Sort();
        var rows = new List<int>(rowHeight.Keys); rows.Sort();

        blockWidth = gutter * (columns.Count - 1);
        foreach (int column in columns) blockWidth += columnWidth[column];
        blockHeight = gutter * (rows.Count - 1);
        foreach (int row in rows) blockHeight += rowHeight[row];

        if (blockWidth > usableWidth || blockHeight > usableHeight) return false;

        var columnCenter = new Dictionary<int, double>();
        double cursor = originX + (usableWidth - blockWidth) / 2;
        foreach (int column in columns)
        {
            columnCenter[column] = cursor + columnWidth[column] / 2;
            cursor += columnWidth[column] + gutter;
        }

        var rowCenter = new Dictionary<int, double>();
        // originY already includes the bottom margin (mirrors originX above); the remaining
        // (usableHeight - blockHeight) is the slack centred between bottom and top margins.
        cursor = originY + (usableHeight - blockHeight) / 2;
        foreach (int row in rows)
        {
            rowCenter[row] = cursor + rowHeight[row] / 2;
            cursor += rowHeight[row] + gutter;
        }

        var planned = new List<PlannedView>();
        foreach (var extent in extents)
        {
            var slot = slots[extent.Kind];
            planned.Add(new PlannedView(extent.Kind, columnCenter[slot.Column], rowCenter[slot.Row]));
        }
        views = planned;
        return true;
    }
}
