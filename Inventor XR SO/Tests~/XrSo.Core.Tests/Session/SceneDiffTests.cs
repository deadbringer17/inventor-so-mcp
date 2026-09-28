using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Session;

namespace InventorXrSo.Core.Tests.Session;

public class SceneDiffTests
{
    [Fact]
    public void NothingBeforeMeansReload() => Assert.True(SceneDiff.Compare(null, new DocumentState("d", "r", "v")).NeedsReload);

    [Fact]
    public void OtherDocumentIsADocumentChange()
    {
        var diff = SceneDiff.Compare(new DocumentState("a", "r", "v"), new DocumentState("b", "r", "v"));
        Assert.True(diff.DocumentChanged);
        Assert.True(diff.NeedsReload);
    }

    [Fact]
    public void VisualRevisionIsAGeometryChange()
    {
        var diff = SceneDiff.Compare(new DocumentState("a", "r1", "v1"), new DocumentState("a", "r2", "v2"));
        Assert.False(diff.DocumentChanged);
        Assert.True(diff.GeometryChanged);
    }

    [Fact]
    public void RevisionOnlyNeedsNoReload() =>
        Assert.False(SceneDiff.Compare(new DocumentState("a", "r1", "v1"), new DocumentState("a", "r2", "v1")).NeedsReload);

    [Fact]
    public void BackoffDoublesUpToThirtySecondsAndResets()
    {
        var backoff = new Backoff();
        Assert.Equal(new[] { 1, 2, 4, 8, 16, 30, 30 }, Enumerable.Range(0, 7).Select(_ => (int)backoff.Next().TotalSeconds));
        backoff.Reset();
        Assert.Equal(1, (int)backoff.Next().TotalSeconds);
    }
}
