using Bimwright.Ipt.Shared.Contracts;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Tests;

public sealed class NativeSplineCreationPolicy96Tests
{
    private static JObject Options(int count=3)=>new() { ["points_mm"]=new JArray(Enumerable.Range(0,count).Select(i=>new JArray(i*10,i%2*7))),["fit_method"]=26369 };
    [Theory]
    [InlineData(26369)] [InlineData(26370)] [InlineData(26371)]
    public void FitClosureKeepsInputAndFrozenNativeMethod(int method)
    {
        var input=Options();input["closed"]=true;input["fit_method"]=method;
        var request=NativeSplineCreationPolicy96.Parse(input,false);
        Assert.True(request.Closed);Assert.Equal(method,request.FitMethod);Assert.Equal(3,request.PointsMm.Count);
        input["points_mm"]![0]![0]=999;
        Assert.Equal((0d,0d),request.PointsMm[0]);
    }
    [Theory]
    [InlineData(2)] [InlineData(3)] [InlineData(5)] [InlineData(64)]
    public void ControlClosureRepeatsOnlyTheFirstPointAndKeepsRequestBudget(int count)
    {
        var input=Options(count);input.Remove("fit_method");input["closed"]=true;
        var request=NativeSplineCreationPolicy96.Parse(input,true);
        Assert.True(request.Closed);Assert.Null(request.FitMethod);
        Assert.Equal(count+1,request.PointsMm.Count);Assert.Equal(request.PointsMm[0],request.PointsMm[count]);
        Assert.Equal(count,((JArray)input["points_mm"]!).Count);
    }
    [Fact]
    public void AlreadyRepeatedControlEndpointIsNotDuplicatedAgain()
    {
        var input=Options();input.Remove("fit_method");input["closed"]=true;
        ((JArray)input["points_mm"]!).Add(new JArray(0,0));
        Assert.Equal(4,NativeSplineCreationPolicy96.Parse(input,true).PointsMm.Count);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void OmittedClosurePreservesOpenCreatorInput(bool control)
    {
        var input=Options();if(control)input.Remove("fit_method");
        var request=NativeSplineCreationPolicy96.Parse(input,control);
        Assert.False(request.Closed);Assert.False(request.ClosureSpecified);Assert.Equal(3,request.PointsMm.Count);
    }
    [Theory]
    [InlineData("true")] [InlineData(1)] [InlineData(null)]
    public void NonBooleanClosureIsRefused(object? value)
    {var input=Options();input["closed"]=value==null?JValue.CreateNull():JToken.FromObject(value);Assert.Throws<ArgumentException>(()=>NativeSplineCreationPolicy96.Parse(input,false));}
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(65)]
    public void InputPointLimitIsCheckedBeforeAddingClosure(int count)
    {var input=Options(count);input["closed"]=true;Assert.Throws<ArgumentException>(()=>NativeSplineCreationPolicy96.Parse(input,false));}
    [Theory]
    [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)] [InlineData(double.NegativeInfinity)]
    public void NonFinitePointsAreRefused(double value)
    {var input=Options();input["points_mm"]![1]![1]=value;Assert.Throws<ArgumentException>(()=>NativeSplineCreationPolicy96.Parse(input,false));}
    [Theory]
    [InlineData("tension")] [InlineData("references")] [InlineData("close")]
    public void UnimplementedOrMisspelledOptionsCannotBeSilentlyIgnored(string key)
    {var input=Options();input[key]=true;Assert.Throws<ArgumentException>(()=>NativeSplineCreationPolicy96.Parse(input,false));}
    [Fact]
    public void ControlDoesNotSilentlyAcceptFitMethod()
    {Assert.Throws<ArgumentException>(()=>NativeSplineCreationPolicy96.Parse(Options(),true));}
    [Fact]
    public void MissingFitMethodIsRefused()
    {var input=Options();input.Remove("fit_method");Assert.Throws<ArgumentException>(()=>NativeSplineCreationPolicy96.Parse(input,false));}
    [Fact]
    public void StringCoordinateIsRefused()
    {var input=Options();input["points_mm"]![0]![0]="0";Assert.Throws<ArgumentException>(()=>NativeSplineCreationPolicy96.Parse(input,false));}
    [Fact]
    public void OmittedControlClosurePreservesNaturallyRepeatedInput()
    {
        var input=Options();input.Remove("fit_method");((JArray)input["points_mm"]!).Add(new JArray(0,0));
        var request=NativeSplineCreationPolicy96.Parse(input,true);
        Assert.False(request.ClosureSpecified);Assert.Equal(4,request.PointsMm.Count);
        input["closed"]=false;Assert.Throws<ArgumentException>(()=>NativeSplineCreationPolicy96.Parse(input,true));
    }
}
