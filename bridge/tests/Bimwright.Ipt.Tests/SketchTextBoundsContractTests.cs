using Bimwright.Ipt.Shared.Contracts;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Ipt.Tests;

public sealed class SketchTextBoundsContractTests
{
    [Theory]
    [InlineData(0)][InlineData(37)][InlineData(90)][InlineData(270)]
    public void RotatedRectangleIsValidWithoutRequiringAxisAlignment(double degrees)
    {
        double c=Math.Cos(degrees*Math.PI/180),s=Math.Sin(degrees*Math.PI/180);var bounds=new JArray();
        foreach(var p in new[]{(0,0),(10,0),(10,4),(0,4)}){bounds.Add(20+c*p.Item1-s*p.Item2);bounds.Add(30+s*p.Item1+c*p.Item2);}
        SketchTextGlyphMeshContract.ValidateBounds(bounds);
    }
    [Theory]
    [InlineData("count")][InlineData("nan")][InlineData("infinity")][InlineData("string")][InlineData("zero")][InlineData("skew")][InlineData("open")][InlineData("huge")]
    public void MalformedBoundsCannotAuthorizeCompleteText(string kind)
    {
        var b=new JArray(0,0,10,0,10,4,0,4);
        switch(kind){case "count":b.RemoveAt(0);break;case "nan":b[0]=double.NaN;break;case "infinity":b[0]=double.PositiveInfinity;break;case "string":b[0]="0";break;case "zero":b=new JArray(0,0,0,0,0,4,0,4);break;case "skew":b=new JArray(0,0,10,0,12,4,2,4);break;case "open":b[4]=11;break;case "huge":b[0]=1e10;break;}
        Assert.Throws<FormatException>(()=>SketchTextGlyphMeshContract.ValidateBounds(b));
    }
}
