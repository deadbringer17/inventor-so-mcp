# D1 — Motore di impaginazione disegni — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give `inventor_create_drawing_safe` a real sheet-layout engine: chosen sheet size and orientation, explicit projection angle, a caller-chosen view set, and an automatically chosen ISO scale that leaves room for dimensioning.

**Architecture:** All layout logic lives in three new pure files under `bridge/src/shared/Infrastructure/`, which the test project already globs and compiles without Inventor. The Inventor handler stays thin: it creates each requested view once at a tiny reference scale, measures the real extents, hands them to the pure planner, applies the returned scale and positions, and re-validates. View extents scale linearly with view scale, so one measurement pass answers every candidate scale.

**Tech Stack:** C# (net48 / net8.0-windows / net10.0-windows for the add-ins, net8.0 for server and tests), xUnit, Inventor COM interop, Newtonsoft.Json for the wire payload.

**Spec:** `docs/superpowers/specs/2026-09-14-impaginazione-disegni-design.md`

## Global Constraints

- Inventor's internal length unit is **centimetres**. Every MCP input in mm converts at the handler boundary via `Bimwright.Ipt.Shared.Handlers.UnitConvert`; every length output converts back to mm. The pure layout files work **entirely in centimetres** and never see mm.
- The three new files go in `bridge/src/shared/Infrastructure/` and must contain **no `using Inventor;`** and no reference to any Inventor type. That directory is globbed into `bridge/tests/Bimwright.Ipt.Tests/Bimwright.Ipt.Tests.csproj`, so an Inventor reference there breaks the test build on machines without the SDK.
- New files must compile under **net48** as well (add-ins 2022–2024). Use plain `sealed class` with constructor-set get-only properties; no records, no target-typed `new`, no top-level file-scoped features beyond file-scoped namespaces (already used in this codebase).
- The handler body is inside `#if INVENTOR2027 … #endif`. Keep that gate.
- Handler code must preserve every existing guard: `READ_ONLY`, `WRONG_DOCUMENT_TYPE`, `DOCUMENT_CHANGED`, `STALE_REVISION`, `TRANSACTION_BUSY`, `SOURCE_CHANGED`, deadline checks, closing only the tool-owned draft, and restoring `UserInterfaceManager.UserInteractionDisabled` in `finally`.
- Normalised scale ladder, largest first: `10, 5, 2, 1, 0.5, 0.2, 0.1, 0.05, 0.02, 0.01, 0.005, 0.002`. Reference scale = `0.002` (1:500).
- Defaults: `sheet_size` = `A3`, `orientation` = `landscape`, `projection` = `first`, `views` = `front,top,right,iso`, `gutter_mm` = `15`, `preview` = `true`.
- Build and test from the `bridge/` directory: `dotnet build src/IptMcp.sln -c Debug` and `dotnet test tests/Bimwright.Ipt.Tests -c Debug`.
- Commit messages in English, one commit per task, ending with the line `Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>`.

---

### Task 1: View vocabulary, parsing and slot assignment

Defines which views exist, how the caller names them, and where each one sits on the 3×3 grid under each projection angle. This is the file that encodes the first-angle/third-angle inversion, which is the correctness defect D1 exists to fix.

**Files:**
- Create: `bridge/src/shared/Infrastructure/DrawingViewSet.cs`
- Test: `bridge/tests/Bimwright.Ipt.Tests/DrawingViewSetTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `enum ViewKind { Front, Back, Top, Bottom, Left, Right, Iso }`
  - `enum ProjectionAngle { First, Third }`
  - `readonly struct ViewSlot` with `int Column { get; }`, `int Row { get; }`, constructor `ViewSlot(int column, int row)`
  - `static class DrawingViewSet` with `const string DefaultViews`, `ViewKind[] Parse(string? views)`, `bool IsProjected(ViewKind kind)`, `ViewSlot Slot(ViewKind kind, ProjectionAngle projection, IReadOnlyList<ViewKind> all)`

Grid convention: front is `(0, 0)`, column `+1` is to the right, row `+1` is upward.

- [ ] **Step 1: Write the failing test**

Create `bridge/tests/Bimwright.Ipt.Tests/DrawingViewSetTests.cs`:

```csharp
using System;
using Bimwright.Ipt.Shared.Infrastructure;

namespace Bimwright.Ipt.Tests;

public sealed class DrawingViewSetTests
{
    [Fact]
    public void DefaultViewSetParsed()
    {
        var views = DrawingViewSet.Parse(null);
        Assert.Equal(new[] { ViewKind.Front, ViewKind.Top, ViewKind.Right, ViewKind.Iso }, views);
    }

    [Fact]
    public void WhitespaceAndCaseIgnored()
    {
        var views = DrawingViewSet.Parse(" Front , TOP ,iso ");
        Assert.Equal(new[] { ViewKind.Front, ViewKind.Top, ViewKind.Iso }, views);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankFallsBackToDefault(string text)
        => Assert.Equal(DrawingViewSet.Parse(null), DrawingViewSet.Parse(text));

    [Theory]
    [InlineData("front,,top")]
    [InlineData("front,plan")]
    [InlineData("front,top,top")]
    [InlineData("top,right")]
    public void InvalidViewSetRejected(string text)
        => Assert.Throws<ArgumentException>(() => DrawingViewSet.Parse(text));

    [Fact]
    public void IsoAloneIsAllowed() => Assert.Equal(new[] { ViewKind.Iso }, DrawingViewSet.Parse("iso"));

    [Fact]
    public void FirstAnglePutsTopBelowAndRightToTheLeft()
    {
        var all = DrawingViewSet.Parse("front,top,right");
        Assert.Equal(0, DrawingViewSet.Slot(ViewKind.Front, ProjectionAngle.First, all).Column);
        Assert.Equal(0, DrawingViewSet.Slot(ViewKind.Front, ProjectionAngle.First, all).Row);
        Assert.Equal(-1, DrawingViewSet.Slot(ViewKind.Top, ProjectionAngle.First, all).Row);
        Assert.Equal(-1, DrawingViewSet.Slot(ViewKind.Right, ProjectionAngle.First, all).Column);
    }

    [Fact]
    public void ThirdAngleMirrorsFirstAngle()
    {
        var all = DrawingViewSet.Parse("front,top,bottom,left,right");
        foreach (var kind in new[] { ViewKind.Top, ViewKind.Bottom, ViewKind.Left, ViewKind.Right })
        {
            var first = DrawingViewSet.Slot(kind, ProjectionAngle.First, all);
            var third = DrawingViewSet.Slot(kind, ProjectionAngle.Third, all);
            Assert.Equal(-first.Column, third.Column);
            Assert.Equal(-first.Row, third.Row);
        }
    }

    [Fact]
    public void IsoTakesAFreeCorner()
    {
        var all = DrawingViewSet.Parse("front,back,top,bottom,left,right,iso");
        var iso = DrawingViewSet.Slot(ViewKind.Iso, ProjectionAngle.First, all);
        Assert.Equal(1, Math.Abs(iso.Column));
        Assert.Equal(1, Math.Abs(iso.Row));
        foreach (var kind in all)
        {
            if (kind == ViewKind.Iso) continue;
            var other = DrawingViewSet.Slot(kind, ProjectionAngle.First, all);
            Assert.False(other.Column == iso.Column && other.Row == iso.Row);
        }
    }

    [Fact]
    public void ProjectedViewsClassified()
    {
        Assert.False(DrawingViewSet.IsProjected(ViewKind.Front));
        Assert.False(DrawingViewSet.IsProjected(ViewKind.Iso));
        foreach (var kind in new[] { ViewKind.Back, ViewKind.Top, ViewKind.Bottom, ViewKind.Left, ViewKind.Right })
            Assert.True(DrawingViewSet.IsProjected(kind));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run from `bridge/`: `dotnet test tests/Bimwright.Ipt.Tests -c Debug --filter DrawingViewSetTests`
Expected: build FAILS — `ViewKind`, `ProjectionAngle`, `DrawingViewSet` do not exist.

- [ ] **Step 3: Write the implementation**

Create `bridge/src/shared/Infrastructure/DrawingViewSet.cs`:

```csharp
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

    public static bool IsProjected(ViewKind kind) =>
        kind == ViewKind.Back || kind == ViewKind.Top || kind == ViewKind.Bottom ||
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
```

- [ ] **Step 4: Run tests to verify they pass**

Run from `bridge/`: `dotnet test tests/Bimwright.Ipt.Tests -c Debug --filter DrawingViewSetTests`
Expected: PASS, all tests.

- [ ] **Step 5: Commit**

```bash
git add bridge/src/shared/Infrastructure/DrawingViewSet.cs bridge/tests/Bimwright.Ipt.Tests/DrawingViewSetTests.cs
git commit -m "feat(drawing): view vocabulary and projection-aware slot assignment"
```

---

### Task 2: Sheet size table

A pure lookup from sheet name and orientation to centimetres, plus the reverse lookup used to suggest a larger sheet when nothing fits.

**Files:**
- Create: `bridge/src/shared/Infrastructure/SheetSizes.cs`
- Test: `bridge/tests/Bimwright.Ipt.Tests/SheetSizesTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `static class SheetSizes` with `string[] Names`, `void Resolve(string? name, string? orientation, out double widthCm, out double heightCm)`, `string? SmallestContaining(double widthCm, double heightCm)`.

- [ ] **Step 1: Write the failing test**

Create `bridge/tests/Bimwright.Ipt.Tests/SheetSizesTests.cs`:

```csharp
using System;
using Bimwright.Ipt.Shared.Infrastructure;

namespace Bimwright.Ipt.Tests;

public sealed class SheetSizesTests
{
    [Fact]
    public void DefaultsToA3Landscape()
    {
        SheetSizes.Resolve(null, null, out double width, out double height);
        Assert.Equal(42.0, width, 3);
        Assert.Equal(29.7, height, 3);
    }

    [Fact]
    public void PortraitSwapsEdges()
    {
        SheetSizes.Resolve("A4", "portrait", out double width, out double height);
        Assert.Equal(21.0, width, 3);
        Assert.Equal(29.7, height, 3);
    }

    [Fact]
    public void NameAndOrientationAreCaseInsensitive()
    {
        SheetSizes.Resolve(" a0 ", " LANDSCAPE ", out double width, out double height);
        Assert.Equal(118.9, width, 3);
        Assert.Equal(84.1, height, 3);
    }

    [Theory]
    [InlineData("A5", "landscape")]
    [InlineData("letter", "landscape")]
    [InlineData("A3", "diagonal")]
    public void UnknownInputRejected(string name, string orientation)
        => Assert.Throws<ArgumentException>(() => SheetSizes.Resolve(name, orientation, out _, out _));

    [Fact]
    public void SmallestContainingPicksTheTightestFit()
    {
        Assert.Equal("A4", SheetSizes.SmallestContaining(20.0, 14.0));
        Assert.Equal("A3", SheetSizes.SmallestContaining(35.0, 25.0));
        Assert.Equal("A0", SheetSizes.SmallestContaining(100.0, 80.0));
    }

    [Fact]
    public void SmallestContainingReturnsNullWhenNothingFits()
        => Assert.Null(SheetSizes.SmallestContaining(500.0, 500.0));
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run from `bridge/`: `dotnet test tests/Bimwright.Ipt.Tests -c Debug --filter SheetSizesTests`
Expected: build FAILS — `SheetSizes` does not exist.

- [ ] **Step 3: Write the implementation**

Create `bridge/src/shared/Infrastructure/SheetSizes.cs`:

```csharp
using System;

namespace Bimwright.Ipt.Shared.Infrastructure;

/// <summary>ISO A-series sheet dimensions in centimetres, Inventor's internal length unit.</summary>
public static class SheetSizes
{
    public static readonly string[] Names = { "A4", "A3", "A2", "A1", "A0" };

    // Short edge, long edge, in centimetres, in the same order as Names.
    private static readonly double[][] Dimensions =
    {
        new[] { 21.0, 29.7 },
        new[] { 29.7, 42.0 },
        new[] { 42.0, 59.4 },
        new[] { 59.4, 84.1 },
        new[] { 84.1, 118.9 }
    };

    public static void Resolve(string? name, string? orientation, out double widthCm, out double heightCm)
    {
        string sheet = (string.IsNullOrWhiteSpace(name) ? "A3" : name!).Trim().ToUpperInvariant();
        int index = Array.IndexOf(Names, sheet);
        if (index < 0)
            throw new ArgumentException("Unknown sheet_size '" + sheet + "'. Use A4, A3, A2, A1 or A0.");
        string mode = (string.IsNullOrWhiteSpace(orientation) ? "landscape" : orientation!).Trim().ToLowerInvariant();
        if (mode == "landscape") { widthCm = Dimensions[index][1]; heightCm = Dimensions[index][0]; }
        else if (mode == "portrait") { widthCm = Dimensions[index][0]; heightCm = Dimensions[index][1]; }
        else throw new ArgumentException("orientation must be 'landscape' or 'portrait'.");
    }

    /// <summary>Smallest listed sheet whose landscape orientation contains the given size, or null.</summary>
    public static string? SmallestContaining(double widthCm, double heightCm)
    {
        for (int i = 0; i < Names.Length; i++)
            if (Dimensions[i][1] >= widthCm && Dimensions[i][0] >= heightCm) return Names[i];
        return null;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run from `bridge/`: `dotnet test tests/Bimwright.Ipt.Tests -c Debug --filter SheetSizesTests`
Expected: PASS, all tests.

- [ ] **Step 5: Commit**

```bash
git add bridge/src/shared/Infrastructure/SheetSizes.cs bridge/tests/Bimwright.Ipt.Tests/SheetSizesTests.cs
git commit -m "feat(drawing): ISO A-series sheet size table in sheet centimetres"
```

---

### Task 3: The sheet planner

The core of D1: given each view's extent at scale 1, pick the largest normalised scale whose layout fits inside the usable area with a dimensioning gutter, and return the centre point of every view.

**Files:**
- Create: `bridge/src/shared/Infrastructure/SheetPlanner.cs`
- Test: `bridge/tests/Bimwright.Ipt.Tests/SheetPlannerTests.cs`

**Interfaces:**
- Consumes: `ViewKind`, `ProjectionAngle`, `ViewSlot`, `DrawingViewSet.Slot` from Task 1; `SheetSizes.SmallestContaining` from Task 2.
- Produces:
  - `sealed class ViewExtent` — constructor `ViewExtent(ViewKind kind, double width, double height)` (centimetres at scale 1), properties `Kind`, `Width`, `Height`
  - `sealed class PlannedView` — constructor `PlannedView(ViewKind kind, double centerX, double centerY)`, properties `Kind`, `CenterX`, `CenterY`
  - `sealed class SheetPlanResult` — properties `double Scale`, `IReadOnlyList<PlannedView>? Views`, `bool Fits`, `double RequiredWidthCm`, `double RequiredHeightCm`, `string? SuggestedSheetSize`
  - `static class SheetPlanner` — `double[] Ladder`, `double ReferenceScale`, `const double MinGutterCm`, `const double MinMarginCm`, `SheetPlanResult Plan(double sheetWidth, double sheetHeight, double reservedBottom, IReadOnlyList<ViewExtent> extents, ProjectionAngle projection, double gutter, double? fixedScale = null)`

`fixedScale` pins the planner to one scale instead of searching the ladder. The handler needs it twice: when the caller passed an explicit `scale`, and when re-planning positions against the views it has already scaled.

Fit failure is **not** an exception — it comes back as `Fits == false` so the handler can turn it into a `NO_FITTING_SCALE` payload. Invalid input still throws `ArgumentException`.

- [ ] **Step 1: Write the failing test**

Create `bridge/tests/Bimwright.Ipt.Tests/SheetPlannerTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using Bimwright.Ipt.Shared.Infrastructure;

namespace Bimwright.Ipt.Tests;

public sealed class SheetPlannerTests
{
    private const double A3Width = 42.0, A3Height = 29.7, Footer = 4.0, Gutter = 1.5;

    private static ViewExtent[] Quad(double size) => new[]
    {
        new ViewExtent(ViewKind.Front, size, size),
        new ViewExtent(ViewKind.Top, size, size),
        new ViewExtent(ViewKind.Right, size, size),
        new ViewExtent(ViewKind.Iso, size, size)
    };

    private static PlannedView Find(SheetPlanResult result, ViewKind kind)
    {
        foreach (var view in result.Views!) if (view.Kind == kind) return view;
        throw new InvalidOperationException("View " + kind + " not planned.");
    }

    [Fact]
    public void LadderIsDescendingAndNormalised()
    {
        Assert.Equal(10.0, SheetPlanner.Ladder[0]);
        Assert.Equal(0.002, SheetPlanner.ReferenceScale, 6);
        for (int i = 1; i < SheetPlanner.Ladder.Length; i++)
            Assert.True(SheetPlanner.Ladder[i] < SheetPlanner.Ladder[i - 1]);
    }

    [Fact]
    public void SmallPartGetsTheLargestScaleThatStillFits()
    {
        // 1 cm views at 10:1 need 3 columns of 10 cm: far wider than A3. 5:1 needs 3x5 cm plus gutters.
        var result = SheetPlanner.Plan(A3Width, A3Height, Footer, Quad(1.0), ProjectionAngle.First, Gutter);
        Assert.True(result.Fits);
        Assert.Equal(5.0, result.Scale, 6);
    }

    [Fact]
    public void LargePartIsScaledDown()
    {
        var result = SheetPlanner.Plan(A3Width, A3Height, Footer, Quad(100.0), ProjectionAngle.First, Gutter);
        Assert.True(result.Fits);
        Assert.True(result.Scale <= 0.1);
        Assert.Contains(result.Scale, SheetPlanner.Ladder);
    }

    [Fact]
    public void PlannedViewsStayInsideTheUsableArea()
    {
        var result = SheetPlanner.Plan(A3Width, A3Height, Footer, Quad(5.0), ProjectionAngle.First, Gutter);
        Assert.True(result.Fits);
        foreach (var view in result.Views!)
        {
            double half = 5.0 * result.Scale / 2;
            Assert.True(view.CenterX - half >= SheetPlanner.MinMarginCm - 1e-9);
            Assert.True(view.CenterX + half <= A3Width - SheetPlanner.MinMarginCm + 1e-9);
            Assert.True(view.CenterY - half >= Footer - 1e-9);
            Assert.True(view.CenterY + half <= A3Height - SheetPlanner.MinMarginCm + 1e-9);
        }
    }

    [Fact]
    public void GutterIsHonouredBetweenNeighbours()
    {
        var result = SheetPlanner.Plan(A3Width, A3Height, Footer, Quad(5.0), ProjectionAngle.First, Gutter);
        var front = Find(result, ViewKind.Front);
        var top = Find(result, ViewKind.Top);
        double extent = 5.0 * result.Scale;
        Assert.True(Math.Abs(front.CenterY - top.CenterY) >= extent + Gutter - 1e-9);
    }

    [Fact]
    public void PlanSurvivesDrawingLayoutValidation()
    {
        var result = SheetPlanner.Plan(A3Width, A3Height, Footer, Quad(5.0), ProjectionAngle.First, Gutter);
        double extent = 5.0 * result.Scale;
        var rectangles = new List<double[]>();
        foreach (var view in result.Views!)
            rectangles.Add(new[] { view.CenterX, view.CenterY, extent, extent });
        DrawingLayout.Validate(A3Width, A3Height, rectangles.ToArray(), Footer);
    }

    [Fact]
    public void FirstAnglePutsThePlanViewBelowTheFront()
    {
        var result = SheetPlanner.Plan(A3Width, A3Height, Footer, Quad(5.0), ProjectionAngle.First, Gutter);
        Assert.True(Find(result, ViewKind.Top).CenterY < Find(result, ViewKind.Front).CenterY);
        Assert.True(Find(result, ViewKind.Right).CenterX < Find(result, ViewKind.Front).CenterX);
    }

    [Fact]
    public void ThirdAnglePutsThePlanViewAboveTheFront()
    {
        var result = SheetPlanner.Plan(A3Width, A3Height, Footer, Quad(5.0), ProjectionAngle.Third, Gutter);
        Assert.True(Find(result, ViewKind.Top).CenterY > Find(result, ViewKind.Front).CenterY);
        Assert.True(Find(result, ViewKind.Right).CenterX > Find(result, ViewKind.Front).CenterX);
    }

    [Fact]
    public void ProjectedViewsStayAlignedWithTheFront()
    {
        var result = SheetPlanner.Plan(A3Width, A3Height, Footer,
            new[]
            {
                new ViewExtent(ViewKind.Front, 4.0, 3.0),
                new ViewExtent(ViewKind.Top, 4.0, 2.0),
                new ViewExtent(ViewKind.Right, 1.5, 3.0)
            }, ProjectionAngle.First, Gutter);
        var front = Find(result, ViewKind.Front);
        Assert.Equal(front.CenterX, Find(result, ViewKind.Top).CenterX, 6);
        Assert.Equal(front.CenterY, Find(result, ViewKind.Right).CenterY, 6);
    }

    [Fact]
    public void SingleViewIsPlanned()
    {
        var result = SheetPlanner.Plan(A3Width, A3Height, Footer,
            new[] { new ViewExtent(ViewKind.Front, 10.0, 10.0) }, ProjectionAngle.First, Gutter);
        Assert.True(result.Fits);
        Assert.Single(result.Views!);
    }

    [Fact]
    public void SixViewsArePlanned()
    {
        var result = SheetPlanner.Plan(A3Width, A3Height, Footer,
            new[]
            {
                new ViewExtent(ViewKind.Front, 2.0, 2.0), new ViewExtent(ViewKind.Back, 2.0, 2.0),
                new ViewExtent(ViewKind.Top, 2.0, 2.0), new ViewExtent(ViewKind.Bottom, 2.0, 2.0),
                new ViewExtent(ViewKind.Left, 2.0, 2.0), new ViewExtent(ViewKind.Right, 2.0, 2.0)
            }, ProjectionAngle.First, Gutter);
        Assert.True(result.Fits);
        Assert.Equal(6, result.Views!.Count);
    }

    [Fact]
    public void NothingFitsReportsRequiredSizeAndSuggestsASheet()
    {
        // 10 m views: even 1:500 leaves a block wider than A4.
        var result = SheetPlanner.Plan(21.0, 29.7, Footer,
            new[] { new ViewExtent(ViewKind.Front, 1000.0, 1000.0) }, ProjectionAngle.First, Gutter);
        Assert.False(result.Fits);
        Assert.Null(result.Views);
        Assert.True(result.RequiredWidthCm > 0);
        Assert.True(result.RequiredHeightCm > 0);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(0)]
    [InlineData(-3)]
    public void DegenerateExtentRejected(double size)
        => Assert.Throws<ArgumentException>(() => new ViewExtent(ViewKind.Front, size, 1.0));

    [Fact]
    public void GutterBelowTheOverlapRuleRejected()
        => Assert.Throws<ArgumentException>(() => SheetPlanner.Plan(A3Width, A3Height, Footer,
            Quad(1.0), ProjectionAngle.First, 0.1));

    [Fact]
    public void FixedScaleSkipsTheLadderSearch()
    {
        var result = SheetPlanner.Plan(A3Width, A3Height, Footer, Quad(1.0), ProjectionAngle.First, Gutter, 1.0);
        Assert.True(result.Fits);
        Assert.Equal(1.0, result.Scale, 6);   // not 5.0, which the ladder search would have picked
    }

    [Fact]
    public void FixedScaleThatDoesNotFitReportsFailureWithoutFallingBack()
    {
        var result = SheetPlanner.Plan(A3Width, A3Height, Footer, Quad(1.0), ProjectionAngle.First, Gutter, 10.0);
        Assert.False(result.Fits);
        Assert.Equal(10.0, result.Scale, 6);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    public void InvalidFixedScaleRejected(double scale)
        => Assert.Throws<ArgumentException>(() => SheetPlanner.Plan(A3Width, A3Height, Footer,
            Quad(1.0), ProjectionAngle.First, Gutter, scale));

    [Fact]
    public void EmptyViewListRejected()
        => Assert.Throws<ArgumentException>(() => SheetPlanner.Plan(A3Width, A3Height, Footer,
            new ViewExtent[0], ProjectionAngle.First, Gutter));

    [Theory]
    [InlineData("A4", "landscape")]
    [InlineData("A3", "portrait")]
    [InlineData("A0", "landscape")]
    public void EveryFormatPlansASmallPart(string sheet, string orientation)
    {
        SheetSizes.Resolve(sheet, orientation, out double width, out double height);
        var result = SheetPlanner.Plan(width, height, Footer, Quad(2.0), ProjectionAngle.First, Gutter);
        Assert.True(result.Fits);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run from `bridge/`: `dotnet test tests/Bimwright.Ipt.Tests -c Debug --filter SheetPlannerTests`
Expected: build FAILS — `ViewExtent`, `PlannedView`, `SheetPlanResult`, `SheetPlanner` do not exist.

- [ ] **Step 3: Write the implementation**

Create `bridge/src/shared/Infrastructure/SheetPlanner.cs`:

```csharp
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

    /// <param name="fixedScale">
    /// When set, the planner tries only this scale instead of searching the ladder. The handler needs
    /// it for an explicitly requested scale, and to re-plan positions against views it already scaled.
    /// </param>
    public static SheetPlanResult Plan(double sheetWidth, double sheetHeight, double reservedBottom,
        IReadOnlyList<ViewExtent> extents, ProjectionAngle projection, double gutter, double? fixedScale = null)
    {
        if (extents == null || extents.Count == 0)
            throw new ArgumentException("At least one view extent is required.");
        if (!ViewExtent.IsFinite(sheetWidth) || !ViewExtent.IsFinite(sheetHeight) || sheetWidth <= 0 || sheetHeight <= 0)
            throw new ArgumentException("Sheet size must be finite and positive.");
        if (!ViewExtent.IsFinite(reservedBottom) || reservedBottom < 0)
            throw new ArgumentException("Invalid title-block reserve.");
        if (!ViewExtent.IsFinite(gutter) || gutter < MinGutterCm)
            throw new ArgumentException("gutter must be at least " + MinGutterCm + " cm.");
        if (fixedScale.HasValue && (!ViewExtent.IsFinite(fixedScale.Value) || fixedScale.Value <= 0))
            throw new ArgumentException("fixedScale must be finite and greater than zero.");

        var kinds = new List<ViewKind>();
        foreach (var extent in extents) kinds.Add(extent.Kind);

        double outer = Math.Max(gutter, MinMarginCm);
        double usableWidth = sheetWidth - 2 * outer;
        double usableHeight = sheetHeight - reservedBottom - outer;
        var result = new SheetPlanResult();
        double blockWidth = 0, blockHeight = 0;

        double[] candidates = fixedScale.HasValue ? new[] { fixedScale.Value } : Ladder;
        foreach (double scale in candidates)
        {
            IReadOnlyList<PlannedView>? views;
            bool fits = TryLayout(extents, kinds, projection, scale, gutter, usableWidth, usableHeight,
                outer, reservedBottom, out views, out blockWidth, out blockHeight);
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
        result.SuggestedSheetSize = SheetSizes.SmallestContaining(
            blockWidth + 2 * outer, blockHeight + reservedBottom + outer);
        return result;
    }

    private static bool TryLayout(IReadOnlyList<ViewExtent> extents, IReadOnlyList<ViewKind> kinds,
        ProjectionAngle projection, double scale, double gutter, double usableWidth, double usableHeight,
        double outer, double reservedBottom,
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
        double cursor = outer + (usableWidth - blockWidth) / 2;
        foreach (int column in columns)
        {
            columnCenter[column] = cursor + columnWidth[column] / 2;
            cursor += columnWidth[column] + gutter;
        }

        var rowCenter = new Dictionary<int, double>();
        cursor = reservedBottom + (usableHeight - blockHeight) / 2;
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
```

- [ ] **Step 4: Run tests to verify they pass**

Run from `bridge/`: `dotnet test tests/Bimwright.Ipt.Tests -c Debug --filter SheetPlannerTests`
Expected: PASS, all tests.

If `SmallPartGetsTheLargestScaleThatStillFits` fails on the exact scale, do **not** loosen the assertion: recompute by hand from the block formula (three columns of `1.0 * scale` plus two gutters of 1.5, against `42 - 2 * 1.5`) and fix whichever side is wrong.

- [ ] **Step 5: Run the whole suite**

Run from `bridge/`: `dotnet test tests/Bimwright.Ipt.Tests -c Debug`
Expected: PASS. In particular `DrawingLayoutTests` must be untouched and still green.

- [ ] **Step 6: Commit**

```bash
git add bridge/src/shared/Infrastructure/SheetPlanner.cs bridge/tests/Bimwright.Ipt.Tests/SheetPlannerTests.cs
git commit -m "feat(drawing): scale-fitting sheet planner with dimensioning gutter"
```

---

### Task 4: Wire the planner into the Inventor handler

Replace the hardcoded A3 layout with: resolve arguments, set sheet and projection, create views at the reference scale, measure, plan, apply, validate.

**Files:**
- Modify: `bridge/src/shared/Handlers/Core/CreateDrawingHandler.cs` (the whole `Execute` body between the argument parsing and the result payload)

**Interfaces:**
- Consumes: `DrawingViewSet.Parse`, `DrawingViewSet.Slot`, `ViewKind`, `ProjectionAngle` (Task 1); `SheetSizes.Resolve` (Task 2); `SheetPlanner.Plan`, `SheetPlanner.ReferenceScale`, `ViewExtent`, `SheetPlanResult` (Task 3); `UnitConvert.MmToCm`, `UnitConvert.CmToMm` from `Bimwright.Ipt.Shared.Handlers`; existing `DrawingLayout.ValidateScale` and `DrawingLayout.Validate`.
- Produces: the wire payload of `create_drawing_safe`, extended with `projection`, `views`, `gutter_mm` and the resolved `scale`.

This task has no unit test of its own: the file is inside `#if INVENTOR2027` and the test project does not compile Inventor handlers. Its verification is the add-in build plus the live smoke test in Task 6. All the logic worth testing was deliberately pushed into Tasks 1–3.

- [ ] **Step 1: Replace the argument parsing**

In `CreateDrawingHandler.Execute`, replace these three lines:

```csharp
        if (p["scale"]?.Type != JTokenType.Float && p["scale"]?.Type != JTokenType.Integer) throw new ArgumentException("Numeric scale required.");
        double scale = DrawingLayout.ValidateScale((double)p["scale"]!);
        if (p["preview"] != null && p["preview"]!.Type != JTokenType.Boolean) throw new ArgumentException("preview must be boolean.");
        bool preview = (bool?)p["preview"] ?? true;
```

with:

```csharp
        double? requestedScale = null;
        if (p["scale"] != null && p["scale"]!.Type != JTokenType.Null)
        {
            if (p["scale"]!.Type != JTokenType.Float && p["scale"]!.Type != JTokenType.Integer)
                throw new ArgumentException("scale must be numeric, or omitted for automatic scaling.");
            requestedScale = DrawingLayout.ValidateScale((double)p["scale"]!);
        }
        if (p["preview"] != null && p["preview"]!.Type != JTokenType.Boolean) throw new ArgumentException("preview must be boolean.");
        bool preview = (bool?)p["preview"] ?? true;
        var kinds = DrawingViewSet.Parse((string?)p["views"]);
        var projection = ParseProjection((string?)p["projection"]);
        double gutterMm = 15;
        if (p["gutter_mm"] != null && p["gutter_mm"]!.Type != JTokenType.Null)
        {
            if (p["gutter_mm"]!.Type != JTokenType.Float && p["gutter_mm"]!.Type != JTokenType.Integer)
                throw new ArgumentException("gutter_mm must be numeric.");
            gutterMm = (double)p["gutter_mm"]!;
        }
        double gutter = UnitConvert.MmToCm(gutterMm);
        string sheetSizeName = ((string?)p["sheet_size"] ?? "A3").Trim().ToUpperInvariant();
        string orientationName = ((string?)p["orientation"] ?? "landscape").Trim().ToLowerInvariant();
        SheetSizes.Resolve(sheetSizeName, orientationName, out _, out _);   // fail fast before creating anything
```

- [ ] **Step 2: Add the private helpers at the end of the class**

Add these members to `CreateDrawingHandler`, after `Execute`:

```csharp
    private static ProjectionAngle ParseProjection(string? value)
    {
        string text = (string.IsNullOrWhiteSpace(value) ? "first" : value!).Trim().ToLowerInvariant();
        if (text == "first") return ProjectionAngle.First;
        if (text == "third") return ProjectionAngle.Third;
        throw new ArgumentException("projection must be 'first' or 'third'.");
    }

    private static DrawingSheetSizeEnum SheetSizeEnum(string name)
    {
        switch (name)
        {
            case "A4": return DrawingSheetSizeEnum.kA4DrawingSheetSize;
            case "A3": return DrawingSheetSizeEnum.kA3DrawingSheetSize;
            case "A2": return DrawingSheetSizeEnum.kA2DrawingSheetSize;
            case "A1": return DrawingSheetSizeEnum.kA1DrawingSheetSize;
            case "A0": return DrawingSheetSizeEnum.kA0DrawingSheetSize;
            default: throw new ArgumentException("Unknown sheet_size '" + name + "'. Use A4, A3, A2, A1 or A0.");
        }
    }

    private static ViewOrientationTypeEnum Orientation(ViewKind kind)
    {
        switch (kind)
        {
            case ViewKind.Front: return ViewOrientationTypeEnum.kFrontViewOrientation;
            case ViewKind.Back: return ViewOrientationTypeEnum.kBackViewOrientation;
            case ViewKind.Top: return ViewOrientationTypeEnum.kTopViewOrientation;
            case ViewKind.Bottom: return ViewOrientationTypeEnum.kBottomViewOrientation;
            case ViewKind.Left: return ViewOrientationTypeEnum.kLeftViewOrientation;
            case ViewKind.Right: return ViewOrientationTypeEnum.kRightViewOrientation;
            case ViewKind.Iso: return ViewOrientationTypeEnum.kIsoTopRightViewOrientation;
            default: throw new ArgumentException("Unsupported view kind.");
        }
    }

    /// <summary>
    /// Force the drawing's projection convention. The template's own standard decides whether a view
    /// placed above the front view reads as the plan (third angle) or the bottom view (first angle),
    /// so leaving it to the host silently produces mirrored drawings on differently configured machines.
    /// </summary>
    private static void ApplyProjection(DrawingDocument drawing, ProjectionAngle projection)
    {
        try
        {
            var standard = drawing.StylesManager.ActiveStandardStyle;
            standard.ProjectionType = projection == ProjectionAngle.First
                ? ProjectionTypeEnum.kFirstAngleProjectionType
                : ProjectionTypeEnum.kThirdAngleProjectionType;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "PROJECTION_UNAVAILABLE: the drawing standard does not accept a projection change: " + ex.Message);
        }
    }
```

- [ ] **Step 3: Replace the view creation block**

Replace the block from `sheet.Size = DrawingSheetSizeEnum.kA3DrawingSheetSize;` down to and including the `DrawingLayout.Validate(...)` call with:

```csharp
            var sheet = drawing.ActiveSheet;
            sheet.Size = SheetSizeEnum(sheetSizeName);
            sheet.Orientation = orientationName == "portrait"
                ? PageOrientationTypeEnum.kPortraitPageOrientation
                : PageOrientationTypeEnum.kLandscapePageOrientation;
            ApplyProjection(drawing, projection);

            var geo = app.TransientGeometry;
            var style = DrawingViewStyleEnum.kHiddenLineRemovedDrawingViewStyle;
            double reference = SheetPlanner.ReferenceScale;
            // Provisional pitch only has to make each projected view's direction unambiguous:
            // AddProjectedView derives the direction from the position relative to its parent.
            double pitchX = sheet.Width / 5, pitchY = sheet.Height / 5;
            double centerX = sheet.Width / 2, centerY = sheet.Height / 2;

            var created = new System.Collections.Generic.Dictionary<ViewKind, DrawingView>();
            DrawingView? baseView = null;
            foreach (var kind in kinds)
            {
                var slot = DrawingViewSet.Slot(kind, projection, kinds);
                var at = geo.CreatePoint2d(centerX + slot.Column * pitchX, centerY + slot.Row * pitchY);
                DrawingView view;
                if (DrawingViewSet.IsProjected(kind))
                {
                    if (baseView == null) throw new ArgumentException("Projected views require 'front'.");
                    view = sheet.DrawingViews.AddProjectedView(baseView, at, style);
                }
                else
                {
                    view = sheet.DrawingViews.AddBaseView((Inventor._Document)source, at, reference, Orientation(kind), style);
                    if (kind == ViewKind.Front) baseView = view;
                }
                created[kind] = view;
            }
            foreach (DrawingView view in sheet.DrawingViews) view.ShowLabel = false;
            if (!drawing.Update2()) throw new InvalidOperationException("Drawing update failed.");

            double footer = Math.Max(4, sheet.TitleBlock == null ? 4 : sheet.TitleBlock.RangeBox.MaxPoint.Y + 0.2);

            // View extents scale linearly with view scale, so normalising the measured reference-scale
            // extents to scale 1 lets the planner answer every candidate scale without another update.
            var extents = new System.Collections.Generic.List<ViewExtent>();
            foreach (var kind in kinds)
                extents.Add(new ViewExtent(kind, created[kind].Width / reference, created[kind].Height / reference));

            var plan = SheetPlanner.Plan(sheet.Width, sheet.Height, footer, extents, projection, gutter, requestedScale);
            if (!plan.Fits)
            {
                string needed = UnitConvert.CmToMm(plan.RequiredWidthCm).ToString("F0") + " x "
                    + UnitConvert.CmToMm(plan.RequiredHeightCm).ToString("F0") + " mm";
                throw new InvalidOperationException(requestedScale.HasValue
                    ? "VIEW_OUTSIDE_LAYOUT: at the requested scale the views need " + needed
                      + "; omit scale for automatic scaling, or use a larger sheet_size."
                    : "NO_FITTING_SCALE: the views need " + needed + " even at 1:500"
                      + (plan.SuggestedSheetSize == null ? "; no listed sheet size fits."
                          : "; retry with sheet_size=" + plan.SuggestedSheetSize + "."));
            }
            double scale = plan.Scale;

            // Apply on base views only: projected views inherit their parent's scale.
            foreach (var kind in kinds)
                if (!DrawingViewSet.IsProjected(kind)) created[kind].Scale = scale;
            if (!drawing.Update2()) throw new InvalidOperationException("Drawing update failed after scaling.");

            // Re-plan on the MEASURED geometry, pinned to the scale just applied, so the positions
            // belong to the drawing as it actually is rather than to the predicted layout.
            var applied = SheetPlanner.Plan(sheet.Width, sheet.Height, footer,
                BuildExtents(kinds, created), projection, gutter, scale);
            if (!applied.Fits) throw new InvalidOperationException(
                "VIEW_OUTSIDE_LAYOUT: the measured views do not fit at the planned scale.");
            // Position the front view first: Inventor keeps projected views aligned to their parent,
            // and the planner keeps every projected view in the front view's own row or column.
            foreach (var kind in Ordered(kinds))
                foreach (var planned in applied.Views!)
                    if (planned.Kind == kind)
                        created[kind].Position = geo.CreatePoint2d(planned.CenterX, planned.CenterY);
            if (!drawing.Update2()) throw new InvalidOperationException("Drawing update failed after placement.");

            var views = sheet.DrawingViews.Cast<DrawingView>().ToArray();
            DrawingLayout.Validate(sheet.Width, sheet.Height,
                views.Select(v => new[] { v.Position.X, v.Position.Y, v.Width, v.Height }).ToArray(), footer);
```

Both `SheetPlanner.Plan` calls pass a scale explicitly where one is known: the first pins to `requestedScale` when the caller gave one (otherwise it searches the ladder), the second pins to the scale just applied so it only recomputes **positions** against the measured geometry. Without that pin the second call would be free to pick a different scale from the one the views already carry, and every position would be wrong.

- [ ] **Step 4: Add the two remaining helpers**

Add to `CreateDrawingHandler`, next to the other helpers:

```csharp
    /// <summary>Measured extents of already-scaled views, normalised back to scale 1.</summary>
    private static System.Collections.Generic.List<ViewExtent> BuildExtents(
        ViewKind[] kinds, System.Collections.Generic.Dictionary<ViewKind, DrawingView> created)
    {
        var extents = new System.Collections.Generic.List<ViewExtent>();
        foreach (var kind in kinds)
        {
            var view = created[kind];
            double scale = view.Scale <= 0 ? 1 : view.Scale;
            extents.Add(new ViewExtent(kind, view.Width / scale, view.Height / scale));
        }
        return extents;
    }

    /// <summary>Base views first, so projected children are repositioned against a settled parent.</summary>
    private static System.Collections.Generic.IEnumerable<ViewKind> Ordered(ViewKind[] kinds)
    {
        foreach (var kind in kinds) if (!DrawingViewSet.IsProjected(kind)) yield return kind;
        foreach (var kind in kinds) if (DrawingViewSet.IsProjected(kind)) yield return kind;
    }
```

- [ ] **Step 5: Extend the result payload**

Replace the `var result = new JObject { … };` initialiser with:

```csharp
            var viewNames = new System.Text.StringBuilder();
            foreach (var kind in kinds)
            {
                if (viewNames.Length > 0) viewNames.Append(',');
                viewNames.Append(kind.ToString().ToLowerInvariant());
            }
            var result = new JObject
            {
                ["status"] = preview ? "preview_rolled_back" : "created",
                ["view_count"] = views.Length,
                ["scale"] = scale,
                ["scale_mode"] = requestedScale.HasValue ? "explicit" : "auto",
                ["sheet"] = sheetSizeName + " " + orientationName,
                ["projection"] = projection == ProjectionAngle.First ? "first" : "third",
                ["views"] = viewNames.ToString(),
                ["gutter_mm"] = gutterMm,
                ["document_id"] = preview ? null : drawingId,
                ["manufacturing_ready"] = false,
                ["dimensions_added"] = 0,
                ["reserved_footer_mm"] = UnitConvert.CmToMm(footer)
            };
```

Also add `using Bimwright.Ipt.Shared.Handlers;` to the file's using block if it is not already there, so `UnitConvert` resolves.

- [ ] **Step 6: Build the server and tests**

Run from `bridge/`: `dotnet build src/IptMcp.sln -c Debug` then `dotnet test tests/Bimwright.Ipt.Tests -c Debug`
Expected: both PASS. The handler itself is excluded from these targets; this step proves the new Infrastructure files did not break the Inventor-free build.

- [ ] **Step 7: Build the 2027 add-in**

Run from `bridge/`: `dotnet build src/plugin-inv27 -c Debug`
Expected: PASS. This is the first compilation of the handler changes.

If `ProjectionTypeEnum` or `ActiveStandardStyle.ProjectionType` does not resolve, do not invent a workaround: report the actual member the interop exposes and stop. The spec flags this exact name as needing verification.

- [ ] **Step 8: Commit**

```bash
git add bridge/src/shared/Handlers/Core/CreateDrawingHandler.cs
git commit -m "feat(drawing): sheet size, projection angle, view set and auto scale in the drawing handler"
```

---

### Task 5: Tool surface and discoverability

Expose the new parameters on the MCP tool and make the drawing surface findable by Tool Search.

**Files:**
- Modify: `bridge/src/server/Tools/SafeArtifactTools.cs:19-21` (the `CreateDrawing` tool)
- Modify: `bridge/src/server/ServerInstructions.cs`

**Interfaces:**
- Consumes: the wire payload shape from Task 4.
- Produces: the public MCP signature `inventor_create_drawing_safe(document_id, expected_revision, scale?, sheet_size?, orientation?, projection?, views?, gutter_mm?, preview?)`.

- [ ] **Step 1: Replace the tool method**

In `bridge/src/server/Tools/SafeArtifactTools.cs`, replace the `CreateDrawing` attribute and method with:

```csharp
    [McpServerTool(Name = "inventor_create_drawing_safe"), Description("Create an unsaved drawing from the active up-to-date part or assembly using the host default drawing template. Chooses sheet_size (A4/A3/A2/A1/A0, default A3) and orientation (landscape/portrait). Sets the projection convention explicitly: projection='first' (ISO/UNI, default) or 'third' (ANSI), so the same call produces the same drawing on any machine. views is a comma-separated list of front, back, top, bottom, left, right and iso (default 'front,top,right,iso'); projected views require front. Omit scale for automatic scaling: the largest ISO 5455 scale whose layout fits leaving gutter_mm (default 15) of free space around each view for dimensioning; pass scale to force one. Requires source document_id/revision. Checks view bounds/overlap and source state; preview=true by default closes only the new draft. Commit leaves the draft active. No dimensions, tolerances or manufacturing approval are added; manufacturing_ready=false. Does not save sources. Export separately with inventor_save_artifact format=pdf.")]
    public Task<string> CreateDrawing(string document_id, string expected_revision, double? scale = null,
        string? sheet_size = null, string? orientation = null, string? projection = null,
        string? views = null, double? gutter_mm = null, bool preview = true, CancellationToken ct = default)
        => CheckpointCall("create_drawing_safe", new JObject
        {
            ["document_id"] = document_id,
            ["expected_revision"] = expected_revision,
            ["scale"] = scale,
            ["sheet_size"] = sheet_size,
            ["orientation"] = orientation,
            ["projection"] = projection,
            ["views"] = views,
            ["gutter_mm"] = gutter_mm,
            ["preview"] = preview
        }, ct);
```

- [ ] **Step 2: Add drawing keywords to the server instructions**

In `bridge/src/server/ServerInstructions.cs`, append this sentence to `Text`, following the style of the surrounding lines:

```csharp
        "inventor_create_drawing_safe lays out a production drawing sheet: sheet size A4 to A0, landscape or portrait, first-angle (ISO/UNI) or third-angle (ANSI) projection, a chosen set of front/back/top/bottom/left/right/iso views, and an automatic ISO scale that leaves a dimensioning gutter around every view. It adds no dimensions, no title-block content and no parts list. " +
```

- [ ] **Step 3: Build and test**

Run from `bridge/`: `dotnet build src/IptMcp.sln -c Debug` then `dotnet test tests/Bimwright.Ipt.Tests -c Debug`
Expected: both PASS. `SoToolPolicyTests` and `RegistrationCountTests` must stay green — no tool was added or removed, only its parameters changed.

- [ ] **Step 4: Commit**

```bash
git add bridge/src/server/Tools/SafeArtifactTools.cs bridge/src/server/ServerInstructions.cs
git commit -m "feat(drawing): expose sheet, projection, view set and auto scale on the MCP tool"
```

---

### Task 6: Live verification procedure

`bridge/CLAUDE.md` requires every Inventor-API handler to be smoke-tested against a live session. Three assumptions in this design cannot be checked without Inventor, and the fallback differs for each.

**Files:**
- Modify: `bridge/docs/testing/manual-smoke.md`

**Interfaces:**
- Consumes: the shipped tool from Task 5.
- Produces: nothing in code — a written, repeatable procedure.

- [ ] **Step 1: Append the procedure**

Add to `bridge/docs/testing/manual-smoke.md`, matching the file's existing heading level and style:

```markdown
## Drawing sheet layout (D1)

Run against a live Inventor 2027 with a saved, up-to-date part open.

1. **Reference-scale measurement.** Call `inventor_create_drawing_safe` with `preview=true` and the
   defaults. It must return `scale_mode="auto"` and a scale from the ISO ladder. If the call fails
   while measuring, views cannot be measured at 1:500 and the reference scale must be raised to the
   smallest step that measures reliably.
2. **Linearity.** Call twice with `scale=1` and `scale=0.5` explicitly, and read the view extents
   from the resulting drawing in the Inventor UI. Halving the scale must halve width and height.
   If it does not, the closed-form solve is invalid: replace the single measurement pass with a
   create-measure-retry loop per ladder step. `SheetPlanner` is unchanged either way — only the
   handler's feeding of it changes.
3. **Projection is actually written.** Call with `projection="first"`, commit with `preview=false`,
   and check in the Inventor UI that the drawing standard reports first-angle projection and that
   the plan view sits **below** the front view. Repeat with `projection="third"` and confirm the
   plan view sits above. If the standard cannot be written, the tool must fail with
   `PROJECTION_UNAVAILABLE` rather than produce a drawing.
4. **Fit failure.** Call with `sheet_size="A4"` on a large assembly and confirm the error names a
   larger sheet size instead of asking the caller to guess a scale.
5. **Guards intact.** Confirm that a stale `expected_revision` still returns `STALE_REVISION`, and
   that a failed call leaves no orphan drawing document open.
```

- [ ] **Step 2: Run the procedure**

Execute steps 1–5 above against live Inventor. Record the outcome of each in the pull request or task notes. Do not mark D1 done while any of the three assumptions is unverified.

- [ ] **Step 3: Commit**

```bash
git add bridge/docs/testing/manual-smoke.md
git commit -m "docs: live smoke-test procedure for the drawing sheet layout"
```

---

## Self-Review

**Spec coverage.** Every spec section maps to a task: projection forced explicitly → Tasks 1 and 4; ISO ladder and gutter → Task 3; view set with default and validation → Task 1; single sheet → no task, it is the absence of one; `scale` optional without breaking the signature → Tasks 4 and 5; planner as a pure function → Tasks 1–3; sheet size table and larger-sheet suggestion → Tasks 2 and 3; `NO_FITTING_SCALE` and `PROJECTION_UNAVAILABLE` → Task 4; existing guards preserved → Task 4 and the Global Constraints; the full unit test list → Tasks 1–3; the three live verifications → Task 6.

**Deliberately out of scope**, restated so no task invents them: dimensions, title-block content, centrelines, section and detail views, parts lists, ballooning, multi-sheet, PDF export options.

**Known risk.** Task 4 step 7 is the first time the handler compiles, and `ActiveStandardStyle.ProjectionType` / `ProjectionTypeEnum` is the one interop name this plan asserts without having verified it against the 2027 interop. The plan tells the implementer to stop and report rather than improvise if it does not resolve.
