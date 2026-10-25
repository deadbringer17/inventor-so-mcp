using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Bimwright.Ipt.Server;
using Inventor.So.Capture.Worker;
using Inventor.So.Mcp.Http;
using Inventor.So.Mcp.Http.Capture;
using Inventor.So.Mcp.Http.Pairing;
using Inventor.So.RealityCapture;
using InventorXrSo.Core.Capture;
using InventorXrSo.Core.Net;
using InventorXrSo.Core.Pairing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Capture.Worker.Tests;

public sealed class CaptureMcpIntegrationTests
{
    [Fact]
    public async Task McpControlRunsExternalWorkerAndPublishesOnlyOwnedRevisionAssets()
    {
        using var fixture=new SyntheticReconstructionTests(); var raw=fixture.MakeInput();
        string root=Path.GetDirectoryName(Path.GetDirectoryName(raw.InputDirectory)!)!;
        var store=new CaptureTransferStore(Path.Combine(root,"server-raw"));
        var token=TokenRegistry.Generate(); var foreign=TokenRegistry.Generate(); var tokens=new TokenRegistry(); tokens.Add("a",token);tokens.Add("b",foreign);
        var config=new InventorMcpConfig {EnableExperimental=true,HttpUrls=new List<string>{"https://127.0.0.1:0"},HttpRateLimitPerMinute=2000,HttpSelfSignedCertificate=true,HttpSelfSignedPath=Path.Combine(root,"test.pfx"),
            DescriptorDirectory=Path.Combine(root,"targets"),AssetDirectory=Path.Combine(root,"assets"),AuditDirectory=Path.Combine(root,"audit")};
        string worker=Environment.GetEnvironmentVariable("XR_SO_TEST_CAPTURE_WORKER") ?? typeof(Inventor.So.Capture.Worker.Program).Assembly.Location;
        Assert.True(File.Exists(Path.ChangeExtension(worker,"runtimeconfig.json")),worker);
        var settings=new CaptureWorkerSettings(worker,TimeSpan.FromSeconds(60));
        using var certificate=PairingSetup.ResolveCertificate(config) ?? throw new InvalidOperationException("Synthetic TLS certificate missing.");
        string pin=SelfSignedCertificate.Sha256Hex(certificate);
        await using var app=HttpHost.Build(Array.Empty<string>(),config,tokens,new HostOptions {Certificate=certificate,ConfigureServices=s=>{s.AddSingleton(store);s.AddSingleton(settings);} });
        await app.StartAsync();
        using var client=Client(app,token,pin); using var other=Client(app,foreign,pin);
        string session=await Initialize(client); string otherSession=await Initialize(other);
        var local=new LocalScanStore(Path.GetDirectoryName(raw.InputDirectory)!,20000000,0,()=>long.MaxValue);
        var files=local.DescribeTransfer(raw.ScanId);
        var created=await Tool(client,session,"create_session",new {manifest_json=JsonConvert.SerializeObject(new {ScanId=raw.ScanId,Files=files})});
        Assert.True((bool)created["ok"]!);
        for(int i=0;i<files.Count;i++)
        {
            using var request=new HttpRequestMessage(HttpMethod.Put,$"/capture/v1/sessions/{raw.ScanId}/chunks/{i}") {Content=new ByteArrayContent(local.ReadTransferFile(raw.ScanId,files[i]))};
            request.Content.Headers.ContentType=new MediaTypeHeaderValue("application/octet-stream"); request.Headers.Add("X-Content-SHA256",files[i].Sha256);
            (await client.SendAsync(request)).EnsureSuccessStatusCode();
        }
        (await client.PostAsync($"/capture/v1/sessions/{raw.ScanId}/finalize",Json(new {}))).EnsureSuccessStatusCode();
        var disabled=new MetricConstraint("reserved","distance",new[] {"unselectedA","unselectedB"},100,0.1,"validation",false,true);
        var added=await Tool(client,session,"add_constraint",new {session_id=raw.ScanId,constraint_json=JsonConvert.SerializeObject(disabled),expected_version=0});
        Assert.Equal(1,(int)added["version"]!);
        var stale=await Tool(client,session,"remove_constraint",new {session_id=raw.ScanId,constraint_id="reserved",expected_version=0});
        Assert.Equal("CAPTURE_CONSTRAINT_VERSION_CHANGED",(string?)stale["error"]?["code"]);
        var started=await Tool(client,session,"start_solve",new {session_id=raw.ScanId,expected_version=1}); Assert.True((bool)started["ok"]!,started.ToString());
        var removed=await Tool(client,session,"remove_constraint",new {session_id=raw.ScanId,constraint_id="reserved",expected_version=1}); Assert.Equal(2,(int)removed["version"]!);
        var solution=await WaitSolution(client,session,raw.ScanId,1);
        Assert.Equal("succeeded",(string?)solution["solution"]?["State"]); Assert.Equal(1,(int)solution["solution"]!["ConstraintVersion"]!);
        var quality=await Tool(client,session,"get_quality",new {session_id=raw.ScanId,revision=1});
        Assert.Equal("synthetic",(string?)quality["quality"]?["evidence_kind"]);
        Assert.NotEqual("VALIDATED",(string?)quality["quality"]?["metric"]?["AccuracyStatus"]);
        var asset=solution["assets"]!.Single(a=>a.Value<string>("name")=="scan.glb");
        using var response=await client.GetAsync((string)asset["url"]!); response.EnsureSuccessStatusCode(); var bytes=await response.Content.ReadAsByteArrayAsync();
        Assert.Equal((string)asset["sha256"]!,CaptureTransferStore.Hash(bytes));
        Assert.Equal(HttpStatusCode.NotFound,(await other.GetAsync((string)asset["url"]!)).StatusCode);
        var denied=await Tool(other,otherSession,"get_quality",new {session_id=raw.ScanId,revision=1}); Assert.Equal("CAPTURE_NOT_FOUND",(string?)denied["error"]?["code"]);
        var frozen=await client.GetStringAsync($"/capture/v1/sessions/{raw.ScanId}/solutions/1/assets/constraints.json"); Assert.Contains("reserved",frozen);
        var paired=new PairedServer(client.BaseAddress!.Host,client.BaseAddress.Port,pin,"review",token);
        var reviewClient=new CaptureReviewClient(new ReviewTransport(client),paired);
        var revisions=await reviewClient.ListAsync(raw.ScanId,CancellationToken.None);Assert.Single(revisions["solutions"]!);
        var review=await reviewClient.OpenAsync(raw.ScanId,1,CancellationToken.None);
        Assert.Equal(bytes,review.Glb);Assert.NotEmpty(review.Points);Assert.Empty(review.Annotations["primitives"]!);
        var fit=await Tool(client,session,"fit_primitive",new {session_id=raw.ScanId,revision=1,primitive_id="plane_1",kind="plane",point_ids_json=JsonConvert.SerializeObject(review.Points.Select(p=>p.Id)),expected_version=0});
        Assert.True((bool)fit["ok"]!,fit.ToString());Assert.Equal(1,(int)fit["version"]!);Assert.Single(fit["primitives"]!);
        review.ApplyAnnotations(fit);Assert.Equal("not_estimated",fit.Value<string>("uncertainty_status"));
        Assert.Equal(JTokenType.Null,(await Tool(client,session,"get_alignment",new {session_id=raw.ScanId,revision=1}))["alignment"]!.Type);
        var registration=new JObject {["document_id"]="synthetic-cad",["cad_revision"]="cad-r1",["review_version"]=1,["expected_alignment_version"]=0,["method"]="scan_basis",["reference_ids"]=new JArray()};
        var aligned=await Tool(client,session,"align_to_cad",new {session_id=raw.ScanId,revision=1,registration_json=registration.ToString(Formatting.None)});
        Assert.True(aligned.Value<bool>("ok"),aligned.ToString());Assert.Equal(1,aligned["alignment"]!.Value<int>("version"));
        var prepared=await Tool(client,session,"create_reference",new {session_id=raw.ScanId,revision=1,alignment_version=1});
        Assert.True(prepared.Value<bool>("ok"),prepared.ToString());Assert.Equal("create_reality_reference",prepared["operations"]![0]!.Value<string>("command"));
        var nativeInput=new Bimwright.Ipt.Shared.Contracts.RealityReferenceContract((JObject)prepared["operations"]![0]!["arguments"]!["reference"]!);
        Assert.Equal(review.Points.Length,nativeInput.Points.Count);Assert.Single(nativeInput.Primitives);
        var alignmentConflict=await Tool(client,session,"align_to_cad",new {session_id=raw.ScanId,revision=1,registration_json=registration.ToString(Formatting.None)});
        Assert.Equal("CAPTURE_ALIGNMENT_VERSION_CHANGED",alignmentConflict["error"]!.Value<string>("code"));
        var forbiddenReference=await Tool(other,otherSession,"create_reference",new {session_id=raw.ScanId,revision=1,alignment_version=1});
        Assert.Equal("CAPTURE_NOT_FOUND",forbiddenReference["error"]!.Value<string>("code"));
        var oldReview=await Tool(client,session,"remove_primitive",new {session_id=raw.ScanId,revision=1,primitive_id="plane_1",expected_version=0});
        Assert.Equal("CAPTURE_REVIEW_VERSION_CHANGED",oldReview["error"]!.Value<string>("code"));
        var missingPoint=await Tool(client,session,"fit_primitive",new {session_id=raw.ScanId,revision=1,primitive_id="point_bad",kind="point",point_ids_json="[\"unknown\"]",expected_version=1});
        Assert.Equal("CAPTURE_REFERENCE_NOT_FOUND",missingPoint["error"]!.Value<string>("code"));
        Assert.Equal(HttpStatusCode.NotFound,(await other.GetAsync($"/capture/v1/sessions/{raw.ScanId}/solutions/1/review")).StatusCode);
        foreach(string format in new[]{"glb","ply","obj"}) Assert.NotEmpty(await reviewClient.ExportAsync(raw.ScanId,1,format,CancellationToken.None));
        Assert.Equal(3,local.Verify(raw.ScanId));
        var second=await Tool(client,session,"start_solve",new {session_id=raw.ScanId,expected_version=2}); Assert.True((bool)second["ok"]!,second.ToString());
        var secondSolution=await WaitSolution(client,session,raw.ScanId,2); Assert.Equal("succeeded",(string?)secondSolution["solution"]?["State"]);
        Assert.Equal("[]",(await client.GetStringAsync($"/capture/v1/sessions/{raw.ScanId}/solutions/2/assets/constraints.json")).Trim());
        Assert.Empty((await Tool(client,session,"get_review",new {session_id=raw.ScanId,revision=2}))["primitives"]!);
        Assert.Single((await Tool(client,session,"get_review",new {session_id=raw.ScanId,revision=1}))["primitives"]!);
        var removedPrimitive=await reviewClient.ControlAsync("remove_primitive",new JObject {["session_id"]=raw.ScanId,["revision"]=1,["primitive_id"]="plane_1",["expected_version"]=1},CancellationToken.None);
        Assert.Equal(2,removedPrimitive.Value<int>("version"));Assert.Empty(removedPrimitive["primitives"]!);
        var staleReference=await Tool(client,session,"create_reference",new {session_id=raw.ScanId,revision=1,alignment_version=1});
        Assert.Equal("CAPTURE_ALIGNMENT_VERSION_CHANGED",staleReference["error"]!.Value<string>("code"));
        Assert.Equal(1,(await Tool(client,session,"get_alignment",new {session_id=raw.ScanId,revision=1}))["alignment"]!.Value<int>("version"));
        Assert.Equal(frozen,await client.GetStringAsync($"/capture/v1/sessions/{raw.ScanId}/solutions/1/assets/constraints.json"));
        var invalid=new MetricConstraint("unresolved","distance",new[] {"does-not-exist-A","does-not-exist-B"},100,0.1);
        var thirdConstraints=await Tool(client,session,"add_constraint",new {session_id=raw.ScanId,constraint_json=JsonConvert.SerializeObject(invalid),expected_version=2});
        Assert.Equal(3,(int)thirdConstraints["version"]!);
        Assert.True((bool)(await Tool(client,session,"start_solve",new {session_id=raw.ScanId,expected_version=3}))["ok"]!);
        var failed=await WaitSolution(client,session,raw.ScanId,3);
        Assert.Equal("failed",(string?)failed["solution"]?["State"]);
        Assert.Equal("CAPTURE_REFERENCE_NOT_FOUND",(string?)failed["solution"]?["ErrorCode"]);
        Assert.Empty(failed["assets"]!);
        Assert.Equal(HttpStatusCode.OK,(await client.GetAsync("/healthz")).StatusCode);
        Assert.Equal(frozen,await client.GetStringAsync($"/capture/v1/sessions/{raw.ScanId}/solutions/1/assets/constraints.json"));
        await app.StopAsync();
    }
    private static HttpClient Client(WebApplication app,string token,string pin)
    {
        var handler=new HttpClientHandler {AllowAutoRedirect=false,ServerCertificateCustomValidationCallback=(_,cert,_,_)=>cert!=null&&ServerTrust.Pinned(pin).Validate(cert.RawData)};
        var client=new HttpClient(handler) {BaseAddress=new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single())};
        client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",token); return client;
    }
    private sealed class ReviewTransport(HttpClient http):IHttpTransport
    {
        public async Task<TransportResponse> SendAsync(TransportRequest request,CancellationToken ct)
        {
            using var message=new HttpRequestMessage(new HttpMethod(request.Method),request.Url);
            if(request.Body!=null) message.Content=new ByteArrayContent(request.Body);
            foreach(var h in request.Headers) if(!message.Headers.TryAddWithoutValidation(h.Key,h.Value)) message.Content?.Headers.TryAddWithoutValidation(h.Key,h.Value);
            using var response=await http.SendAsync(message,ct);
            var headers=response.Headers.Concat(response.Content.Headers).ToDictionary(h=>h.Key,h=>string.Join(",",h.Value),StringComparer.OrdinalIgnoreCase);
            return new TransportResponse((int)response.StatusCode,headers,await response.Content.ReadAsByteArrayAsync(ct));
        }
        public Task<int> StreamLinesAsync(TransportRequest request,Action<string> onLine,CancellationToken ct)=>throw new NotSupportedException();
    }
    private static StringContent Json(object o)=>new(JsonConvert.SerializeObject(o),Encoding.UTF8,"application/json");
    private static async Task<JObject> Rpc(HttpClient client,object payload,string? session=null)
    {
        using var request=new HttpRequestMessage(HttpMethod.Post,"/mcp") {Content=Json(payload)};
        request.Headers.Accept.ParseAdd("application/json");request.Headers.Accept.ParseAdd("text/event-stream");
        if(session!=null) request.Headers.Add("Mcp-Session-Id",session);
        using var response=await client.SendAsync(request); string text=await response.Content.ReadAsStringAsync(); Assert.True(response.IsSuccessStatusCode,text);
        var json=response.Content.Headers.ContentType?.MediaType=="text/event-stream" ? text.Split('\n').Where(l=>l.StartsWith("data:")).Select(l=>JObject.Parse(l[5..])).Last() : JObject.Parse(text);
        json["_session"]=response.Headers.TryGetValues("Mcp-Session-Id",out var values)?values.First():session; return json;
    }
    private static async Task<string> Initialize(HttpClient client)
    {
        var result=await Rpc(client,new {jsonrpc="2.0",id=1,method="initialize",@params=new {protocolVersion="2025-06-18",capabilities=new {},clientInfo=new {name="synthetic-capture-test",version="1"}}});
        string session=(string)result["_session"]!;
        using var request=new HttpRequestMessage(HttpMethod.Post,"/mcp") {Content=Json(new {jsonrpc="2.0",method="notifications/initialized"})};
        request.Headers.Accept.ParseAdd("application/json");request.Headers.Accept.ParseAdd("text/event-stream");request.Headers.Add("Mcp-Session-Id",session);
        (await client.SendAsync(request)).EnsureSuccessStatusCode(); return session;
    }
    private static async Task<JObject> Tool(HttpClient client,string session,string suffix,object args)
    {
        var result=await Rpc(client,new {jsonrpc="2.0",id=2,method="tools/call",@params=new {name="inventor_reality_capture_"+suffix,arguments=args}},session);
        Assert.Null(result["error"]); return JObject.Parse((string)result["result"]!["content"]![0]!["text"]!);
    }
    private static async Task<JObject> WaitSolution(HttpClient client,string session,string id,int revision)
    {
        var deadline=DateTimeOffset.UtcNow.AddSeconds(60);
        while(true)
        {
            var result=await Tool(client,session,"get_solution",new {session_id=id,revision});
            if(result["solution"]?.Value<string>("State") is not ("queued" or "running")) return result;
            Assert.True(DateTimeOffset.UtcNow<deadline,result.ToString()); await Task.Delay(100);
        }
    }
}
