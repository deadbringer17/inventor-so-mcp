using InventorXrSo.Core.Backend;
using Newtonsoft.Json.Linq;

namespace InventorXrSo.Core.Tests.Backend;

public class FeatureEditModelTests
{
    private static FaceFeatureInfo Info(bool suppressed = false, bool healthy = true, bool supported = true) => FaceFeatureInfo.FromJson(new JObject
    {
        ["feature"] = new JObject { ["name"] = "Estrusione1", ["type"] = "extrude", ["suppressed"] = suppressed, ["healthy"] = healthy },
        ["parameters"] = new JArray(
            new JObject { ["name"] = "d3", ["role"] = "distance", ["value"] = 20.0, ["unit"] = "mm", ["expression"] = "20 mm", ["editable"] = true },
            new JObject { ["name"] = "d4", ["role"] = "distance", ["value"] = 40.0, ["unit"] = "mm", ["expression"] = "Largo", ["editable"] = false },
            new JObject { ["name"] = "a1", ["role"] = "angle", ["value"] = 90.0, ["unit"] = "deg", ["expression"] = "d3 * 2", ["editable"] = false }),
        ["previous_feature"] = "Schizzo1"
    });

    [Fact]
    public void ExpressionChipsAreReadOnlyAndNeverWritten()
    {
        var model = new FeatureEditModel(Info(), "ent_f", new[] { "d3", "d4", "a1", "Largo" });
        Assert.Equal(3, model.Chips.Count);
        Assert.False(model.TrySetValue("d4", 5, out var reason));
        StringAssert(reason, "espressione");
        Assert.Equal("Largo", model.Chip("d4").SourceParameter);
        Assert.Null(model.Chip("a1").SourceParameter);
        Assert.True(model.TrySetValue("d3", 25, out _));
        var ops = model.BuildOperations();
        Assert.Single(ops);
        Assert.Equal("set_parameter", (string)ops[0]["command"]);
        Assert.Equal("d3", (string)ops[0]["arguments"]["name"]);
    }

    [Fact]
    public void SeveralModifiedChipsGiveOneOperationEach()
    {
        var info = FaceFeatureInfo.FromJson(new JObject
        {
            ["feature"] = new JObject { ["name"] = "Foro1", ["type"] = "hole", ["suppressed"] = false, ["healthy"] = true },
            ["parameters"] = new JArray(
                new JObject { ["name"] = "d1", ["value"] = 5.0, ["unit"] = "mm", ["expression"] = "5 mm", ["editable"] = true },
                new JObject { ["name"] = "d2", ["value"] = 8.0, ["unit"] = "mm", ["expression"] = "8 mm", ["editable"] = true })
        });
        var model = new FeatureEditModel(info, "ent_f", null);
        model.TrySetValue("d1", 6, out _); model.TrySetValue("d2", 9, out _);
        Assert.Equal(2, model.BuildOperations().Count);
        model.TrySetValue("d2", 8, out _);
        Assert.Single(model.BuildOperations());
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void SuppressedOrUnhealthyBlocksEveryChip(bool suppressed, bool unhealthy)
    {
        var model = new FeatureEditModel(Info(suppressed, !unhealthy), "ent_f", null);
        Assert.NotNull(model.BlockReason);
        Assert.All(model.Chips, c => Assert.False(c.Editable));
        Assert.False(model.TrySetValue("d3", 1, out _));
        Assert.Empty(model.BuildOperations());
    }

    private static void StringAssert(string text, string part) => Assert.Contains(part, text);
}
