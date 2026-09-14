using System;

namespace Bimwright.Ipt.Shared.Infrastructure;

/// <summary>ISO A-series sheet dimensions in centimetres, Inventor's internal length unit.</summary>
public static class SheetSizes
{
    public static readonly string[] Names = { "A4", "A3", "A2", "A1", "A0" };

    // Short edge, long edge, in centimetres, in the same order as Names.
    private static readonly double[][] Dimensions =
    {
        new[] { 21.0, 29.7 },
        new[] { 29.7, 42.0 },
        new[] { 42.0, 59.4 },
        new[] { 59.4, 84.1 },
        new[] { 84.1, 118.9 }
    };

    public static void Resolve(string? name, string? orientation, out double widthCm, out double heightCm)
    {
        string sheet = (string.IsNullOrWhiteSpace(name) ? "A3" : name!).Trim().ToUpperInvariant();
        int index = Array.IndexOf(Names, sheet);
        if (index < 0)
            throw new ArgumentException("Unknown sheet_size '" + sheet + "'. Use A4, A3, A2, A1 or A0.");
        string mode = (string.IsNullOrWhiteSpace(orientation) ? "landscape" : orientation!).Trim().ToLowerInvariant();
        if (mode == "landscape") { widthCm = Dimensions[index][1]; heightCm = Dimensions[index][0]; }
        else if (mode == "portrait") { widthCm = Dimensions[index][0]; heightCm = Dimensions[index][1]; }
        else throw new ArgumentException("orientation must be 'landscape' or 'portrait'.");
    }

    /// <summary>Smallest listed sheet whose landscape orientation contains the given size, or null.</summary>
    public static string? SmallestContaining(double widthCm, double heightCm)
    {
        for (int i = 0; i < Names.Length; i++)
            if (Dimensions[i][1] >= widthCm && Dimensions[i][0] >= heightCm) return Names[i];
        return null;
    }
}
