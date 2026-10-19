using System.Xml;
using System.Xml.Linq;
using Bimwright.Ipt.Shared.Contracts;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Ipt.Tests;

public sealed class SketchTextContentTests
{
    private static JObject Content(params JObject[] runs)=>new(){["schema"]=1,["runs"]=new JArray(runs)};
    [Fact]
    public void LiteralMarkupCannotIntroduceNativeFieldsOrExternalReferences()
    {
        const string literal="<Parameter ComponentIdentifier='C:/foreign.ipt' Name='X'>X</Parameter>&<!DOCTYPE x SYSTEM 'file:///secret'>";
        var content=SketchTextContent.Parse(Content(new JObject{["text"]=literal}));
        var document=XElement.Parse("<root>"+content.FormattedText+"</root>");
        Assert.Empty(document.Elements());Assert.Equal(literal,document.Value);Assert.Empty(content.ParameterNames);
    }
    [Fact]
    public void LocalParameterNamesAreEscapedAndReturnedForAuthoritativeResolution()
    {
        const string name="Length\"/><Property Document='foreign'>";
        var content=SketchTextContent.Parse(Content(new JObject{["text"]="L="},new JObject{["parameter_name"]=name},new JObject{["parameter_name"]=name}));
        var document=XElement.Parse("<root>"+content.FormattedText+"</root>");
        Assert.Equal(new[]{name},content.ParameterNames);
        Assert.Equal(2,document.Elements("Parameter").Count());
        foreach(var parameter in document.Elements())
        {Assert.Equal("Parameter",parameter.Name.LocalName);Assert.Equal(name,(string?)parameter.Attribute("Name"));Assert.Equal("0",(string?)parameter.Attribute("Precision"));Assert.Equal(2,parameter.Attributes().Count());Assert.Equal(name,parameter.Value);}
    }
    [Fact]
    public void LineBreaksAreNativeBreakNodesAndUnicodeIsPreserved()
    {
        var result=SketchTextContent.Parse(Content(new JObject{["text"]="Ø 12\r\nα\rβ\n🎯"}));
        var document=XElement.Parse("<root>"+result.FormattedText+"</root>");
        Assert.Equal(3,document.Elements("Br").Count());Assert.Equal("Ø 12αβ🎯",document.Value);
    }
    [Theory]
    [InlineData("raw_xml")][InlineData("external_reference")][InlineData("mixed")][InlineData("null")][InlineData("empty")][InlineData("too_long")][InlineData("too_many")][InlineData("control")][InlineData("invalid_surrogate")][InlineData("schema")][InlineData("schema_overflow")][InlineData("split_budget")][InlineData("parameter_control")]
    public void InvalidOrUnboundedContentIsRefusedBeforeNativeParsing(string kind)
    {
        var value=Content(new JObject{["text"]="text"});
        switch(kind)
        {
            case "raw_xml":value["runs"]=new JArray(new JObject{["formatted_text"]="<StyleOverride/>"});break;
            case "external_reference":value["runs"]=new JArray(new JObject{["parameter_name"]="Length",["component_identifier"]="foreign.ipt"});break;
            case "mixed":value["runs"]=new JArray(new JObject{["text"]="text",["parameter_name"]="Length"});break;
            case "null":value["runs"]=new JArray(new JObject{["text"]=null});break;
            case "empty":value["runs"]=new JArray(new JObject{["text"]=""});break;
            case "too_long":value["runs"]=new JArray(new JObject{["text"]=new string('a',4097)});break;
            case "too_many":value["runs"]=new JArray(Enumerable.Range(0,65).Select(_=>new JObject{["text"]="x"}));break;
            case "control":value["runs"]=new JArray(new JObject{["text"]="\u0000"});break;
            case "invalid_surrogate":value["runs"]=new JArray(new JObject{["text"]="\ud800"});break;
            case "schema":value["schema"]=2;break;
            case "schema_overflow":value["schema"]=long.MaxValue;break;
            case "split_budget":value["runs"]=new JArray(Enumerable.Range(0,64).Select(_=>new JObject{["text"]=new string('a',100)}));break;
            case "parameter_control":value["runs"]=new JArray(new JObject{["parameter_name"]="Length\n"});break;
        }
        var error=Record.Exception(()=>SketchTextContent.Parse(value));
        Assert.IsType<ArgumentException>(error);
    }
}
