#if INVENTOR2027
using System;
using System.Linq;
using Inventor;
using Newtonsoft.Json.Linq;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Handlers;
using Bimwright.Ipt.Shared.Infrastructure;

namespace Bimwright.Ipt.Shared.Handlers.Core;

/// <summary>
/// <c>list_topology</c> — read-only inventory of a part body's edges or faces with the same portable
/// reference tokens the modeling commands accept. Without it, edges could only be named by a human
/// selecting them in Inventor, which leaves flanges and fillets unreachable for an unattended caller.
/// Geometry is reported in millimetres; the listing is bounded and reports truncation rather than
/// streaming a whole body.
/// </summary>
public sealed class TopologyHandler : HandlerBase, IInventorCommand
{
    public string Name => "list_topology";
    public bool IsReadOnly => true;

    public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
    {
        if (!ActiveDocumentSupport.TryGetActivePart(ctx, Name, out _, out var part, out var failure)) return failure!;
        string kind = ((string?)p["kind"] ?? "edge").Trim().ToLowerInvariant();
        if (kind != "edge" && kind != "face")
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "kind must be 'edge' or 'face'.");
        int body = p["body"] == null ? 1 : p.Value<int>("body");
        int limit = p["limit"] == null ? 50 : p.Value<int>("limit");
        if (limit < 1 || limit > 200)
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT, "limit must be between 1 and 200.");
        double? minLength = p["min_length_mm"]?.Type is JTokenType.Float or JTokenType.Integer
            ? p.Value<double>("min_length_mm") : null;
        string? geometry = (string?)p["geometry"];

        var def = part.ComponentDefinition;
        if (body < 1 || body > def.SurfaceBodies.Count)
            return Fail(ctx, InventorErrorCodes.INVALID_ARGUMENT,
                "body must be between 1 and " + def.SurfaceBodies.Count + "; the part has " + def.SurfaceBodies.Count + " solid bodies.");
        var doc = (global::Inventor.Document)part;
        var solid = def.SurfaceBodies[body];
        var items = new JArray();
        int matched = 0;

        if (kind == "edge")
        {
            foreach (Edge edge in solid.Edges)
            {
                if (ctx.IsDeadlineExceeded?.Invoke() == true) throw new TimeoutException("Deadline while listing edges.");
                string type = edge.GeometryType.ToString();
                if (geometry != null && type.IndexOf(geometry, StringComparison.OrdinalIgnoreCase) < 0) continue;
                double? length = EdgeLengthMm(edge);
                if (minLength != null && (length == null || length.Value < minLength.Value)) continue;
                matched++;
                if (items.Count >= limit) continue;
                var item = new JObject
                {
                    ["id"] = EntityReferences.Describe(doc, edge)["id"],
                    ["geometry_type"] = type,
                    ["length_mm"] = length,
                    ["start_mm"] = PointArray(TryPoint(() => edge.StartVertex?.Point)),
                    ["end_mm"] = PointArray(TryPoint(() => edge.StopVertex?.Point)),
                };
                item["midpoint_mm"] = Midpoint(item["start_mm"], item["end_mm"]);
                items.Add(item);
            }
        }
        else
        {
            foreach (Face face in solid.Faces)
            {
                if (ctx.IsDeadlineExceeded?.Invoke() == true) throw new TimeoutException("Deadline while listing faces.");
                string type = face.SurfaceType.ToString();
                if (geometry != null && type.IndexOf(geometry, StringComparison.OrdinalIgnoreCase) < 0) continue;
                matched++;
                if (items.Count >= limit) continue;
                double? area = null;
                try { area = UnitConvert.Cm2ToMm2(face.Evaluator.Area); } catch { /* reported as null */ }
                var item = new JObject
                {
                    ["id"] = EntityReferences.Describe(doc, face)["id"],
                    ["surface_type"] = type,
                    ["area_mm2"] = area,
                    ["point_on_face_mm"] = PointArray(TryPoint(() => face.PointOnFace)),
                    ["edge_count"] = face.Edges.Count,
                };
                if (face.SurfaceType == SurfaceTypeEnum.kPlaneSurface)
                {
                    try
                    {
                        var plane = (Plane)face.Geometry;
                        item["normal"] = new JArray(plane.Normal.X, plane.Normal.Y, plane.Normal.Z);
                    }
                    catch { item["normal"] = null; }
                }
                items.Add(item);
            }
        }

        return Ok(ctx, new JObject
        {
            ["kind"] = kind,
            ["body"] = body,
            ["body_count"] = def.SurfaceBodies.Count,
            ["matched"] = matched,
            ["returned"] = items.Count,
            ["truncated"] = matched > items.Count,
            ["items"] = items,
        });
    }

    private static double? EdgeLengthMm(Edge edge)
    {
        try
        {
            var evaluator = edge.Evaluator;
            evaluator.GetParamExtents(out double min, out double max);
            evaluator.GetLengthAtParam(min, max, out double length);
            return UnitConvert.CmToMm(length);
        }
        catch { return null; }
    }

    private static Point? TryPoint(Func<Point?> read)
    {
        try { return read(); }
        catch { return null; }
    }

    private static JToken PointArray(Point? point)
        => point == null ? JValue.CreateNull()
            : new JArray(UnitConvert.CmToMm(point.X), UnitConvert.CmToMm(point.Y), UnitConvert.CmToMm(point.Z));

    private static JToken Midpoint(JToken? start, JToken? end)
    {
        if (start is not JArray a || end is not JArray b) return JValue.CreateNull();
        return new JArray(((double)a[0]! + (double)b[0]!) / 2,
                          ((double)a[1]! + (double)b[1]!) / 2,
                          ((double)a[2]! + (double)b[2]!) / 2);
    }
}
#endif
