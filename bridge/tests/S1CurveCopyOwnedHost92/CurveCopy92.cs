using Inventor;
using Newtonsoft.Json.Linq;
using File = System.IO.File;
using Path = System.IO.Path;

internal static partial class Program
{
    private static bool CurveDone92;
    private static DateTimeOffset CurveDeadline92;
    private static FileStream? CurveJournal92;
    private static int CurveSequence92;
    private static int CurveStateSequence92;
    private static JObject ArchiveState92(JObject state)
    {
        string name="curve-state92-"+(++CurveStateSequence92).ToString("D3")+".json";
        WritePublic73(name,new JObject { ["run_id"]=Settings.RunId,["native_state"]=state });
        return new JObject { ["file"]=name,["file_sha256"]=HashFile73(Path.Combine(Settings.OutputDirectory,name)) };
    }
    private static void Budget92(){GuardDocuments73();Require89(DateTimeOffset.UtcNow<CurveDeadline92&&DateTimeOffset.UtcNow<ExpiresUtc,"Curve92 bounded deadline exceeded.");}
    private static void Journal92(string stage,string when,Exception? error=null)
    {
        var row=new JObject { ["sequence"]=++CurveSequence92,["utc"]=DateTimeOffset.UtcNow.ToString("O"),["stage"]=stage,["when"]=when,["owned_pid"]=OwnedPid };
        if(error!=null){row["type"]=error.GetType().FullName;row["hresult"]=error.HResult;}
        var bytes=new System.Text.UTF8Encoding(false).GetBytes(row.ToString(Newtonsoft.Json.Formatting.None)+"\n");CurveJournal92!.Write(bytes);CurveJournal92.Flush(true);
    }
    private static T Native92<T>(string stage,Func<T> call){if(stage=="Abort_owned_copy_transaction"||stage=="capture_full_after_abort")GuardDocuments73();else Budget92();Phase=stage;Journal92(stage,"before");try{T value=call();Journal92(stage,"after");return value;}catch(Exception error){Journal92(stage,"APIERROR",error);throw;}}
    private static void Native92(string stage,Action call)=>Native92(stage,()=>{call();return true;});
    private static void RunCurveCopies92()
    {
        Require89(!CurveDone92,"Curve92 research already consumed.");CurveDone92=true;Require89(Bearer==null&&!BootstrapWritten,"Curve92 forbids HTTP/bootstrap.");
        RequireIdle89();CurveDeadline92=DateTimeOffset.UtcNow.AddMinutes(6);CheckInstallation73("installation_before_curve92");
        var report=new JObject { ["schema"]=1,["run_id"]=Settings.RunId,["target_id"]=TargetId,["status"]="RUNNING",["product_pass"]=false,
            ["evidence"]="OBSERVATION_ONLY: aborted native circle/arc translation, then mixed curves+free line; no TextBox enters Copy collection",
            ["cases"]=new JArray(),["copy_observations"]=0,["not_covered"]=new JArray("Copy/source updates", "Persistence", "Core/HTTPS", "Desktop/Quest", "NonXY/3D", "Ellipses/splines", "Constrained/projection/block profiles", "Optional style getters or nonempty attributes marked UNKNOWN") };
        using var journal=new FileStream(Path.Combine(Settings.OutputDirectory,"curve-copy92-journal.jsonl"),FileMode.CreateNew,FileAccess.Write,FileShare.Read,4096,System.IO.FileOptions.WriteThrough);
        CurveJournal92=journal;WritePublic73("curve-copy92.json",report);
        try
        {
            Require89(CurveFixtures92.Count==10,"Curve92 fixture matrix incomplete.");
            foreach(var fixture in CurveFixtures92)
            {
                var owned=Documents.Single(d=>d.Sheet==fixture.Sheet);owned.Document.Activate();RequireIdle89();
                var sketch=owned.Document.ComponentDefinition.Sketches.Cast<PlanarSketch>().Single(s=>Key73(s)==fixture.SketchKey);
                var row=new JObject { ["context"]=fixture.Sheet?"Sheet":"Part",["profile"]=fixture.Label,["constructor_request"]=fixture.Request,
                    ["phases"]=new JArray(),["status"]="RUNNING" };((JArray)report["cases"]!).Add(row);WritePublic73("curve-copy92.json",report);
                JObject before;
                try { before=Native92("capture_full_before_case",FullState92);row["full_native_before"]=ArchiveState92(before); }
                catch(Exception error) { row["status"]=error is UnknownCurve92?"UNKNOWN_BASELINE":"APIERROR_BASELINE";row["failure"]=Error92(Phase,error);row["transaction_started"]=false;WritePublic73("curve-copy92.json",report);throw; }
                Transaction? transaction=null;bool aborted=false;bool exact=false;
                try
                {
                    transaction=Native92("start_owned_copy_transaction",()=>Application!.TransactionManager.StartTransaction((_Document)owned.Document,"S1 Curve92 aborted native copy research"));
                    row["transaction_started"]=true;
                    Require89(!transaction.HasParentTransaction,"Curve92 refuses nested transaction.");
                    foreach(bool mixed in new[]{false,true})
                    {
                        Require89(SameCom73(transaction,Application!.TransactionManager.CurrentTransaction),"Curve92 owned transaction changed before Copy.");
                        var phase=new JObject { ["selection"]=mixed?"primary_companion_freeLine":"primary_only",["delta_mm"]=mixed?new JArray(34,17):new JArray(12,-8) };
                        ((JArray)row["phases"]!).Add(phase);
                        var phaseBefore=Native92("capture_phase_before_copy",FullState92);phase["before"]=ArchiveState92(phaseBefore);
                        var selected=(mixed?new[]{fixture.PrimaryKey,fixture.CompanionKey,fixture.LineKey}:new[]{fixture.PrimaryKey})
                            .Select(key=>sketch.SketchEntities.Cast<SketchEntity>().Single(e=>CurveKey92(e)==key)).ToArray();
                        var objects=Application.TransientObjects.CreateObjectCollection();foreach(var entity in selected) objects.Add(entity);
                        double dx=mixed?34:12,dy=mixed?17:-8;
                        var returned=Native92("MoveSketchObjects.CopyTrue.RemoveFalse."+(mixed?"mixed":"single"),()=>sketch.MoveSketchObjects(objects,Application.TransientGeometry.CreateVector2d(dx/10,dy/10),true,false));
                        phase["returned"]=Native92("enumerate_returned_copy_objects",()=>Returned92(returned));
                        Native92("Solve_after_native_copy",()=>sketch.Solve());
                        Require89(Native92("Update_after_native_copy",()=>owned.Document.Update2(false)),"Curve92 update returned false.");
                        var after=Native92("capture_phase_after_copy",FullState92);phase["after"]=ArchiveState92(after);
                        ValidatePhase92(phaseBefore,after,fixture,selected.Select(CurveKey92).ToArray(),dx,dy,(JArray)phase["returned"]!,phase);
                        phase["status"]="OBSERVED_TRANSLATION_GRAPH_AND_KNOWN_FLAGS";
                        report["copy_observations"]=(int)report["copy_observations"]!+1;WritePublic73("curve-copy92.json",report);
                    }
                    row["status"]="OBSERVED_TWO_NATIVE_COPY_PHASES";
                }
                catch(Exception error)
                {
                    row["status"]=error is UnknownCurve92?"UNKNOWN":error is HostFailure73?"FAILED_ASSERTION":"APIERROR";
                    row["failure"]=Error92(Phase,error);if(error is HostFailure73 or UnknownCurve92) row["failure"]!["assertion"]=error.Message;
                    throw;
                }
                finally
                {
                    if(transaction!=null)
                    {
                        try
                        {
                            Require89(SameCom73(transaction,Application!.TransactionManager.CurrentTransaction),"Curve92 lost transaction before Abort.");
                            Native92("Abort_owned_copy_transaction",()=>transaction.Abort());aborted=true;
                        }
                        catch(Exception error){row["abort_failure"]=Error92("Abort",error);}
                    }
                    try { var restored=Native92("capture_full_after_abort",FullState92);row["full_native_after_abort"]=ArchiveState92(restored);exact=aborted&&JToken.DeepEquals(before,restored); }
                    catch(Exception error){row["rollback_read_failure"]=Error92("capture_full_after_abort",error);}
                    row["owned_abort_returned"]=aborted;row["rollback_full_native_exact"]=exact;WritePublic73("curve-copy92.json",report);
                    Require89(exact,"Curve92 Abort/full native state not exact; stop all further attempts.");
                }
            }
            Require89(((JArray)report["cases"]!).Count==10&&(int)report["copy_observations"]! ==20,"Curve92 observation matrix incomplete.");
            report["status"]="OBSERVATIONS_COMPLETE_SCOPED";
        }
        catch(Exception error){report["status"]="STOPPED_FAILURE_OR_UNKNOWN";report["failure"]=Error92(Phase,error);throw;}
        finally{report["native_state_files"]=CurveStateSequence92;WritePublic73("curve-copy92.json",report);CurveJournal92=null;}
    }
    private static JArray Returned92(ObjectsEnumerator? objects)
    {
        if(objects==null) throw new UnknownCurve92("Native Copy returned no enumerator.");
        Require89(objects.Count is >=0 and <=1024,"Curve92 returned enumerator exceeds budget.");var rows=new JArray();
        foreach(object item in objects)
        {
            if(item is SketchEntity entity) rows.Add(new JObject { ["key"]=CurveKey92(entity),["type"]=entity.Type.ToString(),["category"]="entity" });
            else if(item is GeometricConstraint constraint){var row=ConstraintState92(constraint);row["category"]="constraint";rows.Add(row);}
            else throw new UnknownCurve92("Native returned object has no verified typed curve/point/constraint adapter.");
        }
        return rows;
    }
}
