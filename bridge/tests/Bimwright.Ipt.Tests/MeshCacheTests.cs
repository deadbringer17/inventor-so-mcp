using Bimwright.Ipt.Server.Assets;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Ipt.Tests;

public class MeshCacheTests
{
    private static GlbBuilder.MeshSource Source(string name) => new() { Name = name, DocumentId = "doc_a" };

    [Fact]
    public void SameVisualRevisionReturnsTheStoredSource()
    {
        var cache = new MeshCache();
        var source = Source("plate");
        cache.Put(MeshCache.Key("doc_a", "e:v3", 0.1, true), new JObject { ["visual_revision"] = "e:v3" }, source);

        Assert.True(cache.TryGet(MeshCache.Key("doc_a", "e:v3", 0.1, true), out var data, out var hit));
        Assert.Same(source, hit);
        Assert.Equal("e:v3", (string?)data["visual_revision"]);
    }

    [Theory]
    [InlineData("doc_b", "e:v3", 0.1, true)]   // another document
    [InlineData("doc_a", "e:v4", 0.1, true)]   // geometry may have changed
    [InlineData("doc_a", "f:v3", 0.1, true)]   // another add-in session
    [InlineData("doc_a", "e:v3", 0.2, true)]   // another chord tolerance
    [InlineData("doc_a", "e:v3", 0.1, false)]  // without face ids
    public void AnyOtherStateMisses(string document, string visual, double tolerance, bool ids)
    {
        var cache = new MeshCache();
        cache.Put(MeshCache.Key("doc_a", "e:v3", 0.1, true), new JObject(), Source("plate"));
        Assert.False(cache.TryGet(MeshCache.Key(document, visual, tolerance, ids), out _, out _));
    }

    [Fact]
    public void ReturnedDataIsACopy()
    {
        var cache = new MeshCache();
        var key = MeshCache.Key("doc_a", "e:v1", 0.1, true);
        cache.Put(key, new JObject { ["revision"] = "e:1" }, Source("plate"));
        Assert.True(cache.TryGet(key, out var first, out _));
        first["revision"] = "changed";
        Assert.True(cache.TryGet(key, out var second, out _));
        Assert.Equal("e:1", (string?)second["revision"]);
    }

    [Fact]
    public void LeastRecentlyUsedEntryIsEvicted()
    {
        var cache = new MeshCache(2);
        string a = MeshCache.Key("doc_a", "e:v1", 0.1, true), b = MeshCache.Key("doc_b", "e:v1", 0.1, true), c = MeshCache.Key("doc_c", "e:v1", 0.1, true);
        cache.Put(a, new JObject(), Source("a"));
        cache.Put(b, new JObject(), Source("b"));
        Assert.True(cache.TryGet(a, out _, out _));   // a is now the most recent
        cache.Put(c, new JObject(), Source("c"));

        Assert.Equal(2, cache.Count);
        Assert.True(cache.TryGet(a, out _, out _));
        Assert.False(cache.TryGet(b, out _, out _));
        Assert.True(cache.TryGet(c, out _, out _));
    }
}
