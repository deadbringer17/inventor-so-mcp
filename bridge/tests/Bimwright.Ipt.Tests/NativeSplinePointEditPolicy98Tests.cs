using Bimwright.Ipt.Shared.Contracts;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Tests;

public sealed class NativeSplinePointEditPolicy98Tests
{
    private const string Doc="doc_fixture98",Revision="epoch:17";
    private static string Id(byte key,string type="sketch_entity",string document=Doc,byte[]? context=null)
        =>new PersistentEntityReference { DocumentId=document,EntityType=type,Key=new[]{key},Context=context??Array.Empty<byte>() }.Encode();
    private static JObject Reference(byte key,string type,string subpart)=>new()
    {
        ["document_id"]=Doc,["revision"]=Revision,["sketch_id"]=Id(1,"sketch"),
        ["entity_id"]=Id(key),["type"]=type,["subpart"]=subpart
    };
    private static JObject Options(string type="spline",string subpart="Point")=>new()
    { ["references"]=new JArray(Reference(2,type,"Entity"),Reference(3,"point",subpart)),["position_mm"]=new JArray(12.5,-7.25) };

    [Theory]
    [InlineData("spline","Point")] [InlineData("spline","Entity")]
    [InlineData("control_point_spline","Point")] [InlineData("control_point_spline","Entity")]
    public void OrderedNativePointEditIsFrozenWithoutChangingIdsOrUnits(string type,string subpart)
    {
        var options=Options(type,subpart);var request=NativeSplinePointEditPolicy98.Parse(options);
        options["position_mm"]![0]=999;options["references"]![1]!["entity_id"]=Id(99);
        Assert.Equal(12.5,request.Xmm);Assert.Equal(-7.25,request.Ymm);
        Assert.Equal(type,request.Curve.Type);Assert.Equal(subpart,request.Point.Subpart);
        Assert.Equal(Id(3),request.Point.EntityId);Assert.Equal(Id(2),request.Curve.EntityId);
        NativeSplinePointEditPolicy98.ValidateBinding(request,Doc,Revision,Id(1,"sketch"));
    }
    [Theory]
    [InlineData("tension")] [InlineData("remove_constraints")] [InlineData("closed")] [InlineData("index")]
    public void UnknownOptionsAreNotIgnored(string name)
    {var options=Options();options[name]=true;Assert.Throws<ArgumentException>(()=>NativeSplinePointEditPolicy98.Parse(options));}
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(3)]
    public void ExactlyTwoOrderedReferencesAreRequired(int count)
    {
        var options=Options();options["references"]=new JArray(Enumerable.Range(0,count).Select(i=>Reference((byte)(i+2),"point","Point")));
        Assert.Throws<ArgumentException>(()=>NativeSplinePointEditPolicy98.Parse(options));
    }
    [Fact]
    public void PointThenCurveIsNotReinterpretedAsTheOrderedContract()
    {
        var options=Options();options["references"]=new JArray(Reference(3,"point","Point"),Reference(2,"spline","Entity"));
        Assert.Throws<ArgumentException>(()=>NativeSplinePointEditPolicy98.Parse(options));
    }
    [Theory]
    [InlineData("line")] [InlineData("offset_spline")] [InlineData("spline_handle")]
    public void OtherCurvesAndHandlesAreNotSplinePointEdits(string type)
    {Assert.Throws<ArgumentException>(()=>NativeSplinePointEditPolicy98.Parse(Options(type)));}
    [Theory]
    [InlineData("Start")] [InlineData("End")] [InlineData("Center")]
    public void PointReferenceCannotBeReplacedByASubpartGuess(string subpart)
    {Assert.Throws<ArgumentException>(()=>NativeSplinePointEditPolicy98.Parse(Options("spline",subpart)));}
    [Fact]
    public void WholeSplineReferenceCannotBeAnEndpointSubpart()
    {var o=Options();o["references"]![0]!["subpart"]="Start";Assert.Throws<ArgumentException>(()=>NativeSplinePointEditPolicy98.Parse(o));}
    [Theory]
    [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)] [InlineData(double.NegativeInfinity)]
    public void NonFiniteDestinationIsRefused(double value)
    {var o=Options();o["position_mm"]![1]=value;Assert.Throws<ArgumentException>(()=>NativeSplinePointEditPolicy98.Parse(o));}
    [Theory]
    [InlineData("0")] [InlineData(true)] [InlineData(null)]
    public void CoordinatesAreNotCoercedFromStringsBooleansOrNull(object? value)
    {var o=Options();o["position_mm"]![0]=value==null?JValue.CreateNull():JToken.FromObject(value);Assert.Throws<ArgumentException>(()=>NativeSplinePointEditPolicy98.Parse(o));}
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(3)]
    public void DestinationMustBeExactlyPlanar(int count)
    {var o=Options();o["position_mm"]=new JArray(Enumerable.Range(0,count));Assert.Throws<ArgumentException>(()=>NativeSplinePointEditPolicy98.Parse(o));}
    [Theory]
    [InlineData("document_id")] [InlineData("revision")] [InlineData("sketch_id")]
    [InlineData("entity_id")] [InlineData("type")] [InlineData("subpart")]
    public void BothTypedReferencesRequireEveryField(string field)
    {
        foreach(int index in new[]{0,1})
        {var o=Options();((JObject)o["references"]![index]!).Remove(field);Assert.Throws<ArgumentException>(()=>NativeSplinePointEditPolicy98.Parse(o));}
    }
    [Fact]
    public void UnknownTypedReferenceFieldIsRefused()
    {var o=Options();o["references"]![1]!["point_index"]=1;Assert.Throws<ArgumentException>(()=>NativeSplinePointEditPolicy98.Parse(o));}
    [Theory]
    [InlineData(" ")] [InlineData("invalid\ntext")]
    public void ReferenceTextCannotBeBlankOrContainControls(string value)
    {var o=Options();o["references"]![1]!["revision"]=value;Assert.Throws<ArgumentException>(()=>NativeSplinePointEditPolicy98.Parse(o));}
    [Fact]
    public void ForgedOpaqueDocumentIsRefusedEvenWhenTypedFieldsAgree()
    {var o=Options();o["references"]![1]!["entity_id"]=Id(3,document:"doc_foreign");Assert.Throws<ArgumentException>(()=>NativeSplinePointEditPolicy98.Parse(o));}
    [Fact]
    public void WrongOpaqueKindOrKeyContextCannotBeUsedAsANativePoint()
    {
        foreach(string id in new[]{Id(3,"sketch"),Id(3,context:new byte[]{7}),"ent_invalid"})
        {var o=Options();o["references"]![1]!["entity_id"]=id;Assert.Throws<ArgumentException>(()=>NativeSplinePointEditPolicy98.Parse(o));}
    }
    [Fact]
    public void DifferentSketchesAreRefusedWithoutInferringPointOwnership()
    {var o=Options();o["references"]![1]!["sketch_id"]=Id(9,"sketch");Assert.Throws<ArgumentException>(()=>NativeSplinePointEditPolicy98.Parse(o));}
    [Fact]
    public void CurveAndPointCannotAliasTheSameOpaqueKey()
    {var o=Options();o["references"]![1]!["entity_id"]=Id(2);Assert.Throws<ArgumentException>(()=>NativeSplinePointEditPolicy98.Parse(o));}
    [Theory]
    [InlineData(0)] [InlineData(1)]
    public void EitherReferenceRevisionMustBeFreshAtBindingTime(int index)
    {
        var o=Options();o["references"]![index]!["revision"]="epoch:16";
        // Parse preserves both tokens, allowing the API helper to emit the
        // structured STALE_REVISION refusal before native resolution or MoveTo.
        var request=NativeSplinePointEditPolicy98.Parse(o);
        Assert.Throws<ArgumentException>(()=>NativeSplinePointEditPolicy98.ValidateBinding(request,Doc,Revision,Id(1,"sketch")));
    }
    [Theory]
    [InlineData("doc_foreign","epoch:17",1)]
    [InlineData("doc_fixture98","epoch:18",1)]
    [InlineData("doc_fixture98","epoch:17",9)]
    [InlineData("doc_fixture98","",1)]
    public void BindingRejectsWrongDocumentSketchOrCurrentRevision(string document,string revision,byte key)
    {var request=NativeSplinePointEditPolicy98.Parse(Options());Assert.Throws<ArgumentException>(()=>NativeSplinePointEditPolicy98.ValidateBinding(request,document,revision,Id(key,"sketch")));}
}
