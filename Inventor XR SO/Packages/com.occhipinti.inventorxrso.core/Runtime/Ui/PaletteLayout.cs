using System;

namespace InventorXrSo.Core.Ui
{
    public enum PaletteProfile { Compact, Sketch }

    /// <summary>Geometria in mm; capacità della pagina indipendente da picker e anello.</summary>
    public sealed class PaletteLayout
    {
        public const int PickerCapacity = 8;
        public static PaletteLayout Compact { get; } = new PaletteLayout(PaletteProfile.Compact, 160, 110);
        public static PaletteLayout Sketch { get; } = ForSketch(320, 220);

        private PaletteLayout(PaletteProfile profile, double width, double height)
        {
            Profile = profile; Width = width; Height = height;
            if (profile == PaletteProfile.Compact)
            {
                MarginX = MarginY = 3; Gap = 3; HeaderHeight = 12;
                Columns = 2; Rows = 4; CellWidth = 75.5; CellHeight = 20;
            }
            else
            {
                MarginX = 6; MarginY = 4; Gap = 4; HeaderHeight = 16;
                GroupHeight = 32; StatusHeight = 8;
                BodyHeight = height - 2 * MarginY - HeaderHeight - GroupHeight - StatusHeight - 3 * Gap;
                Columns = Math.Min(4, (int)Math.Floor((width - 2 * MarginX + Gap) / (74 + Gap)));
                Rows = Math.Min(4, (int)Math.Floor((BodyHeight + Gap) / (33 + Gap)));
                CellWidth = (width - 2 * MarginX - (Columns - 1) * Gap) / Columns;
                CellHeight = 33;
            }
        }

        public static PaletteLayout ForSketch(double width, double height)
        {
            if (double.IsNaN(width) || double.IsInfinity(width) || width < 280 || width > 360)
                throw new ArgumentOutOfRangeException(nameof(width));
            if (double.IsNaN(height) || double.IsInfinity(height) || height < 190 || height > 250)
                throw new ArgumentOutOfRangeException(nameof(height));
            return new PaletteLayout(PaletteProfile.Sketch, width, height);
        }

        public PaletteProfile Profile { get; }
        public double Width { get; }
        public double Height { get; }
        public double MarginX { get; }
        public double MarginY { get; }
        public double Gap { get; }
        public double HeaderHeight { get; }
        public double GroupHeight { get; }
        public double StatusHeight { get; }
        public double BodyHeight { get; }
        public int Columns { get; }
        public int Rows { get; }
        public double CellWidth { get; }
        public double CellHeight { get; }
        public int Capacity => Columns * Rows;
        public int PageCount(int count)
        {
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            return Math.Max(1, (count + Capacity - 1) / Capacity);
        }
    }
}
