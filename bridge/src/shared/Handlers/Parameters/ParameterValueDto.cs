using System;
using Newtonsoft.Json.Linq;
#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using Inventor;
#endif
using Bimwright.Ipt.Shared.Contracts;

namespace Bimwright.Ipt.Shared.Handlers.Parameters;

internal static class ParameterValueDto
{
#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
    public static JObject From(Parameter prm, bool includeKind = true)
    {
        return FromCore(
            prm.Name,
            prm.Expression,
            prm.get_Units(),
            includeKind ? prm.ParameterType.ToString() : null,
            () => prm.Value);
    }

    public static JObject From(UserParameter prm, bool includeKind = true)
    {
        return FromCore(
            prm.Name,
            prm.Expression,
            prm.get_Units(),
            includeKind ? prm.ParameterType.ToString() : null,
            () => prm.Value);
    }
#endif

    // This mapping has no Inventor dependency. Keep typed values separate from
    // converted numeric fields so callers cannot confuse false/text with zero.
    internal static JObject FromCore(string name, string expression, string unit, string? kind, Func<object?> value)
    {
        var o = new JObject
        {
            ["name"] = name,
            ["expression"] = expression,
            ["unit"] = unit,
        };
        if (!string.IsNullOrEmpty(kind))
            o["kind"] = kind;

        object? raw = null;
        try { raw = value(); } catch { }
        if (raw is bool boolean)
        {
            o["evaluated"] = JValue.CreateNull();
            o["value_bool"] = boolean;
            return o;
        }
        if (raw is string text)
        {
            o["evaluated"] = JValue.CreateNull();
            o["value_text"] = text;
            return o;
        }
        if (raw is not double d || double.IsNaN(d) || double.IsInfinity(d))
        {
            o["evaluated"] = JValue.CreateNull();
            return o;
        }

        AddConvertedValue(o, d, unit);
        return o;
    }

    private static void AddConvertedValue(JObject o, double internalValue, string? unit)
    {
        // Explicit unit table: only exact length/area/volume/angle tokens are converted. Anything else
        // (compound units, densities, stresses, time, mass) keeps its internal value, next to the
        // "unit" string already on the DTO, so it is never scaled as if it were millimetres.
        switch (ParameterUnitClassifier.Classify(unit))
        {
            case ParameterUnitKind.Angle:
                o["value_deg"] = UnitConvert.RadToDeg(internalValue);
                break;
            case ParameterUnitKind.Volume:
                o["value_mm3"] = UnitConvert.Cm3ToMm3(internalValue);
                break;
            case ParameterUnitKind.Area:
                o["value_mm2"] = UnitConvert.Cm2ToMm2(internalValue);
                break;
            case ParameterUnitKind.Length:
                o["value_mm"] = UnitConvert.CmToMm(internalValue);
                break;
            case ParameterUnitKind.Unitless:
                o["value_unitless"] = internalValue;
                break;
            default:
                o["value_internal"] = internalValue;
                // Compatibility: before the unit table these landed in value_unitless, which XR clients read
                // (value_mm ?? value_deg ?? value_unitless); keep it so they do not fall back to 0.
                o["value_unitless"] = internalValue;
                break;
        }
    }
}
