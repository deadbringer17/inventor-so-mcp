using Bimwright.Ipt.Shared.Infrastructure;

namespace Bimwright.Ipt.Tests;

public sealed class SafeArtifactWriterTests
{
    [Fact]
    public void UniqueOutputsPreserveExistingFiles()
    {
        string root = Path.Combine(Path.GetTempPath(), "inventor-so-artifact-tests", Guid.NewGuid().ToString("N"));
        var first = SafeArtifactWriter.Write(root, ".ipt", p => File.WriteAllText(p, "first"));
        var second = SafeArtifactWriter.Write(root, ".ipt", p => File.WriteAllText(p, "second"));
        Assert.NotEqual(first, second);
        Assert.Equal("first", File.ReadAllText(first));
        Assert.Equal("second", File.ReadAllText(second));
    }

    [Fact]
    public void EmptyOutputIsNotPublished()
    {
        string root = Path.Combine(Path.GetTempPath(), "inventor-so-artifact-tests", Guid.NewGuid().ToString("N"));
        Assert.Throws<IOException>(() => SafeArtifactWriter.Write(root, ".step", p => File.WriteAllText(p, "")));
        Assert.Empty(Directory.GetFiles(root, "model.step", SearchOption.AllDirectories));
    }

    [Fact]
    public void ACallerChosenNameBecomesTheFileStem()
    {
        string root = Path.Combine(Path.GetTempPath(), "inventor-so-artifact-tests", Guid.NewGuid().ToString("N"));
        var path = SafeArtifactWriter.Write(root, ".dxf", p => File.WriteAllText(p, "flat"), null, "Leg bracket-02");
        Assert.Equal("Leg bracket-02.dxf", Path.GetFileName(path));
        Assert.Equal("flat", File.ReadAllText(path));
    }

    [Fact]
    public void TwoArtifactsMayShareANameBecauseEachKeepsItsOwnDirectory()
    {
        string root = Path.Combine(Path.GetTempPath(), "inventor-so-artifact-tests", Guid.NewGuid().ToString("N"));
        var first = SafeArtifactWriter.Write(root, ".dxf", p => File.WriteAllText(p, "first"), null, "panel");
        var second = SafeArtifactWriter.Write(root, ".dxf", p => File.WriteAllText(p, "second"), null, "panel");
        Assert.NotEqual(first, second);
        Assert.Equal("first", File.ReadAllText(first));
        Assert.Equal("second", File.ReadAllText(second));
    }

    [Theory]
    [InlineData("")]
    [InlineData("../escape")]
    [InlineData("sub/dir")]
    [InlineData("CON")]
    [InlineData("trailing.")]
    public void ANameThatCouldLeaveTheArtifactDirectoryIsRefused(string name)
    {
        string root = Path.Combine(Path.GetTempPath(), "inventor-so-artifact-tests", Guid.NewGuid().ToString("N"));
        Assert.Throws<ArgumentException>(() =>
            SafeArtifactWriter.Write(root, ".dxf", p => File.WriteAllText(p, "x"), null, name));
    }

    [Fact]
    public void NoNameStillProducesTheDefaultStem()
    {
        string root = Path.Combine(Path.GetTempPath(), "inventor-so-artifact-tests", Guid.NewGuid().ToString("N"));
        var path = SafeArtifactWriter.Write(root, ".step", p => File.WriteAllText(p, "x"));
        Assert.Equal("model.step", Path.GetFileName(path));
    }

    [Fact]
    public void InvalidExtensionDoesNotWrite() => Assert.Throws<ArgumentException>(() =>
        SafeArtifactWriter.Write(Path.GetTempPath(), ".exe", _ => throw new Exception("must not execute")));

    [Fact]
    public void ExpiredOperationDoesNotWrite() => Assert.Throws<TimeoutException>(() =>
        SafeArtifactWriter.Write(Path.GetTempPath(), ".step", _ => throw new Exception("must not execute"), () => true));
}
