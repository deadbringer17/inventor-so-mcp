using System;
using System.Collections.Generic;
using Bimwright.Ipt.Shared.Infrastructure;

namespace Bimwright.Ipt.Tests;

public sealed class UsableAreaTests
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
    public void AReservedBottomIsTheSheetAboveTheTitleBlock()
    {
        var area = UsableArea.FromReservedBottom(A3Width, A3Height, Footer);
        Assert.Equal(0, area.XMinCm);
        Assert.Equal(Footer, area.YMinCm);
        Assert.Equal(A3Width, area.XMaxCm);
        Assert.Equal(A3Height, area.YMaxCm);
        Assert.Equal(0, area.ReservedWidthCm, 6);
        Assert.Equal(Footer, area.ReservedHeightCm, 6);
    }

    [Theory]
    [InlineData(3.9)]                 // below the 4 cm floor
    [InlineData(double.NaN)]
    [InlineData(A3Height)]            // swallows the whole sheet
    public void ImpossibleBottomReservesAreRefused(double reservedBottom)
        => Assert.Throws<ArgumentException>(() => UsableArea.FromReservedBottom(A3Width, A3Height, reservedBottom));

    [Theory]
    [InlineData(-1, 4, 42, 29.7)]     // starts left of the sheet
    [InlineData(0, 4, 43, 29.7)]      // ends right of the sheet
    [InlineData(0, 4, 0, 29.7)]       // no width
    [InlineData(0, 29.7, 42, 29.7)]   // no height
    public void AreasOutsideTheSheetAreRefused(double xMin, double yMin, double xMax, double yMax)
        => Assert.Throws<ArgumentException>(() => new UsableArea(A3Width, A3Height, xMin, yMin, xMax, yMax));

    [Fact]
    public void TheAreaOverloadReproducesTheReservedBottomLayoutExactly()
    {
        var legacy = SheetPlanner.Plan(A3Width, A3Height, Footer, Quad(1.0), ProjectionAngle.First, Gutter);
        var viaArea = SheetPlanner.Plan(UsableArea.FromReservedBottom(A3Width, A3Height, Footer),
            Quad(1.0), ProjectionAngle.First, Gutter);
        Assert.Equal(legacy.Scale, viaArea.Scale, 6);
        foreach (var kind in new[] { ViewKind.Front, ViewKind.Top, ViewKind.Right, ViewKind.Iso })
        {
            Assert.Equal(Find(legacy, kind).CenterX, Find(viaArea, kind).CenterX, 6);
            Assert.Equal(Find(legacy, kind).CenterY, Find(viaArea, kind).CenterY, 6);
        }
    }

    [Fact]
    public void ARightHandTitleBlockColumnPushesEveryViewLeftOfIt()
    {
        // 8 cm column down the right-hand edge: the shape a bottom reserve cannot express.
        var area = new UsableArea(A3Width, A3Height, 0, 0, A3Width - 8, A3Height);
        var result = SheetPlanner.Plan(area, Quad(1.0), ProjectionAngle.First, Gutter);
        Assert.True(result.Fits);
        foreach (var view in result.Views!)
            Assert.True(view.CenterX < A3Width - 8, "A view was planned inside the title-block column.");
    }

    [Fact]
    public void ASmallerUsableAreaForcesASmallerScaleThanTheBareSheet()
    {
        var whole = SheetPlanner.Plan(UsableArea.FromReservedBottom(A3Width, A3Height, Footer),
            Quad(1.0), ProjectionAngle.First, Gutter);
        var cramped = SheetPlanner.Plan(new UsableArea(A3Width, A3Height, 2, 1.5, 20, 20),
            Quad(1.0), ProjectionAngle.First, Gutter);
        Assert.True(cramped.Fits);
        Assert.True(cramped.Scale < whole.Scale);
    }

    [Fact]
    public void TheSuggestedSheetStillAccountsForTheReservedTitleBlockSpace()
    {
        // A huge part on a sheet whose right-hand column is reserved: the suggestion has to include
        // that column, not just the block of views.
        var area = new UsableArea(A3Width, A3Height, 0, 4, A3Width - 8, A3Height);
        var result = SheetPlanner.Plan(area, Quad(60.0), ProjectionAngle.First, Gutter, fixedScale: 1.0);
        Assert.False(result.Fits);
        Assert.Null(result.SuggestedSheetSize);   // nothing listed is big enough, and it says so

        var modest = SheetPlanner.Plan(area, Quad(12.0), ProjectionAngle.First, Gutter, fixedScale: 1.0);
        Assert.False(modest.Fits);
        Assert.Equal("A1", modest.SuggestedSheetSize);
    }

    [Fact]
    public void ViewsInsideTheDeclaredAreaValidateAndViewsOverTheTitleBlockDoNot()
    {
        var area = new UsableArea(A3Width, A3Height, 2, 1.5, 34, 28.2);
        DrawingLayout.Validate(area, new[] { new[] { 18d, 15d, 10d, 10d } });
        var error = Assert.Throws<CodedFailureException>(() =>
            DrawingLayout.Validate(area, new[] { new[] { 38d, 15d, 6d, 6d } }));
        Assert.Equal("VIEW_OUTSIDE_LAYOUT", error.Code);
    }

    [Fact]
    public void AManifestClaimingTheWholeSheetStillKeepsTheSheetEdgeMargin()
    {
        var area = new UsableArea(A3Width, A3Height, 0, 0, A3Width, A3Height);
        // Centre 0.9 cm from the left edge with a 1 cm wide view: 0.4 cm of paper margin.
        Assert.Throws<CodedFailureException>(() =>
            DrawingLayout.Validate(area, new[] { new[] { 0.9d, 15d, 1d, 1d } }));
    }
}
