using System.Text;
using Bimwright.Ipt.Server.Planning;
using Bimwright.Ipt.Shared.Contracts;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Tests;

public sealed class PreviewMeshAssetTests
{
    private static JObject Preview() => new()
    {
        ["status"] = "preview_rolled_back", ["document_id"] = "doc", ["revision"] = "r1",
        ["preview_mesh"] = new JObject
        {
            ["document_id"] = "doc", ["units"] = "cm", ["definition_name"] = "Part",
            ["bodies"] = new JArray(MeshPayload.ToJson(new MeshPayload.Body
            {
                Index = 1, Name = "Body", Positions = new float[] { 0,0,0, 10,0,0, 0,10,0 },
                Normals = new float[] { 0,0,1, 0,0,1, 0,0,1 }, Indices = new uint[] { 0,1,2 },
                Faces = new() { new() { FaceId = "ent_transient", Ordinal = 1, FirstIndex = 0, IndexCount = 3 } },
            })),
        },
    };

    [Fact]
    public void ExportsGlbInMetresWithoutTransientReferences()
    {
        var bytes = PreviewMeshAsset.Build(Preview(), "doc", "r1");
        Assert.Equal("glTF", Encoding.ASCII.GetString(bytes, 0, 4));
        var json = JObject.Parse(Encoding.UTF8.GetString(bytes, 20, BitConverter.ToInt32(bytes, 12)).Trim());
        Assert.DoesNotContain("ent_transient", json.ToString());
        Assert.Equal(0.1, (double)json["accessors"]![0]!["max"]![0]!, 6);
    }

    [Theory]
    [InlineData("status", "committed")]
    [InlineData("document_id", "other")]
    [InlineData("revision", "newer")]
    public void RefusesUnconfirmedRollback(string field, string value)
    {
        var preview = Preview(); preview[field] = value;
        Assert.Throws<FormatException>(() => PreviewMeshAsset.Build(preview, "doc", "r1"));
    }

    [Fact]
    public void MissingOrWrongDocumentMeshFailsClosed()
    {
        var preview = Preview(); preview["preview_mesh"]!["document_id"] = "other";
        Assert.Throws<FormatException>(() => PreviewMeshAsset.Build(preview, "doc", "r1"));
        preview.Remove("preview_mesh");
        Assert.Throws<FormatException>(() => PreviewMeshAsset.Build(preview, "doc", "r1"));
    }
}
