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
    public sealed class SketchAnnotation
    {
        public SketchAnnotation(CadPoint point, string text) { Point = point; Text = text ?? ""; }
        public CadPoint Point { get; }
        public string Text { get; }
    }
    public sealed class SketchSnapshot
    {
        public string Name { get; }
        public SketchFrame Frame { get; }
        public bool Visible { get; }
        public IReadOnlyList<SketchElement> Elements { get; }
        public int UnsupportedEntities { get; }
        public bool NativePartial { get; }
        public bool NativeMutationComplete { get; }
        public IReadOnlyList<SketchDimension> Dimensions { get; }
        public IReadOnlyList<string> Constraints { get; }
        public IReadOnlyList<DesignEdge> NativePaths { get; }
        public IReadOnlyList<SketchAnnotation> NativeAnnotations { get; }
        public IReadOnlyList<NativeSketchTextGlyphMesh> NativeTextGlyphs { get; }
        public IEnumerable<IEnumerable<CadPoint>> Outlines => NativePaths.Count > 0
            ? NativePaths.Select(p => (IEnumerable<CadPoint>)p.PointsMm) : Elements.Select(e => (IEnumerable<CadPoint>)e.Outline());
        public SketchSnapshot(string name, SketchFrame frame, bool visible, IEnumerable<SketchElement> elements, int unsupported = 0,
            IEnumerable<SketchDimension> dimensions = null, IEnumerable<string> constraints = null, IEnumerable<DesignEdge> nativePaths = null,
            IEnumerable<SketchAnnotation> nativeAnnotations = null, bool nativePartial = false, bool? nativeMutationComplete = null,
            IEnumerable<NativeSketchTextGlyphMesh> nativeTextGlyphs = null)
        { Name = name; Frame = frame; Visible = visible; Elements = elements.ToArray(); UnsupportedEntities = unsupported;
            Dimensions=dimensions?.ToArray() ?? Array.Empty<SketchDimension>();
            Constraints=constraints?.ToArray() ?? Array.Empty<string>(); NativePaths=nativePaths?.ToArray() ?? Array.Empty<DesignEdge>();
            NativeAnnotations=nativeAnnotations?.ToArray() ?? Array.Empty<SketchAnnotation>(); NativePartial=nativePartial; NativeMutationComplete=nativeMutationComplete ?? !nativePartial;
            NativeTextGlyphs=Array.AsReadOnly(nativeTextGlyphs?.ToArray() ?? Array.Empty<NativeSketchTextGlyphMesh>()); }
        public SketchElement Pick(CadPoint point,double toleranceMm)
        {
            if (!(toleranceMm>0) || double.IsInfinity(toleranceMm)) throw new ArgumentOutOfRangeException(nameof(toleranceMm));
            return Elements.Select(e=>(element:e,distance:e.DistanceTo(point))).Where(e=>e.distance<=toleranceMm)
                .OrderBy(e=>e.distance).Select(e=>e.element).FirstOrDefault();
        }
        public static SketchSnapshot Parse(JToken json)
        {
            if(json["mutation_completeness"]!=null && !(json["native_entities"] is JArray))
                throw new FormatException("Native completeness requires native preview collections.");
            var shapes = new List<SketchElement>(); int unsupported = 0;
            foreach (var e in json["entities"] as JArray ?? new JArray())
            {
                string type = (string)e["type"];
                var constraints=(e["constraints"] as JArray ?? new JArray()).Values<string>();
                if (type == "line") shapes.Add(new SketchElement(SketchShape.Line, CadPoint.Parse(e["start_mm"], 2), CadPoint.Parse(e["end_mm"], 2),constraints:constraints));
                else if (type == "circle") shapes.Add(new SketchElement(SketchShape.Circle, CadPoint.Parse(e["center_mm"], 2), default, (double)e["radius_mm"],constraints:constraints));
                else if (type != "point") unsupported++;
            }
            var paths = new List<DesignEdge>();
            var annotations = new List<SketchAnnotation>();
            var textGlyphs = new List<NativeSketchTextGlyphMesh>();
            int pointCount = 0; bool nativePartial=false;bool? nativeMutationComplete=null;
            if (json["native_entities"] is JArray native)
            {
                if (native.Count > 2000) throw new FormatException("Native preview entity budget exceeded.");
                unsupported = native.Count(e => (bool?)e["supported"] == false);
                nativePartial=(bool?)json["partial"]!=false || native.Any(e=>(bool?)e["supported"]!=true);
                if(json["mutation_completeness"]!=null)
                {
                    if(!(json["mutation_completeness"] is JObject proof)) throw new FormatException("Native preview completeness is invalid.");
                    nativeMutationComplete=SketchMutationCompleteness.Validate(proof,native,json["dimensions"] as JArray,json["native_constraints"] as JArray,true,nativePartial);
                }
                foreach (var item in native)
                {
                    if (item["id"] != null) throw new FormatException("Tentative preview must never export selectable CAD identities.");
                    if(item["text_glyphs"]!=null)
                    {
                        if((string)item["type"]!="text"||!(item["text_glyphs"] is JObject glyphMesh)) throw new FormatException("Native preview glyph type is invalid.");
                        var glyphs=NativeSketchTextGlyphMesh.Parse(glyphMesh);textGlyphs.Add(glyphs);
                        if((pointCount+=glyphs.VerticesMm.Count)>100000||textGlyphs.Sum(g=>g.Indices.Count/3)>200000) throw new FormatException("Native preview glyph budget exceeded.");
                    }
                    if (item["strokes_mm"] is JArray flat)
                    {
                        if (flat.Count < 4 || flat.Count % 2 != 0 || flat.Count > 16384) throw new FormatException("Invalid native preview stroke.");
                        var points = new List<CadPoint>();
                        for (int i=0;i<flat.Count;i+=2) points.Add(CadPoint.Parse(new JArray(flat[i],flat[i+1]),2));
                        if ((pointCount += points.Count) > 100000) throw new FormatException("Native preview stroke budget exceeded.");
                        paths.Add(new DesignEdge("profile:native:" + paths.Count, (string)item["type"], points));
                    }
                    else if (item["point_mm"] is JArray point)
                    {
                        var p = CadPoint.Parse(point,2);
                        paths.Add(new DesignEdge("profile:native:" + paths.Count,"point",new[] { p + new CadPoint(-.5,0),p + new CadPoint(.5,0) }));
                        paths.Add(new DesignEdge("profile:native:" + paths.Count,"point",new[] { p + new CadPoint(0,-.5),p + new CadPoint(0,.5) }));
                    }
                    else if (item["text_glyphs"]==null && item["text_mm"] is JArray textPoint && item["text"] != null)
                        annotations.Add(new SketchAnnotation(CadPoint.Parse(textPoint,2),(string)item["text"]));
                }
            }
            if(json["native_entities"] is JArray formatted)
                foreach(var item in formatted)
                {
                    string label=NativeFormatLabel(item); if(label.Length==0) continue;
                    JToken anchor=item["point_mm"] ?? item["center_mm"] ?? item["start_mm"];
                    if(anchor!=null) annotations.Add(new SketchAnnotation(CadPoint.Parse(anchor,2),label));
                }
            if(json["geometric_constraints"] is JArray nativeConstraints)
            {
                if(nativeConstraints.Count>2000) throw new FormatException("Native preview constraint budget exceeded.");
                foreach(var constraint in nativeConstraints)
                {
                    if(constraint["id"]!=null || constraint["related_entities"]!=null) throw new FormatException("Tentative constraints must never export selectable CAD identities.");
                    if(constraint["annotation_mm"]!=null) annotations.Add(new SketchAnnotation(CadPoint.Parse(constraint["annotation_mm"],2),
                        NativeSketchConstraintRules.Label((string)constraint["type"])));
                }
            }
            return new SketchSnapshot((string)json["sketch_name"], SketchFrame.Parse(json["frame"]),
                (bool?)json["visible"] ?? true, shapes, unsupported,
                (json["dimensions"] as JArray ?? new JArray()).Where(d=>d["text_mm"] is JArray).Select(d=>new SketchDimension(
                    (string)d["name"],(string)d["expression"],CadPoint.Parse(d["text_mm"],2),(bool?)d["driven"] ?? false)),
                (json["geometric_constraints"] as JArray ?? new JArray()).Select(c=>(string)c["type"]), paths, annotations,nativePartial,nativeMutationComplete,textGlyphs);
        }
        public static string NativeFormatLabel(JToken data) => string.Join(" • ",new[] {
            (bool?)data["construction"]==true && (string)data["type"]!="point" ? "Costruzione" : null,
            (bool?)data["centerline"]==true ? "Linea d'asse" : null,
            (bool?)data["hole_center"]==true ? "Punto centrale" : null }.Where(label=>label!=null));
    }

    public sealed class DesignPlane
    {
        public string Reference { get; }
        public string Name { get; }
        public SketchFrame Frame { get; }
        public string NativeSketchId { get; }
        public DesignPlane(string reference, string name, SketchFrame frame, string nativeSketchId = null)
        { Reference = reference; Name = name; Frame = frame; NativeSketchId = nativeSketchId; }
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
                Sketches = (json["sketches"] as JArray ?? new JArray()).Select(p => new DesignPlane((string)p["name"], (string)p["name"], SketchFrame.Parse(p), (string)p["sketch_id"])).ToArray(),
                SketchSnapshots = (json["sketch_snapshots"] as JArray ?? new JArray()).Select(SketchSnapshot.Parse).ToArray(),
                Faces = faces, PlanarFaces = faces.Select(p => p.Id).ToArray(),
                Parameters = (JArray)(json["parameters"] as JArray ?? new JArray()).DeepClone(),
            };
        }
    }
}
