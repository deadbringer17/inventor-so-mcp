using Bimwright.Ipt.Shared.Contracts;
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
    // The code is the contract here: a caller that must decide whether to retry at a smaller scale
    // cannot tell a layout refusal from an Inventor API failure if it only sees the message.
    [Fact]
    public void OverlapRejected()
    {
        var error = Assert.Throws<CodedFailureException>(() => DrawingLayout.Validate(42,29.7,
            new[] { new[] { 10d, 10d, 4d, 4d }, new[] { 11d, 11d, 4d, 4d } }));
        Assert.Equal(InventorErrorCodes.VIEW_OVERLAP, error.Code);
        Assert.Equal(1, (int?)error.Details?["view_index"]);
        Assert.Equal(0, (int?)error.Details?["overlaps_view_index"]);
        Assert.DoesNotContain(error.Code, error.Message);
    }
    [Fact]
    public void TitleBlockRegionReserved()
    {
        var error = Assert.Throws<CodedFailureException>(() => DrawingLayout.Validate(42,29.7,
            new[] { new[] { 10d, 3d, 1d, 1d } }));
        Assert.Equal(InventorErrorCodes.VIEW_OUTSIDE_LAYOUT, error.Code);
        Assert.Equal(40d, (double?)error.Details?["reserved_bottom_mm"]);
        Assert.DoesNotContain(error.Code, error.Message);
    }
    [Fact]
    public void ActualTallTitleBlockReserved()
    {
        var error = Assert.Throws<CodedFailureException>(() => DrawingLayout.Validate(42,29.7,
            new[] { new[] { 30d, 7d, 2d, 2d } }, reservedBottomCm: 7.5));
        Assert.Equal(InventorErrorCodes.VIEW_OUTSIDE_LAYOUT, error.Code);
        Assert.Equal(75d, (double?)error.Details?["reserved_bottom_mm"]);
    }
}
