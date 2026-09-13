using System;
using System.IO;
using Bimwright.Ipt.Shared.Infrastructure;
using Xunit;

namespace Bimwright.Ipt.Tests;

/// <summary>
/// Package destinations are derived from these results, so the net48 replacement must agree with
/// <see cref="Path.GetRelativePath"/> instead of approximating it.
/// </summary>
public sealed class PathCompatTests
{
    private static string Combine(params string[] parts) => Path.Combine(parts);

    [Fact]
    public void Relative_path_matches_the_framework_for_children()
    {
        string root = Combine(Path.GetTempPath(), "inventor-so-relative");
        string child = Combine(root, "parts", "Bracket.ipt");
        Assert.Equal(Path.GetRelativePath(root, child), PathCompat.GetRelativePath(root, child));
        Assert.Equal(Combine("parts", "Bracket.ipt"), PathCompat.GetRelativePath(root, child));
    }

    [Fact]
    public void Relative_path_matches_the_framework_for_siblings_and_parents()
    {
        string root = Combine(Path.GetTempPath(), "inventor-so-relative", "workspace");
        string sibling = Combine(Path.GetTempPath(), "inventor-so-relative", "library", "Bolt.ipt");
        Assert.Equal(Path.GetRelativePath(root, sibling), PathCompat.GetRelativePath(root, sibling));
        Assert.StartsWith("..", PathCompat.GetRelativePath(root, sibling));
    }

    [Fact]
    public void Same_directory_reports_a_dot()
    {
        string root = Combine(Path.GetTempPath(), "inventor-so-relative");
        Assert.Equal(Path.GetRelativePath(root, root), PathCompat.GetRelativePath(root, root));
        Assert.Equal(".", PathCompat.GetRelativePath(root, root));
    }

    [Fact]
    public void Trailing_separators_and_case_do_not_change_the_result()
    {
        string root = Combine(Path.GetTempPath(), "inventor-so-relative");
        string child = Combine(root, "Bracket.ipt");
        Assert.Equal("Bracket.ipt", PathCompat.GetRelativePath(root + Path.DirectorySeparatorChar, child));
        Assert.Equal("Bracket.ipt", PathCompat.GetRelativePath(root.ToUpperInvariant(), child));
    }

    [Fact]
    public void A_different_root_stays_absolute()
    {
        string other = @"\\server\share\Bracket.ipt";
        string result = PathCompat.GetRelativePath(@"C:\workspace", other);
        Assert.Equal(Path.GetRelativePath(@"C:\workspace", other), result);
        Assert.Equal(other, result);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Empty_arguments_are_rejected(string? value)
    {
        Assert.Throws<ArgumentException>(() => PathCompat.GetRelativePath(value!, @"C:\workspace\Bracket.ipt"));
        Assert.Throws<ArgumentException>(() => PathCompat.GetRelativePath(@"C:\workspace", value!));
    }

    [Fact]
    public void ToHex_is_lower_case_and_unseparated()
    {
        Assert.Equal("00ff10", PathCompat.ToHex(new byte[] { 0x00, 0xFF, 0x10 }));
        Assert.Equal("", PathCompat.ToHex(Array.Empty<byte>()));
    }
}
