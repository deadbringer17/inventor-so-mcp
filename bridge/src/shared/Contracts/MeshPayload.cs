using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Contracts;

/// <summary>
/// Wire format of a tessellated definition between the add-in and the server. Arrays travel as
/// base64 little-endian binary (float32 / uint32) so a mesh costs roughly a third of the JSON-number
/// form and stays under the pipe's response limit. Coordinates are Inventor's internal centimetres;
/// the server converts to metres when it builds the GLB. Indices are 0-based.
/// </summary>
public static class MeshPayload
{
    /// <summary>One face's run of indices inside its body's index array.</summary>
    public sealed class FaceRange
    {
        public string? FaceId { get; set; }
        public int Ordinal { get; set; }
        public int FirstIndex { get; set; }
        public int IndexCount { get; set; }
        public string? Surface { get; set; }
        /// <summary>Effective appearance colour, sRGB hex "#RRGGBB", or null when unknown.</summary>
        public string? Color { get; set; }
    }

    /// <summary>Normalises "#RRGGBB" (case-insensitive) to upper case; anything else gives null.</summary>
    public static string? NormalizeColor(string? value)
    {
        if (value == null || value.Length != 7 || value[0] != '#') return null;
        for (int i = 1; i < 7; i++)
            if (!Uri.IsHexDigit(value[i])) return null;
        return value.ToUpperInvariant();
    }

    public sealed class Body
    {
        public int Index { get; set; }
        public string Name { get; set; } = "";
        public bool Visible { get; set; } = true;
        public float[] Positions { get; set; } = Array.Empty<float>();
        public float[] Normals { get; set; } = Array.Empty<float>();
        public uint[] Indices { get; set; } = Array.Empty<uint>();
        public List<FaceRange> Faces { get; set; } = new();
    }

    public static string EncodeFloats(float[] values)
    {
        if (!BitConverter.IsLittleEndian) throw new PlatformNotSupportedException("Mesh payloads are little-endian.");
        var bytes = new byte[values.Length * 4];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        return Convert.ToBase64String(bytes);
    }

    public static string EncodeIndices(uint[] values)
    {
        if (!BitConverter.IsLittleEndian) throw new PlatformNotSupportedException("Mesh payloads are little-endian.");
        var bytes = new byte[values.Length * 4];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        return Convert.ToBase64String(bytes);
    }

    public static float[] DecodeFloats(string? base64)
    {
        var bytes = Convert.FromBase64String(base64 ?? "");
        if (bytes.Length % 4 != 0) throw new FormatException("Float array length is not a multiple of 4 bytes.");
        var values = new float[bytes.Length / 4];
        Buffer.BlockCopy(bytes, 0, values, 0, bytes.Length);
        return values;
    }

    public static uint[] DecodeIndices(string? base64)
    {
        var bytes = Convert.FromBase64String(base64 ?? "");
        if (bytes.Length % 4 != 0) throw new FormatException("Index array length is not a multiple of 4 bytes.");
        var values = new uint[bytes.Length / 4];
        Buffer.BlockCopy(bytes, 0, values, 0, bytes.Length);
        return values;
    }

    /// <summary>
    /// Inventor's <c>CalculateFacets</c> index arrays have been reported both 1-based and 0-based
    /// across releases. Detect the base from the data (the smallest index of a non-empty facet list
    /// is 0 or 1, and the largest reaches vertexCount - 1 or vertexCount) rather than trusting one.
    /// </summary>
    public static int DetectIndexBase(IReadOnlyList<int> indices, int vertexCount)
    {
        if (indices.Count == 0) return 0;
        int min = int.MaxValue, max = int.MinValue;
        foreach (var i in indices) { if (i < min) min = i; if (i > max) max = i; }
        if (min >= 1 && max == vertexCount) return 1;
        if (min >= 0 && max <= vertexCount - 1) return 0;
        throw new FormatException("Facet indices " + min + ".." + max + " do not fit " + vertexCount + " vertices.");
    }

    public static JObject ToJson(Body body) => new()
    {
        ["index"] = body.Index,
        ["name"] = body.Name,
        ["visible"] = body.Visible,
        ["vertex_count"] = body.Positions.Length / 3,
        ["triangle_count"] = body.Indices.Length / 3,
        ["positions"] = EncodeFloats(body.Positions),
        ["normals"] = EncodeFloats(body.Normals),
        ["indices"] = EncodeIndices(body.Indices),
        ["faces"] = new JArray(body.Faces.Select(f =>
        {
            var face = new JObject
            {
                ["face_id"] = f.FaceId,
                ["ordinal"] = f.Ordinal,
                ["first_index"] = f.FirstIndex,
                ["index_count"] = f.IndexCount,
                ["surface"] = f.Surface,
            };
            if (f.Color != null) face["color"] = f.Color;
            return face;
        })),
    };

    /// <summary>Decode and check one body: array sizes agree and every index is in range.</summary>
    public static Body FromJson(JObject json)
    {
        var body = new Body
        {
            Index = (int?)json["index"] ?? 0,
            Name = (string?)json["name"] ?? "",
            Visible = (bool?)json["visible"] ?? true,
            Positions = DecodeFloats((string?)json["positions"]),
            Normals = DecodeFloats((string?)json["normals"]),
            Indices = DecodeIndices((string?)json["indices"]),
        };
        if (body.Positions.Length % 3 != 0) throw new FormatException("Positions are not xyz triples.");
        if (body.Normals.Length != 0 && body.Normals.Length != body.Positions.Length)
            throw new FormatException("Normals do not match positions.");
        if (body.Indices.Length % 3 != 0) throw new FormatException("Indices are not triangles.");
        uint vertexCount = (uint)(body.Positions.Length / 3);
        foreach (var i in body.Indices)
            if (i >= vertexCount) throw new FormatException("Index " + i + " is outside " + vertexCount + " vertices.");
        foreach (var f in json["faces"] as JArray ?? new JArray())
        {
            var range = new FaceRange
            {
                FaceId = (string?)f["face_id"],
                Ordinal = (int?)f["ordinal"] ?? 0,
                FirstIndex = (int?)f["first_index"] ?? 0,
                IndexCount = (int?)f["index_count"] ?? 0,
                Surface = (string?)f["surface"],
                Color = NormalizeColor(f["color"]?.Type == JTokenType.String ? (string?)f["color"] : null),
            };
            if (range.FirstIndex < 0 || range.IndexCount < 0 || range.IndexCount % 3 != 0 ||
                range.FirstIndex + range.IndexCount > body.Indices.Length)
                throw new FormatException("Face range " + range.Ordinal + " is outside the index array.");
            body.Faces.Add(range);
        }
        return body;
    }
}
