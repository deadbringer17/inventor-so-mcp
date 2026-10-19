using System;
using InventorXrSo.Core.Ui;

namespace XrSo.Core.Tests.Ui
{
    public class PaletteLayoutTests
    {
        [Fact]
        public void Default_sketch_is_four_by_four_without_changing_picker_or_ring()
        {
            var layout = PaletteLayout.Sketch;
            Assert.Equal(320, layout.Width); Assert.Equal(220, layout.Height);
            Assert.Equal(4, layout.Columns); Assert.Equal(4, layout.Rows);
            Assert.Equal(74, layout.CellWidth); Assert.Equal(33, layout.CellHeight);
            Assert.Equal(16, layout.Capacity); Assert.Equal(8, PaletteLayout.PickerCapacity);
            Assert.Equal(6, ActionCatalog.MaxContext);
        }

        [Fact]
        public void Every_offered_size_fits_without_shrinking_targets()
        {
            for (int width = 280; width <= 360; width++)
            for (int height = 190; height <= 250; height++)
            {
                var l = PaletteLayout.ForSketch(width, height);
                Assert.True(l.CellWidth >= 74); Assert.True(l.CellHeight >= 33);
                Assert.True(l.Columns * l.CellWidth + (l.Columns - 1) * l.Gap <= width - 2 * l.MarginX);
                Assert.True(l.Rows * l.CellHeight + (l.Rows - 1) * l.Gap <= l.BodyHeight);
                Assert.InRange(l.Capacity, 1, 16);
                int total = 77, pages = l.PageCount(total), seen = 0;
                for (int p = 0; p < pages; p++) seen += Math.Min(l.Capacity, total - p * l.Capacity);
                Assert.Equal(total, seen);
            }
        }

        [Theory]
        [InlineData(279, 220)] [InlineData(361, 220)] [InlineData(320, 189)] [InlineData(320, 251)]
        [InlineData(double.NaN, 220)] [InlineData(320, double.PositiveInfinity)]
        public void Invalid_sizes_are_rejected(double width, double height)
            => Assert.Throws<ArgumentOutOfRangeException>(() => PaletteLayout.ForSketch(width, height));
    }
}
