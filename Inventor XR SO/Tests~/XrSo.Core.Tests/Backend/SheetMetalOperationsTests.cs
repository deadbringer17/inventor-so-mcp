using InventorXrSo.Core.Backend;
using Newtonsoft.Json.Linq;

namespace InventorXrSo.Core.Tests.Backend;

public class SheetMetalOperationsTests
{
    [Fact]
    public void FlangeBuildsWireOperationWithDefaultsAndDedupedEdges()
    {
        var op = SheetMetalOperations.Flange(new[] { "ent_a", "ent_b", "ent_a" }, 12.5);
        Assert.Equal("sheet_metal_flange", (string)op["command"]);
        var a = (JObject)op["arguments"];
        Assert.Equal(new[] { "ent_a", "ent_b" }, a["edge_ids"].Values<string>());
        Assert.Equal(12.5, (double)a["height_mm"]); Assert.Equal(90, (double)a["angle_degrees"]); Assert.Equal("outer", (string)a["height_datum"]);
    }

    [Theory]
    [InlineData(0.0, 90.0, "outer")]
    [InlineData(-1.0, 90.0, "outer")]
    [InlineData(10001.0, 90.0, "outer")]
    [InlineData(double.NaN, 90.0, "outer")]
    [InlineData(10.0, 0.0, "outer")]
    [InlineData(10.0, 360.0, "outer")]
    [InlineData(10.0, double.PositiveInfinity, "outer")]
    [InlineData(10.0, 90.0, "middle")]
    public void FlangeRejectsInvalidNumbersAndDatum(double height, double angle, string datum) =>
        Assert.Throws<ArgumentException>(() => SheetMetalOperations.Flange(new[] { "ent_a" }, height, angle, datum));

    [Fact]
    public void FlangeRequiresPersistentCadEdgeIdsInRange()
    {
        Assert.Throws<ArgumentException>(() => SheetMetalOperations.Flange(null, 10));
        Assert.Throws<ArgumentException>(() => SheetMetalOperations.Flange(Array.Empty<string>(), 10));
        Assert.Throws<ArgumentException>(() => SheetMetalOperations.Flange(new[] { "mesh_face_3" }, 10));
        Assert.Throws<ArgumentException>(() => SheetMetalOperations.Flange(Enumerable.Range(0, 257).Select(i => "ent_" + i).ToArray(), 10));
        Assert.NotNull(SheetMetalOperations.Flange(Enumerable.Range(0, 256).Select(i => "ent_" + i).ToArray(), 10000, 359.5, "tangent"));
    }

    [Fact]
    public void RuleNeedsSomethingAndBoundedThickness()
    {
        Assert.Throws<ArgumentException>(() => SheetMetalOperations.SetRule(null, null, null));
        Assert.Throws<ArgumentException>(() => SheetMetalOperations.SetRule(null, null, 0.04));
        Assert.Throws<ArgumentException>(() => SheetMetalOperations.SetRule(null, null, 50.1));
        Assert.Throws<ArgumentException>(() => SheetMetalOperations.SetRule(null, null, double.NaN));
        var a = (JObject)SheetMetalOperations.SetRule("Default", null, 1.5)["arguments"];
        Assert.Equal("Default", (string)a["rule"]); Assert.Equal(1.5, (double)a["thickness_mm"]); Assert.Null(a["unfold_rule"]);
        Assert.Equal("K", (string)((JObject)SheetMetalOperations.SetRule(null, "K", null)["arguments"])["unfold_rule"]);
    }

    [Fact]
    public void FaceAndCutValidateSketchAndEnums()
    {
        Assert.Equal("Sketch1", (string)SheetMetalOperations.Face("Sketch1")["arguments"]["sketch_name"]);
        Assert.Throws<ArgumentException>(() => SheetMetalOperations.Face(" "));
        var cut = (JObject)SheetMetalOperations.Cut("S", "through_all", "symmetric")["arguments"];
        Assert.Equal("through_all", (string)cut["extent"]); Assert.Equal("symmetric", (string)cut["direction"]); Assert.Null(cut["across_bends"]);
        Assert.True((bool)SheetMetalOperations.Cut("S", "thickness", "positive", true)["arguments"]["across_bends"]);
        Assert.Throws<ArgumentException>(() => SheetMetalOperations.Cut("S", "through_all", "positive", true));
        Assert.Throws<ArgumentException>(() => SheetMetalOperations.Cut("S", "deep"));
        Assert.Throws<ArgumentException>(() => SheetMetalOperations.Cut("S", "thickness", "up"));
    }

    [Fact]
    public void FlatPatternOptionalArgumentsAreValidated()
    {
        Assert.Empty(((JObject)SheetMetalOperations.CreateFlatPattern()["arguments"]).Properties());
        var a = (JObject)SheetMetalOperations.CreateFlatPattern("ent_e", "vertical", true)["arguments"];
        Assert.Equal("ent_e", (string)a["align_to_edge_id"]); Assert.Equal("vertical", (string)a["alignment"]); Assert.True((bool)a["alignment_reversed"]);
        Assert.Throws<ArgumentException>(() => SheetMetalOperations.CreateFlatPattern("edge1"));
        Assert.Throws<ArgumentException>(() => SheetMetalOperations.CreateFlatPattern(null, "diagonal"));
    }

    [Fact]
    public void BuildersReturnIndependentOperations()
    {
        var one = SheetMetalOperations.Face("S"); var two = SheetMetalOperations.Face("S");
        one["arguments"]["sketch_name"] = "X"; Assert.Equal("S", (string)two["arguments"]["sketch_name"]);
    }
}
