#if INVENTOR2027 && SO_EXPERIMENTAL
using System;
using System.Collections.Generic;
using System.Linq;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;
using Inventor;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Handlers.Experimental;

/// <summary>Sketch-side helpers shared by the experimental sketch commands.</summary>
internal static class SketchX
{
    public static PlanarSketch Target(Application app, JObject p, string command)
        => X.Sketch(X.ActivePart(app, command).ComponentDefinition, (string?)p["sketch_name"]);

    public static SketchEntity Entity(PlanarSketch sketch, string id) => EntityResolver.ResolveSketchEntity(sketch, id);

    /// <summary>1-based id of the most recently added sketch entity, the convention of the verified draw commands.</summary>
    public static string LastId(PlanarSketch sketch) => sketch.SketchEntities.Count.ToString();

    public static JObject Result(PlanarSketch sketch, int added) => new()
    {
        ["sketch_name"] = sketch.Name,
        ["added"] = added,
        ["last_entity_id"] = LastId(sketch),
    };

    public static SketchPoint PointOf(SketchEntity entity, string which) => (entity, which) switch
    {
        (SketchLine line, "start") => line.StartSketchPoint,
        (SketchLine line, "end") => line.EndSketchPoint,
        (SketchArc arc, "start") => arc.StartSketchPoint,
        (SketchArc arc, "end") => arc.EndSketchPoint,
        (SketchArc arc, "center") => arc.CenterSketchPoint,
        (SketchCircle circle, "center") => circle.CenterSketchPoint,
        (SketchEllipse ellipse, "center") => ellipse.CenterSketchPoint,
        (SketchPoint point, _) => point,
        _ => throw new ArgumentException("That entity has no '" + which + "' point (use start|end for lines and arcs, center for circles, arcs and ellipses)."),
    };
}

public sealed class DrawEllipseHandler : ExperimentalHandler
{
    public override string Name => "draw_ellipse";
    public override bool IsReadOnly => false;
    protected override JToken Run(InventorCommandContext ctx, Application app, JObject p)
    {
        var sketch = SketchX.Target(app, p, Name);
        double major = X.Positive(p, "major_radius"), minor = X.Positive(p, "minor_radius");
        if (minor > major) throw new ArgumentException("minor_radius must not exceed major_radius.");
        double angle = X.Num(p, "rotation_deg", 0) * Math.PI / 180.0;
        sketch.SketchEllipses.Add(X.P2(app, X.Num(p, "cx"), X.Num(p, "cy")),
            app.TransientGeometry.CreateUnitVector2d(Math.Cos(angle), Math.Sin(angle)),
            UnitConvert.MmToCm(major), UnitConvert.MmToCm(minor));
        return SketchX.Result(sketch, 1);
    }
}

public sealed class DrawSplineHandler : ExperimentalHandler
{
    public override string Name => "draw_spline";
    public override bool IsReadOnly => false;
    protected override JToken Run(InventorCommandContext ctx, Application app, JObject p)
    {
        var sketch = SketchX.Target(app, p, Name);
        var points = X.Points(p, "points_mm", 2, 64);
        var fit = app.TransientObjects.CreateObjectCollection();
        foreach (var point in points) fit.Add(X.P2(app, point[0], point[1]));
        dynamic spline = ((dynamic)sketch.SketchSplines).Add(fit);
        if (X.Bool(p, "closed", false)) spline.Closed = true;
        return SketchX.Result(sketch, 1);
    }
}

/// <summary>Straight slot: two lines and two end arcs sharing their end points (closed profile).</summary>
public sealed class DrawSlotHandler : ExperimentalHandler
{
    public override string Name => "draw_slot";
    public override bool IsReadOnly => false;
    protected override JToken Run(InventorCommandContext ctx, Application app, JObject p)
    {
        var sketch = SketchX.Target(app, p, Name);
        double x1 = X.Num(p, "x1"), y1 = X.Num(p, "y1"), x2 = X.Num(p, "x2"), y2 = X.Num(p, "y2");
        double r = X.Positive(p, "width_mm") / 2;
        double dx = x2 - x1, dy = y2 - y1, length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 1e-6) throw new ArgumentException("The two slot centres must differ.");
        double ux = dx / length, uy = dy / length, nx = -uy, ny = ux;
        var lines = sketch.SketchLines;
        var top = lines.AddByTwoPoints(X.P2(app, x1 + nx * r, y1 + ny * r), X.P2(app, x2 + nx * r, y2 + ny * r));
        var bottom = lines.AddByTwoPoints(X.P2(app, x2 - nx * r, y2 - ny * r), X.P2(app, x1 - nx * r, y1 - ny * r));
        // From +n to -n through +u is clockwise at the far end; from -n to +n through -u is clockwise at the near end.
        var arcs = sketch.SketchArcs;
        arcs.AddByCenterStartEndPoint(X.P2(app, x2, y2), top.EndSketchPoint, bottom.StartSketchPoint, false);
        arcs.AddByCenterStartEndPoint(X.P2(app, x1, y1), bottom.EndSketchPoint, top.StartSketchPoint, false);
        return SketchX.Result(sketch, 4);
    }
}

/// <summary>Regular polygon as chained lines sharing end points.</summary>
public sealed class DrawPolygonHandler : ExperimentalHandler
{
    public override string Name => "draw_polygon";
    public override bool IsReadOnly => false;
    protected override JToken Run(InventorCommandContext ctx, Application app, JObject p)
    {
        var sketch = SketchX.Target(app, p, Name);
        double cx = X.Num(p, "cx"), cy = X.Num(p, "cy"), radius = X.Positive(p, "radius");
        int sides = X.Int(p, "sides", 3, 64);
        bool inscribed = X.Bool(p, "inscribed", true);
        double vertexRadius = inscribed ? radius : radius / Math.Cos(Math.PI / sides);
        double start = X.Num(p, "rotation_deg", 0) * Math.PI / 180.0;
        var lines = sketch.SketchLines;
        SketchLine? first = null, previous = null;
        for (int i = 0; i < sides; i++)
        {
            double a0 = start + 2 * Math.PI * i / sides, a1 = start + 2 * Math.PI * (i + 1) / sides;
            object from = previous == null ? X.P2(app, cx + vertexRadius * Math.Cos(a0), cy + vertexRadius * Math.Sin(a0)) : previous.EndSketchPoint;
            object to = i == sides - 1 ? first!.StartSketchPoint : X.P2(app, cx + vertexRadius * Math.Cos(a1), cy + vertexRadius * Math.Sin(a1));
            previous = lines.AddByTwoPoints(from, to);
            first ??= previous;
        }
        return SketchX.Result(sketch, sides);
    }
}

public sealed class OffsetSketchEntitiesHandler : ExperimentalHandler
{
    public override string Name => "offset_sketch_entities";
    public override bool IsReadOnly => false;
    protected override JToken Run(InventorCommandContext ctx, Application app, JObject p)
    {
        var sketch = SketchX.Target(app, p, Name);
        double distance = X.Num(p, "distance_mm");
        if (Math.Abs(distance) < 1e-6) throw new ArgumentException("distance_mm must not be 0.");
        var entities = app.TransientObjects.CreateObjectCollection();
        foreach (var id in X.Strings(p, "entity_ids")) entities.Add(SketchX.Entity(sketch, id));
        dynamic created = ((dynamic)sketch).OffsetSketchEntitiesUsingDistance(entities, UnitConvert.MmToCm(Math.Abs(distance)), distance > 0, true, false);
        return SketchX.Result(sketch, (int)created.Count);
    }
}

/// <summary>
/// Mirror lines, circles, arcs and points across a sketch line, and bind each copy with a symmetry
/// constraint so the mirror follows later edits of the original.
/// </summary>
public sealed class MirrorSketchEntitiesHandler : ExperimentalHandler
{
    public override string Name => "mirror_sketch_entities";
    public override bool IsReadOnly => false;
    protected override JToken Run(InventorCommandContext ctx, Application app, JObject p)
    {
        var sketch = SketchX.Target(app, p, Name);
        var axis = SketchX.Entity(sketch, X.Str(p, "mirror_line_id")) as SketchLine
            ?? throw new ArgumentException("mirror_line_id must be a sketch line.");
        var a = axis.StartSketchPoint.Geometry;
        var b = axis.EndSketchPoint.Geometry;
        double dx = b.X - a.X, dy = b.Y - a.Y, len2 = dx * dx + dy * dy;
        if (len2 < 1e-12) throw new ArgumentException("The mirror line has zero length.");
        Point2d Mirror(Point2d q)
        {
            double t = ((q.X - a.X) * dx + (q.Y - a.Y) * dy) / len2;
            double fx = a.X + t * dx, fy = a.Y + t * dy;
            return app.TransientGeometry.CreatePoint2d(2 * fx - q.X, 2 * fy - q.Y);
        }
        int added = 0;
        var constraints = sketch.GeometricConstraints;
        foreach (var id in X.Strings(p, "entity_ids"))
        {
            var entity = SketchX.Entity(sketch, id);
            switch (entity)
            {
                case SketchLine line:
                {
                    var copy = sketch.SketchLines.AddByTwoPoints(Mirror(line.StartSketchPoint.Geometry), Mirror(line.EndSketchPoint.Geometry));
                    constraints.AddSymmetry((SketchEntity)line, (SketchEntity)copy, axis);
                    break;
                }
                case SketchCircle circle:
                {
                    var copy = sketch.SketchCircles.AddByCenterRadius(Mirror(circle.CenterSketchPoint.Geometry), circle.Radius);
                    constraints.AddSymmetry((SketchEntity)circle, (SketchEntity)copy, axis);
                    break;
                }
                case SketchArc arc:
                {
                    // A mirror reverses orientation: swap start and end to keep the same arc.
                    var copy = sketch.SketchArcs.AddByCenterStartEndPoint(Mirror(arc.CenterSketchPoint.Geometry),
                        Mirror(arc.EndSketchPoint.Geometry), Mirror(arc.StartSketchPoint.Geometry), true);
                    constraints.AddSymmetry((SketchEntity)arc, (SketchEntity)copy, axis);
                    break;
                }
                case SketchPoint point:
                {
                    var copy = sketch.SketchPoints.Add(Mirror(point.Geometry), false);
                    constraints.AddSymmetry((SketchEntity)point, (SketchEntity)copy, axis);
                    break;
                }
                default:
                    throw new ArgumentException("Entity " + id + " cannot be mirrored (lines, circles, arcs and points).");
            }
            added++;
        }
        return SketchX.Result(sketch, added);
    }
}

public sealed class MoveSketchPointHandler : ExperimentalHandler
{
    public override string Name => "move_sketch_point";
    public override bool IsReadOnly => false;
    protected override JToken Run(InventorCommandContext ctx, Application app, JObject p)
    {
        var sketch = SketchX.Target(app, p, Name);
        var point = SketchX.PointOf(SketchX.Entity(sketch, X.Str(p, "entity_id")), X.Str(p, "point").ToLowerInvariant());
        point.MoveTo(X.P2(app, X.Num(p, "x"), X.Num(p, "y")));
        return new JObject { ["sketch_name"] = sketch.Name, ["point_mm"] = X.Mm2(point.Geometry) };
    }
}

public sealed class DeleteSketchEntityHandler : ExperimentalHandler
{
    public override string Name => "delete_sketch_entity";
    public override bool IsReadOnly => false;
    protected override JToken Run(InventorCommandContext ctx, Application app, JObject p)
    {
        var sketch = SketchX.Target(app, p, Name);
        // Resolve everything first, then delete highest id first so the remaining ids stay valid.
        var targets = X.Strings(p, "entity_ids")
            .Select(id => (index: EntityResolver.ParseIndex(id, "entity"), entity: SketchX.Entity(sketch, id)))
            .OrderByDescending(t => t.index).ToArray();
        foreach (var (_, entity) in targets) entity.Delete();
        return new JObject { ["sketch_name"] = sketch.Name, ["deleted"] = targets.Length };
    }
}

/// <summary>add_dimension: typed sketch dimension, optionally named, valued and driven.</summary>
public sealed class AddDimensionHandler : ExperimentalHandler
{
    public override string Name => "add_dimension";
    public override bool IsReadOnly => false;
    protected override JToken Run(InventorCommandContext ctx, Application app, JObject p)
    {
        var sketch = SketchX.Target(app, p, Name);
        string kind = X.Str(p, "kind").ToLowerInvariant();
        var ids = X.Strings(p, "entity_ids", 2);
        var entities = ids.Select(id => SketchX.Entity(sketch, id)).ToArray();
        bool driven = X.Bool(p, "driven", false);
        var dims = sketch.DimensionConstraints;
        Point2d Text(Point2d near) => p["text_x"] != null && p["text_y"] != null
            ? X.P2(app, X.Num(p, "text_x"), X.Num(p, "text_y"))
            : app.TransientGeometry.CreatePoint2d(near.X + 0.5, near.Y + 0.5);

        Parameter parameter;
        switch (kind)
        {
            case "distance":
            case "horizontal_distance":
            case "vertical_distance":
            {
                var orientation = kind == "horizontal_distance" ? DimensionOrientationEnum.kHorizontalDim
                    : kind == "vertical_distance" ? DimensionOrientationEnum.kVerticalDim : DimensionOrientationEnum.kAlignedDim;
                SketchPoint one, two;
                if (entities.Length == 1 && entities[0] is SketchLine line) { one = line.StartSketchPoint; two = line.EndSketchPoint; }
                else if (entities.Length == 2) { one = SketchX.PointOf(entities[0], "center"); two = SketchX.PointOf(entities[1], "center"); }
                else throw new ArgumentException(kind + " needs one line or two points.");
                var mid = app.TransientGeometry.CreatePoint2d((one.Geometry.X + two.Geometry.X) / 2, (one.Geometry.Y + two.Geometry.Y) / 2);
                parameter = dims.AddTwoPointDistance(one, two, orientation, Text(mid), driven).Parameter;
                break;
            }
            case "angle":
            {
                if (entities.Length != 2 || entities[0] is not SketchLine l1 || entities[1] is not SketchLine l2)
                    throw new ArgumentException("angle needs two sketch lines.");
                parameter = dims.AddTwoLineAngle(l1, l2, Text(l1.EndSketchPoint.Geometry), driven).Parameter;
                break;
            }
            case "radius":
            case "diameter":
            {
                var curve = entities[0];
                var center = curve switch
                {
                    SketchCircle c => c.CenterSketchPoint.Geometry,
                    SketchArc a => a.CenterSketchPoint.Geometry,
                    _ => throw new ArgumentException(kind + " needs a circle or an arc."),
                };
                parameter = kind == "radius"
                    ? dims.AddRadius(curve, Text(center), driven).Parameter
                    : dims.AddDiameter(curve, Text(center), driven).Parameter;
                break;
            }
            case "arc_length":
            {
                if (entities[0] is not SketchArc arc) throw new ArgumentException("arc_length needs an arc.");
                parameter = (Parameter)((dynamic)dims).AddArcLength(arc, Text(arc.CenterSketchPoint.Geometry), driven).Parameter;
                break;
            }
            default:
                throw new ArgumentException("kind must be distance, horizontal_distance, vertical_distance, angle, radius, diameter or arc_length.");
        }
        if (!driven && !string.IsNullOrWhiteSpace((string?)p["value"])) parameter.Expression = (string)p["value"]!;
        if (!string.IsNullOrWhiteSpace((string?)p["name"])) parameter.Name = ((string)p["name"]!).Trim();
        return new JObject
        {
            ["sketch_name"] = sketch.Name, ["dimension"] = parameter.Name, ["expression"] = parameter.Expression, ["driven"] = driven,
        };
    }
}

internal static class DimensionX
{
    public static DimensionConstraint Find(PlanarSketch sketch, string name)
    {
        foreach (DimensionConstraint dimension in sketch.DimensionConstraints)
            if (string.Equals(dimension.Parameter.Name, name, StringComparison.Ordinal)) return dimension;
        throw new ArgumentException("Sketch " + sketch.Name + " has no dimension named '" + name + "'.");
    }
}

public sealed class EditDimensionHandler : ExperimentalHandler
{
    public override string Name => "edit_dimension";
    public override bool IsReadOnly => false;
    protected override JToken Run(InventorCommandContext ctx, Application app, JObject p)
    {
        var sketch = SketchX.Target(app, p, Name);
        var dimension = DimensionX.Find(sketch, X.Str(p, "dimension"));
        if (p["driven"]?.Type == JTokenType.Boolean) dimension.Driven = (bool)p["driven"]!;
        if (!string.IsNullOrWhiteSpace((string?)p["expression"]))
        {
            if (dimension.Driven) throw new ArgumentException("A driven dimension has no editable expression; set driven=false first.");
            dimension.Parameter.Expression = (string)p["expression"]!;
        }
        if (!string.IsNullOrWhiteSpace((string?)p["name"])) dimension.Parameter.Name = ((string)p["name"]!).Trim();
        return new JObject
        {
            ["sketch_name"] = sketch.Name, ["dimension"] = dimension.Parameter.Name,
            ["expression"] = dimension.Parameter.Expression, ["driven"] = dimension.Driven,
        };
    }
}

public sealed class DeleteDimensionHandler : ExperimentalHandler
{
    public override string Name => "delete_dimension";
    public override bool IsReadOnly => false;
    protected override JToken Run(InventorCommandContext ctx, Application app, JObject p)
    {
        var sketch = SketchX.Target(app, p, Name);
        string name = X.Str(p, "dimension");
        DimensionX.Find(sketch, name).Delete();
        return new JObject { ["sketch_name"] = sketch.Name, ["deleted"] = name };
    }
}

/// <summary>
/// Constraint kinds added by the experimental build to the verified <c>add_sketch_constraint</c>
/// command. Returns false for kinds it does not know, so the verified handler reports them.
/// </summary>
internal static class ExperimentalSketchConstraints
{
    public static bool TryAdd(GeometricConstraints constraints, string type, Func<int, SketchEntity> entity, int count)
    {
        switch (type)
        {
            case "midpoint":
                if (count < 2) throw new ArgumentException("midpoint needs [point, curve].");
                ((dynamic)constraints).AddMidpoint(SketchX.PointOf(entity(0), "center"), entity(1));
                return true;
            case "fix":
                ((dynamic)constraints).AddGround(entity(0));
                return true;
            case "equal_radius":
                if (count < 2) throw new ArgumentException("equal_radius needs two circles or arcs.");
                ((dynamic)constraints).AddEqualRadius(entity(0), entity(1));
                return true;
            default:
                return false;
        }
    }
}

/// <summary>get_sketch_info: entities, constraints, dimensions, status and closed loops of one sketch. Read-only.</summary>
public sealed class GetSketchInfoHandler : ExperimentalHandler
{
    public override string Name => "get_sketch_info";
    public override bool IsReadOnly => true;

    protected override JToken Run(InventorCommandContext ctx, Application app, JObject p)
    {
        var sketch = SketchX.Target(app, p, Name);
        int max = p["max_entities"]?.Type == JTokenType.Integer ? Math.Max(1, Math.Min(20000, (int)p["max_entities"]!)) : 2000;
        var entities = new JArray();
        var loops = new LoopFinder();
        int index = 0;
        foreach (SketchEntity entity in sketch.SketchEntities)
        {
            index++;
            X.Deadline(ctx, "while reading the sketch");
            var item = new JObject { ["entity_id"] = index.ToString() };
            try { item["construction"] = entity.Construction; } catch { }
            var related = new JArray();
            foreach (object relation in entity.Constraints)
            {
                if (related.Count >= max) break;
                if (relation is GeometricConstraint geometric) related.Add(geometric.Type.ToString());
                else if (relation is DimensionConstraint dimensional) related.Add(dimensional.Type.ToString());
            }
            item["constraints"] = related;
            switch (entity)
            {
                case SketchLine line:
                    item["type"] = "line";
                    item["start_mm"] = X.Mm2(line.StartSketchPoint.Geometry);
                    item["end_mm"] = X.Mm2(line.EndSketchPoint.Geometry);
                    item["length_mm"] = UnitConvert.CmToMm(line.Length);
                    loops.Edge(line.StartSketchPoint.Geometry, line.EndSketchPoint.Geometry, index);
                    break;
                case SketchArc arc:
                    item["type"] = "arc";
                    item["center_mm"] = X.Mm2(arc.CenterSketchPoint.Geometry);
                    item["radius_mm"] = UnitConvert.CmToMm(arc.Radius);
                    item["start_mm"] = X.Mm2(arc.StartSketchPoint.Geometry);
                    item["end_mm"] = X.Mm2(arc.EndSketchPoint.Geometry);
                    loops.Edge(arc.StartSketchPoint.Geometry, arc.EndSketchPoint.Geometry, index);
                    break;
                case SketchCircle circle:
                    item["type"] = "circle";
                    item["center_mm"] = X.Mm2(circle.CenterSketchPoint.Geometry);
                    item["radius_mm"] = UnitConvert.CmToMm(circle.Radius);
                    loops.Closed(index);
                    break;
                case SketchEllipse ellipse:
                    item["type"] = "ellipse";
                    item["center_mm"] = X.Mm2(ellipse.CenterSketchPoint.Geometry);
                    item["major_radius_mm"] = UnitConvert.CmToMm(ellipse.MajorRadius);
                    item["minor_radius_mm"] = UnitConvert.CmToMm(ellipse.MinorRadius);
                    loops.Closed(index);
                    break;
                case SketchSpline spline:
                    item["type"] = "spline";
                    bool closed = false;
                    try { closed = (bool)((dynamic)spline).Closed; } catch { }
                    item["closed"] = closed;
                    if (closed) loops.Closed(index);
                    break;
                case SketchPoint point:
                    item["type"] = "point";
                    item["point_mm"] = X.Mm2(point.Geometry);
                    item["hole_center"] = point.HoleCenter;
                    break;
                default:
                    item["type"] = entity.Type.ToString();
                    break;
            }
            if (entities.Count < max) entities.Add(item);
        }

        var constraints = new JArray();
        foreach (GeometricConstraint constraint in sketch.GeometricConstraints)
        {
            if (constraints.Count >= max) break;
            constraints.Add(new JObject { ["type"] = constraint.Type.ToString(), ["deletable"] = constraint.Deletable });
        }
        var dimensions = new JArray();
        foreach (DimensionConstraint dimension in sketch.DimensionConstraints)
        {
            if (dimensions.Count >= max) break;
            var parameter = dimension.Parameter;
            dimensions.Add(new JObject
            {
                ["name"] = parameter.Name, ["expression"] = parameter.Expression, ["type"] = dimension.Type.ToString(),
                ["driven"] = dimension.Driven, ["value"] = JToken.FromObject(parameter.Value),
                ["text_mm"] = X.Mm2(dimension.TextPoint),
            });
        }
        string status;
        try { status = sketch.ConstraintStatus.ToString(); } catch { status = "unknown"; }
        return new JObject
        {
            ["sketch_name"] = sketch.Name,
            ["entity_count"] = index,
            ["entities"] = entities,
            ["truncated"] = index > max,
            ["geometric_constraints"] = constraints,
            ["dimensions"] = dimensions,
            ["constraint_status"] = status,
            ["fully_constrained"] = status == "kFullyConstrainedConstraintStatus",
            ["profiles"] = sketch.Profiles.Count,
            ["closed_loops"] = loops.Loops(),
        };
    }

    /// <summary>
    /// Closed loops from the sketch topology alone (no profile is created, so the read stays
    /// read-only): end points are matched on a 1e-6 cm grid; a connected component of lines and arcs
    /// in which every point has exactly two ends is a closed loop.
    /// </summary>
    private sealed class LoopFinder
    {
        private readonly List<(string a, string b, int id)> _edges = new();
        private readonly List<int> _closed = new();
        private static string Key(Point2d q) => Math.Round(q.X, 6) + "," + Math.Round(q.Y, 6);
        public void Edge(Point2d a, Point2d b, int id) => _edges.Add((Key(a), Key(b), id));
        public void Closed(int id) => _closed.Add(id);

        public JArray Loops()
        {
            var result = new JArray(_closed.Select(id => new JArray(id.ToString())));
            var parent = new Dictionary<string, string>();
            string Find(string x) { while (parent[x] != x) x = parent[x] = parent[parent[x]]; return x; }
            foreach (var (a, b, _) in _edges) { parent.TryAdd(a, a); parent.TryAdd(b, b); parent[Find(a)] = Find(b); }
            var degree = new Dictionary<string, int>();
            foreach (var (a, b, _) in _edges) { degree[a] = degree.GetValueOrDefault(a) + 1; degree[b] = degree.GetValueOrDefault(b) + 1; }
            foreach (var component in _edges.GroupBy(e => Find(e.a)))
            {
                var nodes = component.SelectMany(e => new[] { e.a, e.b }).Distinct().ToArray();
                if (nodes.All(n => degree[n] == 2))
                    result.Add(new JArray(component.Select(e => e.id.ToString())));
            }
            return result;
        }
    }
}
#endif
