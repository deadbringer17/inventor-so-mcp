using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Mcp;
using InventorXrSo.Core.Net;
using InventorXrSo.Core.Pairing;
using Newtonsoft.Json.Linq;

internal static partial class Program
{
    private static async Task<int> Main(string[] args)
    {
        if(args.Length!=3||File.Exists(args[2])||Path.GetFileName(args[2])!="copy-client90.json") return 64;
        var report=new JObject { ["schema"]=1,["status"]="RUNNING",["cases"]=new JArray(),["refusals"]=new JArray(),
            ["evidence"]="Real Core pinned HTTPS, synthetic PC preview confirmation, immediate first native snapshot; no Quest or desktop equivalence",
            ["not_covered"]=new JArray("Quest/UI/physical", "Persistence/reopen", "Two source updates", "Arbitrary rich text/fonts/styles", "Creator-call absence trace; source preflight review is separate evidence") };
        byte[]? bytes=null; int exit=1;
        try
        {
            using var deadline=new CancellationTokenSource(TimeSpan.FromMinutes(24)); var ct=deadline.Token;
            Phase="bootstrap";
            Require((File.GetAttributes(args[1])&FileAttributes.ReparsePoint)==0,"Bootstrap reparse file forbidden.");
            using(var stream=new FileStream(args[1],FileMode.Open,FileAccess.Read,FileShare.None))
            {
                Require(stream.Length is >=1 and <=S1AcceptanceBootstrap.MaxBytes,"Bootstrap size differs.");
                bytes=new byte[checked((int)stream.Length)]; await stream.ReadExactlyAsync(bytes,ct);
                Require(stream.ReadByte()==-1,"Bootstrap changed during read.");
            }
            File.Delete(args[1]); var bootstrap=S1AcceptanceBootstrap.Parse(bytes,args[0],DateTimeOffset.UtcNow);
            Array.Clear(bytes); bytes=null; var binding=bootstrap.Metadata; var paired=bootstrap.CreatePairedServer(DateTimeOffset.UtcNow);
            report["run_id"]=binding.RunId; report["target_id"]=binding.TargetId;
            using var transport=new ProbeTransport(ServerTrust.Pinned(paired.CertSha256));
            var backend=new InventorBackend(transport,paired,new MemoryAssetCache()); await backend.ConnectAsync(ct);
            await Inventory(backend,binding,ct); int group=0;
            foreach(bool sheet in new[]{false,true})
            {
                string id=sheet?binding.SheetDocumentId:binding.PartDocumentId;
                Phase=sheet?"initialize_sheet":"initialize_part";
                Require(binding.ExpiresUtc>DateTimeOffset.UtcNow,"Binding expired."); await backend.ActivateOpenAsync(id,ct);
                var state=await backend.GetDocumentStateAsync(ct); Require(state.DocumentId==id,"Activation ownership differs.");
                Phase=sheet?"initialize_sheet_context":"initialize_part_context";
                DesignContext context;
                int contextReads=0;
                while(true)
                {
                    try { context=await backend.GetDesignContextAsync(state,ct); break; }
                    catch(McpException error) when(error.Code=="STALE_REVISION" && contextReads++<3)
                    {
                        await Inventory(backend,binding,ct);
                        state=await backend.GetDocumentStateAsync(ct);
                        Require(state.DocumentId==id,"Context initialization changed document ownership.");
                    }
                }
                report[sheet?"sheet_context_revision_renewals":"part_context_revision_renewals"]=contextReads;
                Require(!context.Truncated,"Context truncated.");
                string sketchId=context.Sketches.Single(s=>s.Name=="S1_Copy88").NativeSketchId;
                Phase=sheet?"initialize_sheet_snapshot":"initialize_part_snapshot";
                NativeSketchSnapshot current;
                int initializationReads=0;
                while(true)
                {
                    try { current=await backend.GetNativeSketchAsync(state,sketchId,ct); break; }
                    catch(McpException error) when(error.Code=="STALE_REVISION" && initializationReads++<3)
                    {
                        // Only the initial read of an activated owned fixture may renew
                        // its token. Never retry the first post-Apply or rollback read.
                        await Inventory(backend,binding,ct);
                        state=await backend.GetDocumentStateAsync(ct);
                        Require(state.DocumentId==id,"Initialization changed document ownership.");
                    }
                }
                report[sheet?"sheet_initialization_revision_renewals":"part_initialization_revision_renewals"]=initializationReads;
                Require(current.NativeMutationComplete&&!current.Partial,"Copy fixture snapshot incomplete.");
                var sourceLine=current.Entities.Single(e=>e.Type=="line");
                var literal=current.Entities.Single(e=>e.Type=="text"&&!((string?)e.Data["formatted_text"]??"").Contains("<Parameter",StringComparison.Ordinal));
                var parameter=current.Entities.Single(e=>e.Type=="text"&&((string?)e.Data["formatted_text"]??"").Contains("<Parameter",StringComparison.Ordinal));
                Require(current.Entities.Count(e=>e.Type=="text")==2,"Fixture text count differs.");
                string lineId=sourceLine.Reference.EntityId,textId=literal.Reference.EntityId,paramId=parameter.Reference.EntityId;
                var staleState=state;
                foreach(string workflow in new[]{"line","text","mixed"})
                {
                    if(group++>0) { Phase="between_groups_rate240"; await Task.Delay(TimeSpan.FromSeconds(65),ct); }
                    sourceLine=current.Entities.Single(e=>e.Reference.EntityId==lineId); literal=current.Entities.Single(e=>e.Reference.EntityId==textId);
                    var selected=workflow=="line"?new[]{sourceLine}:workflow=="text"?new[]{literal}:new[]{sourceLine,literal};
                    double dx=workflow=="line"?12:workflow=="text"?22:36,dy=workflow=="line"?-8:workflow=="text"?-13:-21;
                    var before=current;
                    var result=await Apply(backend,binding,state,before,CopyOperation(sketchId,selected,dx,dy),"copy_"+workflow,ct);
                    state=result.State; current=result.Snapshot;
                    foreach(var old in before.Entities)
                    {
                        var same=current.Entities.Single(e=>e.Reference.EntityId==old.Reference.EntityId);
                        Require(JToken.DeepEquals(old.Data,same.Data),"Existing source geometry/properties/glyph changed.");
                    }
                    var oldIds=before.Entities.Select(e=>e.Reference.EntityId).ToHashSet(StringComparer.Ordinal);
                    var newObjects=current.Entities.Where(e=>!oldIds.Contains(e.Reference.EntityId)&&(e.Type=="line"||e.Type=="text")).ToArray();
                    Require(newObjects.Length==selected.Length,"Exactly selected object types must be copied once.");
                    var pairs=new JArray();
                    foreach(var source in selected)
                    {
                        var copy=newObjects.Single(e=>e.Type==source.Type); CheckCopy88(source,copy,dx,dy);
                        pairs.Add(new JObject { ["type"]=source.Type,["source"]=source.Reference.ToJson(),["copy"]=copy.Reference.ToJson(),
                            ["source_before"]=source.Data,["copy_first_snapshot"]=copy.Data });
                    }
                    ((JArray)report["cases"]!).Add(new JObject { ["context"]=sheet?"Sheet":"Part",["document_id"]=id,["workflow"]=workflow,
                        ["delta_mm"]=new JArray(dx,dy),["pairs"]=pairs,["first_snapshot_immediate_after_last_commit"]=true,
                        ["source_represented_exact"]=true,["represented_preview_rollback_exact"]=true,["synthetic_pc_confirmation"]=true });
                }
                sourceLine=current.Entities.Single(e=>e.Reference.EntityId==lineId); literal=current.Entities.Single(e=>e.Reference.EntityId==textId);
                parameter=current.Entities.Single(e=>e.Reference.EntityId==paramId);
                var foreign=literal.Reference.ToJson(); foreign["document_id"]=sheet?binding.PartDocumentId:binding.SheetDocumentId;
                var invalid=Operation(sketchId,"arc_center",new JObject { ["points_mm"]=new JArray { new JArray(10,10) },["radius_mm"]=0,["start_deg"]=0,["sweep_deg"]=90 });
                var refusals=new (string Name,DocumentState Request,JArray Ops,bool Stale)[]{
                    ("stale_request",staleState,new JArray { CopyOperation(sketchId,new[]{sourceLine}) },true),
                    ("foreign_reference",state,new JArray { CopyReferences88(sketchId,new JArray { foreign }) },false),
                    ("parameter_text_unsupported",state,new JArray { CopyOperation(sketchId,new[]{parameter}) },false),
                    ("mixed_parameter_preflight",state,new JArray { CopyOperation(sketchId,new[]{sourceLine,parameter}) },false),
                    ("post_valid_copy_failure",state,new JArray { CopyOperation(sketchId,new[]{sourceLine,literal},-17,26),invalid },false) };
                foreach(var refusal in refusals)
                {
                    string beforeNative=await Checkpoint88(args[2],binding.RunId,ct);
                    await Refused(backend,binding,refusal.Request,state,current,refusal.Ops,refusal.Name,refusal.Stale,report,ct);
                    string afterNative=await Checkpoint88(args[2],binding.RunId,ct);
                    Require(beforeNative==afterNative,"Refusal changed full native state: keys/geometry/glyphs/styles/parameters/Dirty/DBRevision.");
                    ((JObject)((JArray)report["refusals"]!).Last!)["full_native_checkpoint_exact"]=true;
                }
            }
            Require(((JArray)report["cases"]!).Count==6&&((JArray)report["refusals"]!).Count==10,"Copy/refusal matrix incomplete.");
            report["status"]="PASS_PC_REPRESENTED_SUBSET";
            var frozen88=(JObject)report.DeepClone(); frozen88["exit_code"]=0;
            await PublishNew90(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[2]))!,"copy-client88.json"),frozen88,ct);
            await RunCoreLifecycle90(backend,binding,report,args[2],ct);
            report["status"]="PASS_PC_CORE_LIFECYCLE_SUBSET"; exit=0;
        }
        catch(Exception error) { report["failure"]=Failure(error); report["status"]="FAILED"; }
        finally
        {
            if(bytes!=null) Array.Clear(bytes);
            try { File.Delete(args[1]); } catch(Exception error) { report["bootstrap_cleanup_failure"]=Failure(error); exit=1; }
            report["exit_code"]=exit;
            try { using var output=new FileStream(args[2],FileMode.CreateNew,FileAccess.Write,FileShare.Read); using var writer=new StreamWriter(output,new System.Text.UTF8Encoding(false)); await writer.WriteAsync(report.ToString(Newtonsoft.Json.Formatting.Indented)); }
            catch { exit=1; }
        }
        return exit;
    }
    private static JObject CopyOperation(string sketchId,IEnumerable<NativeSketchObject> objects,double dx=12,double dy=-8)=>CopyReferences88(sketchId,new JArray(objects.Select(e=>e.Reference.ToJson())),dx,dy);
    private static JObject CopyReferences88(string sketchId,JArray references,double dx=12,double dy=-8)=>Operation(sketchId,"transform_copy",new JObject { ["references"]=references,["delta_mm"]=new JArray(dx,dy),["remove_constraints"]=false });
    private static void CheckCopy88(NativeSketchObject source,NativeSketchObject copy,double dx,double dy)
    {
        Require(source.Type==copy.Type&&source.Reference.EntityId!=copy.Reference.EntityId&&copy.Supported,"Copy native type/identity differs.");
        var a=source.Data; var b=copy.Data;
        if(source.Type=="line")
        {
            foreach(string field in new[]{"start_mm","end_mm","strokes_mm"}) ShiftArray88((JArray)a[field]!, (JArray)b[field]!,2,dx,dy);
            foreach(string field in new[]{"construction","native_type"}) Require(JToken.DeepEquals(a[field],b[field]),"Copied line properties differ.");
        }
        else
        {
            foreach(string field in new[]{"text","formatted_text","rotation_deg"}) Require(JToken.DeepEquals(a[field],b[field]),"Copied text properties differ.");
            ShiftArray88((JArray)a["text_mm"]!,(JArray)b["text_mm"]!,2,dx,dy);
            ShiftArray88((JArray)a["text_bounds_mm"]!,(JArray)b["text_bounds_mm"]!,2,dx,dy);
            var ag=(JObject)a["text_glyphs"]!;var bg=(JObject)b["text_glyphs"]!;
            Require((string?)ag["coordinate_space"]=="model_mm"&&(string?)bg["coordinate_space"]=="model_mm","Glyph coordinate space differs.");
            foreach(string field in new[]{"indices","color_rgb"}) Require(JToken.DeepEquals(ag[field],bg[field]),"Copied glyph topology/color differs.");
            ShiftArray88((JArray)ag["vertices_mm"]!,(JArray)bg["vertices_mm"]!,3,dx,dy);
        }
    }
    private static void ShiftArray88(JArray a,JArray b,int stride,double dx,double dy)
    {
        Require(a.Count>0&&a.Count==b.Count&&a.Count%stride==0,"Translated array dimensions differ.");
        for(int i=0;i<a.Count;i++) Require(Math.Abs((double)b[i]-(double)a[i]-(i%stride==0?dx:i%stride==1?dy:0))<=Tol,"Copied coordinate is not pure translation.");
    }
    private static async Task<string> Checkpoint88(string reportPath,string runId,CancellationToken ct)
    {
        Phase="native_checkpoint_metadata";
        string dir=Path.GetDirectoryName(Path.GetFullPath(reportPath))!; string nonce=Guid.NewGuid().ToString("N");
        string request=Path.Combine(dir,"copy-checkpoint-request88.json"),ack=Path.Combine(dir,"copy-checkpoint-ack88.json");
        File.Delete(ack); // Previous handshake is complete; never read an old acknowledgement while it is rewritten.
        string temporary=Path.Combine(dir,"copy-checkpoint-request88-"+nonce+".tmp");
        await File.WriteAllTextAsync(temporary,new JObject { ["run_id"]=runId,["nonce"]=nonce }.ToString(),ct); File.Move(temporary,request,true);
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(30));
        while(true)
        {
            timeout.Token.ThrowIfCancellationRequested();
            if(File.Exists(ack))
            {
                JObject? value=null;
                try { value=JObject.Parse(await File.ReadAllTextAsync(ack,timeout.Token)); }
                catch(Newtonsoft.Json.JsonException) { }
                catch(IOException error) when(error.HResult==unchecked((int)0x80070020)||error.HResult==unchecked((int)0x80070021)) { }
                if((string?)value?["nonce"]==nonce)
                {
                    Require((string?)value?["run_id"]==runId,"Native checkpoint run binding differs.");
                    string hash=(string?)value?["native_state_sha256"]??"";
                    Require(System.Text.RegularExpressions.Regex.IsMatch(hash,"\\A[A-F0-9]{64}\\z"),"Native checkpoint digest invalid."); return hash;
                }
            }
            await Task.Delay(100,timeout.Token); // File handshake only, no CAD retry/read/repair.
        }
    }
}
