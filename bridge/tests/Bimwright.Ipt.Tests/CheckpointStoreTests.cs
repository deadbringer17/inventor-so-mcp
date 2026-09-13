using Bimwright.Ipt.Shared.Infrastructure;
namespace Bimwright.Ipt.Tests;

public sealed class CheckpointStoreTests
{
    [Fact]
    public void SemanticSidecarIsVerifiedAndLegacyAbsenceExplicit()
    {
        string root = Root(); var store = new CheckpointStore(root);
        var semantic = new Newtonsoft.Json.Linq.JObject { ["document_id"] = "doc_one", ["schema_version"] = 1 };
        var entry = store.Create("doc_one", "rev_1", "semantic", p => File.WriteAllText(p, "CAD"), semantic: semantic);
        string id = (string)entry["id"]!;
        Assert.Equal(semantic, store.ReadSemantic(id));
        File.AppendAllText(Path.Combine(root, id.Substring(3), "semantic.json"), " ");
        Assert.Throws<IOException>(() => store.ReadSemantic(id));
        var old = store.Create("doc_one", "rev_1", "legacy", p => File.WriteAllText(p, "CAD"));
        Assert.Contains("SEMANTIC_SNAPSHOT_UNAVAILABLE", Assert.Throws<IOException>(() => store.ReadSemantic((string)old["id"]!)).Message);
    }
    private static string Root() => Path.Combine(Path.GetTempPath(), "inventor-so-checkpoint-tests", Guid.NewGuid().ToString("N"));
    [Fact]
    public void PersistsListsAndRecoversWithoutOverwriting()
    {
        string root = Root(); var store = new CheckpointStore(root);
        var entry = store.Create("doc_one", "rev_1", "before edit", p => File.WriteAllText(p, "native CAD fixture"));
        string id = (string)entry["id"]!;
        var reopened = new CheckpointStore(root);
        Assert.Single(reopened.List("doc_one")["checkpoints"]!);
        Assert.Empty(reopened.List("doc_other")["checkpoints"]!);
        string destination = Root();
        string a = reopened.RecoveryCopy(id, destination), b = reopened.RecoveryCopy(id, destination);
        Assert.NotEqual(a, b); Assert.Equal("native CAD fixture", File.ReadAllText(a));
        File.WriteAllText(a, "edited recovery");
        Assert.Equal("native CAD fixture", File.ReadAllText(b));
        Assert.Equal(entry["sha256"], reopened.Read(id)["sha256"]);
    }
    [Theory]
    [InlineData("../outside")]
    [InlineData("cp_../../outside")]
    [InlineData("cp_invalid")]
    public void RejectsTraversal(string id) => Assert.Throws<ArgumentException>(() => new CheckpointStore(Root()).Read(id));
    [Fact]
    public void CorruptSnapshotCannotRecover()
    {
        string root = Root(); var store = new CheckpointStore(root);
        var entry = store.Create("doc_one", "rev_1", "snapshot", p => File.WriteAllText(p, "original"));
        string id = (string)entry["id"]!;
        File.WriteAllText(Path.Combine(root, id.Substring(3), "model.ipt"), "tampered");
        Assert.Throws<IOException>(() => store.RecoveryCopy(id, Root()));
    }
    [Fact]
    public void InvalidLabelDoesNotWrite()
    {
        foreach (var label in new[] { "", "\n", new string('x', 121) })
            Assert.Throws<ArgumentException>(() => new CheckpointStore(Root()).Create("doc_a", "r", label, _ => throw new Exception("must not run")));
    }
    [Fact]
    public void ExpiredCheckpointDoesNotWrite() => Assert.Throws<TimeoutException>(() =>
        new CheckpointStore(Root()).Create("doc_a", "r", "label", _ => throw new Exception("must not run"), () => true));
    [Fact]
    public void EmptyCatalogDoesNotCreateDirectories()
    {
        string root = Root(); Assert.Empty(new CheckpointStore(root).List()["checkpoints"]!); Assert.False(Directory.Exists(root));
    }
}
