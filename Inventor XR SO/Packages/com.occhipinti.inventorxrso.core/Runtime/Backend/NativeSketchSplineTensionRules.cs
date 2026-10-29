using System;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace InventorXrSo.Core.Backend
{
    public static class NativeSketchSplineTensionRules
    {
        public const string Variant = "spline_tension_update";

        public static bool TryGetTension(NativeSketchSnapshot snapshot, SketchEntityRef reference, out int tension)
        {
            tension = 0;
            if (snapshot == null || reference == null || !snapshot.NativeMutationComplete || !snapshot.SupportsVariant(Variant)
                || reference.Type != "spline" || reference.Subpart != SketchSubpart.Entity || !reference.Matches(snapshot.State, snapshot.SketchId)) return false;
            var entity = snapshot.Entities.SingleOrDefault(e => e.Selectable && e.Type == "spline" && e.Reference?.EntityId == reference.EntityId);
            if (!entity?.SplineTension.HasValue ?? true) return false;
            tension = entity.SplineTension.Value;
            return true;
        }

        public static bool OptionsCurrent(NativeSketchSnapshot snapshot, JObject options)
        {
            if (options == null || options.Count != 2 || options["tension"]?.Type != JTokenType.Integer
                || !int.TryParse(options["tension"].ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) || value < 0 || value > 100
                || !(options["references"] is JArray references) || references.Count != 1 || !(references[0] is JObject data)
                || data.Count != 6 || new[] { "document_id", "revision", "sketch_id", "entity_id", "type", "subpart" }.Any(k => data[k]?.Type != JTokenType.String)
                || (string)data["type"] != "spline" || (string)data["subpart"] != "Entity") return false;
            try
            {
                return TryGetTension(snapshot, new SketchEntityRef((string)data["document_id"], (string)data["revision"],
                    (string)data["sketch_id"], (string)data["entity_id"], "spline"), out _);
            }
            catch (FormatException) { return false; }
        }
    }
}
