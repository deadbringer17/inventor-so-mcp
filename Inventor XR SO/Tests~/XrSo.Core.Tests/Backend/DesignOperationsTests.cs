using InventorXrSo.Core.Backend;
using Newtonsoft.Json.Linq;

namespace InventorXrSo.Core.Tests.Backend;

public class DesignOperationsTests
{
    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(0)]
    [InlineData(-1)]
    public void FeatureDimensionsMustBePositiveFinite(double n)
    {
        Assert.Throws<ArgumentException>(() => DesignOperations.Extrude("S", n, "join", "positive"));
        Assert.Throws<ArgumentException>(() => DesignOperations.Circle("S", 0, 0, n));
        Assert.Throws<ArgumentException>(() => DesignOperations.Fillet(new[] { "ent_edge" }, n));
        Assert.Throws<ArgumentException>(() => DesignOperations.Chamfer(new[] { "ent_edge" }, n));
        Assert.Throws<ArgumentException>(() => DesignOperations.Hole("ent_face", 0, 0, 0, n, null));
    }

    [Fact]
    public void ThroughAndBlindHolesUseModelCoordinatesAndExclusiveDepth()
    {
        var through = DesignOperations.Hole("ent_face", 1, 2, 3, 5, null)["arguments"]!;
        Assert.True((bool)through["through"]!); Assert.Null(through["depth_mm"]);
        var points=Assert.IsType<JArray>(through["points_mm"]);
        var point=Assert.IsType<JArray>(Assert.Single(points));
        Assert.Equal(new[]{1.0,2.0,3.0},point.Values<double>());
        var blind = DesignOperations.Hole("ent_face", 1, 2, 3, 5, 10)["arguments"]!;
        Assert.False((bool)blind["through"]!); Assert.Equal(10, (double)blind["depth_mm"]!);
    }

    [Fact]
    public void DegenerateGeometryAndMissingReferencesAreRejected()
    {
        Assert.Throws<ArgumentException>(() => DesignOperations.Line("S", 1, 2, 1, 2));
        Assert.Throws<ArgumentException>(() => DesignOperations.Rectangle("S", 1, 2, 1, 5));
        Assert.Throws<ArgumentException>(() => DesignOperations.Fillet(Array.Empty<string>(), 2));
        Assert.Throws<ArgumentException>(() => DesignOperations.Hole("face:1", 1, 2, 3, 5, null));
        Assert.Throws<ArgumentException>(() => DesignOperations.Extrude("S", 10, "invalid", "positive"));
    }
}
