using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace InventorXrSo.Core.Backend
{
    /// <summary>Client preflight only. Native membership is checked by Inventor during preview and commit.</summary>
    public static class NativeSketchSplinePointRules
    {
        public const string Variant = "spline_point_update";

        public static bool TrySelection(NativeSketchSnapshot snapshot, IEnumerable<SketchEntityRef> selection,
            out SketchEntityRef curve, out SketchEntityRef point)
        {
            curve = null; point = null;
            if (snapshot == null || !snapshot.NativeMutationComplete || !snapshot.SupportsVariant(Variant) || selection == null) return false;
            var refs = selection.ToArray();
            if (refs.Length != 2 || refs.Any(r => r == null || !r.Matches(snapshot.State, snapshot.SketchId))) return false;
            curve = refs.FirstOrDefault(r => (r.Type == "spline" || r.Type == "control_point_spline") && r.Subpart == SketchSubpart.Entity);
            point = refs.FirstOrDefault(r => r.Type == "point" && (r.Subpart == SketchSubpart.Point || r.Subpart == SketchSubpart.Entity));
            if (curve == null || point == null || curve.EntityId == point.EntityId) return false;
            string pointId = point.EntityId;
            return refs.All(r => snapshot.Entities.Any(e => e.Reference?.EntityId == r.EntityId && e.Type == r.Type && e.CanSelectSubpart(r.Subpart)))
                && snapshot.Entities.Single(e => e.Reference?.EntityId == pointId).TryGetSubpartPoint(SketchSubpart.Point, out _);
        }

        public static bool OptionsCurrent(NativeSketchSnapshot snapshot, JObject options)
        {
            if (options == null || options.Count != 2 || !(options["references"] is JArray refs) || refs.Count != 2
                || !(options["position_mm"] is JArray xy) || xy.Count != 2
                || xy.Any(n => n.Type != JTokenType.Integer && n.Type != JTokenType.Float)) return false;
            try
            {
                if (xy.Any(n => double.IsNaN((double)n) || double.IsInfinity((double)n))) return false;
                var parsed = new List<SketchEntityRef>();
                foreach (var token in refs)
                {
                    if (!(token is JObject data) || data.Count != 6
                        || new[] { "document_id", "revision", "sketch_id", "entity_id", "type", "subpart" }.Any(k => data[k]?.Type != JTokenType.String)
                        || !Enum.TryParse((string)data["subpart"], false, out SketchSubpart part) || !Enum.IsDefined(typeof(SketchSubpart), part)) return false;
                    parsed.Add(new SketchEntityRef((string)data["document_id"], (string)data["revision"], (string)data["sketch_id"],
                        (string)data["entity_id"], (string)data["type"], part));
                }
                return TrySelection(snapshot, parsed, out var curve, out _) && ReferenceEquals(curve, parsed[0]);
            }
            catch (FormatException) { return false; }
            catch (OverflowException) { return false; }
        }
    }
}
