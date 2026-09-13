using System;

namespace Bimwright.Ipt.Shared.Infrastructure;

public static class DrawingLayout
{
    public static double ValidateScale(double scale)
    {
        if (double.IsNaN(scale) || double.IsInfinity(scale) || scale <= 0 || scale > 100)
            throw new ArgumentException("scale must be finite, greater than zero and at most 100.");
        return scale;
    }
    // Each rectangle is center x/y and width/height, in sheet centimeters.
    public static void Validate(double sheetWidth, double sheetHeight, double[][] rectangles, double reservedBottomCm = 4)
    {
        if (double.IsNaN(reservedBottomCm) || double.IsInfinity(reservedBottomCm) || reservedBottomCm < 4)
            throw new ArgumentException("Invalid title-block reserve.");
        for (int i = 0; i < rectangles.Length; i++)
        {
            var a = rectangles[i];
            if (a.Length != 4) throw new ArgumentException("Invalid view rectangle.");
            foreach (var v in a) if (double.IsNaN(v) || double.IsInfinity(v)) throw new ArgumentException("Nonfinite view rectangle.");
            if (a[2] <= 0 || a[3] <= 0 || a[0]-a[2]/2 < 1 || a[0]+a[2]/2 > sheetWidth-1 ||
                a[1]-a[3]/2 < reservedBottomCm || a[1]+a[3]/2 > sheetHeight-1)
                throw new InvalidOperationException("VIEW_OUTSIDE_LAYOUT: choose a smaller scale. Actual title-block height, at least 40 mm, is reserved.");
            for (int j = 0; j < i; j++)
            {
                var b = rectangles[j];
                if (Math.Abs(a[0]-b[0]) < (a[2]+b[2])/2+0.2 && Math.Abs(a[1]-b[1]) < (a[3]+b[3])/2+0.2)
                    throw new InvalidOperationException("VIEW_OVERLAP: choose a smaller scale.");
            }
        }
    }
}
