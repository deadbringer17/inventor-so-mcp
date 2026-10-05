using InventorXrSo.Core.Backend;
using Newtonsoft.Json.Linq;

namespace InventorXrSo.Core.Tests.Backend;

public class DtoTests
{
    [Fact]
    public void CapabilitiesNeedTheXrToolsAndAReachableTarget()
    {
        var json = JObject.Parse(@"{""server"":{""version"":""0.1.0"",""experimental_enabled"":true},
            ""target"":{""inventor_year"":2027,""reachable"":true,""add_in_experimental_enabled"":true,""active_document_kind"":""assembly""},
            ""capabilities"":{""xr_mesh"":true,""scene_graph"":true,""highlight"":true,""event_subscriptions"":true}}");
        var caps = CapabilitiesInfo.FromJson(json);
        Assert.True(caps.IsXrReady);
        Assert.Equal(2027, caps.InventorYear);
        Assert.Equal("assembly", caps.ActiveDocumentKind);
        json["capabilities"]["highlight"] = false;
        Assert.False(CapabilitiesInfo.FromJson(json).IsXrReady);
    }

    [Fact]
    public void PlacedPartsSkipHiddenSuppressedAndSubassemblyNodes()
    {
        var json = JObject.Parse(@"{""document_id"":""asm"",""kind"":""assembly"",""revision"":""r1"",""visual_revision"":""v1"",
            ""definition_document_ids"":[""p"",""q""],
            ""root"":{""name"":""Top.iam"",""definition_kind"":""assembly"",""children"":[
              {""name"":""P:1"",""occurrence_id"":""ent_1"",""definition_document_id"":""p"",""definition_kind"":""part"",""visible"":true,""suppressed"":false,
               ""matrix_gltf"":[1,0,0,0,0,1,0,0,0,0,1,0,0.5,0,0,1],""children"":[]},
              {""name"":""P:2"",""occurrence_id"":""ent_2"",""definition_document_id"":""p"",""definition_kind"":""part"",""visible"":false,""suppressed"":false,""children"":[]},
              {""name"":""Sub:1"",""occurrence_id"":""ent_3"",""definition_document_id"":""s"",""definition_kind"":""assembly"",""visible"":true,""suppressed"":false,""children"":[
                {""name"":""Q:1"",""occurrence_id"":""ent_4"",""definition_document_id"":""q"",""definition_kind"":""part"",""visible"":true,""suppressed"":false,""children"":[]}]}]}}");
        var scene = SceneGraph.FromJson(json);
        var placed = scene.PlacedParts().ToList();
        Assert.Equal(new[] { "ent_1", "ent_4" }, placed.Select(p => p.Node.OccurrenceId));
        Assert.Equal(0.5f, placed[0].MatrixGltf[12]);
        Assert.Equal(1f, placed[1].MatrixGltf[15]);
        Assert.Equal(new[] { "p", "q" }, scene.DefinitionIds);
        Assert.Equal("v1", scene.State.VisualRevision);
    }

    [Fact]
    public void TopLevelOccurrenceIdMapsNestedLeavesToTheDirectOccurrence()
    {
        var json = JObject.Parse(@"{""document_id"":""asm"",""kind"":""assembly"",""revision"":""r1"",""visual_revision"":""v1"",""definition_document_ids"":[],
            ""root"":{""name"":""Top.iam"",""definition_kind"":""assembly"",""children"":[
              {""name"":""P:1"",""occurrence_id"":""ent_1"",""definition_kind"":""part"",""children"":[]},
              {""name"":""Sub:1"",""occurrence_id"":""ent_3"",""definition_kind"":""assembly"",""children"":[
                {""name"":""Mid:1"",""occurrence_id"":""ent_4"",""definition_kind"":""assembly"",""children"":[
                  {""name"":""Q:1"",""occurrence_id"":""ent_5"",""definition_kind"":""part"",""children"":[]}]}]}]}}");
        var scene = SceneGraph.FromJson(json);
        Assert.Equal("ent_1", scene.TopLevelOccurrenceId("ent_1"));
        Assert.Equal("ent_3", scene.TopLevelOccurrenceId("ent_3"));
        Assert.Equal("ent_3", scene.TopLevelOccurrenceId("ent_4"));
        Assert.Equal("ent_3", scene.TopLevelOccurrenceId("ent_5"));
        Assert.Equal("ent_zzz", scene.TopLevelOccurrenceId("ent_zzz"));
        Assert.Null(scene.TopLevelOccurrenceId(null));
        Assert.Equal(new[] { "ent_3", "ent_4", "ent_5" }, scene.OccurrenceAndDescendantIds("ent_3").OrderBy(x => x));
        Assert.Equal(new[] { "ent_1" }, scene.OccurrenceAndDescendantIds("ent_1"));
        Assert.Empty(scene.OccurrenceAndDescendantIds("nope"));
        Assert.Empty(scene.OccurrenceAndDescendantIds(null));
    }

    [Fact]
    public void APartDocumentPlacesItsRootAtTheOrigin()
    {
        var json = JObject.Parse(@"{""document_id"":""p"",""kind"":""part"",""revision"":""r"",""visual_revision"":""v"",""definition_document_ids"":[""p""],
            ""root"":{""name"":""P.ipt"",""definition_document_id"":""p"",""definition_kind"":""part"",""visible"":true,""suppressed"":false,""children"":[]}}");
        var placed = Assert.Single(SceneGraph.FromJson(json).PlacedParts());
        Assert.Null(placed.Node.OccurrenceId);
        Assert.Equal("p", placed.Node.DefinitionDocumentId);
    }

    [Fact]
    public void AssetIdsAreCheckedAgainstTheirContent()
    {
        var bytes = new byte[] { 1, 2, 3 };
        var id = "a_" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
        Assert.True(AssetIds.Matches(id, bytes));
        Assert.False(AssetIds.Matches(id, new byte[] { 1, 2 }));
        Assert.False(AssetIds.Matches("b_" + id.Substring(2), bytes));
    }

    [Fact]
    public void FileCacheSurvivesANewInstance()
    {
        var dir = Path.Combine(Path.GetTempPath(), "xrso-cache-" + Guid.NewGuid().ToString("N"));
        try
        {
            new FileAssetCache(dir).Put("a_" + new string('1', 64), new byte[] { 9 });
            Assert.True(new FileAssetCache(dir).TryGet("a_" + new string('1', 64), out var bytes));
            Assert.Equal(new byte[] { 9 }, bytes);
            Assert.False(new FileAssetCache(dir).TryGet("../evil", out _));
        }
        finally { Directory.Delete(dir, true); }
    }
}
