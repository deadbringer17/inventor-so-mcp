#if INVENTOR2027 && SO_EXPERIMENTAL
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Bimwright.Ipt.Shared.Handlers.Core;
using Bimwright.Ipt.Shared.Infrastructure;
using Inventor;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Handlers.Experimental;

/// <summary>Shared checks of the M7 verification handlers: active assembly, document id and revision, direct occurrences.</summary>
internal static class VerifyXr
{
    public static AssemblyDocument Assembly(InventorCommandContext ctx, Application app, JObject p, string command, out string documentId)
    {
        var assembly = X.ActiveAssembly(app, command);
        documentId = EntityReferences.DocumentId((global::Inventor.Document)assembly);
        if (X.Str(p, "document_id") != documentId) throw ConcurrencyFailure.DocumentChanged((string?)p["document_id"], documentId);
        string revision = X.Str(p, "expected_revision");
        if (ctx.Events == null || ctx.Events.Revision(documentId) != revision)
            throw ConcurrencyFailure.StaleRevision(revision, ctx.Events?.Revision(documentId));
        return assembly;
    }

    public static ComponentOccurrence[] Direct(AssemblyDocument assembly) =>
        assembly.ComponentDefinition.Occurrences.Cast<ComponentOccurrence>().ToArray();

    public static ComponentOccurrence DirectOccurrence(AssemblyDocument assembly, ComponentOccurrence[] direct, string? id)
    {
        if (string.IsNullOrEmpty(id)) throw new ArgumentException("An occurrence id is required.");
        var occurrence = EntityReferences.ResolveOccurrence((global::Inventor.Document)assembly, id);
        if (!direct.Any(o => ReferenceEquals(o, occurrence)))
            throw new ArgumentException("M7 verifications take direct occurrences of the active assembly: " + id);
        return occurrence;
    }

    /// <summary>The direct occurrence that contains <paramref name="occurrence"/> (itself when it is direct); null if unreadable.</summary>
    public static ComponentOccurrence? Top(ComponentOccurrence? occurrence)
    {
        try { while (occurrence?.ParentOccurrence != null) occurrence = occurrence.ParentOccurrence; }
        catch { return null; }
        return occurrence;
    }

    public static string? Id(AssemblyDocument assembly, ComponentOccurrence? occurrence) =>
        occurrence == null ? null : X.Describe((global::Inventor.Document)assembly, occurrence);

    public static JArray Mm(Point p) => new JArray(p.X * 10, p.Y * 10, p.Z * 10);
}

/// <summary>
/// <c>check_interference_xr</c>: Inventor interference analysis of direct occurrences, revision-bound, with portable ids and the
/// range box of every interference body. Without occurrence_ids every unsuppressed direct occurrence is analysed against the
/// others; with occurrence_ids those occurrences are analysed against every other unsuppressed direct occurrence. Read-only.
/// </summary>
public sealed class CheckInterferenceXrHandler : ExperimentalHandler
{
    public override string Name => "check_interference_xr";
    public override bool IsReadOnly => true;

    protected override JToken Run(InventorCommandContext ctx, Application app, JObject p)
    {
        var assembly = VerifyXr.Assembly(ctx, app, p, Name, out string documentId);
        var def = assembly.ComponentDefinition;
        var direct = VerifyXr.Direct(assembly).Where(o => !o.Suppressed).ToArray();
        var selected = (p["occurrence_ids"] as JArray)?.Select(t => (string?)t).Where(s => !string.IsNullOrEmpty(s)).ToArray() ?? Array.Empty<string?>();

        var set1 = app.TransientObjects.CreateObjectCollection();
        var set2 = app.TransientObjects.CreateObjectCollection();
        if (selected.Length == 0) foreach (var o in direct) set1.Add(o);
        else
        {
            var chosen = selected.Select(id => VerifyXr.DirectOccurrence(assembly, VerifyXr.Direct(assembly), id)).ToArray();
            foreach (var o in direct) (chosen.Any(c => ReferenceEquals(c, o)) ? set1 : set2).Add(o);
        }
        int analyzed = set1.Count + set2.Count;
        var watch = Stopwatch.StartNew();
        var result = new JObject { ["document_id"] = documentId, ["revision"] = ctx.Events!.Revision(documentId), ["analyzed"] = analyzed };
        if (set1.Count == 0 || (selected.Length == 0 && set1.Count < 2) || (selected.Length > 0 && set2.Count == 0))
        {
            result["count"] = 0; result["total_volume_mm3"] = 0.0; result["elapsed_ms"] = 0; result["pairs"] = new JArray();
            return result;
        }
        X.Deadline(ctx, "before interference analysis");
        InterferenceResults results = selected.Length == 0 ? def.AnalyzeInterference(set1) : def.AnalyzeInterference(set1, set2);

        var pairs = new Dictionary<(string, string), JObject>();
        double total = 0;
        for (int i = 1; i <= results.Count; i++)
        {
            InterferenceResult r = results[i];
            var one = VerifyXr.Top(r.OccurrenceOne);
            var two = VerifyXr.Top(r.OccurrenceTwo);
            string? oneId = VerifyXr.Id(assembly, one), twoId = VerifyXr.Id(assembly, two);
            var (aId, bId, aName, bName) = string.CompareOrdinal(oneId, twoId) <= 0
                ? (oneId, twoId, one?.Name, two?.Name) : (twoId, oneId, two?.Name, one?.Name);
            double volume = r.Volume * 1000;
            total += volume;
            var key = (aId ?? "?", bId ?? "?");
            if (!pairs.TryGetValue(key, out var pair))
            {
                pair = new JObject
                {
                    ["a_occurrence_id"] = aId, ["b_occurrence_id"] = bId, ["a_name"] = aName, ["b_name"] = bName,
                    ["volume_mm3"] = 0.0, ["boxes"] = new JArray(),
                };
                pairs[key] = pair;
            }
            pair["volume_mm3"] = (double)pair["volume_mm3"]! + volume;
            try
            {
                Box box = r.InterferenceBody.RangeBox;
                ((JArray)pair["boxes"]!).Add(new JObject { ["min_mm"] = VerifyXr.Mm(box.MinPoint), ["max_mm"] = VerifyXr.Mm(box.MaxPoint) });
            }
            catch { /* the box is optional: the pair is still reported */ }
        }
        result["count"] = pairs.Count;
        result["total_volume_mm3"] = total;
        result["elapsed_ms"] = watch.ElapsedMilliseconds;
        result["pairs"] = new JArray(pairs.Values);
        return result;
    }
}

/// <summary>
/// <c>measure_min_distance_xr</c>: Inventor minimum distance between two direct occurrences, revision-bound. The closest points come
/// from the NameValueMap context of GetMinimumDistance when the interop fills it (late bound: a failure leaves them null and never
/// fails the measurement). Read-only.
/// </summary>
public sealed class MeasureMinDistanceXrHandler : ExperimentalHandler
{
    // Keys confirmed or replaced by the live probe recorded in docs/xr-m7-verification.md (task 1).
    private const string PointOneKey = "ClosestPointOne", PointTwoKey = "ClosestPointTwo";

    public override string Name => "measure_min_distance_xr";
    public override bool IsReadOnly => true;

    protected override JToken Run(InventorCommandContext ctx, Application app, JObject p)
    {
        var assembly = VerifyXr.Assembly(ctx, app, p, Name, out string documentId);
        var direct = VerifyXr.Direct(assembly);
        var a = VerifyXr.DirectOccurrence(assembly, direct, (string?)p["a_occurrence_id"]);
        var b = VerifyXr.DirectOccurrence(assembly, direct, (string?)p["b_occurrence_id"]);
        if (ReferenceEquals(a, b)) throw new ArgumentException("Choose two different occurrences.");
        if (a.Suppressed || b.Suppressed) throw new ArgumentException("A suppressed occurrence has no geometry to measure.");
        X.Deadline(ctx, "before minimum distance");

        JToken pointA = JValue.CreateNull(), pointB = JValue.CreateNull();
        double cm;
        try
        {
            var context = app.TransientObjects.CreateNameValueMap();
            cm = ((dynamic)app.MeasureTools).GetMinimumDistance(a, b, InferredTypeEnum.kNoInference, InferredTypeEnum.kNoInference, context);
            if (context.Value[PointOneKey] is Point one && context.Value[PointTwoKey] is Point two)
            {
                pointA = VerifyXr.Mm(one); pointB = VerifyXr.Mm(two);
            }
        }
        catch (Exception ex) when (ex is not CodedFailureException)
        {
            cm = app.MeasureTools.GetMinimumDistance(a, b);
        }
        bool points = pointA.Type == JTokenType.Array && pointB.Type == JTokenType.Array;
        return new JObject
        {
            ["document_id"] = documentId, ["revision"] = ctx.Events!.Revision(documentId), ["distance_mm"] = cm * 10,
            ["point_a"] = pointA, ["point_b"] = pointB, ["points_source"] = points ? "inventor" : "unavailable",
        };
    }
}

/// <summary><c>assembly_health_xr</c>: revision-bound assembly health with portable ids on failing relationships. Read-only.</summary>
public sealed class AssemblyHealthXrHandler : ExperimentalHandler
{
    public override string Name => "assembly_health_xr";
    public override bool IsReadOnly => true;

    protected override JToken Run(InventorCommandContext ctx, Application app, JObject p)
    {
        var assembly = VerifyXr.Assembly(ctx, app, p, Name, out string documentId);
        var result = AssemblyHealthReader.Read(ctx, assembly, p, withIds: true);
        result["document_id"] = documentId;
        result["revision"] = ctx.Events!.Revision(documentId);
        return result;
    }
}
#endif
