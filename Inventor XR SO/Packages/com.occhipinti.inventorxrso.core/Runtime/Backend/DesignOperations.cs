using System;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace InventorXrSo.Core.Backend
{
    /// <summary>M3 commands in millimetres; each builder returns a new independent wire operation.</summary>
    public static class DesignOperations
    {
        public static JObject CreateSketch(string plane, string name) => Step("create_sketch", new JObject
        { ["plane"] = plane != null && plane.StartsWith("ent_", StringComparison.Ordinal) ? Entity(plane) : Required(plane), ["name"] = Required(name) });

        public static JObject Line(string sketch, double x1, double y1, double x2, double y2)
        {
            Finite(x1, y1, x2, y2);
            if (x1 == x2 && y1 == y2) throw new ArgumentException("La linea deve avere lunghezza positiva.");
            var arguments = Points(sketch, x1, y1, x2, y2);
            arguments["infer_constraints"] = true;
            return Step("draw_line", arguments);
        }

        public static JObject Rectangle(string sketch, double x1, double y1, double x2, double y2)
        {
            Finite(x1, y1, x2, y2);
            if (x1 == x2 || y1 == y2) throw new ArgumentException("Il rettangolo deve avere larghezza e altezza positive.");
            return Step("draw_rectangle", Points(sketch, x1, y1, x2, y2));
        }

        public static JObject Circle(string sketch, double x, double y, double radius)
        {
            Finite(x, y); Positive(radius);
            return Step("draw_circle", new JObject { ["sketch_name"] = Required(sketch), ["cx"] = x, ["cy"] = y, ["radius"] = radius });
        }

        public static JObject Extrude(string sketch, double distance, string operation, string direction)
        {
            Positive(distance);
            if (operation != "join" && operation != "cut" && operation != "intersect" && operation != "new_body")
                throw new ArgumentException("Operazione di estrusione non valida.");
            if (direction != "positive" && direction != "negative" && direction != "symmetric")
                throw new ArgumentException("Direzione di estrusione non valida.");
            return Step("extrude", new JObject { ["sketch_name"] = Required(sketch), ["distance_mm"] = distance,
                ["operation"] = operation, ["direction"] = direction });
        }

        public static JObject Hole(string faceId, double x, double y, double z, double diameter, double? depth)
        {
            Finite(x, y, z); Positive(diameter); if (depth.HasValue) Positive(depth.Value);
            var args = new JObject { ["face_id"] = Entity(faceId), ["kind"] = "drilled", ["diameter_mm"] = diameter,
                ["points_mm"] = new JArray { new JArray(x, y, z) }, ["through"] = !depth.HasValue };
            if (depth.HasValue) args["depth_mm"] = depth.Value;
            return Step("hole", args);
        }

        public static JObject Fillet(string[] edgeIds, double radius) => Edges("fillet", "radius_mm", edgeIds, radius);
        public static JObject Chamfer(string[] edgeIds, double distance) => Edges("chamfer", "distance_mm", edgeIds, distance);

        public static JObject Parameter(string name, double value, string unit)
        {
            Finite(value);
            if (unit != "mm" && unit != "deg" && unit != "ul") throw new ArgumentException("Unità non supportata.");
            return Step("set_parameter", new JObject { ["name"] = Required(name),
                ["value"] = value.ToString("R", CultureInfo.InvariantCulture) + " " + unit });
        }

        private static JObject Edges(string command, string dimension, string[] ids, double value)
        {
            Positive(value);
            if (ids == null || ids.Length == 0 || ids.Length > 256) throw new ArgumentException("Seleziona da 1 a 256 spigoli.");
            return Step(command, new JObject { ["edge_ids"] = new JArray(ids.Select(Entity).Distinct()), [dimension] = value });
        }
        private static JObject Points(string sketch, double x1, double y1, double x2, double y2) => new JObject
        { ["sketch_name"] = Required(sketch), ["x1"] = x1, ["y1"] = y1, ["x2"] = x2, ["y2"] = y2 };
        private static JObject Step(string command, JObject arguments) => new JObject { ["command"] = command, ["arguments"] = arguments };
        private static string Required(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 128
            ? value : throw new ArgumentException("Nome o riferimento mancante/non valido.");
        private static string Entity(string value) => !string.IsNullOrWhiteSpace(value) && value.StartsWith("ent_", StringComparison.Ordinal)
            ? value : throw new ArgumentException("Seleziona un riferimento CAD valido.");
        private static void Positive(double value)
        { Finite(value); if (value <= 0) throw new ArgumentException("La dimensione deve essere positiva."); }
        private static void Finite(params double[] values)
        { if (values.Any(v => double.IsNaN(v) || double.IsInfinity(v))) throw new ArgumentException("Inserisci numeri finiti."); }
    }
}
