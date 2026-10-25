using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using InventorXrSo.Core.Mcp;
using InventorXrSo.Core.Net;
using InventorXrSo.Core.Pairing;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace InventorXrSo.Core.Capture
{
    public sealed class ScanReview
    {
        public string SessionId { get; }
        public int Revision { get; }
        public ScanPoint[] Points { get; }
        public JObject Quality { get; }
        public JToken Constraints { get; }
        public JObject Units { get; }
        public JObject Annotations { get; private set; }
        public byte[] Glb { get; }
        public ScanReview(string id,int revision,ScanPoint[] points,JObject quality,JToken constraints,JObject units,JObject review,byte[] glb)
        { ScanGeometry.Validate(points);SessionId=id;Revision=revision;Points=points;Quality=quality;Constraints=constraints;Units=units;Glb=glb;ApplyAnnotations(review); }
        public void ApplyAnnotations(JObject review)
        {
            if(review==null||review.Value<string>("scan_id")!=SessionId||review.Value<int?>("revision")!=Revision||review.Value<int?>("version").GetValueOrDefault(-1)<0||!(review["primitives"] is JArray primitives)||primitives.Count>32)
                throw new InvalidDataException("Review identity mismatch.");
            var ids=new System.Collections.Generic.HashSet<string>(Points.Select(p=>p.Id));
            foreach(var p in primitives.ToObject<ScanPrimitive[]>())
                if(p==null||string.IsNullOrEmpty(p.Id)||!new[]{"point","axis","plane","circle","cylinder"}.Contains(p.Kind)||
                    p.Origin==null||p.Origin.Length!=3||p.Origin.Any(n=>!ScanGeometry.Finite(n)||Math.Abs(n)>1e7)||p.Direction==null||p.Direction.Length!=3||
                    p.Direction.Any(n=>!ScanGeometry.Finite(n))||Math.Abs(p.Direction.Sum(n=>n*n)-1)>.001||
                    !ScanGeometry.Finite(p.Radius)||p.Radius<0||!ScanGeometry.Finite(p.Length)||p.Length<0||!ScanGeometry.Finite(p.FitRms)||p.FitRms<0||
                    p.SupportIds==null||p.SupportIds.Length>2000||p.SupportIds.Any(id=>!ids.Contains(id))||p.ConstraintIds==null||p.ConstraintIds.Length>2048)
                    throw new InvalidDataException("Invalid primitive.");
            Annotations=review;
        }
    }
    /// <summary>Owns one paired-server snapshot. Asset URLs are constructed locally, never followed from response data.</summary>
    public sealed class CaptureReviewClient
    {
        private readonly IHttpTransport _transport;
        private readonly string _baseUrl,_token;
        private readonly string _certificate;
        private readonly McpClient _mcp;
        private bool _initialized;
        public CaptureReviewClient(IHttpTransport transport,PairedServer server)
        {
            _transport=transport??throw new ArgumentNullException(nameof(transport));
            _baseUrl=server.BaseUrl.TrimEnd('/')+"/capture/v1/sessions/";
            if(!Uri.TryCreate(_baseUrl,UriKind.Absolute,out var uri)||uri.Scheme!="https") throw new ArgumentException("Capture requires HTTPS.");
            _token=server.Token;_certificate=server.CertSha256;_mcp=new McpClient(transport,server.BaseUrl,server.Token);
        }
        public bool Matches(PairedServer server)=>server!=null&&server.BaseUrl.TrimEnd('/')+"/capture/v1/sessions/"==_baseUrl&&server.Token==_token&&server.CertSha256==_certificate;
        public async Task<JObject> CadToolAsync(string name,JObject args,CancellationToken ct)
        {
            if(!new[]{"inventor_new_document_safe","inventor_get_visual_revision","inventor_plan_change","inventor_commit_plan","inventor_save_document_safe"}.Contains(name)) throw new ArgumentException("Unsupported capture CAD operation.");
            if(!_initialized) {await _mcp.InitializeAsync(ct);_initialized=true;}
            return await _mcp.CallToolAsync(name,args,ct);
        }
        private static string Session(string id)
        { if(string.IsNullOrEmpty(id)||!System.Text.RegularExpressions.Regex.IsMatch(id,@"\A[a-zA-Z0-9_-]{1,80}\z")) throw new ArgumentException("Invalid session.");return id; }
        public Task<JObject> ListAsync(string id,CancellationToken ct)=>GetJsonAsync(_baseUrl+Session(id)+"/solutions",ct);
        public Task<JObject> SolutionAsync(string id,int revision,CancellationToken ct)=>GetJsonAsync(RevisionUrl(id,revision),ct);
        private string RevisionUrl(string id,int revision)
        { if(revision<1||revision>4) throw new ArgumentException("Invalid revision.");return _baseUrl+Session(id)+"/solutions/"+revision; }
        public async Task<ScanReview> OpenAsync(string id,int revision,CancellationToken ct)
        {
            string url=RevisionUrl(id,revision);var solution=await GetJsonAsync(url,ct);
            if(solution["solution"]?.Value<string>("State")!="succeeded"||solution["solution"]?.Value<string>("ScanId")!=id||solution["solution"]?.Value<int?>("Revision")!=revision) throw new IOException("Solution is not ready or identity differs.");
            var pointsJson=await AssetJsonAsync(url,solution,"points.json",ct);
            if(pointsJson.Value<string>("unit")!="mm"||pointsJson.Value<string>("coordinate_space")!="scan_metric") throw new InvalidDataException("Unexpected scan units.");
            var points=pointsJson["points"]?.ToObject<ScanPoint[]>();ScanGeometry.Validate(points);
            var quality=await AssetJsonAsync(url,solution,"quality_report.json",ct);
            var constraints=ParseToken(await AssetAsync(url,solution,"constraints.json",ct));
            var units=await AssetJsonAsync(url,solution,"units.json",ct);
            var annotations=await GetJsonAsync(url+"/review",ct);
            var glb=await AssetAsync(url,solution,"scan.glb",ct);
            return new ScanReview(id,revision,points,quality,constraints,units,annotations,glb);
        }
        public async Task<JObject> ControlAsync(string operation,JObject args,CancellationToken ct)
        {
            if(!_initialized) {await _mcp.InitializeAsync(ct);_initialized=true;}
            var result=await _mcp.CallToolAsync("inventor_reality_capture_"+operation,args,ct);
            if(result.Value<bool?>("ok")!=true) throw new IOException("Capture operation rejected.");return result;
        }
        public async Task<byte[]> ExportAsync(string id,int revision,string format,CancellationToken ct)
        {
            string name=format=="glb"?"scan.glb":format=="ply"?"pointcloud.ply":format=="obj"?"scan.obj":throw new ArgumentException("Invalid format.");
            string url=RevisionUrl(id,revision);var solution=await GetJsonAsync(url,ct);
            return await AssetAsync(url,solution,name,ct);
        }
        private async Task<JObject> AssetJsonAsync(string url,JObject solution,string name,CancellationToken ct)=>Parse(await AssetAsync(url,solution,name,ct));
        private async Task<byte[]> AssetAsync(string url,JObject solution,string name,CancellationToken ct)
        {
            var asset=(solution["assets"] as JArray)?.SingleOrDefault(a=>a.Value<string>("name")==name);
            long size=asset?.Value<long?>("size")??-1;string hash=asset?.Value<string>("sha256");
            int limit=name.EndsWith(".json",StringComparison.Ordinal)?1024*1024:32*1024*1024;
            if(size<1||size>limit||hash==null||!System.Text.RegularExpressions.Regex.IsMatch(hash,@"\A[0-9a-f]{64}\z")) throw new InvalidDataException("Invalid asset description.");
            var response=await SendAsync(url+"/assets/"+name,(int)size,ct);
            using(var sha=SHA256.Create())
            {
                string actual=BitConverter.ToString(sha.ComputeHash(response.Body)).Replace("-","").ToLowerInvariant();
                if(response.Body.Length!=size||actual!=hash||response.Header("X-Content-SHA256")!=hash) throw new InvalidDataException("Asset hash mismatch.");
            }
            return response.Body;
        }
        private async Task<JObject> GetJsonAsync(string url,CancellationToken ct)
        {var response=await SendAsync(url,1024*1024,ct);var result=Parse(response.Body);if(result.Value<bool?>("ok")!=true) throw new IOException("Capture request rejected.");return result;}
        private async Task<TransportResponse> SendAsync(string url,int max,CancellationToken ct)
        {
            var request=new TransportRequest("GET",url) {MaxResponseBytes=max};request.Headers["Authorization"]="Bearer "+_token;
            var result=await _transport.SendAsync(request,ct);if(!result.IsSuccess||result.Body==null||result.Body.Length>max) throw new IOException("Capture download failed.");return result;
        }
        private static JObject Parse(byte[] bytes)
        { using(var text=new StringReader(System.Text.Encoding.UTF8.GetString(bytes))) using(var reader=new JsonTextReader(text) {MaxDepth=32}) return JObject.Load(reader); }
        private static JToken ParseToken(byte[] bytes)
        { using(var text=new StringReader(System.Text.Encoding.UTF8.GetString(bytes))) using(var reader=new JsonTextReader(text) {MaxDepth=32}) return JToken.Load(reader); }
    }
}
