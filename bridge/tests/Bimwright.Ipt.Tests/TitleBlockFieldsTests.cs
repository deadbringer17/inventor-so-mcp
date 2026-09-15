using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using Bimwright.Ipt.Shared.Infrastructure;

namespace Bimwright.Ipt.Tests;

public sealed class TitleBlockFieldsTests
{
    [Fact]
    public void AnAbsentTitleBlockIsNoFieldsAtAll()
    {
        Assert.Empty(TitleBlockFields.Parse(null));
        Assert.Empty(TitleBlockFields.Parse(JValue.CreateNull()));
        Assert.Empty(TitleBlockFields.Parse(new JObject()));
    }

    [Fact]
    public void FieldsKeepTheirNameAndOrder()
    {
        var fields = TitleBlockFields.Parse(JObject.Parse(
            @"{ ""Title"": ""Flangia DN50"", ""Designer"": ""S.O."", ""Commessa"": ""24-118"" }"));
        Assert.Equal(new[] { "Title", "Designer", "Commessa" }, fields.Select(f => f.Name));
        Assert.Equal("Flangia DN50", fields[0].Value);
    }

    [Fact]
    public void NamesAreTrimmedButNotOtherwiseRewritten()
    {
        var fields = TitleBlockFields.Parse(JObject.Parse(@"{ ""  Disegnato Da  "": ""SO"" }"));
        Assert.Equal("Disegnato Da", fields[0].Name);
    }

    [Fact]
    public void NumbersAndBooleansAreFormattedInvariantly()
    {
        var fields = TitleBlockFields.Parse(JObject.Parse(@"{ ""Revision"": 3, ""Scale"": 2.5, ""Approved"": true }"));
        Assert.Equal("3", fields[0].Value);
        Assert.Equal("2.5", fields[1].Value);   // never "2,5", whatever the host locale
        Assert.Equal("true", fields[2].Value);
    }

    [Fact]
    public void NullClearsTheFieldRatherThanSkippingIt()
    {
        var fields = TitleBlockFields.Parse(JObject.Parse(@"{ ""Title"": null }"));
        Assert.Single(fields);
        Assert.Equal("", fields[0].Value);
    }

    [Fact]
    public void ANonObjectTitleBlockIsRefused()
    {
        Assert.Throws<ArgumentException>(() => TitleBlockFields.Parse(JToken.Parse(@"[""Title""]")));
        Assert.Throws<ArgumentException>(() => TitleBlockFields.Parse(JToken.Parse(@"""Title=x""")));
    }

    [Fact]
    public void NestedValuesAreRefused()
        => Assert.Throws<ArgumentException>(() => TitleBlockFields.Parse(JObject.Parse(@"{ ""Title"": { ""x"": 1 } }")));

    [Fact]
    public void DuplicateNamesAreRefusedCaseInsensitively()
    {
        // Both resolve to the same iProperty, so silently keeping the last one would make the
        // written value depend on JSON ordering.
        var o = new JObject { ["Title"] = "a", ["title"] = "b" };
        Assert.Throws<ArgumentException>(() => TitleBlockFields.Parse(o));
    }

    [Fact]
    public void EmptyAndOverlongNamesAreRefused()
    {
        Assert.Throws<ArgumentException>(() => TitleBlockFields.Parse(new JObject { ["   "] = "x" }));
        Assert.Throws<ArgumentException>(() =>
            TitleBlockFields.Parse(new JObject { [new string('n', TitleBlockFields.MaxNameLength + 1)] = "x" }));
    }

    [Fact]
    public void OverlongValuesAreRefusedAtTheIPropertyLimit()
    {
        var ok = new JObject { ["Title"] = new string('v', TitleBlockFields.MaxValueLength) };
        Assert.Single(TitleBlockFields.Parse(ok));
        var tooLong = new JObject { ["Title"] = new string('v', TitleBlockFields.MaxValueLength + 1) };
        Assert.Throws<ArgumentException>(() => TitleBlockFields.Parse(tooLong));
    }

    [Fact]
    public void ControlCharactersAreRefusedInNamesAndValues()
    {
        Assert.Throws<ArgumentException>(() => TitleBlockFields.Parse(new JObject { ["Ti\ttle"] = "x" }));
        Assert.Throws<ArgumentException>(() => TitleBlockFields.Parse(new JObject { ["Title"] = "line\nbreak" }));
    }

    [Fact]
    public void MoreThanThirtyFieldsAreRefused()
    {
        var o = new JObject();
        for (int i = 0; i <= TitleBlockFields.MaxFields; i++) o["Field" + i] = "v";
        Assert.Throws<ArgumentException>(() => TitleBlockFields.Parse(o));
    }
}
