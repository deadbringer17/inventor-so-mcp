using System;
using System.IO;
using System.Linq;
using Bimwright.Ipt.Shared.Infrastructure;
using Xunit;

namespace Bimwright.Ipt.Tests;

/// <summary>
/// A punch is an iFeature Inventor executes, so the name is restricted to Inventor's own catalog.
/// These rules are what stop an arbitrary .ide file on the machine from being run.
/// </summary>
public sealed class PunchCatalogPolicyTests
{
    [Theory]
    [InlineData("obround.ide", "obround.ide")]
    [InlineData("obround", "obround.ide")]
    [InlineData("  keyhole.ide  ", "keyhole.ide")]
    [InlineData("D-Sub connector 2", "D-Sub connector 2.ide")]
    public void ValidateName_accepts_catalog_file_names(string punch, string expected)
        => Assert.Equal(expected, PunchCatalogPolicy.ValidateName(punch));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("sub/obround.ide")]
    [InlineData(@"sub\obround.ide")]
    [InlineData("../obround.ide")]
    [InlineData(@"C:\punches\obround.ide")]
    [InlineData(".ide")]
    public void ValidateName_refuses_paths_and_empty_names(string? punch)
        => Assert.Throws<ArgumentException>(() => PunchCatalogPolicy.ValidateName(punch));

    [Fact]
    public void CandidateRoots_walk_up_to_the_inventor_directory()
    {
        var roots = PunchCatalogPolicy.CandidateRoots(
            @"C:\Users\Public\Documents\Autodesk\Inventor 2027\Templates\it-IT", null, 2027);
        Assert.Contains(roots, root => root.EndsWith(Path.Combine("Inventor 2027", "Catalog", "Punches")));
        Assert.All(roots, root => Assert.EndsWith(Path.Combine("Catalog", "Punches"), root));
    }

    [Fact]
    public void CandidateRoots_are_distinct_and_include_the_public_documents_fallback()
    {
        var roots = PunchCatalogPolicy.CandidateRoots(null, null, 2026);
        Assert.Equal(roots.Length, roots.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains(roots, root => root.Contains("Inventor 2026"));
    }

    [Fact]
    public void Resolve_returns_the_first_catalog_that_holds_the_punch()
    {
        var roots = new[] { @"C:\first", @"C:\second" };
        string expected = Path.Combine(@"C:\second", "obround.ide");
        Assert.Equal(expected, PunchCatalogPolicy.Resolve(roots, "obround.ide", path => path == expected));
    }

    [Fact]
    public void Resolve_reports_every_searched_catalog_when_the_punch_is_absent()
    {
        var error = Assert.Throws<FileNotFoundException>(
            () => PunchCatalogPolicy.Resolve(new[] { @"C:\first" }, "obround.ide", _ => false));
        Assert.Contains("PUNCH_NOT_IN_CATALOG", error.Message);
        Assert.Contains(@"C:\first", error.Message);
    }
}
