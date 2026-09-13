using System.Collections.Generic;
using Bimwright.Ipt.Shared.Infrastructure;
namespace Bimwright.Ipt.Tests;
public sealed class ArtifactFormatPolicyTests
{
    [Theory]
    [InlineData("part", "native", false, ".ipt")]
    [InlineData("part", "step", false, ".step")]
    [InlineData("part", "dxf", false, ".dxf")]
    [InlineData("assembly", "step", false, ".step")]
    [InlineData("drawing", "native", false, ".idw")]
    [InlineData("drawing", "native", true, ".dwg")]
    [InlineData("drawing", "pdf", false, ".pdf")]
    public void Supported(string kind, string format, bool dwg, string extension) => Assert.Equal(extension, ArtifactFormatPolicy.Extension(kind, format, dwg));
    [Theory]
    [InlineData("assembly", "native")]
    [InlineData("drawing", "step")]
    [InlineData("part", "pdf")]
    [InlineData("assembly", "dxf")]
    [InlineData("drawing", "dxf")]
    [InlineData("unknown", "native")]
    public void Unsupported(string kind, string format) => Assert.Throws<ArgumentException>(() => ArtifactFormatPolicy.Extension(kind, format));

    [Theory]
    [InlineData("2018", "FLAT PATTERN DXF?AcadVersion=2018")]
    [InlineData("2000", "FLAT PATTERN DXF?AcadVersion=2000")]
    [InlineData(" 2013 ", "FLAT PATTERN DXF?AcadVersion=2013")]
    [InlineData(null, "FLAT PATTERN DXF?AcadVersion=2018")]
    [InlineData("", "FLAT PATTERN DXF?AcadVersion=2018")]
    public void FlatPatternDxfOptionsUseAnAllowedVersion(string? version, string expected)
        => Assert.Equal(expected, ArtifactFormatPolicy.FlatPatternDxfOptions(version));

    [Theory]
    [InlineData("2019")]
    [InlineData("R12")]
    [InlineData("2018&Layers=ALL")]
    [InlineData("2018?x=1")]
    public void FlatPatternDxfOptionsRefuseAnythingElse(string version)
        => Assert.Throws<ArgumentException>(() => ArtifactFormatPolicy.FlatPatternDxfOptions(version));

    [Fact]
    public void FlatPatternDxfOptionsAppendAllowedLayers()
    {
        var layers = new Dictionary<string, string> { ["OuterProfileLayer"] = "OUTER", ["BendUpLayer"] = "BEND UP" };
        string options = ArtifactFormatPolicy.FlatPatternDxfOptions("2018", layers);
        Assert.StartsWith("FLAT PATTERN DXF?AcadVersion=2018", options);
        Assert.Contains("&OuterProfileLayer=OUTER", options);
        Assert.Contains("&BendUpLayer=BEND UP", options);
    }

    [Theory]
    [InlineData("NotALayerOption", "X")]
    [InlineData("OuterProfileLayer", "")]
    [InlineData("OuterProfileLayer", "bad&AcadVersion=2000")]
    [InlineData("OuterProfileLayer", "bad=value")]
    [InlineData("OuterProfileLayer", "bad?x")]
    public void FlatPatternDxfOptionsRefuseUnknownKeysAndInjectedValues(string key, string value)
        => Assert.Throws<ArgumentException>(() => ArtifactFormatPolicy.FlatPatternDxfOptions(
            "2018", new Dictionary<string, string> { [key] = value }));

    [Fact]
    public void FlatPatternDxfOptionsRefuseAnOverlongLayerName()
        => Assert.Throws<ArgumentException>(() => ArtifactFormatPolicy.FlatPatternDxfOptions(
            "2018", new Dictionary<string, string> { ["OuterProfileLayer"] = new string('L', 65) }));
}
