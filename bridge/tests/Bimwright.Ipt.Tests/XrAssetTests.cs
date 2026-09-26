using System.IO;
using System.Linq;
using System.Text;
using Bimwright.Ipt.Server.Assets;
using Bimwright.Ipt.Shared.Contracts;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Tests;

internal static class GlbReader
{
    /// <summary>Parse a GLB: checks header, chunk layout and padding; returns the JSON and BIN chunks.</summary>
    public static (JObject json, byte[] bin) Read(byte[] glb)
    {
        Assert.True(glb.Length >= 20);
        Assert.Equal(0x46546C67u, System.BitConverter.ToUInt32(glb, 0));
        Assert.Equal(2u, System.BitConverter.ToUInt32(glb, 4));
        Assert.Equal((uint)glb.Length, System.BitConverter.ToUInt32(glb, 8));
        int jsonLength = (int)System.BitConverter.ToUInt32(glb, 12);
        Assert.Equal(0x4E4F534Au, System.BitConverter.ToUInt32(glb, 16));
        Assert.Equal(0, jsonLength % 4);
        var json = JObject.Parse(Encoding.UTF8.GetString(glb, 20, jsonLength));
        int offset = 20 + jsonLength;
        byte[] bin = System.Array.Empty<byte>();
        if (offset < glb.Length)
        {
            int binLength = (int)System.BitConverter.ToUInt32(glb, offset);
            Assert.Equal(0x004E4942u, System.BitConverter.ToUInt32(glb, offset + 4));
            Assert.Equal(0, binLength % 4);
            bin = glb.Skip(offset + 8).Take(binLength).ToArray();
            Assert.Equal(offset + 8 + binLength, glb.Length);
        }
        return (json, bin);
    }

    public static float[] Floats(JObject gltf, byte[] bin, int accessor)
    {
        var a = gltf["accessors"]![accessor]!;
        var view = gltf["bufferViews"]![(int)a["bufferView"]!]!;
        int offset = (int)view["byteOffset"]!, count = (int)a["count"]! * ((string)a["type"]! == "VEC3" ? 3 : 1);
        var values = new float[count];
        System.Buffer.BlockCopy(bin, offset, values, 0, count * 4);
        return values;
    }
}

public sealed class GlbBuilderTests
{
    /// <summary>A 10 mm (1 cm) cube face pair: two faces, four triangles, in centimetres.</summary>
    internal static MeshPayload.Body Body(float scaleCm = 1f)
    {
        var positions = new float[]
        {
            0, 0, 0, scaleCm, 0, 0, scaleCm, scaleCm, 0, 0, scaleCm, 0,          // z = 0 face
            0, 0, scaleCm, scaleCm, 0, scaleCm, scaleCm, scaleCm, scaleCm, 0, scaleCm, scaleCm, // z = 1 face
        };
        var normals = new float[] { 0, 0, -1, 0, 0, -1, 0, 0, -1, 0, 0, -1, 0, 0, 2, 0, 0, 2, 0, 0, 2, 0, 0, 2 };
        var indices = new uint[] { 0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7 };
        return new MeshPayload.Body
        {
            Index = 1, Name = "Solid1", Positions = positions, Normals = normals, Indices = indices,
            Faces =
            {
                new MeshPayload.FaceRange { FaceId = "ent_a", Ordinal = 1, FirstIndex = 0, IndexCount = 6 },
                new MeshPayload.FaceRange { FaceId = "ent_b", Ordinal = 2, FirstIndex = 6, IndexCount = 6 },
            },
        };
    }

    [Fact]
    public void DefinitionGlbIsWellFormedAndInMetres()
    {
        var glb = GlbBuilder.BuildDefinition(new GlbBuilder.MeshSource { Name = "Part1", DocumentId = "doc_x", Bodies = new[] { Body() } });
        var (gltf, bin) = GlbReader.Read(glb);
        Assert.Equal("2.0", (string?)gltf["asset"]!["version"]);
        Assert.Equal("m", (string?)gltf["asset"]!["extras"]!["units"]);
        var primitive = gltf["meshes"]![0]!["primitives"]![0]!;
        int position = (int)primitive["attributes"]!["POSITION"]!;
        Assert.Equal(0.01, (double)gltf["accessors"]![position]!["max"]![0]!, 6);   // 1 cm = 0.01 m
        Assert.Equal(0.0, (double)gltf["accessors"]![position]!["min"]![2]!, 6);
        Assert.Equal(12, (int)gltf["accessors"]![(int)primitive["indices"]!]!["count"]!);
        Assert.Equal(5125, (int)gltf["accessors"]![(int)primitive["indices"]!]!["componentType"]!);
        Assert.Equal((int)gltf["buffers"]![0]!["byteLength"]!, bin.Length);
        foreach (var view in gltf["bufferViews"]!) Assert.Equal(0, (int)view["byteOffset"]! % 4);
        // Face ranges survive for triangle -> face mapping.
        var faces = primitive["extras"]!["faces"]!;
        Assert.Equal("ent_b", (string?)faces[1]!["face_id"]);
        Assert.Equal(6, (int)faces[1]!["first_index"]!);
    }

    [Fact]
    public void NormalsAreUnitLength()
    {
        var glb = GlbBuilder.BuildDefinition(new GlbBuilder.MeshSource { Name = "P", Bodies = new[] { Body() } });
        var (gltf, bin) = GlbReader.Read(glb);
        int normal = (int)gltf["meshes"]![0]!["primitives"]![0]!["attributes"]!["NORMAL"]!;
        var values = GlbReader.Floats(gltf, bin, normal);
        for (int i = 0; i < values.Length; i += 3)
            Assert.Equal(1.0, System.Math.Sqrt(values[i] * values[i] + values[i + 1] * values[i + 1] + values[i + 2] * values[i + 2]), 5);
    }

    [Fact]
    public void SameGeometrySameBytesDifferentGeometryDifferentBytes()
    {
        byte[] Build(float s) => GlbBuilder.BuildDefinition(new GlbBuilder.MeshSource { Name = "P", DocumentId = "doc", Bodies = new[] { Body(s) } });
        Assert.Equal(Build(1), Build(1));
        Assert.NotEqual(Build(1), Build(2));
    }

    [Fact]
    public void EmptyDefinitionStillProducesAValidGlb()
    {
        var glb = GlbBuilder.BuildDefinition(new GlbBuilder.MeshSource { Name = "Empty", Bodies = System.Array.Empty<MeshPayload.Body>() });
        var (gltf, bin) = GlbReader.Read(glb);
        Assert.Null(gltf["meshes"]);
        Assert.Null(gltf["nodes"]![0]!["mesh"]);
        Assert.Empty(bin);
    }

    [Fact]
    public void RowMajorCentimetresBecomeColumnMajorMetres()
    {
        // Inventor: rotation about Z by 90 deg, translation (10, 20, 30) cm, row-major.
        var inventor = new double[] { 0, -1, 0, 10, 1, 0, 0, 20, 0, 0, 1, 30, 0, 0, 0, 1 };
        var gltf = SceneMath.RowMajorCmToGltf(inventor);
        Assert.Equal(new double[] { 0, 1, 0, 0, -1, 0, 0, 0, 0, 0, 1, 0, 0.1, 0.2, 0.3, 1 }, gltf.Select(v => System.Math.Round(v, 9)));
        var mm = SceneMath.RowMajorCmToMm(inventor);
        Assert.Equal(100, mm[3]); Assert.Equal(200, mm[7]); Assert.Equal(300, mm[11]);
    }
}

public sealed class MeshPayloadTests
{
    [Fact]
    public void RoundTripsThroughBase64()
    {
        var json = MeshPayload.ToJson(GlbBuilderTests.Body());
        var body = MeshPayload.FromJson(json);
        Assert.Equal(GlbBuilderTests.Body().Positions, body.Positions);
        Assert.Equal(GlbBuilderTests.Body().Indices, body.Indices);
        Assert.Equal(2, body.Faces.Count);
    }

    [Fact]
    public void OutOfRangeIndexIsRejected()
    {
        var json = MeshPayload.ToJson(GlbBuilderTests.Body());
        json["indices"] = MeshPayload.EncodeIndices(new uint[] { 0, 1, 99 });
        Assert.Throws<System.FormatException>(() => MeshPayload.FromJson(json));
    }

    [Fact]
    public void FaceRangeOutsideIndicesIsRejected()
    {
        var json = MeshPayload.ToJson(GlbBuilderTests.Body());
        json["faces"]![1]!["index_count"] = 9;
        Assert.Throws<System.FormatException>(() => MeshPayload.FromJson(json));
    }

    [Theory]
    [InlineData(new[] { 1, 2, 3, 3, 4, 1 }, 4, 1)]
    [InlineData(new[] { 0, 1, 2, 2, 3, 0 }, 4, 0)]
    [InlineData(new[] { 1, 2, 3 }, 4, 0)]     // max 3 < 4: consistent with 0-based
    public void IndexBaseIsDetected(int[] indices, int vertices, int expected)
        => Assert.Equal(expected, MeshPayload.DetectIndexBase(indices, vertices));

    [Fact]
    public void ImpossibleIndicesAreRejected()
        => Assert.Throws<System.FormatException>(() => MeshPayload.DetectIndexBase(new[] { 0, 5 }, 4));
}

public sealed class SceneComposerTests
{
    private static JObject Scene() => JObject.Parse(@"{
      ""document_id"": ""doc_asm"",
      ""root"": { ""name"": ""Asm"", ""children"": [
        { ""name"": ""Bolt:1"", ""occurrence_id"": ""ent_1"", ""definition_document_id"": ""doc_bolt"", ""definition_kind"": ""part"",
          ""matrix_rowmajor_cm"": [1,0,0,1, 0,1,0,0, 0,0,1,0, 0,0,0,1], ""children"": [] },
        { ""name"": ""Bolt:2"", ""occurrence_id"": ""ent_2"", ""definition_document_id"": ""doc_bolt"", ""definition_kind"": ""part"",
          ""matrix_rowmajor_cm"": [1,0,0,5, 0,1,0,0, 0,0,1,0, 0,0,0,1], ""children"": [] },
        { ""name"": ""Hidden:1"", ""occurrence_id"": ""ent_3"", ""definition_document_id"": ""doc_hidden"", ""visible"": false,
          ""matrix_rowmajor_cm"": [1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1], ""children"": [] },
        { ""name"": ""Sub:1"", ""occurrence_id"": ""ent_4"", ""definition_document_id"": ""doc_sub"", ""definition_kind"": ""assembly"", ""children"": [
            { ""name"": ""Plate:1"", ""occurrence_id"": ""ent_5"", ""definition_document_id"": ""doc_plate"", ""definition_kind"": ""part"",
              ""matrix_rowmajor_cm"": [1,0,0,0, 0,1,0,0, 0,0,1,2, 0,0,0,1], ""children"": [] } ] }
      ] } }");

    [Fact]
    public void AnnotateAddsBothMatrixFormsAndListsVisibleDefinitionsOnce()
    {
        var scene = Scene();
        var definitions = SceneComposer.Annotate(scene);
        Assert.Equal(new[] { "doc_bolt", "doc_plate" }, definitions);
        var bolt2 = scene["root"]!["children"]![1]!;
        Assert.Equal(50.0, (double)bolt2["matrix_mm"]![3]!);
        Assert.Equal(0.05, (double)bolt2["matrix_gltf"]![12]!, 9);
    }

    [Fact]
    public void ComposeInstancesSharedMeshesAndKeepsHiddenNodesWithoutMesh()
    {
        var scene = Scene();
        SceneComposer.Annotate(scene);
        var mesh = new GlbBuilder.MeshSource { Name = "bolt", DocumentId = "doc_bolt", Bodies = new[] { GlbBuilderTests.Body() } };
        var plate = new GlbBuilder.MeshSource { Name = "plate", DocumentId = "doc_plate", Bodies = new[] { GlbBuilderTests.Body(2) } };
        var glb = SceneComposer.Compose(scene, new System.Collections.Generic.Dictionary<string, GlbBuilder.MeshSource>
            { ["doc_bolt"] = mesh, ["doc_plate"] = plate }, "doc_asm");
        var (gltf, _) = GlbReader.Read(glb);
        Assert.Equal(2, ((JArray)gltf["meshes"]!).Count);                   // bolt written once
        var nodes = (JArray)gltf["nodes"]!;
        var bolts = nodes.Where(n => ((string?)n["name"])!.StartsWith("Bolt")).ToArray();
        Assert.Equal((int)bolts[0]["mesh"]!, (int)bolts[1]["mesh"]!);       // instanced
        Assert.Equal(0.05, (double)bolts[1]["matrix"]![12]!, 9);
        Assert.Equal(0.01, (double)bolts[0]["matrix"]![12]!, 9);
        var hidden = nodes.Single(n => (string?)n["name"] == "Hidden:1");
        Assert.Null(hidden["mesh"]);
        Assert.False((bool)hidden["extras"]!["visible"]!);
        var sub = nodes.Single(n => (string?)n["name"] == "Sub:1");
        Assert.Single((JArray)sub["children"]!);
    }
}

public sealed class AssetStoreTests : System.IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "so-assets-" + System.Guid.NewGuid().ToString("N"));
    private System.DateTimeOffset _now = new(2026, 9, 26, 12, 0, 0, System.TimeSpan.Zero);

    private AssetStore Store(long max = 1000, long total = 2000, int ttlMinutes = 10)
        => new(_dir, System.TimeSpan.FromMinutes(ttlMinutes), max, total, () => _now);

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void ContentAddressedAndOwnerScoped()
    {
        var store = Store();
        var a = store.Put(new byte[] { 1, 2, 3 }, "model/gltf-binary", "quest");
        var again = store.Put(new byte[] { 1, 2, 3 }, "model/gltf-binary", "quest");
        Assert.Equal(a.Id, again.Id);
        Assert.Matches("^a_[0-9a-f]{64}$", a.Id);
        Assert.True(store.TryRead(a.Id, "quest", out _, out var bytes));
        Assert.Equal(new byte[] { 1, 2, 3 }, bytes);
        Assert.False(store.TryRead(a.Id, "someone-else", out _, out _));   // foreign reads as not found
    }

    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("a_..%2f..%2fsecret")]
    [InlineData("a_ABCDEF")]
    [InlineData("")]
    [InlineData("C:\\Windows\\win.ini")]
    public void PathLikeIdsAreNeverResolved(string id)
    {
        var store = Store();
        store.Put(new byte[] { 9 }, "text/csv", "quest");
        Assert.False(store.TryRead(id, "quest", out _, out _));
    }

    [Fact]
    public void MimeAllowlistAndSizeLimitAreEnforced()
    {
        var store = Store(max: 4);
        Assert.Throws<System.ArgumentException>(() => store.Put(new byte[] { 1 }, "application/x-msdownload", "quest"));
        Assert.Throws<System.ArgumentException>(() => store.Put(new byte[5], "model/gltf-binary", "quest"));
    }

    [Fact]
    public void ExpiredAssetsDisappearAndReadsRenewTtl()
    {
        var store = Store(ttlMinutes: 10);
        var a = store.Put(new byte[] { 1 }, "image/png", "quest");
        _now = _now.AddMinutes(9);
        Assert.True(store.TryRead(a.Id, "quest", out _, out _));          // renewed to +10 from here
        _now = _now.AddMinutes(9);
        Assert.True(store.TryRead(a.Id, "quest", out _, out _));
        _now = _now.AddMinutes(11);
        Assert.False(store.TryRead(a.Id, "quest", out _, out _));
    }

    [Fact]
    public void TotalLimitEvictsLeastRecentlyUsed()
    {
        var store = Store(max: 1000, total: 1000);
        var first = store.Put(new byte[600], "text/csv", "quest");
        _now = _now.AddSeconds(1);
        var second = store.Put(Enumerable.Repeat((byte)1, 600).ToArray(), "text/csv", "quest");
        Assert.False(store.TryRead(first.Id, "quest", out _, out _));
        Assert.True(store.TryRead(second.Id, "quest", out _, out _));
        Assert.True(store.StoredBytes <= 1000);
    }

    [Fact]
    public void TamperedFileIsNotServed()
    {
        var store = Store();
        var a = store.Put(new byte[] { 1, 2, 3 }, "application/json", "quest");
        File.WriteAllBytes(Path.Combine(store.StorageDirectory, a.Id + ".bin"), new byte[] { 6, 6, 6 });
        Assert.False(store.TryRead(a.Id, "quest", out _, out _));
    }

    [Fact]
    public void LeftoversFromAPreviousRunAreRemoved()
    {
        Directory.CreateDirectory(_dir);
        var stale = Path.Combine(_dir, "a_" + new string('0', 64) + ".bin");
        File.WriteAllBytes(stale, new byte[] { 1 });
        _ = Store();
        Assert.False(File.Exists(stale));
    }

    [Fact]
    public void ProcessesKeepTheirOwnDirectoriesAndDeadOnesAreRemoved()
    {
        Directory.CreateDirectory(_dir);
        var dead = Path.Combine(_dir, "p999999-deadbeef");
        Directory.CreateDirectory(dead);
        File.WriteAllBytes(Path.Combine(dead, "a_x.bin"), new byte[] { 1 });
        var first = Store();
        var kept = first.Put(new byte[] { 7 }, "image/png", "quest");
        var second = Store();   // a second server process on the same workstation
        Assert.NotEqual(first.StorageDirectory, second.StorageDirectory);
        Assert.False(Directory.Exists(dead));
        Assert.True(first.TryRead(kept.Id, "quest", out _, out _));   // the live sibling was not touched
        Assert.False(second.TryRead(kept.Id, "quest", out _, out _));  // and is not served by the other process
    }

    [Fact]
    public void RecordCarriesUrlOnlyWithAPublicBase()
    {
        var store = Store();
        var a = store.Put(new byte[] { 1 }, "image/png", "quest");
        Assert.Null((string?)a.ToJson(null)["asset_url"]);
        Assert.Equal("https://pc:8787/assets/" + a.Id, (string?)a.ToJson("https://pc:8787/")["asset_url"]);
        Assert.Equal("inventor://assets/" + a.Id, (string?)a.ToJson(null)["resource_uri"]);
    }
}
