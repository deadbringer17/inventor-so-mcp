using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Infrastructure;

/// <summary>One title-block field: the iProperty name as written in the template, and its value.</summary>
public sealed class TitleBlockField
{
    public TitleBlockField(string name, string value) { Name = name; Value = value; }
    public string Name { get; }
    public string Value { get; }
}

/// <summary>
/// Validation for the <c>title_block</c> argument of <c>create_drawing_safe</c>.
///
/// Fields are addressed by iProperty NAME rather than by a fixed vocabulary of our own. A company
/// title block is a sketched symbol whose text is bound to whatever iProperties the company chose,
/// very often custom ones ("Commessa", "Disegnato Da"), so a fixed map of title/designer/revision
/// would fill the stock Inventor block and leave the company one empty. The handler resolves each
/// name against the document's existing property sets and creates the ones that are missing as user
/// -defined properties.
///
/// Pure validation: no CAD, so it is unit-testable without Inventor.
/// </summary>
public static class TitleBlockFields
{
    public const int MaxFields = 30;
    public const int MaxNameLength = 80;
    /// <summary>Inventor stores summary-style iProperty strings in 255-character slots.</summary>
    public const int MaxValueLength = 255;

    /// <summary>
    /// Parses the argument. A null or absent token is no title block at all, not an empty one, and
    /// returns an empty list so the drawing keeps whatever the template already carries.
    /// </summary>
    public static IReadOnlyList<TitleBlockField> Parse(JToken? token)
    {
        if (token == null || token.Type == JTokenType.Null) return Array.Empty<TitleBlockField>();
        if (token is not JObject o)
            throw new ArgumentException("title_block must be an object of iProperty name to value, for example {\"Title\": \"Flangia\"}.");

        var fields = new List<TitleBlockField>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in o.Properties())
        {
            if (fields.Count == MaxFields)
                throw new ArgumentException("title_block accepts at most " + MaxFields + " fields.");
            string name = property.Name.Trim();
            if (name.Length == 0 || name.Length > MaxNameLength)
                throw new ArgumentException("A title_block field name must be 1 to " + MaxNameLength + " characters.");
            if (HasControlCharacter(name))
                throw new ArgumentException("The title_block field name '" + Describe(property.Name) + "' contains control characters.");
            // Case-insensitive, because that is how the property is resolved against the document:
            // accepting "Title" and "title" together would make the winner depend on JSON order.
            if (!seen.Add(name))
                throw new ArgumentException("The title_block field '" + name + "' is given more than once.");
            fields.Add(new TitleBlockField(name, Value(name, property.Value)));
        }
        return fields;
    }

    private static string Value(string name, JToken token)
    {
        string text;
        switch (token.Type)
        {
            case JTokenType.String: text = (string?)token ?? ""; break;
            // A revision or a sheet count arrives as a number often enough that refusing it would be
            // pedantry; it is formatted invariantly so the drawing does not depend on the host locale.
            case JTokenType.Integer: text = ((long)token).ToString(CultureInfo.InvariantCulture); break;
            case JTokenType.Float: text = ((double)token).ToString("R", CultureInfo.InvariantCulture); break;
            case JTokenType.Boolean: text = (bool)token ? "true" : "false"; break;
            // Null clears the field rather than meaning "leave it alone": the caller that wants it
            // left alone omits the key.
            case JTokenType.Null: text = ""; break;
            default:
                throw new ArgumentException("The title_block field '" + name + "' must be a string, number or boolean.");
        }
        if (text.Length > MaxValueLength)
            throw new ArgumentException("The title_block field '" + name + "' exceeds " + MaxValueLength + " characters.");
        if (HasControlCharacter(text))
            throw new ArgumentException("The title_block field '" + name + "' contains control characters.");
        return text;
    }

    private static bool HasControlCharacter(string value)
    {
        foreach (char c in value) if (char.IsControl(c)) return true;
        return false;
    }

    /// <summary>Renders a rejected name safely for an error message.</summary>
    private static string Describe(string value)
    {
        var builder = new System.Text.StringBuilder();
        foreach (char c in value) builder.Append(char.IsControl(c) ? '?' : c);
        return builder.ToString();
    }
}
