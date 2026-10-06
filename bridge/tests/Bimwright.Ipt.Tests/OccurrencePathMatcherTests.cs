using System.Linq;
using Bimwright.Ipt.Shared.Contracts;

namespace Bimwright.Ipt.Tests;

public sealed class OccurrencePathMatcherTests
{
    // Assembly: Frame:1, SubA:1/{Bolt:1, Nut:1}, SubB:1/{Bolt:1}, Bolt:1 is NOT top level here.
    private static readonly (string Path, string Name)[] Tree =
    {
        ("Frame:1", "Frame:1"),
        ("SubA:1", "SubA:1"),
        ("SubA:1/Bolt:1", "Bolt:1"),
        ("SubA:1/Nut:1", "Nut:1"),
        ("SubB:1", "SubB:1"),
        ("SubB:1/Bolt:1", "Bolt:1"),
    };

    [Fact]
    public void BareNameDuplicatedInTwoSubassembliesIsAmbiguous()
    {
        var match = OccurrencePathMatcher.Resolve(Tree, "Bolt:1");
        Assert.Equal(OccurrenceMatchKind.Ambiguous, match.Kind);
        Assert.Equal(new[] { "SubA:1/Bolt:1", "SubB:1/Bolt:1" }, match.Candidates.Select(i => Tree[i].Path).ToArray());
    }

    [Fact]
    public void FullPathPicksTheIntendedCopy()
    {
        var match = OccurrencePathMatcher.Resolve(Tree, "SubB:1/Bolt:1");
        Assert.Equal(OccurrenceMatchKind.Found, match.Kind);
        Assert.Equal("SubB:1/Bolt:1", Tree[match.Index].Path);
    }

    [Fact]
    public void UniqueNestedBareNameResolves()
    {
        var match = OccurrencePathMatcher.Resolve(Tree, "nut:1");   // case-insensitive
        Assert.Equal(OccurrenceMatchKind.Found, match.Kind);
        Assert.Equal("SubA:1/Nut:1", Tree[match.Index].Path);
    }

    [Fact]
    public void TopLevelExactMatchWinsOverNestedSameName()
    {
        var tree = new[]
        {
            ("Bolt:1", "Bolt:1"),
            ("SubA:1", "SubA:1"),
            ("SubA:1/Bolt:1", "Bolt:1"),
        };
        var match = OccurrencePathMatcher.Resolve(tree, "Bolt:1");
        Assert.Equal(OccurrenceMatchKind.Found, match.Kind);
        Assert.Equal(0, match.Index);
    }

    [Theory]
    [InlineData("Missing:1")]
    [InlineData("SubA:1/Missing:1")]
    [InlineData("SubB:1/Nut:1")]
    [InlineData("")]
    [InlineData("   ")]
    public void UnknownReferenceIsNotFound(string query)
        => Assert.Equal(OccurrenceMatchKind.NotFound, OccurrencePathMatcher.Resolve(Tree, query).Kind);

    [Fact]
    public void QueryIsTrimmed()
        => Assert.Equal(OccurrenceMatchKind.Found, OccurrencePathMatcher.Resolve(Tree, "  Frame:1 ").Kind);

    [Fact]
    public void PathIsNotMatchedByItsLastSegmentOnly()
        => Assert.Equal(OccurrenceMatchKind.NotFound, OccurrencePathMatcher.Resolve(Tree, "SubC:1/Bolt:1").Kind);

    [Fact]
    public void CombineBuildsSlashSeparatedPaths()
    {
        Assert.Equal("A:1", OccurrencePathMatcher.Combine("", "A:1"));
        Assert.Equal("A:1/B:1", OccurrencePathMatcher.Combine("A:1", "B:1"));
    }

    [Fact]
    public void ListIsCappedAtTwentyWithRemainderCount()
    {
        var items = Enumerable.Range(1, 25).Select(i => "P" + i).ToList();
        string text = OccurrencePathMatcher.FormatList(items);
        Assert.Contains("P20", text);
        Assert.DoesNotContain("P21", text);
        Assert.EndsWith("+5 more", text);
    }

    [Fact]
    public void ShortListIsNotTruncated()
        => Assert.Equal("a, b", OccurrencePathMatcher.FormatList(new[] { "a", "b" }));

    [Fact]
    public void ExactlyTwentyItemsAreAllListed()
    {
        var items = Enumerable.Range(1, 20).Select(i => "P" + i).ToList();
        Assert.DoesNotContain("more", OccurrencePathMatcher.FormatList(items));
    }
}
