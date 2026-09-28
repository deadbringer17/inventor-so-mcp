using System;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace InventorXrSo.Core.Backend
{
    public static class AssemblyOperations
    {
        public static readonly string[] JointTypes = { "rigid", "rotational", "slide", "cylindrical", "planar", "ball" };
        public static JObject Move(string occurrenceId, CadPoint translation, CadPoint? axis = null, CadPoint? center = null, double degrees = 0)
        {
            Finite(degrees);
            var args = new JObject { ["occurrence_id"] = Entity(occurrenceId), ["translation_mm"] = Point(translation) };
            if (axis.HasValue || center.HasValue || degrees != 0)
            {
                if (!axis.HasValue || !center.HasValue || Math.Abs(axis.Value.Length - 1) > 1e-5) throw new ArgumentException("Asse normalizzato e centro richiesti.");
                args["rotation_axis"] = Point(axis.Value); args["rotation_center_mm"] = Point(center.Value); args["rotation_degrees"] = degrees;
            }
            return Step("assembly_move", args);
        }
        public static string[] CompatibleConstraints(AssemblyReference a, AssemblyReference b)
        {
            if (a == null || b == null || !a.Available || !b.Available || a.OccurrenceId == b.OccurrenceId) return Array.Empty<string>();
            if (a.IsCircle && b.IsCircle) return new[] { "insert" };
            if (a.IsPlane && b.IsPlane) return new[] { "mate", "flush", "angle" };
            if (a.IsAxial && b.IsAxial) return new[] { "mate_axis", "angle", "tangent" };
            if (a.Kind == "face" && b.Kind == "face" && (a.IsAxial || b.IsAxial)) return new[] { "tangent", "angle" };
            return Array.Empty<string>();
        }
        public static JObject Constraint(string type, AssemblyReference a, AssemblyReference b, double value, bool opposed = true, bool inside = false)
        {
            Finite(value);
            if (!CompatibleConstraints(a, b).Contains(type)) throw new ArgumentException("Geometrie incompatibili con il vincolo.");
            if (type == "angle" && (value <= -360 || value >= 360)) throw new ArgumentException("Angolo fuori intervallo.");
            var args = new JObject { ["type"] = type, ["entity_a_id"] = a.Id, ["entity_b_id"] = b.Id,
                [type == "angle" ? "angle_degrees" : "offset_mm"] = value };
            if (type == "insert") args["axes_opposed"] = opposed;
            if (type == "tangent") args["inside"] = inside;
            return Step("assembly_constraint", args);
        }
        public static JObject Joint(string type, AssemblyReference a, AssemblyReference b, double gap = 0, bool flipOrigin = false, bool flipAlignment = false)
        {
            Finite(gap);
            if (type == "ball" && gap != 0) throw new ArgumentException("Ball richiede origini coincidenti: gap zero.");
            if (!JointTypes.Contains(type) || a == null || b == null || !a.Available || !b.Available || a.OccurrenceId == b.OccurrenceId)
                throw new ArgumentException("Seleziona due origini valide di componenti diversi.");
            if ((type == "rotational" || type == "cylindrical") && (!a.IsCircle || !b.IsCircle)) throw new ArgumentException("Seleziona due spigoli circolari.");
            return Step("assembly_joint", new JObject { ["joint_type"] = type, ["origin_a_id"] = a.Id, ["origin_b_id"] = b.Id,
                ["gap_mm"] = gap, ["flip_origin"] = flipOrigin, ["flip_alignment"] = flipAlignment });
        }
        internal static string Entity(string id) => !string.IsNullOrWhiteSpace(id) && id.StartsWith("ent_", StringComparison.Ordinal)
            ? id : throw new ArgumentException("Riferimento CAD non valido.");
        private static JArray Point(CadPoint point) => new JArray(point.X, point.Y, point.Z);
        private static JObject Step(string name, JObject args) => new JObject { ["command"] = name, ["arguments"] = args };
        private static void Finite(double value) { if (double.IsNaN(value) || double.IsInfinity(value)) throw new ArgumentException("Inserisci un numero finito."); }
    }
}
