using Bimwright.Ipt.Shared.Infrastructure;
namespace Bimwright.Ipt.Tests;
public sealed class DrawingLayoutTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(101)]
    public void InvalidScaleRejected(double scale) => Assert.Throws<ArgumentException>(() => DrawingLayout.ValidateScale(scale));
    [Fact]
    public void SeparatedViewsFit() => DrawingLayout.Validate(42, 29.7, new[] { new[] { 10d, 10d, 4d, 4d }, new[] { 30d, 20d, 4d, 4d } });
    [Fact]
    public void OverlapRejected() => Assert.Throws<InvalidOperationException>(() => DrawingLayout.Validate(42,29.7,
        new[] { new[] { 10d, 10d, 4d, 4d }, new[] { 11d, 11d, 4d, 4d } }));
    [Fact]
    public void TitleBlockRegionReserved() => Assert.Throws<InvalidOperationException>(() => DrawingLayout.Validate(42,29.7,
        new[] { new[] { 10d, 3d, 1d, 1d } }));
    [Fact]
    public void ActualTallTitleBlockReserved() => Assert.Throws<InvalidOperationException>(() => DrawingLayout.Validate(42,29.7,
        new[] { new[] { 30d, 7d, 2d, 2d } }, reservedBottomCm: 7.5));
}
