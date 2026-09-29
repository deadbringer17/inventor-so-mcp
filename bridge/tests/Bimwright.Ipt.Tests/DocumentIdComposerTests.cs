using System.Text.RegularExpressions;
using Bimwright.Ipt.Shared.Contracts;
using Xunit;

namespace Bimwright.Ipt.Tests;

public sealed class DocumentIdComposerTests
{
    private const string Name = "{12110C6E-0000-4A11-8B22-AABBCCDDEEFF}";
    private static readonly Regex IdShape = new("^doc_[A-Za-z0-9_{}-]+(~[0-9a-f]{8})?$");

    [Fact]
    public void UniqueNameIsUnchanged() =>
        Assert.Equal("doc_" + Name, DocumentIdComposer.Compose(Name, @"C:\a\x.ipt", false));

    [Fact]
    public void DuplicateGetsEightHexSuffix()
    {
        var id = DocumentIdComposer.Compose(Name, @"C:\a\x.ipt", true);
        Assert.StartsWith("doc_" + Name + "~", id);
        Assert.Matches("~[0-9a-f]{8}$", id);
        Assert.Matches(IdShape, id);
    }

    [Fact]
    public void SuffixIsDeterministic() =>
        Assert.Equal(DocumentIdComposer.Compose(Name, @"C:\a\x.ipt", true), DocumentIdComposer.Compose(Name, @"C:\a\x.ipt", true));

    [Fact]
    public void SuffixDiffersPerPath() =>
        Assert.NotEqual(DocumentIdComposer.Compose(Name, @"C:\a\x.ipt", true), DocumentIdComposer.Compose(Name, @"C:\a\y.ipt", true));

    [Fact]
    public void PathIsCaseAndSlashInsensitive() =>
        Assert.Equal(DocumentIdComposer.Compose(Name, @"C:\Lamiera\SX.ipt", true), DocumentIdComposer.Compose(Name, "c:/lamiera/sx.IPT", true));

    [Fact]
    public void KnownVectorIsStable() =>
        Assert.Equal("ba7816bf".Length, DocumentIdComposer.PathHash("abc").Length);

    [Fact]
    public void NullPathDoesNotThrow() =>
        Assert.Matches("~[0-9a-f]{8}$", DocumentIdComposer.Compose(Name, null, true));
}
