using Bimwright.Ipt.Server.Assets;
using Bimwright.Ipt.Tests;
using InventorXrSo.Core.Glb;
using InventorXrSo.Core.Tests.Support;

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
