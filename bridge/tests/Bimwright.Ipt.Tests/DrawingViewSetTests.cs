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
        // Back is a BASE view, not a projected one. AddProjectedView derives a child's orientation
        // from the DIRECTION to its parent, not the distance, so treating back as "projected two
        // steps out" made it indistinguishable from a projected side view sharing that direction
        // (views=front,right,back silently produced two right views under first angle). Back is
        // created with AddBaseView and kBackViewOrientation instead, exactly like front and iso.
        Assert.False(DrawingViewSet.IsProjected(ViewKind.Front));
        Assert.False(DrawingViewSet.IsProjected(ViewKind.Back));
        Assert.False(DrawingViewSet.IsProjected(ViewKind.Iso));
        foreach (var kind in new[] { ViewKind.Top, ViewKind.Bottom, ViewKind.Left, ViewKind.Right })
            Assert.True(DrawingViewSet.IsProjected(kind));
    }

    [Fact]
    public void BackAloneIsAllowed()
        // A base view needs no parent, so unlike top/bottom/left/right this is legal without 'front'.
        => Assert.Equal(new[] { ViewKind.Back }, DrawingViewSet.Parse("back"));

    [Fact]
    public void BackSitsTwoColumnsFromFrontUnderBothProjections()
    {
        // The slot arithmetic itself (2 * side) is unchanged by this fix; what changed is HOW that
        // slot is realized in Inventor (AddBaseView instead of AddProjectedView). This test pins the
        // slot so a future regression on the arithmetic side is still caught, even though the
        // AddProjectedView-vs-AddBaseView distinction can only be observed against live Inventor.
        var all = DrawingViewSet.Parse("front,right,back");
        var firstAngle = DrawingViewSet.Slot(ViewKind.Back, ProjectionAngle.First, all);
        Assert.Equal(-2, firstAngle.Column);
        Assert.Equal(0, firstAngle.Row);
        var thirdAngle = DrawingViewSet.Slot(ViewKind.Back, ProjectionAngle.Third, all);
        Assert.Equal(2, thirdAngle.Column);
        Assert.Equal(0, thirdAngle.Row);

        // Back must never collide with the projected 'right' view's slot under either convention.
        var right = DrawingViewSet.Slot(ViewKind.Right, ProjectionAngle.First, all);
        Assert.NotEqual((firstAngle.Column, firstAngle.Row), (right.Column, right.Row));
    }
}
