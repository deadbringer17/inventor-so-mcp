using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Mcp;
using Newtonsoft.Json.Linq;

namespace InventorXrSo.Core.Tests.Backend;

public class FaceFeatureTests
{
    private static JObject Example() => new JObject
    {
        ["feature"] = new JObject { ["name"] = "Estrusione1", ["type"] = "extrude", ["suppressed"] = false, ["healthy"] = true },
        ["parameters"] = new JArray(new JObject
        {
            ["name"] = "d3", ["role"] = "distance", ["value"] = 20.0, ["unit"] = "mm", ["expression"] = "20 mm", ["editable"] = true
        }),
        ["previous_feature"] = "Schizzo1"
    };

    [Fact]
    public void ParsesSpecExample()
    {
        var info = FaceFeatureInfo.FromJson(Example());
        Assert.Equal("Estrusione1", info.FeatureName); Assert.Equal("extrude", info.FeatureType);
        Assert.Equal("Schizzo1", info.PreviousFeature);
        Assert.True(info.Supported); Assert.True(info.Healthy); Assert.False(info.Suppressed);
        var p = Assert.Single(info.Parameters);
        Assert.Equal("d3", p.Name); Assert.Equal("distance", p.Role); Assert.Equal("mm", p.Unit);
        Assert.Equal("20 mm", p.Expression); Assert.Equal(20.0, p.Value); Assert.True(p.Editable);
        Assert.True(info.CanEdit);
    }

    [Fact]
    public void ExpressionDrivenParameterIsNotEditable()
    {
        var json = Example();
        json["parameters"][0]["editable"] = false; json["parameters"][0]["expression"] = "d0 * 2";
        var info = FaceFeatureInfo.FromJson(json);
        Assert.False(info.Parameters[0].Editable); Assert.Equal("d0 * 2", info.Parameters[0].Expression);
        Assert.False(info.CanEdit);
    }

    [Fact]
    public void SuppressedFeatureCannotBeEdited()
    {
        var json = Example(); json["feature"]["suppressed"] = true;
        var info = FaceFeatureInfo.FromJson(json);
        Assert.True(info.Suppressed); Assert.False(info.CanEdit); Assert.True(info.Parameters[0].Editable);
    }

    [Fact]
    public void UnhealthyFeatureCannotBeEdited()
    {
        var json = Example(); json["feature"]["healthy"] = false;
        Assert.False(FaceFeatureInfo.FromJson(json).CanEdit);
    }

    [Fact]
    public void UnsupportedFeatureKeepsNameAndType()
    {
        var ex = new McpToolException("inventor_face_feature", "UNSUPPORTED_FEATURE", "no",
            new JObject { ["code"] = "UNSUPPORTED_FEATURE", ["details"] = new JObject { ["feature"] = new JObject { ["name"] = "Loft1", ["type"] = "loft" } } });
        var info = FaceFeatureInfo.FromToolError(ex);
        Assert.False(info.Supported); Assert.False(info.CanEdit); Assert.Empty(info.Parameters);
        Assert.Equal("Loft1", info.FeatureName); Assert.Equal("loft", info.FeatureType);
    }

    [Fact]
    public void UnsupportedFeatureReadsFlatDetails()
    {
        var ex = new McpToolException("t", "UNSUPPORTED_FEATURE", "no", new JObject { ["name"] = "Sweep1", ["type"] = "sweep" });
        var info = FaceFeatureInfo.FromToolError(ex);
        Assert.Equal("Sweep1", info.FeatureName); Assert.Equal("sweep", info.FeatureType);
    }

    [Fact]
    public void UnsupportedFeatureWithoutDetailsDoesNotThrow()
    {
        var info = FaceFeatureInfo.FromToolError(new McpToolException("t", "UNSUPPORTED_FEATURE", "no", null));
        Assert.False(info.Supported); Assert.Null(info.FeatureName);
    }

    [Theory]
    [InlineData("NO_OWNING_FEATURE")]
    [InlineData("STALE_REVISION")]
    public void OtherErrorsAreRethrown(string code)
    {
        var ex = Assert.Throws<McpToolException>(() => FaceFeatureInfo.FromToolError(new McpToolException("t", code, "m", null)));
        Assert.Equal(code, ex.Code);
    }
}
