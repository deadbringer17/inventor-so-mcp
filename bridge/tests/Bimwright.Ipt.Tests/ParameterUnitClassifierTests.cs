using Bimwright.Ipt.Shared.Contracts;

namespace Bimwright.Ipt.Tests;

public sealed class ParameterUnitClassifierTests
{
    [Theory]
    [InlineData("mm", ParameterUnitKind.Length)]
    [InlineData("cm", ParameterUnitKind.Length)]
    [InlineData("m", ParameterUnitKind.Length)]
    [InlineData("in", ParameterUnitKind.Length)]
    [InlineData("ft", ParameterUnitKind.Length)]
    [InlineData("mil", ParameterUnitKind.Length)]
    [InlineData("um", ParameterUnitKind.Length)]
    [InlineData("MM", ParameterUnitKind.Length)]
    [InlineData(" mm ", ParameterUnitKind.Length)]
    [InlineData("mm^2", ParameterUnitKind.Area)]
    [InlineData("mm**2", ParameterUnitKind.Area)]
    [InlineData("mm ^ 2", ParameterUnitKind.Area)]
    [InlineData("mm²", ParameterUnitKind.Area)]
    [InlineData("cm^2", ParameterUnitKind.Area)]
    [InlineData("m^2", ParameterUnitKind.Area)]
    [InlineData("in^2", ParameterUnitKind.Area)]
    [InlineData("ft^2", ParameterUnitKind.Area)]
    [InlineData("mm^3", ParameterUnitKind.Volume)]
    [InlineData("mm**3", ParameterUnitKind.Volume)]
    [InlineData("mm³", ParameterUnitKind.Volume)]
    [InlineData("cm^3", ParameterUnitKind.Volume)]
    [InlineData("m^3", ParameterUnitKind.Volume)]
    [InlineData("in^3", ParameterUnitKind.Volume)]
    [InlineData("ft^3", ParameterUnitKind.Volume)]
    [InlineData("deg", ParameterUnitKind.Angle)]
    [InlineData("rad", ParameterUnitKind.Angle)]
    [InlineData("Deg", ParameterUnitKind.Angle)]
    [InlineData("ul", ParameterUnitKind.Unitless)]
    [InlineData("UL", ParameterUnitKind.Unitless)]
    [InlineData("", ParameterUnitKind.Unitless)]
    [InlineData("   ", ParameterUnitKind.Unitless)]
    public void SupportedTokensClassify(string unit, ParameterUnitKind expected)
        => Assert.Equal(expected, ParameterUnitClassifier.Classify(unit));

    [Fact]
    public void NullUnitIsUnitless()
        => Assert.Equal(ParameterUnitKind.Unitless, ParameterUnitClassifier.Classify(null));

    // Each of these used to be scaled as a length, area or volume.
    [Theory]
    [InlineData("min")]        // time: contains "in"
    [InlineData("lbmass")]     // mass: contains "in"? no, but matched "mass"/"in" style substrings before
    [InlineData("mm^4")]       // second moment of area: was "length"
    [InlineData("N/mm^2")]     // stress: was "area"
    [InlineData("kg/m^3")]     // density: was "volume"
    [InlineData("mm/s")]
    [InlineData("kg")]
    [InlineData("s")]
    [InlineData("N")]
    [InlineData("hour")]
    [InlineData("inch")]       // not a table token: the table is exact
    [InlineData("mm^1")]
    [InlineData("mm^-1")]
    [InlineData("degree")]
    [InlineData("gradian")]
    [InlineData("kg mm^2")]
    public void EverythingElseIsOther(string unit)
        => Assert.Equal(ParameterUnitKind.Other, ParameterUnitClassifier.Classify(unit));

    [Fact]
    public void NormalizeFoldsPowersAndWhitespace()
    {
        Assert.Equal("mm^2", ParameterUnitClassifier.Normalize(" MM ** 2 "));
        Assert.Equal("", ParameterUnitClassifier.Normalize(null));
    }
}
