using Bimwright.Ipt.Server;
using Bimwright.Ipt.Shared.Contracts;
using Inventor;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

internal static partial class Program
{
    private static bool Done96;
    private static int RawCount96;
    private static DateTimeOffset Deadline96;
    private static bool Exact96(JToken a,JToken b)=>a.ToString(Formatting.None)==b.ToString(Formatting.None);
    private static JObject Contents96(JObject state)
    {
        var result=(JObject)state.DeepClone();
        foreach(var doc in (JArray)result["documents"]!){doc["database_revision_id"]?.Parent?.Remove();doc["dirty"]?.Parent?.Remove();}
        return result;
    }
    private static JObject Save96(JObject state)
    {
        string file="spline-native96-"+(++RawCount96).ToString("D3")+".json";
        WritePublic73(file,new JObject { ["run_id"]=Settings.RunId,["native_state"]=state });
        return new JObject { ["file"]=file,["native_state_sha256"]=HashText73(state.ToString(Formatting.None)) };
    }
    private static void Budget96(){GuardDocuments73();Require89(DateTimeOffset.UtcNow<Deadline96,"Spline96 suite deadline exceeded.");}
    private static JObject Wire96(PluginClient client,string command,JObject args)
    {
        Budget96();Require89(client.CurrentTarget?.TargetId==TargetId,"Spline96 target changed.");
        using var cancel=new CancellationTokenSource(TimeSpan.FromSeconds(45));
        return (JObject)client.SendAsync(command,args,cancel.Token).GetAwaiter().GetResult();
    }
    private static JObject Op96(string sketch,string variant,JObject options)=>new(){["command"]="edit_sketch",["arguments"]=new JObject { ["sketch_id"]=sketch,["variant"]=variant,["options"]=options }};
    private static JObject Args96(OwnedDocument73 owned,string revision,JArray operations,bool preview)=>new(){["document_id"]=owned.DocumentId,["expected_revision"]=revision,["operations"]=operations,["preview"]=preview,["validate"]=new JArray("rebuild","feature_health")};
    private static JObject Revision96(PluginClient client,OwnedDocument73 owned)
    {
        var state=Wire96(client,"get_visual_revision",new JObject { ["document_id"]=owned.DocumentId });
        Require89((string?)state["document_id"]==owned.DocumentId&&!string.IsNullOrEmpty((string?)state["revision"]),"Spline96 document state differs.");return state;
    }
    private static JObject Curve96(SketchEntity entity)
    {
        var points=new JArray();Curve2dEvaluator evaluator;bool closed;var result=new JObject { ["key"]=Key73(entity),["native_type"]=entity.Type.ToString(),["construction"]=entity.Construction,["reference"]=entity.Reference,["constraint_status"]=entity.ConstraintStatus.ToString() };
        if(entity is SketchSpline fit)
        {closed=fit.Closed;result["fit_method"]=(int)fit.FitMethod;result["tension"]=fit.Tension;for(int i=1;i<=fit.FitPointCount;i++){var p=fit.FitPoint[i];points.Add(new JObject { ["key"]=Key73(p),["xy"]=new JArray(p.Geometry.X*10,p.Geometry.Y*10) });}evaluator=fit.Geometry.Evaluator;}
        else {var cv=(SketchControlPointSpline)entity;closed=cv.IsClosed;for(int i=1;i<=cv.ControlPointCount;i++){var p=cv.ControlPoint[i];points.Add(new JObject { ["key"]=Key73(p),["xy"]=new JArray(p.Geometry.X*10,p.Geometry.Y*10) });}evaluator=cv.Geometry.Evaluator;}
        result["closed"]=closed;result["points"]=points;
        double min=0,max=0;evaluator.GetParamExtents(out min,out max);Require89(double.IsFinite(min)&&double.IsFinite(max)&&max>min,"Spline96 invalid evaluator interval.");
        double[] parameters=Enumerable.Range(0,101).Select(i=>min+(max-min)*i/100).ToArray(),samples=Array.Empty<double>();evaluator.GetPointAtParam(ref parameters,ref samples);
        int n=0;double[] strokes=Array.Empty<double>();evaluator.GetStrokes(min,max,.0001,out n,out strokes);
        Require89(samples.Length==202&&samples.All(double.IsFinite)&&n>=2&&n<=8192&&strokes.Length==n*2&&strokes.All(double.IsFinite),"Spline96 evaluator read incomplete.");
        result["domain"]=new JArray(min,max);result["samples_mm"]=new JArray(samples.Select(x=>x*10));result["strokes_mm"]=new JArray(strokes.Select(x=>x*10));return result;
    }
    private static void Near96(JArray a,JArray b,double tolerance=1e-5)
    {Require89(a.Count==b.Count,"Spline96 coordinate count differs.");for(int i=0;i<a.Count;i++)Require89(double.IsFinite((double)a[i])&&double.IsFinite((double)b[i])&&Math.Abs((double)a[i]-(double)b[i])<=tolerance,"Spline96 native oracle geometry differs.");}
    private static void Compare96(JObject actual,JObject oracle)
    {
        foreach(string field in new[]{"native_type","construction","reference","constraint_status","closed","fit_method","tension"})Require89(JToken.DeepEquals(actual[field],oracle[field]),"Spline96 native oracle property differs.");
        Near96((JArray)actual["domain"]!,(JArray)oracle["domain"]!,1e-10);Near96((JArray)actual["samples_mm"]!,(JArray)oracle["samples_mm"]!);Near96((JArray)actual["strokes_mm"]!,(JArray)oracle["strokes_mm"]!);
        var a=(JArray)actual["points"]!;var b=(JArray)oracle["points"]!;Require89(a.Count==b.Count,"Spline96 native fit/control point count differs.");for(int i=0;i<a.Count;i++)Near96((JArray)a[i]["xy"]!,(JArray)b[i]["xy"]!);
    }
    private static void RunSplineClosure96()
    {
        Require89(!Done96,"Spline96 suite already consumed.");Done96=true;RequireIdle89();Deadline96=DateTimeOffset.UtcNow.AddMinutes(10);
        var client=new PluginClient(new InventorMcpConfig { DescriptorDirectory=Settings.DescriptorDirectory,TargetId=TargetId,EnableExperimental=true,TimeoutMs=30000 });
        var report=new JObject { ["schema"]=1,["run_id"]=Settings.RunId,["target_id"]=TargetId,["status"]="RUNNING",["cases"]=new JArray(),["refusals"]=new JArray(),["quest_proof"]=false,["full_s1_pass"]=false };
        WritePublic73("spline-closure96.json",report);
        try
        {
            foreach(var owned in Documents)Refusals96(client,owned,report);
            foreach(var owned in Documents)
            foreach(bool control in new[]{false,true})
            foreach(int count in new[]{2,3,5,64})
            foreach(bool closed in new[]{false,true})
            foreach(int method in control?new[]{0}:new[]{26369,26370,26371})
            {
                Budget96();RequireIdle89();owned.Document.Activate();var baseline=CopyFullNative88();
                var prep=Application!.TransactionManager.StartTransaction((_Document)owned.Document,"Spline96 owned case fixture");PlanarSketch sketch;
                try{sketch=owned.Document.ComponentDefinition.Sketches.Add(owned.Document.ComponentDefinition.WorkPlanes[3]);sketch.Name="S1_Spline96_Case";prep.End();}catch{prep.Abort();throw;}
                try
                {
                    var reference=new PersistentEntityReference { DocumentId=owned.DocumentId,EntityType="sketch",Key=Convert.FromBase64String(Key73(sketch)) }.Encode();
                    var points=new JArray(Enumerable.Range(0,count).Select(i=>new JArray(i*10d,Math.Sin(i*.7)*10)));
                    var options=new JObject { ["points_mm"]=points,["closed"]=closed };if(!control)options["fit_method"]=method;
                    var row=new JObject { ["context"]=owned.Sheet?"Sheet":"Part",["variant"]=control?"spline_control":"spline_fit",["input_count"]=count,["closed"]=closed,["fit_method"]=method,["status"]="RUNNING" };((JArray)report["cases"]!).Add(row);
                    row["native_before_case_preparation"]=Save96(baseline);
                    var before=CopyFullNative88();row["native_before"]=Save96(before);var state=Revision96(client,owned);string revision=(string)state["revision"]!;
                    var operation=Op96(reference,control?"spline_control":"spline_fit",options);
                    Phase="spline96_preview";var preview=Wire96(client,"atomic_batch",Args96(owned,revision,new JArray(operation),true));
                    var afterPreview=CopyFullNative88();row["native_after_preview"]=Save96(afterPreview);Require89((string?)preview["status"]=="preview_rolled_back"&&Exact96(before,afterPreview),"Spline96 preview native rollback differs.");
                    Require89((string?)Revision96(client,owned)["revision"]==revision,"Spline96 preview revision changed.");
                    Phase="spline96_commit";var commit=Wire96(client,"atomic_batch",Args96(owned,revision,new JArray(operation.DeepClone()),false));Require89((string?)commit["status"]=="committed","Spline96 commit not confirmed.");
                    // One first product snapshot before any native geometry/oracle.
                    var committed=Revision96(client,owned);var snapshot=Wire96(client,"get_sketch_editor_xr",new JObject { ["document_id"]=owned.DocumentId,["expected_revision"]=committed["revision"],["sketch_id"]=reference });row["first_snapshot"]=snapshot;row["first_snapshot_attempts"]=1;
                    Require89((bool?)snapshot["partial"]==false&&(string?)snapshot["mutation_completeness"]?["state"]=="complete","Spline96 first product snapshot incomplete.");
                    SketchEntity native=control?(SketchEntity)sketch.SketchControlPointSplines[1]:(SketchEntity)sketch.SketchSplines[1];var actual=Curve96(native);row["native_committed"]=actual;
                    var entity=((JArray)snapshot["entities"]!).Single(x=>(string?)x["type"]==(control?"control_point_spline":"spline"));Require89((bool?)entity["closed"]==closed&&(bool?)actual["closed"]==closed,"Spline96 closure not represented correctly.");
                    Near96((JArray)entity["strokes_mm"]!,(JArray)actual["strokes_mm"]!);
                    var oracleBefore=CopyFullNative88();var tx=Application.TransactionManager.StartTransaction((_Document)owned.Document,"Spline96 independent native oracle, aborted");
                    try
                    {
                        var direct=owned.Document.ComponentDefinition.Sketches.Add(owned.Document.ComponentDefinition.WorkPlanes[3]);var objects=Application.TransientObjects.CreateObjectCollection();
                        foreach(JArray p in points)objects.Add(Application.TransientGeometry.CreatePoint2d((double)p[0]/10,(double)p[1]/10));SketchEntity oracle;
                        if(control){if(closed)objects.Add(Application.TransientGeometry.CreatePoint2d((double)points[0][0]!/10,(double)points[0][1]!/10));oracle=(SketchEntity)direct.SketchControlPointSplines.Add(objects);}
                        else{var fit=direct.SketchSplines.Add(objects,(SplineFitMethodEnum)method);fit.Closed=closed;oracle=(SketchEntity)fit;}
                        direct.Solve();Require89(owned.Document.Update2(false),"Spline96 native oracle update failed.");var read=Curve96(oracle);row["native_oracle"]=read;Compare96(actual,read);
                    }
                    finally{tx.Abort();}
                    Require89(Exact96(actual,Curve96(native))&&Exact96(oracleBefore,CopyFullNative88()),"Spline96 oracle Abort changed the committed product.");
                    row["status"]="PASS_PLUGIN_NATIVE_SPLINE_CLOSURE_SUBSET";WritePublic73("spline-closure96.json",report);
                }
                finally
                {
                    RequireIdle89();var cleanup=Application!.TransactionManager.StartTransaction((_Document)owned.Document,"Spline96 remove owned case sketch");try{sketch.Delete();cleanup.End();}catch{cleanup.Abort();throw;}
                    var after=CopyFullNative88();var rows=(JArray)report["cases"]!;
                    if(rows.Last is JObject current){current["native_after_case_cleanup"]=Save96(after);current["case_cleanup_native_contents_exact"]=Exact96(Contents96(baseline),Contents96(after));}
                    Require89(Exact96(Contents96(baseline),Contents96(after)),"Spline96 case cleanup changed unrelated native content.");WritePublic73("spline-closure96.json",report);
                }
            }
            Require89(((JArray)report["cases"]!).Count==64&&((JArray)report["refusals"]!).Count==14,"Spline96 matrix incomplete.");
            report["status"]="PASS_PLUGIN_NATIVE_SPLINE_CLOSURE_SUBSET";
        }
        catch(Exception error){report["status"]="FAILED";report["failure"]=Error73(error);if(error is InventorGatewayException gateway){report["gateway_code"]=gateway.Code;report["gateway_details"]=gateway.Details;}throw;}
        finally{report["raw_state_count"]=RawCount96;WritePublic73("spline-closure96.json",report);}
    }
    private static void Refusals96(PluginClient client,OwnedDocument73 owned,JObject report)
    {
        owned.Document.Activate();var sketch=owned.Document.ComponentDefinition.Sketches.Cast<PlanarSketch>().Single(s=>s.Name=="S1_Create");
        string reference=new PersistentEntityReference { DocumentId=owned.DocumentId,EntityType="sketch",Key=Convert.FromBase64String(Key73(sketch)) }.Encode();
        JObject Valid()=>new(){["points_mm"]=new JArray(new JArray(0,0),new JArray(10,7),new JArray(20,0)),["closed"]=true,["fit_method"]=26369};
        foreach(string kind in new[]{"closed_string","closed_number","closed_null","bad_fit_method","unknown_tension","bad_coordinate","failure_after_valid_closed"})
        {
            Budget96();var before=CopyFullNative88();var state=Revision96(client,owned);string revision=(string)state["revision"]!;var invalid=Valid();
            if(kind=="closed_string")invalid["closed"]="true";else if(kind=="closed_number")invalid["closed"]=1;else if(kind=="closed_null")invalid["closed"]=JValue.CreateNull();
            else if(kind=="bad_fit_method")invalid["fit_method"]="26369";else if(kind=="unknown_tension")invalid["tension"]=5;else if(kind=="bad_coordinate")invalid["points_mm"]![1]![0]="10";
            var operations=kind=="failure_after_valid_closed"?new JArray(Op96(reference,"spline_fit",Valid()),Op96(reference,"arc_center",new JObject { ["points_mm"]=new JArray(new JArray(0,0)),["radius_mm"]=0,["start_deg"]=0,["sweep_deg"]=90 })):new JArray(Op96(reference,"spline_fit",invalid));
            bool refused=false;JObject? details=null;
            try{Wire96(client,"atomic_batch",Args96(owned,revision,operations,true));}
            catch(InventorGatewayException failure)
            {details=failure.Details;Require89(failure.Code=="ROLLED_BACK"&&(string?)details?["step_code"]=="INVALID_ARGUMENT"&&(int?)details?["step_index"]==(kind=="failure_after_valid_closed"?1:0),"Spline96 refusal code/step differs.");refused=true;}
            var after=CopyFullNative88();Require89(refused&&Exact96(before,after)&&(string?)Revision96(client,owned)["revision"]==revision,"Spline96 refusal changed native content or revision.");
            ((JArray)report["refusals"]!).Add(new JObject { ["context"]=owned.Sheet?"Sheet":"Part",["kind"]=kind,["details"]=details,["native_before"]=Save96(before),["native_after"]=Save96(after),["event_revision_preserved"]=true,["full_native_rollback_exact"]=true });
            WritePublic73("spline-closure96.json",report);
        }
    }
}
