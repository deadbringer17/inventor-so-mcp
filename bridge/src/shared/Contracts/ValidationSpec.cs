using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Contracts;

/// <summary>
/// The <c>validate</c> list of an atomic batch: which checks must pass before the transaction may
/// commit. The vocabulary is closed and parsed before any transaction starts, so a misspelt check
/// is refused up front instead of being silently skipped after the model was already edited.
/// Checks run on the whole document once, before commit - never after each operation, which would
/// multiply rebuilds without catching anything the final check does not.
/// </summary>
public sealed class ValidationSpec
{
    /// <summary>One requested check, with its millimetre argument when it takes one.</summary>
    public sealed class Rule
    {
        public Rule(string name, double? valueMm) { Name = name; ValueMm = valueMm; }
        public string Name { get; }
        public double? ValueMm { get; }
        public override string ToString() => ValueMm is { } mm
            ? Name + ":" + mm.ToString("0.###", CultureInfo.InvariantCulture) + "mm"
            : Name;
    }

    public const string Rebuild = "rebuild";
    public const string FeatureHealth = "feature_health";
    public const string SketchFullyConstrained = "sketch_fully_constrained";
    public const string ConstraintHealth = "constraint_health";
    public const string Interference = "interference";
    public const string MinClearance = "min_clearance";
    public const string DrawingReferences = "drawing_references";

    /// <summary>Every check and the document kinds it can run on.</summary>
    private static readonly Dictionary<string, string[]> Vocabulary = new(StringComparer.Ordinal)
    {
        [Rebuild] = new[] { CadDocumentKinds.Part, CadDocumentKinds.Assembly, CadDocumentKinds.Drawing },
        [FeatureHealth] = new[] { CadDocumentKinds.Part },
        [SketchFullyConstrained] = new[] { CadDocumentKinds.Part },
        [ConstraintHealth] = new[] { CadDocumentKinds.Assembly },
        [Interference] = new[] { CadDocumentKinds.Assembly },
        [MinClearance] = new[] { CadDocumentKinds.Assembly },
        [DrawingReferences] = new[] { CadDocumentKinds.Drawing },
    };

    /// <summary>Checks that always run for a document kind, whether or not the caller asked.</summary>
    private static readonly Dictionary<string, string[]> Defaults = new(StringComparer.Ordinal)
    {
        [CadDocumentKinds.Part] = new[] { Rebuild, FeatureHealth },
        [CadDocumentKinds.Assembly] = new[] { Rebuild, ConstraintHealth },
        [CadDocumentKinds.Drawing] = new[] { Rebuild },
    };

    public const int MaxRules = 16;
    public const double MaxClearanceMm = 10000;

    private ValidationSpec(IReadOnlyList<Rule> rules) { Rules = rules; }

    public IReadOnlyList<Rule> Rules { get; }

    public static IReadOnlyCollection<string> Names => Vocabulary.Keys;

    public bool Has(string name) => Rules.Any(r => r.Name == name);

    public Rule? Find(string name) => Rules.FirstOrDefault(r => r.Name == name);

    public JArray ToJson() => new(Rules.Select(r => r.ToString()));

    /// <summary>
    /// Parse a <c>validate</c> argument (null, or an array of strings) for a document kind. The
    /// defaults of the kind are always included. Throws <see cref="ArgumentException"/> naming the
    /// offending entry and the accepted vocabulary.
    /// </summary>
    public static ValidationSpec Parse(JToken? token, string documentKind)
    {
        if (!Defaults.ContainsKey(documentKind))
            throw new ArgumentException("Unknown document kind '" + documentKind + "'.");
        var rules = new List<Rule>();
        foreach (var name in Defaults[documentKind]) rules.Add(new Rule(name, null));
        if (token == null || token.Type == JTokenType.Null) return new ValidationSpec(rules);
        if (token is not JArray array)
            throw new ArgumentException("validate must be an array of check names. " + Accepted(documentKind));
        if (array.Count > MaxRules)
            throw new ArgumentException("validate accepts at most " + MaxRules + " checks.");
        foreach (var item in array)
        {
            if (item.Type != JTokenType.String)
                throw new ArgumentException("validate entries must be strings. " + Accepted(documentKind));
            var rule = ParseRule(((string)item!).Trim(), documentKind);
            // A repeated check replaces the earlier one: the stricter reading of "min_clearance:2mm"
            // then "min_clearance:5mm" is ambiguous, so the last one wins and is reported back.
            rules.RemoveAll(r => r.Name == rule.Name);
            rules.Add(rule);
        }
        return new ValidationSpec(rules);
    }

    private static Rule ParseRule(string text, string documentKind)
    {
        string name = text;
        double? value = null;
        int colon = text.IndexOf(':');
        if (colon >= 0)
        {
            name = text.Substring(0, colon).Trim();
            string argument = text.Substring(colon + 1).Trim();
            if (argument.EndsWith("mm", StringComparison.OrdinalIgnoreCase))
                argument = argument.Substring(0, argument.Length - 2).Trim();
            if (!double.TryParse(argument, NumberStyles.Float, CultureInfo.InvariantCulture, out var mm) ||
                double.IsNaN(mm) || double.IsInfinity(mm) || mm < 0 || mm > MaxClearanceMm)
                throw new ArgumentException("'" + text + "' needs a millimetre value between 0 and " + MaxClearanceMm + ", e.g. min_clearance:2mm.");
            value = mm;
        }
        if (!Vocabulary.TryGetValue(name, out var kinds))
            throw new ArgumentException("'" + text + "' is not a validation check. " + Accepted(documentKind));
        if (!kinds.Contains(documentKind))
            throw new ArgumentException("'" + name + "' does not apply to a " + documentKind + " document. " + Accepted(documentKind));
        if (name == MinClearance && value == null)
            throw new ArgumentException("min_clearance needs a value, e.g. min_clearance:2mm.");
        if (name != MinClearance && value != null)
            throw new ArgumentException("'" + name + "' takes no value.");
        return new Rule(name, value);
    }

    public static string Accepted(string documentKind)
        => "Accepted for " + documentKind + ": " +
           string.Join(", ", Vocabulary.Where(v => v.Value.Contains(documentKind))
               .Select(v => v.Key == MinClearance ? "min_clearance:<n>mm" : v.Key)) + ".";

    /// <summary>The vocabulary, for discovery.</summary>
    public static JObject Describe() => new()
    {
        ["checks"] = new JArray(Vocabulary.Select(v => new JObject
        {
            ["name"] = v.Key == MinClearance ? "min_clearance:<n>mm" : v.Key,
            ["documents"] = new JArray(v.Value),
        })),
        ["defaults"] = new JObject(Defaults.Select(d => new JProperty(d.Key, new JArray(d.Value)))),
        ["max_checks"] = MaxRules,
    };
}

/// <summary>The document kinds a batch command or a validation check can apply to.</summary>
public static class CadDocumentKinds
{
    public const string Part = "part";
    public const string Assembly = "assembly";
    public const string Drawing = "drawing";

    public static readonly string[] All = { Part, Assembly, Drawing };
}
