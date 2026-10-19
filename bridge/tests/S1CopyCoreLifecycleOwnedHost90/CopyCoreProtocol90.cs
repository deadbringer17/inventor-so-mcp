using Bimwright.Ipt.Shared.Contracts;
using Inventor;
using Newtonsoft.Json.Linq;
using File = System.IO.File;
using Path = System.IO.Path;

internal static partial class Program
{
    private sealed record CoreBinding90(CopyBinding89 Source,string StartKey,string EndKey,string OriginKey,string ParameterTextKey,string ParameterKey);
    private static readonly (string Action,string Context,int Pass)[] Steps90={
        ("prepare","all",0),("edit","Part",1),("inspect","Part",1),("edit","Part",2),("inspect","Part",2),
        ("reopen","Part",0),("inspect_reopen","Part",0),("edit","Sheet",1),("inspect","Sheet",1),
        ("edit","Sheet",2),("inspect","Sheet",2),("reopen","Sheet",0),("inspect_reopen","Sheet",0),("complete","all",0) };
    private static int Step90;
    private static readonly HashSet<string> Nonces90=new(StringComparer.Ordinal);
    private static readonly Dictionary<bool,CoreBinding90> Bindings90=new();
    private static readonly Dictionary<bool,JObject> Initial90=new(),LastInspect90=new(),Rows90=new();
    private static JObject? Report90;

    private static void RunCopyCoreRequest90()
    {
        Report90??=new JObject { ["schema"]=1,["run_id"]=Settings.RunId,["target_id"]=TargetId,["status"]="RUNNING",
            ["source_updates"]=0,["reopens"]=0,["documents"]=new JArray(),["protocol"]=new JArray(),
            ["evidence"]="Native fixture mutations; Core first reads must precede inspect commands. No fabricated product commit or Quest proof.",
            ["quest_proof"]=false,["parameter_text_copy_supported"]=false };
        try
        {
            RequireIdle89(); Require89(DateTimeOffset.UtcNow<ExpiresUtc,"Core90 lifetime expired.");
            CheckInstallation73("installation_core90_before_action");
            string path=Path.Combine(Settings.OutputDirectory,"copy-core-request90.json");
            Require89(File.Exists(path)&&(File.GetAttributes(path)&FileAttributes.ReparsePoint)==0&&new FileInfo(path).Length is >0 and <=4096,"Invalid Core90 request file.");
            JObject request;
            using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))
            {
                Require89(stream.Length is >0 and <=4096,"Core90 locked request exceeds byte budget.");
                using var reader=new StreamReader(stream);
                request=JObject.Parse(reader.ReadToEnd(),new JsonLoadSettings { DuplicatePropertyNameHandling=DuplicatePropertyNameHandling.Error });
            }
            var names=new[]{"schema","run_id","nonce","ordinal","action","context","pass"};
            Require89(request.Count==names.Length&&request.Properties().All(p=>names.Contains(p.Name,StringComparer.Ordinal)),"Core90 request has unknown/missing fields.");
            Require89(request["schema"]?.Type==JTokenType.Integer&&(int)request["schema"]! ==1
                &&request["ordinal"]?.Type==JTokenType.Integer&&request["pass"]?.Type==JTokenType.Integer,"Core90 request integers invalid.");
            Require89(request["run_id"]?.Type==JTokenType.String&&(string?)request["run_id"]==Settings.RunId,"Core90 run differs.");
            string nonce=(string?)request["nonce"]??"";
            Require89(request["nonce"]?.Type==JTokenType.String&&System.Text.RegularExpressions.Regex.IsMatch(nonce,"\\A[0-9a-f]{32}\\z")&&!Nonces90.Contains(nonce),"Core90 nonce invalid or consumed.");
            Require89(Step90<Steps90.Length&&(int)request["ordinal"]! ==Step90+1,"Core90 ordinal is out of order or consumed.");
            var expected=Steps90[Step90];
            Require89(request["action"]?.Type==JTokenType.String&&request["context"]?.Type==JTokenType.String
                &&(string?)request["action"]==expected.Action&&(string?)request["context"]==expected.Context&&(int)request["pass"]! ==expected.Pass,"Core90 action/context/pass differs.");
            Nonces90.Add(nonce); Step90++;
            var trace=new JObject { ["nonce"]=nonce,["ordinal"]=Step90,["action"]=expected.Action,["context"]=expected.Context,["pass"]=expected.Pass,["state"]="consumed_before_action" };
            ((JArray)Report90!["protocol"]!).Add(trace); WritePublic73("copy-native-lifecycle90.json",Report90);
            bool sheet=expected.Context=="Sheet";
            Phase="core90_"+expected.Action+"_"+expected.Context+"_"+expected.Pass;
            switch(expected.Action)
            {
                case "prepare": PrepareCore90(); break;
                case "edit": EditCoreSource90(sheet,expected.Pass); break;
                case "inspect": InspectCoreEdit90(sheet,expected.Pass); break;
                case "reopen": ReopenCore90(sheet); break;
                case "inspect_reopen": InspectCoreReopen90(sheet); break;
                case "complete":
                    Require89((int)Report90!["source_updates"]! ==4&&(int)Report90!["reopens"]! ==2&&Rows90.Count==2,"Core90 native matrix incomplete.");
                    Report90!["status"]="PASS_NATIVE_CORE_LIFECYCLE_SUBSET"; break;
                default: throw new HostFailure73("Unreachable Core90 action.");
            }
            trace["state"]="completed";
            var ack=new JObject { ["schema"]=1,["run_id"]=Settings.RunId,["target_id"]=TargetId,["owned_pid"]=OwnedPid,["owned_start_utc_ticks"]=OwnedStart,
                ["nonce"]=nonce,["ordinal"]=Step90,["action"]=expected.Action,["context"]=expected.Context,["pass"]=expected.Pass,["status"]="ACK",
                ["expires_utc"]=ExpiresUtc.ToString("O"),["documents"]=AckDocuments90() };
            WritePublic73("copy-native-lifecycle90.json",Report90);
            WriteAckAtomic90(nonce,ack);
        }
        catch(Exception error)
        {
            Report90!["status"]="FAILED"; Report90!["failure"]=new JObject { ["phase"]=Phase,["type"]=error.GetType().FullName,["hresult"]=error.HResult };
            if(error is HostFailure73) Report90!["failure"]!["assertion"]=error.Message;
            WritePublic73("copy-native-lifecycle90.json",Report90); throw;
        }
    }
    private static void PrepareCore90()
    {
        VerifyCopies88();
        var client=JObject.Parse(File.ReadAllText(Path.Combine(Settings.OutputDirectory,"copy-client88.json")));
        Require89(Documents.Count==2,"Core90 requires two owned documents.");
        foreach(var owned in Documents)
        {
            var source=LifecycleBinding89(owned,client); var initial=ReadCopyLifecycle89(owned,source);
            var sketch=owned.Document.ComponentDefinition.Sketches.Cast<PlanarSketch>().Single(s=>Key73(s)==source.SketchKey);
            var line=sketch.SketchLines.Cast<SketchLine>().Single(l=>Key73(l)==source.LineKey);
            var text=sketch.TextBoxes.Cast<TextBox>().Single(t=>Key73(t)==source.TextKey);
            var parameter=owned.Document.ComponentDefinition.Parameters.UserParameters.Cast<UserParameter>().Single(p=>p.Name=="S1Copy88Length");
            var parameterText=((JArray)initial["sketch"]!["texts"]!).Single(t=>((string?)t["formatted_text"]??"").Contains("<Parameter",StringComparison.Ordinal));
            Require89((string?)parameterText["key"]!=source.TextKey,"Unselected Parameter fixture aliases copied literal source.");
            _=OwnedParameterRelocation89.Normalize(initial,owned.SavedPath!);
            Bindings90.Add(owned.Sheet,new CoreBinding90(source,Key73(line.StartSketchPoint),Key73(line.EndSketchPoint),Key73(text.OriginSketchPoint),(string)parameterText["key"]!,Key73(parameter)));
            Initial90.Add(owned.Sheet,initial); LastInspect90.Add(owned.Sheet,initial);
            var row=new JObject { ["context"]=owned.Sheet?"Sheet":"Part",["initial"]=initial.DeepClone(),["updates"]=new JArray(),["state"]="prepared" };
            Rows90.Add(owned.Sheet,row); ((JArray)Report90!["documents"]!).Add(row);
        }
    }
    private static OwnedDocument73 Owned90(bool sheet)=>Documents.Single(d=>d.Sheet==sheet);
    private static JArray AckDocuments90()
    {
        GuardDocuments73(); var result=new JArray();
        foreach(bool sheet in new[]{false,true})
        {
            var owned=Owned90(sheet);var b=Bindings90[sheet];var doc=owned.Document;
            Require89((doc.ComponentDefinition is SheetMetalComponentDefinition)==sheet,"Core90 current subtype differs from owned context.");
            var sketch=doc.ComponentDefinition.Sketches.Cast<PlanarSketch>().Single(s=>Key73(s)==b.Source.SketchKey);
            var line=sketch.SketchLines.Cast<SketchLine>().Single(l=>Key73(l)==b.Source.LineKey);
            var text=sketch.TextBoxes.Cast<TextBox>().Single(t=>Key73(t)==b.Source.TextKey);
            var parameterText=sketch.TextBoxes.Cast<TextBox>().Single(t=>Key73(t)==b.ParameterTextKey);
            var parameter=doc.ComponentDefinition.Parameters.UserParameters.Cast<UserParameter>().Single(p=>Key73(p)==b.ParameterKey);
            Require89(Key73(line.StartSketchPoint)==b.StartKey&&Key73(line.EndSketchPoint)==b.EndKey&&Key73(text.OriginSketchPoint)==b.OriginKey,"Core90 source point identity changed.");
            string Encode(object entity,string type)=>new PersistentEntityReference { DocumentId=owned.DocumentId,EntityType=type,Key=Convert.FromBase64String(Key73(entity)),Context=Array.Empty<byte>() }.Encode();
            result.Add(new JObject { ["context"]=sheet?"Sheet":"Part",["document_id"]=owned.DocumentId,["internal_name"]=doc.InternalName,
                ["native_path"]=doc.FullFileName,["display_name"]=doc.DisplayName,["subtype"]=sheet?"Sheet":"Part",["sketch_id"]=Encode(sketch,"sketch"),
                ["parameter_key"]=Key73(parameter),["native_database_revision_id"]=doc.DatabaseRevisionId,["source_ids"]=new JObject {
                    ["line"]=Encode(line,"sketch_entity"),["start"]=Encode(line.StartSketchPoint,"sketch_entity"),["end"]=Encode(line.EndSketchPoint,"sketch_entity"),
                    ["text"]=Encode(text,"sketch_text"),["origin"]=Encode(text.OriginSketchPoint,"sketch_entity"),["parameter_text"]=Encode(parameterText,"sketch_text") } });
        }
        GuardDocuments73(); return result;
    }
    private static void WriteAckAtomic90(string nonce,JObject ack)
    {
        string path=Path.Combine(Settings.OutputDirectory,"copy-core-ack90-"+nonce+".json"),temporary=path+".tmp";
        Require89(!File.Exists(path)&&!File.Exists(temporary),"Core90 acknowledgement destination already exists.");
        using(var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None))
        {
            var bytes=new System.Text.UTF8Encoding(false).GetBytes(ack.ToString(Newtonsoft.Json.Formatting.Indented)); stream.Write(bytes); stream.Flush(true);
        }
        File.Move(temporary,path); // Same private owner-only directory; atomic new filename, never overwrite.
    }
}

