#if INVENTOR2027 && SO_EXPERIMENTAL
using System;
using System.Collections.Generic;
using System.Linq;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Handlers.Core;
using Bimwright.Ipt.Shared.Infrastructure;
using Inventor;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Handlers.Experimental;

internal static class InsightX
{
    public static string KindOf(Parameter parameter) => parameter switch
    {
        UserParameter => "user",
        ModelParameter => "model",
        ReferenceParameter => "reference",
        _ => parameter.ParameterType.ToString(),
    };

    /// <summary>Name and object type of anything Inventor lists as a dependent.</summary>
    public static JObject Describe(object item)
    {
        var result = new JObject();
        if (item is Parameter parameter)
        {
            result["name"] = parameter.Name;
            result["kind"] = KindOf(parameter);
            return result;
        }
        try { result["name"] = (string)((dynamic)item).Name; } catch { result["name"] = null; }
        try { result["kind"] = ((ObjectTypeEnum)(int)((dynamic)item).Type).ToString(); } catch { result["kind"] = item.GetType().Name; }
        return result;
    }

    public static IEnumerable<PartFeature> Features(global::Inventor.Document doc)
    {
        if (doc is PartDocument part)
            foreach (PartFeature feature in part.ComponentDefinition.Features) yield return feature;
    }

    /// <summary>Parameters a feature owns, when Inventor exposes them.</summary>
    public static IEnumerable<Parameter> ParametersOf(PartFeature feature)
    {
        var list = new List<Parameter>();
        try { foreach (Parameter parameter in ((dynamic)feature).Parameters) list.Add(parameter); } catch { }
        return list;
    }
}

/// <summary>
/// get_dependencies: one parameter or feature, what drives it and what it drives, followed to a
/// bounded depth. With trace=true every reached node is listed with the path that reached it.
/// </summary>
public sealed class GetDependenciesHandler : ExperimentalHandler
{
    private const int MaxNodes = 2000;
    public override string Name => "get_dependencies";
    public override bool IsReadOnly => true;

    protected override JToken Run(InventorCommandContext ctx, Application app, JObject p)
    {
        var doc = X.Active(app);
        string name = X.Str(p, "name");
        int depth = p["depth"]?.Type == JTokenType.Integer ? Math.Max(1, Math.Min(8, (int)p["depth"]!)) : 1;
        var parameters = X.ParametersOf(doc);
        var root = X.FindParameter(parameters, name);
        if (root == null)
        {
            var feature = InsightX.Features(doc).FirstOrDefault(f => f.Name == name)
                ?? throw new ArgumentException("No parameter or feature named '" + name + "'.");
            return new JObject
            {
                ["name"] = feature.Name,
                ["kind"] = "feature",
                ["feature_type"] = feature.Type.ToString(),
                ["health"] = feature.HealthStatus.ToString(),
                ["suppressed"] = feature.Suppressed,
                ["driven_by"] = new JArray(InsightX.ParametersOf(feature).Select(q => new JObject
                    { ["name"] = q.Name, ["kind"] = InsightX.KindOf(q), ["expression"] = q.Expression })),
                ["dependents"] = new JArray(),
            };
        }

        // Which features own a model parameter: the reverse map, built once.
        var owners = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var feature in InsightX.Features(doc))
            foreach (var owned in InsightX.ParametersOf(feature)) owners[owned.Name] = feature.Name;

        var nodes = new JArray();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<(Parameter parameter, int level, string path)>();
        queue.Enqueue((root, 0, root.Name));
        while (queue.Count > 0 && nodes.Count < MaxNodes)
        {
            X.Deadline(ctx, "while tracing dependencies");
            var (parameter, level, path) = queue.Dequeue();
            if (!seen.Add(parameter.Name)) continue;
            var dependents = new JArray();
            foreach (object dependent in parameter.Dependents)
            {
                var described = InsightX.Describe(dependent);
                dependents.Add(described);
                if (dependent is Parameter next && level + 1 < depth) queue.Enqueue((next, level + 1, path + " > " + next.Name));
            }
            var drivenBy = new JArray();
            foreach (Parameter source in parameter.DrivenBy) drivenBy.Add(new JObject { ["name"] = source.Name, ["kind"] = InsightX.KindOf(source) });
            var node = new JObject
            {
                ["name"] = parameter.Name,
                ["kind"] = InsightX.KindOf(parameter),
                ["expression"] = parameter.Expression,
                ["unit"] = parameter.get_Units(),
                ["value"] = JToken.FromObject(parameter.Value),
                ["owner_feature"] = owners.TryGetValue(parameter.Name, out var owner) ? owner : null,
                ["driven_by"] = drivenBy,
                ["dependents"] = dependents,
                ["level"] = level,
            };
            if (X.Bool(p, "trace", false)) node["path"] = path;
            nodes.Add(node);
        }
        var affected = nodes.OfType<JObject>().Select(n => (string?)n["owner_feature"]).Where(f => f != null).Distinct().ToArray();
        return new JObject
        {
            ["name"] = root.Name,
            ["depth"] = depth,
            ["nodes"] = nodes,
            ["affected_features"] = new JArray(affected.Cast<object>().ToArray()),
            ["truncated"] = queue.Count > 0,
        };
    }
}

/// <summary>get_semantic_state: typed nodes and relations of the active part or assembly (plan §17).</summary>
public sealed class GetSemanticStateHandler : ExperimentalHandler
{
    public override string Name => "get_semantic_state";
    public override bool IsReadOnly => true;

    protected override JToken Run(InventorCommandContext ctx, Application app, JObject p)
    {
        var doc = X.Active(app);
        int max = p["max_nodes"]?.Type == JTokenType.Integer ? Math.Max(1, Math.Min(20000, (int)p["max_nodes"]!)) : 2000;
        string docId = EntityReferences.DocumentId(doc);
        string? revision = ctx.Events?.Revision(docId);
        var nodes = new JArray();
        var relations = new JArray();
        bool truncated = false;
        bool Add(JObject node)
        {
            X.Deadline(ctx, "while building the semantic state");
            if (nodes.Count >= max) { truncated = true; return false; }
            node["document_id"] = docId;
            node["revision"] = revision;
            nodes.Add(node);
            return true;
        }
        void Relate(string from, string type, string to) => relations.Add(new JObject { ["from"] = from, ["type"] = type, ["to"] = to });

        foreach (Parameter parameter in X.ParametersOf(doc))
        {
            if (!Add(new JObject { ["id"] = "param:" + parameter.Name, ["type"] = "parameter", ["name"] = parameter.Name,
                    ["metadata"] = new JObject { ["kind"] = InsightX.KindOf(parameter), ["expression"] = parameter.Expression, ["unit"] = parameter.get_Units() } })) break;
            foreach (object dependent in parameter.Dependents)
                if (dependent is Parameter next) Relate("param:" + parameter.Name, "drives", "param:" + next.Name);
        }

        if (doc is PartDocument part)
        {
            var def = part.ComponentDefinition;
            foreach (PartFeature feature in def.Features)
            {
                if (!Add(new JObject { ["id"] = "feature:" + feature.Name, ["type"] = "feature", ["name"] = feature.Name,
                        ["metadata"] = new JObject { ["feature_type"] = feature.Type.ToString(), ["health"] = feature.HealthStatus.ToString(), ["suppressed"] = feature.Suppressed } })) break;
                foreach (var owned in InsightX.ParametersOf(feature)) Relate("param:" + owned.Name, "drives", "feature:" + feature.Name);
            }
            foreach (PlanarSketch sketch in def.Sketches)
                if (!Add(new JObject { ["id"] = "sketch:" + sketch.Name, ["type"] = "sketch", ["name"] = sketch.Name,
                        ["metadata"] = new JObject { ["entities"] = sketch.SketchEntities.Count, ["dimensions"] = sketch.DimensionConstraints.Count } })) break;
            int bodyIndex = 0;
            foreach (SurfaceBody body in def.SurfaceBodies)
            {
                bodyIndex++;
                if (!Add(new JObject { ["id"] = "body:" + bodyIndex, ["type"] = "body", ["name"] = body.Name,
                        ["metadata"] = new JObject { ["faces"] = body.Faces.Count, ["visible"] = body.Visible } })) break;
            }
            foreach (WorkPlane plane in def.WorkPlanes)
                if (!plane.IsCoordinateSystemElement && !Add(new JObject { ["id"] = "work:" + plane.Name, ["type"] = "work_plane", ["name"] = plane.Name })) break;
            foreach (WorkAxis axis in def.WorkAxes)
                if (!axis.IsCoordinateSystemElement && !Add(new JObject { ["id"] = "work:" + axis.Name, ["type"] = "work_axis", ["name"] = axis.Name })) break;
            foreach (WorkPoint point in def.WorkPoints)
                if (!point.IsCoordinateSystemElement && !Add(new JObject { ["id"] = "work:" + point.Name, ["type"] = "work_point", ["name"] = point.Name })) break;
        }
        else if (doc is AssemblyDocument assembly)
        {
            var def = assembly.ComponentDefinition;
            foreach (ComponentOccurrence occurrence in def.Occurrences)
            {
                string id = X.Describe((global::Inventor.Document)assembly, occurrence) ?? "occurrence:" + occurrence.Name;
                if (!Add(new JObject { ["id"] = id, ["type"] = "occurrence", ["name"] = occurrence.Name,
                        ["metadata"] = new JObject { ["suppressed"] = occurrence.Suppressed, ["grounded"] = occurrence.Grounded, ["visible"] = occurrence.Visible } })) break;
                if (!occurrence.Suppressed && occurrence.Definition.Document is global::Inventor.Document definition)
                    Relate(id, "instance_of", EntityReferences.DocumentId(definition));
            }
            foreach (AssemblyConstraint constraint in def.Constraints)
            {
                string id = X.Describe((global::Inventor.Document)assembly, constraint) ?? "constraint:" + constraint.Name;
                if (!Add(new JObject { ["id"] = id, ["type"] = "constraint", ["name"] = constraint.Name,
                        ["metadata"] = new JObject { ["constraint_type"] = constraint.Type.ToString(), ["health"] = constraint.HealthStatus.ToString(), ["suppressed"] = constraint.Suppressed } })) break;
                foreach (var occurrence in new[] { constraint.OccurrenceOne, constraint.OccurrenceTwo })
                    if (occurrence != null && X.Describe((global::Inventor.Document)assembly, occurrence) is { } target) Relate(id, "constrains", target);
            }
        }
        else throw new CodedFailureException(InventorErrorCodes.WRONG_DOCUMENT_TYPE, "The semantic state covers parts and assemblies.");

        return new JObject
        {
            ["document_id"] = docId,
            ["kind"] = X.Kind(doc),
            ["revision"] = revision,
            ["node_count"] = nodes.Count,
            ["truncated"] = truncated,
            ["nodes"] = nodes,
            ["relations"] = relations,
        };
    }
}
#endif
