using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Mcp;
using Newtonsoft.Json.Linq;

namespace InventorXrSo.Core.Tests.Backend;

public class SheetMetalContextTests
{
    private static readonly DocumentState State = new("doc", "r1", "v1");

    internal static JObject Info(bool flat = false, int bodies = 1) => JObject.Parse(@"{
      ""is_sheet_metal"":true,""rule"":""Default_mm"",""thickness_mm"":2.0,
      ""available_rules"":[{""rule"":""Default_mm"",""active"":true},{""rule"":""Alu_1mm"",""active"":false}],
      ""body_count"":" + bodies + @",""bend_count"":2,
      ""flat_pattern"":" + (flat ? @"{""exists"":true,""length_mm"":120.5,""width_mm"":40,""bend_count"":2,""alignment"":{""type"":""kHorizontalAlignment"",""reversed"":false}}" : @"{""exists"":false}") + "}");

    [Fact]
    public void ParsesRuleThicknessRulesCountsAndFlatPattern()
    {
        var c = SheetMetalContext.Parse(Info(true), State, State);
        Assert.True(c.IsSheetMetal); Assert.True(c.CanArmWrite); Assert.Null(c.Reason);
        Assert.Equal("Default_mm", c.Rule); Assert.Equal(2.0, c.ThicknessMm);
        Assert.Equal(new[] { "Default_mm", "Alu_1mm" }, c.AvailableRules);
        Assert.Equal(1, c.BodyCount); Assert.Equal(2, c.BendCount);
        Assert.True(c.FlatPattern.Exists); Assert.Equal(120.5, c.FlatPattern.LengthMm); Assert.Equal(40, c.FlatPattern.WidthMm);
        Assert.Equal(2, c.FlatPattern.BendCount); Assert.Equal("kHorizontalAlignment", c.FlatPattern.Alignment);
        Assert.True(c.IsCurrentFor(State)); Assert.False(c.IsCurrentFor(new("doc", "r2", "v")));
    }

    [Fact]
    public void ReportsAbsentFlatPattern()
    {
        var c = SheetMetalContext.Parse(Info(false), State);
        Assert.True(c.CanArmWrite); Assert.False(c.FlatPattern.Exists); Assert.Null(c.FlatPattern.LengthMm);
    }

    [Fact]
    public void OrdinaryPartIsCompleteButNotSheetMetal()
    {
        var c = SheetMetalContext.Parse(JObject.Parse(@"{""is_sheet_metal"":false,""sub_type"":""x""}"), State);
        Assert.False(c.IsSheetMetal); Assert.True(c.IsComplete); Assert.False(c.CanArmWrite); Assert.NotNull(c.Reason);
    }

    [Theory]
    [InlineData("rule")] [InlineData("thickness_mm")] [InlineData("body_count")] [InlineData("bend_count")]
    [InlineData("available_rules")] [InlineData("flat_pattern")]
    public void IncompleteReadNeverArmsAWrite(string missing)
    {
        var json = Info(true); json.Remove(missing);
        var c = SheetMetalContext.Parse(json, State);
        Assert.True(c.IsSheetMetal); Assert.False(c.IsComplete); Assert.False(c.CanArmWrite);
        Assert.StartsWith("Lettura lamiera incompleta", c.Reason);
    }

    [Fact]
    public void ExistingPatternWithoutMeasuresIsIncompleteAndBadNumbersAreRejected()
    {
        var json = Info(true); ((JObject)json["flat_pattern"]).Remove("length_mm");
        Assert.False(SheetMetalContext.Parse(json, State).CanArmWrite);
        var zero = Info(); zero["thickness_mm"] = 0; Assert.False(SheetMetalContext.Parse(zero, State).CanArmWrite);
        var neg = Info(); neg["body_count"] = -1; Assert.False(SheetMetalContext.Parse(neg, State).CanArmWrite);
        var text = Info(); text["thickness_mm"] = "2"; Assert.False(SheetMetalContext.Parse(text, State).CanArmWrite);
    }

    [Fact]
    public void StaleOrForeignReadsAreReadableAndNotWritable()
    {
        var moved = SheetMetalContext.Parse(Info(), State, new("doc", "r2", "v2"));
        Assert.False(moved.CanArmWrite); Assert.Contains("revisione", moved.Reason);
        var otherDoc = SheetMetalContext.Parse(Info(), State, new("other", "r1", "v1"));
        Assert.False(otherDoc.CanArmWrite);
        var named = Info(); named["revision"] = "r0";
        Assert.False(SheetMetalContext.Parse(named, State).CanArmWrite);
        var sameNamed = Info(); sameNamed["document_id"] = "doc"; sameNamed["revision"] = "r1";
        Assert.True(SheetMetalContext.Parse(sameNamed, State).CanArmWrite);
    }

    [Fact]
    public void MissingPayloadOrFlagYieldsReasonNotException()
    {
        Assert.Contains("assente", SheetMetalContext.Parse(null, State).Reason);
        var c = SheetMetalContext.Parse(new JObject(), State);
        Assert.False(c.CanArmWrite); Assert.False(c.IsSheetMetal); Assert.False(c.IsComplete);
        Assert.NotNull(SheetMetalContext.Unavailable(State, null).Reason);
    }

    [Fact]
    public void FlatPatternRefusalsMapToDistinctOutcomes()
    {
        FlatPatternException Map(string code, string reason) => FlatPatternException.From(new McpToolException("t", code, "m",
            new JObject { ["code"] = code, ["details"] = reason == null ? null : new JObject { ["reason"] = reason } }));
        Assert.Equal(FlatPatternFailure.Missing, Map("INVALID_ARGUMENT", "FLAT_PATTERN_MISSING").Failure);
        Assert.Equal(FlatPatternFailure.MultiBody, Map("INVALID_ARGUMENT", "MULTI_BODY_PART").Failure);
        Assert.Equal(FlatPatternFailure.WrongDocumentType, Map("WRONG_DOCUMENT_TYPE", null).Failure);
        Assert.Equal(FlatPatternFailure.TooLarge, Map("MESH_TOO_LARGE", null).Failure);
        Assert.Equal(FlatPatternFailure.Empty, Map("API_ERROR", "FLAT_PATTERN_MESH_EMPTY").Failure);
        Assert.Null(Map("STALE_REVISION", null));
        Assert.Null(FlatPatternException.From(new McpToolException("t", "INVALID_ARGUMENT", "m", null)));
    }
}
