using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Net;
using InventorXrSo.Core.Pairing;
using Newtonsoft.Json.Linq;

internal static partial class Program
{
    private static async Task<int> Main(string[] args)
    {
        if(args.Length!=4||File.Exists(args[2])||Path.GetFileName(args[2])!="copy-client95.json"||Path.GetFileName(args[3])!="curve-copy-sources94.json")return 64;
        var report=new JObject{["schema"]=1,["status"]="RUNNING",["cases"]=new JArray(),["refusals"]=new JArray(),["initializations"]=new JArray(),
            ["evidence"]="Real Core pinned HTTPS, actual product preview/Cancel/repreview and synthetic PC confirmation; one first native Core snapshot after Apply, then native checkpoint metadata",
            ["not_covered"]=new JArray("Quest/UI/physical", "Desktop semantic parity", "Source/copy updates and persistence/reopen", "Rich/parameter/rotated/multiline text copy", "Other native copy geometry/constraints/options", "Core structural point ownership not exposed: independent native94 oracle required", "No product capability/gate promotion")};
        byte[]? bootstrapBytes=null;int exit=1;
        try
        {
            using var deadline=new CancellationTokenSource(TimeSpan.FromMinutes(28));var ct=deadline.Token;Phase="bootstrap";
            string privateDir=Path.GetDirectoryName(Path.GetFullPath(args[2]))!;
            Require(string.Equals(Path.GetDirectoryName(Path.GetFullPath(args[1])),privateDir,StringComparison.OrdinalIgnoreCase)
                &&string.Equals(Path.GetDirectoryName(Path.GetFullPath(args[3])),privateDir,StringComparison.OrdinalIgnoreCase),"Bootstrap/catalog/report must share the dedicated owned run directory.");
            Require((File.GetAttributes(args[1])&FileAttributes.ReparsePoint)==0,"Bootstrap reparse file forbidden.");
            using(var stream=new FileStream(args[1],FileMode.Open,FileAccess.Read,FileShare.None))
            {Require(stream.Length is >=1 and <=S1AcceptanceBootstrap.MaxBytes,"Bootstrap size differs.");bootstrapBytes=new byte[checked((int)stream.Length)];await stream.ReadExactlyAsync(bootstrapBytes,ct);Require(stream.ReadByte()==-1,"Bootstrap changed during read.");}
            File.Delete(args[1]);var bootstrap=S1AcceptanceBootstrap.Parse(bootstrapBytes,args[0],DateTimeOffset.UtcNow);Array.Clear(bootstrapBytes);bootstrapBytes=null;
            var binding=bootstrap.Metadata;var paired=bootstrap.CreatePairedServer(DateTimeOffset.UtcNow);report["run_id"]=binding.RunId;report["target_id"]=binding.TargetId;
            Phase="source_catalog";var catalog=await ReadCatalog94(args[3],binding,ct);report["owned_pid"]=catalog.Pid;report["owned_start_utc_ticks"]=catalog.Start;report["source_catalog"]=catalog.Raw;
            using var transport=new ProbeTransport(ServerTrust.Pinned(paired.CertSha256));var backend=new InventorBackend(transport,paired,new MemoryAssetCache());await backend.ConnectAsync(ct);
            int workflowOrdinal=0;
            foreach(string context in new[]{"Part","Sheet"})
            {
                var sources=catalog.Documents[context];var other=catalog.Documents[context=="Part"?"Sheet":"Part"];
                // Real foreign identity from the other owned document, captured
                // while that document is active. Negative-only revision override
                // below is explicit, so stale checking cannot hide owner checking.
                Phase=context+"_initial_foreign_source";var foreignEvidence=new JObject{["purpose"]="foreign_source_identity",["context"]=other.Context};((JArray)report["initializations"]!).Add(foreignEvidence);
                var foreignInitial=await Initial94(backend,binding,other,foreignEvidence,ct,false);var originalForeign=Select94(foreignInitial.Snapshot,other,foreignInitial.State,new[]{"circle"})[0].Reference.ToJson();
                Phase=context+"_initial_owned_context";var ownEvidence=new JObject{["purpose"]="owned_workflows",["context"]=context};((JArray)report["initializations"]!).Add(ownEvidence);
                var initial=await Initial94(backend,binding,sources,ownEvidence,ct,true);var state=initial.State;var current=initial.Snapshot;var staleState=state;
                var parameter=Select94(current,sources,state,new[]{"parameter_text"})[0];Require(((string?)parameter.Data["formatted_text"]??"").Contains("<Parameter",StringComparison.Ordinal),"Attested sentinel is not native parameter text.");
                var literal=Select94(current,sources,state,new[]{"text"})[0];Require(!((string?)literal.Data["formatted_text"]??"").Contains("<Parameter",StringComparison.Ordinal),"Literal text source is a parameter field.");
                foreach(string workflow in Workflows94)
                {
                    if(workflowOrdinal>0&&workflowOrdinal%3==0)await Pace94(report,"before_three_workflow_group",ct);workflowOrdinal++;
                    Phase=context+"_"+workflow;
                    var row=new JObject{["context"]=context,["document_id"]=sources.DocumentId,["workflow"]=workflow,["delta_mm"]=new JArray(12,-8),["before_state"]=State94(state),["before_snapshot_raw"]=Represented(current),["status"]="RUNNING"};((JArray)report["cases"]!).Add(row);
                    var result=await Workflow94(backend,binding,sources,state,current,workflow,row,args[2],ct);state=result.State;current=result.Snapshot;
                }
                await Pace94(report,"before_refusal_context_"+context,ct);
                foreach(string kind in new[]{"stale_request","foreign_reference","parameter_text_unsupported","mixed_parameter_preflight","post_valid_copy_failure"})
                {
                    // Every valid role is looked up again in the newest actual
                    // snapshot; no reference from a previous document revision.
                    var circle=Select94(current,sources,state,new[]{"circle"})[0];parameter=Select94(current,sources,state,new[]{"parameter_text"})[0];
                    JArray operations;JObject? adversarial=null;
                    if(kind=="foreign_reference")
                    {
                        var foreign=(JObject)originalForeign.DeepClone();foreign["revision"]=state.Revision;
                        operations=new JArray{CopyReferences94(sources.SketchId,new JArray{foreign})};
                        adversarial=new JObject{["adversarial_revision_override"]=true,["original_foreign_reference"]=originalForeign.DeepClone(),["adversarial_foreign_reference"]=foreign.DeepClone(),["foreign_identity_fields_unchanged"]=true};
                    }
                    else if(kind=="parameter_text_unsupported")operations=new JArray{Copy94(sources.SketchId,new[]{parameter})};
                    else if(kind=="mixed_parameter_preflight")operations=new JArray{Copy94(sources.SketchId,new[]{circle,parameter})};
                    else if(kind=="post_valid_copy_failure")
                    {
                        var invalid=Operation(sources.SketchId,"arc_center",new JObject{["points_mm"]=new JArray{new JArray(10,10)},["radius_mm"]=0,["start_deg"]=0,["sweep_deg"]=90});
                        operations=new JArray{Copy94(sources.SketchId,Select94(current,sources,state,Selection94("mixed_all"))),invalid};
                    }
                    else operations=new JArray{Copy94(sources.SketchId,new[]{circle})};
                    var result=await Refused94(backend,binding,sources,kind=="stale_request"?staleState:state,state,current,operations,kind,kind=="stale_request",report,args[2],adversarial,ct);
                    state=result.State;current=result.Snapshot;
                }
            }
            Require(((JArray)report["cases"]!).Count==18&&((JArray)report["cases"]!).Sum(c=>((JArray)c["pairs"]!).Count)==28
                &&((JArray)report["refusals"]!).Count==10&&CheckpointTrace94.Count==74,"Complete94 matrix/counts differ.");
            report["checkpoint_count"]=CheckpointTrace94.Count;report["checkpoint_trace94"]=CheckpointTrace94;
            report["status"]="PASS_PC_CURVE_COPY_SUBSET";
            var frozen94=(JObject)report.DeepClone();frozen94["exit_code"]=0;
            await Publish94(Path.Combine(privateDir,"copy-client94.json"),frozen94,ct);
            await Prerequisite95(privateDir,binding,report,ct);
            await RunLifecycle95(backend,binding,catalog,report,args[2],ct);
            report["status"]="PASS_PC_CURVE_COPY_LIFECYCLE_SUBSET";exit=0;
        }
        catch(Exception error){report["failure"]=Failure(error);report["status"]="FAILED";}
        finally
        {
            if(bootstrapBytes!=null)Array.Clear(bootstrapBytes);
            try{File.Delete(args[1]);}catch(Exception error){report["bootstrap_cleanup_failure"]=Failure(error);report["status"]="FAILED";exit=1;}
            report["checkpoint_trace94"]=CheckpointTrace94;report["exit_code"]=exit;
            try{await Publish94(args[2],report,CancellationToken.None);}catch{exit=1;}
        }
        return exit;
    }
    private static async Task Pace94(JObject report,string reason,CancellationToken ct)
    {Phase="pacing_rate240";report["last_pacing"]=new JObject{["reason"]=reason,["seconds"]=65};await Task.Delay(TimeSpan.FromSeconds(65),ct);}
}
