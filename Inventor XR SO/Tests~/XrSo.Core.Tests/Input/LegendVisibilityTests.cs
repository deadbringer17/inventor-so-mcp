using InventorXrSo.Core.Input;

namespace XrSo.Core.Tests.Input
{
    public class LegendVisibilityTests
    {
        [Theory]
        [InlineData(0)]
        [InlineData(10)]
        [InlineData(25)]
        [InlineData(-25)]
        public void Opacity_IsFullInsideTheCone(double angle)
            => Assert.Equal(LegendVisibility.FullOpacity, LegendVisibility.Opacity(angle));

        [Theory]
        [InlineData(25.01)]
        [InlineData(60)]
        [InlineData(180)]
        [InlineData(-90)]
        public void Opacity_IsLowOutsideTheCone(double angle)
            => Assert.Equal(LegendVisibility.BaseOpacity, LegendVisibility.Opacity(angle));

        [Fact]
        public void Opacity_NaNIsLow()
            => Assert.Equal(LegendVisibility.BaseOpacity, LegendVisibility.Opacity(double.NaN));

        [Fact]
        public void BaseOpacityIsLowerThanFull()
            => Assert.True(LegendVisibility.BaseOpacity > 0f && LegendVisibility.BaseOpacity < LegendVisibility.FullOpacity);

        [Theory]
        [InlineData(null, "")]
        [InlineData("", "")]
        [InlineData("Isola", "Isola")]
        [InlineData("123456789012", "123456789012")]
        [InlineData("1234567890123", "123456789012")]
        public void Fit_TruncatesTo12(string input, string expected)
            => Assert.Equal(expected, LegendVisibility.Fit(input));
    }
}
