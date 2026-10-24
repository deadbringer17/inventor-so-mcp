using Bimwright.Ipt.Server;
using Bimwright.Ipt.Shared.Contracts;
using Inventor;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

internal static partial class Program
{
    private const string Candidate99="0D7801E7332B0743FCDB00C343CBDE888B9BBD8B1A70281DB14CD200CECC22E1";
    private const string Pass99="PASS_PLUGIN_NATIVE_SPLINE_TENSION_EDIT_SUBSET";
    private static bool Done99;
    private static int RawCount99;
    private static DateTimeOffset Deadline99;
    private static bool Exact99(JToken a,JToken b)=>a.ToString(Formatting.None)==b.ToString(Formatting.None);
    private static void Budget99(){GuardDocuments73();Require89(DateTimeOffset.UtcNow<Deadline99,"Spline tension99 thirty-minute suite deadline exceeded.");}
    private static void Own99(Transaction transaction,bool enforceDeadline=true)
    {
        if(enforceDeadline)Budget99();else GuardDocuments73();
        Require89(!transaction.HasParentTransaction&&SameCom73(Application!.TransactionManager.CurrentTransaction,transaction),"Spline tension99 current top-level owned transaction differs.");
    }
    private static JObject Wire99(PluginClient client,string command,JObject args)
    {
        Budget99();Require89(client.CurrentTarget?.TargetId==TargetId,"Spline tension99 exact target changed.");
        using var cancel=new CancellationTokenSource(TimeSpan.FromSeconds(45));
        return (JObject)client.SendAsync(command,args,cancel.Token).GetAwaiter().GetResult();
    }
    private static JObject State99(PluginClient client,OwnedDocument73 owned)
    {
        var result=Wire99(client,"get_visual_revision",new JObject{["document_id"]=owned.DocumentId});
        Require89((string?)result["document_id"]==owned.DocumentId&&!string.IsNullOrEmpty((string?)result["revision"]),"Spline tension99 document/revision unavailable.");return result;
    }
    private static string Id99(OwnedDocument73 owned,object entity,string kind="sketch_entity")
        =>new PersistentEntityReference{DocumentId=owned.DocumentId,EntityType=kind,Key=Convert.FromBase64String(Key73(entity))}.Encode();
    private static JObject Ref99(OwnedDocument73 owned,PlanarSketch sketch,object entity,string type,string revision,string subpart="Entity")
        =>new(){["document_id"]=owned.DocumentId,["revision"]=revision,["sketch_id"]=Id99(owned,sketch,"sketch"),["entity_id"]=Id99(owned,entity),["type"]=type,["subpart"]=subpart};
    private static JObject Op99(string sketch,string variant,JObject options)
        =>new(){["command"]="edit_sketch",["arguments"]=new JObject{["sketch_id"]=sketch,["variant"]=variant,["options"]=options}};
    private static JObject Args99(OwnedDocument73 owned,string revision,JArray operations,bool preview)
        =>new(){["document_id"]=owned.DocumentId,["expected_revision"]=revision,["operations"]=operations,["preview"]=preview,["validate"]=new JArray("rebuild","feature_health")};
    private static JObject Full99()
    {
        GuardDocuments73();var before=Documents.ToDictionary(d=>d.InternalName,d=>(d.Document.DatabaseRevisionId,d.Document.Dirty));
        var state=CopyFullNative88();
        state["spline_definitions99"]=new JArray(Documents.Select(d=>new JObject{
            ["document_id"]=d.DocumentId,["sketches"]=new JArray(d.Document.ComponentDefinition.Sketches.Cast<PlanarSketch>().Select(s=>new JObject{
                ["sketch_key"]=Key73(s),["fit"]=new JArray(s.SketchSplines.Cast<SketchSpline>().Select(c=>Curve99((SketchEntity)c))),
                ["control"]=new JArray(s.SketchControlPointSplines.Cast<SketchControlPointSpline>().Select(c=>Curve99((SketchEntity)c))) })) }));
        GuardDocuments73();Require89(Documents.All(d=>d.Document.DatabaseRevisionId==before[d.InternalName].DatabaseRevisionId&&d.Document.Dirty==before[d.InternalName].Dirty),"Spline tension99 full read changed Dirty or DatabaseRevisionId.");
        return state;
    }
    private static JObject Contents99(JObject source,OwnedDocument73 target)
    {
        var result=(JObject)source.DeepClone();var doc=((JArray)result["documents"]!).Single(d=>(string?)d["document_id"]==target.DocumentId);
        doc["dirty"]?.Parent?.Remove();doc["database_revision_id"]?.Parent?.Remove();return result;
    }
    private static JObject Save99(JObject state)
    {
        string file="spline-native99-"+(++RawCount99).ToString("D3")+".json";
        WritePublic73(file,new JObject{["schema"]=1,["run_id"]=Settings.RunId,["target_id"]=TargetId,["native_state"]=state});
        return new JObject{["file"]=file,["native_state_sha256"]=HashText73(state.ToString(Formatting.None))};
    }
    private static SketchEntity Create99(PlanarSketch sketch,bool control,int count,bool closed,int method)
    {
        var objects=Application!.TransientObjects.CreateObjectCollection();
        for(int i=0;i<count;i++)objects.Add(Application.TransientGeometry.CreatePoint2d(i,Math.Sin(i*.7)));
        if(control)
        {
            if(closed)objects.Add(Application.TransientGeometry.CreatePoint2d(0,0));
            return (SketchEntity)sketch.SketchControlPointSplines.Add(objects);
        }
        var fit=sketch.SketchSplines.Add(objects,(SplineFitMethodEnum)method);fit.Closed=closed;return (SketchEntity)fit;
    }
    private static SketchPoint Select99(SketchEntity curve,string role,out int ordinal)
    {
        int count=curve is SketchSpline fit?fit.FitPointCount:((SketchControlPointSpline)curve).ControlPointCount;
        Require89(count is >=2 and <=2000,"Spline tension99 native member count unavailable.");
        ordinal=role switch{"Start"=>1,"Interior"=>(count+1)/2,"End"=>count,_=>throw new HostFailure73("Spline tension99 role invalid.")};
        var point=curve is SketchSpline spline?spline.FitPoint[ordinal]:((SketchControlPointSpline)curve).ControlPoint[ordinal];
        Require89(point!=null&&SameCom73(curve.Parent,point.Parent),"Spline tension99 actual member has a foreign sketch owner.");return point!;
    }
    private static void Identity99(JObject before,JObject after)
    {
        Require89((string?)before["key"]==(string?)after["key"],"Spline tension99 replaced the existing native curve.");
        var a=(JArray)before["points"]!;var b=(JArray)after["points"]!;
        Require89(a.Count==b.Count,"Spline tension99 changed native point membership cardinality.");
        for(int i=0;i<a.Count;i++)Require89((string?)a[i]["key"]==(string?)b[i]["key"],"Spline tension99 replaced or reordered an existing native member.");
    }
    private static void Remove99(OwnedDocument73 owned,PlanarSketch sketch,string key)
    {
        RequireIdle89();Require89(SameCom73(sketch.Parent,owned.Document.ComponentDefinition)&&Key73(sketch)==key
            &&owned.Document.ComponentDefinition.Sketches.Cast<PlanarSketch>().Count(s=>SameCom73(s,sketch)&&Key73(s)==key)==1,"Spline tension99 cleanup exact owned sketch proof differs.");
        var transaction=Application!.TransactionManager.StartTransaction((_Document)owned.Document,"Spline99 remove exact owned case sketch");
        try{Own99(transaction,false);sketch.Delete();transaction.End();}catch{transaction.Abort();throw;}
    }
    private static bool First99(JObject snapshot,OwnedDocument73 owned,JObject actual)
    {
        Require89((string?)snapshot["document_id"]==owned.DocumentId&&(bool?)snapshot["partial"]==false
            &&(string?)snapshot["mutation_completeness"]?["state"]=="complete"&&(int?)snapshot["mutation_completeness"]?["read_error_count"]==0,"Spline tension99 first product snapshot incomplete.");
        var entities=(JArray)snapshot["entities"]!;
        foreach(string collection in new[]{"entities","constraints","dimensions"})
            Require89((int?)snapshot["mutation_completeness"]?["collections"]?[collection]?["native_count"]==((JArray)snapshot[collection]!).Count
                &&(int?)snapshot["mutation_completeness"]?["collections"]?[collection]?["represented_count"]==((JArray)snapshot[collection]!).Count,"Spline tension99 first snapshot collection coverage differs.");
        var map=new Dictionary<string,JObject>(StringComparer.Ordinal);
        foreach(JObject entity in entities)
        {var reference=PersistentEntityReference.Decode((string)entity["id"]!);Require89(reference.DocumentId==owned.DocumentId&&reference.EntityType=="sketch_entity"&&reference.Context.Length==0&&map.TryAdd(Convert.ToBase64String(reference.Key),entity),"Spline tension99 first snapshot native identity ambiguous.");}
        Require89(map.TryGetValue((string)actual["key"]!,out var curve),"Spline tension99 original curve absent from first snapshot.");
        var displayed=curve!;
        Require89((string?)displayed["native_type"]==(string?)actual["native_type"]&&(bool?)displayed["supported"]==true
            &&(bool?)displayed["closed"]==(bool?)actual["closed"]&&(bool?)displayed["construction"]==(bool?)actual["construction"],"Spline tension99 first product curve flags/closure differ.");
        // The current reader does not publish Reference. An absent field is
        // not a false flag; native actual/oracle comparisons still check it.
        bool referenceExposed=displayed.TryGetValue("reference",out var referenceToken);
        if(referenceExposed)Require89(referenceToken!.Type==JTokenType.Boolean&&(bool)referenceToken==(bool)actual["reference"]!,"Spline tension99 published reference flag differs.");
        Near99((JArray)displayed["strokes_mm"]!,(JArray)actual["strokes_mm"]!);
        Require89((int?)displayed["tension"]==(int?)actual["tension"],"Spline99 first snapshot tension differs.");
        bool fit=(string?)actual["native_type"]=="kSketchSplineObject";
        Require89((int?)displayed[fit?"fit_point_count":"control_point_count"]==((JArray)actual["points"]!).Count,"Spline tension99 first product member count differs.");
        if(fit)Require89((int?)displayed["fit_method"]==(int?)actual["fit_method"],"Spline tension99 first product native method differs.");
        foreach(JObject point in (JArray)actual["points"]!)
        {Require89(map.TryGetValue((string)point["key"]!,out var shown)&&(string?)shown["type"]=="point","Spline tension99 actual native member missing from first snapshot.");Near99((JArray)shown!["point_mm"]!,(JArray)point["xy"]!);}
        return referenceExposed;
    }
    private static void RunSplineTensionEdit99()
    {
        Require89(!Done99,"Spline tension99 suite already consumed.");Done99=true;RequireIdle89();Deadline99=DateTimeOffset.UtcNow.AddMinutes(30);
        var client=new PluginClient(new InventorMcpConfig{DescriptorDirectory=Settings.DescriptorDirectory,TargetId=TargetId,EnableExperimental=true,TimeoutMs=30000});
        var report=new JObject{["schema"]=1,["run_id"]=Settings.RunId,["target_id"]=TargetId,["candidate_sha256"]=Candidate99,["status"]="RUNNING",
            ["cases"]=new JArray(),["refusals"]=new JArray(),["refusal_fixture_cleanup"]=new JArray(),["quest_proof"]=false,["core_https_proof"]=false,["full_s1_pass"]=false,["open_s1_gates"]=18};
        WritePublic73("spline-tension-edit99.json",report);
        try
        {
            foreach(var owned in Documents)
            foreach(int method in new[]{26369,26370,26371})
            foreach(bool closed in new[]{false,true})
            foreach(int tension in new[]{0,25,50,75,100})
                Case99(client,owned,method,closed,tension,report);
            foreach(var owned in Documents)Refusals99(client,owned,report);
            Require89(((JArray)report["cases"]!).Count==60&&((JArray)report["refusals"]!).Count==16&&RawCount99==272,"Spline tension99 matrix/raw inventory incomplete.");
            report["status"]=Pass99;
        }
        catch(Exception error)
        {
            report["status"]="FAILED";report["failure"]=Error73(error);
            if(error is InventorGatewayException gateway){report["gateway_code"]=gateway.Code;report["gateway_details"]=gateway.Details;}
            throw;
        }
        finally{report["raw_state_count"]=RawCount99;WritePublic73("spline-tension-edit99.json",report);}
    }
    private static void Case99(PluginClient client,OwnedDocument73 owned,int method,bool closed,int tension,JObject report)
    {
        Budget99();RequireIdle89();owned.Document.Activate();var baseline=Full99();
        const bool control=false;const int count=5;const string role="Interior";
        var row=new JObject{["context"]=owned.Sheet?"Sheet":"Part",["variant"]=control?"spline_control":"spline_fit",["input_count"]=count,["initial_closed"]=closed,["fit_method"]=method,["requested_tension"]=tension,["status"]="RUNNING",["native_before_case_preparation"]=Save99(baseline)};
        ((JArray)report["cases"]!).Add(row);WritePublic73("spline-tension-edit99.json",report);
        PlanarSketch? sketch=null;string sketchKey="";SketchEntity? native=null;
        var prep=Application!.TransactionManager.StartTransaction((_Document)owned.Document,"Spline99 existing native spline fixture");
        try
        {
            Own99(prep);sketch=owned.Document.ComponentDefinition.Sketches.Add(owned.Document.ComponentDefinition.WorkPlanes[3]);sketch.Name="S1_Spline99_Case";
            sketchKey=Key73(sketch);native=Create99(sketch,control,count,closed,method);sketch.Solve();Require89(owned.Document.Update2(false),"Spline99 preparation update failed.");prep.End();
        }
        catch{prep.Abort();throw;}
        try
        {
            var point=Select99(native!,role,out int ordinal);string pointKey=Key73(point);var initial=Curve99(native!);row["native_initial"]=initial.DeepClone();
            Require89((bool?)initial["closed"]==closed,"Spline99 initial native closure differs from the declared fixture case.");
            row["selected_native_member_ordinal"]=ordinal;row["selected_native_point_key"]=pointKey;

            var before=Full99();row["native_before"]=Save99(before);
            var state=State99(client,owned);string revision=(string)state["revision"]!;row["state_before"]=state.DeepClone();
            string sketchId=Id99(owned,sketch!,"sketch");var options=new JObject{["references"]=new JArray(Ref99(owned,sketch!,native!,"spline",revision)),["tension"]=tension};
            row["options"]=options.DeepClone();var operation=Op99(sketchId,"spline_tension_update",options);
            Phase="spline99_preview";var preview=Wire99(client,"atomic_batch",Args99(owned,revision,new JArray(operation.DeepClone()),true));row["preview_result"]=preview.DeepClone();
            var afterPreview=Full99();row["native_after_preview"]=Save99(afterPreview);var previewState=State99(client,owned);row["state_after_preview"]=previewState.DeepClone();
            Require89((string?)preview["status"]=="preview_rolled_back"&&Exact99(before,afterPreview)&&(string?)previewState["revision"]==revision,"Spline99 preview native/event rollback differs.");
            Phase="spline99_commit";var commit=Wire99(client,"atomic_batch",Args99(owned,revision,new JArray(operation.DeepClone()),false));row["commit_result"]=commit.DeepClone();
            Require89((string?)commit["status"]=="committed","Spline99 commit unconfirmed.");
            // Nothing below has read a native curve/point/facet since the commit.
            var committed=State99(client,owned);row["state_after_commit"]=committed.DeepClone();
            var snapshot=Wire99(client,"get_sketch_editor_xr",new JObject{["document_id"]=owned.DocumentId,["expected_revision"]=committed["revision"],["sketch_id"]=sketchId});
            row["first_snapshot"]=snapshot.DeepClone();row["first_snapshot_attempts"]=1;WritePublic73("spline-tension-edit99.json",report);
            var actual=Curve99(native!);row["native_committed"]=actual.DeepClone();
            row["actual_after_closed"]=actual["closed"]!.DeepClone();row["closure_preserved"]=(bool)actual["closed"]! == closed;
            Identity99(initial,actual);
            Require89(Key73(point)==pointKey,"Spline99 selected native point identity changed.");
            Require89((int?)actual["tension"]==tension,"Spline99 native tension readback differs from request.");
            Require89((string?)committed["revision"]!=revision,"Spline99 successful commit did not advance event revision.");
            Require89((string?)snapshot["revision"]==(string?)committed["revision"]&&(string?)snapshot["sketch_id"]==sketchId,"Spline99 first snapshot revision/sketch binding differs.");
            row["first_snapshot_reference_flag_exposed"]=First99(snapshot,owned,actual);
            var oracleBefore=Full99();row["oracle_before_sha256"]=HashText73(oracleBefore.ToString(Formatting.None));
            var tx=Application.TransactionManager.StartTransaction((_Document)owned.Document,"Spline99 independent direct Tension oracle, aborted");
            try
            {
                Own99(tx);var direct=owned.Document.ComponentDefinition.Sketches.Add(owned.Document.ComponentDefinition.WorkPlanes[3]);
                var oracle=Create99(direct,control,count,closed,method);direct.Solve();Require89(owned.Document.Update2(false),"Spline99 oracle initial update failed.");
                var oracleInitial=Curve99(oracle);row["oracle_initial"]=oracleInitial.DeepClone();Compare99(initial,oracleInitial);
                var sourceKeys=((JArray)initial["points"]!).Select(p=>(string)p["key"]!).Append((string)initial["key"]!).ToHashSet(StringComparer.Ordinal);
                Require89(!sourceKeys.Contains((string)oracleInitial["key"]!)&&((JArray)oracleInitial["points"]!).All(p=>!sourceKeys.Contains((string)p["key"]!)),"Spline99 independent oracle shares product curve or point keys.");
                var selected=Select99(oracle,role,out int oracleOrdinal);Require89(oracleOrdinal==ordinal,"Spline99 oracle role/member cardinality differs.");
                Require89(Key73(oracle)!=Key73(native!)&&Key73(selected)!=pointKey,"Spline99 oracle aliases product native identities.");
                ((SketchSpline)oracle).Tension=tension;
                direct.Solve();Require89(owned.Document.Update2(false),"Spline99 oracle mutation update failed.");
                var oracleAfter=Curve99(oracle);row["native_oracle"]=oracleAfter.DeepClone();
                row["oracle_after_closed"]=oracleAfter["closed"]!.DeepClone();
                row["closure_preserved"]=(bool)actual["closed"]! == closed && (bool)oracleAfter["closed"]! == closed;
                Identity99(oracleInitial,oracleAfter);Compare99(actual,oracleAfter);
                Require89((int?)oracleAfter["tension"]==tension,"Spline99 independent native tension differs from request.");
            }
            finally{Own99(tx,false);tx.Abort();}
            var oracleAfterAbort=Full99();row["oracle_after_abort_sha256"]=HashText73(oracleAfterAbort.ToString(Formatting.None));
            Require89(Exact99(oracleBefore,oracleAfterAbort)&&Exact99(actual,Curve99(native!)),"Spline99 oracle Abort changed native full state or spline definitions.");
            row["oracle_abort_full_native_exact"]=true;row["event_revision_preserved_after_preview"]=true;row["native_identity_preserved"]=true;row["status"]=Pass99;
        }
        finally
        {
            if(sketch!=null)
            {
                Remove99(owned,sketch,sketchKey);var after=Full99();row["native_after_case_cleanup"]=Save99(after);
                bool exact=Exact99(Contents99(baseline,owned),Contents99(after,owned));row["case_cleanup_native_contents_exact"]=exact;
                WritePublic73("spline-tension-edit99.json",report);Require89(exact,"Spline99 case cleanup changed unrelated native content.");
            }
        }
    }
    private static void Refusals99(PluginClient client,OwnedDocument73 owned,JObject report)
    {
        Budget99();RequireIdle89();owned.Document.Activate();var baseline=Full99();PlanarSketch sketch;SketchEntity curve,control;
        var prep=Application!.TransactionManager.StartTransaction((_Document)owned.Document,"Spline99 refusal native fixtures");
        try
        {
            Own99(prep);sketch=owned.Document.ComponentDefinition.Sketches.Add(owned.Document.ComponentDefinition.WorkPlanes[3]);sketch.Name="S1_Tension99_Refusals";
            curve=Create99(sketch,false,5,false,26371);control=Create99(sketch,true,5,false,0);
            sketch.Solve();Require89(owned.Document.Update2(false),"Spline99 refusal preparation update failed.");prep.End();
        }
        catch{prep.Abort();throw;}
        string key=Key73(sketch);
        try
        {
            foreach(string kind in new[]{"stale_reference","control_spline","non_whole_curve","foreign_document_reference","negative_tension","decimal_tension","unknown_option","failure_after_valid_update"})
            {
                Budget99();var before=Full99();var state=State99(client,owned);string revision=(string)state["revision"]!;
                var options=new JObject{["references"]=new JArray(Ref99(owned,sketch,curve,"spline",revision)),["tension"]=75};
                var row=new JObject{["context"]=owned.Sheet?"Sheet":"Part",["kind"]=kind,["state_before"]=state.DeepClone(),["native_before"]=Save99(before),["status"]="RUNNING"};
                ((JArray)report["refusals"]!).Add(row);
                if(kind=="stale_reference")options["references"]![0]!["revision"]=revision+"-stale99";
                else if(kind=="control_spline")options["references"]![0]=Ref99(owned,sketch,control,"control_point_spline",revision);
                else if(kind=="non_whole_curve")options["references"]![0]!["subpart"]="Start";
                else if(kind=="foreign_document_reference")
                {
                    var foreign=Documents.Single(d=>d.DocumentId!=owned.DocumentId);
                    var foreignSketch=foreign.Document.ComponentDefinition.Sketches.Cast<PlanarSketch>().First(s=>s.SketchLines.Count>0);
                    string foreignRevision=(string)State99(client,foreign)["revision"]!;
                    var foreignRef=Ref99(foreign,foreignSketch,foreignSketch.SketchLines[1],"spline",foreignRevision);
                    row["original_foreign_reference"]=foreignRef.DeepClone();row["original_foreign_native_type"]=foreignSketch.SketchLines[1].Type.ToString();options["references"]![0]=foreignRef;
                }
                else if(kind=="negative_tension")options["tension"]=-1;
                else if(kind=="decimal_tension")options["tension"]=50.5;
                else if(kind=="unknown_option")options["remove_constraints"]=true;
                string sketchId=Id99(owned,sketch,"sketch");var operations=new JArray(Op99(sketchId,"spline_tension_update",options));
                if(kind=="failure_after_valid_update")operations.Add(Op99(sketchId,"arc_center",new JObject{["points_mm"]=new JArray(new JArray(0,0)),["radius_mm"]=0,["start_deg"]=0,["sweep_deg"]=90}));
                row["operations"]=operations.DeepClone();row["preview"]=false;bool refused=false;
                // Preserve the real foreign revision: the production helper
                // checks freshness before document binding. Do not forge a
                // current revision merely to force a chosen failure code.
                string expectedCode=kind=="stale_reference" || (kind=="foreign_document_reference"
                    &&(string?)options["references"]![0]!["revision"]!=revision)?"STALE_REVISION":"INVALID_ARGUMENT";
                row["expected_step_code"]=expectedCode;
                try{row["unexpected_result"]=Wire99(client,"atomic_batch",Args99(owned,revision,operations,false));}
                catch(InventorGatewayException failure)
                {
                    row["code"]=failure.Code;row["details"]=failure.Details?.DeepClone();
                    Require89(failure.Code=="ROLLED_BACK"&&(string?)failure.Details?["step_code"]==expectedCode
                        &&(int?)failure.Details?["step_index"]==(kind=="failure_after_valid_update"?1:0),"Spline99 refusal structured code/step differs.");refused=true;
                }
                var after=Full99();row["native_after"]=Save99(after);var afterState=State99(client,owned);row["state_after"]=afterState.DeepClone();
                Require89(refused&&Exact99(before,after)&&(string?)afterState["revision"]==revision,"Spline99 refusal changed full native content, spline definitions or revision.");
                row["event_revision_preserved"]=true;row["full_native_rollback_exact"]=true;row["status"]=Pass99;WritePublic73("spline-tension-edit99.json",report);
            }
        }
        finally
        {
            Remove99(owned,sketch,key);var after=Full99();bool exact=Exact99(Contents99(baseline,owned),Contents99(after,owned));
            ((JArray)report["refusal_fixture_cleanup"]!).Add(new JObject{["context"]=owned.Sheet?"Sheet":"Part",["before"]=baseline,["after"]=after,["native_contents_exact"]=exact});
            WritePublic73("spline-tension-edit99.json",report);Require89(exact,"Spline99 refusal cleanup changed unrelated native content.");
        }
    }
}
