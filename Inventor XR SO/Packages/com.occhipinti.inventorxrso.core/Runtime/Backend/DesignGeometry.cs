using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace InventorXrSo.Core.Backend
{
    public readonly struct CadPoint
    {
        public readonly double X, Y, Z;
        public CadPoint(double x, double y, double z = 0)
        {
            if (double.IsNaN(x) || double.IsInfinity(x) || double.IsNaN(y) || double.IsInfinity(y)
                || double.IsNaN(z) || double.IsInfinity(z)) throw new ArgumentException("Coordinate non finite.");
            X = x; Y = y; Z = z;
        }
        public double Length => Math.Sqrt(Dot(this));
        public double Dot(CadPoint b) => X*b.X + Y*b.Y + Z*b.Z;
        public CadPoint Cross(CadPoint b) => new CadPoint(Y*b.Z-Z*b.Y, Z*b.X-X*b.Z, X*b.Y-Y*b.X);
        public static CadPoint operator +(CadPoint a, CadPoint b) => new CadPoint(a.X+b.X, a.Y+b.Y, a.Z+b.Z);
        public static CadPoint operator -(CadPoint a, CadPoint b) => new CadPoint(a.X-b.X, a.Y-b.Y, a.Z-b.Z);
        public static CadPoint operator *(CadPoint a, double b) => new CadPoint(a.X*b, a.Y*b, a.Z*b);
        public static CadPoint Parse(JToken token, int count = 3)
        {
            if (!(token is JArray a) || a.Count != count || a.Any(v => v.Type != JTokenType.Float && v.Type != JTokenType.Integer))
                throw new FormatException("Invalid CAD coordinate.");
            return new CadPoint((double)a[0], (double)a[1], count == 3 ? (double)a[2] : 0);
        }
    }

    public sealed class SketchFrame
    {
        public CadPoint OriginMm { get; }
        public CadPoint XAxis { get; }
        public CadPoint YAxis { get; }
        public CadPoint Normal => XAxis.Cross(YAxis);
        public SketchFrame(CadPoint origin, CadPoint x, CadPoint y)
        {
            if (Math.Abs(x.Length-1) > 0.001 || Math.Abs(y.Length-1) > 0.001 || Math.Abs(x.Dot(y)) > 0.001)
                throw new FormatException("Sketch frame must have orthonormal axes.");
            OriginMm = origin; XAxis = x; YAxis = y;
        }
        public CadPoint ToModel(CadPoint sketch) => OriginMm + XAxis*sketch.X + YAxis*sketch.Y;
        public CadPoint ToSketch(CadPoint model) => new CadPoint((model-OriginMm).Dot(XAxis), (model-OriginMm).Dot(YAxis));
        public bool IntersectRay(CadPoint originMm, CadPoint direction, out CadPoint sketch)
        {
            sketch = default;
            double denominator = direction.Dot(Normal);
            if (Math.Abs(denominator) < 1e-9) return false;
            double distance = (OriginMm-originMm).Dot(Normal) / denominator;
            if (distance < 0) return false;
            sketch = ToSketch(originMm + direction*distance); return true;
        }
        public static SketchFrame Parse(JToken json) => new SketchFrame(CadPoint.Parse(json?["origin_mm"]),
            CadPoint.Parse(json?["x_axis"]), CadPoint.Parse(json?["y_axis"]));
    }

    public sealed class SketchDimension
    {
        public string Name { get; }
        public string Expression { get; }
        public CadPoint TextPoint { get; }
        public bool Driven { get; }
        public SketchDimension(string name,string expression,CadPoint textPoint,bool driven)
        {
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(expression)) throw new FormatException("Invalid sketch dimension.");
            Name=name; Expression=expression; TextPoint=textPoint; Driven=driven;
        }
    }
    public sealed class SketchSnapshot
    {
        public string Name { get; }
        public SketchFrame Frame { get; }
        public bool Visible { get; }
        public IReadOnlyList<SketchElement> Elements { get; }
        public int UnsupportedEntities { get; }
        public IReadOnlyList<SketchDimension> Dimensions { get; }
        public IReadOnlyList<string> Constraints { get; }
        public SketchSnapshot(string name, SketchFrame frame, bool visible, IEnumerable<SketchElement> elements, int unsupported = 0,
            IEnumerable<SketchDimension> dimensions = null, IEnumerable<string> constraints = null)
        { Name = name; Frame = frame; Visible = visible; Elements = elements.ToArray(); UnsupportedEntities = unsupported;
            Dimensions=dimensions?.ToArray() ?? Array.Empty<SketchDimension>();
            Constraints=constraints?.ToArray() ?? Array.Empty<string>(); }
        public SketchElement Pick(CadPoint point,double toleranceMm)
        {
            if (!(toleranceMm>0) || double.IsInfinity(toleranceMm)) throw new ArgumentOutOfRangeException(nameof(toleranceMm));
            return Elements.Select(e=>(element:e,distance:e.DistanceTo(point))).Where(e=>e.distance<=toleranceMm)
                .OrderBy(e=>e.distance).Select(e=>e.element).FirstOrDefault();
        }
        public static SketchSnapshot Parse(JToken json)
        {
            var shapes = new List<SketchElement>(); int unsupported = 0;
            foreach (var e in json["entities"] as JArray ?? new JArray())
            {
                string type = (string)e["type"];
                var constraints=(e["constraints"] as JArray ?? new JArray()).Values<string>();
                if (type == "line") shapes.Add(new SketchElement(SketchShape.Line, CadPoint.Parse(e["start_mm"], 2), CadPoint.Parse(e["end_mm"], 2),constraints:constraints));
                else if (type == "circle") shapes.Add(new SketchElement(SketchShape.Circle, CadPoint.Parse(e["center_mm"], 2), default, (double)e["radius_mm"],constraints:constraints));
                else if (type != "point") unsupported++;
            }
            return new SketchSnapshot((string)json["sketch_name"], SketchFrame.Parse(json["frame"]),
                (bool?)json["visible"] ?? true, shapes, unsupported,
                (json["dimensions"] as JArray ?? new JArray()).Where(d=>d["text_mm"] is JArray).Select(d=>new SketchDimension(
                    (string)d["name"],(string)d["expression"],CadPoint.Parse(d["text_mm"],2),(bool?)d["driven"] ?? false)),
                (json["geometric_constraints"] as JArray ?? new JArray()).Select(c=>(string)c["type"]));
        }
    }

    public sealed class DesignPlane
    {
        public string Reference { get; }
        public string Name { get; }
        public SketchFrame Frame { get; }
        public DesignPlane(string reference, string name, SketchFrame frame) { Reference = reference; Name = name; Frame = frame; }
    }
    public sealed class DesignFace
    {
        public string Id { get; }
        public CadPoint PointMm { get; }
        public CadPoint Normal { get; }
        public int BodyIndex { get; }
        public int FaceOrdinal { get; }
        public DesignFace(string id, CadPoint pointMm, CadPoint normal, int bodyIndex = 0, int faceOrdinal = 0)
        {
            if (string.IsNullOrWhiteSpace(id) || Math.Abs(normal.Length-1) > 0.001)
                throw new FormatException("Invalid planar face reference or normal.");
            Id = id; PointMm = pointMm; Normal = normal * (1 / normal.Length);
            BodyIndex = bodyIndex; FaceOrdinal = faceOrdinal;
        }
        public CadPoint Project(CadPoint point) => point - Normal * (point-PointMm).Dot(Normal);
    }
    public sealed class DesignEdge
    {
        public string Id { get; }
        public string Kind { get; }
        public IReadOnlyList<CadPoint> PointsMm { get; }
        public DesignEdge(string id, string kind, IEnumerable<CadPoint> points) { Id = id; Kind = kind; PointsMm = points.ToArray(); }
    }
    public sealed class DesignContext
    {
        public DocumentState State { get; private set; }
        public IReadOnlyList<DesignPlane> Planes { get; private set; }
        public IReadOnlyList<DesignPlane> Sketches { get; private set; }
        public IReadOnlyList<SketchSnapshot> SketchSnapshots { get; private set; }
        public IReadOnlyList<DesignEdge> Edges { get; private set; }
        public IReadOnlyList<string> PlanarFaces { get; private set; }
        public IReadOnlyList<DesignFace> Faces { get; private set; }
        public JArray Parameters { get; private set; }
        public bool Truncated { get; private set; }
        public static DesignContext Parse(JObject json, DocumentState expected)
        {
            if ((string)json["document_id"] != expected.DocumentId || (string)json["revision"] != expected.Revision
                || (string)json["kind"] != "part") throw new FormatException("Design context differs from the active part/revision.");
            var edges = new List<DesignEdge>();
            foreach (var edge in json["edges"] as JArray ?? new JArray())
            {
                if (!(edge["points_mm"] is JArray flat)) continue;
                if (flat.Count < 6 || flat.Count % 3 != 0) throw new FormatException("Invalid edge strokes.");
                var points = new List<CadPoint>();
                for (int i = 0; i < flat.Count; i += 3) points.Add(new CadPoint((double)flat[i], (double)flat[i+1], (double)flat[i+2]));
                edges.Add(new DesignEdge((string)edge["id"], (string)edge["kind"], points));
            }
            var faces = (json["faces"] as JArray ?? new JArray()).Select(p => new DesignFace(
                (string)p["id"], CadPoint.Parse(p["point_mm"]), CadPoint.Parse(p["normal"]),
                (int?)p["body_index"] ?? 0, (int?)p["face_ordinal"] ?? 0)).ToArray();
            return new DesignContext
            {
                State = expected, Truncated = (bool?)json["truncated"] ?? false, Edges = edges,
                Planes = (json["planes"] as JArray ?? new JArray()).Select(p => new DesignPlane((string)p["reference"], (string)p["name"], SketchFrame.Parse(p))).ToArray(),
                Sketches = (json["sketches"] as JArray ?? new JArray()).Select(p => new DesignPlane((string)p["name"], (string)p["name"], SketchFrame.Parse(p))).ToArray(),
                SketchSnapshots = (json["sketch_snapshots"] as JArray ?? new JArray()).Select(SketchSnapshot.Parse).ToArray(),
                Faces = faces, PlanarFaces = faces.Select(p => p.Id).ToArray(),
                Parameters = (JArray)(json["parameters"] as JArray ?? new JArray()).DeepClone(),
            };
        }
    }
}
