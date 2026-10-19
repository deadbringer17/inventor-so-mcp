using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace InventorXrSo.Core.Pairing
{
    /// <summary>Non-secret, immutable fixture binding available to the QA runner.</summary>
    public sealed class S1AcceptanceBootstrapMetadata
    {
        internal S1AcceptanceBootstrapMetadata(string runId,DateTimeOffset expires,string targetId,string part,string sheet)
        {RunId=runId;ExpiresUtc=expires;TargetId=targetId;PartDocumentId=part;SheetDocumentId=sheet;}
        public string RunId {get;}
        public DateTimeOffset ExpiresUtc {get;}
        public string TargetId {get;}
        public string PartDocumentId {get;}
        public string SheetDocumentId {get;}
    }

    /// <summary>QA bootstrap only; never persists credentials. Error messages never contain input or parser details.</summary>
    public sealed class S1AcceptanceBootstrap
    {
        public const int MaxBytes=16*1024;
        private readonly string _host,_pin,_client,_token;
        private readonly int _port;
        private S1AcceptanceBootstrap(S1AcceptanceBootstrapMetadata metadata,string host,int port,string pin,string client,string token)
        {Metadata=metadata;_host=host;_port=port;_pin=pin;_client=client;_token=token;}
        public S1AcceptanceBootstrapMetadata Metadata {get;}
        public static bool IsValidRunId(string value)=>Hex(value,32,true);
        public static string FileName(string runId)
        {if(!IsValidRunId(runId)) throw Invalid();return "s1-"+runId+".bootstrap.json";}

        public static S1AcceptanceBootstrap Parse(byte[] utf8,string expectedRunId,DateTimeOffset now)
        {
            try
            {
                if(utf8==null || utf8.Length<1 || utf8.Length>MaxBytes || !IsValidRunId(expectedRunId)) throw Invalid();
                var json=new UTF8Encoding(false,true).GetString(utf8);
                using(var reader=new JsonTextReader(new StringReader(json)){DateParseHandling=DateParseHandling.None,MaxDepth=4})
                {
                    var value=JObject.Load(reader,new JsonLoadSettings{DuplicatePropertyNameHandling=DuplicatePropertyNameHandling.Error});
                    if(reader.Read()) throw Invalid();
                    var fields=new[]{"schema","run_id","expires_utc","host","port","cert_sha256","client_name","token","target_id","part_document_id","sheet_document_id"};
                    if(value.Count!=fields.Length || value.Properties().Any(p=>!fields.Contains(p.Name,StringComparer.Ordinal)) || Integer(value,"schema",1,1)!=1) throw Invalid();
                    string runId=Text(value,"run_id"),expiresText=Text(value,"expires_utc"),host=Text(value,"host");
                    if(runId!=expectedRunId || !IsValidRunId(runId) || !HostAllowed(host)) throw Invalid();
                    if(!(expiresText.EndsWith("Z",StringComparison.Ordinal) || expiresText.EndsWith("+00:00",StringComparison.Ordinal))
                        || !DateTimeOffset.TryParseExact(expiresText,"O",CultureInfo.InvariantCulture,DateTimeStyles.None,out var expires)
                        || expires.Offset!=TimeSpan.Zero || !ValidTime(expires,now)) throw Invalid();
                    int port=Integer(value,"port",1,65535);
                    string pin=Text(value,"cert_sha256"),client=Text(value,"client_name"),token=Text(value,"token");
                    if(!Hex(pin,64,false) || client!="s1-qa-"+runId || token.Length<32 || token.Length>256 || token.Any(c=>c<0x21 || c>0x7e)) throw Invalid();
                    string target=Text(value,"target_id"),part=Text(value,"part_document_id"),sheet=Text(value,"sheet_document_id");
                    const string targetPrefix="inventor-2027-";
                    if(!target.StartsWith(targetPrefix,StringComparison.Ordinal) || !int.TryParse(target.Substring(targetPrefix.Length),NumberStyles.None,CultureInfo.InvariantCulture,out int pid)
                        || pid<1 || target!=targetPrefix+pid.ToString(CultureInfo.InvariantCulture) || !DocumentId(part) || !DocumentId(sheet) || part==sheet) throw Invalid();
                    return new S1AcceptanceBootstrap(new S1AcceptanceBootstrapMetadata(runId,expires,target,part,sheet),host,port,pin.ToLowerInvariant(),client,token);
                }
            }
            catch(Exception ex) when(ex is JsonException || ex is DecoderFallbackException || ex is ArgumentException || ex is OverflowException || ex is FormatException)
            {throw Invalid();}
        }
        public PairedServer CreatePairedServer(DateTimeOffset now)
        {
            if(!ValidTime(Metadata.ExpiresUtc,now)) throw Invalid();
            return new PairedServer(_host,_port,_pin,_client,_token);
        }
        private static bool ValidTime(DateTimeOffset expires,DateTimeOffset now)=>expires>now && expires-now<=TimeSpan.FromMinutes(30);
        private static bool HostAllowed(string host)
        {
            if(host=="127.0.0.1") return true; // QA may use an explicitly configured adb reverse tunnel.
            var fields=host.Split('.');if(fields.Length!=4) return false;
            var bytes=new int[4];
            for(int i=0;i<4;i++)
                if(fields[i].Length<1 || fields[i].Length>3 || fields[i].Any(c=>c<'0' || c>'9') || fields[i].Length>1 && fields[i][0]=='0'
                    || !int.TryParse(fields[i],NumberStyles.None,CultureInfo.InvariantCulture,out bytes[i]) || bytes[i]>255) return false;
            return bytes[0]==10 || bytes[0]==172 && bytes[1]>=16 && bytes[1]<=31 || bytes[0]==192 && bytes[1]==168;
        }
        private static bool DocumentId(string value)=>value.StartsWith("doc_",StringComparison.Ordinal) && value.Length>4 && value.Length<=256
            && value.Substring(4).All(c=>c>='a' && c<='z' || c>='A' && c<='Z' || c>='0' && c<='9' || c=='_' || c=='-' || c=='{' || c=='}');
        private static bool Hex(string value,int length,bool lower)=>value!=null && value.Length==length
            && value.All(c=>c>='0' && c<='9' || c>='a' && c<='f' || !lower && c>='A' && c<='F');
        private static string Text(JObject value,string key)
        {if(value[key]?.Type!=JTokenType.String) throw Invalid();return (string)value[key];}
        private static int Integer(JObject value,string key,int minimum,int maximum)
        {
            if(value[key]?.Type!=JTokenType.Integer) throw Invalid();
            long number=(long)value[key];if(number<minimum || number>maximum) throw Invalid();return (int)number;
        }
        private static FormatException Invalid()=>new FormatException("Bootstrap S1 QA non valido o scaduto.");
    }
}
