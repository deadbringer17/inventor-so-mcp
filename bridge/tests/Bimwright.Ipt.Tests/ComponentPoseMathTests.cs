using Bimwright.Ipt.Shared.Infrastructure;
using Newtonsoft.Json.Linq;
namespace Bimwright.Ipt.Tests;
public sealed class ComponentPoseMathTests
{
    [Fact]
    public void RotationAboutAssemblyOriginRotatesPositionAndOrientation()
    {
        var request = ComponentMoveRequest.Parse(JObject.Parse("{translation_mm:[0,0,0],minimum_clearance_mm:5,rotation_axis:[0,0,2],rotation_center_mm:[0,0,0],rotation_degrees:90}"));
        var result = ComponentPoseMath.Apply(new double[,] {{1,0,0,3},{0,1,0,0},{0,0,1,0},{0,0,0,1}}, request);
        Assert.Equal(0,result[0,3],8); Assert.Equal(3,result[1,3],8);
        Assert.Equal(-1,result[0,1],8); Assert.Equal(1,result[1,0],8);
    }
    [Fact]
    public void RotationAboutOwnOriginPreservesPositionThenTranslates()
    {
        var request = ComponentMoveRequest.Parse(JObject.Parse("{translation_mm:[10,0,0],minimum_clearance_mm:5,rotation_axis:[0,1,0],rotation_center_mm:[30,0,0],rotation_degrees:90}"));
        var result = ComponentPoseMath.Apply(new double[,] {{1,0,0,3},{0,1,0,0},{0,0,1,0},{0,0,0,1}}, request);
        Assert.Equal(4,result[0,3],8); Assert.Equal(0,result[2,3],8); Assert.Equal(1,result[0,2],8);
    }
    [Fact]
    public void NullRotationMeansTranslationOnly()
    {
        var request = ComponentMoveRequest.Parse(JObject.Parse("{translation_mm:[0,0,0],minimum_clearance_mm:0,rotation_axis:null,rotation_center_mm:null,rotation_degrees:null}"));
        Assert.Null(request.RotationAxis);
    }
    [Theory]
    [InlineData("{rotation_axis:[0,0,0],rotation_center_mm:[0,0,0],rotation_degrees:90}")]
    [InlineData("{rotation_axis:[0,1,0]}")]
    public void InvalidRotationRejected(string json)
    {
        var p = JObject.Parse(json); p["translation_mm"]=new JArray(0,0,0); p["minimum_clearance_mm"]=5;
        Assert.Throws<ArgumentException>(() => ComponentMoveRequest.Parse(p));
    }
}
