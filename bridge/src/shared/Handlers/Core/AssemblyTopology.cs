#if INVENTOR2027
using System;
using System.Linq;
using Inventor;
using Newtonsoft.Json.Linq;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;

namespace Bimwright.Ipt.Shared.Handlers.Core;

/// <summary>
/// Assembly side of <c>list_topology</c>: direct occurrences, and their faces and edges as
/// assembly-context proxies carrying portable ids.
///
/// This is what makes constraining reachable without a person clicking in Inventor.
/// <c>create_constraint_safe</c> takes <c>face_proxy</c> ids, and before this the only source of one
/// was the user's own selection.
/// </summary>
internal static class AssemblyTopology
{
    public static JObject List(InventorCommandContext ctx, AssemblyDocument assembly, string kind,
        string? componentId, string? geometry, int limit, Func<bool>? expired)
    {
        var doc = (global::Inventor.Document)assembly;
        var def = assembly.ComponentDefinition;
        var occurrences = def.Occurrences.Cast<ComponentOccurrence>().ToArray();

        if (kind == "occurrence")
        {
            var listed = new JArray();
            int matched = 0;
            foreach (var occurrence in occurrences)
            {
                if (expired?.Invoke() == true) throw new TimeoutException("Deadline while listing occurrences.");
                matched++;
                if (listed.Count >= limit) continue;
                listed.Add(Describe(doc, occurrence));
            }
            return new JObject
            {
                ["kind"] = kind,
                ["document_type"] = "assembly",
                ["occurrence_count"] = occurrences.Length,
                ["matched"] = matched,
                ["returned"] = listed.Count,
                ["truncated"] = matched > listed.Count,
                ["items"] = listed,
            };
        }

        // Faces and edges belong to a component, so the scope is one occurrence unless the caller
        // deliberately asks for all of them.
        var scope = occurrences;
        if (!string.IsNullOrWhiteSpace(componentId))
        {
            var wanted = EntityReferences.ResolveOccurrence(doc, componentId!);
            if (!occurrences.Any(o => ReferenceEquals(o, wanted)))
                throw new ArgumentException("component_id is not a direct occurrence of the active assembly.");
            scope = new[] { wanted };
        }

        var items = new JArray();
        int found = 0;
        foreach (var occurrence in scope)
        {
            if (occurrence.Suppressed) continue;
            foreach (var body in Bodies(occurrence))
            {
                if (kind == "face")
                {
                    foreach (Face face in body.Faces)
                    {
                        if (expired?.Invoke() == true) throw new TimeoutException("Deadline while listing faces.");
                        string type = face.SurfaceType.ToString();
                        if (geometry != null && type.IndexOf(geometry, StringComparison.OrdinalIgnoreCase) < 0) continue;
                        found++;
                        if (items.Count >= limit) continue;
                        occurrence.CreateGeometryProxy(face, out var raw);
                        if (raw is not FaceProxy proxy) continue;
                        items.Add(DescribeFace(doc, occurrence, proxy, type));
                    }
                }
                else
                {
                    foreach (Edge edge in body.Edges)
                    {
                        if (expired?.Invoke() == true) throw new TimeoutException("Deadline while listing edges.");
                        string type = edge.GeometryType.ToString();
                        if (geometry != null && type.IndexOf(geometry, StringComparison.OrdinalIgnoreCase) < 0) continue;
                        found++;
                        if (items.Count >= limit) continue;
                        occurrence.CreateGeometryProxy(edge, out var raw);
                        if (raw is not EdgeProxy proxy) continue;
                        items.Add(DescribeEdge(doc, occurrence, proxy, type));
                    }
                }
            }
        }

        return new JObject
        {
            ["kind"] = kind,
            ["document_type"] = "assembly",
            ["occurrence_count"] = occurrences.Length,
            ["scoped_to_component"] = !string.IsNullOrWhiteSpace(componentId),
            ["matched"] = found,
            ["returned"] = items.Count,
            ["truncated"] = found > items.Count,
            ["items"] = items,
        };
    }

    private static SurfaceBody[] Bodies(ComponentOccurrence occurrence)
    {
        try { return occurrence.SurfaceBodies.Cast<SurfaceBody>().ToArray(); }
        catch { return Array.Empty<SurfaceBody>(); }
    }

    private static JObject Describe(global::Inventor.Document doc, ComponentOccurrence occurrence)
    {
        var item = new JObject
        {
            ["id"] = EntityReferences.Describe(doc, occurrence)["id"],
            ["name"] = occurrence.Name,
            ["grounded"] = Read(() => occurrence.Grounded),
            ["suppressed"] = Read(() => occurrence.Suppressed),
            ["adaptive"] = Read(() => occurrence.Adaptive),
            ["constraint_count"] = Read(() => occurrence.Constraints.Count),
            ["document_type"] = Read(() => occurrence.DefinitionDocumentType.ToString()),
            ["path"] = Read(() => occurrence.ReferencedDocumentDescriptor?.FullDocumentName),
        };
        try
        {
            var translation = occurrence.Transformation.Translation;
            item["position_mm"] = new JArray(UnitConvert.CmToMm(translation.X),
                UnitConvert.CmToMm(translation.Y), UnitConvert.CmToMm(translation.Z));
        }
        catch { item["position_mm"] = JValue.CreateNull(); }
        return item;
    }

    private static JObject DescribeFace(global::Inventor.Document doc, ComponentOccurrence occurrence,
        FaceProxy proxy, string type)
    {
        var item = new JObject
        {
            ["id"] = EntityReferences.Describe(doc, proxy)["id"],
            ["component_id"] = EntityReferences.Describe(doc, occurrence)["id"],
            ["component_name"] = occurrence.Name,
            ["surface_type"] = type,
            ["area_mm2"] = Read(() => UnitConvert.Cm2ToMm2(proxy.Evaluator.Area)),
            ["constrainable"] = type == SurfaceTypeEnum.kPlaneSurface.ToString(),
        };
        try
        {
            var point = proxy.PointOnFace;
            item["point_on_face_mm"] = new JArray(UnitConvert.CmToMm(point.X),
                UnitConvert.CmToMm(point.Y), UnitConvert.CmToMm(point.Z));
        }
        catch { item["point_on_face_mm"] = JValue.CreateNull(); }
        if (type == SurfaceTypeEnum.kPlaneSurface.ToString())
        {
            try
            {
                // Plane.Normal is the underlying surface normal; the face points the other way when its
                // parameterization is reversed, so both are reported and outward_normal is the usable one.
                var plane = (Plane)proxy.Geometry;
                bool reversed = proxy.IsParamReversed;
                double sign = reversed ? -1 : 1;
                item["normal"] = new JArray(plane.Normal.X, plane.Normal.Y, plane.Normal.Z);
                item["param_reversed"] = reversed;
                item["outward_normal"] = new JArray(plane.Normal.X * sign, plane.Normal.Y * sign, plane.Normal.Z * sign);
            }
            catch
            {
                item["normal"] = JValue.CreateNull();
                item["param_reversed"] = JValue.CreateNull();
                item["outward_normal"] = JValue.CreateNull();
            }
        }
        return item;
    }

    private static JObject DescribeEdge(global::Inventor.Document doc, ComponentOccurrence occurrence,
        EdgeProxy proxy, string type)
    {
        var item = new JObject
        {
            ["id"] = EntityReferences.Describe(doc, proxy)["id"],
            ["component_id"] = EntityReferences.Describe(doc, occurrence)["id"],
            ["component_name"] = occurrence.Name,
            ["geometry_type"] = type,
            ["length_mm"] = Read(() =>
            {
                var evaluator = proxy.Evaluator;
                evaluator.GetParamExtents(out double min, out double max);
                evaluator.GetLengthAtParam(min, max, out double length);
                return UnitConvert.CmToMm(length);
            }),
        };
        item["start_mm"] = Point(() => proxy.StartVertex?.Point);
        item["end_mm"] = Point(() => proxy.StopVertex?.Point);
        return item;
    }

    private static JToken Read<T>(Func<T> read)
    {
        try
        {
            var value = read();
            return value == null ? JValue.CreateNull() : JToken.FromObject(value);
        }
        catch { return JValue.CreateNull(); }
    }

    private static JToken Point(Func<Point?> read)
    {
        try
        {
            var point = read();
            return point == null ? JValue.CreateNull()
                : new JArray(UnitConvert.CmToMm(point.X), UnitConvert.CmToMm(point.Y), UnitConvert.CmToMm(point.Z));
        }
        catch { return JValue.CreateNull(); }
    }
}
#endif
