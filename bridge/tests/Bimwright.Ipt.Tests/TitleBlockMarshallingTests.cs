using System.Text.Json;
using Bimwright.Ipt.Server.Tools;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Tests;

public sealed class TitleBlockMarshallingTests
{
    private static JsonElement Element(string json)
        => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void JsonObjectReachesTheAddinAsAnObject()
    {
        Assert.True(SafeArtifactTools.TryReadTitleBlock(
            Element("""{"Title":"Flangia","Revision":3,"Approved":true}"""),
            out var value, out var rejection), rejection);
        var fields = Assert.IsType<JObject>(value);
        Assert.Equal("Flangia", (string?)fields["Title"]);
        Assert.Equal(3, (int?)fields["Revision"]);
        Assert.True((bool?)fields["Approved"]);
    }

    [Fact]
    public void LegacyJsonStringRemainsAccepted()
    {
        Assert.True(SafeArtifactTools.TryReadTitleBlock(
            Element(JsonSerializer.Serialize("{\"Title\":\"Flangia\"}")),
            out var value, out var rejection), rejection);
        Assert.Equal("Flangia", (string?)value!["Title"]);
    }

    [Fact]
    public void NullAndMissingMeanNoFields()
    {
        Assert.True(SafeArtifactTools.TryReadTitleBlock(null, out var missing, out _));
        Assert.Null(missing);
        Assert.True(SafeArtifactTools.TryReadTitleBlock(Element("null"), out var explicitNull, out _));
        Assert.Null(explicitNull);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("7")]
    [InlineData("true")]
    public void NonObjectsAreRejected(string json)
    {
        Assert.False(SafeArtifactTools.TryReadTitleBlock(Element(json), out _, out var rejection));
        Assert.Contains("JSON object", rejection);
    }

    [Fact]
    public void MalformedLegacyStringIsRejected()
    {
        Assert.False(SafeArtifactTools.TryReadTitleBlock(Element("\"Title=Flangia\""), out _, out var rejection));
        Assert.Contains("valid JSON", rejection);
    }
}
