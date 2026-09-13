using Bimwright.Ipt.Shared.Infrastructure;
using Newtonsoft.Json.Linq;
namespace Bimwright.Ipt.Tests;

public sealed class PartSnapshotDiffTests
{
    private static JObject Snapshot() => JObject.Parse("""
        {"schema_version":1,"document_id":"doc_a","parameters":[{"name":"Width","expression":"20 mm","database_value":2,"declared_units":"mm"}],
         "features":[{"name":"Extrusion1","suppressed":false}],
         "physical":{"mass_kg":1,"volume_mm3":1000,"area_mm2":600,"center_x_mm":0,"center_y_mm":0,"center_z_mm":5}}
        """);
    [Fact]
    public void IdenticalSnapshotsDoNotClaimGeometryEquivalence()
    {
        var result = PartSnapshotDiff.Compare(Snapshot(), Snapshot());
        Assert.Empty(result["parameters"]!["changed"]!);
        Assert.All(result["physical"]!, x => Assert.False((bool)x["changed"]!));
        Assert.False((bool)result["geometry_equivalence_proven"]!);
    }
    [Fact]
    public void ReportsChangesAdditionRemovalAndPhysicalDelta()
    {
        var after = Snapshot(); after["parameters"]![0]!["expression"] = "30 mm";
        after["features"] = JArray.Parse("[{\"name\":\"ExtrusionRenamed\",\"suppressed\":false}]");
        after["physical"]!["volume_mm3"] = 1200;
        var result = PartSnapshotDiff.Compare(Snapshot(), after);
        Assert.Single(result["parameters"]!["changed"]!);
        Assert.Single(result["features"]!["added"]!); Assert.Single(result["features"]!["removed"]!);
        var volume = result["physical"]!.Single(x => (string?)x["quantity"] == "volume_mm3");
        Assert.Equal(200, (double)volume["delta"]!); Assert.True((bool)volume["changed"]!);
    }
    [Fact]
    public void PhysicalNoiseWithinToleranceIsNotChange()
    {
        var after = Snapshot(); after["physical"]!["volume_mm3"] = 1000.0001;
        Assert.All(PartSnapshotDiff.Compare(Snapshot(), after)["physical"]!, x => Assert.False((bool)x["changed"]!));
    }
    [Fact]
    public void WrongDocumentRejected()
    {
        var after = Snapshot(); after["document_id"] = "doc_b";
        Assert.Throws<ArgumentException>(() => PartSnapshotDiff.Compare(Snapshot(), after));
    }
    [Fact]
    public void DuplicateNamesRejected()
    {
        var after = Snapshot(); ((JArray)after["features"]!).Add(after["features"]![0]!.DeepClone());
        Assert.Throws<ArgumentException>(() => PartSnapshotDiff.Compare(Snapshot(), after));
    }
    [Fact]
    public void NonfinitePhysicalValueRejected()
    {
        var after = Snapshot(); after["physical"]!["mass_kg"] = double.NaN;
        Assert.Throws<ArgumentException>(() => PartSnapshotDiff.Compare(Snapshot(), after));
    }
}
