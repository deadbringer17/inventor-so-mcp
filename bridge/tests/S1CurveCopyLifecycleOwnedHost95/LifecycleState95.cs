using System.Runtime.InteropServices;
using Bimwright.Ipt.Shared.Contracts;
using Inventor;
using Newtonsoft.Json.Linq;

internal static partial class Program
{
    private static readonly string[] CurveRoles95={"circle","arc_quarter","arc_wrap","arc_major","arc_clockwise"};
    private static int RawOrdinal95;
    private static readonly Dictionary<bool,JObject> LastFull95=new();
    private static readonly Dictionary<bool,OwnedUntouchedParameterRelocation95.Binding> UntouchedBindings95=new();
    private static OwnedDocument73 Owned95(bool sheet)=>Documents.Single(d=>d.Sheet==sheet);
    private static void RequireIdle95()
    {
        GuardDocuments73();Require89(Application!.CommandManager.ActiveCommand=="AppSelectNorthwestArrowCmd","Native command prevents95.");
        var transaction=Application.TransactionManager.CurrentTransaction;if(transaction==null)return;
        bool identified=true;try{_=transaction.HasParentTransaction;}catch(COMException error)when(error.ErrorCode==unchecked((int)0x80004005)){identified=false;}
        Require89(!identified,"Identified transaction prevents95.");
    }
    private static void VerifyPrerequisite95()
    {
        var ready=ReadPublished94("prerequisite94-ready.json",4096);
        Require89(ready.Count==1&&ready["run_id"]?.Type==JTokenType.String&&(string?)ready["run_id"]==Settings.RunId,"Baseline94 prerequisite owner differs.");
    }
    private static JObject Raw95(string stage,JObject state)
    {
        string file="copy-native-state95-"+(++RawOrdinal95).ToString("D3")+".json";
        string sha=HashText73(state.ToString(Newtonsoft.Json.Formatting.None));
        WritePublic73(file,new JObject { ["schema"]=1,["run_id"]=Settings.RunId,["target_id"]=TargetId,["ordinal"]=RawOrdinal95,
            ["stage"]=stage,["native_state_sha256"]=sha,["native_state"]=state });
        return new JObject { ["file"]=file,["file_sha256"]=HashFile73(System.IO.Path.Combine(Settings.OutputDirectory,file)),["native_state_sha256"]=sha };
    }
    private static JObject DocumentState95(JObject full,OwnedDocument73 owned)
    {
        var document=((JArray)full["documents"]!).Single(d=>(string?)d["document_id"]==owned.DocumentId);
        var styles=((JArray)full["local_text_styles88"]!).Single(d=>(string?)d["document_id"]==owned.DocumentId)["styles"]!;
        var users=new JArray(owned.Document.ComponentDefinition.Parameters.UserParameters.Cast<UserParameter>().Select(p=>new JObject {
            ["key"]=Key73(p),["name"]=p.Name,["expression"]=p.Expression,["units"]=p.get_Units(),["value"]=JToken.FromObject(p.Value) }));
        return new JObject { ["document_id"]=owned.DocumentId,["internal_name"]=owned.InternalName,
            ["sketch"]=((JArray)document["sketches"]!).Single(s=>(string?)s["name"]=="S1_Copy88").DeepClone(),
            ["styles"]=styles.DeepClone(),["user_parameters"]=users,["curve_graph"]=Graph94(full,owned).DeepClone() };
    }
    private static JObject PersistentSemantic95(JObject state,string path)
    {
        var canonical=OwnedParameterRelocation89.Normalize(state,path);canonical["curve_graph"]=state["curve_graph"]!.DeepClone();return canonical;
    }
    private static void PrepareCore95()
    {
        VerifyPrerequisite95();var verified=ReadPublished94("copy-native-verifier94.json",1024*1024);
        Require89((string?)verified["status"]=="PASS_NATIVE_CURVE_COPY_SUBSET"&&(string?)verified["run_id"]==Settings.RunId
            &&(string?)verified["target_id"]==TargetId&&((JArray)verified["checks"]!).Count==28&&CheckpointCount94==74,"Native94 verification required before95.");
        var release=ReadPublished94("prerequisite94-verified.json",4096);Require89(release.Count==1&&(string?)release["run_id"]==Settings.RunId,"Wrapper94 release required before95.");
        var full=FullState92();var raw=Raw95("prepare",full);
        foreach(var owned in Documents)
        {
            var initial=DocumentState95(full,owned);var graph=(JObject)initial["curve_graph"]!;var entities=Index94(graph["entities"]!);var curves=new JObject();
            foreach(string label in CurveRoles95)
            {
                string key=CreatorKeys94[owned.Sheet][label];var curve=entities[key];var points=(JObject)curve["structural_points"]!;
                var pointIds=new JObject();var pointKeys=new JObject();var pointMm=new JObject();var selected=new HashSet<string>(new[]{key},StringComparer.Ordinal);
                foreach(var point in points.Properties()){string pk=(string)point.Value["key"]!;pointIds[point.Name]=Encode94(owned.DocumentId,"sketch_entity",pk);pointKeys[point.Name]=pk;pointMm[point.Name]=point.Value["point_mm"]!.DeepClone();selected.Add(pk);}
                var anchors=new JArray();var constraintKeys=new JArray();
                foreach(JObject constraint in (JArray)graph["constraints"]!)
                {
                    var members=(JArray)constraint["members"]!;if(!members.Any(m=>selected.Contains((string)m["key"]!)))continue;
                    Require89((string?)constraint["binding_status"]=="COMPLETE_TYPED_MEMBERS"&&members.Count>0&&members.All(m=>selected.Contains((string)m["key"]!)),"Source95 relation has external/unknown binding.");
                    string first=(string)members[0]["key"]!;var entity=entities[first];JToken anchor=entity["kind"] switch {
                        null=>entity["point_mm"]!, JValue v when (string?)v=="circle"=>entity["structural_points"]!["center"]!["point_mm"]!,
                        JValue v when (string?)v=="arc"=>entity["structural_points"]!["start"]!["point_mm"]!, _=>throw new HostFailure73("Unsupported95 first constraint member anchor.") };
                    constraintKeys.Add(constraint["key"]!.DeepClone());anchors.Add(new JObject { ["constraint_key"]=constraint["key"]!.DeepClone(),["native_type"]=constraint["native_type"]!.DeepClone(),["members"]=members.DeepClone(),["anchor_mm"]=anchor.DeepClone() });
                }
                var angles=new JObject();foreach(string field in new[]{"start_angle_rad","sweep_angle_rad","geometry_start_angle_rad","geometry_sweep_angle_rad"})if(curve[field]!=null)angles[field]=curve[field]!.DeepClone();
                curves[label]=new JObject { ["entity_id"]=Encode94(owned.DocumentId,"sketch_entity",key),["entity_key"]=key,["point_ids"]=pointIds,["point_keys"]=pointKeys,["point_mm"]=pointMm,
                    ["constraint_keys"]=constraintKeys,["constraint_anchors"]=anchors,["initial_center_mm"]=points["center"]!["point_mm"]!.DeepClone(),["radius_mm"]=curve["radius_mm"]!.DeepClone(),["angles"]=angles,["domain"]=curve["domain"]!.DeepClone() };
            }
            var parameter=owned.Document.ComponentDefinition.Parameters.UserParameters.Cast<UserParameter>().Single(p=>p.Name=="S1Copy88Length");
            Bindings95.Add(owned.Sheet,new CoreBinding95(owned.DocumentId,Key73(CopySketch94(owned)),Key73(parameter),curves));
            _=PersistentSemantic95(initial,owned.SavedPath!);Initial95.Add(owned.Sheet,initial);LastInspect95.Add(owned.Sheet,initial);LastFull95.Add(owned.Sheet,full);
            var nativeDocument=(JObject)((JArray)full["documents"]!).Single(d=>(string?)d["document_id"]==owned.DocumentId);
            var untouched=OwnedUntouchedParameterRelocation95.Attest(nativeDocument,owned.SavedPath!);UntouchedBindings95.Add(owned.Sheet,untouched);
            var row=new JObject { ["context"]=owned.Sheet?"Sheet":"Part",["initial"]=raw.DeepClone(),["updates"]=new JArray(),["state"]="prepared",["untouched_parameter_binding95"]=untouched.Json() };
            Rows95.Add(owned.Sheet,row);((JArray)Report95!["documents"]!).Add(row);
        }
    }
    private static JArray AckDocuments95()
    {
        GuardDocuments73();var rows=new JArray();
        foreach(bool sheet in new[]{false,true})
        {
            var owned=Owned95(sheet);var binding=Bindings95[sheet];var ids=new JObject();foreach(var pair in CreatorKeys94[sheet])ids[pair.Key]=Encode94(owned.DocumentId,pair.Key is "text" or "parameter_text"?"sketch_text":"sketch_entity",pair.Value);
            rows.Add(new JObject { ["context"]=sheet?"Sheet":"Part",["document_id"]=owned.DocumentId,["internal_name"]=owned.Document.InternalName,
                ["native_path"]=owned.Document.FullFileName,["display_name"]=owned.Document.DisplayName,["subtype"]=sheet?"Sheet":"Part",
                ["sketch_id"]=Encode94(owned.DocumentId,"sketch",binding.SketchKey),["parameter_key"]=binding.ParameterKey,["native_database_revision_id"]=owned.Document.DatabaseRevisionId,
                ["source_ids"]=ids,["source_curves"]=binding.SourceCurves.DeepClone() });
        }
        return rows;
    }
}
