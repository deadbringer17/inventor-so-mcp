using Bimwright.Ipt.Shared.Handlers.Parameters;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Tests;

public class ParameterValueDtoTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Boolean_values_remain_booleans_without_numeric_conversion(bool value)
    {
        var dto = Map(value, "boolean");
        Assert.Equal(JTokenType.Boolean, dto["value_bool"]!.Type);
        Assert.Equal(value, (bool)dto["value_bool"]!);
        Assert.Equal(JTokenType.Null, dto["evaluated"]!.Type);
        AssertNoNumericFields(dto);
    }

    [Theory]
    [InlineData("")]
    [InlineData("false")]
    [InlineData("123")]
    [InlineData("12 mm")]
    [InlineData("acciaio \"A\" & <B>\n\u03b1\U0001F527")]
    public void Text_values_round_trip_without_interpreting_expressions(string value)
    {
        var dto = Map(value, "text");
        var roundTrip = JObject.Parse(dto.ToString());
        Assert.Equal(JTokenType.String, roundTrip["value_text"]!.Type);
        Assert.Equal(value, (string?)roundTrip["value_text"]);
        Assert.Null(dto["value_bool"]);
        Assert.Equal(JTokenType.Null, dto["evaluated"]!.Type);
        AssertNoNumericFields(dto);
    }

    [Theory]
    [InlineData("mm", "value_mm", 20)]
    [InlineData("cm", "value_mm", 20)]
    [InlineData("mm^2", "value_mm2", 200)]
    [InlineData("mm^3", "value_mm3", 2000)]
    [InlineData("ul", "value_unitless", 2)]
    [InlineData("", "value_unitless", 2)]
    public void Existing_numeric_unit_fields_are_preserved(string unit, string field, double expected)
    {
        var dto = Map(2d, unit);
        Assert.Equal(expected, (double)dto[field]!);
        Assert.Null(dto["value_bool"]);
        Assert.Null(dto["value_text"]);
        Assert.Null(dto["evaluated"]);
    }

    [Fact]
    public void Radians_are_converted_to_degrees_and_compound_units_keep_the_compatibility_alias()
    {
        Assert.Equal(180d, (double)Map(Math.PI, "rad")["value_deg"]!, 10);
        foreach (string unit in new[] { "min", "kg", "N/mm^2", "mm^4" })
        {
            var dto = Map(2d, unit);
            Assert.Equal(2d, (double)dto["value_internal"]!);
            Assert.Equal(2d, (double)dto["value_unitless"]!);
            Assert.Null(dto["value_mm"]);
        }
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Nonfinite_values_are_unavailable_instead_of_becoming_json_strings(double value)
    {
        var dto = Map(value, "mm");
        Assert.Equal(JTokenType.Null, dto["evaluated"]!.Type);
        AssertNoNumericFields(dto);
        Assert.DoesNotContain("NaN", dto.ToString());
        Assert.DoesNotContain("Infinity", dto.ToString());
    }

    [Fact]
    public void Unsupported_or_unavailable_getters_preserve_metadata_without_serializing_exception_data()
    {
        foreach (object? raw in new object?[] { null, 1, 1m, new object() })
            Assert.Equal(JTokenType.Null, Map(raw, "mm")["evaluated"]!.Type);
        var dto = ParameterValueDto.FromCore("p", "source + 2 mm", "mm", "user",
            () => throw new InvalidOperationException("private-path-and-data"));
        Assert.Equal("p", (string?)dto["name"]);
        Assert.Equal("source + 2 mm", (string?)dto["expression"]);
        Assert.Equal("mm", (string?)dto["unit"]);
        Assert.Equal("user", (string?)dto["kind"]);
        Assert.Equal(JTokenType.Null, dto["evaluated"]!.Type);
        Assert.DoesNotContain("private-path-and-data", dto.ToString());
        Assert.Null(Map(false, "boolean")["kind"]);
    }

    private static JObject Map(object? value, string unit) =>
        ParameterValueDto.FromCore("p", "expression unchanged", unit, null, () => value);
    private static void AssertNoNumericFields(JObject dto)
    {
        foreach (string field in new[] { "value_mm", "value_mm2", "value_mm3", "value_deg", "value_unitless", "value_internal" })
            Assert.Null(dto[field]);
    }
}
