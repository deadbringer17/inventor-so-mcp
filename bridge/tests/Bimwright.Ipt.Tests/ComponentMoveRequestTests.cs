using Bimwright.Ipt.Shared.Infrastructure;
using Newtonsoft.Json.Linq;
namespace Bimwright.Ipt.Tests;
public sealed class ComponentMoveRequestTests
{
    [Fact]
    public void PreviewIsDefault() => Assert.True(ComponentMoveRequest.Parse(JObject.Parse("{translation_mm:[1,2,3],minimum_clearance_mm:5}")).Preview);
    [Theory]
    [InlineData("{translation_mm:[1,2],minimum_clearance_mm:5}")]
    [InlineData("{translation_mm:[1,2,3],minimum_clearance_mm:-1}")]
    [InlineData("{translation_mm:[1,2,'3'],minimum_clearance_mm:5}")]
    [InlineData("{translation_mm:[1,2,3]}")]
    public void BadRequestRejected(string json) => Assert.Throws<ArgumentException>(() => ComponentMoveRequest.Parse(JObject.Parse(json)));
}
