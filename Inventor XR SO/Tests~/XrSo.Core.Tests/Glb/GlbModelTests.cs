using System.Collections.Generic;
using System.Text;
using Bimwright.Ipt.Server.Assets;
using Bimwright.Ipt.Tests;
using InventorXrSo.Core.Glb;
using InventorXrSo.Core.Tests.Support;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace InventorXrSo.Core.Tests.Glb;

public class GlbModelTests
{
    /// <summary>What the server publishes for FakeAddIn's 1 cm bolt: one body, six faces, 12 triangles.</summary>
    internal static byte[] BoltGlb() => GlbBuilder.BuildDefinition(new GlbBuilder.MeshSource
    {
        Name = FakeAddIn.BoltId, DocumentId = FakeAddIn.BoltId, Bodies = new[] { FakeAddIn.Box(1.0, FakeAddIn.BoltId) },
    });

    [Fact]
    public void ReadsGeometryInMetresAndTheFaceTable()
    {
        var model = GlbModel.Parse(BoltGlb());
        Assert.Equal(FakeAddIn.BoltId, model.DocumentId);
        var body = Assert.Single(model.Primitives);
        Assert.Equal(24 * 3, body.Positions.Length);
        Assert.Equal(24 * 3, body.Normals.Length);
        Assert.Equal(36, body.Indices.Length);
        Assert.Equal(12, body.TriangleCount);
        Assert.Equal(0.01f, body.Positions.Max(), 5);
        Assert.Equal(6, body.Faces.Count);
        Assert.True(body.Visible);
    }

    [Theory]
    [InlineData(0, "ent_doc_bolt_f1")]
    [InlineData(1, "ent_doc_bolt_f1")]
    [InlineData(2, "ent_doc_bolt_f2")]
    [InlineData(11, "ent_doc_bolt_f6")]
    public void MapsTrianglesToFaces(int triangle, string faceId) =>
        Assert.Equal(faceId, GlbModel.Parse(BoltGlb()).Primitives[0].FaceMap.FaceAtTriangle(triangle).FaceId);

    [Fact]
    public void TrianglesOutsideEveryFaceMapToNothing() =>
        Assert.Null(GlbModel.Parse(BoltGlb()).Primitives[0].FaceMap.FaceAtTriangle(12));

    [Fact]
    public void RejectsCorruptFiles()
    {
        var glb = BoltGlb();
        glb[0] = 0;
        Assert.Throws<FormatException>(() => GlbModel.Parse(glb));
        Assert.Throws<FormatException>(() => GlbModel.Parse(new byte[4]));
    }

    [Fact]
    public void FaceRangesMustBeWholeNonOverlappingTriangles()
    {
        Assert.Throws<FormatException>(() => new FaceMap(new[] { new FaceRange("a", 1, 1, 3) }, 6));
        Assert.Throws<FormatException>(() => new FaceMap(new[] { new FaceRange("a", 1, 0, 9) }, 6));
        Assert.Throws<FormatException>(() => new FaceMap(new[] { new FaceRange("a", 1, 0, 6), new FaceRange("b", 2, 3, 3) }, 6));
    }

    /// <summary>Re-packs a valid bolt GLB with its JSON chunk mutated, keeping the original BIN chunk and a correct header.</summary>
    private static byte[] WithMutatedJson(Action<JObject> mutate)
    {
        var glb = BoltGlb();
        int jsonLength = (int)BitConverter.ToUInt32(glb, 12);
        var json = JObject.Parse(Encoding.UTF8.GetString(glb, 20, jsonLength));
        mutate(json);
        var newJson = Encoding.UTF8.GetBytes(json.ToString(Formatting.None));
        int jsonPadded = (newJson.Length + 3) & ~3;
        int binStart = 20 + jsonLength;
        int binLength = glb.Length - binStart;
        var result = new byte[20 + jsonPadded + binLength];
        BitConverter.GetBytes(0x46546C67u).CopyTo(result, 0);
        BitConverter.GetBytes(2u).CopyTo(result, 4);
        BitConverter.GetBytes((uint)result.Length).CopyTo(result, 8);
        BitConverter.GetBytes((uint)jsonPadded).CopyTo(result, 12);
        BitConverter.GetBytes(0x4E4F534Au).CopyTo(result, 16);
        Array.Copy(newJson, 0, result, 20, newJson.Length);
        for (int i = newJson.Length; i < jsonPadded; i++) result[20 + i] = 0x20;
        Array.Copy(glb, binStart, result, 20 + jsonPadded, binLength);
        return result;
    }

    private static JObject FirstPrimitive(JObject json) => (JObject)json["meshes"][0]["primitives"][0];

    [Fact]
    public void RejectsOutOfRangePositionAccessor()
    {
        var glb = WithMutatedJson(json => ((JObject)FirstPrimitive(json)["attributes"])["POSITION"] = 99);
        Assert.Throws<FormatException>(() => GlbModel.Parse(glb));
    }

    [Fact]
    public void RejectsOutOfRangeBufferViewOnIndicesAccessor()
    {
        var glb = WithMutatedJson(json =>
        {
            int indicesAccessor = (int)FirstPrimitive(json)["indices"];
            ((JObject)json["accessors"][indicesAccessor])["bufferView"] = 99;
        });
        Assert.Throws<FormatException>(() => GlbModel.Parse(glb));
    }

    [Fact]
    public void RejectsIndicesAccessorThatIsNotScalar()
    {
        var glb = WithMutatedJson(json =>
        {
            int indicesAccessor = (int)FirstPrimitive(json)["indices"];
            ((JObject)json["accessors"][indicesAccessor])["type"] = "VEC3";
        });
        Assert.Throws<FormatException>(() => GlbModel.Parse(glb));
    }

    /// <summary>One triangle with POSITION and, optionally, a COLOR_0 accessor of the given glTF type (float32).</summary>
    private static byte[] TriangleGlb(string colorType, float[] colors, int colorComponentType = 5126)
    {
        var positions = new float[] { 0, 0, 0, 1, 0, 0, 0, 1, 0 };
        var bin = new List<byte>();
        foreach (var f in positions) bin.AddRange(BitConverter.GetBytes(f));
        int colorOffset = bin.Count;
        if (colors != null) foreach (var f in colors) bin.AddRange(BitConverter.GetBytes(f));
        while (bin.Count % 4 != 0) bin.Add(0);
        var attributes = new JObject { ["POSITION"] = 0 };
        var accessors = new JArray { new JObject { ["bufferView"] = 0, ["componentType"] = 5126, ["count"] = 3, ["type"] = "VEC3" } };
        var views = new JArray { new JObject { ["buffer"] = 0, ["byteOffset"] = 0, ["byteLength"] = 36 } };
        if (colors != null)
        {
            attributes["COLOR_0"] = 1;
            accessors.Add(new JObject { ["bufferView"] = 1, ["componentType"] = colorComponentType, ["count"] = 3, ["type"] = colorType });
            views.Add(new JObject { ["buffer"] = 0, ["byteOffset"] = colorOffset, ["byteLength"] = colors.Length * 4 });
        }
        var json = new JObject
        {
            ["asset"] = new JObject { ["version"] = "2.0" },
            ["buffers"] = new JArray { new JObject { ["byteLength"] = bin.Count } },
            ["bufferViews"] = views,
            ["accessors"] = accessors,
            ["meshes"] = new JArray { new JObject { ["name"] = "tri", ["primitives"] = new JArray { new JObject { ["attributes"] = attributes } } } },
        };
        var jsonBytes = new List<byte>(Encoding.UTF8.GetBytes(json.ToString(Formatting.None)));
        while (jsonBytes.Count % 4 != 0) jsonBytes.Add(0x20);
        var glb = new List<byte>();
        glb.AddRange(BitConverter.GetBytes(0x46546C67u));
        glb.AddRange(BitConverter.GetBytes(2u));
        glb.AddRange(BitConverter.GetBytes((uint)(12 + 8 + jsonBytes.Count + 8 + bin.Count)));
        glb.AddRange(BitConverter.GetBytes((uint)jsonBytes.Count));
        glb.AddRange(BitConverter.GetBytes(0x4E4F534Au));
        glb.AddRange(jsonBytes);
        glb.AddRange(BitConverter.GetBytes((uint)bin.Count));
        glb.AddRange(BitConverter.GetBytes(0x004E4942u));
        glb.AddRange(bin);
        return glb.ToArray();
    }

    [Fact]
    public void ReadsColor0Vec4ExactlyAsLinearRgba()
    {
        var rgba = new float[] { 1, 0, 0, 1, 0.25f, 0.5f, 0.75f, 0.5f, 0, 0, 1, 1 };
        var primitive = Assert.Single(GlbModel.Parse(TriangleGlb("VEC4", rgba)).Primitives);
        Assert.Equal(rgba, primitive.Colors);
    }

    [Fact]
    public void Color0Vec3GetsOpaqueAlpha()
    {
        var primitive = Assert.Single(GlbModel.Parse(TriangleGlb("VEC3", new float[] { 1, 0, 0, 0, 1, 0, 0, 0, 1 })).Primitives);
        Assert.Equal(new float[] { 1, 0, 0, 1, 0, 1, 0, 1, 0, 0, 1, 1 }, primitive.Colors);
    }

    [Fact]
    public void MissingColor0GivesEmptyColors()
    {
        Assert.Empty(GlbModel.Parse(BoltGlb()).Primitives[0].Colors);
        Assert.Empty(Assert.Single(GlbModel.Parse(TriangleGlb(null, null)).Primitives).Colors);
    }

    [Fact]
    public void UnsupportedColor0IsIgnoredNotFatal()
    {
        // wrong accessor type, and a non-float component type: geometry still loads, colours are dropped
        Assert.Empty(Assert.Single(GlbModel.Parse(TriangleGlb("SCALAR", new float[] { 1, 2, 3 })).Primitives).Colors);
        var normalised = Assert.Single(GlbModel.Parse(TriangleGlb("VEC4", new float[12], 5121)).Primitives);
        Assert.Empty(normalised.Colors);
        Assert.Equal(9, normalised.Positions.Length);
    }

    /// <summary>
    /// Golden GLB for the Unity EditMode tests (Unity cannot run GlbBuilder). Regenerate with
    /// XRSO_UPDATE_FIXTURES=1 when GlbBuilder changes on purpose.
    /// </summary>
    [Fact]
    public void UnityFixtureMatchesTheServerOutput()
    {
        var path = Path.Combine(RepoPaths.XrProject, "Assets", "XrSo", "Tests", "EditMode", "Fixtures", "bolt-1cm.glb.bytes");
        var expected = BoltGlb();
        if (Environment.GetEnvironmentVariable("XRSO_UPDATE_FIXTURES") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, expected);
        }
        Assert.True(File.Exists(path), "Missing " + path + ": run once with XRSO_UPDATE_FIXTURES=1.");
        Assert.Equal(expected, File.ReadAllBytes(path));
    }
}
