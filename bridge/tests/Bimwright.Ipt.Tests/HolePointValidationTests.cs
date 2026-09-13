using Bimwright.Ipt.Shared.Infrastructure;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Tests;

public sealed class HolePointValidationTests
{
    [Fact]
    public void FourNumericCentersAccepted() => HolePointValidation.Validate(JArray.Parse("[[-4,-4,25],[4,-4,25],[4,4,25],[-4,4,25]]"));

    [Theory]
    [InlineData("[]")]
    [InlineData("[[1,2]]")]
    [InlineData("[[1,2,null]]")]
    [InlineData("[[1,2,\"3\"]]")]
    [InlineData("[[1,2,3],[1,2,3]]")]
    public void BadInputRejected(string input) => Assert.Throws<ArgumentException>(() => HolePointValidation.Validate(JArray.Parse(input)));

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void NonFiniteRejected(double value) => Assert.Throws<ArgumentException>(() => HolePointValidation.Validate(new JArray(new JArray(value, 0, 0))));

    [Fact]
    public void ExcessiveCountRejected() => Assert.Throws<ArgumentException>(() => HolePointValidation.Validate(new JArray(Enumerable.Range(0,257).Select(i => new JArray(i,0,0)))));
}
