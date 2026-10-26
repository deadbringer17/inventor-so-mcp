using Bimwright.Ipt.Server;
using Inventor.So.Mcp.Http;
using Inventor.So.Mcp.Http.Capture;
using Inventor.So.Mcp.Http.Pairing;
using InventorXrSo.Core.Capture;
using InventorXrSo.Core.Net;
using InventorXrSo.Core.Pairing;
using InventorXrSo.Core.Tests.Support;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using System.Security.Cryptography.X509Certificates;

namespace XrSo.Core.Tests;

public sealed class RealityCaptureHttpTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(),"xrso-capture-https-" + Guid.NewGuid().ToString("N"));
    private readonly string _token = TokenRegistry.Generate(), _foreign = TokenRegistry.Generate();
    private WebApplication _app;
    private X509Certificate2 _certificate;
    private PairedServer _server;
    private SystemHttpTransport _http;
    private CaptureTransferStore _remote;

    public async Task InitializeAsync()
    {
        var config = new InventorMcpConfig {
            HttpSelfSignedCertificate = true, HttpSelfSignedPath = Path.Combine(_root,"cert.pfx"),
            DescriptorDirectory = Path.Combine(_root,"targets"), AssetDirectory = Path.Combine(_root,"assets"),
            AuditDirectory = Path.Combine(_root,"audit"), EnableExperimental = true, HttpRateLimitPerMinute = 1000,
            HttpUrls = new List<string> { "https://127.0.0.1:0" }
        };
        var tokens = new TokenRegistry(); tokens.Add("capture-a",_token); tokens.Add("capture-b",_foreign);
        _remote = new CaptureTransferStore(Path.Combine(_root,"remote"));
        _certificate = PairingSetup.ResolveCertificate(config);
        string sha = SelfSignedCertificate.Sha256Hex(_certificate);
        _app = HttpHost.Build(Array.Empty<string>(),config,tokens,new HostOptions {
            Certificate = _certificate, ConfigureServices = services => services.AddSingleton(_remote)
        });
        await _app.StartAsync();
        var origin = new Uri(_app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>().Addresses.Single());
        _server = new PairedServer(origin.Host,origin.Port,sha,"capture-a",_token);
        _http = new SystemHttpTransport(ServerTrust.Pinned(sha));
    }
    private (LocalScanStore store,string id) Capture()
    {
        var store = new LocalScanStore(Path.Combine(_root,"raw"),10000000,0,() => long.MaxValue);
        var session = new ScanSession(Guid.NewGuid().ToString("N"),CaptureMode.Object,"test","1",DateTimeOffset.UtcNow,
            new CaptureVolume(CaptureTransform.Identity,600,600,600),CaptureTransform.Identity);
        var recorder = new ScanRecorder(store,session); recorder.Start();
        var frame = new CaptureFrame(1000000,"left",new CaptureIntrinsics(16,16,12,12,8,8),CaptureTransform.Identity,
            CaptureTransform.Identity,CaptureTransform.Identity,new CaptureQuality(120,0.01,100,0.3),true);
        Assert.True(recorder.Record(frame,new byte[16*16*3])); recorder.Stop();
        return (store,session.SessionId);
    }

    [Fact]
    public async Task RealPinnedHttpsTransfersAndReplayingCompletedUploadSendsNoBinary()
    {
        var (store,id) = Capture();
        var requests = new List<string>();
        var recording = new RecordingTransport(_http,requests);
        var client = new CaptureSessionClient(recording,_server);
        await client.UploadAsync(store,id,null,CancellationToken.None);
        Assert.Equal(3,requests.Count(r => r == "PUT"));
        requests.Clear();
        await client.UploadAsync(store,id,null,CancellationToken.None);
        Assert.Equal(new[] { "POST" },requests);
        Assert.True((bool)JObject.FromObject(_remote.Describe(CaptureTransferStore.TokenOwner(_token),id))["finalized"]);
    }

    [Fact]
    public async Task UploadResumesOnlyMissingChunksAfterInterruptedTransport()
    {
        var (store,id) = Capture();
        var requests = new List<string>();
        var interrupted = new RecordingTransport(_http,requests) { FailPut = 2 };
        await Assert.ThrowsAsync<IOException>(() => new CaptureSessionClient(interrupted,_server).UploadAsync(store,id,null,CancellationToken.None));
        requests.Clear();
        await new CaptureSessionClient(new RecordingTransport(_http,requests),_server).UploadAsync(store,id,null,CancellationToken.None);
        Assert.Equal(2,requests.Count(r => r == "PUT"));
        Assert.Equal(1,store.Verify(id));
    }

    [Fact]
    public async Task AnonymousAndOtherBearerCannotReadDeleteOrFinalizeRaw()
    {
        var (store,id) = Capture();
        await new CaptureSessionClient(_http,_server).UploadAsync(store,id,null,CancellationToken.None);
        string url = _server.BaseUrl + "/capture/v1/sessions/" + id;
        Assert.Equal(401,(await _http.SendAsync(new TransportRequest("GET",url),CancellationToken.None)).Status);
        foreach (var (method,suffix) in new[] { ("GET",""),("DELETE",""),("POST","/finalize") })
        {
            var request = new TransportRequest(method,url + suffix);
            request.Headers["Authorization"] = "Bearer " + _foreign;
            Assert.Equal(404,(await _http.SendAsync(request,CancellationToken.None)).Status);
        }
        Assert.True((bool)JObject.FromObject(_remote.Describe(CaptureTransferStore.TokenOwner(_token),id))["finalized"]);
    }

    private sealed class RecordingTransport(IHttpTransport inner,List<string> methods) : IHttpTransport
    {
        public int FailPut { get; init; }
        private int _puts;
        public Task<TransportResponse> SendAsync(TransportRequest request,CancellationToken ct)
        {
            methods.Add(request.Method);
            if (request.Method == "PUT" && ++_puts == FailPut) throw new IOException("synthetic transport failure");
            return inner.SendAsync(request,ct);
        }
        public Task<int> StreamLinesAsync(TransportRequest request,Action<string> onLine,CancellationToken ct) => throw new NotSupportedException();
    }
    public async Task DisposeAsync()
    {
        _http?.Dispose();
        if (_app != null) { await _app.StopAsync(); await _app.DisposeAsync(); }
        _certificate?.Dispose();
        if (Directory.Exists(_root)) Directory.Delete(_root,true);
    }
}
