using System;
using Bimwright.Ipt.Shared.Infrastructure;

namespace Bimwright.Ipt.Tests;

public sealed class DrawingTemplateManifestTests
{
    // A3 landscape is 420 x 297 mm. Title block bottom right, note column on the right.
    private const string A3 = @"{
        ""sheet_size"": ""A3"", ""orientation"": ""landscape"",
        ""usable_area_mm"": { ""x_min"": 20, ""y_min"": 15, ""x_max"": 340, ""y_max"": 282 } }";

    [Fact]
    public void ParsesTheDeclaredSheetAndArea()
    {
        var manifest = DrawingTemplateManifest.Parse(A3);
        Assert.Equal("A3", manifest.SheetSize);
        Assert.Equal("landscape", manifest.Orientation);
        Assert.Equal(20, manifest.XMinMm);
        Assert.Equal(282, manifest.YMaxMm);
        manifest.DeclaredSheetCm(out double width, out double height);
        Assert.Equal(42.0, width, 6);
        Assert.Equal(29.7, height, 6);
    }

    [Fact]
    public void SheetSizeAndOrientationAreNormalised()
    {
        var manifest = DrawingTemplateManifest.Parse(A3.Replace(@"""A3""", @"""  a3  """).Replace("landscape", "LANDSCAPE"));
        Assert.Equal("A3", manifest.SheetSize);
        Assert.Equal("landscape", manifest.Orientation);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData(@"{ ""orientation"": ""landscape"", ""usable_area_mm"": { ""x_min"":20,""y_min"":15,""x_max"":340,""y_max"":282 } }")]
    [InlineData(@"{ ""sheet_size"": ""A3"", ""usable_area_mm"": { ""x_min"":20,""y_min"":15,""x_max"":340,""y_max"":282 } }")]
    [InlineData(@"{ ""sheet_size"": ""A9"", ""orientation"": ""landscape"", ""usable_area_mm"": { ""x_min"":20,""y_min"":15,""x_max"":340,""y_max"":282 } }")]
    [InlineData(@"{ ""sheet_size"": ""A3"", ""orientation"": ""sideways"", ""usable_area_mm"": { ""x_min"":20,""y_min"":15,""x_max"":340,""y_max"":282 } }")]
    [InlineData(@"{ ""sheet_size"": ""A3"", ""orientation"": ""landscape"" }")]
    public void IncompleteOrUnknownManifestsAreRefused(string json)
        => Assert.Throws<ArgumentException>(() => DrawingTemplateManifest.Parse(json));

    [Theory]
    [InlineData(@"{ ""x_min"":20,""y_min"":15,""x_max"":""340"",""y_max"":282 }")]   // string, not number
    [InlineData(@"{ ""y_min"":15,""x_max"":340,""y_max"":282 }")]                   // missing field
    [InlineData(@"{ ""x_min"":-5,""y_min"":15,""x_max"":340,""y_max"":282 }")]      // negative origin
    [InlineData(@"{ ""x_min"":340,""y_min"":15,""x_max"":20,""y_max"":282 }")]      // inverted
    [InlineData(@"{ ""x_min"":20,""y_min"":15,""x_max"":60,""y_max"":282 }")]       // narrower than 50 mm
    [InlineData(@"{ ""x_min"":20,""y_min"":15,""x_max"":340,""y_max"":60 }")]       // shorter than 50 mm
    [InlineData(@"{ ""x_min"":20,""y_min"":15,""x_max"":900,""y_max"":282 }")]      // wider than the sheet
    [InlineData(@"{ ""x_min"":20,""y_min"":15,""x_max"":340,""y_max"":400 }")]      // taller than the sheet
    public void BadUsableAreasAreRefused(string area)
    {
        string json = @"{ ""sheet_size"": ""A3"", ""orientation"": ""landscape"", ""usable_area_mm"": " + area + " }";
        Assert.Throws<ArgumentException>(() => DrawingTemplateManifest.Parse(json));
    }

    [Fact]
    public void VerifyTurnsMillimetresIntoTheSheetRectangle()
    {
        var area = DrawingTemplateManifest.Parse(A3).Verify(42.0, 29.7);
        Assert.Equal(2.0, area.XMinCm, 6);
        Assert.Equal(1.5, area.YMinCm, 6);
        Assert.Equal(34.0, area.XMaxCm, 6);
        Assert.Equal(28.2, area.YMaxCm, 6);
        Assert.Equal(42.0, area.SheetWidthCm, 6);
        // Reserved = everything the views may not use, in each axis: 2 cm left + 8 cm right here.
        Assert.Equal(10.0, area.ReservedWidthCm, 6);
    }

    [Fact]
    public void VerifyRefusesAManifestPairedWithTheWrongTemplate()
    {
        var manifest = DrawingTemplateManifest.Parse(A3);
        // The same manifest dropped next to the A2 template: without this check the views would be
        // planned into A3 space on an A2 sheet - or worse, over an A4 title block.
        var error = Assert.Throws<ArgumentException>(() => manifest.Verify(59.4, 42.0));
        Assert.Contains("A3", error.Message);
        Assert.Contains("594", error.Message);
    }

    [Fact]
    public void VerifyToleratesHalfAMillimetreOfSheetRounding()
    {
        var area = DrawingTemplateManifest.Parse(A3).Verify(42.04, 29.66);
        Assert.Equal(42.04, area.SheetWidthCm, 6);
    }

    [Fact]
    public void PortraitIsVerifiedAgainstThePortraitSheet()
    {
        string json = @"{ ""sheet_size"": ""A3"", ""orientation"": ""portrait"",
            ""usable_area_mm"": { ""x_min"": 20, ""y_min"": 15, ""x_max"": 282, ""y_max"": 400 } }";
        var manifest = DrawingTemplateManifest.Parse(json);
        var area = manifest.Verify(29.7, 42.0);
        Assert.Equal(29.7, area.SheetWidthCm, 6);
        Assert.Throws<ArgumentException>(() => manifest.Verify(42.0, 29.7));
    }
}
