using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace InventorXrSo.Core.Glb
{
    public sealed class GlbPrimitive
    {
        public GlbPrimitive(int bodyIndex, string bodyName, bool visible, float[] positions, float[] normals, uint[] indices, IReadOnlyList<FaceRange> faces)
        {
            BodyIndex = bodyIndex;
            BodyName = bodyName;
            Visible = visible;
            Positions = positions;
            Normals = normals;
            Indices = indices;
            Faces = faces;
            FaceMap = new FaceMap(faces, indices.Length);
        }

        public int BodyIndex { get; }
        public string BodyName { get; }
        public bool Visible { get; }
        /// <summary>xyz triples, metres, glTF (right-handed, Y up).</summary>
        public float[] Positions { get; }
        public float[] Normals { get; }
        public uint[] Indices { get; }
        public IReadOnlyList<FaceRange> Faces { get; }
        public FaceMap FaceMap { get; }
        public int TriangleCount => Indices.Length / 3;
    }

    /// <summary>
    /// Reader for the GLBs Inventor SO publishes (GlbBuilder): triangle primitives with float POSITION /
    /// NORMAL and integer indices, face table in primitive extras. Anything else is refused, not guessed.
    /// </summary>
    public sealed class GlbModel
    {
        private const uint Magic = 0x46546C67, JsonChunk = 0x4E4F534A, BinChunk = 0x004E4942;

        public GlbModel(string documentId, IReadOnlyList<GlbPrimitive> primitives)
        {
            DocumentId = documentId;
            Primitives = primitives;
        }

        public string DocumentId { get; }
        public IReadOnlyList<GlbPrimitive> Primitives { get; }

        public static GlbModel Parse(byte[] glb)
        {
            if (glb == null || glb.Length < 20 || U32(glb, 0) != Magic) throw new FormatException("Not a GLB file.");
            if (U32(glb, 4) != 2) throw new FormatException("Only glTF 2.0 GLB files are supported.");
            if (U32(glb, 8) != glb.Length) throw new FormatException("GLB length does not match its header.");
            int jsonLength = (int)U32(glb, 12);
            if (U32(glb, 16) != JsonChunk || 20 + jsonLength > glb.Length) throw new FormatException("GLB JSON chunk missing.");
            JObject gltf;
            try { gltf = JObject.Parse(Encoding.UTF8.GetString(glb, 20, jsonLength)); }
            catch (JsonReaderException ex) { throw new FormatException("GLB JSON chunk unreadable.", ex); }

            int binStart = 20 + jsonLength, binOffset = 0, binLength = 0;
            if (binStart + 8 <= glb.Length)
            {
                binLength = (int)U32(glb, binStart);
                if (U32(glb, binStart + 4) != BinChunk || binStart + 8 + binLength > glb.Length) throw new FormatException("GLB BIN chunk corrupt.");
                binOffset = binStart + 8;
            }
            var bin = new Bin(glb, binOffset, binLength);

            var primitives = new List<GlbPrimitive>();
            foreach (var mesh in gltf["meshes"] as JArray ?? new JArray())
            foreach (var p in mesh["primitives"] as JArray ?? new JArray())
            {
                if (((int?)p["mode"] ?? 4) != 4) throw new NotSupportedException("Only triangle primitives are supported.");
                var attributes = p["attributes"] as JObject ?? throw new FormatException("Primitive without attributes.");
                var positions = ReadFloats(gltf, bin, (int?)attributes["POSITION"] ?? throw new FormatException("Primitive without POSITION."), "VEC3");
                var normals = attributes["NORMAL"] == null ? new float[0] : ReadFloats(gltf, bin, (int)attributes["NORMAL"], "VEC3");
                var indices = p["indices"] == null
                    ? Enumerable.Range(0, positions.Length / 3).Select(i => (uint)i).ToArray()
                    : ReadIndices(gltf, bin, (int)p["indices"]);
                var extras = p["extras"] as JObject ?? new JObject();
                var faces = (extras["faces"] as JArray ?? new JArray())
                    .Select(f => new FaceRange((string)f["face_id"], (int?)f["ordinal"] ?? 0, (int)f["first_index"], (int)f["index_count"]))
                    .ToList();
                primitives.Add(new GlbPrimitive((int?)extras["body_index"] ?? primitives.Count + 1,
                    (string)extras["body_name"] ?? (string)mesh["name"] ?? "body", (bool?)extras["visible"] ?? true,
                    positions, normals, indices, faces));
            }
            return new GlbModel((string)gltf["asset"]?["extras"]?["document_id"], primitives);
        }

        private readonly struct Bin
        {
            public Bin(byte[] bytes, int offset, int length) { Bytes = bytes; Offset = offset; Length = length; }
            public byte[] Bytes { get; }
            public int Offset { get; }
            public int Length { get; }
        }

        private static JToken ResolveAccessor(JObject gltf, int accessorIndex)
        {
            var accessors = gltf["accessors"] as JArray;
            if (accessors == null || accessorIndex < 0 || accessorIndex >= accessors.Count) throw new FormatException("Missing accessor " + accessorIndex + ".");
            return accessors[accessorIndex];
        }

        private static JToken ResolveBufferView(JObject gltf, int bufferViewIndex)
        {
            var views = gltf["bufferViews"] as JArray;
            if (views == null || bufferViewIndex < 0 || bufferViewIndex >= views.Count) throw new FormatException("Missing buffer view " + bufferViewIndex + ".");
            return views[bufferViewIndex];
        }

        private static (int start, int count, int componentType) Locate(JObject gltf, Bin bin, int accessorIndex, int componentsPerElement)
        {
            var accessor = ResolveAccessor(gltf, accessorIndex);
            var view = ResolveBufferView(gltf, (int)accessor["bufferView"]);
            int componentType = (int)accessor["componentType"];
            int size = componentType == 5126 || componentType == 5125 ? 4 : componentType == 5123 ? 2 : componentType == 5121 ? 1 : 0;
            if (size == 0) throw new NotSupportedException("Accessor component type " + componentType + " is not supported.");
            if (view["byteStride"] != null && (int)view["byteStride"] != size * componentsPerElement)
                throw new NotSupportedException("Interleaved buffer views are not supported.");
            int count = (int)accessor["count"] * componentsPerElement;
            int start = ((int?)view["byteOffset"] ?? 0) + ((int?)accessor["byteOffset"] ?? 0);
            if (start < 0 || start + count * size > bin.Length) throw new FormatException("Accessor " + accessorIndex + " runs past the BIN chunk.");
            return (bin.Offset + start, count, componentType);
        }

        private static float[] ReadFloats(JObject gltf, Bin bin, int accessorIndex, string type)
        {
            var accessor = ResolveAccessor(gltf, accessorIndex);
            if ((string)accessor["type"] != type) throw new FormatException("Accessor " + accessorIndex + " is not " + type + ".");
            var (start, count, componentType) = Locate(gltf, bin, accessorIndex, 3);
            if (componentType != 5126) throw new NotSupportedException("Only float vertex attributes are supported.");
            var values = new float[count];
            Buffer.BlockCopy(bin.Bytes, start, values, 0, count * 4);
            return values;
        }

        private static uint[] ReadIndices(JObject gltf, Bin bin, int accessorIndex)
        {
            var indicesAccessor = ResolveAccessor(gltf, accessorIndex);
            if ((string)indicesAccessor["type"] != "SCALAR") throw new FormatException("Accessor " + accessorIndex + " is not SCALAR.");
            var (start, count, componentType) = Locate(gltf, bin, accessorIndex, 1);
            var values = new uint[count];
            for (int i = 0; i < count; i++)
                values[i] = componentType == 5125 ? BitConverter.ToUInt32(bin.Bytes, start + i * 4)
                          : componentType == 5123 ? BitConverter.ToUInt16(bin.Bytes, start + i * 2)
                          : bin.Bytes[start + i];
            return values;
        }

        private static uint U32(byte[] bytes, int offset) => BitConverter.ToUInt32(bytes, offset);
    }
}
