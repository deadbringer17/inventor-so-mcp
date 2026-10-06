using System;
using System.Collections.Generic;

namespace Bimwright.Ipt.Shared.Contracts;

/// <summary>What a parameter's unit string means for the mm conversion of its internal value.</summary>
public enum ParameterUnitKind
{
    /// <summary>Plain length: internal centimetres, reported in mm.</summary>
    Length,
    /// <summary>Area: internal cm^2, reported in mm^2.</summary>
    Area,
    /// <summary>Volume: internal cm^3, reported in mm^3.</summary>
    Volume,
    /// <summary>Angle: internal radians, reported in degrees.</summary>
    Angle,
    /// <summary>No unit ("ul" or empty).</summary>
    Unitless,
    /// <summary>Anything else (compound, density, stress, time, mass, ...): no conversion is safe.</summary>
    Other,
}

/// <summary>
/// Explicit unit table for parameter values. The earlier substring tests read "min" and "lbmass" as
/// lengths (both contain "in") and "mm^4", "N/mm^2" and "kg/m^3" as length/area/volume, so a value was
/// silently multiplied by ten (or by a hundred) and reported as millimetres. Only the exact tokens
/// below convert; everything else is reported as its internal value with the unit string beside it.
/// </summary>
public static class ParameterUnitClassifier
{
    private static readonly Dictionary<string, ParameterUnitKind> Table = new(StringComparer.Ordinal)
    {
        ["mm"] = ParameterUnitKind.Length, ["cm"] = ParameterUnitKind.Length, ["m"] = ParameterUnitKind.Length,
        ["in"] = ParameterUnitKind.Length, ["ft"] = ParameterUnitKind.Length, ["mil"] = ParameterUnitKind.Length,
        ["um"] = ParameterUnitKind.Length,

        ["mm^2"] = ParameterUnitKind.Area, ["cm^2"] = ParameterUnitKind.Area, ["m^2"] = ParameterUnitKind.Area,
        ["in^2"] = ParameterUnitKind.Area, ["ft^2"] = ParameterUnitKind.Area,

        ["mm^3"] = ParameterUnitKind.Volume, ["cm^3"] = ParameterUnitKind.Volume, ["m^3"] = ParameterUnitKind.Volume,
        ["in^3"] = ParameterUnitKind.Volume, ["ft^3"] = ParameterUnitKind.Volume,

        ["deg"] = ParameterUnitKind.Angle, ["rad"] = ParameterUnitKind.Angle,

        ["ul"] = ParameterUnitKind.Unitless, [""] = ParameterUnitKind.Unitless,
    };

    /// <summary>Lower-case, no whitespace, <c>**</c> and superscripts folded to <c>^</c> powers.</summary>
    public static string Normalize(string? unit)
    {
        if (string.IsNullOrWhiteSpace(unit)) return "";
        var sb = new System.Text.StringBuilder(unit!.Length);
        foreach (char c in unit)
        {
            if (char.IsWhiteSpace(c)) continue;
            switch (c)
            {
                case '²': sb.Append("^2"); break;
                case '³': sb.Append("^3"); break;
                default: sb.Append(char.ToLowerInvariant(c)); break;
            }
        }
        return sb.ToString().Replace("**", "^");
    }

    public static ParameterUnitKind Classify(string? unit)
        => Table.TryGetValue(Normalize(unit), out var kind) ? kind : ParameterUnitKind.Other;
}
