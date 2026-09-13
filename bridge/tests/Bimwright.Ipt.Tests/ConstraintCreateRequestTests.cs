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
    [InlineData("type", "insert")]
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
}
