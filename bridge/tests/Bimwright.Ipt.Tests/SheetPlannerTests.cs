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
    public void BottomMostViewClearsReservedBottomByAtLeastTheGutter()
    {
        // Regression for the missing bottom-side corridor: the planner used to size usableHeight as
        // sheetHeight - reservedBottom - outer (one margin, for the top) and start the row block
        // exactly at reservedBottom, so a view block that exactly filled the usable height sat flush
        // against the title-block band while top/left/right kept their margin. Spec decision 2 puts
        // the dimensioning corridor around each view, on all four sides, not three.
        var result = SheetPlanner.Plan(A3Width, A3Height, Footer, Quad(5.0), ProjectionAngle.First, Gutter);
        Assert.True(result.Fits);
        double half = 5.0 * result.Scale / 2;
        double lowestBottomEdge = double.MaxValue;
        foreach (var view in result.Views!)
            lowestBottomEdge = Math.Min(lowestBottomEdge, view.CenterY - half);
        Assert.True(lowestBottomEdge >= Footer + Gutter - 1e-9);
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
        // A4 portrait leaves 18 cm of usable width, so a 200 m view still needs 40 cm at 1:500.
        var result = SheetPlanner.Plan(21.0, 29.7, Footer,
            new[] { new ViewExtent(ViewKind.Front, 20000.0, 20000.0) }, ProjectionAngle.First, Gutter);
        Assert.False(result.Fits);
        Assert.Null(result.Views);
        Assert.Equal(40.0, result.RequiredWidthCm, 6);
        Assert.Equal(40.0, result.RequiredHeightCm, 6);
        Assert.Equal("A1", result.SuggestedSheetSize);
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
    public void ReservedBottomBelowTheTitleBlockFloorRejected()
        => Assert.Throws<ArgumentException>(() => SheetPlanner.Plan(A3Width, A3Height,
            SheetPlanner.MinReservedBottomCm - 0.1, Quad(1.0), ProjectionAngle.First, Gutter));

    [Fact]
    public void ReservedBottomAtTheTitleBlockFloorIsAccepted()
    {
        var result = SheetPlanner.Plan(A3Width, A3Height, SheetPlanner.MinReservedBottomCm,
            Quad(1.0), ProjectionAngle.First, Gutter);
        Assert.True(result.Fits);
    }

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
