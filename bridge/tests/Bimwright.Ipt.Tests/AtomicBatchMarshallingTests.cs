using System.Text.Json;
using Bimwright.Ipt.Server.Tools;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Tests;

/// <summary>
/// The MCP host binds tool arguments with System.Text.Json and the add-in speaks Newtonsoft, so every
/// batch step crosses a serializer boundary. Handing a bound <c>JsonElement</c> straight to Newtonsoft
/// serializes the struct's own public surface — {"ValueKind": N} — and every argument value is lost
/// while the call still looks well-formed. These tests bind exactly as the host does and assert the
/// values survive.
/// </summary>
public sealed class AtomicBatchMarshallingTests
{
    private static AtomicOperation[] Bind(string json)
        => JsonSerializer.Deserialize<AtomicOperation[]>(json)!;

    private static JArray Wire(string json)
    {
        Assert.True(ParameterTools.TryBuildOperations(Bind(json), out var wire, out var rejection),
            rejection?.ToString());
        return wire;
    }

    [Fact]
    public void StringArgumentsReachTheWireAsStrings()
    {
        var wire = Wire("""[{"command":"set_parameter","arguments":{"name":"length","value":"25 mm"}}]""");
        var arguments = (JObject)wire[0]["arguments"]!;
        Assert.Equal("set_parameter", (string?)wire[0]["command"]);
        Assert.Equal("length", (string?)arguments["name"]);
        Assert.Equal("25 mm", (string?)arguments["value"]);
        Assert.DoesNotContain("ValueKind", wire.ToString());
    }

    [Fact]
    public void NumbersBooleansArraysAndNestedObjectsAllSurvive()
    {
        var wire = Wire("""
            [{"command":"draw_rectangle","arguments":{"x1":-10.5,"y1":0,"x2":10,"y2":7,"sketch_name":"Sketch1"}},
             {"command":"hole","arguments":{"face_id":"body:1/face:3","points_mm":[[1,2],[3,4]],"kind":"drilled",
                                            "diameter_mm":6.6,"through":true}},
             {"command":"sheet_metal_punch","arguments":{"punch":"Round","sketch_name":"S","parameters":{"Diameter":"5 mm"}}}]
            """);

        var rectangle = (JObject)wire[0]["arguments"]!;
        Assert.Equal(-10.5, (double?)rectangle["x1"]);
        Assert.Equal(JTokenType.Float, rectangle["x1"]!.Type);
        Assert.Equal("Sketch1", (string?)rectangle["sketch_name"]);

        var hole = (JObject)wire[1]["arguments"]!;
        Assert.Equal(6.6, (double?)hole["diameter_mm"]);
        Assert.True((bool?)hole["through"]);
        var points = Assert.IsType<JArray>(hole["points_mm"]);
        Assert.Equal(2, points.Count);
        Assert.Equal(3, (double?)points[1]![0]);

        var punch = (JObject)wire[2]["arguments"]!;
        Assert.Equal("5 mm", (string?)punch["parameters"]!["Diameter"]);
    }

    [Fact]
    public void AnArgumentLessCommandSendsAnEmptyObject()
    {
        foreach (var json in new[]
                 {
                     """[{"command":"close_sketch"}]""",
                     """[{"command":"close_sketch","arguments":null}]""",
                     """[{"command":"close_sketch","arguments":{}}]""",
                 })
        {
            var arguments = Assert.IsType<JObject>(Wire(json)[0]["arguments"]);
            Assert.Empty(arguments);
        }
    }

    [Fact]
    public void ArgumentsSentAsAJsonStringAreAccepted()
    {
        // Some clients cannot nest an object inside a tool call and stringify it instead. That is a
        // transport detail, not a bad plan, so the step is unwrapped rather than refused.
        var arguments = (JObject)Wire("""[{"command":"set_parameter","arguments":"{\"name\":\"length\",\"value\":\"25 mm\"}"}]""")[0]["arguments"]!;
        Assert.Equal("length", (string?)arguments["name"]);
        Assert.Equal("25 mm", (string?)arguments["value"]);
    }

    [Theory]
    [InlineData("""[{"command":"set_parameter","arguments":[1,2]}]""", "array")]
    [InlineData("""[{"command":"set_parameter","arguments":7}]""", "number")]
    [InlineData("""[{"command":"set_parameter","arguments":true}]""", "true")]
    public void ANonObjectArgumentIsRefusedByStepAndName(string json, string kind)
    {
        Assert.False(ParameterTools.TryBuildOperations(Bind(json), out _, out var rejection));
        Assert.Equal("INVALID_ARGUMENT", (string?)rejection!["error"]!["code"]);
        Assert.Equal(0, (int?)rejection["error"]!["details"]!["step_index"]);
        Assert.Equal("set_parameter", (string?)rejection["error"]!["details"]!["command"]);
        Assert.Contains(kind, (string?)rejection["error"]!["message"]);
    }

    [Fact]
    public void AStringThatIsNotAnObjectIsRefusedRatherThanCrashing()
    {
        Assert.False(ParameterTools.TryBuildOperations(
            Bind("""[{"command":"close_sketch","arguments":{}},{"command":"set_parameter","arguments":"length=25"}]"""),
            out _, out var rejection));
        Assert.Equal("INVALID_ARGUMENT", (string?)rejection!["error"]!["code"]);
        Assert.Equal(1, (int?)rejection["error"]!["details"]!["step_index"]);
        Assert.Contains("JSON object", (string?)rejection["error"]!["message"]);
    }
}
