using InventorXrSo.Core.Pairing;
using Newtonsoft.Json.Linq;
using System.Text.RegularExpressions;

internal static partial class Program
{
    private static readonly JArray CheckpointTrace94=new();
    private static async Task<JObject> Checkpoint94(string reportPath,S1AcceptanceBootstrapMetadata binding,string context,string workflow,string stage,CancellationToken ct)
    {
        Phase="checkpoint94_"+stage;Require(binding.ExpiresUtc>DateTimeOffset.UtcNow&&CheckpointTrace94.Count<74,"Checkpoint lifetime/count exceeded.");
        string dir=Path.GetDirectoryName(Path.GetFullPath(reportPath))!,nonce=Guid.NewGuid().ToString("N");
        string requestPath=Path.Combine(dir,"copy-checkpoint-request94.json"),ackPath=Path.Combine(dir,"copy-checkpoint-ack94.json");
        if(File.Exists(ackPath)){Require((File.GetAttributes(ackPath)&FileAttributes.ReparsePoint)==0,"Checkpoint ACK reparse forbidden.");File.Delete(ackPath);}
        var request=new JObject { ["run_id"]=binding.RunId,["nonce"]=nonce };await Publish94(requestPath,request,ct,true);
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromSeconds(30));
        while(true)
        {
            timeout.Token.ThrowIfCancellationRequested();JObject? ack=null;
            if(File.Exists(ackPath))
            {
                try{ack=await ReadPublic94(ackPath,8192,timeout.Token);}
                catch(IOException){}
                catch(UnauthorizedAccessException){}
            }
            if(ack!=null)
            {
                Closed94(ack,"run_id","nonce","native_state_sha256","file");
                Require(Text94(ack,"run_id",32)==binding.RunId&&Text94(ack,"nonce",32)==nonce,"Checkpoint ACK run/nonce mismatch; no replay permitted.");
                string hash=Text94(ack,"native_state_sha256",64),file=Text94(ack,"file",128);int ordinal=CheckpointTrace94.Count+1;
                Require(Regex.IsMatch(hash,"\\A[A-F0-9]{64}\\z")&&file=="copy-native-checkpoint94-"+ordinal.ToString("D3",System.Globalization.CultureInfo.InvariantCulture)+".json","Checkpoint digest/file ordinal differs.");
                var trace=new JObject { ["ordinal"]=ordinal,["context"]=context,["workflow"]=workflow,["stage"]=stage,["request"]=request,["ack"]=ack };
                CheckpointTrace94.Add(trace);return (JObject)trace.DeepClone();
            }
            await Task.Delay(100,timeout.Token); // IPC wait only; no CAD retry/read/repair.
        }
    }
}
