using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace InventorXrSo.Core.Backend
{
    /// <summary>Flat pattern state as Inventor reports it (never estimated from the folded mesh).</summary>
    public sealed class FlatPatternInfo
    {
        public bool Exists { get; }
        public double? LengthMm { get; }
        public double? WidthMm { get; }
        public int? BendCount { get; }
        public string Alignment { get; }
        internal FlatPatternInfo(bool exists, double? length, double? width, int? bends, string alignment)
        { Exists = exists; LengthMm = length; WidthMm = width; BendCount = bends; Alignment = alignment; }
        public static FlatPatternInfo Absent => new FlatPatternInfo(false, null, null, null, null);
    }

    /// <summary>
    /// Sheet-metal read of one document/revision. Absent, incomplete or stale reads never arm a write:
    /// <see cref="Reason"/> says why in Italian and <see cref="CanArmWrite"/> stays false.
    /// </summary>
    public sealed class SheetMetalContext
    {
        public DocumentState State { get; private set; }
        public bool IsSheetMetal { get; private set; }
        public bool IsComplete { get; private set; }
        public string Reason { get; private set; }
        public string Rule { get; private set; }
        public double? ThicknessMm { get; private set; }
        public IReadOnlyList<string> AvailableRules { get; private set; } = Array.Empty<string>();
        public int? BodyCount { get; private set; }
        public int? BendCount { get; private set; }
        public FlatPatternInfo FlatPattern { get; private set; } = FlatPatternInfo.Absent;
        public bool CanArmWrite => IsSheetMetal && IsComplete && Reason == null;
        public bool IsSingleBody => BodyCount == 1;

        public bool IsCurrentFor(DocumentState state) => state != null && State != null
            && State.DocumentId == state.DocumentId && State.Revision == state.Revision;

        /// <summary>No usable read (transport failure, no answer): readable reason, no write.</summary>
        public static SheetMetalContext Unavailable(DocumentState state, string reason) => new SheetMetalContext
        { State = state, Reason = string.IsNullOrWhiteSpace(reason) ? "Contesto lamiera non disponibile." : reason };

        /// <summary>
        /// The tool answers about the active part and carries no revision, so the caller brackets it with
        /// two state reads: <paramref name="before"/> is the state the read is bound to and
        /// <paramref name="after"/> (optional) the state observed once it returned. A payload that names
        /// another document/revision, or a state that moved in between, is stale and read-only.
        /// </summary>
        public static SheetMetalContext Parse(JObject json, DocumentState before, DocumentState after = null)
        {
            if (before == null) throw new ArgumentNullException(nameof(before));
            if (json == null) return Unavailable(before, "Lettura lamiera assente.");
            if (json["document_id"] != null && (string)json["document_id"] != before.DocumentId
                || json["revision"] != null && (string)json["revision"] != before.Revision
                || after != null && (after.DocumentId != before.DocumentId || after.Revision != before.Revision))
                return Unavailable(before, "La lettura lamiera non coincide con il documento o la revisione attuale.");
            if (json["is_sheet_metal"]?.Type != JTokenType.Boolean)
                return Unavailable(before, "Lettura lamiera incompleta: manca is_sheet_metal.");
            if (!(bool)json["is_sheet_metal"])
                return new SheetMetalContext { State = before, IsSheetMetal = false, IsComplete = true,
                    Reason = "La parte non è in lamiera." };

            var context = new SheetMetalContext { State = before, IsSheetMetal = true };
            string missing = Fill(context, json);
            context.IsComplete = missing == null;
            context.Reason = missing == null ? null : "Lettura lamiera incompleta: " + missing + ".";
            return context;
        }

        private static string Fill(SheetMetalContext c, JObject json)
        {
            string rule = json["rule"]?.Type == JTokenType.String ? (string)json["rule"] : null;
            double? thickness = Number(json["thickness_mm"]);
            int? bodies = Count(json["body_count"]), bends = Count(json["bend_count"]);
            c.Rule = rule; c.ThicknessMm = thickness; c.BodyCount = bodies; c.BendCount = bends;
            c.AvailableRules = json["available_rules"] is JArray list
                ? list.Select(r => r.Type == JTokenType.String ? (string)r : (string)r["rule"]).Where(r => !string.IsNullOrWhiteSpace(r)).ToArray()
                : Array.Empty<string>();
            var flat = json["flat_pattern"] as JObject;
            if (flat != null)
            {
                bool exists = flat["exists"]?.Type == JTokenType.Boolean && (bool)flat["exists"];
                string alignment = flat["alignment"]?["type"]?.Type == JTokenType.String ? (string)flat["alignment"]["type"] : null;
                c.FlatPattern = exists
                    ? new FlatPatternInfo(true, Number(flat["length_mm"]), Number(flat["width_mm"]), Count(flat["bend_count"]), alignment)
                    : FlatPatternInfo.Absent;
            }
            if (string.IsNullOrWhiteSpace(rule)) return "regola attiva mancante";
            if (!thickness.HasValue || thickness.Value <= 0) return "spessore mancante";
            if (!bodies.HasValue) return "numero di body mancante";
            if (!bends.HasValue) return "numero di pieghe mancante";
            if (c.AvailableRules.Count == 0) return "elenco regole mancante";
            if (flat == null || flat["exists"]?.Type != JTokenType.Boolean) return "stato dello sviluppo mancante";
            if (c.FlatPattern.Exists && (!c.FlatPattern.LengthMm.HasValue || !c.FlatPattern.WidthMm.HasValue
                || c.FlatPattern.LengthMm <= 0 || c.FlatPattern.WidthMm <= 0 || !c.FlatPattern.BendCount.HasValue))
                return "misure dello sviluppo mancanti";
            return null;
        }

        private static double? Number(JToken token)
        {
            if (token == null || (token.Type != JTokenType.Float && token.Type != JTokenType.Integer)) return null;
            double value = (double)token;
            return double.IsNaN(value) || double.IsInfinity(value) ? (double?)null : value;
        }
        private static int? Count(JToken token) =>
            token != null && token.Type == JTokenType.Integer && (long)token >= 0 && (long)token <= int.MaxValue ? (int)token : (int?)null;
    }
}
