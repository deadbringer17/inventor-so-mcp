using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

internal static partial class Program
{
    private static int CheckpointCount94;
    private static readonly HashSet<string> CheckpointNonces94=new(StringComparer.Ordinal);
    private static JObject ReadPublished94(string name,long maximum)
    {
        Require89(System.IO.Path.GetFileName(name)==name,"Invalid94 public filename.");
        string path=System.IO.Path.Combine(Settings.OutputDirectory,name);
        Require89((System.IO.File.GetAttributes(path)&System.IO.FileAttributes.ReparsePoint)==0,"Reparse94 public file refused.");
        using var stream=new System.IO.FileStream(path,System.IO.FileMode.Open,System.IO.FileAccess.Read,System.IO.FileShare.ReadWrite|System.IO.FileShare.Delete);
        Require89(stream.Length is >0&&stream.Length<=maximum,"Public94 byte budget differs.");
        using var input=new System.IO.StreamReader(stream);
        return JObject.Parse(input.ReadToEnd(),new JsonLoadSettings { DuplicatePropertyNameHandling=DuplicatePropertyNameHandling.Error });
    }
    private static void Checkpoint94()
    {
        GuardDocuments73();Require89(CheckpointCount94<74&&DateTimeOffset.UtcNow<ExpiresUtc,"Checkpoint94 count/lifetime exceeded.");
        var request=ReadPublished94("copy-checkpoint-request94.json",4096);
        string nonce=(string?)request["nonce"]??"";
        Require89(request.Count==2&&request.Properties().All(p=>p.Name is "run_id" or "nonce")
            &&request["run_id"]?.Type==JTokenType.String&&(string?)request["run_id"]==Settings.RunId
            &&request["nonce"]?.Type==JTokenType.String&&System.Text.RegularExpressions.Regex.IsMatch(nonce,"\\A[0-9a-f]{32}\\z")
            &&CheckpointNonces94.Add(nonce),"Checkpoint94 ownership/nonce differs.");
        var state=FullState92();string hash=HashText73(state.ToString(Formatting.None));
        string file="copy-native-checkpoint94-"+(++CheckpointCount94).ToString("D3")+".json";
        Require89(!System.IO.File.Exists(System.IO.Path.Combine(Settings.OutputDirectory,file)),"Checkpoint94 file already exists.");
        WritePublic73(file,new JObject { ["run_id"]=Settings.RunId,["nonce"]=nonce,["native_state_sha256"]=hash,["native_state"]=state });
        WritePublic73("copy-checkpoint-ack94.json",new JObject { ["run_id"]=Settings.RunId,["nonce"]=nonce,["native_state_sha256"]=hash,["file"]=file });
    }
}
