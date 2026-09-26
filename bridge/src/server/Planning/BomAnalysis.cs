using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Server.Planning;

/// <summary>
/// Server-side BOM intelligence over the rows of the verified <c>get_assembly_bom</c> query
/// (<c>part_number</c>, <c>description</c>, <c>path</c>, <c>qty</c>, <c>unit_mass_g</c>). Pure
/// functions: no Inventor access, fully unit-testable.
/// </summary>
public static class BomAnalysis
{
    public sealed class Row
    {
        public string PartNumber { get; init; } = "";
        public string? Description { get; init; }
        public string? Path { get; init; }
        public double Quantity { get; init; }
        public double? UnitMassG { get; init; }

        /// <summary>Identity for comparison: the part number, or the file when the part number is blank.</summary>
        public string Key => string.IsNullOrWhiteSpace(PartNumber) ? "path:" + (Path ?? "").ToLowerInvariant() : "pn:" + PartNumber.Trim();
    }

    /// <summary>Accepts the tool's own output ({bom:[...]}) or a bare array of rows.</summary>
    public static IReadOnlyList<Row> ReadRows(JToken? token)
    {
        var array = token as JArray ?? (token as JObject)?["bom"] as JArray
            ?? throw new ArgumentException("A BOM is an array of rows or an object with a bom array.");
        if (array.Count > 20000) throw new ArgumentException("A BOM of more than 20000 rows is refused.");
        return array.OfType<JObject>().Select(o => new Row
        {
            PartNumber = (string?)o["part_number"] ?? "",
            Description = (string?)o["description"],
            Path = (string?)o["path"],
            Quantity = o["qty"]?.Type is JTokenType.Integer or JTokenType.Float ? (double)o["qty"]! : 0,
            UnitMassG = o["unit_mass_g"]?.Type is JTokenType.Integer or JTokenType.Float ? (double?)o["unit_mass_g"] : null,
        }).ToArray();
    }

    /// <summary>Findings: blank part numbers, one part number on different files, non-positive quantities, missing descriptions.</summary>
    public static JObject Validate(IReadOnlyList<Row> rows, bool truncated)
    {
        var findings = new JArray();
        void Add(string severity, string code, string message, JObject? data = null)
        {
            var finding = new JObject { ["severity"] = severity, ["code"] = code, ["message"] = message };
            if (data != null) finding["data"] = data;
            findings.Add(finding);
        }
        foreach (var row in rows.Where(r => string.IsNullOrWhiteSpace(r.PartNumber)))
            Add("error", "PART_NUMBER_MISSING", "A component has no part number.", new JObject { ["path"] = row.Path });
        foreach (var group in rows.Where(r => !string.IsNullOrWhiteSpace(r.PartNumber))
                     .GroupBy(r => r.PartNumber.Trim(), StringComparer.Ordinal))
        {
            var files = group.Select(r => (r.Path ?? "").ToLowerInvariant()).Distinct().ToArray();
            if (files.Length > 1)
                Add("error", "PART_NUMBER_DUPLICATE", "Part number '" + group.Key + "' is used by " + files.Length + " different files.",
                    new JObject { ["part_number"] = group.Key, ["paths"] = new JArray(group.Select(r => r.Path).Distinct()) });
            var descriptions = group.Select(r => (r.Description ?? "").Trim()).Distinct().ToArray();
            if (descriptions.Length > 1)
                Add("warning", "DESCRIPTION_CONFLICT", "Part number '" + group.Key + "' has different descriptions.",
                    new JObject { ["part_number"] = group.Key, ["descriptions"] = new JArray(descriptions) });
        }
        foreach (var row in rows.Where(r => r.Quantity <= 0))
            Add("error", "QUANTITY_INVALID", "Row '" + row.PartNumber + "' has quantity " + row.Quantity.ToString(CultureInfo.InvariantCulture) + ".",
                new JObject { ["part_number"] = row.PartNumber, ["path"] = row.Path });
        foreach (var row in rows.Where(r => !string.IsNullOrWhiteSpace(r.PartNumber) && string.IsNullOrWhiteSpace(r.Description)))
            Add("warning", "DESCRIPTION_MISSING", "Part number '" + row.PartNumber + "' has no description.", new JObject { ["part_number"] = row.PartNumber });
        if (truncated)
            Add("warning", "BOM_TRUNCATED", "The BOM query was truncated; findings cover only the rows returned.");
        return new JObject
        {
            ["valid"] = !findings.Any(f => (string?)f["severity"] == "error"),
            ["row_count"] = rows.Count,
            ["total_quantity"] = rows.Sum(r => r.Quantity),
            ["findings"] = findings,
        };
    }

    /// <summary>Added, removed and changed rows between a baseline and the current BOM.</summary>
    public static JObject Compare(IReadOnlyList<Row> baseline, IReadOnlyList<Row> current)
    {
        static Dictionary<string, Row> Index(IEnumerable<Row> rows)
        {
            // Rows sharing a key are merged by quantity: the BOM is a parts list, not an occurrence list.
            var index = new Dictionary<string, Row>(StringComparer.Ordinal);
            foreach (var row in rows)
                index[row.Key] = index.TryGetValue(row.Key, out var existing)
                    ? new Row { PartNumber = existing.PartNumber, Description = existing.Description, Path = existing.Path,
                        Quantity = existing.Quantity + row.Quantity, UnitMassG = existing.UnitMassG }
                    : row;
            return index;
        }
        var before = Index(baseline);
        var after = Index(current);
        static JObject Describe(Row r) => new()
        {
            ["part_number"] = r.PartNumber, ["description"] = r.Description, ["path"] = r.Path, ["qty"] = r.Quantity,
        };
        var added = new JArray(after.Keys.Except(before.Keys).OrderBy(k => k, StringComparer.Ordinal).Select(k => Describe(after[k])));
        var removed = new JArray(before.Keys.Except(after.Keys).OrderBy(k => k, StringComparer.Ordinal).Select(k => Describe(before[k])));
        var changed = new JArray();
        foreach (var key in before.Keys.Intersect(after.Keys).OrderBy(k => k, StringComparer.Ordinal))
        {
            var b = before[key];
            var a = after[key];
            var differences = new JObject();
            if (Math.Abs(a.Quantity - b.Quantity) > 1e-9) differences["qty"] = new JObject { ["before"] = b.Quantity, ["after"] = a.Quantity };
            if ((a.Description ?? "") != (b.Description ?? "")) differences["description"] = new JObject { ["before"] = b.Description, ["after"] = a.Description };
            if (!string.Equals(a.Path ?? "", b.Path ?? "", StringComparison.OrdinalIgnoreCase)) differences["path"] = new JObject { ["before"] = b.Path, ["after"] = a.Path };
            if (differences.Count > 0) changed.Add(new JObject { ["part_number"] = a.PartNumber, ["changes"] = differences });
        }
        return new JObject
        {
            ["identical"] = added.Count == 0 && removed.Count == 0 && changed.Count == 0,
            ["added"] = added,
            ["removed"] = removed,
            ["changed"] = changed,
        };
    }

    /// <summary>RFC 4180 CSV with a header row. Cells that a spreadsheet would execute are neutralised.</summary>
    public static string ToCsv(IReadOnlyList<Row> rows)
    {
        var builder = new StringBuilder("part_number,description,qty,unit_mass_g,path\r\n");
        foreach (var row in rows)
            builder.Append(Cell(row.PartNumber)).Append(',')
                .Append(Cell(row.Description)).Append(',')
                .Append(row.Quantity.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(row.UnitMassG?.ToString(CultureInfo.InvariantCulture) ?? "").Append(',')
                .Append(Cell(row.Path)).Append("\r\n");
        return builder.ToString();
    }

    private static string Cell(string? value)
    {
        value ??= "";
        // CSV injection: a leading =, +, - or @ is evaluated as a formula by spreadsheet programs.
        if (value.Length > 0 && "=+-@".IndexOf(value[0]) >= 0) value = "'" + value;
        return value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }
}
