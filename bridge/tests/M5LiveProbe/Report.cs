using System.Diagnostics;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>
/// Outcome book of the probe. Every check exists from the start as NOT_RUN and only becomes PASS or
/// FAIL when the code that exercises it says so, so an unexercised check can never read as passed.
/// </summary>
internal sealed class Report
{
    internal sealed class Check
    {
        public string Id = "", Title = "", Status = "NOT_RUN", Detail = "not executed";
        public JObject? Data;
    }

    private readonly List<Check> _checks = new();
    public JObject Measurements { get; } = new();
    public JObject Findings { get; } = new();

    public void Declare(string id, string title) => _checks.Add(new Check { Id = id, Title = title });

    private Check Get(string id) => _checks.Single(c => c.Id == id);

    public void Pass(string id, string detail, JObject? data = null) => Set(id, "PASS", detail, data);
    public void Fail(string id, string detail, JObject? data = null) => Set(id, "FAIL", detail, data);
    public void NotRun(string id, string reason) => Set(id, "NOT_RUN", reason, null);

    /// <summary>PASS when the condition holds, FAIL otherwise; the detail says what was compared.</summary>
    public void Verdict(string id, bool ok, string detail, JObject? data = null) => Set(id, ok ? "PASS" : "FAIL", detail, data);

    /// <summary>Marks every check still NOT_RUN with the reason a stage could not run.</summary>
    public void NotRunAll(IEnumerable<string> ids, string reason)
    {
        foreach (var id in ids) if (Get(id).Status == "NOT_RUN") Set(id, "NOT_RUN", reason, null);
    }

    public bool IsOpen(string id) => Get(id).Status == "NOT_RUN" && Get(id).Detail == "not executed";

    private void Set(string id, string status, string detail, JObject? data)
    {
        var check = Get(id);
        check.Status = status; check.Detail = detail; check.Data = data;
        Console.WriteLine(status.PadRight(8) + id + " - " + detail);
    }

    public int Count(string status) => _checks.Count(c => c.Status == status);

    public JObject ToJson(JObject header) => new()
    {
        ["header"] = header,
        ["summary"] = new JObject { ["pass"] = Count("PASS"), ["fail"] = Count("FAIL"), ["not_run"] = Count("NOT_RUN") },
        ["checks"] = new JArray(_checks.Select(c => new JObject
        {
            ["id"] = c.Id, ["title"] = c.Title, ["status"] = c.Status, ["detail"] = c.Detail, ["data"] = c.Data,
        })),
        ["measurements"] = Measurements,
        ["findings"] = Findings,
    };

    public string ToText(JObject header)
    {
        var text = new System.Text.StringBuilder();
        text.AppendLine("M5 live probe - Flat Pattern XR (decisions 1 and 4)");
        foreach (var property in header.Properties()) text.AppendLine(property.Name + ": " + property.Value.ToString(Formatting.None));
        text.AppendLine();
        foreach (var c in _checks)
        {
            text.AppendLine(c.Status.PadRight(8) + c.Id + " - " + c.Title);
            text.AppendLine("         " + c.Detail);
        }
        text.AppendLine();
        text.AppendLine("PASS " + Count("PASS") + "  FAIL " + Count("FAIL") + "  NOT_RUN " + Count("NOT_RUN"));
        text.AppendLine();
        text.AppendLine("Timings (ms, min / median / max):");
        foreach (var property in Measurements.Properties())
            if (property.Value is JObject m && m["median_ms"] != null)
                text.AppendLine("  " + property.Name.PadRight(44) + m["min_ms"] + " / " + m["median_ms"] + " / " + m["max_ms"] + "  (n=" + m["runs"] + ")");
        return text.ToString();
    }
}

internal static class Timing
{
    public static double Ms(Action action)
    {
        var watch = Stopwatch.StartNew();
        action();
        return watch.Elapsed.TotalMilliseconds;
    }

    public static JObject Stats(IReadOnlyList<double> samples)
    {
        var sorted = samples.OrderBy(v => v).ToArray();
        double median = sorted.Length == 0 ? double.NaN
            : sorted.Length % 2 == 1 ? sorted[sorted.Length / 2] : (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2;
        return new JObject
        {
            ["runs"] = sorted.Length,
            ["min_ms"] = Round(sorted.FirstOrDefault()), ["median_ms"] = Round(median), ["max_ms"] = Round(sorted.LastOrDefault()),
            ["samples_ms"] = new JArray(samples.Select(Round)),
        };
    }

    private static double Round(double value) => Math.Round(value, 1);

    public static string Fixed(double value, int digits = 3) => value.ToString("F" + digits, CultureInfo.InvariantCulture);
}
