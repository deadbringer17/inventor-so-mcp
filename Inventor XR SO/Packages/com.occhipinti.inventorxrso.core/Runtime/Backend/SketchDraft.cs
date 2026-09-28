using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace InventorXrSo.Core.Backend
{
    public enum SketchShape { Line, Rectangle, Circle }
    public sealed class SketchElement
    {
        public SketchShape Shape { get; }
        public CadPoint A { get; }
        public CadPoint B { get; }
        public double Radius { get; }
        public bool Dimensioned { get; }
        public CadPoint? DimensionText { get; }
        public IReadOnlyList<string> Constraints { get; }
        public SketchElement(SketchShape shape, CadPoint a, CadPoint b, double radius = 0, bool dimensioned = false, CadPoint? dimensionText = null,
            IEnumerable<string> constraints = null)
        {
            Shape = shape; A = a; B = b; Radius = radius; Dimensioned = dimensioned;
            DimensionText = dimensionText;
            Constraints=constraints?.ToArray() ?? Array.Empty<string>();
            Operation("validation");
        }
        public JObject Operation(string sketch)
        {
            var operation = Shape switch {
            SketchShape.Line => DesignOperations.Line(sketch, A.X, A.Y, B.X, B.Y),
            SketchShape.Rectangle => DesignOperations.Rectangle(sketch, A.X, A.Y, B.X, B.Y),
            SketchShape.Circle => DesignOperations.Circle(sketch, A.X, A.Y, Radius),
            _ => throw new ArgumentException("Unsupported sketch shape."),
            };
            if (Dimensioned) operation["arguments"]["add_dimensions"] = true;
            if (Dimensioned && DimensionText.HasValue)
            {
                operation["arguments"]["text_x"] = DimensionText.Value.X;
                operation["arguments"]["text_y"] = DimensionText.Value.Y;
            }
            return operation;
        }
        public IEnumerable<(CadPoint point, string kind)> SnapPoints()
        {
            if (Shape == SketchShape.Circle) { yield return (A, "Centro"); yield break; }
            yield return (A, "Estremo"); yield return (B, "Estremo");
            if (Shape == SketchShape.Rectangle)
            {
                var c = new CadPoint(A.X, B.Y); var d = new CadPoint(B.X, A.Y);
                yield return (c, "Estremo"); yield return (d, "Estremo");
                yield return ((A+c)*0.5, "Medio"); yield return ((A+d)*0.5, "Medio");
                yield return ((B+c)*0.5, "Medio"); yield return ((B+d)*0.5, "Medio");
            }
            else yield return ((A+B)*0.5, "Medio");
        }
        public double DistanceTo(CadPoint point)
        {
            if (Shape==SketchShape.Circle) return Math.Abs((point-A).Length-Radius);
            double best=double.PositiveInfinity; var outline=Outline();
            for(int i=1;i<outline.Length;i++)
            {
                var edge=outline[i]-outline[i-1];
                double t=Math.Max(0,Math.Min(1,(point-outline[i-1]).Dot(edge)/edge.Dot(edge)));
                best=Math.Min(best,(point-(outline[i-1]+edge*t)).Length);
            }
            return best;
        }
        public CadPoint[] Outline(int circleSegments = 64)
        {
            if (Shape == SketchShape.Line) return new[] { A, B };
            if (Shape == SketchShape.Rectangle) return new[] { A, new CadPoint(B.X,A.Y), B, new CadPoint(A.X,B.Y), A };
            if (circleSegments < 8 || circleSegments > 1024) throw new ArgumentOutOfRangeException(nameof(circleSegments));
            return Enumerable.Range(0, circleSegments+1).Select(i =>
                new CadPoint(A.X+Radius*Math.Cos(i*2*Math.PI/circleSegments), A.Y+Radius*Math.Sin(i*2*Math.PI/circleSegments))).ToArray();
        }
    }

    public readonly struct SketchSnap
    {
        public readonly CadPoint Point;
        public readonly string Kind;
        public SketchSnap(CadPoint point, string kind) { Point = point; Kind = kind; }
    }

    public sealed class SketchDraft
    {
        private readonly List<SketchElement> _elements = new List<SketchElement>();
        private readonly List<(string kind,int a,int b,int axis)> _constraints = new List<(string,int,int,int)>();
        public int ConstraintCount => _constraints.Count;
        public string Name { get; }
        public string Plane { get; }
        public SketchFrame Frame { get; set; }
        public IReadOnlyList<SketchElement> Elements => _elements.AsReadOnly();
        public SketchDraft(string plane, SketchFrame frame, string name = null)
        {
            Name = name ?? "XR_" + Guid.NewGuid().ToString("N").Substring(0,12);
            Plane = plane; Frame = frame;
            DesignOperations.CreateSketch(plane, Name);
        }
        public void Add(SketchElement shape)
        {
            if (_elements.Count+_constraints.Count >= 30) throw new InvalidOperationException("Massimo 30 geometrie e vincoli per comando schizzo.");
            _elements.Add(shape ?? throw new ArgumentNullException(nameof(shape)));
        }
        public void RemoveLast()
        {
            if (_elements.Count == 0) return;
            _elements.RemoveAt(_elements.Count-1);
            _constraints.RemoveAll(c=>c.a/4>=_elements.Count || c.b/4>=_elements.Count || (c.axis>=0 && c.axis/4>=_elements.Count));
        }
        public void Replace(int index, SketchElement shape)
        {
            if (shape==null) throw new ArgumentNullException(nameof(shape));
            if (_elements[index].Shape!=shape.Shape) _constraints.RemoveAll(c=>c.a/4==index || c.b/4==index || (c.axis>=0 && c.axis/4==index));
            _elements[index] = shape;
        }
        public void AddConstraint(string kind,int a,int b,int axis=-1)
            => AddEntityConstraint(kind,checked(a*4),checked(b*4),axis<0 ? -1 : checked(axis*4));
        // Keys reserve four slots per draft element; rectangle sides follow its outline.
        public IEnumerable<int> ConstraintEntityKeys => Enumerable.Range(0,_elements.Count)
            .SelectMany(i=>Enumerable.Range(i*4,_elements[i].Shape==SketchShape.Rectangle ? 4 : 1));
        public SketchElement ConstraintEntity(int key)
        {
            if(key<0 || key/4>=_elements.Count) throw new ArgumentException("Geometria del vincolo non disponibile.");
            var element=_elements[key/4];
            if(element.Shape!=SketchShape.Rectangle)
            {
                if(key%4!=0) throw new ArgumentException("Lato del vincolo non disponibile.");
                return element;
            }
            var points=element.Outline();
            return new SketchElement(SketchShape.Line,points[key%4],points[key%4+1]);
        }
        public string ConstraintEntityLabel(int key)
        {
            ConstraintEntity(key);
            var shape=_elements[key/4].Shape;
            return (shape==SketchShape.Rectangle ? "Rettangolo " : shape==SketchShape.Line ? "Linea " : "Cerchio ")
                +(key/4+1)+(shape==SketchShape.Rectangle ? " • lato "+(key%4+1) : "");
        }
        public void AddEntityConstraint(string kind,int a,int b,int axis=-1)
        {
            if (_elements.Count+_constraints.Count>=30) throw new InvalidOperationException("Massimo 30 geometrie e vincoli per comando schizzo.");
            if (a==b)
                throw new ArgumentException("Seleziona due geometrie distinte.");
            var first=ConstraintEntity(a).Shape; var second=ConstraintEntity(b).Shape;
            bool lines=first==SketchShape.Line && second==SketchShape.Line;
            bool circles=first==SketchShape.Circle && second==SketchShape.Circle;
            bool tangent=(first==SketchShape.Line && second==SketchShape.Circle)
                || (first==SketchShape.Circle && second==SketchShape.Line) || circles;
            bool valid=kind=="equal" ? lines || circles : kind=="tangent" ? tangent
                : kind=="concentric" ? circles : (kind=="parallel" || kind=="perpendicular" || kind=="symmetric") && lines;
            if (!valid) throw new ArgumentException("Il vincolo non è compatibile con le geometrie selezionate.");
            if (kind=="symmetric" && (axis<0 || axis==a || axis==b || ConstraintEntity(axis).Shape!=SketchShape.Line))
                throw new ArgumentException("Seleziona una terza linea come asse di simmetria.");
            if (kind!="symmetric") axis=-1;
            if (_constraints.Any(c=>c.kind==kind && c.axis==axis && ((c.a==a && c.b==b)||(c.a==b && c.b==a))))
                throw new ArgumentException("Questo vincolo è già presente nella bozza.");
            _constraints.Add((kind,a,b,axis));
        }
        public void RemoveLastConstraint() { if (_constraints.Count>0) _constraints.RemoveAt(_constraints.Count-1); }
        private string TypedReference(int key)
        {
            int index=key/4; int count=1+key%4; var shape=ConstraintEntity(key).Shape;
            for(int i=0;i<index;i++)
                if (shape==SketchShape.Line) count+=_elements[i].Shape==SketchShape.Rectangle ? 4 : _elements[i].Shape==SketchShape.Line ? 1 : 0;
                else if (_elements[i].Shape==SketchShape.Circle) count++;
            return (shape==SketchShape.Line ? "line:" : "circle:")+count;
        }
        public int Pick(CadPoint point, double toleranceMm)
        {
            if (!(toleranceMm > 0) || double.IsInfinity(toleranceMm)) throw new ArgumentOutOfRangeException(nameof(toleranceMm));
            int best = -1; double distance = toleranceMm;
            for (int index = 0; index < _elements.Count; index++)
            {
                var element = _elements[index];
                if (element.Shape == SketchShape.Circle)
                {
                    double d = Math.Abs((point-element.A).Length-element.Radius);
                    if (d <= distance) { best=index; distance=d; }
                    continue;
                }
                var outline=element.Outline();
                for(int i=1;i<outline.Length;i++)
                {
                    var edge=outline[i]-outline[i-1];
                    double t=Math.Max(0,Math.Min(1,(point-outline[i-1]).Dot(edge)/edge.Dot(edge)));
                    double d=(point-(outline[i-1]+edge*t)).Length;
                    if(d<=distance) { best=index; distance=d; }
                }
            }
            return best;
        }
        public JArray Operations(JObject feature = null)
        {
            var result = new JArray(DesignOperations.CreateSketch(Plane, Name));
            foreach (var element in _elements) result.Add(element.Operation(Name));
            foreach (var constraint in _constraints)
            {
                var ids = new JArray(TypedReference(constraint.a),TypedReference(constraint.b));
                if (constraint.axis>=0) ids.Add(TypedReference(constraint.axis));
                string kind=constraint.kind=="equal" && ConstraintEntity(constraint.a).Shape==SketchShape.Circle ? "equal_radius" : constraint.kind;
                result.Add(new JObject { ["command"]="add_sketch_constraint",["arguments"]=new JObject {
                    ["sketch_name"]=Name,["type"]=kind,["entity_ids"]=ids } });
            }
            if (feature != null) result.Add(feature.DeepClone());
            return result;
        }
        public SketchSnap Snap(CadPoint point, CadPoint? start, double toleranceMm)
        {
            if (!(toleranceMm > 0) || double.IsInfinity(toleranceMm)) throw new ArgumentOutOfRangeException(nameof(toleranceMm));
            double distance = toleranceMm;
            var best = new SketchSnap(point, "");
            foreach (var element in _elements)
            foreach (var candidate in element.SnapPoints())
            {
                double d = (point-candidate.point).Length;
                if (d <= distance) { distance = d; best = new SketchSnap(candidate.point, candidate.kind); }
            }
            if (best.Kind.Length > 0 || !start.HasValue) return best;
            var origin = start.Value;
            if (Math.Abs(point.Y-origin.Y) <= toleranceMm) return new SketchSnap(new CadPoint(point.X,origin.Y), "Orizzontale");
            if (Math.Abs(point.X-origin.X) <= toleranceMm) return new SketchSnap(new CadPoint(origin.X,point.Y), "Verticale");
            return best;
        }
    }
}
