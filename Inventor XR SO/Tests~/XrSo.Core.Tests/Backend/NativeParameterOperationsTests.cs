using InventorXrSo.Core.Backend;
using Newtonsoft.Json.Linq;

namespace InventorXrSo.Core.Tests.Backend;

public class NativeParameterOperationsTests
{
    [Fact]
    public void Native_expressions_are_preserved_without_numeric_conversion_or_implicit_execution()
    {
        const string expression = "  source_length + 2.5 cm  ";
        var set = NativeParameterOperations.SetExpression("lunghezza", expression);
        Assert.Equal("set_parameter", (string?)set["command"]);
        var args = Assert.IsType<JObject>(set["arguments"]);
        Assert.Equal("lunghezza", (string?)args["name"]);
        Assert.Equal(expression, (string?)args["value"]);
        Assert.Equal(new[] { "name", "value" }, args.Properties().Select(p => p.Name));
        Assert.Null(set["preview"]);
        Assert.Null(set["document_id"]);
    }

    [Fact]
    public void Create_rename_delete_match_the_existing_wire_vocabulary()
    {
        var create = NativeParameterOperations.Create("pressione", "forza / area", "N/mm^2");
        Assert.Equal("create_parameter", (string?)create["command"]);
        Assert.Equal("forza / area", (string?)create["arguments"]!["expression"]);
        Assert.Equal("N/mm^2", (string?)create["arguments"]!["unit"]);
        var rename = NativeParameterOperations.Rename("pressione", "pressione_massima");
        Assert.Equal("rename_parameter", (string?)rename["command"]);
        Assert.Equal("pressione_massima", (string?)rename["arguments"]!["new_name"]);
        var delete = NativeParameterOperations.Delete("pressione_massima");
        Assert.Equal("delete_parameter", (string?)delete["command"]);
        Assert.Single(Assert.IsType<JObject>(delete["arguments"]));
    }

    [Theory]
    [InlineData("True")]
    [InlineData("\"acciaio\"")]
    [InlineData("\"testo & <tag>\"")]
    [InlineData("\"\u03b1\U0001F527\"")]
    public void Non_numeric_native_expression_strings_remain_opaque(string expression)
    {
        var dto = NativeParameterOperations.SetExpression("p", expression);
        Assert.Equal(expression, (string?)JObject.Parse(dto.ToString())["arguments"]!["value"]);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("bad\0name")]
    [InlineData("bad\nname")]
    public void Invalid_names_are_refused_before_building_a_batch(string name)
    {
        Assert.Throws<ArgumentException>(() => NativeParameterOperations.SetExpression(name, "1 mm"));
        Assert.Throws<ArgumentException>(() => NativeParameterOperations.Create(name, "1 mm", "mm"));
        Assert.Throws<ArgumentException>(() => NativeParameterOperations.Rename("p", name));
        Assert.Throws<ArgumentException>(() => NativeParameterOperations.Delete(name));
    }

    [Fact]
    public void Lone_surrogates_in_names_are_refused_without_attribute_string_normalization()
    {
        foreach (char surrogate in new[] { (char)0xD800, (char)0xDC00 })
        {
            string name = new string(surrogate, 1);
            Assert.Throws<ArgumentException>(() => NativeParameterOperations.SetExpression(name, "1 mm"));
            Assert.Throws<ArgumentException>(() => NativeParameterOperations.Create(name, "1 mm", "mm"));
            Assert.Throws<ArgumentException>(() => NativeParameterOperations.Rename("p", name));
            Assert.Throws<ArgumentException>(() => NativeParameterOperations.Delete(name));
        }
    }

    [Fact]
    public void Budgets_and_control_validation_keep_the_wire_payload_bounded()
    {
        Assert.Throws<ArgumentException>(() => NativeParameterOperations.Delete(new string('p', NativeParameterOperations.MaxNameLength + 1)));
        Assert.Throws<ArgumentException>(() => NativeParameterOperations.SetExpression("p", new string('x', NativeParameterOperations.MaxExpressionLength + 1)));
        Assert.Throws<ArgumentException>(() => NativeParameterOperations.SetExpression("p", "1\0mm"));
        Assert.Throws<ArgumentException>(() => NativeParameterOperations.SetExpression("p", "\uD800"));
        Assert.Throws<ArgumentException>(() => NativeParameterOperations.Create("p", "1 mm", "\nmm"));
        Assert.Throws<ArgumentException>(() => NativeParameterOperations.Create("p", "1 mm", new string('u', NativeParameterOperations.MaxUnitLength + 1)));
        var accepted = NativeParameterOperations.SetExpression(new string('p', NativeParameterOperations.MaxNameLength),
            new string('x', NativeParameterOperations.MaxExpressionLength));
        Assert.Equal(NativeParameterOperations.MaxExpressionLength, ((string)accepted["arguments"]!["value"]!).Length);
    }

    [Fact]
    public void Returned_operations_have_no_shared_mutable_payload()
    {
        var first = NativeParameterOperations.Create("p", "source + 1 mm", "mm");
        var second = NativeParameterOperations.Create("p", "source + 1 mm", "mm");
        first["arguments"]!["expression"] = "changed";
        Assert.Equal("source + 1 mm", (string?)second["arguments"]!["expression"]);
    }
}
