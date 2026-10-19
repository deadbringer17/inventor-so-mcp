using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Mcp;
using InventorXrSo.Core.Pairing;
using Newtonsoft.Json.Linq;

internal static partial class Program
{
    private sealed class Baseline90
    {
        internal DocumentState State=null!;
        internal string SketchId="",SourceLineId="",SourceTextId="",ParameterTextId="";
        internal HashSet<string> CopyIds=new(StringComparer.Ordinal);
        internal JObject Initial=null!,Last=null!;
    }
    private static JObject StateData90(DocumentState state)=>new() {
        ["document_id"]=state.DocumentId,["revision"]=state.Revision,["visual_revision"]=state.VisualRevision };
    private static string Epoch90(DocumentState state)
    {
        Require(Regex.IsMatch(state.Revision,@"\A[a-f0-9]{32}:[0-9]+\z"),"CAD event revision has an unexpected epoch/token shape.");
        return state.Revision.Substring(0,32);
    }
    private static Dictionary<string,JObject> Rows90(JObject represented,string collection)
        =>((JArray)represented[collection]!).OfType<JObject>().ToDictionary(row=>(string)row["id"]!,row=>row,StringComparer.Ordinal);
    private static async Task RunCoreLifecycle90(InventorBackend backend,S1AcceptanceBootstrapMetadata binding,JObject report,string reportPath,CancellationToken ct)
    {
        report["lifecycle"]=new JArray();report["baselines90"]=new JArray();
        report["not_covered"]=new JArray("Parameter-bound text COPY (still refused)", "Other curve/constraint/rich-text/rotation profiles",
            "Shared style mutation independence", "Native edits90 are fixture transactions, not Core product preview/commit", "Desktop/Quest/physical");
        var protocol=new ProtocolState90 { Directory=Path.GetDirectoryName(Path.GetFullPath(reportPath))!,RunId=binding.RunId,TargetId=binding.TargetId };
        report["protocol90"]=protocol.Evidence;
        var baselines=new Dictionary<string,Baseline90>(StringComparer.Ordinal);
        Phase="core90_prepare_pacing";await Task.Delay(TimeSpan.FromSeconds(65),ct);
        // Preparatory reads only: bounded initialization STALE renewal is legal here,
        // before the first of14 IPC requests. No post-edit or post-Open read is retried.
        foreach(string contextName in new[]{"Part","Sheet"})
        {
            string id=contextName=="Part"?binding.PartDocumentId:binding.SheetDocumentId;
            await Inventory(backend,binding,ct);await backend.ActivateOpenAsync(id,ct);
            DocumentState state=await backend.GetDocumentStateAsync(ct);Require(state.DocumentId==id,"Baseline90 activation owner differs.");
            DesignContext context;int contextRenewals=0;
            while(true)
            {
                try { context=await backend.GetDesignContextAsync(state,ct);break; }
                catch(McpException e) when(e.Code=="STALE_REVISION"&&contextRenewals++<3)
                {state=await backend.GetDocumentStateAsync(ct);Require(state.DocumentId==id,"Baseline90 context owner changed.");}
            }
            Require(!context.Truncated,"Baseline90 context is partial.");
            var fixtureParameters=context.Parameters.OfType<JObject>().Where(p=>(string?)p["name"]=="S1Copy88Length").ToArray();
            Require(fixtureParameters.Length==1&&fixtureParameters[0]["value_mm"]?.Type is (JTokenType.Integer or JTokenType.Float)
                &&Math.Abs((double)fixtureParameters[0]["value_mm"]!-13)<=Tol&&(string?)fixtureParameters[0]["unit"]=="mm",
                "Preparatory Core fixture parameter is not the unchanged13mm length.");
            string sketchId=context.Sketches.Single(s=>s.Name=="S1_Copy88").NativeSketchId;
            NativeSketchSnapshot snapshot;int initialRenewals=0;
            while(true)
            {
                try { snapshot=await backend.GetNativeSketchAsync(state,sketchId,ct);break; }
                catch(McpException e) when(e.Code=="STALE_REVISION"&&initialRenewals++<3)
                {state=await backend.GetDocumentStateAsync(ct);Require(state.DocumentId==id,"Baseline90 snapshot owner changed.");}
            }
            Require(snapshot.NativeMutationComplete&&!snapshot.Partial,"Baseline90 native snapshot is incomplete.");
            var cases=((JArray)report["cases"]!).OfType<JObject>().Where(c=>(string?)c["context"]==contextName).ToArray();
            Require(cases.Length==3,"Prerequisite88 context workflows missing.");
            var pairs=cases.SelectMany(c=>((JArray)c["pairs"]!).OfType<JObject>()).ToArray();
            string line=pairs.Where(p=>(string?)p["type"]=="line").Select(p=>(string)p["source"]!["entity_id"]!).Distinct().Single();
            string text=pairs.Where(p=>(string?)p["type"]=="text").Select(p=>(string)p["source"]!["entity_id"]!).Distinct().Single();
            string param=snapshot.Entities.Single(e=>e.Type=="text"&&((string?)e.Data["formatted_text"]??"").Contains("<Parameter",StringComparison.Ordinal)).Reference.EntityId;
            var baseline=new Baseline90 { State=state,SketchId=sketchId,SourceLineId=line,SourceTextId=text,ParameterTextId=param,
                CopyIds=pairs.Select(p=>(string)p["copy"]!["entity_id"]!).ToHashSet(StringComparer.Ordinal),Initial=Represented(snapshot),Last=Represented(snapshot) };
            Require(baseline.CopyIds.Count==4&&baseline.CopyIds.All(key=>snapshot.Entities.Any(e=>e.Reference.EntityId==key)),"Baseline90 explicit copy IDs differ.");
            baselines.Add(contextName,baseline);
            ((JArray)report["baselines90"]!).Add(new JObject { ["context"]=contextName,["state"]=StateData90(state),["sketch_id"]=sketchId,
                ["copy_ids"]=new JArray(baseline.CopyIds),["snapshot_raw"]=baseline.Initial,["context_renewals"]=contextRenewals,["initial_renewals"]=initialRenewals });
        }
        await Action90(protocol,binding,"prepare","all",0,ct);
        foreach(string kind in new[]{"Part","Sheet"})
        {
            var metadata=protocol.Documents[kind];var baseline=baselines[kind];
            Require(metadata.SketchId==baseline.SketchId&&metadata.SourceIds["line"]==baseline.SourceLineId
                &&metadata.SourceIds["text"]==baseline.SourceTextId&&metadata.SourceIds["parameter_text"]==baseline.ParameterTextId,
                "Prepared native source map differs from prerequisite88 Core identities.");
            Require(metadata.SourceIds.Values.All(id=>Rows90(baseline.Initial,"entities").ContainsKey(id)),"Prepared native point/source IDs missing from Core baseline.");
            _=ParameterCanonical90(Rows90(baseline.Initial,"entities")[metadata.SourceIds["parameter_text"]],metadata);
        }
        foreach(string kind in new[]{"Part","Sheet"})
        {
            var baseline=baselines[kind];
            foreach(int pass in new[]{1,2})
            {
                Phase="core90_before_native_edit_pacing";await Task.Delay(TimeSpan.FromSeconds(65),ct);
                await Action90(protocol,binding,"edit",kind,pass,ct);
                var metadata=protocol.Documents[kind];
                // EXACT first two HTTP calls after native End/ACK. No activation,
                // inventory, context, delay, retry, oracle or fabricated LastCommit.
                Phase="core90_first_state_after_edit_"+kind+"_"+pass;
                var state=await backend.GetDocumentStateAsync(ct);
                Require(state.DocumentId==metadata.DocumentId&&Epoch90(state)==Epoch90(baseline.State)&&state.Revision!=baseline.State.Revision,
                    "First state after fixture edit has wrong owner/epoch or unchanged revision.");
                Phase="core90_first_snapshot_after_edit_"+kind+"_"+pass;
                report["pending_first_read90"]=new JObject {["kind"]="source_edit",["context"]=kind,["pass"]=pass,["state"]=StateData90(state),["sketch_id"]=metadata.SketchId,["attempts"]=1};
                var first=await backend.GetNativeSketchAsync(state,metadata.SketchId,ct); // ONE attempt. STALE is evidence, never retried.
                var observation=new JObject { ["kind"]="source_edit",["context"]=kind,["pass"]=pass,
                    ["mutation_origin"]="native_fixture_edit",["event_revision_before"]=baseline.State.Revision,["event_revision_after"]=state.Revision,
                    ["state"]=StateData90(state),["revision"]=state.Revision,["snapshot_raw"]=Represented(first),["first_core_snapshot"]=true };
                ((JArray)report["lifecycle"]!).Add(observation); // Preserve first raw read even when its assertions fail.
                ValidateFresh90(first,state,metadata,baseline);
                CheckIndependent90(baseline.Initial,Represented(first),metadata,metadata,baseline,false);
                SourceInvariants90(baseline.Initial,Represented(first),metadata);
                CheckSources90(first,Rows90(baseline.Last,"entities")[metadata.SourceIds["text"]],metadata,pass);
                observation["copies_and_unrelated_exact"]=true;observation["fresh_references"]=true;
                baseline.State=state;baseline.Last=Represented(first);
                await Action90(protocol,binding,"inspect",kind,pass,ct); // Native oracle only AFTER first Core assertions.
            }
            Phase="core90_before_reopen_pacing";await Task.Delay(TimeSpan.FromSeconds(65),ct);
            var oldMetadata=protocol.Documents[kind];
            // Only frozen Data/IDs remain for comparison. No old typed reference/session
            // is used as an input to any post-reopen call, even when doc GUID is equal.
            await Action90(protocol,binding,"reopen",kind,0,ct);
            var reopenedMetadata=protocol.Documents[kind];
            Phase="core90_post_reopen_inventory";await MetadataInventory90(backend,protocol,ct);
            Phase="core90_first_state_after_reopen_"+kind;
            var reopenedState=await backend.GetDocumentStateAsync(ct);
            Require(reopenedState.DocumentId==reopenedMetadata.DocumentId&&Epoch90(reopenedState)==Epoch90(baseline.State)
                &&reopenedState.Revision!=baseline.State.Revision,"First reopened state owner/epoch/revision differs.");
            Phase="core90_first_snapshot_after_reopen_"+kind;
            report["pending_first_read90"]=new JObject {["kind"]="reopen",["context"]=kind,["pass"]=0,["state"]=StateData90(reopenedState),["sketch_id"]=reopenedMetadata.SketchId,["attempts"]=1};
            var reopened=await backend.GetNativeSketchAsync(reopenedState,reopenedMetadata.SketchId,ct); // ONE attempt, no context initialization retry.
            var persistence=new JObject { ["kind"]="reopen",["context"]=kind,["pass"]=0,
                ["event_revision_before"]=baseline.State.Revision,["event_revision_after"]=reopenedState.Revision,
                ["state"]=StateData90(reopenedState),["revision"]=reopenedState.Revision,["snapshot_raw"]=Represented(reopened),["first_core_snapshot"]=true,
                ["before_native_path"]=oldMetadata.Path,["reopened_native_path"]=reopenedMetadata.Path };
            ((JArray)report["lifecycle"]!).Add(persistence);
            ValidateFresh90(reopened,reopenedState,reopenedMetadata,baseline);
            CheckIndependent90(baseline.Last,Represented(reopened),oldMetadata,reopenedMetadata,baseline,true);
            persistence["copies_and_unrelated_exact"]=true;persistence["fresh_references"]=true;
            persistence["parameter_owner_relocation_only"]=true;
            baseline.State=reopenedState;baseline.Last=Represented(reopened);
            await Action90(protocol,binding,"inspect_reopen",kind,0,ct);
        }
        await Action90(protocol,binding,"complete","all",0,ct);
        Require(protocol.Ordinal==14&&((JArray)report["lifecycle"]!).Count==6,"Lifecycle90 sequence incomplete.");
        report.Remove("pending_first_read90");report["source_updates"]=4;report["reopens"]=2;
        report["open_s1_gates"]=18;report["quest_proof"]=false;
    }
    private static async Task MetadataInventory90(InventorBackend backend,ProtocolState90 protocol,CancellationToken ct)
    {
        var documents=await backend.ListOpenAsync(ct);
        Require(documents.Count==2&&protocol.Documents.Values.All(m=>documents.Count(d=>d.Id==m.DocumentId&&d.Name==m.DisplayName)==1),
            "Fresh post-reopen open-document inventory differs from native attestation.");
    }
    private static void ValidateFresh90(NativeSketchSnapshot snapshot,DocumentState state,OwnedMetadata90 metadata,Baseline90 baseline)
    {
        Require(snapshot.NativeMutationComplete&&!snapshot.Partial&&snapshot.SketchId==metadata.SketchId&&Same(snapshot.State,state),"First native snapshot is incomplete or bound elsewhere.");
        foreach(var entity in snapshot.Entities.Concat(snapshot.Dimensions).Concat(snapshot.Constraints))
            Require(entity.Reference.Matches(state,metadata.SketchId),"First snapshot contains stale typed references.");
        Require(baseline.CopyIds.All(id=>snapshot.Entities.Count(e=>e.Reference.EntityId==id)==1)
            &&metadata.SourceIds.Values.All(id=>snapshot.Entities.Count(e=>e.Reference.EntityId==id)==1),"Copy/source identity lost after native lifecycle.");
    }
    private static void CheckIndependent90(JObject expected,JObject actual,OwnedMetadata90 before,OwnedMetadata90 after,Baseline90 baseline,bool reopened)
    {
        Require(before.DocumentId==after.DocumentId&&before.ParameterKey==after.ParameterKey,"Parameter relocation changed native owner/key.");
        var sourceIncidences=Rows90(expected,"constraints").Values.Where(c=>SourceIncidence90(c,before)).ToArray();
        Require(sourceIncidences.Length==2,"Exactly two intrinsic source-line incidences required.");
        foreach(string collection in new[]{"entities","dimensions","constraints"})
        {
            var one=Rows90(expected,collection);var two=Rows90(actual,collection);
            Require(one.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(two.Keys),"Lifecycle entity/dimension/constraint inventory changed.");
            foreach(var pair in one)
            {
                string id=pair.Key;
                if(!reopened&&collection=="constraints"&&sourceIncidences.Any(c=>(string?)c["id"]==id))
                {
                    var oldConstraint=(JObject)pair.Value.DeepClone();var newConstraint=(JObject)two[id].DeepClone();
                    // The producer anchors a line-first coincidence at that line's
                    // StartSketchPoint. Its display annotation follows the source;
                    // identity, topology and all other constraint properties stay exact.
                    Require(JToken.DeepEquals(oldConstraint["annotation_mm"],Rows90(expected,"entities")[before.SourceIds["line"]]["start_mm"])
                        &&JToken.DeepEquals(newConstraint["annotation_mm"],Rows90(actual,"entities")[after.SourceIds["line"]]["start_mm"]),
                        "Intrinsic source incidence annotation does not follow its exact native line anchor.");
                    oldConstraint.Remove("annotation_mm");newConstraint.Remove("annotation_mm");
                    Require(JToken.DeepEquals(oldConstraint,newConstraint),"Intrinsic source incidence changed beyond its declared annotation anchor.");
                    continue;
                }
                if(collection=="entities"&&id==before.SourceIds["parameter_text"])
                {
                    Require(JToken.DeepEquals(ParameterCanonical90(pair.Value,before),ParameterCanonical90(two[id],after)),"Uncopied parameter text changed beyond owned local path relocation.");
                    continue;
                }
                bool mutable=collection=="entities"&&new[]{"line","start","end","text","origin"}.Any(role=>before.SourceIds[role]==id);
                if(reopened||!mutable) Require(JToken.DeepEquals(pair.Value,two[id]),"Copy/unrelated represented Data changed during source edit or persistence.");
            }
        }
        Require(JToken.DeepEquals(expected["complete"],actual["complete"])&&JToken.DeepEquals(expected["partial"],actual["partial"])
            &&JToken.DeepEquals(expected["constraint_status"],actual["constraint_status"]),"Lifecycle completeness/solver representation changed.");
        foreach(string copyId in baseline.CopyIds) Require(JToken.DeepEquals(Rows90(baseline.Initial,"entities")[copyId],Rows90(actual,"entities")[copyId]),"An explicit copied object changed.");
    }
    private static bool SourceIncidence90(JObject constraint,OwnedMetadata90 metadata)
    {
        if((string?)constraint["type"]!="constraint"||(string?)constraint["native_type"]!="kCoincidentConstraintObject"
            ||constraint["deletable"]?.Type!=JTokenType.Boolean||(bool)constraint["deletable"]!
            ||(string?)constraint["binding_state"]!="complete") return false;
        foreach(var endpoint in new[]{("start","Start"),("end","End")})
        {
            JObject Link(string id,string type,string subpart)=>new(){["entity_id"]=id,["type"]=type,["subpart"]=subpart};
            var expected=new JArray(Link(metadata.SourceIds["line"],"line","Entity"),
                Link(metadata.SourceIds[endpoint.Item1],"point","Entity"),Link(metadata.SourceIds[endpoint.Item1],"point","Point"),
                Link(metadata.SourceIds["line"],"line",endpoint.Item2));
            if(JToken.DeepEquals(constraint["related_entities"],expected)) return true;
        }
        return false;
    }
    private static void SourceInvariants90(JObject initial,JObject actual,OwnedMetadata90 metadata)
    {
        var before=Rows90(initial,"entities");var after=Rows90(actual,"entities");
        foreach(string role in new[]{"line","start","end","text","origin"})
        {
            string id=metadata.SourceIds[role];var expected=(JObject)before[id].DeepClone();var current=(JObject)after[id].DeepClone();
            string[] fields=role=="line"?new[]{"start_mm","end_mm","strokes_mm"}:role=="text"
                ?new[]{"text","formatted_text","text_mm","text_bounds_mm","text_glyphs"}:new[]{"point_mm"};
            foreach(string field in fields){expected.Remove(field);current.Remove(field);}
            Require(JToken.DeepEquals(expected,current),"Native source edit changed an undeclared represented property/type/format.");
        }
    }
    private static void CheckSources90(NativeSketchSnapshot snapshot,JObject previousText,OwnedMetadata90 metadata,int pass)
    {
        var map=Rows90(Represented(snapshot),"entities");
        double sx=pass==1?8:13,sy=pass==1?14:4,ex=pass==1?40:29,ey=pass==1?22:9,tx=pass==1?41:16,ty=pass==1?37:44;
        void Point(JToken? value,double x,double y)=>Require(value is JArray p&&p.Count==2&&Math.Abs((double)p[0]-x)<=Tol&&Math.Abs((double)p[1]-y)<=Tol,"First source point differs from declared native fixture edit.");
        var line=map[metadata.SourceIds["line"]];Require((string?)line["type"]=="line","Source line type changed.");
        Point(line["start_mm"],sx,sy);Point(line["end_mm"],ex,ey);
        Point(map[metadata.SourceIds["start"]]["point_mm"],sx,sy);Point(map[metadata.SourceIds["end"]]["point_mm"],ex,ey);
        var text=map[metadata.SourceIds["text"]];
        Require((string?)text["type"]=="text"&&(string?)text["text"]==(pass==1?"Source89 pass1 & changed":"Source89 second content")
            &&(string?)text["formatted_text"]==(pass==1?"Source89 pass1 &amp; changed":"Source89 second content"),"First source content differs.");
        Point(text["text_mm"],tx,ty);Point(map[metadata.SourceIds["origin"]]["point_mm"],tx,ty);
        Require(text["text_glyphs"] is JObject glyphs&&glyphs["vertices_mm"] is JArray vertices&&vertices.Count>0&&glyphs["indices"] is JArray indices&&indices.Count>0
            &&!JToken.DeepEquals(previousText["text_glyphs"]?["vertices_mm"],text["text_glyphs"]?["vertices_mm"]),"First edited source glyphs are empty or stale.");
        Require(text["text_bounds_mm"] is JArray bounds&&bounds.Count==8&&!JToken.DeepEquals(previousText["text_bounds_mm"],bounds),"First source bounds are empty or stale.");
        Require((string?)text["text_glyphs"]!["coordinate_space"]=="model_mm","First source glyph coordinate space differs.");
        Require(JToken.DeepEquals(text["text_glyphs"]!["color_rgb"],pass==1?new JArray(33,117,155):new JArray(95,42,61)),"First source glyph color differs from native fixture override.");
    }
    private static JObject ParameterCanonical90(JObject data,OwnedMetadata90 metadata)
    {
        Require((string?)data["id"]==metadata.SourceIds["parameter_text"]&&(string?)data["type"]=="text"&&(string?)data["text"]=="Bound=13",
            "Only the explicitly attested unchanged parameter text can be normalized.");
        string markup=(string?)data["formatted_text"]??"";
        Require(markup.Length<=4096,"Parameter field markup budget exceeded.");
        var settings=new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=4111};
        using var input=new StringReader("<owner>"+markup+"</owner>");using var reader=XmlReader.Create(input,settings);
        var root=XElement.Load(reader,LoadOptions.PreserveWhitespace);var nodes=root.Nodes().ToArray();
        Require(nodes.Length==2&&nodes[0].GetType()==typeof(XText)&&((XText)nodes[0]).Value=="Bound="&&nodes[1] is XElement,"Parameter field grammar differs.");
        var parameter=(XElement)nodes[1];
        Require(parameter.Name==XName.Get("Parameter")&&parameter.Attributes().Count()==4
            &&parameter.Attributes().All(a=>a.Name.Namespace==XNamespace.None&&new[]{"Resolved","ComponentIdentifier","Name","Precision"}.Contains(a.Name.LocalName,StringComparer.Ordinal))
            &&(string?)parameter.Attribute("Resolved")=="True"&&(string?)parameter.Attribute("Name")=="S1Copy88Length"
            &&(string?)parameter.Attribute("Precision")=="0"&&parameter.Nodes().Count()==1&&parameter.FirstNode!.GetType()==typeof(XText)&&parameter.Value=="13",
            "Parameter field is not the exact unchanged13mm fixture binding.");
        string owner=(string?)parameter.Attribute("ComponentIdentifier")??"";
        Require(Path.IsPathFullyQualified(owner)&&SamePath90(owner,metadata.Path),"Parameter field owner is not the exact attested owned native file.");
        parameter.Attribute("ComponentIdentifier")!.Value="owned-local:"+metadata.DocumentId+"#"+metadata.ParameterKey;
        string canonical=root.ToString(SaveOptions.DisableFormatting);
        var result=(JObject)data.DeepClone();result["formatted_text"]=canonical.Substring(7,canonical.Length-15);
        return result;
    }
}
