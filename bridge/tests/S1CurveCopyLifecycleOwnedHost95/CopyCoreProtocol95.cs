using Bimwright.Ipt.Shared.Contracts;
using Inventor;
using Newtonsoft.Json.Linq;
using File = System.IO.File;
using Path = System.IO.Path;

internal static partial class Program
{
    private sealed record CoreBinding95(string DocumentId,string SketchKey,string ParameterKey,JObject SourceCurves);
    private static readonly (string Action,string Context,int Pass)[] Steps95={
        ("prepare","all",0),("edit","Part",1),("inspect","Part",1),("edit","Part",2),("inspect","Part",2),
        ("reopen","Part",0),("inspect_reopen","Part",0),("edit","Sheet",1),("inspect","Sheet",1),
        ("edit","Sheet",2),("inspect","Sheet",2),("reopen","Sheet",0),("inspect_reopen","Sheet",0),("complete","all",0) };
    private static int Step95;
    private static readonly HashSet<string> Nonces95=new(StringComparer.Ordinal);
    private static readonly Dictionary<bool,CoreBinding95> Bindings95=new();
    private static readonly Dictionary<bool,JObject> Initial95=new(),LastInspect95=new(),Rows95=new();
    private static JObject? Report95;

    private static void RunCopyCoreRequest95()
    {
        Report95??=new JObject { ["schema"]=1,["run_id"]=Settings.RunId,["target_id"]=TargetId,["status"]="RUNNING",
            ["source_updates"]=0,["reopens"]=0,["documents"]=new JArray(),["protocol"]=new JArray(),
            ["evidence"]="Native fixture mutations; Core first reads must precede inspect commands. No fabricated product commit or Quest proof.",
            ["quest_proof"]=false,["parameter_text_copy_supported"]=false };
        try
        {
            RequireIdle95(); Require89(DateTimeOffset.UtcNow<ExpiresUtc,"Core95 lifetime expired.");
            CheckInstallation73("installation_core95_before_action");
            string path=Path.Combine(Settings.OutputDirectory,"copy-core-request95.json");
            Require89(File.Exists(path)&&(File.GetAttributes(path)&FileAttributes.ReparsePoint)==0&&new FileInfo(path).Length is >0 and <=4096,"Invalid Core95 request file.");
            JObject request;
            using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))
            {
                Require89(stream.Length is >0 and <=4096,"Core95 locked request exceeds byte budget.");
                using var reader=new StreamReader(stream);
                request=JObject.Parse(reader.ReadToEnd(),new JsonLoadSettings { DuplicatePropertyNameHandling=DuplicatePropertyNameHandling.Error });
            }
            var names=new[]{"schema","run_id","nonce","ordinal","action","context","pass"};
            Require89(request.Count==names.Length&&request.Properties().All(p=>names.Contains(p.Name,StringComparer.Ordinal)),"Core95 request has unknown/missing fields.");
            Require89(request["schema"]?.Type==JTokenType.Integer&&(int)request["schema"]! ==1
                &&request["ordinal"]?.Type==JTokenType.Integer&&request["pass"]?.Type==JTokenType.Integer,"Core95 request integers invalid.");
            Require89(request["run_id"]?.Type==JTokenType.String&&(string?)request["run_id"]==Settings.RunId,"Core95 run differs.");
            string nonce=(string?)request["nonce"]??"";
            Require89(request["nonce"]?.Type==JTokenType.String&&System.Text.RegularExpressions.Regex.IsMatch(nonce,"\\A[0-9a-f]{32}\\z")&&!Nonces95.Contains(nonce),"Core95 nonce invalid or consumed.");
            Require89(Step95<Steps95.Length&&(int)request["ordinal"]! ==Step95+1,"Core95 ordinal is out of order or consumed.");
            var expected=Steps95[Step95];
            Require89(request["action"]?.Type==JTokenType.String&&request["context"]?.Type==JTokenType.String
                &&(string?)request["action"]==expected.Action&&(string?)request["context"]==expected.Context&&(int)request["pass"]! ==expected.Pass,"Core95 action/context/pass differs.");
            Nonces95.Add(nonce); Step95++;
            var trace=new JObject { ["nonce"]=nonce,["ordinal"]=Step95,["action"]=expected.Action,["context"]=expected.Context,["pass"]=expected.Pass,["state"]="consumed_before_action" };
            ((JArray)Report95!["protocol"]!).Add(trace); WritePublic73("copy-native-lifecycle95.json",Report95);
            bool sheet=expected.Context=="Sheet";
            Phase="core95_"+expected.Action+"_"+expected.Context+"_"+expected.Pass;
            switch(expected.Action)
            {
                case "prepare": PrepareCore95(); break;
                case "edit": EditCoreSource95(sheet,expected.Pass); break;
                case "inspect": InspectCoreEdit95(sheet,expected.Pass); break;
                case "reopen": ReopenCore95(sheet); break;
                case "inspect_reopen": InspectCoreReopen95(sheet); break;
                case "complete":
                    Require89((int)Report95!["source_updates"]! ==4&&(int)Report95!["reopens"]! ==2&&Rows95.Count==2,"Core95 native matrix incomplete.");
                    Report95!["final_native"]=Raw95("complete",FullState92());
                    Report95!["status"]="PASS_NATIVE_CURVE_COPY_LIFECYCLE_SUBSET"; break;
                default: throw new HostFailure73("Unreachable Core95 action.");
            }
            trace["state"]="completed";
            var ack=new JObject { ["schema"]=1,["run_id"]=Settings.RunId,["target_id"]=TargetId,["owned_pid"]=OwnedPid,["owned_start_utc_ticks"]=OwnedStart,
                ["nonce"]=nonce,["ordinal"]=Step95,["action"]=expected.Action,["context"]=expected.Context,["pass"]=expected.Pass,["status"]="ACK",
                ["expires_utc"]=ExpiresUtc.ToString("O"),["documents"]=AckDocuments95() };
            WritePublic73("copy-native-lifecycle95.json",Report95);
            WriteAckAtomic95(nonce,ack);
            string ackFile="copy-core-ack95-"+nonce+".json";
            trace["ack"]=new JObject { ["file"]=ackFile,["file_sha256"]=HashFile73(Path.Combine(Settings.OutputDirectory,ackFile)) };
            WritePublic73("copy-native-lifecycle95.json",Report95);
        }
        catch(Exception error)
        {
            Report95!["status"]="FAILED"; Report95!["failure"]=new JObject { ["phase"]=Phase,["type"]=error.GetType().FullName,["hresult"]=error.HResult };
            if(error is HostFailure73) Report95!["failure"]!["assertion"]=error.Message;
            WritePublic73("copy-native-lifecycle95.json",Report95); throw;
        }
    }
    private static void WriteAckAtomic95(string nonce,JObject ack)
    {
        string path=Path.Combine(Settings.OutputDirectory,"copy-core-ack95-"+nonce+".json"),temporary=path+".tmp";
        Require89(!File.Exists(path)&&!File.Exists(temporary),"Core95 acknowledgement destination already exists.");
        using(var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None))
        {
            var bytes=new System.Text.UTF8Encoding(false).GetBytes(ack.ToString(Newtonsoft.Json.Formatting.Indented)); stream.Write(bytes); stream.Flush(true);
        }
        File.Move(temporary,path); // Same private owner-only directory; atomic new filename, never overwrite.
    }
}
