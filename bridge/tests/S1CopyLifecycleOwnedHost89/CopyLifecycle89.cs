using System.Runtime.InteropServices;
using Bimwright.Ipt.Shared.Contracts;
using Inventor;
using Newtonsoft.Json.Linq;
using File = System.IO.File;
using Path = System.IO.Path;

internal static partial class Program
{
    private static bool CopyLifecycle89Done;
    private sealed record CopyBinding89(string DocumentId,string SketchKey,string LineKey,string TextKey);
    private static void Require89(bool value,string assertion) { if(!value) throw new HostFailure73(assertion); }
    private static void RequireIdle89()
    {
        GuardDocuments73();
        Require89(Application!.CommandManager.ActiveCommand=="AppSelectNorthwestArrowCmd","Native command prevents owned lifecycle89.");
        var current=Application.TransactionManager.CurrentTransaction;
        if(current==null) return;
        bool identified=true;
        try { _=current.HasParentTransaction; }
        catch(COMException error) when(error.ErrorCode==unchecked((int)0x80004005)) { identified=false; }
        Require89(!identified,"Identified transaction prevents lifecycle89.");
    }
    private static void RunCopyLifecycle89()
    {
        Require89((bool?)Evidence["http_stopped_before_lifecycle89"]==true&&Bearer==null,"Stop/dispose HTTP before native source edits and reopen.");
        RequireIdle89();
        var report=new JObject { ["schema"]=1,["run_id"]=Settings.RunId,["target_id"]=TargetId,["status"]="RUNNING_NATIVE_RESEARCH",
            ["evidence"]="Native owned source edits and SaveCopy/reopen after HTTP88; no post-edit Core/HTTPS or Quest proof",
            ["documents"]=new JArray(),["not_covered"]=new JArray("Post-source-update Core first snapshot", "Post-reopen HTTPS references", "Parameter-bound text copy", "Shared style edit independence", "Desktop/Quest/physical") };
        WritePublic73("copy-lifecycle89.json",report);
        try
        {
            // Refuse to run on an incomplete88 matrix. Verifier rechecks its exact
            // client keys and all eight native pairs before any lifecycle mutation.
            VerifyCopies88();
            var clientPath=Path.Combine(Settings.OutputDirectory,"copy-client88.json");
            var client=JObject.Parse(File.ReadAllText(clientPath));
            Require89((string?)client["run_id"]==Settings.RunId&&(string?)client["target_id"]==TargetId,"Lifecycle client binding differs.");
            foreach(var original in Documents.ToArray())
            {
                RequireIdle89(); original.Document.Activate(); RequireIdle89();
                var binding=LifecycleBinding89(original,client);
                var initial=ReadCopyLifecycle89(original,binding);
                var row=new JObject { ["context"]=original.Sheet?"Sheet":"Part",["original_document_id"]=original.DocumentId,
                    ["initial"]=initial,["updates"]=new JArray(),["state"]="before_updates" };
                ((JArray)report["documents"]!).Add(row); WritePublic73("copy-lifecycle89.json",report);
                for(int pass=1;pass<=2;pass++)
                {
                    RequireIdle89(); var doc=original.Document;
                    var sketch=doc.ComponentDefinition.Sketches.Cast<PlanarSketch>().Single(s=>Key73(s)==binding.SketchKey);
                    var line=sketch.SketchLines.Cast<SketchLine>().Single(s=>Key73(s)==binding.LineKey);
                    var text=sketch.TextBoxes.Cast<TextBox>().Single(t=>Key73(t)==binding.TextKey);
                    var prior=ReadCopyLifecycle89(original,binding); var fullBefore=CopyFullNative88();
                    var update=new JObject { ["pass"]=pass,["before"]=prior,["state"]="before_owned_transaction",
                        ["full_native_before_sha256"]=HashText73(fullBefore.ToString(Newtonsoft.Json.Formatting.None)) };
                    ((JArray)row["updates"]!).Add(update); WritePublic73("copy-lifecycle89.json",report);
                    Transaction? transaction=null; bool ended=false;
                    try
                    {
                        transaction=Application!.TransactionManager.StartTransaction((_Document)doc,"S1 Copy89 source independence native research");
                        Require89(!transaction.HasParentTransaction,"Lifecycle source mutation refuses nested transaction.");
                        var g=Application.TransientGeometry;
                        double sx=pass==1?8:13,sy=pass==1?14:4,ex=pass==1?40:29,ey=pass==1?22:9;
                        double tx=pass==1?41:16,ty=pass==1?37:44,scale=pass==1?1.5:0.75;
                        string markup=pass==1?"Source89 pass1 &amp; changed":"Source89 second content";
                        update["requested"]=new JObject { ["start_mm"]=new JArray(sx,sy),["end_mm"]=new JArray(ex,ey),
                            ["origin_mm"]=new JArray(tx,ty),["formatted_text"]=markup,["width_scale"]=scale };
                        Phase="copy89_move_source_line_start"; line.StartSketchPoint.MoveTo(g.CreatePoint2d(sx/10,sy/10));
                        Phase="copy89_move_source_line_end"; line.EndSketchPoint.MoveTo(g.CreatePoint2d(ex/10,ey/10));
                        Phase="copy89_change_source_literal"; text.FormattedText=markup;
                        Phase="copy89_move_source_literal"; text.Origin=g.CreatePoint2d(tx/10,ty/10);
                        Phase="copy89_change_source_text_override"; text.WidthScale=scale;
                        byte red=(byte)(pass==1?33:95),green=(byte)(pass==1?117:42),blue=(byte)(pass==1?155:61);
                        text.Color=Application.TransientObjects.CreateColor(red,green,blue,1);
                        update["requested"]!["color_rgb"]=new JArray((int)red,(int)green,(int)blue);
                        // These are explicit edit completion operations, before readback.
                        // No retry/Rebuild is performed after an assertion has failed.
                        Phase="copy89_source_solve"; sketch.Solve();
                        Phase="copy89_source_update"; Require89(doc.Update2(false),"Source update returned false.");
                        Phase="copy89_source_rebuild"; Require89(doc.Rebuild2(false),"Source text glyph regeneration returned false.");
                        var after=ReadCopyLifecycle89(original,binding);
                        update["native_readback_before_assertions"]=after;
                        Phase="copy89_check_source_readback";
                        CheckIndependence89(initial,after,binding);
                        var actualLine=(JObject)((JArray)after["sketch"]!["entities"]!).Single(e=>(string?)e["key"]==binding.LineKey);
                        NearPoint89(actualLine["start_mm"],sx,sy); NearPoint89(actualLine["end_mm"],ex,ey);
                        var actualText=(JObject)((JArray)after["sketch"]!["texts"]!).Single(e=>(string?)e["key"]==binding.TextKey);
                        Require89((string?)actualText["text"]==(pass==1?"Source89 pass1 & changed":"Source89 second content")
                            &&(string?)actualText["formatted_text"]==markup,"Native source text content differs.");
                        NearPoint89(actualText["origin_mm"],tx,ty);
                        Require89(Math.Abs((double)actualText["width_scale"]!-scale)<=1e-12,"Native source width override differs.");
                        var rawColor=(JObject)actualText["copy_details88"]!["color"]!;
                        Require89((int)rawColor["red"]! == red && (int)rawColor["green"]! == green && (int)rawColor["blue"]! == blue,
                            "Native source TextBox color property differs.");
                        Require89(((JArray)actualText["copy_details88"]!["raw_vertices_model_mm"]!).Count>0
                            &&((JArray)actualText["copy_details88"]!["raw_indices_native_one_based"]!).Count>0,"Updated source glyph geometry is empty.");
                        var priorText=((JArray)prior["sketch"]!["texts"]!).Single(e=>(string?)e["key"]==binding.TextKey);
                        Require89(!JToken.DeepEquals(priorText["copy_details88"]!["raw_vertices_model_mm"],actualText["copy_details88"]!["raw_vertices_model_mm"]),
                            "Source glyph vertices did not change after native content/position edit.");
                        update["before_commit_readback"]=after;
                        Require89(SameCom73(transaction,Application.TransactionManager.CurrentTransaction),"Source transaction ownership lost before End.");
                        Phase="copy89_commit_owned_source_update"; transaction.End(); ended=true;
                        update["state"]="owned_update_committed";
                        var committed=ReadCopyLifecycle89(original,binding); CheckIndependence89(initial,committed,binding);
                        Require89(JToken.DeepEquals(Semantic89(after),Semantic89(committed)),"Commit changed the checked native semantic state.");
                        update["after_commit_first_native_read"]=committed; update["copies_independent_exact"]=true;
                    }
                    catch
                    {
                        string failedPhase=Phase;
                        if(transaction!=null&&!ended)
                        {
                            Require89(SameCom73(transaction,Application!.TransactionManager.CurrentTransaction),"Source transaction ownership lost before Abort.");
                            Phase="copy89_abort_failed_source_update"; transaction.Abort();
                            var aborted=CopyFullNative88();
                            bool exact=JToken.DeepEquals(fullBefore,aborted); update["failure_abort_full_native_exact"]=exact;
                            update["full_native_after_abort_sha256"]=HashText73(aborted.ToString(Newtonsoft.Json.Formatting.None));
                            Require89(exact,"Failed source-update Abort differs; stop lifecycle.");
                        }
                        Phase=failedPhase;
                        throw;
                    }
                    finally { WritePublic73("copy-lifecycle89.json",report); }
                }
                var expected=ReadCopyLifecycle89(original,binding); row["before_persistence"]=expected;
                try { row["persistence"]=PersistCopyLifecycle89(original,binding,expected); }
                catch
                {
                    row["persistence"]=Evidence["copy_lifecycle89_persistence_transition"]?.DeepClone();
                    throw;
                }
                row["state"]="two_source_updates_and_reopen_observed";
                WritePublic73("copy-lifecycle89.json",report);
            }
            Require89(((JArray)report["documents"]!).Count==2,"Lifecycle requires both native contexts.");
            report["status"]="PASS_NATIVE_LIFECYCLE_SUBSET"; report["core_post_edit_proof"]=false; report["quest_proof"]=false;
        }
        catch(Exception error)
        {
            report["status"]="FAILED"; report["failure"]=new JObject { ["phase"]=Phase,["type"]=error.GetType().FullName,["hresult"]=error.HResult };
            if(error is HostFailure73) report["failure"]!["assertion"]=error.Message;
            throw;
        }
        finally { WritePublic73("copy-lifecycle89.json",report); }
    }

    private static CopyBinding89 LifecycleBinding89(OwnedDocument73 owned,JObject client)
    {
        var workflows=((JArray)client["cases"]!).OfType<JObject>().Where(w=>(string?)w["document_id"]==owned.DocumentId).ToArray();
        Require89(workflows.Length==3,"Three copy workflows required per owned document.");
        string Source(string type)
        {
            var ids=workflows.SelectMany(w=>((JArray)w["pairs"]!).OfType<JObject>()).Where(p=>(string?)p["type"]==type)
                .Select(p=>(string)p["source"]!["entity_id"]!).Distinct(StringComparer.Ordinal).ToArray();
            Require89(ids.Length==1,"Source pair identity is ambiguous.");
            var reference=PersistentEntityReference.Decode(ids[0]);
            Require89(reference.DocumentId==owned.DocumentId&&reference.EntityType==(type=="text"?"sketch_text":"sketch_entity")&&reference.Context.Length==0,"Source typed ownership differs.");
            return Convert.ToBase64String(reference.Key);
        }
        var sketchIds=workflows.SelectMany(w=>((JArray)w["pairs"]!).OfType<JObject>()).Select(p=>(string)p["source"]!["sketch_id"]!).Distinct(StringComparer.Ordinal).ToArray();
        Require89(sketchIds.Length==1,"Lifecycle source sketch is ambiguous.");
        var sketchRef=PersistentEntityReference.Decode(sketchIds[0]);
        Require89(sketchRef.DocumentId==owned.DocumentId&&sketchRef.EntityType=="sketch"&&sketchRef.Context.Length==0,"Lifecycle typed sketch ownership differs.");
        return new CopyBinding89(owned.DocumentId,Convert.ToBase64String(sketchRef.Key),Source("line"),Source("text"));
    }
    private static JObject ReadCopyLifecycle89(OwnedDocument73 owned,CopyBinding89 binding)
    {
        string previousPhase=Phase;
        GuardDocuments73(); var doc=owned.Document; string revision=doc.DatabaseRevisionId; bool dirty=doc.Dirty;
        var full=CopyFullNative88();
        var document=((JArray)full["documents"]!).Single(d=>(string?)d["document_id"]==owned.DocumentId);
        var sketch=((JArray)document["sketches"]!).Single(s=>(string?)s["key"]==binding.SketchKey);
        Require89((string?)sketch["name"]=="S1_Copy88","Exact source sketch key resolves to wrong name.");
        var styles=((JArray)full["local_text_styles88"]!).Single(d=>(string?)d["document_id"]==owned.DocumentId)["styles"]!;
        var users=new JArray(doc.ComponentDefinition.Parameters.UserParameters.Cast<UserParameter>().Select(p=>new JObject {
            ["key"]=Key73(p),["name"]=p.Name,["expression"]=p.Expression,["units"]=p.get_Units(),["value"]=JToken.FromObject(p.Value) }));
        Require89(revision==doc.DatabaseRevisionId&&dirty==doc.Dirty,"Lifecycle native read changed Dirty/DBRevision."); GuardDocuments73();
        var result=new JObject { ["document_id"]=owned.DocumentId,["internal_name"]=owned.InternalName,["database_revision_id"]=revision,["dirty"]=dirty,
            ["sketch"]=sketch.DeepClone(),["styles"]=styles.DeepClone(),["user_parameters"]=users };
        Phase=previousPhase; return result;
    }
    private static JObject Semantic89(JObject state)=>new(){["sketch"]=state["sketch"]!.DeepClone(),["styles"]=state["styles"]!.DeepClone(),["user_parameters"]=state["user_parameters"]!.DeepClone()};
    private static void CheckIndependence89(JObject initial,JObject after,CopyBinding89 binding)
    {
        var a=initial["sketch"]!;var b=after["sketch"]!;
        var sourceLine=((JArray)a["entities"]!).Single(e=>(string?)e["key"]==binding.LineKey);
        var sourceText=((JArray)a["texts"]!).Single(e=>(string?)e["key"]==binding.TextKey);
        var excluded=new HashSet<string>(new[]{binding.LineKey,(string)sourceLine["start_key"]!,(string)sourceLine["end_key"]!,
            (string)sourceText["copy_details88"]!["origin_key"]!},StringComparer.Ordinal);
        foreach(string collection in new[]{"entities","texts"})
        {
            var before=(JArray)a[collection]!;var current=(JArray)b[collection]!;
            Require89(before.Select(e=>(string)e["key"]!).ToHashSet(StringComparer.Ordinal).SetEquals(current.Select(e=>(string)e["key"]!)),"Native identity set changed during source updates.");
            foreach(var entity in before)
            {
                string key=(string)entity["key"]!;var same=current.Single(e=>(string?)e["key"]==key);
                if(collection=="texts"&&key==binding.TextKey)
                {
                    Require89((string?)entity["copy_details88"]!["origin_key"]==(string?)same["copy_details88"]!["origin_key"],"Source text origin native identity changed.");
                    continue;
                }
                if(!excluded.Contains(key)) Require89(JToken.DeepEquals(entity,same),"Copy or unrelated native geometry/properties/glyphs changed with its source.");
                if(key==binding.LineKey) Require89((string?)entity["start_key"]==(string?)same["start_key"]&&(string?)entity["end_key"]==(string?)same["end_key"],"Source line endpoint identity changed.");
            }
        }
        foreach(string field in new[]{"constraints","dimensions"}) Require89(JToken.DeepEquals(a[field],b[field]),"Native relation/dimension changed with a free source edit.");
        Require89(JToken.DeepEquals(initial["styles"],after["styles"]),"Shared native style changed; no style edits are permitted in independence test.");
        Require89(JToken.DeepEquals(initial["user_parameters"],after["user_parameters"]),"Unrelated user parameters changed.");
    }
    private static JObject PersistCopyLifecycle89(OwnedDocument73 original,CopyBinding89 binding,JObject expected)
    {
        RequireIdle89(); string originalPath=original.SavedPath??throw new HostFailure73("Saved original required.");
        string originalHash=HashFile73(originalPath),identity=original.InternalName;
        Require89(originalHash==original.BaselineSha,"Original baseline file changed before SaveCopy.");
        string destination=Path.GetFullPath(Path.Combine(FixtureDirectory,original.Sheet?"S1_CopyLifecycle89_Sheet_Reopen.ipt":"S1_CopyLifecycle89_Part_Reopen.ipt"));
        Require89(Path.GetDirectoryName(destination)==Path.GetFullPath(FixtureDirectory)&&!File.Exists(destination)&&!Directory.Exists(destination)&&!SamePath73(destination,originalPath),"SaveCopy destination not a new exact owned file.");
        var transition=new JObject { ["original_path"]=originalPath,["original_sha256"]=originalHash,["copy_path"]=destination,["state"]="before_save_copy" };
        Evidence["copy_lifecycle89_persistence_transition"]=transition; PersistOwnership73();
        Phase="copy89_save_copy"; original.Document.SaveAs(destination,true); GuardDocuments73();
        Require89(original.Document.InternalName==identity&&SamePath73(original.Document.FullFileName,originalPath)&&HashFile73(originalPath)==originalHash,"SaveCopy moved/wrote original.");
        string copyHash=HashFile73(destination); var afterSave=ReadCopyLifecycle89(original,binding);
        transition["after_save_copy_read"]=afterSave;
        Require89(JToken.DeepEquals(Semantic89(expected),Semantic89(afterSave)),"SaveCopy changed native semantic state.");
        transition["copy_sha256"]=copyHash; transition["state"]="copy_written_original_open"; PersistOwnership73();
        RequireIdle89(); Phase="copy89_close_original"; original.Document.Close(true); Documents.Remove(original); PersistOwnership73(); GuardDocuments73();
        transition["state"]="original_closed"; PersistOwnership73();
        Require89(!Application!.Documents.Cast<Document>().Any(d=>d.InternalName==identity)&&HashFile73(originalPath)==originalHash,"Original identity still open or disk baseline changed.");
        if(Marshal.IsComObject(original.Document)) Marshal.ReleaseComObject(original.Document);
        RequireIdle89(); Phase="copy89_open_saved_copy";
        transition["state"]="pending_open_identity_validation"; PersistOwnership73();
        var opened=(PartDocument)Application.Documents.Open(destination,true);
        GuardProcess73();
        Require89(opened.InternalName==identity&&SamePath73(opened.FullFileName,destination)
            &&(opened.ComponentDefinition is SheetMetalComponentDefinition)==original.Sheet
            &&Application.Documents.Count==Documents.Count+1,"Returned saved copy identity/path/subtype/inventory differs; do not adopt or close it.");
        var reopenedId=DocumentIdComposer.Compose(opened.InternalName,opened.FullFileName,false);
        Require89(reopenedId==DocumentIdComposer.Prefix+identity&&reopenedId==binding.DocumentId,"Returned copy composer identity differs; do not adopt or close it.");
        foreach(Document native in Application.Documents)
        {
            if(SameCom73(native,opened)) continue;
            Require89(Documents.Any(known=>SameCom73(known.Document,native)&&known.InternalName==native.InternalName
                &&known.SavedPath!=null&&SamePath73(known.SavedPath,native.FullFileName)),"Open introduced or changed an unrelated document; do not adopt the returned object.");
        }
        var adopted=new OwnedDocument73(opened,original.Sheet,opened.InternalName,destination){SavedPath=destination,BaselineSha=copyHash};
        adopted.DocumentId=reopenedId;
        Documents.Add(adopted); PersistOwnership73(); GuardDocuments73();
        Require89(opened.InternalName==identity&&SamePath73(opened.FullFileName,destination)&&(opened.ComponentDefinition is SheetMetalComponentDefinition)==original.Sheet,"Reopened copy identity/path/subtype differs.");
        // First native semantic read; no Solve/Update/Rebuild or new constructor oracle.
        Phase="copy89_reopen_first_native_read"; var first=ReadCopyLifecycle89(adopted,binding);
        transition["first_reopened_native_read"]=first;
        transition["raw_first_difference"]=FirstDifference89(Semantic89(expected),Semantic89(first),"$");
        var expectedPersistent=OwnedParameterRelocation89.Normalize(expected,originalPath);
        var actualPersistent=OwnedParameterRelocation89.Normalize(first,destination);
        transition["local_parameter_owner_relocation_verified"]=true;
        transition["first_difference"]=FirstDifference89(expectedPersistent,actualPersistent,"$");
        PersistOwnership73();
        Require89(JToken.DeepEquals(expectedPersistent,actualPersistent),"First reopened native state differs beyond the verified owned parameter-path relocation.");
        Require89(HashFile73(destination)==copyHash&&HashFile73(originalPath)==originalHash,"Reopen read changed owned disk bytes.");
        transition["first_reopened_native_read"]=first; transition["state"]="reopened_verified";
        transition["original_disk_unchanged"]=true; transition["saved_copy_disk_readonly"]=true; transition["old_http_reused"]=false;
        PersistOwnership73(); return transition;
    }
    private static void NearPoint89(JToken? token,double x,double y)
    {
        Require89(token is JArray p&&p.Count==2&&Math.Abs((double)p[0]-x)<=1e-5&&Math.Abs((double)p[1]-y)<=1e-5,"Native source point differs from requested millimetres.");
    }
    private static JObject? FirstDifference89(JToken? before,JToken? after,string path)
    {
        if(JToken.DeepEquals(before,after)) return null;
        if(before is JObject a&&after is JObject b)
        {
            foreach(string name in a.Properties().Select(p=>p.Name).Union(b.Properties().Select(p=>p.Name),StringComparer.Ordinal))
            {
                var difference=FirstDifference89(a[name],b[name],path+"."+name);
                if(difference!=null) return difference;
            }
        }
        if(before is JArray x&&after is JArray y&&x.Count==y.Count)
            for(int i=0;i<x.Count;i++)
            {
                var difference=FirstDifference89(x[i],y[i],path+"["+i+"]");
                if(difference!=null) return difference;
            }
        JToken Summary(JToken? value)=>value is JValue scalar ? scalar.DeepClone()
            : new JObject { ["type"]=value?.Type.ToString()??"missing",["count"]=value is JContainer container?container.Count:0 };
        return new JObject { ["path"]=path,["expected"]=Summary(before),["actual"]=Summary(after) };
    }
}
