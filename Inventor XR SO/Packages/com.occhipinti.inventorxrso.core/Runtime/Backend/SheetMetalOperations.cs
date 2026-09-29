using System;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace InventorXrSo.Core.Backend
{
    /// <summary>M5 sheet-metal commands in millimetres/degrees; each builder returns a new independent wire operation.</summary>
    public static class SheetMetalOperations
    {
        public const double MinThicknessMm = 0.05, MaxThicknessMm = 50, MaxFlangeHeightMm = 10000;
        public const int MaxEdges = 256;

        public static JObject SetRule(string rule, string unfoldRule, double? thicknessMm)
        {
            var args = new JObject();
            if (!string.IsNullOrWhiteSpace(rule)) args["rule"] = Required(rule);
            if (!string.IsNullOrWhiteSpace(unfoldRule)) args["unfold_rule"] = Required(unfoldRule);
            if (thicknessMm.HasValue)
            {
                Finite(thicknessMm.Value);
                if (thicknessMm.Value < MinThicknessMm || thicknessMm.Value > MaxThicknessMm)
                    throw new ArgumentException("Lo spessore deve essere tra 0,05 e 50 mm.");
                args["thickness_mm"] = thicknessMm.Value;
            }
            if (args.Count == 0) throw new ArgumentException("Indica regola, spessore o regola di sviluppo.");
            return Step("set_sheet_metal_rule", args);
        }

        public static JObject Face(string sketch) =>
            Step("sheet_metal_face", new JObject { ["sketch_name"] = Required(sketch) });

        public static JObject Flange(string[] edgeIds, double heightMm, double angleDegrees = 90, string heightDatum = "outer")
        {
            Finite(heightMm, angleDegrees);
            if (heightMm <= 0 || heightMm > MaxFlangeHeightMm) throw new ArgumentException("L'altezza della flangia deve essere tra 0 (escluso) e 10000 mm.");
            if (angleDegrees <= 0 || angleDegrees >= 360) throw new ArgumentException("L'angolo della flangia deve essere tra 0 e 360 gradi, estremi esclusi.");
            if (heightDatum != "outer" && heightDatum != "inner" && heightDatum != "tangent")
                throw new ArgumentException("Il riferimento di altezza deve essere esterno, interno o tangente.");
            if (edgeIds == null || edgeIds.Length == 0) throw new ArgumentException("Seleziona da 1 a 256 spigoli.");
            var edges = edgeIds.Select(Entity).Distinct().ToArray();
            if (edges.Length > MaxEdges) throw new ArgumentException("Seleziona da 1 a 256 spigoli.");
            return Step("sheet_metal_flange", new JObject { ["edge_ids"] = new JArray(edges), ["height_mm"] = heightMm,
                ["angle_degrees"] = angleDegrees, ["height_datum"] = heightDatum });
        }

        public static JObject Cut(string sketch, string extent = "thickness", string direction = "positive", bool acrossBends = false)
        {
            if (extent != "thickness" && extent != "through_all") throw new ArgumentException("L'estensione del taglio deve essere spessore o passante.");
            if (direction != "positive" && direction != "negative" && direction != "symmetric")
                throw new ArgumentException("Direzione del taglio non valida.");
            if (acrossBends && extent == "through_all") throw new ArgumentException("Il taglio attraverso le pieghe non è compatibile con l'estensione passante.");
            var args = new JObject { ["sketch_name"] = Required(sketch), ["extent"] = extent, ["direction"] = direction };
            if (acrossBends) args["across_bends"] = true;
            return Step("sheet_metal_cut", args);
        }

        public static JObject CreateFlatPattern(string alignToEdgeId = null, string alignment = null, bool reversed = false)
        {
            var args = new JObject();
            if (alignToEdgeId != null) args["align_to_edge_id"] = Entity(alignToEdgeId);
            if (alignment != null)
            {
                if (alignment != "horizontal" && alignment != "vertical") throw new ArgumentException("L'allineamento deve essere orizzontale o verticale.");
                args["alignment"] = alignment;
            }
            if (reversed) args["alignment_reversed"] = true;
            return Step("create_flat_pattern", args);
        }

        private static JObject Step(string command, JObject arguments) => new JObject { ["command"] = command, ["arguments"] = arguments };
        private static string Required(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 128
            ? value : throw new ArgumentException("Nome o riferimento mancante/non valido.");
        private static string Entity(string value) => !string.IsNullOrWhiteSpace(value) && value.StartsWith("ent_", StringComparison.Ordinal)
            ? value : throw new ArgumentException("Seleziona un riferimento CAD valido.");
        private static void Finite(params double[] values)
        { if (values.Any(v => double.IsNaN(v) || double.IsInfinity(v))) throw new ArgumentException("Inserisci numeri finiti."); }
    }
}
