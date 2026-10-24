using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Tests;

public sealed class OwnedUntouchedParameterRelocation95Tests
{
    private const string Before=@"C:\owned\original.ipt",After=@"C:\owned\reopened.ipt";
    private const string Internal="{0E7BE4EA-4BCC-FA65-A38C-829F40D8BBE7}";
    private static JObject Document(string path)=>new()
    {
        ["document_id"]="doc_"+Internal,["internal_name"]=Internal,["path"]=path,
        ["parameters"]=new JArray(new JObject{["key"]="parameter-key",["name"]="S1QuestLength73",["expression"]="10 mm",["unit"]="mm",["kind"]="kUserParameter",["native_value"]=1d}),
        ["sketches"]=new JArray(new JObject{["name"]="S1_TextRead",["key"]="sketch-key",["texts"]=new JArray(new JObject{
            ["key"]="text-key",["native_type"]="kTextBoxObject",["text"]="L=10",["glyph_coordinates_sha256"]="unchanged",
            ["formatted_text"]=$"L=<Parameter Resolved='True' ComponentIdentifier='{path}' Name='S1QuestLength73' Precision='0'>10</Parameter>"})}),
        ["bodies"]=new JArray(new JObject{["volume_mm3"]=100d})
    };
    [Fact]
    public void RelocatesOnlyTheAttestedFieldWithoutMutatingRawDocument()
    {
        var before=Document(Before);var after=Document(After);string raw=before.ToString(Formatting.None);
        var binding=OwnedUntouchedParameterRelocation95.Attest(before,Before);
        var a=OwnedUntouchedParameterRelocation95.Normalize(before,Before,binding);
        var b=OwnedUntouchedParameterRelocation95.Normalize(after,After,binding);
        Assert.Equal(raw,before.ToString(Formatting.None));Assert.Equal(4,binding.Json().Count);
        // Path is a separate document metadata difference; only the markup is canonicalized.
        Assert.Equal(a["sketches"]!.ToString(Formatting.None),b["sketches"]!.ToString(Formatting.None));
        Assert.Equal(Before,(string?)a["path"]);Assert.Equal(After,(string?)b["path"]);
        Assert.Equal(before["bodies"]!.ToString(Formatting.None),a["bodies"]!.ToString(Formatting.None));
        Assert.Equal(before["parameters"]!.ToString(Formatting.None),a["parameters"]!.ToString(Formatting.None));
    }
    [Theory]
    [InlineData("foreign_owner")][InlineData("parameter_name")][InlineData("parameter_value")]
    [InlineData("parameter_key")][InlineData("parameter_expression")][InlineData("parameter_unit")]
    [InlineData("parameter_kind")][InlineData("sketch_key")][InlineData("text_key")]
    [InlineData("display_value")][InlineData("resolved")][InlineData("precision")]
    [InlineData("extra_attribute")][InlineData("extra_field")][InlineData("prefix")]
    [InlineData("xml_entity")][InlineData("document_id")][InlineData("duplicate_parameter")]
    public void RefusesChangedBindingsValuesAndGrammars(string mutation)
    {
        var source=Document(Before);var binding=OwnedUntouchedParameterRelocation95.Attest(source,Before);var after=Document(After);
        var parameter=(JObject)after["parameters"]![0]!;var sketch=(JObject)after["sketches"]![0]!;var text=(JObject)sketch["texts"]![0]!;
        string markup=(string)text["formatted_text"]!;
        switch(mutation)
        {
            case "foreign_owner":markup=markup.Replace(After,@"C:\external\other.ipt");break;
            case "parameter_name":markup=markup.Replace("S1QuestLength73","OtherParameter");break;
            case "parameter_value":parameter["native_value"]=2d;break;
            case "parameter_key":parameter["key"]="foreign-parameter-key";break;
            case "parameter_expression":parameter["expression"]="5 mm + 5 mm";break;
            case "parameter_unit":parameter["unit"]="cm";break;
            case "parameter_kind":parameter["kind"]="kModelParameter";break;
            case "sketch_key":sketch["key"]="foreign-sketch-key";break;
            case "text_key":text["key"]="foreign-text-key";break;
            case "display_value":text["text"]="L=11";markup=markup.Replace(">10<",">11<");break;
            case "resolved":markup=markup.Replace("Resolved='True'","Resolved='False'");break;
            case "precision":markup=markup.Replace("Precision='0'","Precision='1'");break;
            case "extra_attribute":markup=markup.Replace("Precision='0'","Precision='0' Extra='x'");break;
            case "extra_field":((JArray)sketch["texts"]!).Add(text.DeepClone());break;
            case "prefix":markup="Other="+markup.Substring(2);break;
            case "xml_entity":markup="<!DOCTYPE owner [<!ENTITY p SYSTEM 'file:///C:/private'>]>"+markup;break;
            case "document_id":after["document_id"]="doc_{11111111-1111-1111-1111-111111111111}";break;
            case "duplicate_parameter":((JArray)after["parameters"]!).Add(parameter.DeepClone());break;
        }
        text["formatted_text"]=markup;
        Assert.ThrowsAny<Exception>(()=>OwnedUntouchedParameterRelocation95.Normalize(after,After,binding));
    }
    [Fact]
    public void PreservesOtherLiteralTextInTheSameNativeSketch()
    {
        var before=Document(Before);var after=Document(After);
        foreach(var document in new[]{before,after})((JArray)document["sketches"]![0]!["texts"]!).Add(new JObject{
            ["key"]="literal-text-key",["native_type"]="kTextBoxObject",["text"]="Quest73",["formatted_text"]="Quest73"});
        var binding=OwnedUntouchedParameterRelocation95.Attest(before,Before);
        var normalized=OwnedUntouchedParameterRelocation95.Normalize(after,After,binding);
        Assert.Equal(2,((JArray)normalized["sketches"]![0]!["texts"]!).Count);
        Assert.Equal("Quest73",(string?)normalized["sketches"]![0]!["texts"]![1]!["formatted_text"]);
    }
    [Fact]
    public void OtherNativeChangesArePreservedForTheExactOuterComparison()
    {
        var before=Document(Before);var binding=OwnedUntouchedParameterRelocation95.Attest(before,Before);var after=Document(After);
        after["bodies"]![0]!["volume_mm3"]=101d;after["sketches"]![0]!["texts"]![0]!["glyph_coordinates_sha256"]="changed";
        var normalized=OwnedUntouchedParameterRelocation95.Normalize(after,After,binding);
        Assert.Equal(101d,(double)normalized["bodies"]![0]!["volume_mm3"]!);
        Assert.Equal("changed",(string?)normalized["sketches"]![0]!["texts"]![0]!["glyph_coordinates_sha256"]);
    }
}
