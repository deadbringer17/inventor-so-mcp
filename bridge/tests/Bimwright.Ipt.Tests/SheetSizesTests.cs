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
