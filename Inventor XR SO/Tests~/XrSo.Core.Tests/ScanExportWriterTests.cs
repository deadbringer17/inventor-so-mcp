using System.Security.Cryptography;
using InventorXrSo.Core.Capture;
using Newtonsoft.Json.Linq;

namespace XrSo.Core.Tests;

public sealed class ScanExportWriterTests:IDisposable
{
    private readonly string _root=Path.Combine(Path.GetTempPath(),"rc3-export-"+Guid.NewGuid().ToString("N"));
    private static ScanReview Review()
    {
        const string id="11111111111111111111111111111111";
        return new ScanReview(id,2,new[]{new ScanPoint {Id="p0",Position=new[]{0d,0,0},Observations=3}},new JObject {["evidence_kind"]="synthetic"},new JArray(),
            new JObject {["scan_glb"]="metre; glTF right-handed Y-up",["scan_obj"]="mm; Unity left-handed Y-up"},
            new JObject {["scan_id"]=id,["revision"]=2,["version"]=3,["primitives"]=new JArray()},new byte[]{1,2,3});
    }
    [Theory][InlineData("glb","metre")][InlineData("ply","mm")][InlineData("obj","mm")]
    public void ExportContainsUnitsFrozenMetadataAndVerifiedHashesAndPreservesPreviousExports(string format,string unit)
    {
        var review=Review();byte[] bytes={10,20,30};string directory=ScanExportWriter.WriteNew(_root,review,format,bytes);
        var manifest=JObject.Parse(File.ReadAllText(Path.Combine(directory,"export.json")));
        Assert.Equal(2,manifest.Value<int>("revision"));Assert.Equal(3,manifest.Value<int>("review_version"));Assert.Equal(unit,manifest.Value<string>("unit"));
        Assert.Equal("not_estimated",manifest.Value<string>("uncertainty_status"));Assert.Equal("synthetic",manifest.Value<string>("evidence_kind"));Assert.Equal(5,manifest["files"]!.Count());
        foreach(var entry in manifest["files"]!)
        {
            byte[] file=File.ReadAllBytes(Path.Combine(directory,entry.Value<string>("name")!));
            Assert.Equal(entry.Value<long>("size"),file.LongLength);Assert.Equal(entry.Value<string>("sha256"),Convert.ToHexString(SHA256.HashData(file)).ToLowerInvariant());
        }
        Assert.Equal(bytes,File.ReadAllBytes(Path.Combine(directory,"scan."+format)));Assert.IsType<JObject>(JToken.Parse(File.ReadAllText(Path.Combine(directory,"units.json"))));
        string second=ScanExportWriter.WriteNew(_root,review,format,new byte[]{9});Assert.NotEqual(directory,second);Assert.Equal(bytes,File.ReadAllBytes(Path.Combine(directory,"scan."+format)));
        Assert.DoesNotContain(Directory.GetDirectories(_root),p=>p.EndsWith(".pending"));
    }
    [Fact] public void InvalidFormatAndOversizedMetadataDoNotPublishDirectories()
    {
        var review=Review();Assert.Throws<ArgumentException>(()=>ScanExportWriter.WriteNew(_root,review,"../bad",new byte[]{1}));Assert.False(Directory.Exists(_root));
        review.Quality["oversized"]=new string('x',1024*1024);Assert.Throws<InvalidDataException>(()=>ScanExportWriter.WriteNew(_root,review,"obj",new byte[]{1}));Assert.False(Directory.Exists(_root));
    }
    public void Dispose() {if(Directory.Exists(_root)) Directory.Delete(_root,true);}
}
