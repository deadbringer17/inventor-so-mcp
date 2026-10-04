using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Handlers.Core;

/// <summary>
/// Pure rules behind <c>face_feature</c> (M9 spec §4): which Inventor feature types are editable and
/// with which parameter roles, when a parameter is <c>editable</c>, the mm/degree conversion and the
/// JSON shape. It has no Inventor reference, so the unit tests compile and exercise it directly; the
/// experimental handler only reads the COM objects and feeds raw values in here.
/// </summary>
public static class FaceFeatureModel
{
    public const string Distance = "distance";
    public const string Angle = "angle";
    public const string Radius = "radius";
    public const string Diameter = "diameter";
    public const string Depth = "depth";
    public const string Count = "count";
    public const string Spacing = "spacing";

    /// <summary>A feature parameter as read from Inventor, in Inventor internal units (cm, rad, unitless).</summary>
    public sealed class RawParameter
    {
        public RawParameter(string name, string role, double internalValue, string? expression)
        {
            Name = name; Role = role; InternalValue = internalValue; Expression = expression;
        }

        public string Name { get; }
        public string Role { get; }
        public double InternalValue { get; }
        public string? Expression { get; }
    }

    /// <summary>Wire type -> parameter roles the client may show for it (spec table).</summary>
    public static readonly IReadOnlyDictionary<string, string[]> RolesByType = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["extrude"] = new[] { Distance },
        ["revolve"] = new[] { Angle },
        ["fillet"] = new[] { Radius },
        ["chamfer"] = new[] { Distance },
        ["hole"] = new[] { Diameter, Depth },
        ["rectangular_pattern"] = new[] { Count, Spacing },
        ["circular_pattern"] = new[] { Count, Angle },
        ["flange"] = new[] { Distance, Angle },
    };

    private static readonly Dictionary<string, string> ObjectTypeToWire = new(StringComparer.Ordinal)
    {
        ["kExtrudeFeatureObject"] = "extrude",
        ["kRevolveFeatureObject"] = "revolve",
        ["kFilletFeatureObject"] = "fillet",
        ["kChamferFeatureObject"] = "chamfer",
        ["kHoleFeatureObject"] = "hole",
        ["kRectangularPatternFeatureObject"] = "rectangular_pattern",
        ["kCircularPatternFeatureObject"] = "circular_pattern",
        ["kFlangeFeatureObject"] = "flange",
    };

    /// <summary>Wire type of a supported Inventor <c>ObjectTypeEnum</c> name, null when outside the table.</summary>
    public static string? SupportedType(string? objectType)
        => objectType != null && ObjectTypeToWire.TryGetValue(objectType, out var wire) ? wire : null;

    /// <summary>
    /// A readable snake_case type for a feature the table does not cover (<c>kSweepFeatureObject</c>
    /// becomes <c>sweep</c>), so UNSUPPORTED_FEATURE still tells the client what it hit.
    /// </summary>
    public static string GenericType(string? objectType)
    {
        if (string.IsNullOrWhiteSpace(objectType)) return "unknown";
        var name = objectType!;
        if (name.StartsWith("k", StringComparison.Ordinal) && name.Length > 1 && char.IsUpper(name[1])) name = name.Substring(1);
        foreach (var suffix in new[] { "FeatureObject", "Object" })
            if (name.EndsWith(suffix, StringComparison.Ordinal) && name.Length > suffix.Length) { name = name.Substring(0, name.Length - suffix.Length); break; }
        var snake = Regex.Replace(name, "(?<=[a-z0-9])(?=[A-Z])", "_").ToLowerInvariant();
        return snake.Length == 0 ? "unknown" : snake;
    }

    /// <summary>
    /// Features that are the model body itself rather than an editable operation: a non-parametric
    /// (imported / converted) base, derived or reference geometry. Faces they own have no owning
    /// parametric feature as far as an editing client is concerned.
    /// </summary>
    public static bool IsBodyWithoutOperation(string? objectType)
        => objectType != null && new[] { "NonParametricBase", "Derived", "ReferenceFeature", "Imported" }
            .Any(marker => objectType.IndexOf(marker, StringComparison.Ordinal) >= 0);

    private static readonly Regex SimpleValue = new(
        @"^\s*[-+]?(\d+([.,]\d+)?|[.,]\d+)\s*(mm|cm|m|in|ft|deg|rad|ul|°)?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// A plain literal (<c>20 mm</c>, <c>3</c>, <c>45 deg</c>). Anything that names another
    /// parameter or computes (<c>d1*2</c>, <c>d1</c>, <c>20 mm + 5 mm</c>) is not simple: overwriting it
    /// with a number would silently cut the dependency.
    /// </summary>
    public static bool IsSimpleExpression(string? expression)
        => !string.IsNullOrWhiteSpace(expression) && SimpleValue.IsMatch(expression!);

    /// <summary>Spec §4: editable only for a simple value on a feature that is neither suppressed nor in error.</summary>
    public static bool IsEditable(string? expression, bool suppressed, bool healthy)
        => !suppressed && healthy && IsSimpleExpression(expression);

    /// <summary>Wire unit of a role: mm for lengths, deg for angles, empty for a count.</summary>
    public static string UnitOf(string role) => role switch
    {
        Angle => "deg",
        Count => "",
        _ => "mm",
    };

    /// <summary>Inventor internal value (cm, rad, unitless) to the wire value (mm, degrees, count).</summary>
    public static double ToWireValue(string role, double internalValue)
    {
        double value = role switch
        {
            Angle => UnitConvert.RadToDeg(internalValue),
            Count => internalValue,
            _ => UnitConvert.CmToMm(internalValue),
        };
        return Math.Round(value, 6);
    }

    public static JObject FeatureJson(string name, string type, bool suppressed, bool healthy) => new()
    {
        ["name"] = name, ["type"] = type, ["suppressed"] = suppressed, ["healthy"] = healthy,
    };

    public static JObject ParameterJson(RawParameter parameter, bool suppressed, bool healthy) => new()
    {
        ["name"] = parameter.Name,
        ["role"] = parameter.Role,
        ["value"] = ToWireValue(parameter.Role, parameter.InternalValue),
        ["unit"] = UnitOf(parameter.Role),
        ["expression"] = parameter.Expression == null ? JValue.CreateNull() : (JToken)parameter.Expression,
        ["editable"] = IsEditable(parameter.Expression, suppressed, healthy),
    };

    /// <summary>
    /// The success payload for a supported feature. Raw parameters whose role is not in the type's
    /// table are dropped, so a probe that picks up an extra Inventor property cannot widen the contract.
    /// </summary>
    public static JObject Result(string name, string type, bool suppressed, bool healthy,
        IEnumerable<RawParameter> parameters, string? previousFeature)
    {
        var roles = RolesByType.TryGetValue(type, out var allowed) ? allowed : Array.Empty<string>();
        var list = new JArray(parameters.Where(p => roles.Contains(p.Role, StringComparer.Ordinal))
            .Select(p => (JToken)ParameterJson(p, suppressed, healthy)));
        return new JObject
        {
            ["feature"] = FeatureJson(name, type, suppressed, healthy),
            ["parameters"] = list,
            ["previous_feature"] = previousFeature == null ? JValue.CreateNull() : (JToken)previousFeature,
        };
    }

    public static CodedFailureException NoOwningFeature()
        => new(InventorErrorCodes.NO_OWNING_FEATURE,
            "The face belongs to the base body or to a derived or imported body: no parametric feature owns it.");

    /// <summary>Refusal for a type outside the table; Details still carry the feature name and type.</summary>
    public static CodedFailureException Unsupported(string name, string type, bool suppressed, bool healthy, string? previousFeature)
        => new(InventorErrorCodes.UNSUPPORTED_FEATURE,
            "Feature '" + name + "' (" + type + ") cannot be edited from a face; edit it in Inventor.",
            new JObject { ["feature"] = FeatureJson(name, type, suppressed, healthy), ["previous_feature"] = previousFeature == null ? JValue.CreateNull() : (JToken)previousFeature });

    /// <summary>
    /// The name that precedes <paramref name="featureName"/> in <paramref name="orderedNames"/> (browser
    /// order), null for the first feature or a name that is not in the list.
    /// </summary>
    public static string? PreviousOf(IReadOnlyList<string> orderedNames, string featureName)
    {
        for (int i = 0; i < orderedNames.Count; i++)
            if (string.Equals(orderedNames[i], featureName, StringComparison.Ordinal))
                return i == 0 ? null : orderedNames[i - 1];
        return null;
    }
}
