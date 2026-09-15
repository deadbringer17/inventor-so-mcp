using System;
using System.IO;
using Bimwright.Ipt.Shared.Infrastructure;

namespace Bimwright.Ipt.Tests;

public sealed class DrawingTemplatePolicyTests
{
    private const string Root = @"C:\Standards\templates";

    [Fact]
    public void UnsetRootFallsBackToTheHostOwnedLibrary()
    {
        Assert.Equal(DrawingTemplatePolicy.DefaultRoot(), DrawingTemplatePolicy.Resolve(null));
        Assert.Equal(DrawingTemplatePolicy.DefaultRoot(), DrawingTemplatePolicy.Resolve("   "));
        Assert.Contains("InventorSO", DrawingTemplatePolicy.DefaultRoot());
    }

    [Theory]
    [InlineData(@"templates")]          // relative
    [InlineData(@"C:templates")]        // drive-relative
    [InlineData(@"\templates")]         // current-drive
    public void RelativeRootsAreRefusedRatherThanResolvedAgainstTheProcess(string configured)
        => Assert.Throws<ArgumentException>(() => DrawingTemplatePolicy.Resolve(configured));

    [Theory]
    [InlineData(@"C:\")]
    [InlineData(@"C:")]
    public void DriveRootIsNotALibrary(string configured)
        => Assert.Throws<ArgumentException>(() => DrawingTemplatePolicy.Resolve(configured));

    [Fact]
    public void WindowsDirectoryIsRefused()
    {
        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        Assert.Throws<ArgumentException>(() => DrawingTemplatePolicy.Resolve(windows));
        Assert.Throws<ArgumentException>(() => DrawingTemplatePolicy.Resolve(Path.Combine(windows, "templates")));
    }

    [Fact]
    public void AnAbsoluteRootIsKeptWithoutItsTrailingSeparator()
        => Assert.Equal(@"C:\Standards\templates", DrawingTemplatePolicy.Resolve(@"C:\Standards\templates\"));

    [Theory]
    [InlineData("Company_A3.idw")]
    [InlineData("Company A3.DWG")]
    [InlineData("cartiglio-uni-a2.idw")]
    public void TemplateNamesAreAccepted(string name)
        => Assert.Equal(name, DrawingTemplatePolicy.ValidateName(name));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Company_A3")]             // no extension
    [InlineData("Company_A3.ipt")]         // not a drawing
    [InlineData("Company_A3.idw.txt")]     // extension is the last one
    [InlineData(".idw")]                   // no stem
    [InlineData("CON.idw")]                // reserved device name
    [InlineData(" Company_A3.idw")]        // leading whitespace
    public void BadTemplateNamesAreRefused(string? name)
        => Assert.Throws<ArgumentException>(() => DrawingTemplatePolicy.ValidateName(name));

    [Theory]
    [InlineData(@"..\..\Windows\evil.idw")]
    [InlineData(@"sub\Company_A3.idw")]
    [InlineData(@"sub/Company_A3.idw")]
    [InlineData(@"C:\Windows\evil.idw")]
    [InlineData(@"\\server\share\evil.idw")]
    public void PathsAreNeverAcceptedAsTemplateNames(string name)
    {
        Assert.Throws<ArgumentException>(() => DrawingTemplatePolicy.ValidateName(name));
        Assert.Throws<ArgumentException>(() => DrawingTemplatePolicy.PathOf(Root, name));
    }

    [Fact]
    public void PathOfStaysInsideTheLibrary()
    {
        string path = DrawingTemplatePolicy.PathOf(Root, "Company_A3.idw");
        Assert.Equal(Path.Combine(Root, "Company_A3.idw"), path);
        Assert.True(WorkspaceDocumentPolicy.IsInside(Root, path));
    }

    [Fact]
    public void ManifestSitsNextToTheTemplateUnderTheSameStem()
    {
        Assert.Equal(Path.Combine(Root, "Company_A3.json"),
            DrawingTemplatePolicy.ManifestPathOf(Path.Combine(Root, "Company_A3.idw")));
        // The stem, not the whole file name: a .dwg template's manifest is not Company_A3.dwg.json.
        Assert.Equal(Path.Combine(Root, "Company_A3.json"),
            DrawingTemplatePolicy.ManifestPathOf(Path.Combine(Root, "Company_A3.dwg")));
    }

    [Theory]
    [InlineData("a.idw", true)]
    [InlineData("a.IDW", true)]
    [InlineData("a.dwg", true)]
    [InlineData("a.ipt", false)]
    [InlineData("a.json", false)]
    public void OnlyDrawingExtensionsAreTemplates(string file, bool expected)
        => Assert.Equal(expected, DrawingTemplatePolicy.IsTemplateExtension(file));
}
