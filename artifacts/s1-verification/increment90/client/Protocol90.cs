using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using InventorXrSo.Core.Pairing;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

internal static partial class Program
{
    private sealed class OwnedMetadata90
    {
        internal JObject Raw=null!;
        internal string Context="",DocumentId="",InternalName="",Path="",DisplayName="",SketchId="",ParameterKey="",NativeRevision="";
        internal Dictionary<string,string> SourceIds=new(StringComparer.Ordinal);
    }
    private sealed class ProtocolState90
    {
        internal string Directory="",RunId="",TargetId="";
        internal int Ordinal,OwnedPid;
        internal long OwnedStart;
        internal DateTimeOffset Expiry;
        internal Dictionary<string,OwnedMetadata90> Documents=new(StringComparer.Ordinal);
        internal JArray Evidence=new();
    }
    private static readonly (string Action,string Context,int Pass)[] Steps90={
        ("prepare","all",0),("edit","Part",1),("inspect","Part",1),("edit","Part",2),("inspect","Part",2),
        ("reopen","Part",0),("inspect_reopen","Part",0),("edit","Sheet",1),("inspect","Sheet",1),
        ("edit","Sheet",2),("inspect","Sheet",2),("reopen","Sheet",0),("inspect_reopen","Sheet",0),("complete","all",0) };

    private static async Task PublishNew90(string path,JObject value,CancellationToken ct,bool replace=false)
    {
        string dir=Path.GetDirectoryName(Path.GetFullPath(path))!;
        Require(Directory.Exists(dir)&&(File.GetAttributes(dir)&FileAttributes.ReparsePoint)==0,"Public IPC directory must be the regular owned private run directory.");
        string temporary=Path.Combine(dir,".copy90-"+Guid.NewGuid().ToString("N")+".tmp");
        try
        {
            using(var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            {
                byte[] bytes=new UTF8Encoding(false).GetBytes(value.ToString(Formatting.Indented));
                await stream.WriteAsync(bytes,ct);stream.Flush(true);
            }
            File.Move(temporary,path,replace); // Same-directory atomic publication; no rewrite of an ack/report.
        }
        finally { if(File.Exists(temporary)) File.Delete(temporary); }
    }
    private static string MetadataText90(JObject value,string field,int maximum=131072)
    {
        Require(value[field]?.Type==JTokenType.String,"ACK metadata string field is missing.");
        string text=(string)value[field]!;
        Require(!string.IsNullOrWhiteSpace(text)&&text.Length<=maximum&&!text.Any(char.IsControl),"ACK metadata string field is invalid.");
        return text;
    }
    private static void Closed90(JObject value,params string[] fields)
    { Require(value.Count==fields.Length&&value.Properties().All(p=>fields.Contains(p.Name,StringComparer.Ordinal)),"IPC metadata fields differ from the closed protocol."); }
    private static bool SamePath90(string one,string two)=>string.Equals(System.IO.Path.GetFullPath(one),System.IO.Path.GetFullPath(two),StringComparison.OrdinalIgnoreCase);
    private static OwnedMetadata90 DocumentMetadata90(JObject raw,string context)
    {
        Closed90(raw,"context","document_id","internal_name","native_path","display_name","subtype","sketch_id","parameter_key","native_database_revision_id","source_ids");
        Require(MetadataText90(raw,"context",16)==context&&MetadataText90(raw,"subtype",16)==context,"ACK native context/subtype differs.");
        var result=new OwnedMetadata90 { Raw=(JObject)raw.DeepClone(),Context=context,
            DocumentId=MetadataText90(raw,"document_id",256),InternalName=MetadataText90(raw,"internal_name",256),
            Path=MetadataText90(raw,"native_path",32768),DisplayName=MetadataText90(raw,"display_name",1024),
            SketchId=MetadataText90(raw,"sketch_id"),ParameterKey=MetadataText90(raw,"parameter_key",65536),
            NativeRevision=MetadataText90(raw,"native_database_revision_id",256) };
        Require(result.DocumentId.StartsWith("doc_",StringComparison.Ordinal)&&Guid.TryParse(result.DocumentId.Substring(4),out var id)
            &&Guid.TryParse(result.InternalName,out var native)&&id==native,"ACK native internal/document GUID identity differs.");
        Require(System.IO.Path.IsPathFullyQualified(result.Path)&&System.IO.Path.GetExtension(result.Path).Equals(".ipt",StringComparison.OrdinalIgnoreCase)
            &&result.SketchId.StartsWith("ent_",StringComparison.Ordinal),"ACK native path/sketch identity differs.");
        try { Require(Convert.FromBase64String(result.ParameterKey).Length>0,"ACK native parameter key is empty."); }
        catch(FormatException) { throw new Assertion83("ACK parameter key is not Base64 native data."); }
        Require(raw["source_ids"] is JObject,"ACK native source identity map is missing.");
        var sources=(JObject)raw["source_ids"]!;
        Closed90(sources,"line","start","end","text","origin","parameter_text");
        foreach(string role in new[]{"line","start","end","text","origin","parameter_text"})
        { string value=MetadataText90(sources,role);Require(value.StartsWith("ent_",StringComparison.Ordinal),"ACK source identity is not opaque native data.");result.SourceIds.Add(role,value); }
        Require(result.SourceIds.Values.Distinct(StringComparer.Ordinal).Count()==6,"ACK native source roles collide.");
        return result;
    }
    private static async Task<ProtocolState90> Action90(ProtocolState90 protocol,S1AcceptanceBootstrapMetadata binding,
        string action,string context,int pass,CancellationToken ct)
    {
        int ordinal=protocol.Ordinal+1;
        Require(ordinal<=Steps90.Length&&Steps90[ordinal-1]==(action,context,pass),"Client lifecycle FSM action/order differs.");
        Require(DateTimeOffset.UtcNow<binding.ExpiresUtc,"Lifecycle bootstrap expired.");
        string nonce=Guid.NewGuid().ToString("N");
        var request=new JObject { ["schema"]=1,["run_id"]=binding.RunId,["nonce"]=nonce,["ordinal"]=ordinal,
            ["action"]=action,["context"]=context,["pass"]=pass };
        string ackPath=Path.Combine(protocol.Directory,"copy-core-ack90-"+nonce+".json");
        Require(!File.Exists(ackPath),"ACK nonce already exists; no replay is allowed.");
        Phase="protocol90_"+ordinal+"_"+action+"_"+context;
        await PublishNew90(Path.Combine(protocol.Directory,"copy-core-request90.json"),request,ct,true);
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromSeconds(120));
        JObject? ack=null;
        while(ack==null)
        {
            timeout.Token.ThrowIfCancellationRequested();
            if(File.Exists(ackPath))
            {
                Require((File.GetAttributes(ackPath)&FileAttributes.ReparsePoint)==0&&new FileInfo(ackPath).Length is >0 and <=1048576,"ACK exceeds the regular metadata file budget.");
                string json=await File.ReadAllTextAsync(ackPath,timeout.Token);
                using var input=new StringReader(json);using var reader=new JsonTextReader(input){MaxDepth=16,DateParseHandling=DateParseHandling.None};
                ack=JObject.Load(reader,new JsonLoadSettings{DuplicatePropertyNameHandling=DuplicatePropertyNameHandling.Error});
                Require(!reader.Read(),"ACK contains trailing JSON."); // Atomic ACK: malformed data fails, never CAD retry.
            }
            else await Task.Delay(100,timeout.Token); // Local IPC only; no HTTP in flight while host mutates.
        }
        Closed90(ack,"schema","run_id","target_id","owned_pid","owned_start_utc_ticks","nonce","ordinal","action","context","pass","status","expires_utc","documents");
        Require(ack["schema"]?.Type==JTokenType.Integer&&(int)ack["schema"]! ==1
            &&MetadataText90(ack,"run_id",32)==binding.RunId&&MetadataText90(ack,"target_id",128)==binding.TargetId
            &&MetadataText90(ack,"nonce",32)==nonce&&ack["ordinal"]?.Type==JTokenType.Integer&&(int)ack["ordinal"]! ==ordinal
            &&MetadataText90(ack,"action",32)==action&&MetadataText90(ack,"context",16)==context
            &&ack["pass"]?.Type==JTokenType.Integer&&(int)ack["pass"]! ==pass&&MetadataText90(ack,"status",16)=="ACK","ACK request/run/target binding differs.");
        Require(ack["owned_pid"]?.Type==JTokenType.Integer&&ack["owned_start_utc_ticks"]?.Type==JTokenType.Integer,"ACK process metadata is invalid.");
        int pid=(int)ack["owned_pid"]!;long start=(long)ack["owned_start_utc_ticks"]!;
        Require(pid>0&&start>0&&binding.TargetId=="inventor-2027-"+pid.ToString(CultureInfo.InvariantCulture),"ACK target process identity differs.");
        string expiry=MetadataText90(ack,"expires_utc",64);
        Require(DateTimeOffset.TryParseExact(expiry,"O",CultureInfo.InvariantCulture,DateTimeStyles.None,out var expires)
            &&expires.Offset==TimeSpan.Zero&&expires>DateTimeOffset.UtcNow&&expires<=binding.ExpiresUtc,"ACK TTL is stale or exceeds the bound bootstrap.");
        Require(ack["documents"] is JArray docs&&docs.Count==2&&docs.All(d=>d is JObject),"ACK must attest exactly two native documents.");
        var metadata=new Dictionary<string,OwnedMetadata90>(StringComparer.Ordinal);
        foreach(string kind in new[]{"Part","Sheet"})
        {
            var matching=((JArray)ack["documents"]!).OfType<JObject>().Where(d=>(string?)d["context"]==kind).ToArray();
            Require(matching.Length==1,"ACK context is missing or duplicated.");
            metadata.Add(kind,DocumentMetadata90(matching[0],kind));
        }
        Require(metadata["Part"].DocumentId!=metadata["Sheet"].DocumentId&&metadata["Part"].InternalName!=metadata["Sheet"].InternalName
            &&!SamePath90(metadata["Part"].Path,metadata["Sheet"].Path)
            &&SamePath90(Path.GetDirectoryName(metadata["Part"].Path)!,Path.GetDirectoryName(metadata["Sheet"].Path)!),"ACK documents collide or leave the common owned fixture directory.");
        if(protocol.Ordinal==0)
        {
            Require(metadata["Part"].DocumentId==binding.PartDocumentId&&metadata["Sheet"].DocumentId==binding.SheetDocumentId,"Prepare ACK changed original document identity.");
            protocol.OwnedPid=pid;protocol.OwnedStart=start;protocol.Expiry=expires;
        }
        else
        {
            Require(protocol.OwnedPid==pid&&protocol.OwnedStart==start&&protocol.Expiry==expires,"ACK owned process/lifetime changed.");
            foreach(string kind in new[]{"Part","Sheet"})
            {
                var previous=protocol.Documents[kind];var current=metadata[kind];
                Require(previous.DocumentId==current.DocumentId&&previous.InternalName==current.InternalName
                    &&previous.ParameterKey==current.ParameterKey&&previous.SketchId==current.SketchId
                    &&previous.SourceIds.All(p=>current.SourceIds[p.Key]==p.Value),"ACK native identities changed unexpectedly.");
                bool reopening=action=="reopen"&&kind==context;
                Require(reopening ? !SamePath90(previous.Path,current.Path)&&SamePath90(Path.GetDirectoryName(previous.Path)!,Path.GetDirectoryName(current.Path)!)
                    : SamePath90(previous.Path,current.Path),"ACK native path transition differs.");
            }
        }
        protocol.Ordinal=ordinal;protocol.Documents=metadata;
        protocol.Evidence.Add(new JObject{["request"]=request,["ack"]=ack});
        return protocol;
    }
}