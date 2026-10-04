using System.Diagnostics;
using Inventor;
using Newtonsoft.Json.Linq;

/// <summary>
/// Live probes for the M7 spec risks (read-only): interference duration, interference body boxes, closest points of the minimum
/// distance, occurrences of a failing constraint, and AnalyzeInterference with two sets. Prints JSON; nothing is saved.
/// </summary>
internal static class ProbeM7
{
    private static ComponentOccurrence Occurrence(AssemblyDocument assembly, string name) =>
        assembly.ComponentDefinition.Occurrences.Cast<ComponentOccurrence>().First(o => o.Name == name);

    private static JArray Mm(Point p) => new JArray(p.X * 10, p.Y * 10, p.Z * 10);

    /// <summary>Independent Inventor reading of the values the Quest runner checks.</summary>
    internal static void AddInspection(global::Inventor.Application app, AssemblyDocument assembly, JObject result)
    {
        var def = assembly.ComponentDefinition;
        var all = app.TransientObjects.CreateObjectCollection();
        foreach (ComponentOccurrence o in def.Occurrences) all.Add(o);
        var interference = def.AnalyzeInterference(all);
        double volume = 0;
        for (int i = 1; i <= interference.Count; i++) volume += interference[i].Volume * 1000;
        result["interference_bodies"] = interference.Count;
        result["interference_volume_mm3"] = volume;
        result["distance_a_c_mm"] = app.MeasureTools.GetMinimumDistance(Occurrence(assembly, "M7_A"), Occurrence(assembly, "M7_C")) * 10;
        result["constraints"] = new JArray(def.Constraints.Cast<AssemblyConstraint>()
            .Select(c => (JToken)new JObject { ["name"] = c.Name, ["health"] = c.HealthStatus.ToString() }));
    }

    /// <summary>--probe-m7: the four risks on the open M7 fixture.</summary>
    internal static int Probe(global::Inventor.Application app)
    {
        if (app.ActiveDocument is not AssemblyDocument assembly || !assembly.DisplayName.StartsWith("XR_M7_Quest_Acceptance", StringComparison.Ordinal))
            throw new InvalidOperationException("Activate the M7 fixture first (--prepare-quest m7).");
        var def = assembly.ComponentDefinition;
        var output = new JObject();

        // Risk 1 and 3: duration and boxes of the interference bodies.
        var all = app.TransientObjects.CreateObjectCollection();
        foreach (ComponentOccurrence o in def.Occurrences) all.Add(o);
        var watch = Stopwatch.StartNew();
        var results = def.AnalyzeInterference(all);
        output["interference_ms"] = watch.ElapsedMilliseconds;
        var bodies = new JArray();
        for (int i = 1; i <= results.Count; i++)
        {
            var r = results[i];
            var item = new JObject { ["a"] = r.OccurrenceOne?.Name, ["b"] = r.OccurrenceTwo?.Name, ["volume_mm3"] = r.Volume * 1000 };
            try { var box = r.InterferenceBody.RangeBox; item["min_mm"] = Mm(box.MinPoint); item["max_mm"] = Mm(box.MaxPoint); }
            catch (Exception ex) { item["box_error"] = ex.GetType().Name + ": " + ex.Message; }
            bodies.Add(item);
        }
        output["interference"] = bodies;

        // Two sets: M7_B against the others (the "solo selezione" scope).
        try
        {
            var set1 = app.TransientObjects.CreateObjectCollection();
            var set2 = app.TransientObjects.CreateObjectCollection();
            foreach (ComponentOccurrence o in def.Occurrences) (o.Name == "M7_B" ? set1 : set2).Add(o);
            output["two_sets_count"] = def.AnalyzeInterference(set1, set2).Count;
        }
        catch (Exception ex) { output["two_sets_error"] = ex.GetType().Name + ": " + ex.Message; }

        // Risk 2: closest points from the context of GetMinimumDistance.
        try
        {
            var context = app.TransientObjects.CreateNameValueMap();
            double cm = app.MeasureTools.GetMinimumDistance(Occurrence(assembly, "M7_A"), Occurrence(assembly, "M7_C"),
                InferredTypeEnum.kNoInference, InferredTypeEnum.kNoInference, context);
            output["distance_mm"] = cm * 10;
            var entries = new JObject();
            for (int i = 1; i <= context.Count; i++)
            {
                string name = context.Name[i];
                object value = context.Value[name];
                entries[name] = value is Point p ? Mm(p) : JToken.FromObject(value?.ToString() ?? "null");
            }
            output["distance_context"] = entries;
        }
        catch (Exception ex) { output["distance_context_error"] = ex.GetType().Name + ": " + ex.Message; }

        // Risk 4: the occurrences of the failing constraint.
        var constraints = new JArray();
        foreach (AssemblyConstraint c in def.Constraints)
        {
            var item = new JObject { ["name"] = c.Name, ["health"] = c.HealthStatus.ToString() };
            try { item["occurrence_one"] = c.OccurrenceOne?.Name; item["occurrence_two"] = c.OccurrenceTwo?.Name; }
            catch (Exception ex) { item["occurrence_error"] = ex.GetType().Name + ": " + ex.Message; }
            constraints.Add(item);
        }
        output["constraints"] = constraints;
        Console.WriteLine(output.ToString());
        return 0;
    }

    /// <summary>--probe-active: duration only, on whatever assembly the user activated. Read-only, nothing saved.</summary>
    internal static int ProbeActive(global::Inventor.Application app)
    {
        if (app.ActiveDocument is not AssemblyDocument assembly) throw new InvalidOperationException("Activate an assembly first.");
        var def = assembly.ComponentDefinition;
        var all = app.TransientObjects.CreateObjectCollection();
        int count = 0;
        foreach (ComponentOccurrence o in def.Occurrences) if (!o.Suppressed) { all.Add(o); count++; }
        var watch = Stopwatch.StartNew();
        var results = def.AnalyzeInterference(all);
        long interferenceMs = watch.ElapsedMilliseconds;
        watch.Restart();
        int failing = def.Constraints.Cast<AssemblyConstraint>().Count(c => !c.Suppressed && c.HealthStatus != HealthStatusEnum.kUpToDateHealth);
        foreach (ComponentOccurrence o in def.Occurrences)
            if (!o.Suppressed) o.GetDegreesOfFreedom(out int _, out ObjectsEnumerator _, out int _, out ObjectsEnumerator _, out Point _);
        long healthMs = watch.ElapsedMilliseconds;
        Console.WriteLine(new JObject
        {
            ["document"] = assembly.DisplayName, ["top_level_occurrences"] = count, ["interference_bodies"] = results.Count,
            ["interference_ms"] = interferenceMs, ["failing_constraints"] = failing, ["health_ms"] = healthMs,
        }.ToString());
        return 0;
    }
}
