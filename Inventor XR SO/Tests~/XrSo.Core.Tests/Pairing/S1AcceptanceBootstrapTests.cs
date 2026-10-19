using System.Text;
using InventorXrSo.Core.Pairing;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace InventorXrSo.Core.Tests.Pairing;

public sealed class S1AcceptanceBootstrapTests
{
    private const string Run="0123456789abcdef0123456789abcdef";
    private static readonly DateTimeOffset Now=new(2026,10,15,4,0,0,TimeSpan.Zero);
    private static JObject Fixture()=>new()
    {
        ["schema"]=1,["run_id"]=Run,["expires_utc"]=Now.AddMinutes(20).ToString("O"),["host"]="127.0.0.1",["port"]=8443,
        ["cert_sha256"]=new string('a',64),["client_name"]="s1-qa-"+Run,["token"]=new string('t',32),["target_id"]="inventor-2027-1234",
        ["part_document_id"]="doc_{1234-ABCD}_hash",["sheet_document_id"]="doc_Sheet-5678"
    };
    private static byte[] Encode(JObject value)=>Encoding.UTF8.GetBytes(value.ToString(Formatting.None));
    private static S1AcceptanceBootstrap Parse(JObject value)=>S1AcceptanceBootstrap.Parse(Encode(value),Run,Now);

    [Fact] public void ParsesImmutableMetadataAndBuildsAnExplicitMemoryOnlyPairedServer()
    {
        var input=Fixture();var bootstrap=Parse(input);input["token"]="modified";input["part_document_id"]="doc_other";
        Assert.Equal(Run,bootstrap.Metadata.RunId);Assert.Equal("doc_{1234-ABCD}_hash",bootstrap.Metadata.PartDocumentId);
        Assert.Equal("doc_Sheet-5678",bootstrap.Metadata.SheetDocumentId);Assert.Equal("inventor-2027-1234",bootstrap.Metadata.TargetId);
        Assert.Equal(Now.AddMinutes(20),bootstrap.Metadata.ExpiresUtc);
        var server=bootstrap.CreatePairedServer(Now);Assert.Equal("127.0.0.1",server.Host);Assert.Equal(8443,server.Port);
        Assert.Equal(new string('t',32),server.Token);Assert.Equal("s1-qa-"+Run,server.ClientName);
        Assert.Equal("s1-"+Run+".bootstrap.json",S1AcceptanceBootstrap.FileName(Run));
        var metadataJson=JsonConvert.SerializeObject(bootstrap.Metadata);Assert.DoesNotContain("token",metadataJson,StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(new string('t',32),JsonConvert.SerializeObject(bootstrap));
    }
    [Theory]
    [InlineData("10.0.0.1")][InlineData("172.16.0.1")][InlineData("172.31.255.254")][InlineData("192.168.2.3")][InlineData("127.0.0.1")]
    public void AcceptsOnlyExplicitPrivateOrReverseTunnelHosts(string host)
    {var input=Fixture();input["host"]=host;Assert.Equal(host,Parse(input).CreatePairedServer(Now).Host);}
    [Theory]
    [InlineData("8.8.8.8")][InlineData("172.15.0.1")][InlineData("172.32.0.1")][InlineData("127.0.0.2")]
    [InlineData("localhost")][InlineData("192.168.001.1")][InlineData("0xc0a80101")][InlineData("192.168.1")]
    [InlineData("192.168.1.256")][InlineData("192.168.1.1/path")][InlineData("https://192.168.1.1")][InlineData("::1")]
    [InlineData("169.254.1.2")][InlineData("192.168.1.1\r\n")]
    public void RejectsPublicAmbiguousOrNonIpHosts(string host)
    {var input=Fixture();input["host"]=host;Assert.Throws<FormatException>(()=>Parse(input));}
    [Theory]
    [InlineData("schema")][InlineData("run_id")][InlineData("expires_utc")][InlineData("host")][InlineData("port")]
    [InlineData("cert_sha256")][InlineData("client_name")][InlineData("token")][InlineData("target_id")]
    [InlineData("part_document_id")][InlineData("sheet_document_id")]
    public void EveryFieldIsRequiredAndUnknownFieldsAreRejected(string field)
    {var input=Fixture();input.Remove(field);Assert.Throws<FormatException>(()=>Parse(input));input=Fixture();input["unexpected"]=true;Assert.Throws<FormatException>(()=>Parse(input));}
    [Theory]
    [InlineData("0")][InlineData("65536")][InlineData("1.5")][InlineData("\"8443\"")][InlineData("true")][InlineData("9999999999999999999999999999")]
    public void PortMustBeAnIntegerInRange(string port)
    {var input=Fixture();input["port"]=JToken.Parse(port);Assert.Throws<FormatException>(()=>Parse(input));}
    [Theory]
    [InlineData(0)][InlineData(-1)][InlineData(31)]
    public void RejectsExpiredOrOverlongLifetime(int minutes)
    {var input=Fixture();input["expires_utc"]=Now.AddMinutes(minutes).ToString("O");Assert.Throws<FormatException>(()=>Parse(input));}
    [Fact] public void AcceptsUtcRoundTripZAndUppercaseCertificateButNeverNonUtcOffset()
    {
        var input=Fixture();input["expires_utc"]=Now.AddMinutes(30).UtcDateTime.ToString("O");input["cert_sha256"]=new string('A',64);
        Assert.Equal(new string('a',64),Parse(input).CreatePairedServer(Now).CertSha256);
        input["expires_utc"]=Now.AddMinutes(20).ToOffset(TimeSpan.FromHours(1)).ToString("O");Assert.Throws<FormatException>(()=>Parse(input));
        input["expires_utc"]="2026-10-15T04:20:00";Assert.Throws<FormatException>(()=>Parse(input));
    }
    [Fact] public void SessionCreationRechecksExpiryRatherThanTrustingAnOldParse()
    {var bootstrap=Parse(Fixture());Assert.Throws<FormatException>(()=>bootstrap.CreatePairedServer(Now.AddMinutes(20)));Assert.Throws<FormatException>(()=>bootstrap.CreatePairedServer(Now.AddHours(-1)));}
    [Theory]
    [InlineData("../file")][InlineData("0123456789ABCDEF0123456789ABCDEF")][InlineData("")][InlineData("0123456789abcdef0123456789abcde")]
    public void InvalidRunIdCannotBecomeAFilenameOrMatchARequest(string run)
    {Assert.False(S1AcceptanceBootstrap.IsValidRunId(run));Assert.Throws<FormatException>(()=>S1AcceptanceBootstrap.FileName(run));Assert.Throws<FormatException>(()=>S1AcceptanceBootstrap.Parse(Encode(Fixture()),run,Now));}
    [Theory]
    [InlineData("inventor-2027-0")][InlineData("inventor-2027-01234")][InlineData("inventor-2027-2147483648")]
    [InlineData("inventor-2027-+1234")][InlineData("2027")][InlineData("inventor-2026-1234")]
    public void TargetMustBeAnExactCanonicalPositivePid(string target)
    {var input=Fixture();input["target_id"]=target;Assert.Throws<FormatException>(()=>Parse(input));}
    [Fact] public void FixtureDocumentsAreDistinctOpaqueIdsRatherThanPaths()
    {
        var input=Fixture();input["sheet_document_id"]=input["part_document_id"]!.DeepClone();Assert.Throws<FormatException>(()=>Parse(input));
        foreach(var invalid in new[]{"doc_","C:\\fixture.ipt","doc_a/b","doc_a b","doc_é","doc_"+new string('a',253)})
        {input=Fixture();input["part_document_id"]=invalid;Assert.Throws<FormatException>(()=>Parse(input));}
    }
    [Fact] public void TokenMustBeBoundedVisibleAsciiAndErrorsDoNotEchoIt()
    {
        foreach(var token in new[]{new string('t',31),new string('t',257),new string('t',32)+"\r\nsecret",new string('t',32)+" é",new string('t',32)+"\u007f"})
        {var input=Fixture();input["token"]=token;var error=Assert.Throws<FormatException>(()=>Parse(input));Assert.DoesNotContain(token,error.ToString());Assert.Null(error.InnerException);}
        var valid=Fixture();valid["token"]=new string('x',256);Assert.Equal(256,Parse(valid).CreatePairedServer(Now).Token.Length);
    }
    [Fact] public void MismatchedRunAndClientCannotReuseAnotherBootstrap()
    {
        var input=Fixture();input["run_id"]=new string('f',32);Assert.Throws<FormatException>(()=>Parse(input));
        input=Fixture();input["client_name"]="default";Assert.Throws<FormatException>(()=>Parse(input));
    }
    [Fact] public void BoundedUtf8ClosedJsonRejectsDuplicatesTrailingPayloadAndDeepStructures()
    {
        string json=Fixture().ToString(Formatting.None);
        foreach(var payload in new[]{"", "[]",json+"{}",json.Insert(1,"\"token\":\"never-echo-this-secret\","),json.Replace("\"host\":\"127.0.0.1\"","\"host\":[[[[[[1]]]]]]")})
        {
            var error=Assert.Throws<FormatException>(()=>S1AcceptanceBootstrap.Parse(Encoding.UTF8.GetBytes(payload),Run,Now));
            Assert.DoesNotContain("never-echo",error.ToString());Assert.Null(error.InnerException);
        }
        Assert.Throws<FormatException>(()=>S1AcceptanceBootstrap.Parse(new byte[]{0xc3,0x28},Run,Now));
        Assert.Throws<FormatException>(()=>S1AcceptanceBootstrap.Parse(new byte[S1AcceptanceBootstrap.MaxBytes+1],Run,Now));
        Assert.Throws<FormatException>(()=>S1AcceptanceBootstrap.Parse(null!,Run,Now));
    }
    [Fact] public void ExactMaximumBytesAreAcceptedButOneAdditionalByteIsRejected()
    {
        var json=Fixture().ToString(Formatting.None);var atMaximum=Encoding.UTF8.GetBytes(json+new string(' ',S1AcceptanceBootstrap.MaxBytes-Encoding.UTF8.GetByteCount(json)));
        Assert.NotNull(S1AcceptanceBootstrap.Parse(atMaximum,Run,Now));
        Assert.Throws<FormatException>(()=>S1AcceptanceBootstrap.Parse(atMaximum.Concat(new byte[]{32}).ToArray(),Run,Now));
    }
}
