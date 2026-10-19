using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Mcp;
using InventorXrSo.Core.Net;
using InventorXrSo.Core.Pairing;
using Newtonsoft.Json.Linq;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if(args.Length!=3 || File.Exists(args[2])) return 64;
        var report=new JObject { ["schema"]=1,["evidence"]="Real HTTPS/Core first text snapshot after parameter Apply; synthetic render confirmation, no Unity/Quest or COM client.",
            ["open_s1_gates"]=18,["cases"]=new JArray() };
        byte[]? bytes=null; int exit=1;
        try
        {
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(150)); var ct=timeout.Token;
            if(new FileInfo(args[1]).Length is <1 or >S1AcceptanceBootstrap.MaxBytes) throw new InvalidOperationException("Bootstrap byte budget.");
            bytes=await File.ReadAllBytesAsync(args[1],ct); File.Delete(args[1]);
            var bootstrap=S1AcceptanceBootstrap.Parse(bytes,args[0],DateTimeOffset.UtcNow);
            Array.Clear(bytes); bytes=null;
            var paired=bootstrap.CreatePairedServer(DateTimeOffset.UtcNow);
            using var transport=new ProbeTransport(ServerTrust.Pinned(paired.CertSha256));
            var backend=new InventorBackend(transport,paired,new MemoryAssetCache());
            report["phase"]="connect_backend"; await backend.ConnectAsync(ct);
            report["phase"]="owned_inventory";
            var documents=await backend.ListOpenAsync(ct);
            if(documents.Count!=2 || !documents.Any(d=>d.Id==bootstrap.Metadata.PartDocumentId && d.Name=="XR_S1_Quest_Acceptance_Part.ipt")
                || !documents.Any(d=>d.Id==bootstrap.Metadata.SheetDocumentId && d.Name=="XR_S1_Quest_Acceptance_Sheet.ipt"))
                throw new InvalidOperationException("Owned fixture inventory differs.");
            foreach(var id in new[]{bootstrap.Metadata.PartDocumentId,bootstrap.Metadata.SheetDocumentId})
            {
                report["phase"]="activate_owned"; await backend.ActivateOpenAsync(id,ct);
                report["phase"]="initial_document_state";
                var state=await backend.GetDocumentStateAsync(ct);
                if(state.DocumentId!=id) throw new InvalidOperationException("Active owned document differs.");
                report["phase"]="initial_design_context";
                DesignContext context;
                int refreshes=0;
                while(true)
                {
                    try { context=await backend.GetDesignContextAsync(state,ct); break; }
                    catch(McpException error) when(error.Code=="STALE_REVISION" && refreshes<3)
                    {
                        // Context activation can deliver a deferred view event. This
                        // bounded initialization refresh precedes every baseline and
                        // mutation; first post-Apply snapshots are never retried.
                        refreshes++;
                        state=await backend.GetDocumentStateAsync(ct);
                        if(state.DocumentId!=id) throw new InvalidOperationException("Active owned document differs.");
                    }
                }
                report["initial_context_refreshes_"+(id==bootstrap.Metadata.PartDocumentId?"part":"sheet")]=refreshes;
                var sketch=context.Sketches.Single(s=>s.Name=="S1_TextRead");
                report["phase"]="initial_native_snapshot";
                var baseline=await backend.GetNativeSketchAsync(state,sketch.NativeSketchId,ct);
                NativeSketchObject Text(NativeSketchSnapshot value)=>value.Entities.Single(e=>e.Type=="text" && ((string?)e.Data["formatted_text"])?.Contains("S1QuestLength73",StringComparison.Ordinal)==true);
                var prior=Text(baseline);
                foreach(int length in new[]{20,35})
                {
                    using var mutation=new DesignSession(backend);
                    mutation.SetContext(state,true,true);
                    mutation.SetDraft(new JArray(NativeParameterOperations.SetExpression("S1QuestLength73",length+" mm")));
                    report["phase"]="preview_"+length; await mutation.PreviewAsync();
                    if(mutation.Status!=DesignStatus.PreviewReady || mutation.Preview==null || mutation.CanApply) throw new InvalidOperationException("Preview gate differs.");
                    var unchanged=await backend.GetDocumentStateAsync(ct);
                    var rolledBack=await backend.GetNativeSketchAsync(unchanged,sketch.NativeSketchId,ct);
                    if(unchanged.Revision!=state.Revision || !JToken.DeepEquals(Text(rolledBack).Data,prior.Data)) throw new InvalidOperationException("Preview rollback differs.");
                    mutation.ConfirmRendered(mutation.Preview.PlanId); // Explicitly synthetic PC probe only.
                    report["phase"]="apply_"+length; await mutation.ApplyAsync();
                    var after=await backend.GetDocumentStateAsync(ct);
                    if(after.DocumentId!=id || after.Revision==state.Revision || mutation.LastCommit?.Revision!=after.Revision) throw new InvalidOperationException("Commit revision differs.");
                    // No COM property access, native inspection, Rebuild, oracle or other
                    // request occurs between this commit state and the first snapshot.
                    report["phase"]="first_snapshot_"+length;
                    var first=Text(await backend.GetNativeSketchAsync(after,sketch.NativeSketchId,ct));
                    var second=Text(await backend.GetNativeSketchAsync(after,sketch.NativeSketchId,ct));
                    var lastState=await backend.GetDocumentStateAsync(ct);
                    if(lastState.Revision!=after.Revision || lastState.DocumentId!=id) throw new InvalidOperationException("Readonly snapshots changed state.");
                    ((JArray)report["cases"]!).Add(new JObject {
                        ["document_id"]=id,["length_mm"]=length,["revision_before"]=state.Revision,["revision_after"]=after.Revision,
                        ["same_reference"]=first.Reference.EntityId==prior.Reference.EntityId,
                        ["first_text_changed"]=(string?)first.Data["text"]!=(string?)prior.Data["text"],
                        ["first_glyph_changed"]=!JToken.DeepEquals(first.Data["text_glyphs"],prior.Data["text_glyphs"]),
                        ["first_second_equal"]=JToken.DeepEquals(first.Data,second.Data),
                        ["before"]=prior.Data,["first"]=first.Data,["second"]=second.Data,
                        ["readonly_revision_preserved"]=true,["preview_rollback_exact"]=true });
                    state=after; prior=second;
                }
            }
            report["status"]="OBSERVATIONS_COMPLETE"; exit=0;
        }
        catch(Exception error){report["failure"]=new JObject {["type"]=error.GetType().FullName,["hresult"]=error.HResult};
            if(error is McpException mcp && System.Text.RegularExpressions.Regex.IsMatch(mcp.Code,"\\A[A-Z0-9_]{1,64}\\z")) report["failure"]!["code"]=mcp.Code;
            report["status"]="FAILED";}
        finally
        {
            if(bytes!=null) Array.Clear(bytes);
            File.Delete(args[1]); report["exit_code"]=exit;
            await File.WriteAllTextAsync(args[2],report.ToString(Newtonsoft.Json.Formatting.Indented));
        }
        return exit;
    }
}
