using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace InventorXrSo.Core.Backend
{
    /// <summary>A verification result is valid only for the document revision it was computed on.</summary>
    public interface IVerifyResult { string Revision { get; } }

    internal static class VerifyJson
    {
        public static double? Number(JToken token)
        {
            if (token == null || (token.Type != JTokenType.Float && token.Type != JTokenType.Integer)) return null;
            double value = (double)token;
            return double.IsNaN(value) || double.IsInfinity(value) ? (double?)null : value;
        }

        /// <summary>Exactly three finite numbers, else null.</summary>
        public static double[] Vector(JToken token)
        {
            if (!(token is JArray array) || array.Count != 3) return null;
            var values = array.Select(Number).ToArray();
            return values.All(v => v.HasValue) ? values.Select(v => v.Value).ToArray() : null;
        }

        public static IEnumerable<JObject> Objects(JToken token) => (token as JArray ?? new JArray()).OfType<JObject>();
    }

    /// <summary>Axis-aligned box in assembly millimetres.</summary>
    public sealed class VerifyBox
    {
        public VerifyBox(double[] minMm, double[] maxMm) { MinMm = minMm; MaxMm = maxMm; }
        public double[] MinMm { get; }
        public double[] MaxMm { get; }

        internal static VerifyBox FromJson(JObject json)
        {
            var min = VerifyJson.Vector(json["min_mm"]); var max = VerifyJson.Vector(json["max_mm"]);
            return min == null || max == null ? null : new VerifyBox(min, max);
        }
    }

    public sealed class InterferencePair
    {
        public string AOccurrenceId { get; private set; }
        public string BOccurrenceId { get; private set; }
        public string AName { get; private set; }
        public string BName { get; private set; }
        public double VolumeMm3 { get; private set; }
        public IReadOnlyList<VerifyBox> Boxes { get; private set; }

        internal static InterferencePair FromJson(JObject json) => new InterferencePair
        {
            AOccurrenceId = (string)json["a_occurrence_id"], BOccurrenceId = (string)json["b_occurrence_id"],
            AName = (string)json["a_name"] ?? (string)json["a_occurrence_id"], BName = (string)json["b_name"] ?? (string)json["b_occurrence_id"],
            VolumeMm3 = VerifyJson.Number(json["volume_mm3"]) ?? 0,
            Boxes = VerifyJson.Objects(json["boxes"]).Select(VerifyBox.FromJson).Where(b => b != null).ToArray(),
        };
    }

    public sealed class InterferenceReport : IVerifyResult
    {
        public string Revision { get; private set; }
        public int Analyzed { get; private set; }
        public int Count { get; private set; }
        public double TotalVolumeMm3 { get; private set; }
        public long? ElapsedMs { get; private set; }
        public IReadOnlyList<InterferencePair> Pairs { get; private set; }

        public static InterferenceReport FromJson(JObject json) => new InterferenceReport
        {
            Revision = (string)json["revision"], Analyzed = (int?)json["analyzed"] ?? 0, Count = (int?)json["count"] ?? 0,
            TotalVolumeMm3 = VerifyJson.Number(json["total_volume_mm3"]) ?? 0, ElapsedMs = (long?)json["elapsed_ms"],
            Pairs = VerifyJson.Objects(json["pairs"]).Select(InterferencePair.FromJson).ToArray(),
        };
    }

    public sealed class DistanceReport : IVerifyResult
    {
        public string Revision { get; private set; }
        public double DistanceMm { get; private set; }
        public double[] PointAMm { get; private set; }
        public double[] PointBMm { get; private set; }
        public bool HasPoints => PointAMm != null && PointBMm != null;

        public static DistanceReport FromJson(JObject json) => new DistanceReport
        {
            Revision = (string)json["revision"], DistanceMm = VerifyJson.Number(json["distance_mm"]) ?? double.NaN,
            PointAMm = VerifyJson.Vector(json["point_a"]), PointBMm = VerifyJson.Vector(json["point_b"]),
        };
    }

    public sealed class HealthIssue
    {
        public HealthIssue(string kind, string name, string health, string aOccurrenceId, string bOccurrenceId)
        { Kind = kind; Name = name; Health = health; AOccurrenceId = aOccurrenceId; BOccurrenceId = bOccurrenceId; }
        /// <summary>"constraint" or "joint".</summary>
        public string Kind { get; }
        public string Name { get; }
        public string Health { get; }
        public string AOccurrenceId { get; }
        public string BOccurrenceId { get; }
    }

    public sealed class UnconstrainedOccurrence
    {
        public UnconstrainedOccurrence(string occurrenceId, string name) { OccurrenceId = occurrenceId; Name = name; }
        public string OccurrenceId { get; }
        public string Name { get; }
    }

    public sealed class BomIssue
    {
        public BomIssue(string severity, string code, string message) { Severity = severity; Code = code; Message = message; }
        public string Severity { get; }
        public string Code { get; }
        public string Message { get; }
    }

    public sealed class HealthReport : IVerifyResult
    {
        public string Revision { get; private set; }
        public bool Healthy { get; private set; }
        public int OccurrenceCount { get; private set; }
        public IReadOnlyList<UnconstrainedOccurrence> Unconstrained { get; private set; }
        public IReadOnlyList<HealthIssue> Issues { get; private set; }
        public bool BomValid { get; private set; }
        public IReadOnlyList<BomIssue> BomIssues { get; private set; }

        public static HealthReport FromJson(JObject json)
        {
            IEnumerable<HealthIssue> Issues(string key, string kind) => VerifyJson.Objects(json[key]).Select(o =>
                new HealthIssue(kind, (string)o["name"], (string)o["health"], (string)o["a_occurrence_id"], (string)o["b_occurrence_id"]));
            var bom = json["bom"] as JObject ?? new JObject();
            return new HealthReport
            {
                Revision = (string)json["revision"], Healthy = (bool?)json["healthy"] ?? false,
                OccurrenceCount = (int?)json["occurrence_count"] ?? 0,
                Unconstrained = VerifyJson.Objects(json["occurrences"]).Where(o => (bool?)o["unconstrained"] == true)
                    .Select(o => new UnconstrainedOccurrence((string)o["occurrence_id"], (string)o["name"])).ToArray(),
                Issues = Issues("failing_constraints", "constraint").Concat(Issues("failing_joints", "joint")).ToArray(),
                BomValid = (bool?)bom["valid"] ?? true,
                BomIssues = VerifyJson.Objects(bom["findings"]).Select(o => new BomIssue((string)o["severity"], (string)o["code"], (string)o["message"])).ToArray(),
            };
        }
    }

    public interface IVerifyBackend
    {
        /// <summary>Null or empty ids: every direct occurrence against the others; else those occurrences against all the others.</summary>
        Task<InterferenceReport> CheckInterferenceAsync(DocumentState state, IReadOnlyList<string> occurrenceIds, CancellationToken ct);
        Task<DistanceReport> MeasureMinDistanceAsync(DocumentState state, string aOccurrenceId, string bOccurrenceId, CancellationToken ct);
        Task<HealthReport> GetAssemblyHealthAsync(DocumentState state, CancellationToken ct);
    }

    public sealed partial class InventorBackend : IVerifyBackend
    {
        public async Task<InterferenceReport> CheckInterferenceAsync(DocumentState state, IReadOnlyList<string> occurrenceIds, CancellationToken ct) =>
            InterferenceReport.FromJson(await _mcp.CallToolAsync("inventor_check_interference_xr", new JObject
            {
                ["document_id"] = state.DocumentId, ["expected_revision"] = state.Revision,
                ["occurrence_ids"] = occurrenceIds == null || occurrenceIds.Count == 0 ? null : new JArray(occurrenceIds.Cast<object>().ToArray()),
            }, ct));

        public async Task<DistanceReport> MeasureMinDistanceAsync(DocumentState state, string aOccurrenceId, string bOccurrenceId, CancellationToken ct) =>
            DistanceReport.FromJson(await _mcp.CallToolAsync("inventor_measure_min_distance_xr", new JObject
            {
                ["document_id"] = state.DocumentId, ["expected_revision"] = state.Revision,
                ["a_occurrence_id"] = aOccurrenceId, ["b_occurrence_id"] = bOccurrenceId,
            }, ct));

        public async Task<HealthReport> GetAssemblyHealthAsync(DocumentState state, CancellationToken ct) =>
            HealthReport.FromJson(await _mcp.CallToolAsync("inventor_assembly_health_xr", new JObject
            {
                ["document_id"] = state.DocumentId, ["expected_revision"] = state.Revision,
            }, ct));
    }
}
