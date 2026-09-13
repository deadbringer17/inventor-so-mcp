using Bimwright.Ipt.Shared.Infrastructure;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Tests;

public sealed class NativePackageStoreTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "inventor-so-package-tests", Guid.NewGuid().ToString("N"));
    public NativePackageStoreTests() => Directory.CreateDirectory(root);
    private string Source()
    {
        string file = Path.Combine(root, "source.ipt");
        File.WriteAllText(file, "immutable test fixture");
        return file;
    }
    [Theory]
    [InlineData("../escape.ipt")]
    [InlineData("a/../../escape.ipt")]
    [InlineData("C:\\escape.ipt")]
    [InlineData("/escape.ipt")]
    [InlineData("part.ipt:stream")]
    [InlineData("a//b.ipt")]
    [InlineData("a/CON.ipt")]
    [InlineData("part.ipt.")]
    [InlineData("a/LPT1")]
    [InlineData("package.json")]
    public void RejectsUnsafePaths(string path) => Assert.Throws<ArgumentException>(() => NativePackageStore.ValidateRelativePath(path));

    [Fact]
    public void PublishesOnlyAfterValidationAndHashesFinalFiles()
    {
        string source = Source();
        bool validated = false;
        string output = NativePackageStore.Create(Path.Combine(root, "out"),
            new[] { new NativePackageStore.Entry(source, "models/part.ipt") }, stage =>
            {
                Assert.EndsWith(".pending", stage);
                Assert.Equal(File.ReadAllText(source), File.ReadAllText(Path.Combine(stage, "models/part.ipt")));
                File.WriteAllText(Path.Combine(stage, "drawing.idw"), "test drawing");
                validated = true;
            });
        Assert.True(validated);
        Assert.False(output.EndsWith(".pending"));
        var manifest = JObject.Parse(File.ReadAllText(Path.Combine(output, "package.json")));
        Assert.Equal(2, ((JArray)manifest["files"]!).Count);
        Assert.All((JArray)manifest["files"]!, item => Assert.Equal(64, ((string)item["sha256"]!).Length));
        Assert.Equal("immutable test fixture", File.ReadAllText(source));
    }
    [Fact]
    public void RejectsCaseInsensitiveCollisionBeforeWriting()
    {
        string source = Source(), output = Path.Combine(root, "out");
        Assert.Throws<ArgumentException>(() => NativePackageStore.Create(output,
            new[] { new NativePackageStore.Entry(source, "Part.ipt"), new NativePackageStore.Entry(source, "part.ipt") }, _ => { }));
        Assert.False(Directory.Exists(output));
    }
    [Fact]
    public void ValidationFailureRetainsUnpublishedStaging()
    {
        string source = Source(), output = Path.Combine(root, "out");
        Assert.Throws<IOException>(() => NativePackageStore.Create(output,
            new[] { new NativePackageStore.Entry(source, "part.ipt") }, _ => throw new InvalidOperationException("Unresolved reference")));
        Assert.EndsWith(".pending", Assert.Single(Directory.GetDirectories(output)));
        Assert.Empty(Directory.GetFiles(output, "package.json", SearchOption.AllDirectories));
    }
    [Fact]
    public void ExpirationAfterValidationPreventsPublication()
    {
        bool expired = false;
        string source = Source(), output = Path.Combine(root, "out");
        Assert.Throws<IOException>(() => NativePackageStore.Create(output,
            new[] { new NativePackageStore.Entry(source, "part.ipt") }, _ => expired = true, () => expired));
        Assert.EndsWith(".pending", Assert.Single(Directory.GetDirectories(output)));
    }
    [Fact]
    public void ConcurrentSourceMutationPreventsPublication()
    {
        string source = Source(), output = Path.Combine(root, "out");
        Assert.Throws<IOException>(() => NativePackageStore.Create(output,
            new[] { new NativePackageStore.Entry(source, "part.ipt") }, _ => File.WriteAllText(source, "changed fixture")));
        Assert.EndsWith(".pending", Assert.Single(Directory.GetDirectories(output)));
    }
    [Fact]
    public void MissingSourceDoesNotCreateOutput()
    {
        string output = Path.Combine(root, "out");
        Assert.Throws<FileNotFoundException>(() => NativePackageStore.Create(output,
            new[] { new NativePackageStore.Entry(Path.Combine(root, "absent.ipt"), "part.ipt") }, _ => { }));
        Assert.False(Directory.Exists(output));
    }
    [Fact]
    public void ExpiredRequestDoesNotWriteOrCallCad()
    {
        string source = Source(), output = Path.Combine(root, "out");
        bool called = false;
        Assert.Throws<TimeoutException>(() => NativePackageStore.Create(output,
            new[] { new NativePackageStore.Entry(source, "part.ipt") }, _ => called = true, () => true));
        Assert.False(called);
        Assert.False(Directory.Exists(output));
    }
    [Fact]
    public void RepeatedCallsNeverOverwriteEarlierPackage()
    {
        string source = Source(), output = Path.Combine(root, "out");
        var entries = new[] { new NativePackageStore.Entry(source, "part.ipt") };
        string first = NativePackageStore.Create(output, entries, _ => { });
        string second = NativePackageStore.Create(output, entries, _ => { });
        Assert.NotEqual(first, second);
        Assert.Equal(File.ReadAllBytes(Path.Combine(first, "part.ipt")), File.ReadAllBytes(Path.Combine(second, "part.ipt")));
    }
    public void Dispose() => Directory.Delete(root, recursive: true); // unique test-owned directory only
}
