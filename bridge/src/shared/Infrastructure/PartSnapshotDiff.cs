using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Infrastructure;

public static class PartSnapshotDiff
{
    public static JObject Compare(JObject before, JObject after)
    {
        if ((int?)before["schema_version"] != 1 || (int?)after["schema_version"] != 1) throw new ArgumentException("Unsupported semantic snapshot schema.");
        if ((string?)before["document_id"] == null || (string?)before["document_id"] != (string?)after["document_id"])
            throw new ArgumentException("DOCUMENT_MISMATCH");
        JObject Named(string key)
        {
            Dictionary<string, JObject> Index(JObject snapshot)
            {
                if (snapshot[key] is not JArray list) throw new ArgumentException("Missing snapshot " + key);
                var result = new Dictionary<string, JObject>(StringComparer.Ordinal);
                foreach (var item in list)
                {
                    if (item is not JObject obj || obj["name"]?.Type != JTokenType.String || result.ContainsKey((string)obj["name"]!))
                        throw new ArgumentException("Missing or duplicate name in " + key);
                    result.Add((string)obj["name"]!, obj);
                }
                return result;
            }
            var a = Index(before); var b = Index(after);
            var added = new JArray(); var removed = new JArray(); var changed = new JArray();
            foreach (var name in a.Keys.Union(b.Keys).OrderBy(n => n, StringComparer.Ordinal))
            {
                if (!a.ContainsKey(name)) added.Add(b[name].DeepClone());
                else if (!b.ContainsKey(name)) removed.Add(a[name].DeepClone());
                else if (!JToken.DeepEquals(a[name], b[name])) changed.Add(new JObject { ["name"] = name, ["before"] = a[name].DeepClone(), ["after"] = b[name].DeepClone() });
            }
            return new JObject { ["added"] = added, ["removed"] = removed, ["changed"] = changed };
        }
        var physical = new JArray();
        var tolerances = new Dictionary<string, double> { ["mass_kg"] = 1e-9, ["volume_mm3"] = 1e-3, ["area_mm2"] = 1e-3,
            ["center_x_mm"] = 1e-6, ["center_y_mm"] = 1e-6, ["center_z_mm"] = 1e-6 };
        foreach (var entry in tolerances)
        {
            double Number(JObject snapshot)
            {
                var token = snapshot["physical"]?[entry.Key];
                if (token == null || (token.Type != JTokenType.Float && token.Type != JTokenType.Integer)) throw new ArgumentException("Missing physical quantity " + entry.Key);
                double value = (double)token;
                if (double.IsNaN(value) || double.IsInfinity(value)) throw new ArgumentException("Nonfinite physical quantity.");
                return value;
            }
            double a = Number(before), b = Number(after), tolerance = Math.Max(entry.Value, Math.Max(Math.Abs(a), Math.Abs(b)) * 1e-9);
            physical.Add(new JObject { ["quantity"] = entry.Key, ["before"] = a, ["after"] = b, ["delta"] = b - a,
                ["tolerance"] = tolerance, ["changed"] = Math.Abs(b - a) > tolerance });
        }
        return new JObject { ["document_id"] = before["document_id"], ["parameters"] = Named("parameters"), ["features"] = Named("features"),
            ["physical"] = physical, ["matching"] = "exact_name; rename appears as removal/addition", ["geometry_equivalence_proven"] = false };
    }
}
