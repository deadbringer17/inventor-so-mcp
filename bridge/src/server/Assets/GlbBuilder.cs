using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Bimwright.Ipt.Shared.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Server.Assets;

/// <summary>
/// Minimal, dependency-free glTF 2.0 binary (GLB) writer for tessellated CAD definitions.
/// <list type="bullet">
/// <item>Units: glTF mandates metres. Inventor facets arrive in centimetres and are scaled here, so
/// accessor min/max are metres too.</item>
/// <item>One mesh per solid body, one triangle primitive per mesh; <c>primitive.extras.faces</c> maps
/// each face to its index range, so a client resolves a picked triangle to a face id locally.</item>
/// <item>Deterministic: the same geometry always yields the same bytes (no timestamps, no revision
/// tokens), so the SHA-256 content address changes only when the geometry does.</item>
/// </list>
/// </summary>
public static class GlbBuilder
{
    public const double CentimetresToMetres = 0.01;

    /// <summary>A mesh to write: the decoded bodies of one definition.</summary>
    public sealed class MeshSource
    {
        public string Name { get; set; } = "";
        public string? DocumentId { get; set; }
        public IReadOnlyList<MeshPayload.Body> Bodies { get; set; } = Array.Empty<MeshPayload.Body>();
    }

    /// <summary>A scene node: an instance of <see cref="MeshSource"/> (by index) with a transform.</summary>
    public sealed class Node
    {
        public string Name { get; set; } = "";
        public int? Mesh { get; set; }
        /// <summary>glTF column-major 4x4 in metres, or null for identity.</summary>
        public double[]? Matrix { get; set; }
        public List<int> Children { get; set; } = new();
        public JObject? Extras { get; set; }
    }

    /// <summary>GLB for one definition: one node per body at the origin.</summary>
    public static byte[] BuildDefinition(MeshSource source)
    {
        var nodes = new List<Node> { new() { Name = source.Name, Mesh = 0 } };
        return Build(new[] { source }, nodes, new[] { 0 }, source.DocumentId);
    }

    /// <summary>
    /// GLB for a scene: <paramref name="meshes"/> are written once each and instanced by
    /// <paramref name="nodes"/>; <paramref name="roots"/> are the scene's root node indices.
    /// </summary>
    public static byte[] Build(IReadOnlyList<MeshSource> meshes, IReadOnlyList<Node> nodes, IReadOnlyList<int> roots, string? documentId)
    {
        var bin = new MemoryStream();
        var bufferViews = new JArray();
        var accessors = new JArray();
        var gltfMeshes = new JArray();

        int AddView(byte[] data, int? target)
        {
            Pad(bin, 0);
            var view = new JObject { ["buffer"] = 0, ["byteOffset"] = bin.Length, ["byteLength"] = data.Length };
            if (target != null) view["target"] = target;
            bin.Write(data, 0, data.Length);
            bufferViews.Add(view);
            return bufferViews.Count - 1;
        }

        foreach (var mesh in meshes)
        {
            // A multi-body definition is one glTF mesh with one primitive per body, so a node
            // instancing it carries every body - and body visibility survives as primitive extras.
            var primitives = new JArray();
            foreach (var body in mesh.Bodies)
            {
                if (body.Indices.Length == 0) continue;
                var positions = new float[body.Positions.Length];
                for (int i = 0; i < positions.Length; i++) positions[i] = (float)(body.Positions[i] * CentimetresToMetres);
                var (min, max) = Bounds(positions);
                int positionAccessor = accessors.Count;
                accessors.Add(new JObject
                {
                    ["bufferView"] = AddView(Bytes(positions), 34962), ["componentType"] = 5126,
                    ["count"] = positions.Length / 3, ["type"] = "VEC3",
                    ["min"] = new JArray(min.Cast<object>().ToArray()), ["max"] = new JArray(max.Cast<object>().ToArray()),
                });
                var attributes = new JObject { ["POSITION"] = positionAccessor };
                if (body.Normals.Length == body.Positions.Length)
                {
                    attributes["NORMAL"] = accessors.Count;
                    accessors.Add(new JObject
                    {
                        ["bufferView"] = AddView(Bytes(Normalized(body.Normals)), 34962), ["componentType"] = 5126,
                        ["count"] = body.Normals.Length / 3, ["type"] = "VEC3",
                    });
                }
                bool coloured = body.Faces.Any(f => MeshPayload.NormalizeColor(f.Color) != null);
                if (coloured)
                {
                    attributes["COLOR_0"] = accessors.Count;
                    accessors.Add(new JObject
                    {
                        ["bufferView"] = AddView(Bytes(VertexColors(body)), 34962), ["componentType"] = 5126,
                        ["count"] = body.Positions.Length / 3, ["type"] = "VEC4",
                    });
                }
                int indexAccessor = accessors.Count;
                accessors.Add(new JObject
                {
                    ["bufferView"] = AddView(Bytes(body.Indices), 34963), ["componentType"] = 5125,
                    ["count"] = body.Indices.Length, ["type"] = "SCALAR",
                });
                primitives.Add(new JObject
                {
                    ["attributes"] = attributes, ["indices"] = indexAccessor, ["mode"] = 4, ["material"] = coloured ? 1 : 0,
                    ["extras"] = new JObject
                    {
                        ["body_index"] = body.Index, ["body_name"] = body.Name, ["visible"] = body.Visible,
                        ["faces"] = new JArray(body.Faces.Select(f => new JObject
                        {
                            ["face_id"] = f.FaceId, ["ordinal"] = f.Ordinal,
                            ["first_index"] = f.FirstIndex, ["index_count"] = f.IndexCount,
                        })),
                    },
                });
            }
            var gltfMesh = new JObject { ["name"] = mesh.Name, ["primitives"] = primitives };
            if (mesh.DocumentId != null) gltfMesh["extras"] = new JObject { ["document_id"] = mesh.DocumentId };
            gltfMeshes.Add(gltfMesh);
        }

        // A glTF mesh must have at least one primitive; an empty definition (no solid bodies) is
        // dropped and every node that referenced it becomes a plain transform node.
        var meshMap = new int?[gltfMeshes.Count];
        var keptMeshes = new JArray();
        for (int i = 0; i < gltfMeshes.Count; i++)
            if (((JArray)gltfMeshes[i]["primitives"]!).Count > 0) { meshMap[i] = keptMeshes.Count; keptMeshes.Add(gltfMeshes[i]); }

        var gltfNodes = new JArray(nodes.Select(n =>
        {
            var node = new JObject { ["name"] = n.Name };
            if (n.Mesh is { } m && m >= 0 && m < meshMap.Length && meshMap[m] is { } kept) node["mesh"] = kept;
            if (n.Matrix != null && !IsIdentity(n.Matrix)) node["matrix"] = new JArray(n.Matrix.Cast<object>().ToArray());
            if (n.Children.Count > 0) node["children"] = new JArray(n.Children.Cast<object>().ToArray());
            if (n.Extras != null) node["extras"] = n.Extras;
            return node;
        }));

        Pad(bin, 0);
        var assetExtras = new JObject { ["units_source"] = "cm", ["units"] = "m", ["up_axis"] = "Y (Inventor Y unchanged)" };
        if (documentId != null) assetExtras["document_id"] = documentId;
        var gltf = new JObject
        {
            ["asset"] = new JObject { ["version"] = "2.0", ["generator"] = "inventor-so-mcp", ["extras"] = assetExtras },
            ["scene"] = 0,
            ["scenes"] = new JArray(new JObject { ["nodes"] = new JArray(roots.Cast<object>().ToArray()) }),
            ["nodes"] = gltfNodes,
            // Material 1 serves primitives with COLOR_0: glTF multiplies the vertex colour into
            // baseColorFactor, and the default grey is already baked into the vertex colours.
            ["materials"] = new JArray(
                new JObject
                {
                    ["name"] = "cad_default",
                    ["pbrMetallicRoughness"] = new JObject
                    {
                        ["baseColorFactor"] = new JArray(0.72, 0.74, 0.77, 1.0), ["metallicFactor"] = 0.1, ["roughnessFactor"] = 0.6,
                    },
                },
                new JObject
                {
                    ["name"] = "cad_vertex_color",
                    ["pbrMetallicRoughness"] = new JObject
                    {
                        ["baseColorFactor"] = new JArray(1.0, 1.0, 1.0, 1.0), ["metallicFactor"] = 0.1, ["roughnessFactor"] = 0.6,
                    },
                }),
        };
        if (keptMeshes.Count > 0)
        {
            gltf["meshes"] = keptMeshes;
            gltf["accessors"] = accessors;
            gltf["bufferViews"] = bufferViews;
            gltf["buffers"] = new JArray(new JObject { ["byteLength"] = bin.Length });
        }
        return Pack(gltf, bin.ToArray());
    }

    /// <summary>Assemble header + JSON chunk (space padded) + BIN chunk (zero padded).</summary>
    private static byte[] Pack(JObject gltf, byte[] bin)
    {
        var json = Encoding.UTF8.GetBytes(gltf.ToString(Formatting.None));
        int jsonPadded = (json.Length + 3) & ~3;
        int binPadded = (bin.Length + 3) & ~3;
        bool hasBin = bin.Length > 0;
        int total = 12 + 8 + jsonPadded + (hasBin ? 8 + binPadded : 0);
        var output = new MemoryStream(total);
        using (var writer = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(0x46546C67u); // "glTF"
            writer.Write(2u);
            writer.Write((uint)total);
            writer.Write((uint)jsonPadded);
            writer.Write(0x4E4F534Au); // "JSON"
            writer.Write(json);
            for (int i = json.Length; i < jsonPadded; i++) writer.Write((byte)0x20);
            if (hasBin)
            {
                writer.Write((uint)binPadded);
                writer.Write(0x004E4942u); // "BIN\0"
                writer.Write(bin);
                for (int i = bin.Length; i < binPadded; i++) writer.Write((byte)0);
            }
        }
        return output.ToArray();
    }

    private static void Pad(MemoryStream stream, byte value)
    {
        while (stream.Length % 4 != 0) stream.WriteByte(value);
    }

    private static byte[] Bytes(float[] values)
    {
        var bytes = new byte[values.Length * 4];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static byte[] Bytes(uint[] values)
    {
        var bytes = new byte[values.Length * 4];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static float SrgbToLinear(double c) =>
        (float)(c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4));

    /// <summary>
    /// Per-vertex linear RGBA. Faces are tessellated separately, so each vertex belongs to the
    /// face range whose indices reference it; uncoloured or unreferenced vertices get the default grey.
    /// </summary>
    private static float[] VertexColors(MeshPayload.Body body)
    {
        int vertexCount = body.Positions.Length / 3;
        var result = new float[vertexCount * 4];
        var defaultLinear = new[]
        {
            SrgbToLinear(0.72), SrgbToLinear(0.74), SrgbToLinear(0.77),
        };
        for (int v = 0; v < vertexCount; v++)
        {
            result[v * 4] = defaultLinear[0]; result[v * 4 + 1] = defaultLinear[1];
            result[v * 4 + 2] = defaultLinear[2]; result[v * 4 + 3] = 1f;
        }
        foreach (var face in body.Faces)
        {
            var hex = MeshPayload.NormalizeColor(face.Color);
            if (hex == null) continue;
            float r = SrgbToLinear(Convert.ToInt32(hex.Substring(1, 2), 16) / 255.0);
            float g = SrgbToLinear(Convert.ToInt32(hex.Substring(3, 2), 16) / 255.0);
            float b = SrgbToLinear(Convert.ToInt32(hex.Substring(5, 2), 16) / 255.0);
            int end = Math.Min(face.FirstIndex + face.IndexCount, body.Indices.Length);
            for (int i = Math.Max(0, face.FirstIndex); i < end; i++)
            {
                int v = (int)body.Indices[i];
                if (v >= vertexCount) continue;
                result[v * 4] = r; result[v * 4 + 1] = g; result[v * 4 + 2] = b;
            }
        }
        return result;
    }

    /// <summary>glTF requires unit-length normals; CAD facet normals are usually unit but not guaranteed.</summary>
    private static float[] Normalized(float[] normals)
    {
        var result = new float[normals.Length];
        for (int i = 0; i < normals.Length; i += 3)
        {
            double x = normals[i], y = normals[i + 1], z = normals[i + 2];
            double length = Math.Sqrt(x * x + y * y + z * z);
            if (length < 1e-12 || double.IsNaN(length)) { result[i + 2] = 1; continue; }
            result[i] = (float)(x / length); result[i + 1] = (float)(y / length); result[i + 2] = (float)(z / length);
        }
        return result;
    }

    private static (double[] min, double[] max) Bounds(float[] positions)
    {
        var min = new[] { double.MaxValue, double.MaxValue, double.MaxValue };
        var max = new[] { double.MinValue, double.MinValue, double.MinValue };
        for (int i = 0; i < positions.Length; i += 3)
            for (int k = 0; k < 3; k++)
            {
                if (positions[i + k] < min[k]) min[k] = positions[i + k];
                if (positions[i + k] > max[k]) max[k] = positions[i + k];
            }
        return (min, max);
    }

    private static bool IsIdentity(double[] m)
    {
        for (int i = 0; i < 16; i++)
            if (Math.Abs(m[i] - ((i % 5 == 0) ? 1 : 0)) > 1e-12) return false;
        return true;
    }
}

/// <summary>Transform conversion between Inventor matrices and glTF.</summary>
public static class SceneMath
{
    /// <summary>
    /// Inventor's <c>Matrix.GetMatrixData</c> is row-major with the translation in the last column
    /// (indices 3, 7, 11) in centimetres. glTF node matrices are column-major with the translation in
    /// indices 12-14, in metres. Transpose and scale the translation.
    /// </summary>
    public static double[] RowMajorCmToGltf(IReadOnlyList<double> rowMajorCm)
    {
        if (rowMajorCm == null || rowMajorCm.Count != 16) throw new ArgumentException("A 4x4 matrix has 16 values.");
        var result = new double[16];
        for (int row = 0; row < 4; row++)
            for (int column = 0; column < 4; column++)
                result[column * 4 + row] = rowMajorCm[row * 4 + column];
        for (int i = 12; i < 15; i++) result[i] *= GlbBuilder.CentimetresToMetres;
        return result;
    }

    /// <summary>Same matrix, row-major, translation in millimetres - the CAD-facing JSON form.</summary>
    public static double[] RowMajorCmToMm(IReadOnlyList<double> rowMajorCm)
    {
        if (rowMajorCm == null || rowMajorCm.Count != 16) throw new ArgumentException("A 4x4 matrix has 16 values.");
        var result = rowMajorCm.ToArray();
        result[3] *= 10; result[7] *= 10; result[11] *= 10;
        return result;
    }
}
