using Bimwright.Ipt.Shared.Infrastructure;
using Newtonsoft.Json.Linq;
namespace Bimwright.Ipt.Tests;

public sealed class ConstraintCreateRequestTests
{
    private static JObject Valid() => new() { ["type"] = "flush", ["face_a_id"] = "ent_a", ["face_b_id"] = "ent_b", ["offset_mm"] = 30, ["minimum_clearance_mm"] = 5 };
    [Theory]
    [InlineData("mate")]
    [InlineData("flush")]
    public void DefaultsToPreview(string type)
    {
        var p = Valid(); p["type"] = type;
        var r = ConstraintCreateRequest.Parse(p);
        Assert.True(r.Preview); Assert.Equal(30, r.OffsetMm); Assert.Equal(type, r.Type);
    }
    [Theory]
    [InlineData("type", "joint")]
    [InlineData("face_a_id", "")]
    [InlineData("face_b_id", "ent_a")]
    [InlineData("preview", "false")]
    [InlineData("offset_mm", "30")]
    public void RejectsMalformed(string key, string value)
    {
        var p = Valid(); p[key] = value;
        Assert.Throws<ArgumentException>(() => ConstraintCreateRequest.Parse(p));
    }
    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void RejectsNonfinite(double value)
    {
        foreach (var key in new[] { "offset_mm", "minimum_clearance_mm" })
        { var p = Valid(); p[key] = value; Assert.Throws<ArgumentException>(() => ConstraintCreateRequest.Parse(p)); }
    }
    [Fact]
    public void RejectsMissingNumericAndNegativeClearance()
    {
        var p = Valid(); p.Remove("offset_mm"); Assert.Throws<ArgumentException>(() => ConstraintCreateRequest.Parse(p));
        p = Valid(); p["minimum_clearance_mm"] = -1; Assert.Throws<ArgumentException>(() => ConstraintCreateRequest.Parse(p));
    }

    [Theory]
    [InlineData("mate_axis")]
    [InlineData("insert")]
    [InlineData("tangent")]
    public void OffsetTypesCarryTheirOffset(string type)
    {
        var p = Valid(); p["type"] = type;
        var r = ConstraintCreateRequest.Parse(p);
        Assert.Equal(type, r.Type);
        Assert.Equal(30, r.OffsetMm);
        Assert.Equal(type == "insert", r.NeedsEdges);
        Assert.False(r.NeedsPlanarFaces);
    }

    [Fact]
    public void AngleTakesDegreesAndRefusesAnOffset()
    {
        var p = Valid(); p["type"] = "angle"; p.Remove("offset_mm"); p["angle_degrees"] = 45;
        var r = ConstraintCreateRequest.Parse(p);
        Assert.Equal(45, r.AngleDegrees);
        Assert.Equal(0, r.OffsetMm);

        var withOffset = Valid(); withOffset["type"] = "angle"; withOffset["angle_degrees"] = 45;
        Assert.Throws<ArgumentException>(() => ConstraintCreateRequest.Parse(withOffset));
    }

    [Fact]
    public void AngleDegreesAreRefusedOnOtherTypes()
    {
        var p = Valid(); p["angle_degrees"] = 45;
        Assert.Throws<ArgumentException>(() => ConstraintCreateRequest.Parse(p));
    }

    [Theory]
    [InlineData(360)]
    [InlineData(-360)]
    [InlineData(720)]
    public void AngleIsBounded(double degrees)
    {
        var p = Valid(); p["type"] = "angle"; p.Remove("offset_mm"); p["angle_degrees"] = degrees;
        Assert.Throws<ArgumentException>(() => ConstraintCreateRequest.Parse(p));
    }

    [Fact]
    public void InsertDefaultsToOpposedAxesAndTangentToOutside()
    {
        var p = Valid(); p["type"] = "insert";
        var r = ConstraintCreateRequest.Parse(p);
        Assert.True(r.AxesOpposed);
        Assert.False(r.InsideTangency);

        p["axes_opposed"] = false; p["inside"] = true;
        var flipped = ConstraintCreateRequest.Parse(p);
        Assert.False(flipped.AxesOpposed);
        Assert.True(flipped.InsideTangency);

        p["axes_opposed"] = "yes";
        Assert.Throws<ArgumentException>(() => ConstraintCreateRequest.Parse(p));
    }

    [Fact]
    public void EntityIdsMayUseTheGeneralNames()
    {
        var p = new JObject { ["type"] = "insert", ["entity_a_id"] = "ent_a", ["entity_b_id"] = "ent_b",
            ["offset_mm"] = 0, ["minimum_clearance_mm"] = 0 };
        var r = ConstraintCreateRequest.Parse(p);
        Assert.Equal("ent_a", r.FaceA);
        Assert.Equal("ent_b", r.FaceB);
    }

    [Fact]
    public void PlanarTypesAreTheOnlyOnesNeedingPlanes()
    {
        foreach (var type in new[] { "mate", "flush" })
        {
            var p = Valid(); p["type"] = type;
            Assert.True(ConstraintCreateRequest.Parse(p).NeedsPlanarFaces);
        }
    }
}
