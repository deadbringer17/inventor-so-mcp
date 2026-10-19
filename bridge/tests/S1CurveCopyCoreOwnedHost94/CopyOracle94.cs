using Inventor;
using Bimwright.Ipt.Shared.Contracts;
using Newtonsoft.Json.Linq;

internal static partial class Program
{
    private static bool ExactNative94(JToken? a,JToken? b)=>string.Equals(a?.ToString(Newtonsoft.Json.Formatting.None),b?.ToString(Newtonsoft.Json.Formatting.None),StringComparison.Ordinal);
    private static readonly string[] Workflows94={"circle","arc_quarter","arc_wrap","arc_major","arc_clockwise","mixed_curves","mixed_all","line","text"};
    private static JObject Graph94(JObject full,OwnedDocument73 owned)=>(JObject)((JArray)((JArray)full["curve_point_graph92"]!).Single(d=>(string?)d["document_id"]==owned.DocumentId)["sketches"]!).Single(s=>(string?)s["key"]==Key73(CopySketch94(owned)));
    private static Dictionary<string,JObject> Index94(JToken rows)=>((JArray)rows).OfType<JObject>().ToDictionary(e=>(string)e["key"]!,StringComparer.Ordinal);
    private static string ReferenceKey94(JObject reference,OwnedDocument73 owned,string kind,string sketchId,string type)
    {
        var decoded=PersistentEntityReference.Decode((string)reference["entity_id"]!);
        Require89(reference.Count==6&&(string?)reference["document_id"]==owned.DocumentId&&(string?)reference["sketch_id"]==sketchId
            &&(string?)reference["subpart"]=="Entity"&&(string?)reference["type"]==type&&!string.IsNullOrEmpty((string?)reference["revision"])
            &&decoded.DocumentId==owned.DocumentId&&decoded.EntityType==kind&&decoded.Context.Length==0,"Oracle94 typed reference owner differs.");
        return Convert.ToBase64String(decoded.Key);
    }
    private static void VerifyCopies94()
    {
        GuardDocuments73();var client=ReadPublished94("copy-client94.json",64*1024*1024);
        Require89((string?)client["run_id"]==Settings.RunId&&(string?)client["target_id"]==TargetId
            &&(string?)client["status"]=="PASS_PC_CURVE_COPY_SUBSET"&&client["cases"] is JArray cases&&cases.Count==18
            &&client["refusals"] is JArray refusals&&refusals.Count==10&&CheckpointCount94==74,"Oracle94 terminal client/matrix/checkpoint scope differs.");
        VerifyCheckpointTrace94(client);
        var full=FullState92();var checks=new JArray();var trace=new JArray();
        foreach(var owned in Documents)
        {
            var sketch=CopySketch94(owned);string sketchId=Encode94(owned.DocumentId,"sketch",Key73(sketch));
            var before=Graph94(Initial94,owned);var after=Graph94(full,owned);
            var oldEntities=Index94(before["entities"]!);var nowEntities=Index94(after["entities"]!);
            var oldConstraints=Index94(before["constraints"]!);var nowConstraints=Index94(after["constraints"]!);
            foreach(var pair in oldEntities) Require89(nowEntities.TryGetValue(pair.Key,out var actual)&&ExactNative94(pair.Value,actual),"Oracle94 original curve/point state changed.");
            foreach(var pair in oldConstraints) Require89(nowConstraints.TryGetValue(pair.Key,out var actual)&&ExactNative94(pair.Value,actual),"Oracle94 original constraint state changed.");
            Require89(ExactNative94(before["dimension_count"],after["dimension_count"]),"Oracle94 dimensions changed.");
            Require89((string?)after["constraint_status"] is "kUnderConstrainedConstraintStatus" or "kFullyConstrainedConstraintStatus","Oracle94 solver invalid.");
            var allowedNew=new HashSet<string>(StringComparer.Ordinal);var expectedConstraints=new List<JObject>();
            var workflows=((JArray)client["cases"]!).OfType<JObject>().Where(w=>(string?)w["document_id"]==owned.DocumentId).ToArray();
            Require89(workflows.Length==9&&workflows.Select(w=>(string)w["workflow"]!).ToHashSet(StringComparer.Ordinal).SetEquals(Workflows94),"Oracle94 workflow labels differ.");
            foreach(var workflow in workflows)
            {
                string label=(string)workflow["workflow"]!;
                string[] labels=label=="mixed_curves"?new[]{"circle","arc_quarter","line"}:label=="mixed_all"?new[]{"circle","arc_quarter","line","text"}:new[]{label};
                var pairs=((JArray)workflow["pairs"]!).OfType<JObject>().ToArray();
                Require89(pairs.Length==labels.Length&&(string?)workflow["context"]==(owned.Sheet?"Sheet":"Part"),"Oracle94 pair/context count differs.");
                var delta=((JArray)workflow["delta_mm"]!).Values<double>().ToArray();Require89(delta.SequenceEqual(new[]{12d,-8d}),"Oracle94 translation differs.");
                var map=new Dictionary<string,string>(StringComparer.Ordinal);var remaining=labels.ToHashSet(StringComparer.Ordinal);
                foreach(var pair in pairs)
                {
                    string type=(string)pair["type"]!;bool text=type=="text";string kind=text?"sketch_text":"sketch_entity";
                    string sourceKey=ReferenceKey94((JObject)pair["source"]!,owned,kind,sketchId,type),copyKey=ReferenceKey94((JObject)pair["copy"]!,owned,kind,sketchId,type);
                    string sourceLabel=CreatorKeys94[owned.Sheet].Single(k=>k.Value==sourceKey).Key;
                    Require89(remaining.Remove(sourceLabel)&&sourceLabel!="parameter_text"&&copyKey!=sourceKey&&allowedNew.Add(copyKey),"Oracle94 source catalog/copy identity differs.");
                    JObject source,copy;
                    if(text)
                    {
                        source=Text73(sketch.TextBoxes.Cast<TextBox>().Single(t=>Key73(t)==sourceKey),sketch);
                        copy=Text73(sketch.TextBoxes.Cast<TextBox>().Single(t=>Key73(t)==copyKey),sketch);
                        var a=(JObject)source["copy_details88"]!;var b=(JObject)copy["copy_details88"]!;
                        string origin=(string)b["origin_key"]!;
                        Require89(!oldEntities.ContainsKey(origin)&&nowEntities.ContainsKey(origin)&&allowedNew.Add(origin),"Oracle94 copied text origin is shared.");
                        foreach(string field in new[]{"text","formatted_text","fitted","style","rotation_rad","width_scale","color_rgb"})Equal88(source[field],copy[field],field);
                        foreach(string field in new[]{"single_line","horizontal","vertical","line_spacing_type","line_spacing","style_key","style_location","font","font_size_mm","bold","italic","underline","color","style_color","raw_indices_native_one_based"})Equal88(a[field],b[field],field);
                        Shift88((JArray)a["origin_mm"]!,(JArray)b["origin_mm"]!,2,delta);Shift88((JArray)a["raw_vertices_model_mm"]!,(JArray)b["raw_vertices_model_mm"]!,3,delta);
                    }
                    else
                    {
                        source=nowEntities[sourceKey];copy=nowEntities[copyKey];
                        Require89(type==(string?)copy["kind"]&&type==(string?)source["kind"]&&(string?)copy["parent_key"]==Key73(sketch),"Oracle94 copied curve type/parent differs.");
                        map.Add(sourceKey,copyKey);
                        var a=(JObject)source["structural_points"]!;var b=(JObject)copy["structural_points"]!;
                        Require89(a.Properties().Select(p=>p.Name).SequenceEqual(b.Properties().Select(p=>p.Name)),"Oracle94 structural roles differ.");
                        foreach(var p in a.Properties())
                        {
                            string oldPoint=(string)p.Value["key"]!,newPoint=(string)b[p.Name]!["key"]!;
                            Require89(!oldEntities.ContainsKey(newPoint)&&nowEntities.ContainsKey(newPoint)&&allowedNew.Add(newPoint),"Oracle94 copied structural point is shared/unresolved.");
                            map.Add(oldPoint,newPoint);Shift88((JArray)p.Value["point_mm"]!,(JArray)b[p.Name]!["point_mm"]!,2,delta);
                            var pointExpected=(JObject)nowEntities[oldPoint].DeepClone();var pointActual=(JObject)nowEntities[newPoint].DeepClone();
                            pointExpected.Remove("key");pointExpected.Remove("point_mm");pointActual.Remove("key");pointActual.Remove("point_mm");
                            var pointOwners=(JArray)pointExpected["owned_by"]!;
                            for(int ownerIndex=0;ownerIndex<pointOwners.Count;ownerIndex++)
                            {
                                Require89((string?)pointOwners[ownerIndex]==sourceKey,"Oracle94 source point has an external owner.");
                                pointOwners[ownerIndex]=copyKey;
                            }
                            Equal88(pointExpected,pointActual,"mapped structural point flags/ownership");
                        }
                        foreach(string field in new[]{"native_type","construction","reference","has_reference_component","style_flags","centerline"})if(source[field]!=null)Equal88(source[field],copy[field],field);
                        // Evaluator domains may differ by floating point rounding
                        // under translation (observed in primitive92). This is
                        // only a source-to-copy comparison; originals remain exact.
                        var domainA=(JArray)source["domain"]!;var domainB=(JArray)copy["domain"]!;
                        Require89(domainA.Count==2&&domainB.Count==2,"Oracle94 evaluator domain shape differs.");
                        for(int domainIndex=0;domainIndex<2;domainIndex++)Require89(double.IsFinite((double)domainA[domainIndex])&&double.IsFinite((double)domainB[domainIndex])&&Math.Abs((double)domainA[domainIndex]-(double)domainB[domainIndex])<=1e-10,"Oracle94 evaluator domain differs.");
                        Require89(((JArray)copy["owned_by"]!).Count==0&&(string?)copy["constraint_status"] is "kUnderConstrainedConstraintStatus" or "kFullyConstrainedConstraintStatus","Oracle94 copied curve owner/solver differs.");
                        foreach(string field in new[]{"radius_mm","start_angle_rad","sweep_angle_rad","geometry_start_angle_rad","geometry_sweep_angle_rad"})if(source[field]!=null)
                            Require89(double.IsFinite((double)copy[field]!)&&Math.Abs((double)source[field]!-(double)copy[field]!)<=(field=="radius_mm"?1e-8:1e-10),"Oracle94 radius/angle differs.");
                        Shift88((JArray)source["strokes_mm"]!,(JArray)copy["strokes_mm"]!,2,delta);Shift88((JArray)source["samples_mm"]!,(JArray)copy["samples_mm"]!,2,delta);
                        CheckAnalytic94(copy);
                    }
                    Equal88(SourceData94[owned.DocumentId+":"+sourceKey],source,"source frozen from creator94");
                    checks.Add(new JObject { ["document_id"]=owned.DocumentId,["workflow"]=label,["type"]=type,["source_key"]=sourceKey,["copy_key"]=copyKey,
                        ["native_translation_and_original_exact"]=true,["independent_native_keys_points_glyphs"]=true });
                }
                Require89(remaining.Count==0,"Oracle94 workflow omitted a catalog source.");
                foreach(var constraint in oldConstraints.Values)
                {
                    var members=(JArray)constraint["members"]!;if(!members.Any(m=>map.ContainsKey((string)m["key"]!)))continue;
                    Require89((string?)constraint["binding_status"]=="COMPLETE_TYPED_MEMBERS"&&members.All(m=>map.ContainsKey((string)m["key"]!)),"Oracle94 source relation is external/unknown.");
                    var mapped=(JObject)constraint.DeepClone();mapped.Remove("key");foreach(var member in (JArray)mapped["members"]!)member["key"]=map[(string)member["key"]!];expectedConstraints.Add(mapped);
                }
            }
            foreach(var constraint in nowConstraints.Values.Where(c=>!oldConstraints.ContainsKey((string)c["key"]!)))
            {
                Require89((string?)constraint["binding_status"]=="COMPLETE_TYPED_MEMBERS"&&((JArray)constraint["members"]!).All(m=>allowedNew.Contains((string)m["key"]!)),"Oracle94 new relation crosses old or unsupported geometry.");
                var candidate=(JObject)constraint.DeepClone();candidate.Remove("key");int found=expectedConstraints.FindIndex(e=>ExactNative94(e,candidate));Require89(found>=0,"Oracle94 new intrinsic graph mismatch.");expectedConstraints.RemoveAt(found);
            }
            Require89(expectedConstraints.Count==0&&nowEntities.Keys.Where(k=>!oldEntities.ContainsKey(k)).All(allowedNew.Contains),"Oracle94 missing intrinsic relation or unexplained new entity.");
            foreach(var label in new[]{"text","parameter_text"})
            {
                string key=CreatorKeys94[owned.Sheet][label];Equal88(SourceData94[owned.DocumentId+":"+key],Text73(sketch.TextBoxes.Cast<TextBox>().Single(t=>Key73(t)==key),sketch),"unselected/original native text");
            }
            Require89(sketch.TextBoxes.Count==4,"Oracle94 unexpected extra native text.");
            trace.Add(new JObject { ["context"]=owned.Sheet?"Sheet":"Part",["originals_exact"]=true,["native_graph_mapped_exact"]=true });
        }
        Equal88(Unrelated94(Initial94),Unrelated94(full),"unrelated native documents/styles/parameters");
        Require89(checks.Count==28,"Oracle94 requires28 native pairs.");
        WritePublic73("copy-native-verifier94.json",new JObject { ["schema"]=1,["run_id"]=Settings.RunId,["target_id"]=TargetId,["status"]="PASS_NATIVE_CURVE_COPY_SUBSET",
            ["checks"]=checks,["contexts"]=trace,["checkpoints"]=CheckpointCount94,["quest_proof"]=false,["source_update_proof"]=false,["persistence_proof"]=false,
            ["not_covered"]=new JArray("UI/Quest/physical","Source updates/reopen","Other Copy profiles","Optional UNKNOWN native style properties") });
    }
    private static JObject Unrelated94(JObject source)
    {
        var state=(JObject)source.DeepClone();state.Remove("active_document_id");
        foreach(var doc in (JArray)state["documents"]!) {doc["dirty"]?.Parent?.Remove();doc["database_revision_id"]?.Parent?.Remove();((JArray)doc["sketches"]!).Single(s=>(string?)s["name"]=="S1_Copy88").Remove();}
        foreach(var doc in (JArray)state["curve_point_graph92"]!)((JArray)doc["sketches"]!).Single(s=>(string?)s["name"]=="S1_Copy88").Remove();
        return state;
    }
    private static void CheckAnalytic94(JObject curve)
    {
        var samples=(JArray)curve["samples_mm"]!;var points=(JObject)curve["structural_points"]!;
        if((string?)curve["kind"]=="line")
        {
            var a=(JArray)points["start"]!["point_mm"]!;var b=(JArray)points["end"]!["point_mm"]!;double dx=(double)b[0]-(double)a[0],dy=(double)b[1]-(double)a[1],length=Math.Sqrt(dx*dx+dy*dy);
            Require89(double.IsFinite(length)&&length>0,"Oracle94 degenerate line.");for(int i=0;i<samples.Count;i+=2)Require89(Math.Abs(((double)samples[i]-(double)a[0])*dy-((double)samples[i+1]-(double)a[1])*dx)/length<=1e-5,"Oracle94 line support differs.");
        }
        else {var center=(JArray)points["center"]!["point_mm"]!;double radius=(double)curve["radius_mm"]!;Require89(double.IsFinite(radius)&&radius>0,"Oracle94 radius invalid.");for(int i=0;i<samples.Count;i+=2){double x=(double)samples[i]-(double)center[0],y=(double)samples[i+1]-(double)center[1];Require89(Math.Abs(Math.Sqrt(x*x+y*y)-radius)<=1e-5,"Oracle94 circle support differs.");}}
    }
}
