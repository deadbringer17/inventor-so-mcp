using System.Diagnostics;
using System.Text;
using Bimwright.Ipt.Server;
using Bimwright.Ipt.Shared.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Tests;

public sealed class PluginClientRequestBudgetTests
{
    private static InventorCommandEnvelope Envelope(string value,string token="test-auth") => new()
    {Id=Guid.Empty,Command="atomic_batch",Params=new JObject {["value"]=value},TimeoutMs=30000,AuthToken=token};

    [Theory]
    [InlineData("a")][InlineData("é")][InlineData("中")][InlineData("😀")][InlineData("\"\\\n")]
    public void BudgetCountsSerializedUtf8IncludingEnvelopeAndEscapes(string character)
    {
        int empty=Encoding.UTF8.GetByteCount(JsonConvert.SerializeObject(Envelope("")));
        int encoded=Encoding.UTF8.GetByteCount(JsonConvert.SerializeObject(character))-2;
        int count=(InventorTransportLimits.MaxRequestLineBytes-empty)/encoded;
        string value=string.Concat(Enumerable.Repeat(character,count));
        var line=PluginClient.SerializeRequestLine(Envelope(value));
        Assert.EndsWith("\n",line);
        Assert.InRange(Encoding.UTF8.GetByteCount(line)-1,InventorTransportLimits.MaxRequestLineBytes-encoded+1,InventorTransportLimits.MaxRequestLineBytes);
        var failure=Assert.Throws<InventorGatewayException>(()=>PluginClient.SerializeRequestLine(Envelope(value+character)));
        Assert.Equal(InventorErrorCodes.INVALID_ARGUMENT,failure.Code);
        Assert.True((int)failure.Details!["request_bytes"]!>InventorTransportLimits.MaxRequestLineBytes);
        Assert.Equal(InventorTransportLimits.MaxRequestLineBytes,(int)failure.Details["max_request_bytes"]!);
        Assert.DoesNotContain("test-auth",failure.Message);
    }

    [Fact]
    public void ExactLimitIsAcceptedAndAuthenticationOverheadIsIncluded()
    {
        int overhead=Encoding.UTF8.GetByteCount(JsonConvert.SerializeObject(Envelope("")));
        string value=new('a',InventorTransportLimits.MaxRequestLineBytes-overhead);
        Assert.Equal(InventorTransportLimits.MaxRequestLineBytes+1,Encoding.UTF8.GetByteCount(PluginClient.SerializeRequestLine(Envelope(value))));
        Assert.Throws<InventorGatewayException>(()=>PluginClient.SerializeRequestLine(Envelope(value,"test-auth-longer")));
    }

    [Fact]
    public async Task OversizedSendRefusesBeforeOpeningTheTransport()
    {
        using var targets=new Targets();var client=targets.Client();
        var timer=Stopwatch.StartNew();
        var failure=await Assert.ThrowsAsync<InventorGatewayException>(()=>client.SendAsync("atomic_batch",
            new JObject {["content"]=new string('a',InventorTransportLimits.MaxRequestLineBytes)},CancellationToken.None));
        Assert.Equal(InventorErrorCodes.INVALID_ARGUMENT,failure.Code);
        Assert.True(timer.Elapsed<TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task PinnedTransferCannotFollowAnExplicitTargetSwitch()
    {
        using var targets=new Targets();var client=targets.Client();
        Assert.Equal(targets.First,client.CurrentTarget!.TargetId);
        var pinned=client.CapturePinnedTarget();
        Assert.True(client.SwitchTarget(targets.Second));
        var failure=await Assert.ThrowsAsync<InventorGatewayException>(()=>client.SendPinnedAsync(pinned,"stage_import_chunk",new JObject(),CancellationToken.None));
        Assert.Equal(InventorErrorCodes.TARGET_UNAVAILABLE,failure.Code);
    }

    [Theory][InlineData("auth_token")][InlineData("pipe_name")]
    public async Task ReusedTargetIdCannotMoveAPinnedTransferToNewCredentialsOrEndpoint(string property)
    {
        using var targets=new Targets();var client=targets.Client();var pinned=client.CapturePinnedTarget();
        targets.ReplaceProperty(property,"replacement");
        var failure=await Assert.ThrowsAsync<InventorGatewayException>(()=>client.SendPinnedAsync(pinned,"stage_import_chunk",new JObject(),CancellationToken.None));
        Assert.Equal(InventorErrorCodes.TARGET_UNAVAILABLE,failure.Code);
        Assert.DoesNotContain("replacement",failure.Message);
    }

    private sealed class Targets : IDisposable
    {
        private readonly string _directory=Path.Combine(Path.GetTempPath(),"s1-request-budget-"+Guid.NewGuid().ToString("N"));
        internal string First=>"inventor-2027-"+Environment.ProcessId;
        internal string Second=>First+"-second";
        internal Targets()
        {
            Directory.CreateDirectory(_directory);
            foreach(var id in new[] {First,Second})
                File.WriteAllText(Path.Combine(_directory,id+".json"),JsonConvert.SerializeObject(new TargetDescriptor
                {TargetId=id,InventorYear=2027,ProcessId=Environment.ProcessId,HostApp="Inventor",Transport="pipe",
                    PipeName="s1-absent-"+Guid.NewGuid().ToString("N"),AuthToken="test-auth",LastHeartbeatUtc=DateTimeOffset.UtcNow}));
        }
        internal void ReplaceProperty(string property,string value)
        { string path=Path.Combine(_directory,First+".json");var json=JObject.Parse(File.ReadAllText(path));json[property]=value;File.WriteAllText(path,json.ToString(Formatting.None)); }
        internal PluginClient Client()=>new(new InventorMcpConfig {DescriptorDirectory=_directory,TargetId=First,TimeoutMs=30000},null,null,TimeSpan.Zero);
        public void Dispose()=>Directory.Delete(_directory,true);
    }
}
