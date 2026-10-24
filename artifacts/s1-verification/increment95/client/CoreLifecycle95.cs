using System.Text.RegularExpressions;
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Pairing;
using Newtonsoft.Json.Linq;

internal static partial class Program
{
    private static string Epoch95(DocumentState state)
    {Require(Regex.IsMatch(state.Revision,"\\A[a-f0-9]{32}:[0-9]+\\z"),"Unexpected CAD event revision shape.");return state.Revision[..32];}
    private static async Task RunLifecycle95(InventorBackend backend,S1AcceptanceBootstrapMetadata binding,Catalog94 catalog,JObject report,string reportPath,CancellationToken ct)
    {
        report["lifecycle"]=new JArray();report["baselines95"]=new JArray();
        report["not_covered"]=new JArray("Arc reshaping/radius/extent changes", "Ground/MoveTo experiments", "Other copy profiles and parameter-bound text copying", "Desktop/UI/Quest/physical/performance", "Native95 edits are fixture transactions, not product LastCommit/preview/Apply", "No S1 gate or capability promotion");
        var protocol=new Protocol95{Directory=Path.GetDirectoryName(Path.GetFullPath(reportPath))!};report["protocol95"]=protocol.Trace;
        var baselines=new Dictionary<string,Baseline95>(StringComparer.Ordinal);
        await Pace94(report,"before95_preparation",ct);
        // Only this initialization may renew STALE (three attempts total in
        // Initial94). Every post-native-edit/Open snapshot below is one attempt.
        foreach(string kind in new[]{"Part","Sheet"})
        {
            Phase="baseline95_"+kind;var initialization=new JObject{["purpose"]="lifecycle95_baseline",["context"]=kind};var initial=await Initial94(backend,binding,catalog.Documents[kind],initialization,ct,true);
            var workflows=((JArray)report["cases"]!).Cast<JObject>().Where(c=>(string?)c["context"]==kind).ToArray();Require(workflows.Length==9,"Lifecycle95 requires the complete nine-workflow94 context.");
            var copies=workflows.SelectMany(w=>((JArray)w["pairs"]!).Cast<JObject>()).Select(p=>(string)p["copy"]!["entity_id"]!).ToHashSet(StringComparer.Ordinal);
            Require(copies.Count==14&&copies.All(id=>initial.Snapshot.Entities.Count(e=>e.Reference.EntityId==id)==1),"Lifecycle95 explicit completed94 copy IDs incomplete.");
            var baseline=new Baseline95{State=initial.State,Initial=Represented(initial.Snapshot),Last=Represented(initial.Snapshot),CopyIds=copies};baselines.Add(kind,baseline);
            ((JArray)report["baselines95"]!).Add(new JObject{["context"]=kind,["state"]=State94(initial.State),["sketch_id"]=initial.Snapshot.SketchId,["copy_ids"]=new JArray(copies),["snapshot_raw"]=baseline.Initial.DeepClone(),["initialization"]=initialization});
        }
        await Action95(protocol,binding,catalog,"prepare","all",0,ct);
        foreach(string kind in new[]{"Part","Sheet"})
        {
            var metadata=protocol.Documents[kind];var baseline=baselines[kind];var entities=Rows95(baseline.Initial,"entities");
            Require(metadata.SourceIds.Values.All(entities.ContainsKey),"Prepare95 native creator identities differ from Core baseline.");
            _=Parameter95(entities[metadata.SourceIds["parameter_text"]],metadata);
            // Preserve the actual Core snapshot's entity order. Build association
            // expectations from stored raw IDs; never synthesize typed references
            // or carry an old typed snapshot into post-edit/reopen requests.
            baseline.Annotations=AnnotationRulesFromData95(baseline.Initial,metadata);
            var baselineRow=((JArray)report["baselines95"]!).Cast<JObject>().Single(r=>(string?)r["context"]==kind);
            baselineRow["annotation_rules"]=new JArray(baseline.Annotations.Values.Select(r=>new JObject{["constraint_id"]=r.ConstraintId,["constraint_key"]=r.ConstraintKey,["native_type"]=r.NativeType,["anchor_point_id"]=r.AnchorPointId,["related_entities"]=r.ExpectedBindings.DeepClone()}));
        }
        foreach(string kind in new[]{"Part","Sheet"})
        {
            var baseline=baselines[kind];
            foreach(int pass in new[]{1,2})
            {
                await Pace94(report,"before95_native_edit_"+kind+"_"+pass,ct);
                await Action95(protocol,binding,catalog,"edit",kind,pass,ct);var metadata=protocol.Documents[kind];
                // No context, inventory, oracle, pacing or retry after ACK.
                Phase="first95_state_after_edit_"+kind+"_"+pass;var state=await backend.GetDocumentStateAsync(ct);
                report["pending_first_read95"]=new JObject{["kind"]="source_edit",["context"]=kind,["pass"]=pass,["state"]=State94(state),["sketch_id"]=metadata.SketchId,["attempts"]=1};
                Require(state.DocumentId==metadata.DocumentId&&Epoch95(state)==Epoch95(baseline.State)&&state.Revision!=baseline.State.Revision,"First95 state owner/epoch/revision differs after edit.");
                Phase="first95_snapshot_after_edit_"+kind+"_"+pass;var snapshot=await backend.GetNativeSketchAsync(state,metadata.SketchId,ct);
                var observation=new JObject{["kind"]="source_edit",["context"]=kind,["pass"]=pass,["mutation_origin"]="native_fixture_edit",["event_revision_before"]=baseline.State.Revision,["event_revision_after"]=state.Revision,["state"]=State94(state),["revision"]=state.Revision,["snapshot_raw"]=Represented(snapshot),["first_core_snapshot"]=true,["snapshot_attempts"]=1};
                ((JArray)report["lifecycle"]!).Add(observation); // Raw first result precedes every geometry assertion.
                ValidateFresh95(snapshot,state,metadata,baseline);CheckIndependent95(baseline.Last,Represented(snapshot),metadata,metadata,baseline,false);SourceGeometry95(Represented(snapshot),metadata,baseline,pass);
                observation["copies_and_unrelated_exact"]=true;observation["fresh_references"]=true;observation["source_geometry_checked"]=true;
                baseline.State=state;baseline.Last=Represented(snapshot);await Action95(protocol,binding,catalog,"inspect",kind,pass,ct);
            }
            await Pace94(report,"before95_reopen_"+kind,ct);var previous=protocol.Documents[kind];
            await Action95(protocol,binding,catalog,"reopen",kind,0,ct);var reopenedMetadata=protocol.Documents[kind];
            // Fresh open-document identity metadata is legal; no native shape or
            // design-context read is permitted before this ONE Core snapshot.
            Phase="first95_metadata_after_reopen_"+kind;var documents=await backend.ListOpenAsync(ct);
            Require(documents.Count==2&&protocol.Documents.Values.All(m=>documents.Count(d=>d.Id==m.DocumentId&&d.Name==m.DisplayName)==1),"Post-reopen95 document inventory differs from fresh native metadata.");
            Phase="first95_state_after_reopen_"+kind;var reopenedState=await backend.GetDocumentStateAsync(ct);
            report["pending_first_read95"]=new JObject{["kind"]="reopen",["context"]=kind,["pass"]=0,["state"]=State94(reopenedState),["sketch_id"]=reopenedMetadata.SketchId,["attempts"]=1};
            Require(reopenedState.DocumentId==reopenedMetadata.DocumentId&&Epoch95(reopenedState)==Epoch95(baseline.State)&&reopenedState.Revision!=baseline.State.Revision,"First95 reopened state owner/epoch/revision differs.");
            Phase="first95_snapshot_after_reopen_"+kind;var reopened=await backend.GetNativeSketchAsync(reopenedState,reopenedMetadata.SketchId,ct);
            var persistence=new JObject{["kind"]="reopen",["context"]=kind,["pass"]=0,["event_revision_before"]=baseline.State.Revision,["event_revision_after"]=reopenedState.Revision,["state"]=State94(reopenedState),["revision"]=reopenedState.Revision,["snapshot_raw"]=Represented(reopened),["first_core_snapshot"]=true,["snapshot_attempts"]=1,["before_native_path"]=previous.Path,["reopened_native_path"]=reopenedMetadata.Path};
            ((JArray)report["lifecycle"]!).Add(persistence);
            ValidateFresh95(reopened,reopenedState,reopenedMetadata,baseline);CheckIndependent95(baseline.Last,Represented(reopened),previous,reopenedMetadata,baseline,true);
            persistence["copies_and_unrelated_exact"]=true;persistence["fresh_references"]=true;persistence["parameter_owner_relocation_only"]=true;
            baseline.State=reopenedState;baseline.Last=Represented(reopened);await Action95(protocol,binding,catalog,"inspect_reopen",kind,0,ct);
        }
        await Action95(protocol,binding,catalog,"complete","all",0,ct);Require(protocol.Ordinal==14&&((JArray)report["lifecycle"]!).Count==6,"Lifecycle95 FSM/first-read count incomplete.");
        report.Remove("pending_first_read95");report["source_updates"]=4;report["reopens"]=2;report["first_core_reads"]=6;report["open_s1_gates"]=18;report["quest_proof"]=false;report["physical_proof"]=false;
    }
}
