using Bimwright.Ipt.Server;
using Bimwright.Ipt.Shared.Contracts;
using Inventor;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

internal static partial class Program
{
    private const string Candidate98="D70976D527A541CE0940B0DF13CD318C9775C9D55F548961758D1BFAEC1B2877";
    private const string Pass98="PASS_PLUGIN_NATIVE_SPLINE_POINT_EDIT_SUBSET";
    private static bool Done98;
    private static int RawCount98;
    private static DateTimeOffset Deadline98;
    private static bool Exact98(JToken a,JToken b)=>a.ToString(Formatting.None)==b.ToString(Formatting.None);
    private static void Budget98(){GuardDocuments73();Require89(DateTimeOffset.UtcNow<Deadline98,"Spline point98 thirty-minute suite deadline exceeded.");}
    private static void Own98(Transaction transaction,bool enforceDeadline=true)
    {
        if(enforceDeadline)Budget98();else GuardDocuments73();
        Require89(!transaction.HasParentTransaction&&SameCom73(Application!.TransactionManager.CurrentTransaction,transaction),"Spline point98 current top-level owned transaction differs.");
    }
    private static JObject Wire98(PluginClient client,string command,JObject args)
    {
        Budget98();Require89(client.CurrentTarget?.TargetId==TargetId,"Spline point98 exact target changed.");
        using var cancel=new CancellationTokenSource(TimeSpan.FromSeconds(45));
        return (JObject)client.SendAsync(command,args,cancel.Token).GetAwaiter().GetResult();
    }
    private static JObject State98(PluginClient client,OwnedDocument73 owned)
    {
        var result=Wire98(client,"get_visual_revision",new JObject{["document_id"]=owned.DocumentId});
        Require89((string?)result["document_id"]==owned.DocumentId&&!string.IsNullOrEmpty((string?)result["revision"]),"Spline point98 document/revision unavailable.");return result;
    }
    private static string Id98(OwnedDocument73 owned,object entity,string kind="sketch_entity")
        =>new PersistentEntityReference{DocumentId=owned.DocumentId,EntityType=kind,Key=Convert.FromBase64String(Key73(entity))}.Encode();
    private static JObject Ref98(OwnedDocument73 owned,PlanarSketch sketch,object entity,string type,string revision,string subpart="Entity")
        =>new(){["document_id"]=owned.DocumentId,["revision"]=revision,["sketch_id"]=Id98(owned,sketch,"sketch"),["entity_id"]=Id98(owned,entity),["type"]=type,["subpart"]=subpart};
    private static JObject Op98(string sketch,string variant,JObject options)
        =>new(){["command"]="edit_sketch",["arguments"]=new JObject{["sketch_id"]=sketch,["variant"]=variant,["options"]=options}};
    private static JObject Args98(OwnedDocument73 owned,string revision,JArray operations,bool preview)
        =>new(){["document_id"]=owned.DocumentId,["expected_revision"]=revision,["operations"]=operations,["preview"]=preview,["validate"]=new JArray("rebuild","feature_health")};
    private static JObject Options98(OwnedDocument73 owned,PlanarSketch sketch,SketchEntity curve,SketchPoint point,string revision,JArray destination)
        =>new(){["references"]=new JArray(Ref98(owned,sketch,curve,curve is SketchSpline?"spline":"control_point_spline",revision),Ref98(owned,sketch,point,"point",revision,"Point")),["position_mm"]=destination.DeepClone()};
    private static JObject Full98()
    {
        GuardDocuments73();var before=Documents.ToDictionary(d=>d.InternalName,d=>(d.Document.DatabaseRevisionId,d.Document.Dirty));
        var state=CopyFullNative88();
        state["spline_definitions98"]=new JArray(Documents.Select(d=>new JObject{
            ["document_id"]=d.DocumentId,["sketches"]=new JArray(d.Document.ComponentDefinition.Sketches.Cast<PlanarSketch>().Select(s=>new JObject{
                ["sketch_key"]=Key73(s),["fit"]=new JArray(s.SketchSplines.Cast<SketchSpline>().Select(c=>Curve98((SketchEntity)c))),
                ["control"]=new JArray(s.SketchControlPointSplines.Cast<SketchControlPointSpline>().Select(c=>Curve98((SketchEntity)c))) })) }));
        GuardDocuments73();Require89(Documents.All(d=>d.Document.DatabaseRevisionId==before[d.InternalName].DatabaseRevisionId&&d.Document.Dirty==before[d.InternalName].Dirty),"Spline point98 full read changed Dirty or DatabaseRevisionId.");
        return state;
    }
    private static JObject Contents98(JObject source,OwnedDocument73 target)
    {
        var result=(JObject)source.DeepClone();var doc=((JArray)result["documents"]!).Single(d=>(string?)d["document_id"]==target.DocumentId);
        doc["dirty"]?.Parent?.Remove();doc["database_revision_id"]?.Parent?.Remove();return result;
    }
    private static JObject Save98(JObject state)
    {
        string file="spline-native98-"+(++RawCount98).ToString("D3")+".json";
        WritePublic73(file,new JObject{["schema"]=1,["run_id"]=Settings.RunId,["target_id"]=TargetId,["native_state"]=state});
        return new JObject{["file"]=file,["native_state_sha256"]=HashText73(state.ToString(Formatting.None))};
    }
    private static SketchEntity Create98(PlanarSketch sketch,bool control,int count,bool closed,int method)
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
    private static SketchPoint Select98(SketchEntity curve,string role,out int ordinal)
    {
        int count=curve is SketchSpline fit?fit.FitPointCount:((SketchControlPointSpline)curve).ControlPointCount;
        Require89(count is >=2 and <=2000,"Spline point98 native member count unavailable.");
        ordinal=role switch{"Start"=>1,"Interior"=>(count+1)/2,"End"=>count,_=>throw new HostFailure73("Spline point98 role invalid.")};
        var point=curve is SketchSpline spline?spline.FitPoint[ordinal]:((SketchControlPointSpline)curve).ControlPoint[ordinal];
        Require89(point!=null&&SameCom73(curve.Parent,point.Parent),"Spline point98 actual member has a foreign sketch owner.");return point!;
    }
    private static void Identity98(JObject before,JObject after)
    {
        Require89((string?)before["key"]==(string?)after["key"],"Spline point98 replaced the existing native curve.");
        var a=(JArray)before["points"]!;var b=(JArray)after["points"]!;
        Require89(a.Count==b.Count,"Spline point98 changed native point membership cardinality.");
        for(int i=0;i<a.Count;i++)Require89((string?)a[i]["key"]==(string?)b[i]["key"],"Spline point98 replaced or reordered an existing native member.");
    }
    private static void Remove98(OwnedDocument73 owned,PlanarSketch sketch,string key)
    {
        RequireIdle89();Require89(SameCom73(sketch.Parent,owned.Document.ComponentDefinition)&&Key73(sketch)==key
            &&owned.Document.ComponentDefinition.Sketches.Cast<PlanarSketch>().Count(s=>SameCom73(s,sketch)&&Key73(s)==key)==1,"Spline point98 cleanup exact owned sketch proof differs.");
        var transaction=Application!.TransactionManager.StartTransaction((_Document)owned.Document,"Spline98 remove exact owned case sketch");
        try{Own98(transaction,false);sketch.Delete();transaction.End();}catch{transaction.Abort();throw;}
    }
    private static bool First98(JObject snapshot,OwnedDocument73 owned,JObject actual)
    {
        Require89((string?)snapshot["document_id"]==owned.DocumentId&&(bool?)snapshot["partial"]==false
            &&(string?)snapshot["mutation_completeness"]?["state"]=="complete"&&(int?)snapshot["mutation_completeness"]?["read_error_count"]==0,"Spline point98 first product snapshot incomplete.");
        var entities=(JArray)snapshot["entities"]!;
        foreach(string collection in new[]{"entities","constraints","dimensions"})
            Require89((int?)snapshot["mutation_completeness"]?["collections"]?[collection]?["native_count"]==((JArray)snapshot[collection]!).Count
                &&(int?)snapshot["mutation_completeness"]?["collections"]?[collection]?["represented_count"]==((JArray)snapshot[collection]!).Count,"Spline point98 first snapshot collection coverage differs.");
        var map=new Dictionary<string,JObject>(StringComparer.Ordinal);
        foreach(JObject entity in entities)
        {var reference=PersistentEntityReference.Decode((string)entity["id"]!);Require89(reference.DocumentId==owned.DocumentId&&reference.EntityType=="sketch_entity"&&reference.Context.Length==0&&map.TryAdd(Convert.ToBase64String(reference.Key),entity),"Spline point98 first snapshot native identity ambiguous.");}
        Require89(map.TryGetValue((string)actual["key"]!,out var curve),"Spline point98 original curve absent from first snapshot.");
        var displayed=curve!;
        Require89((string?)displayed["native_type"]==(string?)actual["native_type"]&&(bool?)displayed["supported"]==true
            &&(bool?)displayed["closed"]==(bool?)actual["closed"]&&(bool?)displayed["construction"]==(bool?)actual["construction"],"Spline point98 first product curve flags/closure differ.");
        // The current reader does not publish Reference. An absent field is
        // not a false flag; native actual/oracle comparisons still check it.
        bool referenceExposed=displayed.TryGetValue("reference",out var referenceToken);
        if(referenceExposed)Require89(referenceToken!.Type==JTokenType.Boolean&&(bool)referenceToken==(bool)actual["reference"]!,"Spline point98 published reference flag differs.");
        Near98((JArray)displayed["strokes_mm"]!,(JArray)actual["strokes_mm"]!);
        bool fit=(string?)actual["native_type"]=="kSketchSplineObject";
        Require89((int?)displayed[fit?"fit_point_count":"control_point_count"]==((JArray)actual["points"]!).Count,"Spline point98 first product member count differs.");
        if(fit)Require89((int?)displayed["fit_method"]==(int?)actual["fit_method"],"Spline point98 first product native method differs.");
        foreach(JObject point in (JArray)actual["points"]!)
        {Require89(map.TryGetValue((string)point["key"]!,out var shown)&&(string?)shown["type"]=="point","Spline point98 actual native member missing from first snapshot.");Near98((JArray)shown!["point_mm"]!,(JArray)point["xy"]!);}
        return referenceExposed;
    }
    private static void RunSplinePointEdit98()
    {
        Require89(!Done98,"Spline point98 suite already consumed.");Done98=true;RequireIdle89();Deadline98=DateTimeOffset.UtcNow.AddMinutes(30);
        var client=new PluginClient(new InventorMcpConfig{DescriptorDirectory=Settings.DescriptorDirectory,TargetId=TargetId,EnableExperimental=true,TimeoutMs=30000});
        var report=new JObject{["schema"]=1,["run_id"]=Settings.RunId,["target_id"]=TargetId,["candidate_sha256"]=Candidate98,["status"]="RUNNING",
            ["cases"]=new JArray(),["refusals"]=new JArray(),["refusal_fixture_cleanup"]=new JArray(),["quest_proof"]=false,["core_https_proof"]=false,["full_s1_pass"]=false,["open_s1_gates"]=18};
        WritePublic73("spline-point-edit98.json",report);
        try
        {
            foreach(var owned in Documents)
            foreach(bool control in new[]{false,true})
            foreach(int method in control?new[]{0}:new[]{26369,26370,26371})
            foreach(bool closed in new[]{false,true})
            foreach(int count in new[]{2,5,64})
            foreach(string role in count==2?new[]{"Start","End"}:new[]{"Start","Interior","End"})
                Case98(client,owned,control,method,closed,count,role,report);
            foreach(var owned in Documents)Refusals98(client,owned,report);
            Require89(((JArray)report["cases"]!).Count==128&&((JArray)report["refusals"]!).Count==14&&RawCount98==540,"Spline point98 matrix/raw inventory incomplete.");
            report["status"]=Pass98;
        }
        catch(Exception error)
        {
            report["status"]="FAILED";report["failure"]=Error73(error);
            if(error is InventorGatewayException gateway){report["gateway_code"]=gateway.Code;report["gateway_details"]=gateway.Details;}
            throw;
        }
        finally{report["raw_state_count"]=RawCount98;WritePublic73("spline-point-edit98.json",report);}
    }
    private static void Case98(PluginClient client,OwnedDocument73 owned,bool control,int method,bool closed,int count,string role,JObject report)
    {
        Budget98();RequireIdle89();owned.Document.Activate();var baseline=Full98();
        var row=new JObject{["context"]=owned.Sheet?"Sheet":"Part",["variant"]=control?"spline_control":"spline_fit",["input_count"]=count,["initial_closed"]=closed,["fit_method"]=method,["role"]=role,["status"]="RUNNING",["native_before_case_preparation"]=Save98(baseline)};
        ((JArray)report["cases"]!).Add(row);WritePublic73("spline-point-edit98.json",report);
        PlanarSketch? sketch=null;string sketchKey="";SketchEntity? native=null;
        var prep=Application!.TransactionManager.StartTransaction((_Document)owned.Document,"Spline98 existing native spline fixture");
        try
        {
            Own98(prep);sketch=owned.Document.ComponentDefinition.Sketches.Add(owned.Document.ComponentDefinition.WorkPlanes[3]);sketch.Name="S1_Spline98_Case";
            sketchKey=Key73(sketch);native=Create98(sketch,control,count,closed,method);sketch.Solve();Require89(owned.Document.Update2(false),"Spline98 preparation update failed.");prep.End();
        }
        catch{prep.Abort();throw;}
        try
        {
            var point=Select98(native!,role,out int ordinal);string pointKey=Key73(point);var initial=Curve98(native!);row["native_initial"]=initial.DeepClone();
            Require89((bool?)initial["closed"]==closed,"Spline98 initial native closure differs from the declared fixture case.");
            row["selected_native_member_ordinal"]=ordinal;row["selected_native_point_key"]=pointKey;
            var position=point.Geometry;var target=new JArray(position.X*10+3,position.Y*10-2);row["position_mm"]=target.DeepClone();
            var before=Full98();row["native_before"]=Save98(before);
            var state=State98(client,owned);string revision=(string)state["revision"]!;row["state_before"]=state.DeepClone();
            string sketchId=Id98(owned,sketch!,"sketch");var options=Options98(owned,sketch!,native!,point,revision,target);
            row["options"]=options.DeepClone();var operation=Op98(sketchId,"spline_point_update",options);
            Phase="spline98_preview";var preview=Wire98(client,"atomic_batch",Args98(owned,revision,new JArray(operation.DeepClone()),true));row["preview_result"]=preview.DeepClone();
            var afterPreview=Full98();row["native_after_preview"]=Save98(afterPreview);var previewState=State98(client,owned);row["state_after_preview"]=previewState.DeepClone();
            Require89((string?)preview["status"]=="preview_rolled_back"&&Exact98(before,afterPreview)&&(string?)previewState["revision"]==revision,"Spline98 preview native/event rollback differs.");
            Phase="spline98_commit";var commit=Wire98(client,"atomic_batch",Args98(owned,revision,new JArray(operation.DeepClone()),false));row["commit_result"]=commit.DeepClone();
            Require89((string?)commit["status"]=="committed","Spline98 commit unconfirmed.");
            // Nothing below has read a native curve/point/facet since the commit.
            var committed=State98(client,owned);row["state_after_commit"]=committed.DeepClone();
            var snapshot=Wire98(client,"get_sketch_editor_xr",new JObject{["document_id"]=owned.DocumentId,["expected_revision"]=committed["revision"],["sketch_id"]=sketchId});
            row["first_snapshot"]=snapshot.DeepClone();row["first_snapshot_attempts"]=1;WritePublic73("spline-point-edit98.json",report);
            var actual=Curve98(native!);row["native_committed"]=actual.DeepClone();
            row["actual_after_closed"]=actual["closed"]!.DeepClone();row["closure_preserved"]=(bool)actual["closed"]! == closed;
            Identity98(initial,actual);
            Require89(Key73(point)==pointKey,"Spline98 selected native point identity changed.");
            var actualSelected=new JArray(point.Geometry.X*10,point.Geometry.Y*10);
            row["actual_selected_position_mm"]=actualSelected.DeepClone();
            row["actual_request_residual_mm"]=new JArray((double)actualSelected[0]-(double)target[0],(double)actualSelected[1]-(double)target[1]);
            Require89((string?)committed["revision"]!=revision,"Spline98 successful commit did not advance event revision.");
            Require89((string?)snapshot["revision"]==(string?)committed["revision"]&&(string?)snapshot["sketch_id"]==sketchId,"Spline98 first snapshot revision/sketch binding differs.");
            row["first_snapshot_reference_flag_exposed"]=First98(snapshot,owned,actual);
            var oracleBefore=Full98();row["oracle_before_sha256"]=HashText73(oracleBefore.ToString(Formatting.None));
            var tx=Application.TransactionManager.StartTransaction((_Document)owned.Document,"Spline98 independent direct MoveTo oracle, aborted");
            try
            {
                Own98(tx);var direct=owned.Document.ComponentDefinition.Sketches.Add(owned.Document.ComponentDefinition.WorkPlanes[3]);
                var oracle=Create98(direct,control,count,closed,method);direct.Solve();Require89(owned.Document.Update2(false),"Spline98 oracle initial update failed.");
                var oracleInitial=Curve98(oracle);row["oracle_initial"]=oracleInitial.DeepClone();Compare98(initial,oracleInitial);
                var sourceKeys=((JArray)initial["points"]!).Select(p=>(string)p["key"]!).Append((string)initial["key"]!).ToHashSet(StringComparer.Ordinal);
                Require89(!sourceKeys.Contains((string)oracleInitial["key"]!)&&((JArray)oracleInitial["points"]!).All(p=>!sourceKeys.Contains((string)p["key"]!)),"Spline98 independent oracle shares product curve or point keys.");
                var selected=Select98(oracle,role,out int oracleOrdinal);Require89(oracleOrdinal==ordinal,"Spline98 oracle role/member cardinality differs.");
                Require89(Key73(oracle)!=Key73(native!)&&Key73(selected)!=pointKey,"Spline98 oracle aliases product native identities.");
                selected.MoveTo(Application.TransientGeometry.CreatePoint2d((double)target[0]/10,(double)target[1]/10));
                direct.Solve();Require89(owned.Document.Update2(false),"Spline98 oracle mutation update failed.");
                var oracleAfter=Curve98(oracle);row["native_oracle"]=oracleAfter.DeepClone();
                row["oracle_after_closed"]=oracleAfter["closed"]!.DeepClone();
                row["closure_preserved"]=(bool)actual["closed"]! == closed && (bool)oracleAfter["closed"]! == closed;
                Identity98(oracleInitial,oracleAfter);Compare98(actual,oracleAfter);
                // MoveTo on an approximating fit spline can solve to a point
                // slightly different from the request. Compare the actual
                // plugin result to the independently solved native operation;
                // retain both request residuals, never claim exact placement.
                var oracleSelected=new JArray(selected.Geometry.X*10,selected.Geometry.Y*10);
                row["oracle_selected_position_mm"]=oracleSelected.DeepClone();
                row["oracle_request_residual_mm"]=new JArray((double)oracleSelected[0]-(double)target[0],(double)oracleSelected[1]-(double)target[1]);
                Near98(actualSelected,oracleSelected);
            }
            finally{Own98(tx,false);tx.Abort();}
            var oracleAfterAbort=Full98();row["oracle_after_abort_sha256"]=HashText73(oracleAfterAbort.ToString(Formatting.None));
            Require89(Exact98(oracleBefore,oracleAfterAbort)&&Exact98(actual,Curve98(native!)),"Spline98 oracle Abort changed native full state or spline definitions.");
            row["oracle_abort_full_native_exact"]=true;row["event_revision_preserved_after_preview"]=true;row["native_identity_preserved"]=true;row["status"]=Pass98;
        }
        finally
        {
            if(sketch!=null)
            {
                Remove98(owned,sketch,sketchKey);var after=Full98();row["native_after_case_cleanup"]=Save98(after);
                bool exact=Exact98(Contents98(baseline,owned),Contents98(after,owned));row["case_cleanup_native_contents_exact"]=exact;
                WritePublic73("spline-point-edit98.json",report);Require89(exact,"Spline98 case cleanup changed unrelated native content.");
            }
        }
    }
    private static void Refusals98(PluginClient client,OwnedDocument73 owned,JObject report)
    {
        Budget98();RequireIdle89();owned.Document.Activate();var baseline=Full98();PlanarSketch sketch;SketchEntity curve,other;SketchLine line;
        var prep=Application!.TransactionManager.StartTransaction((_Document)owned.Document,"Spline98 refusal existing native fixtures");
        try
        {
            Own98(prep);sketch=owned.Document.ComponentDefinition.Sketches.Add(owned.Document.ComponentDefinition.WorkPlanes[3]);sketch.Name="S1_Spline98_Refusals";
            curve=Create98(sketch,false,5,false,26371);other=Create98(sketch,false,2,false,26371);
            line=sketch.SketchLines.AddByTwoPoints(Application.TransientGeometry.CreatePoint2d(0,8),Application.TransientGeometry.CreatePoint2d(2,8));
            sketch.Solve();Require89(owned.Document.Update2(false),"Spline98 refusal preparation update failed.");prep.End();
        }
        catch{prep.Abort();throw;}
        string key=Key73(sketch);
        try
        {
            foreach(string kind in new[]{"different_spline_same_sketch","stale_reference","foreign_document_point","wrong_point_type","malformed_position","swapped_references","failure_after_valid_update"})
            {
                Budget98();var before=Full98();var state=State98(client,owned);string revision=(string)state["revision"]!;
                var point=Select98(curve,"Interior",out _);var xy=point.Geometry;var target=new JArray(xy.X*10+3,xy.Y*10-2);
                var options=Options98(owned,sketch,curve,point,revision,target);var row=new JObject{["context"]=owned.Sheet?"Sheet":"Part",["kind"]=kind,["state_before"]=state.DeepClone(),["native_before"]=Save98(before),["status"]="RUNNING"};
                ((JArray)report["refusals"]!).Add(row);
                if(kind=="different_spline_same_sketch")
                {
                    var unrelated=Select98(other,"Start",out _);var initial=Curve98(curve);
                    Require89(((JArray)initial["points"]!).All(p=>(string?)p["key"]!=Key73(unrelated))
                        &&Enumerable.Range(1,((SketchSpline)curve).FitPointCount).All(i=>!SameCom73(((SketchSpline)curve).FitPoint[i],unrelated)),"Spline98 ownership refusal fixture unexpectedly shares a real member.");
                    options["references"]![1]=Ref98(owned,sketch,unrelated,"point",revision,"Point");
                }
                else if(kind=="stale_reference")options["references"]![1]!["revision"]=revision+"-stale98";
                else if(kind=="foreign_document_point")
                {
                    var foreign=Documents.Single(d=>d.DocumentId!=owned.DocumentId);
                    var foreignSketch=foreign.Document.ComponentDefinition.Sketches.Cast<PlanarSketch>().Single(s=>s.Name=="S1_Copy88");
                    var foreignPoint=foreignSketch.SketchLines[1].StartSketchPoint;string foreignRevision=(string)State98(client,foreign)["revision"]!;
                    var foreignRef=Ref98(foreign,foreignSketch,foreignPoint,"point",foreignRevision,"Point");row["original_foreign_reference"]=foreignRef.DeepClone();options["references"]![1]=foreignRef;
                }
                else if(kind=="wrong_point_type")options["references"]![1]=Ref98(owned,sketch,line,"line",revision);
                else if(kind=="malformed_position")options["position_mm"]![0]="12";
                else if(kind=="swapped_references")options["references"]=new JArray(options["references"]![1]!.DeepClone(),options["references"]![0]!.DeepClone());
                string sketchId=Id98(owned,sketch,"sketch");var operations=new JArray(Op98(sketchId,"spline_point_update",options));
                if(kind=="failure_after_valid_update")operations.Add(Op98(sketchId,"arc_center",new JObject{["points_mm"]=new JArray(new JArray(0,0)),["radius_mm"]=0,["start_deg"]=0,["sweep_deg"]=90}));
                row["operations"]=operations.DeepClone();row["preview"]=false;bool refused=false;
                try{row["unexpected_result"]=Wire98(client,"atomic_batch",Args98(owned,revision,operations,false));}
                catch(InventorGatewayException failure)
                {
                    row["code"]=failure.Code;row["details"]=failure.Details?.DeepClone();
                    Require89(failure.Code=="ROLLED_BACK"&&(string?)failure.Details?["step_code"]==(kind=="stale_reference"?"STALE_REVISION":"INVALID_ARGUMENT")
                        &&(int?)failure.Details?["step_index"]==(kind=="failure_after_valid_update"?1:0),"Spline98 refusal structured code/step differs.");refused=true;
                }
                var after=Full98();row["native_after"]=Save98(after);var afterState=State98(client,owned);row["state_after"]=afterState.DeepClone();
                Require89(refused&&Exact98(before,after)&&(string?)afterState["revision"]==revision,"Spline98 refusal changed full native content, spline definitions or event revision.");
                row["event_revision_preserved"]=true;row["full_native_rollback_exact"]=true;row["status"]=Pass98;WritePublic73("spline-point-edit98.json",report);
            }
        }
        finally
        {
            Remove98(owned,sketch,key);var after=Full98();bool exact=Exact98(Contents98(baseline,owned),Contents98(after,owned));
            ((JArray)report["refusal_fixture_cleanup"]!).Add(new JObject{["context"]=owned.Sheet?"Sheet":"Part",["before"]=baseline,["after"]=after,["native_contents_exact"]=exact});
            WritePublic73("spline-point-edit98.json",report);Require89(exact,"Spline98 refusal cleanup changed unrelated native content.");
        }
    }
}
