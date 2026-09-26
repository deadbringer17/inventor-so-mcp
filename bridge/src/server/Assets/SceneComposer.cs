using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Server.Assets;

/// <summary>
/// Turns the add-in's scene graph into client-ready form and, given the meshes of its definitions,
/// into one instanced GLB. The add-in reports every occurrence transform in the top-level assembly
/// space (<c>matrix_space: "assembly"</c>), so group nodes are written as identity and leaves carry
/// their full transform: the composed scene is right regardless of nesting depth.
/// </summary>
public static class SceneComposer
{
    /// <summary>
    /// Add <c>matrix_mm</c> (row-major, mm) and <c>matrix_gltf</c> (column-major, m) next to each
    /// node's <c>matrix_rowmajor_cm</c>, in place. Returns the definition document ids of the
    /// visible, unsuppressed leaf occurrences, each once, in first-seen order.
    /// </summary>
    public static IReadOnlyList<string> Annotate(JObject scene)
    {
        var definitions = new List<string>();
        void Visit(JObject node, bool hidden)
        {
            bool suppressed = (bool?)node["suppressed"] ?? false;
            bool visible = (bool?)node["visible"] ?? true;
            bool skip = hidden || suppressed || !visible;
            if (node["matrix_rowmajor_cm"] is JArray m && m.Count == 16)
            {
                var values = m.Select(v => (double)v).ToArray();
                node["matrix_mm"] = new JArray(SceneMath.RowMajorCmToMm(values).Cast<object>().ToArray());
                node["matrix_gltf"] = new JArray(SceneMath.RowMajorCmToGltf(values).Cast<object>().ToArray());
            }
            var children = node["children"] as JArray;
            if ((children == null || children.Count == 0) && !skip && (string?)node["definition_document_id"] is { } def &&
                (string?)node["definition_kind"] != "assembly" && !definitions.Contains(def))
                definitions.Add(def);
            if (children != null)
                foreach (var child in children.OfType<JObject>()) Visit(child, skip);
        }
        if (scene["root"] is JObject root) Visit(root, false);
        return definitions;
    }

    /// <summary>
    /// Compose one GLB for the scene. <paramref name="meshes"/> maps a definition document id to its
    /// decoded mesh; leaves whose definition has no mesh become empty transform nodes. Hidden and
    /// suppressed occurrences are kept as nodes (with <c>extras.visible=false</c>) but get no mesh, so
    /// the client's node tree matches the CAD tree.
    /// </summary>
    public static byte[] Compose(JObject scene, IReadOnlyDictionary<string, GlbBuilder.MeshSource> meshes, string? documentId)
    {
        var sources = new List<GlbBuilder.MeshSource>();
        var meshIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var pair in meshes)
        {
            meshIndex[pair.Key] = sources.Count;
            sources.Add(pair.Value);
        }
        var nodes = new List<GlbBuilder.Node>();
        int Visit(JObject node, bool hidden)
        {
            bool suppressed = (bool?)node["suppressed"] ?? false;
            bool visible = (bool?)node["visible"] ?? true;
            bool skip = hidden || suppressed || !visible;
            var gltfNode = new GlbBuilder.Node
            {
                Name = (string?)node["name"] ?? "",
                Extras = new JObject
                {
                    ["occurrence_id"] = node["occurrence_id"]?.DeepClone(),
                    ["definition_document_id"] = node["definition_document_id"]?.DeepClone(),
                    ["visible"] = !skip,
                    ["suppressed"] = suppressed,
                },
            };
            int index = nodes.Count;
            nodes.Add(gltfNode);
            var children = node["children"] as JArray;
            if (children == null || children.Count == 0)
            {
                if (node["matrix_gltf"] is JArray m && m.Count == 16) gltfNode.Matrix = m.Select(v => (double)v).ToArray();
                if (!skip && (string?)node["definition_document_id"] is { } def && meshIndex.TryGetValue(def, out var mi))
                    gltfNode.Mesh = mi;
            }
            else
            {
                foreach (var child in children.OfType<JObject>()) gltfNode.Children.Add(Visit(child, skip));
            }
            return index;
        }
        var roots = new List<int>();
        if (scene["root"] is JObject root)
        {
            // A part's scene graph is a single root leaf with its own definition and no matrix.
            roots.Add(Visit(root, false));
        }
        return GlbBuilder.Build(sources, nodes, roots, documentId);
    }
}
