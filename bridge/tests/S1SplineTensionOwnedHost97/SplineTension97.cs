using System.Runtime.InteropServices;
using Inventor;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

internal static partial class Program
{
    // RESEARCH only. Five declared input coordinates, not five grounded native
    // fit points. Methods69/70 can approximate them; never assert input==output.
    private static readonly double[][] Input97 = { new[]{0d,0d},new[]{12d,7d},new[]{25d,-4d},new[]{39d,11d},new[]{52d,2d} };
    private static bool Done97;
    private static DateTimeOffset Deadline97;
    private const string Candidate97="2AF7E77A8A34EA82052AD84445398CDFA6858A2E9BC2AF13A6B577B6A13D15E0";
    private static void Require97(bool condition,string assertion)
    {if(!condition)throw new HostFailure73(assertion);}
    private static void Idle97()
    {
        GuardDocuments73();
        Require97(Application!.CommandManager.ActiveCommand=="AppSelectNorthwestArrowCmd","A native command prevents tension97 research.");
        var current=Application.TransactionManager.CurrentTransaction;
        if(current==null)return;
        bool identified=true;
        try{_=current.HasParentTransaction;}
        catch(COMException error)when(error.ErrorCode==unchecked((int)0x80004005)){identified=false;}
        Require97(!identified,"An identified transaction prevents tension97 research.");
    }
    private static void Budget97()
    {GuardDocuments73();Require97(DateTimeOffset.UtcNow<Deadline97,"Tension97 fifteen-minute research deadline exceeded.");}
    private static void Own97(Transaction transaction,bool enforceDeadline=true)
    {
        if(enforceDeadline)Budget97();else GuardDocuments73();
        Require97(!transaction.HasParentTransaction&&SameCom73(Application!.TransactionManager.CurrentTransaction,transaction),
            "Tension97 requires its own top-level current transaction.");
    }
    private static JObject Curve97(SketchSpline fit)
    {
        int count=fit.FitPointCount;
        Require97(count is >=2 and <=64,"Native tension97 fit point budget differs.");
        var points=new JArray();
        for(int i=1;i<=count;i++)
        {
            var point=fit.FitPoint[i];var p=point.Geometry;
            Require97(double.IsFinite(p.X*10)&&double.IsFinite(p.Y*10),"Native fit point is nonfinite.");
            points.Add(new JObject { ["index"]=i,["key"]=Key73(point),["xy_mm"]=new JArray(p.X*10,p.Y*10) });
        }
        var evaluator=fit.Geometry.Evaluator;
        double min=0,max=0;evaluator.GetParamExtents(out min,out max);
        Require97(double.IsFinite(min)&&double.IsFinite(max)&&max>min,"Tension97 evaluator domain invalid.");
        double[] parameters=Enumerable.Range(0,101).Select(i=>min+(max-min)*i/100).ToArray(),samples=Array.Empty<double>();
        evaluator.GetPointAtParam(ref parameters,ref samples);
        int strokesCount=0;double[] strokes=Array.Empty<double>();
        evaluator.GetStrokes(min,max,.0001,out strokesCount,out strokes);
        Require97(samples.Length==202&&samples.All(double.IsFinite)&&strokesCount is >=2 and <=8192
            &&strokes.Length==strokesCount*2&&strokes.All(double.IsFinite),"Tension97 evaluator/strokes read incomplete.");
        return new JObject {
            ["key"]=Key73(fit),["native_type"]=fit.Type.ToString(),["fit_method"]=(int)fit.FitMethod,
            ["fit_method_name"]=fit.FitMethod.ToString(),["tension"]=fit.Tension,["closed"]=fit.Closed,
            ["construction"]=fit.Construction,["reference"]=fit.Reference,["constraint_status"]=fit.ConstraintStatus.ToString(),
            ["fit_points"]=points,["domain"]=new JArray(min,max),["samples_mm"]=new JArray(samples.Select(x=>x*10)),
            ["stroke_tolerance_mm"]=.001,["strokes_mm"]=new JArray(strokes.Select(x=>x*10)) };
    }
    private static JObject Full97()
    {
        GuardDocuments73();
        var before=Documents.ToDictionary(d=>d.InternalName,d=>(d.Document.DatabaseRevisionId,d.Document.Dirty));
        var full=CopyFullNative88();
        full["spline_definitions97"]=new JArray(Documents.Select(d=>new JObject {
            ["document_id"]=d.DocumentId,["sketches"]=new JArray(d.Document.ComponentDefinition.Sketches.Cast<PlanarSketch>().Select(s=>new JObject {
                ["sketch_key"]=Key73(s),["spline_count"]=s.SketchSplines.Count,
                ["splines"]=new JArray(s.SketchSplines.Cast<SketchSpline>().Select(Curve97)) })) }));
        GuardDocuments73();
        Require97(Documents.All(d=>d.Document.DatabaseRevisionId==before[d.InternalName].DatabaseRevisionId
            &&d.Document.Dirty==before[d.InternalName].Dirty),"Tension97 read changed native Dirty/DatabaseRevisionId.");
        return full;
    }
    private static string Compact97(JToken value)=>value.ToString(Formatting.None);
    private static JObject Error97(Exception error,string stage)=>new JObject {
        ["stage"]=stage,["type"]=error.GetType().FullName,["hresult"]=error.HResult,
        ["hresult_hex"]="0x"+unchecked((uint)error.HResult).ToString("X8") };
    private static void RunSplineTension97()
    {
        Require97(!Done97,"Tension97 suite already consumed.");Done97=true;
        Require97(Settings.ExpectedAddinSha==Candidate97,"Tension97 requires frozen candidate96.");
        Idle97();Deadline97=DateTimeOffset.UtcNow.AddMinutes(15);
        var report=new JObject { ["schema"]=1,["run_id"]=Settings.RunId,["target_id"]=TargetId,["owned_pid"]=OwnedPid,
            ["candidate_sha256"]=Candidate97,["status"]="RUNNING",["evidence"]="NATIVE_API_RESEARCH_NOT_PRODUCT_PARITY",
            ["cases"]=new JArray(),["expected_cases"]=60,["input_points_mm"]=new JArray(Input97.Select(p=>new JArray(p))),
            ["input_points_grounded"]=false,["writes_global_sketch_options"]=false,["http_started"]=false,["bootstrap_exposed"]=false,
            ["product_pass"]=false,["full_s1_pass"]=false,["quest_proof"]=false,["physical_proof"]=false,
            ["not_covered"]=new JArray("Product wire/preview/commit", "Desktop equivalence", "Point/handle editing", "Persistence/dependencies", "Quest") };
        WritePublic73("spline-tension97.json",report);
        try
        {
            int number=0;
            foreach(var owned in Documents)
            foreach(int method in new[]{26369,26370,26371})
            foreach(bool closed in new[]{false,true})
            foreach(int tension in new[]{0,25,50,75,100})
            {
                Budget97();Idle97();owned.Document.Activate();
                var before=Full97();string beforeJson=Compact97(before);
                string name="spline-tension97-case-"+(++number).ToString("D3")+".json";
                var sidecar=new JObject { ["schema"]=1,["run_id"]=Settings.RunId,["case_number"]=number,["native_before"]=before,
                    ["native_before_sha256"]=HashText73(beforeJson) };
                WritePublic73(name,sidecar);
                var row=new JObject { ["number"]=number,["context"]=owned.Sheet?"Sheet":"Part",["document_id"]=owned.DocumentId,
                    ["requested_fit_method"]=method,["requested_closed"]=closed,["requested_tension"]=tension,
                    ["status"]="RUNNING",["setter_returned"]=false,["solve_calls"]=0,["update_calls"]=0,["sidecar"]=name };
                ((JArray)report["cases"]!).Add(row);WritePublic73("spline-tension97.json",report);
                Transaction? transaction=null;SketchSpline? fit=null;string stage="start_transaction";
                Exception? fatal=null;
                try
                {
                    Phase="tension97_start_transaction";
                    transaction=Application!.TransactionManager.StartTransaction((_Document)owned.Document,"Tension97 owned research, always aborted");Own97(transaction);
                    stage="add_sketch";Phase="tension97_"+stage;
                    var sketch=owned.Document.ComponentDefinition.Sketches.Add(owned.Document.ComponentDefinition.WorkPlanes[3]);
                    sketch.Name="S1_Tension97_"+number.ToString("D3");
                    var objects=Application.TransientObjects.CreateObjectCollection();
                    foreach(var p in Input97)objects.Add(Application.TransientGeometry.CreatePoint2d(p[0]/10,p[1]/10));
                    stage="add_spline";Phase="tension97_"+stage;Own97(transaction);
                    fit=sketch.SketchSplines.Add(objects,(SplineFitMethodEnum)method);
                    stage="capture_after_add";row["after_add"]=Curve97(fit);
                    stage="set_closed";Phase="tension97_"+stage;Own97(transaction);fit.Closed=closed;
                    stage="capture_before_tension";row["before_tension"]=Curve97(fit);
                    stage="set_tension";Phase="tension97_"+stage;WritePublic73("spline-tension97.json",report);Own97(transaction);
                    // Interop31 verified Int32 (VBA/API Long), UI-valid values only.
                    fit.Tension=tension;row["setter_returned"]=true;
                    stage="capture_after_tension";row["after_tension"]=Curve97(fit);
                    stage="solve_once";Phase="tension97_"+stage;Own97(transaction);row["solve_calls"]=1;sketch.Solve();
                    stage="update_once";Phase="tension97_"+stage;Own97(transaction);row["update_calls"]=1;
                    row["update_returned"]=owned.Document.Update2(false);
                    stage="capture_after_update";row["after_update"]=Curve97(fit);
                    row["fit_method_changed_immediately"]=(int)row["before_tension"]!["fit_method"]! != (int)row["after_tension"]!["fit_method"]!;
                    row["tension_readback_matches_request"]=(int)row["after_update"]!["tension"]! == tension;
                    sidecar["native_after_attempt"]=Full97();
                    row["outcome"]=(bool)row["update_returned"]! ? "API_COMPLETED_OBSERVATION" : "NATIVE_UPDATE_RETURNED_FALSE";
                }
                catch(Exception error)
                {
                    row["outcome"]=error is COMException ? "NATIVE_API_ERROR_OBSERVATION" : "READBACK_OR_GUARD_FAILURE";
                    row["failure"]=Error97(error,stage);
                    if(error is not COMException)fatal=error;
                    if(fit!=null)
                    {
                        try{row["after_error_readback"]=Curve97(fit);}
                        catch(Exception readError){row["after_error_readback_failure"]=Error97(readError,"capture_after_error");}
                    }
                }
                finally
                {
                    Phase="tension97_abort";
                    if(transaction!=null)
                    {
                        try {Own97(transaction,false);transaction.Abort();row["abort_returned"]=true;}
                        catch(Exception abortError){row["abort_returned"]=false;row["abort_failure"]=Error97(abortError,"abort");fatal=abortError;}
                    }
                    try
                    {
                        // No Solve/Update/Rebuild, repair, normalization or relaxed Dirty comparison here.
                        var after=Full97();string afterJson=Compact97(after);
                        sidecar["native_after_abort"]=after;sidecar["native_after_abort_sha256"]=HashText73(afterJson);
                        bool exact=beforeJson==afterJson;
                        row["full_native_abort_exact"]=transaction!=null&&(bool?)row["abort_returned"]==true&&exact;
                        if((bool?)row["full_native_abort_exact"]!=true)fatal??=new HostFailure73("Tension97 full native Abort differs; stop further cases.");
                    }
                    catch(Exception afterError){row["after_abort_failure"]=Error97(afterError,"after_abort_readback");fatal=afterError;}
                    row["status"]=fatal==null?"OBSERVATION_ONLY":"FAILED";
                    sidecar["result"]=row.DeepClone();WritePublic73(name,sidecar);
                    row["sidecar_file_sha256"]=HashFile73(System.IO.Path.Combine(Settings.OutputDirectory,name));
                    WritePublic73("spline-tension97.json",report);
                }
                if(fatal!=null)throw new HostFailure73("Tension97 native guard/readback/Abort failed; observations preserved, no subsequent case.");
            }
            Require97(((JArray)report["cases"]!).Count==60,"Tension97 research matrix incomplete.");
            report["status"]="OBSERVATION_ONLY";
            report["completed_cases"]=60;
            report["exact_aborts"]=((JArray)report["cases"]!).Count(r=>(bool?)r["full_native_abort_exact"]==true);
            report["setter_returned_cases"]=((JArray)report["cases"]!).Count(r=>(bool?)r["setter_returned"]==true);
            report["api_error_observations"]=((JArray)report["cases"]!).Count(r=>(string?)r["outcome"]=="NATIVE_API_ERROR_OBSERVATION");
        }
        catch(Exception error){report["status"]="FAILED";report["failure"]=Error73(error);throw;}
        finally{WritePublic73("spline-tension97.json",report);}
    }
}
