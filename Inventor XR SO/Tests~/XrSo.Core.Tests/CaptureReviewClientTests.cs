using System.Security.Cryptography;
using System.Text;
using InventorXrSo.Core.Capture;
using InventorXrSo.Core.Net;
using InventorXrSo.Core.Pairing;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace XrSo.Core.Tests;

public sealed class CaptureReviewClientTests
{
    private const string Id="11111111111111111111111111111111";
    private sealed class Server:IHttpTransport
    {
        public Dictionary<string,byte[]> Assets=new();
        public List<TransportRequest> Requests=new();
        public JObject Solution;
        public bool Corrupt,WrongHeader;
        public JObject Review=new(){["ok"]=true,["scan_id"]=Id,["revision"]=1,["version"]=0,["primitives"]=new JArray()};
        public Server()
        {
            Assets["points.json"]=Encoding.UTF8.GetBytes("{\"unit\":\"mm\",\"coordinate_space\":\"scan_metric\",\"points\":[{\"id\":\"p1\",\"position_mm\":[0,0,1000],\"observations\":2},{\"id\":\"p2\",\"position_mm\":[100,0,1000],\"observations\":3}]}");
            Assets["quality_report.json"]=Encoding.UTF8.GetBytes("{\"metric\":{\"AccuracyStatus\":\"UNSCALED\"}}");
            Assets["constraints.json"]=Encoding.UTF8.GetBytes("[]");Assets["units.json"]=Encoding.UTF8.GetBytes("{\"scan_glb\":\"metre\"}");Assets["scan.glb"]=new byte[]{1,2,3};
            Solution=new JObject { ["ok"]=true,["solution"]=new JObject {["ScanId"]=Id,["Revision"]=1,["State"]="succeeded"},
                ["assets"]=new JArray(Assets.Select(a=>new JObject {["name"]=a.Key,["size"]=a.Value.Length,["sha256"]=Hash(a.Value),["url"]="https://attacker.example/"+a.Key})) };
        }
        public CaptureReviewClient Client()=>new(this,new PairedServer("paired.test",443,new string('a',64),"test","secret-test-token"));
        public Task<TransportResponse> SendAsync(TransportRequest request,CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();Requests.Add(request);
            var headers=new Dictionary<string,string>();byte[] body;
            if(request.Url.Contains("/assets/"))
            {body=Assets[request.Url.Split('/').Last()].ToArray();headers["X-Content-SHA256"]=WrongHeader?new string('0',64):Hash(body);if(Corrupt) body[0]^=1;}
            else body=Encoding.UTF8.GetBytes((request.Url.EndsWith("/review")?Review:Solution).ToString(Formatting.None));
            return Task.FromResult(new TransportResponse(200,headers,body));
        }
        public Task<int> StreamLinesAsync(TransportRequest request,Action<string> onLine,CancellationToken ct)=>throw new NotSupportedException();
    }
    private static string Hash(byte[] data)=>Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
    [Fact]
    public async Task DownloadIgnoresUntrustedAssetUrlsAndChecksAllHashesWithBoundedPinnedRequests()
    {
        var server=new Server();var review=await server.Client().OpenAsync(Id,1,CancellationToken.None);
        Assert.Equal(100,ScanGeometry.Distance(review.Points[0].Position,review.Points[1].Position));Assert.IsType<JArray>(review.Constraints);
        Assert.All(server.Requests,r=>{Assert.StartsWith("https://paired.test:443/capture/v1/sessions/",r.Url);Assert.Equal("Bearer secret-test-token",r.Headers["Authorization"]);Assert.InRange(r.MaxResponseBytes,1,32*1024*1024);});
    }
    [Theory][InlineData(true,false)][InlineData(false,true)]
    public async Task ChangedBytesOrHashHeaderAreRejected(bool corrupt,bool header)
    {var server=new Server {Corrupt=corrupt,WrongHeader=header};await Assert.ThrowsAsync<InvalidDataException>(()=>server.Client().OpenAsync(Id,1,CancellationToken.None));}
    [Fact]
    public async Task OversizedAssetIsRejectedBeforeDownloadAndRevisionIdentityMustMatch()
    {
        var server=new Server();server.Solution["assets"]![0]!["size"]=40*1024*1024;
        await Assert.ThrowsAsync<InvalidDataException>(()=>server.Client().OpenAsync(Id,1,CancellationToken.None));Assert.Single(server.Requests);
        server=new Server();server.Solution["solution"]!["Revision"]=2;
        await Assert.ThrowsAsync<IOException>(()=>server.Client().OpenAsync(Id,1,CancellationToken.None));
    }
    [Fact]
    public async Task CancelledRequestAndPathInjectionNeverSendAuthenticatedRequests()
    {
        var server=new Server();var client=server.Client();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>client.OpenAsync(Id,1,new CancellationToken(true)));Assert.Empty(server.Requests);
        await Assert.ThrowsAsync<ArgumentException>(()=>client.ListAsync("../other",CancellationToken.None));Assert.Empty(server.Requests);
    }
    [Fact]
    public async Task ReviewIsBoundToItsScanAndSolveAndCannotReferenceUnknownPoints()
    {
        var server=new Server();var review=await server.Client().OpenAsync(Id,1,CancellationToken.None);
        var wrong=(JObject)server.Review.DeepClone();wrong["revision"]=2;Assert.Throws<InvalidDataException>(()=>review.ApplyAnnotations(wrong));
        var primitive=ScanGeometry.Fit("test","point",new[]{new ScanPoint {Id="unknown",Position=new[]{0d,0,0}}});
        wrong=(JObject)server.Review.DeepClone();wrong["primitives"]=JArray.FromObject(new[]{primitive});Assert.Throws<InvalidDataException>(()=>review.ApplyAnnotations(wrong));
        Assert.Empty(review.Annotations["primitives"]!);
    }
    [Fact]
    public async Task ArtifactDerivedConstraintsCanExceedTheManualConstraintLimitButRemainBounded()
    {
        var server=new Server();var review=await server.Client().OpenAsync(Id,1,CancellationToken.None);
        var primitive=ScanGeometry.Fit("point","point",new[]{review.Points[0]});
        primitive.ConstraintIds=Enumerable.Range(0,300).Select(i=>"cal:artifact:"+i).ToArray();
        var annotation=(JObject)server.Review.DeepClone();annotation["primitives"]=JArray.FromObject(new[]{primitive});
        review.ApplyAnnotations(annotation);Assert.Equal(300,review.Annotations["primitives"]![0]!["constraint_ids"]!.Count());
        primitive.ConstraintIds=Enumerable.Range(0,2049).Select(i=>"cal:artifact:"+i).ToArray();annotation["primitives"]=JArray.FromObject(new[]{primitive});
        Assert.Throws<InvalidDataException>(()=>review.ApplyAnnotations(annotation));
    }
}
