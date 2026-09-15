using System;
using System.Collections.Generic;

namespace Bimwright.Ipt.Shared.Infrastructure;

/// <summary>The orthographic and isometric views a drawing sheet can carry.</summary>
public enum ViewKind { Front, Back, Top, Bottom, Left, Right, Iso }

/// <summary>Projection convention. First angle is ISO/UNI, third angle is ANSI.</summary>
public enum ProjectionAngle { First, Third }

/// <summary>Grid cell of a view relative to the front view at (0,0). Column +1 is right, row +1 is up.</summary>
public readonly struct ViewSlot
{
    public ViewSlot(int column, int row) { Column = column; Row = row; }
    public int Column { get; }
    public int Row { get; }
}

/// <summary>
/// Parses the caller's view list and places each view on the sheet grid.
/// The same cell means different things under the two projection conventions:
/// under first angle the plan view goes BELOW the front view, under third angle ABOVE.
/// Getting this wrong produces a mirrored, silently wrong production drawing.
/// </summary>
public static class DrawingViewSet
{
    public const string DefaultViews = "front,top,right,iso";

    private static readonly ViewSlot[] Corners =
    {
        new ViewSlot(1, 1), new ViewSlot(-1, 1), new ViewSlot(1, -1), new ViewSlot(-1, -1)
    };

    public static ViewKind[] Parse(string? views)
    {
        string text = string.IsNullOrWhiteSpace(views) ? DefaultViews : views!;
        var result = new List<ViewKind>();
        foreach (string raw in text.Split(','))
        {
            string token = raw.Trim();
            if (token.Length == 0)
                throw new ArgumentException("views must not contain empty entries.");
            ViewKind kind;
            switch (token.ToLowerInvariant())
            {
                case "front": kind = ViewKind.Front; break;
                case "back": kind = ViewKind.Back; break;
                case "top": kind = ViewKind.Top; break;
                case "bottom": kind = ViewKind.Bottom; break;
                case "left": kind = ViewKind.Left; break;
                case "right": kind = ViewKind.Right; break;
                case "iso": kind = ViewKind.Iso; break;
                default:
                    throw new ArgumentException(
                        "Unknown view '" + token + "'. Use front, back, top, bottom, left, right or iso.");
            }
            if (result.Contains(kind)) throw new ArgumentException("Duplicate view '" + token + "'.");
            result.Add(kind);
        }
        if (result.Count == 0) throw new ArgumentException("views must name at least one view.");
        if (!result.Contains(ViewKind.Front))
            foreach (var kind in result)
                if (IsProjected(kind))
                    throw new ArgumentException(
                        "Projected views require 'front': they exist only as children of a base view.");
        return result.ToArray();
    }

    // Back is deliberately NOT projected: AddProjectedView derives a child's orientation from the
    // DIRECTION to its parent, not the distance, so a "two columns out" back view would be
    // indistinguishable from the projected side view sharing that direction. Back is created with
    // AddBaseView and kBackViewOrientation instead, like Front and Iso.
    public static bool IsProjected(ViewKind kind) =>
        kind == ViewKind.Top || kind == ViewKind.Bottom ||
        kind == ViewKind.Left || kind == ViewKind.Right;

    public static ViewSlot Slot(ViewKind kind, ProjectionAngle projection, IReadOnlyList<ViewKind> all)
    {
        // Under first angle each view lands on the side OPPOSITE the direction it is seen from.
        int side = projection == ProjectionAngle.First ? -1 : 1;
        switch (kind)
        {
            case ViewKind.Front: return new ViewSlot(0, 0);
            case ViewKind.Top: return new ViewSlot(0, side);
            case ViewKind.Bottom: return new ViewSlot(0, -side);
            case ViewKind.Right: return new ViewSlot(side, 0);
            case ViewKind.Left: return new ViewSlot(-side, 0);
            case ViewKind.Back: return new ViewSlot(2 * side, 0);
            case ViewKind.Iso: return FreeCorner(projection, all);
            default: throw new ArgumentException("Unsupported view kind.");
        }
    }

    private static ViewSlot FreeCorner(ProjectionAngle projection, IReadOnlyList<ViewKind> all)
    {
        foreach (var corner in Corners)
        {
            bool taken = false;
            foreach (var other in all)
            {
                if (other == ViewKind.Iso) continue;
                var slot = Slot(other, projection, all);
                if (slot.Column == corner.Column && slot.Row == corner.Row) { taken = true; break; }
            }
            if (!taken) return corner;
        }
        // Unreachable: orthographic views only occupy the axes and the back column, never a corner.
        throw new InvalidOperationException("No free corner for the isometric view.");
    }
}
