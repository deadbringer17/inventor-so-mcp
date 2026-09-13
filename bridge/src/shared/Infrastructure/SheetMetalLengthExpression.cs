using System.Globalization;

namespace Bimwright.Ipt.Shared.Infrastructure;

/// <summary>
/// Thickness and extent expressions for sheet-metal styles. Inventor's own GetStringFromValue returns
/// a localized display string that the style then refuses (observed as "0.200 su" on it-IT), so the
/// expression is built here: invariant first, comma form as the fallback for locales that need it.
/// </summary>
public static class SheetMetalLengthExpression
{
    public static string[] Forms(double millimetres)
    {
        string invariant = millimetres.ToString("0.####", CultureInfo.InvariantCulture);
        return invariant.IndexOf('.') < 0
            ? new[] { invariant + " mm" }
            : new[] { invariant + " mm", invariant.Replace('.', ',') + " mm" };
    }
}
