using Bimwright.Ipt.Shared.Contracts;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Tests;

public sealed class NativeSketchCopyPolicyTests
{
    [Fact]
    public void TextBackgroundCannotBeSilentlyDroppedByTheLiteralCopyProfile()
        =>Assert.Throws<ArgumentException>(()=>NativeSketchCopyPolicy.ValidateTextProfile(true,true,0,1,1,4,true,true,true));
    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void NonfiniteNativeCopyCoordinatesCannotPassToleranceChecks(double value)
    {
        Assert.False(NativeSketchCopyPolicy.CopiedPointMatches(value,2,1,2));
        Assert.False(NativeSketchCopyPolicy.CopiedPointMatches(1,value,1,2));
        Assert.False(NativeSketchCopyPolicy.CopiedPointMatches(1,2,value,2));
        Assert.False(NativeSketchCopyPolicy.CopiedPointMatches(1,2,1,value));
    }
    [Fact]
    public void NativeCopyCoordinatesMustMatchBothAxesWithinMillimetreTolerance()
    {
        Assert.True(NativeSketchCopyPolicy.CopiedPointMatches(1+1e-6,2-1e-6,1,2));
        Assert.False(NativeSketchCopyPolicy.CopiedPointMatches(1+1e-4,2,1,2));
        Assert.False(NativeSketchCopyPolicy.CopiedPointMatches(1,2-1e-4,1,2));
    }
    private static JObject Reference(string id="line-a",string type="line")=>new() {
        ["document_id"]="doc-owned",["revision"]="rev-current",["sketch_id"]="sketch-owned",
        ["entity_id"]=id,["type"]=type,["subpart"]="Entity" };
    private static JObject Options(params JObject[] references)=>new() {
        ["references"]=new JArray(references),["delta_mm"]=new JArray(12d,-8d),["remove_constraints"]=false };
    [Fact]
    public void FreeLineAndLiteralTextSelectionIsFrozenInNativeOrder()
    {
        var line=Reference();var text=Reference("text-b","text");var options=Options(line,text);
        var request=NativeSketchCopyPolicy.Parse(options);
        line["entity_id"]="changed";text["type"]="arc";((JArray)options["delta_mm"]!)[0]=999;
        Assert.Equal(new[]{"line-a","text-b"},request.References.Select(r=>r.EntityId));
        Assert.Equal(new[]{"line","text"},request.References.Select(r=>r.Type));
        Assert.Equal(12,request.DeltaXmm);Assert.Equal(-8,request.DeltaYmm);
        NativeSketchCopyPolicy.ValidateBinding(request,"doc-owned","rev-current","sketch-owned");
    }
    [Theory]
    [InlineData("copy")][InlineData("angle_deg")][InlineData("center_mm")][InlineData("unknown")]
    public void UnsupportedOptionsCannotSilentlyAlterCopySemantics(string name)
    { var value=Options(Reference());value[name]=false;Assert.Throws<ArgumentException>(()=>NativeSketchCopyPolicy.Parse(value)); }
    [Theory]
    [InlineData(true)][InlineData("false")][InlineData(0)]
    public void ConstraintRemovalIsRefusedBeforeNativeResolution(object value)
    { var options=Options(Reference());options["remove_constraints"]=JToken.FromObject(value);Assert.Throws<ArgumentException>(()=>NativeSketchCopyPolicy.Parse(options)); }
    [Fact]
    public void OmittedConstraintRemovalMeansOnlyTheSupportedFalsePolicy()
    { var options=Options(Reference());options.Remove("remove_constraints");Assert.Single(NativeSketchCopyPolicy.Parse(options).References); }
    [Theory]
    [InlineData("image")][InlineData("spline_handle")][InlineData("spline")]
    public void LastUnsupportedMixedMemberRejectsTheWholeParsedSelection(string type)
    { Assert.Throws<ArgumentException>(()=>NativeSketchCopyPolicy.Parse(Options(Reference(),Reference("last",type)))); }
    [Theory]
    [InlineData("Start")][InlineData("End")][InlineData("Point")][InlineData("entity")]
    public void OnlyWholeObjectsCanBeCopied(string subpart)
    { var reference=Reference();reference["subpart"]=subpart;Assert.Throws<ArgumentException>(()=>NativeSketchCopyPolicy.Parse(Options(reference))); }
    [Theory]
    [InlineData("document_id")][InlineData("revision")][InlineData("sketch_id")][InlineData("entity_id")][InlineData("type")][InlineData("subpart")]
    public void MissingOrNonStringBindingFieldsRefuse(string field)
    {
        var missing=Reference();missing.Remove(field);Assert.Throws<ArgumentException>(()=>NativeSketchCopyPolicy.Parse(Options(missing)));
        var numeric=Reference();numeric[field]=1;Assert.Throws<ArgumentException>(()=>NativeSketchCopyPolicy.Parse(Options(numeric)));
    }
    [Fact]
    public void DuplicateIdentityAndSpoofedExtraFieldAreRefused()
    {
        Assert.Throws<ArgumentException>(()=>NativeSketchCopyPolicy.Parse(Options(Reference(),Reference())));
        var reference=Reference();reference["owner"]=true;Assert.Throws<ArgumentException>(()=>NativeSketchCopyPolicy.Parse(Options(reference)));
    }
    [Theory]
    [InlineData("foreign","rev-current","sketch-owned")]
    [InlineData("doc-owned","old","sketch-owned")]
    [InlineData("doc-owned","rev-current","other")]
    public void CurrentBindingIncludesDocumentRevisionAndSketch(string document,string revision,string sketch)
    { Assert.Throws<ArgumentException>(()=>NativeSketchCopyPolicy.ValidateBinding(NativeSketchCopyPolicy.Parse(Options(Reference())),document,revision,sketch)); }
    [Fact]
    public void MixedSecondForeignReferenceIsNotIgnored()
    { var last=Reference("text-b","text");last["document_id"]="foreign";var request=NativeSketchCopyPolicy.Parse(Options(Reference(),last));
        Assert.Throws<ArgumentException>(()=>NativeSketchCopyPolicy.ValidateBinding(request,"doc-owned","rev-current","sketch-owned")); }
    [Theory]
    [InlineData(double.NaN)][InlineData(double.PositiveInfinity)][InlineData(double.NegativeInfinity)]
    public void NonfiniteDeltaRefuses(double x)
    { var options=Options(Reference());options["delta_mm"]=new JArray(x,0);Assert.Throws<ArgumentException>(()=>NativeSketchCopyPolicy.Parse(options)); }
    [Fact]
    public void DeltaRequiresExactlyTwoNumbersAndANonzeroTranslation()
    {
        foreach(var delta in new[]{new JArray(0,0),new JArray(12),new JArray(12,-8,0),new JArray("12",-8)})
        {var options=Options(Reference());options["delta_mm"]=delta;Assert.Throws<ArgumentException>(()=>NativeSketchCopyPolicy.Parse(options));}
    }
    [Fact]
    public void NativeDestinationOverflowRefuses()
    {
        var options=Options(Reference());options["delta_mm"]=new JArray(double.MaxValue,1);
        Assert.Throws<ArgumentException>(()=>NativeSketchCopyPolicy.Translate(double.MaxValue,0,NativeSketchCopyPolicy.Parse(options)));
    }
    [Fact]
    public void TranslationRetainsFractionalAndNegativeMillimetres()
    { var p=NativeSketchCopyPolicy.Translate(-2.25,3.5,NativeSketchCopyPolicy.Parse(Options(Reference())));Assert.Equal(9.75,p.X);Assert.Equal(-4.5,p.Y); }
    [Theory]
    [InlineData("hello","hello")][InlineData("A&amp;B &lt;x&gt;","A&B <x>")][InlineData("&quot;è&quot; &#x03A9;","\"è\" Ω")]
    public void LiteralAndEscapedMarkupRemainLiteral(string markup,string decoded)
    { Assert.Equal(decoded,NativeSketchCopyPolicy.ValidateLiteralMarkup(markup)); }
    [Theory]
    [InlineData("<Parameter Resolved=\"True\" ComponentIdentifier=\"Parte1\" Name=\"L\" Precision=\"0\">10</Parameter>")]
    [InlineData("<Parameter Name=\"L\">L</Parameter>")][InlineData("<StyleOverride Font=\"Other\">A</StyleOverride>")]
    [InlineData("line<Br/>two")][InlineData("line\ntwo")][InlineData("line\rtwo")]
    [InlineData("<![CDATA[text]]>")][InlineData("<!-- comment -->text")][InlineData("<?pi data?>text")]
    [InlineData("&unknown;")][InlineData("<!DOCTYPE x [<!ENTITY v SYSTEM 'file:///secret'>]>&v;")]
    public void FieldsOverridesMultilineAndXmlDirectivesFailClosed(string markup)
    { Assert.Throws<ArgumentException>(()=>NativeSketchCopyPolicy.ValidateLiteralMarkup(markup)); }
    [Fact]
    public void ExistingLiteralBudgetIsAppliedAfterEntityExpansion()
    {
        Assert.Equal(4096,NativeSketchCopyPolicy.ValidateLiteralMarkup(string.Concat(Enumerable.Repeat("&amp;",4096))).Length);
        Assert.Throws<ArgumentException>(()=>NativeSketchCopyPolicy.ValidateLiteralMarkup(new string('a',4097)));
        Assert.Throws<ArgumentException>(()=>NativeSketchCopyPolicy.ValidateLiteralMarkup(""));
    }
    [Theory]
    [InlineData(true,true,0d,1.25d,1.2d,4d,true,true)]
    [InlineData(true,true,0d,.8d,2d,6.75d,true,true)]
    public void FittedProfileDoesNotHardcodeFixtureFontSizeOrOverrides(bool fitted,bool single,double rotation,double width,double spacing,double size,bool owned,bool enums)
    { NativeSketchCopyPolicy.ValidateTextProfile(fitted,single,rotation,width,spacing,size,owned,enums); }
    [Theory]
    [InlineData(false,true,0d,1d,1d,4d,true,true)]
    [InlineData(true,false,0d,1d,1d,4d,true,true)]
    [InlineData(true,true,.01d,1d,1d,4d,true,true)]
    [InlineData(true,true,0d,1d,1d,4d,false,true)]
    [InlineData(true,true,0d,1d,1d,4d,true,false)]
    [InlineData(true,true,0d,0d,1d,4d,true,true)]
    [InlineData(true,true,0d,1d,0d,4d,true,true)]
    [InlineData(true,true,0d,1d,1d,0d,true,true)]
    public void UnsupportedTextProfilesRejectBeforeCreator(bool fitted,bool single,double rotation,double width,double spacing,double size,bool owned,bool enums)
    { Assert.Throws<ArgumentException>(()=>NativeSketchCopyPolicy.ValidateTextProfile(fitted,single,rotation,width,spacing,size,owned,enums)); }
    [Theory]
    [InlineData(double.NaN)][InlineData(double.PositiveInfinity)]
    public void NonfiniteTextPropertiesRefuse(double value)
    {
        Assert.Throws<ArgumentException>(()=>NativeSketchCopyPolicy.ValidateTextProfile(true,true,value,1,1,4,true,true));
        Assert.Throws<ArgumentException>(()=>NativeSketchCopyPolicy.ValidateTextProfile(true,true,0,value,1,4,true,true));
        Assert.Throws<ArgumentException>(()=>NativeSketchCopyPolicy.ValidateTextProfile(true,true,0,1,value,4,true,true));
        Assert.Throws<ArgumentException>(()=>NativeSketchCopyPolicy.ValidateTextProfile(true,true,0,1,1,value,true,true));
    }
    [Theory]
    [InlineData(false,"line","start",true)][InlineData(false,"end","line",true)]
    [InlineData(true,"line","start",false)][InlineData(false,"line","foreign",false)]
    [InlineData(false,"start","end",false)][InlineData(false,"other-line","start",false)]
    public void OnlyExactNondeletableCurveOwnEndpointIncidenceIsIntrinsic(bool deletable,string one,string two,bool expected)
    { Assert.Equal(expected,NativeSketchCopyPolicy.IsIntrinsicLineCoincidence(deletable,one,two,"line","start","end")); }
    [Fact]
    public void AmbiguousOwnEndpointIdentitiesCannotAuthorizeFreeLine()
    { Assert.False(NativeSketchCopyPolicy.IsIntrinsicLineCoincidence(false,"line","same","line","same","same")); }
    [Fact]
    public void SelectionCeilingIsExplicitAndNeverSilentlyTruncates()
    {
        var options=Options(Enumerable.Range(0,2000).Select(i=>Reference("line-"+i)).ToArray());
        Assert.Equal(2000,NativeSketchCopyPolicy.Parse(options).References.Count);
        ((JArray)options["references"]!).Add(Reference("overflow"));
        Assert.Throws<ArgumentException>(()=>NativeSketchCopyPolicy.Parse(options));
    }
    [Fact]
    public void OnlyIndependentFreeLinesAreAdmitted()
    {
        NativeSketchCopyPolicy.ValidateLineProfile(false,false,false,0,0,0,false,false);
        Assert.Throws<ArgumentException>(()=>NativeSketchCopyPolicy.ValidateLineProfile(true,false,false,0,0,0,false,false));
        Assert.Throws<ArgumentException>(()=>NativeSketchCopyPolicy.ValidateLineProfile(false,true,false,0,0,0,false,false));
        Assert.Throws<ArgumentException>(()=>NativeSketchCopyPolicy.ValidateLineProfile(false,false,true,0,0,0,false,false));
        Assert.Throws<ArgumentException>(()=>NativeSketchCopyPolicy.ValidateLineProfile(false,false,false,1,0,0,false,false));
        Assert.Throws<ArgumentException>(()=>NativeSketchCopyPolicy.ValidateLineProfile(false,false,false,0,1,0,false,false));
        Assert.Throws<ArgumentException>(()=>NativeSketchCopyPolicy.ValidateLineProfile(false,false,false,0,0,1,false,false));
        Assert.Throws<ArgumentException>(()=>NativeSketchCopyPolicy.ValidateLineProfile(false,false,false,0,0,0,true,false));
        Assert.Throws<ArgumentException>(()=>NativeSketchCopyPolicy.ValidateLineProfile(false,false,false,0,0,0,false,true));
    }
}
