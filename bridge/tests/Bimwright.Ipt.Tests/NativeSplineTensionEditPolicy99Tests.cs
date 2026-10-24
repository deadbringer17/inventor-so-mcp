using Bimwright.Ipt.Shared.Contracts;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Tests;

public sealed class NativeSplineTensionEditPolicy99Tests
{
    private const string Doc="doc_tension99",Rev="epoch:9";
    private static string Id(byte key,string kind="sketch_entity",string document=Doc)
        =>new PersistentEntityReference{DocumentId=document,EntityType=kind,Key=new[]{key}}.Encode();
    private static JObject Options(int tension=50)=>new(){["tension"]=tension,["references"]=new JArray(new JObject{
        ["document_id"]=Doc,["revision"]=Rev,["sketch_id"]=Id(1,"sketch"),["entity_id"]=Id(2),["type"]="spline",["subpart"]="Entity"})};

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(25)] [InlineData(50)] [InlineData(75)] [InlineData(99)] [InlineData(100)]
    public void ExplicitTensionAndIdentityAreFrozen(int value)
    {
        var options=Options(value);var request=NativeSplineTensionEditPolicy99.Parse(options);
        options["tension"]=0;options["references"]![0]!["entity_id"]=Id(3);
        Assert.Equal(value,request.Tension);Assert.Equal(Id(2),request.Curve.EntityId);
        NativeSplineTensionEditPolicy99.ValidateBinding(request,Doc,Rev,Id(1,"sketch"));
    }
    [Theory]
    [InlineData(-1)] [InlineData(101)] [InlineData(int.MinValue)] [InlineData(int.MaxValue)]
    public void TensionOutsideTheDeclaredDomainIsRefused(int value)
    {Assert.Throws<ArgumentException>(()=>NativeSplineTensionEditPolicy99.Parse(Options(value)));}
    [Theory]
    [InlineData("50")] [InlineData(50.0)] [InlineData(true)] [InlineData(null)]
    public void TensionIsNotCoerced(object? value)
    {var options=Options();options["tension"]=value==null?JValue.CreateNull():JToken.FromObject(value);Assert.Throws<ArgumentException>(()=>NativeSplineTensionEditPolicy99.Parse(options));}
    [Theory]
    [InlineData("fit_method")] [InlineData("closed")] [InlineData("position_mm")] [InlineData("remove_constraints")]
    public void UnknownMutationOptionsAreNotIgnored(string field)
    {var options=Options();options[field]=true;Assert.Throws<ArgumentException>(()=>NativeSplineTensionEditPolicy99.Parse(options));}
    [Theory]
    [InlineData("control_point_spline")] [InlineData("spline_handle")] [InlineData("line")]
    public void TensionRequiresAnActualFitSplineType(string type)
    {var options=Options();options["references"]![0]!["type"]=type;Assert.Throws<ArgumentException>(()=>NativeSplineTensionEditPolicy99.Parse(options));}
    [Theory]
    [InlineData("Start")] [InlineData("End")] [InlineData("Point")]
    public void AWholeCurveIsRequired(string subpart)
    {var options=Options();options["references"]![0]!["subpart"]=subpart;Assert.Throws<ArgumentException>(()=>NativeSplineTensionEditPolicy99.Parse(options));}
    [Fact]
    public void MultipleCurvesCannotBeAnImplicitMultiEdit()
    {var options=Options();((JArray)options["references"]!).Add(options["references"]![0]!.DeepClone());Assert.Throws<ArgumentException>(()=>NativeSplineTensionEditPolicy99.Parse(options));}
    [Theory]
    [InlineData("document_id")] [InlineData("revision")] [InlineData("sketch_id")]
    [InlineData("entity_id")] [InlineData("type")] [InlineData("subpart")]
    public void EveryTypedReferenceFieldIsRequired(string field)
    {var options=Options();((JObject)options["references"]![0]!).Remove(field);Assert.Throws<ArgumentException>(()=>NativeSplineTensionEditPolicy99.Parse(options));}
    [Fact]
    public void ForgedOpaqueOwnerIsRefused()
    {var options=Options();options["references"]![0]!["entity_id"]=Id(2,document:"doc_foreign");Assert.Throws<ArgumentException>(()=>NativeSplineTensionEditPolicy99.Parse(options));}
    [Fact]
    public void StaleRevisionIsPreservedUntilBindingForStructuredRefusal()
    {var options=Options();options["references"]![0]!["revision"]="epoch:8";var request=NativeSplineTensionEditPolicy99.Parse(options);Assert.Throws<ArgumentException>(()=>NativeSplineTensionEditPolicy99.ValidateBinding(request,Doc,Rev,Id(1,"sketch")));}
    [Theory]
    [InlineData("doc_other","epoch:9",1)] [InlineData(Doc,"epoch:10",1)]
    [InlineData(Doc,Rev,9)] [InlineData(Doc,"",1)]
    public void BindingCannotChangeDocumentSketchOrRevision(string doc,string revision,byte sketch)
    {var request=NativeSplineTensionEditPolicy99.Parse(Options());Assert.Throws<ArgumentException>(()=>NativeSplineTensionEditPolicy99.ValidateBinding(request,doc,revision,Id(sketch,"sketch")));}
}
