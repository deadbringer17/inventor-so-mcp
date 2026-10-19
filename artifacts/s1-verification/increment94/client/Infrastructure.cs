using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Mcp;
using InventorXrSo.Core.Pairing;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

internal static partial class Program
{
    private const double Tol=1e-5;
    private static string Phase="arguments";
    private static void Require(bool value,string assertion){if(!value)throw new Assertion94(assertion);}
    private sealed class Assertion94(string assertion):Exception(assertion);
    private static bool Exact94(JToken? a,JToken? b)=>string.Equals(a?.ToString(Formatting.None),b?.ToString(Formatting.None),StringComparison.Ordinal);
    private static bool Same(DocumentState a,DocumentState b)=>a.DocumentId==b.DocumentId&&a.Revision==b.Revision;
    private static JObject State94(DocumentState s)=>new(){["document_id"]=s.DocumentId,["revision"]=s.Revision,["visual_revision"]=s.VisualRevision};
    private static JObject Represented(NativeSketchSnapshot s)=>new(){["entities"]=new JArray(s.Entities.Select(e=>e.Data)),["dimensions"]=new JArray(s.Dimensions.Select(e=>e.Data)),["constraints"]=new JArray(s.Constraints.Select(e=>e.Data)),["complete"]=s.NativeMutationComplete,["partial"]=s.Partial,["constraint_status"]=s.ConstraintStatus};
    private static JObject Operation(string sketch,string variant,JObject options)=>new(){["command"]="edit_sketch",["arguments"]=new JObject{["sketch_id"]=sketch,["variant"]=variant,["options"]=options}};
    private static JObject CopyReferences94(string sketch,JArray references)=>Operation(sketch,"transform_copy",new JObject{["references"]=references,["delta_mm"]=new JArray(12,-8),["remove_constraints"]=false});
    private static JObject Copy94(string sketch,IEnumerable<NativeSketchObject> selected)=>CopyReferences94(sketch,new JArray(selected.Select(o=>o.Reference.ToJson())));
    private static string Type94(string role)=>role is "text" or "parameter_text"?"text":role=="circle"?"circle":role.StartsWith("arc_",StringComparison.Ordinal)?"arc":"line";
    private static string[] Selection94(string workflow)=>workflow=="mixed_curves"?new[]{"circle","arc_quarter","line"}:workflow=="mixed_all"?new[]{"circle","arc_quarter","line","text"}:new[]{workflow};
    private static NativeSketchObject[] Select94(NativeSketchSnapshot snapshot,Sources94 sources,DocumentState state,IEnumerable<string> roles)
    {
        Require(snapshot.State.DocumentId==state.DocumentId&&snapshot.State.Revision==state.Revision&&snapshot.SketchId==sources.SketchId&&snapshot.NativeMutationComplete&&!snapshot.Partial,"Selection snapshot is not authoritative/current/complete.");
        return roles.Select(role=>{
            var matches=snapshot.Entities.Where(e=>e.Reference?.EntityId==sources.Sources[role]).ToArray();
            Require(matches.Length==1&&matches[0].Supported&&matches[0].Type==Type94(role)&&matches[0].Reference.Subpart==SketchSubpart.Entity&&matches[0].Reference.Matches(state,sources.SketchId),"Attested source has no fresh typed whole reference.");return matches[0];
        }).ToArray();
    }
    private static async Task Inventory(InventorBackend backend,S1AcceptanceBootstrapMetadata binding,CancellationToken ct)
    {
        Require(binding.ExpiresUtc>DateTimeOffset.UtcNow,"Owned bootstrap expired.");var docs=await backend.ListOpenAsync(ct);
        Require(docs.Count==2&&docs.Any(d=>d.Id==binding.PartDocumentId&&d.Name=="XR_S1_Quest_Acceptance_Part.ipt")&&docs.Any(d=>d.Id==binding.SheetDocumentId&&d.Name=="XR_S1_Quest_Acceptance_Sheet.ipt"),"Exact owned fixture inventory differs.");
    }
    private static async Task Guard(InventorBackend backend,S1AcceptanceBootstrapMetadata binding,DocumentState state,CancellationToken ct)
    {await Inventory(backend,binding,ct);Require((state.DocumentId==binding.PartDocumentId||state.DocumentId==binding.SheetDocumentId)&&Same(state,await backend.GetDocumentStateAsync(ct)),"Owned mutation baseline is no longer current.");}
    private static async Task<(DocumentState State,NativeSketchSnapshot Snapshot)> Initial94(InventorBackend backend,S1AcceptanceBootstrapMetadata binding,Sources94 sources,JObject evidence,CancellationToken ct,bool context)
    {
        await Inventory(backend,binding,ct);await backend.ActivateOpenAsync(sources.DocumentId,ct);var state=await backend.GetDocumentStateAsync(ct);Require(state.DocumentId==sources.DocumentId,"Initial activation is outside owned fixture.");
        int contextAttempts=0,snapshotAttempts=0;
        if(context)
        {
            while(true){contextAttempts++;try{var design=await backend.GetDesignContextAsync(state,ct);Require(!design.Truncated&&design.Sketches.Single(s=>s.Name=="S1_Copy88").NativeSketchId==sources.SketchId,"Context sketch differs from attested catalog.");break;}
                catch(McpException e)when(e.Code=="STALE_REVISION"&&contextAttempts<3){state=await backend.GetDocumentStateAsync(ct);Require(state.DocumentId==sources.DocumentId,"Initial context ownership changed.");}}
        }
        NativeSketchSnapshot snapshot;
        while(true){snapshotAttempts++;try{snapshot=await backend.GetNativeSketchAsync(state,sources.SketchId,ct);break;}
            catch(McpException e)when(e.Code=="STALE_REVISION"&&snapshotAttempts<3){state=await backend.GetDocumentStateAsync(ct);Require(state.DocumentId==sources.DocumentId,"Initial snapshot ownership changed.");}}
        evidence["context_attempts"]=contextAttempts;evidence["snapshot_attempts"]=snapshotAttempts;evidence["state"]=State94(state);
        Select94(snapshot,sources,state,Roles94);return(state,snapshot);
    }
    private static void CheckPreview94(DesignSession session)
    {
        Require(session.Status==DesignStatus.PreviewReady&&session.Preview!=null&&!session.CanApply,"Preview failed unconfirmed rendering gate.");
        var preview=session.Preview!.Sketches.Single(s=>s.Name=="S1_Copy88");Require(preview.NativeMutationComplete&&preview.NativePaths.Count>0,"Native preview incomplete or lacks actual paths.");
    }
    private static async Task<(DocumentState State,NativeSketchSnapshot Snapshot)> Workflow94(InventorBackend backend,S1AcceptanceBootstrapMetadata binding,Sources94 sources,
        DocumentState state,NativeSketchSnapshot before,string workflow,JObject row,string reportPath,CancellationToken ct)
    {
        var roles=Selection94(workflow);await Guard(backend,binding,state,ct);
        var firstCheckpoint=await Checkpoint94(reportPath,binding,sources.Context,workflow,"preview_before",ct);
        using var session=new DesignSession(new ObservedDesignBackend94(backend,row));session.SetContext(state,true,true);session.SetDraft(new JArray{Copy94(sources.SketchId,Select94(before,sources,state,roles))});
        Phase=workflow+"_first_preview";await session.PreviewAsync().WaitAsync(ct);CheckPreview94(session);row["cancelled_plan_id"]=session.Preview!.PlanId;session.Cancel();
        Require(session.Status==DesignStatus.Empty&&session.Preview==null&&!session.CanApply&&session.LastCommit==null,"Cancelled preview still exposes an applicable plan/commit.");
        var restoredState=await backend.GetDocumentStateAsync(ct);var restored=await backend.GetNativeSketchAsync(restoredState,sources.SketchId,ct);
        Require(Same(state,restoredState)&&Exact94(Represented(before),Represented(restored)),"Cancelled preview changed represented native state/revision.");
        var cancelledCheckpoint=await Checkpoint94(reportPath,binding,sources.Context,workflow,"preview_cancel_after",ct);
        Require((string?)firstCheckpoint["ack"]!["native_state_sha256"]==(string?)cancelledCheckpoint["ack"]!["native_state_sha256"],"Cancelled preview changed full native state digest.");
        row["checkpoints"]=new JObject{["preview_before"]=(int)firstCheckpoint["ordinal"]!,["preview_cancel_after"]=(int)cancelledCheckpoint["ordinal"]!};
        row["represented_preview_rollback_exact"]=true;row["full_native_preview_checkpoint_exact"]=true;row["cancel_discarded_plan"]=true;
        // Rebuild all valid references from the actual post-Cancel snapshot.
        session.SetDraft(new JArray{Copy94(sources.SketchId,Select94(restored,sources,restoredState,roles))});
        Phase=workflow+"_second_preview";await session.PreviewAsync().WaitAsync(ct);CheckPreview94(session);
        var afterPreviewState=await backend.GetDocumentStateAsync(ct);var afterPreview=await backend.GetNativeSketchAsync(afterPreviewState,sources.SketchId,ct);
        Require(Same(restoredState,afterPreviewState)&&Exact94(Represented(restored),Represented(afterPreview)),"Second preview rollback changed native state/revision.");
        await Guard(backend,binding,afterPreviewState,ct);
        session.ConfirmRendered(session.Preview!.PlanId);Require(session.CanApply,"Synthetic PC confirmation did not unlock current preview.");row["synthetic_pc_confirmation"]=true;
        Phase=workflow+"_apply";await session.ApplyAsync().WaitAsync(ct);
        Require(!session.CommitOutcomeUnknown&&session.LastCommit!=null&&session.LastCommit.DocumentId==state.DocumentId&&session.LastCommit.Revision!=state.Revision,"Apply outcome unknown or revision unchanged.");
        // The only requests after Apply are this authoritative state and ONE
        // snapshot. Never insert inventory/oracle/pacing/context/retry here.
        Phase=workflow+"_first_state";var after=await backend.GetDocumentStateAsync(ct);row["after_state"]=State94(after);
        Require(Same(session.LastCommit!,after),"First post-Apply document state differs from LastCommit.");
        Phase=workflow+"_first_snapshot";var first=await backend.GetNativeSketchAsync(after,sources.SketchId,ct);
        row["first_snapshot_raw"]=Represented(first);row["first_snapshot_immediate_after_last_commit"]=true;row["first_snapshot_attempts"]=1;
        Require(first.NativeMutationComplete&&!first.Partial,"First post-Apply snapshot incomplete.");
        CheckOld94(before,first);row["source_represented_exact"]=true;row["prior_copies_represented_exact"]=true;
        var old=before.Entities.Select(e=>e.Reference.EntityId).ToHashSet(StringComparer.Ordinal);var added=first.Entities.Where(e=>!old.Contains(e.Reference.EntityId)).ToArray();
        Require(added.All(e=>e.Type is "point" or "line" or "circle" or "arc" or "text")&&added.All(e=>e.Supported),"Copy introduced unsupported native represented entity.");
        var curves=added.Where(e=>e.Type!="point").ToArray();var selected=Select94(afterPreview,sources,afterPreviewState,roles);Require(curves.Length==selected.Length,"Exactly selected whole object count was not created.");
        var pairs=new JArray();
        foreach(var source in selected){var matches=curves.Where(e=>e.Type==source.Type).ToArray();Require(matches.Length==1,"Selected profile does not uniquely pair types.");var copy=matches[0];Require(copy.Reference.Matches(after,sources.SketchId),"Copied typed reference is not fresh.");CheckCopy94(source,copy,12,-8);pairs.Add(new JObject{["type"]=source.Type,["source"]=source.Reference.ToJson(),["copy"]=copy.Reference.ToJson(),["source_before"]=source.Data,["copy_first_snapshot"]=copy.Data});}
        row["pairs"]=pairs;row["fresh_valid_references"]=true;
        var committedCheckpoint=await Checkpoint94(reportPath,binding,sources.Context,workflow,"commit_first_core_after",ct);row["checkpoints"]!["commit_first_core_after"]=(int)committedCheckpoint["ordinal"]!;row["status"]="PASS_PC_CURVE_COPY_WORKFLOW";
        return(after,first);
    }
    private static async Task<(DocumentState State,NativeSketchSnapshot Snapshot)> Refused94(InventorBackend backend,S1AcceptanceBootstrapMetadata binding,Sources94 sources,DocumentState request,DocumentState actual,
        NativeSketchSnapshot before,JArray operations,string kind,bool stale,JObject report,string reportPath,JObject? adversarial,CancellationToken ct)
    {
        Phase="refusal_"+kind;await Guard(backend,binding,actual,ct);var beforeNative=await Checkpoint94(reportPath,binding,sources.Context,kind,"refusal_before",ct);
        var row=new JObject{["context"]=sources.Context,["document_id"]=actual.DocumentId,["kind"]=kind,["request_state"]=State94(request),["actual_state"]=State94(actual),["operations"]=operations.DeepClone(),["before_snapshot_raw"]=Represented(before)};
        if(adversarial!=null)foreach(var property in adversarial.Properties())row[property.Name]=property.Value.DeepClone();((JArray)report["refusals"]!).Add(row);
        string? code=null,stepCode=null;int? stepIndex=null;
        try{await backend.PreviewDesignAsync(request,operations,ct);}
        catch(McpException error)when(stale?error.Code=="STALE_REVISION":error.Code is "ROLLED_BACK" or "INVALID_ARGUMENT")
        {code=error.Code;var details=(error as McpToolException)?.Details;stepCode=(string?)details?["details"]?["step_code"]??(string?)details?["step_code"];stepIndex=(int?)details?["details"]?["step_index"]??(int?)details?["step_index"];row["code"]=code;row["step_code"]=stepCode;row["step_index"]=stepIndex;Require(code!="ROLLED_BACK"||stepCode=="INVALID_ARGUMENT","Rollback failed to identify invalid argument.");}
        Require(code!=null,"Expected refusal was not observed.");Require(kind!="post_valid_copy_failure"||code=="ROLLED_BACK"&&stepIndex==1,"Post-valid-copy rollback did not fail at step1.");
        Require(stale||code=="ROLLED_BACK"&&stepCode=="INVALID_ARGUMENT"&&stepIndex==(kind=="post_valid_copy_failure"?1:0),"Nonstale refusal must identify the exact invalid argument step.");
        Phase="refusal_"+kind+"_state";var state=await backend.GetDocumentStateAsync(ct);Phase="refusal_"+kind+"_snapshot";var snapshot=await backend.GetNativeSketchAsync(state,sources.SketchId,ct);row["after_state"]=State94(state);row["after_snapshot_raw"]=Represented(snapshot);
        Require(Same(actual,state)&&Exact94(Represented(before),Represented(snapshot)),"Refusal changed represented native state/revision.");
        var afterNative=await Checkpoint94(reportPath,binding,sources.Context,kind,"refusal_after",ct);Require((string?)beforeNative["ack"]!["native_state_sha256"]==(string?)afterNative["ack"]!["native_state_sha256"],"Refusal changed full native state digest.");
        row["represented_rollback_exact"]=true;row["event_revision_preserved"]=true;row["full_native_checkpoint_exact"]=true;row["before_native_checkpoint"]=(int)beforeNative["ordinal"]!;row["after_native_checkpoint"]=(int)afterNative["ordinal"]!;
        return(state,snapshot);
    }
    private static JObject Failure(Exception error)
    {
        var result=new JObject{["phase"]=Phase,["type"]=error.GetType().FullName,["hresult"]=error.HResult};if(error is Assertion94 a)result["assertion"]=a.Message;
        if(error is McpException m&&System.Text.RegularExpressions.Regex.IsMatch(m.Code??"","\\A[A-Z0-9_]{1,64}\\z"))result["code"]=m.Code;
        if(error is McpToolException tool){var details=tool.Details?["details"] as JObject??tool.Details as JObject;foreach(string field in new[]{"expected_revision","actual_revision","phase","step_code","step_index"})if(details?[field] is JValue value)result["detail_"+field]=value.DeepClone();}return result;
    }
}
