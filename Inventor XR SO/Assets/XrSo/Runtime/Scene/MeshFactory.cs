using System;
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
            return mesh;
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

        private static Vector3[] ToVectors(float[] xyz)
        {
            var vectors = new Vector3[xyz.Length / 3];
            for (int i = 0; i < vectors.Length; i++) vectors[i] = new Vector3(xyz[i * 3], xyz[i * 3 + 1], xyz[i * 3 + 2]);
            return vectors;
        }
    }
}
