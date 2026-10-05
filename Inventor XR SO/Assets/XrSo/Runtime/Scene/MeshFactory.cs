using System;
using System.Collections.Generic;
using InventorXrSo.Core.Glb;
using UnityEngine;
using UnityEngine.Rendering;

namespace InventorXrSo.Unity.Scene
{
    public static class MeshFactory
    {
        /// <summary>One Unity mesh per body: X mirrored, winding reversed, triangle order kept (face ranges stay valid).</summary>
        public static Mesh Build(GlbPrimitive primitive)
        {
            var positions = Handedness.FlipX(primitive.Positions);
            var normals = Handedness.FlipX(primitive.Normals);
            var indices = Handedness.ReverseWinding(primitive.Indices);
            var mesh = new Mesh { name = primitive.BodyName, indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(ToVectors(positions));
            if (normals.Length == positions.Length) mesh.SetNormals(ToVectors(normals));
            var ints = new int[indices.Length];
            for (int i = 0; i < ints.Length; i++) ints[i] = (int)indices[i];
            mesh.SetIndices(ints, MeshTopology.Triangles, 0, true);
            if (normals.Length != positions.Length) mesh.RecalculateNormals();
            mesh.SetColors(VertexColors(primitive.Colors, mesh.vertexCount));
            return mesh;
        }

        /// <summary>The CadBody material reads vertex colour, so every mesh carries one: the file's COLOR_0 (linear) or the default grey.</summary>
        public static readonly Color DefaultColor = new Color(0.72f, 0.74f, 0.77f, 1f);

        public static Color[] VertexColors(float[] rgba, int vertexCount)
        {
            bool linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
            var colors = new Color[vertexCount];
            if (rgba != null && rgba.Length == vertexCount * 4)
            {
                for (int i = 0; i < colors.Length; i++)
                {
                    // glTF COLOR_0 is linear; in Gamma colour space the shader output is not converted, so go to gamma.
                    var c = new Color(rgba[i * 4], rgba[i * 4 + 1], rgba[i * 4 + 2], rgba[i * 4 + 3]);
                    colors[i] = linear ? c : c.gamma;
                }
                return colors;
            }
            // Same look the CadBody material had: _BaseColor is authored in gamma and uploaded as linear.
            var fallback = linear ? DefaultColor.linear : DefaultColor;
            for (int i = 0; i < colors.Length; i++) colors[i] = fallback;
            return colors;
        }

        /// <summary>The triangles of one face over the same vertices, for the highlight overlay.</summary>
        public static Mesh BuildFaceOverlay(Mesh source, FaceRange range)
        {
            var all = source.GetIndices(0);
            var slice = new int[range.IndexCount];
            Array.Copy(all, range.FirstIndex, slice, 0, range.IndexCount);
            var overlay = new Mesh { name = source.name + " face", indexFormat = source.indexFormat };
            overlay.SetVertices(source.vertices);
            overlay.SetNormals(source.normals);
            overlay.SetIndices(slice, MeshTopology.Triangles, 0, true);
            return overlay;
        }

        /// <summary>
        /// Boundary of one face as polylines over the mesh's local positions: the edges used by exactly one triangle of the face,
        /// vertices welded by position (a CAD mesh duplicates vertices along sharp edges). Closed loops repeat their first point.
        /// <paramref name="normal"/> is the area-weighted normal of the face (zero if degenerate), to lift the line off the surface.
        /// </summary>
        public static List<Vector3[]> FaceBoundary(Mesh source, FaceRange range, out Vector3 normal)
        {
            var positions = source.vertices;
            var all = source.GetIndices(0);
            var weld = new Dictionary<Vector3Int, int>();
            var canonical = new List<Vector3>();
            int Weld(int vertex)
            {
                var p = positions[vertex];
                var key = new Vector3Int(Mathf.RoundToInt(p.x * 100000f), Mathf.RoundToInt(p.y * 100000f), Mathf.RoundToInt(p.z * 100000f));
                if (weld.TryGetValue(key, out var id)) return id;
                id = canonical.Count; weld[key] = id; canonical.Add(p);
                return id;
            }
            var uses = new Dictionary<long, int>();
            long Key(int a, int b) => a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
            var sum = Vector3.zero;
            for (int t = range.FirstIndex; t + 2 < range.FirstIndex + range.IndexCount; t += 3)
            {
                int a = Weld(all[t]), b = Weld(all[t + 1]), c = Weld(all[t + 2]);
                sum += Vector3.Cross(canonical[b] - canonical[a], canonical[c] - canonical[a]);
                foreach (var key in new[] { Key(a, b), Key(b, c), Key(c, a) })
                    uses[key] = uses.TryGetValue(key, out var n) ? n + 1 : 1;
            }
            normal = sum.sqrMagnitude > 1e-18f ? sum.normalized : Vector3.zero;
            var next = new Dictionary<int, List<int>>();
            foreach (var pair in uses)
            {
                if (pair.Value != 1) continue;
                int a = (int)(pair.Key >> 32), b = (int)(pair.Key & 0xffffffff);
                if (a == b) continue;
                if (!next.TryGetValue(a, out var la)) next[a] = la = new List<int>();
                if (!next.TryGetValue(b, out var lb)) next[b] = lb = new List<int>();
                la.Add(b); lb.Add(a);
            }
            var chains = new List<Vector3[]>();
            var used = new HashSet<long>();
            foreach (var start in new List<int>(next.Keys))
            {
                while (true)
                {
                    int first = -1;
                    foreach (var n in next[start]) if (!used.Contains(Key(start, n))) { first = n; break; }
                    if (first < 0) break;
                    var chain = new List<int> { start };
                    int current = first;
                    used.Add(Key(start, first));
                    while (true)
                    {
                        chain.Add(current);
                        if (current == start) break;
                        int step = -1;
                        foreach (var n in next[current]) if (!used.Contains(Key(current, n))) { step = n; break; }
                        if (step < 0) break;
                        used.Add(Key(current, step));
                        current = step;
                    }
                    var points = new Vector3[chain.Count];
                    for (int i = 0; i < points.Length; i++) points[i] = canonical[chain[i]];
                    chains.Add(points);
                }
            }
            return chains;
        }

        private static Vector3[] ToVectors(float[] xyz)
        {
            var vectors = new Vector3[xyz.Length / 3];
            for (int i = 0; i < vectors.Length; i++) vectors[i] = new Vector3(xyz[i * 3], xyz[i * 3 + 1], xyz[i * 3 + 2]);
            return vectors;
        }
    }
}
