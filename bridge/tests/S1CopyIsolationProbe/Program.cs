using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Inventor;
using Bimwright.Ipt.Shared.Handlers;
using File = System.IO.File;
using Path = System.IO.Path;

// Standalone research worker. One case per newly owned Inventor process.
// No ROT, product handler, event tracker, installation mutation, or process kill.
internal static partial class Program
{
    private sealed class ResearchFailure(string message) : Exception(message);
    private sealed record ProcessIdentity(int Pid,long StartUtcTicks);
    private static readonly JsonSerializerOptions JsonOptions=new() {WriteIndented=true};
    private static readonly JsonObject Report=new();
    private static Inventor.Application? App;
    private static PartDocument? OwnedDocument;
    private static string? OwnedDocumentId;
    private static int OwnedPid;
    private static long OwnedStart;
    private static ProcessIdentity[] Existing=Array.Empty<ProcessIdentity>();
    private static FileStream? Journal;
    private static string Phase="arguments";
    private static long Sequence;
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);

    [STAThread]
    private static int Main(string[] args)
    {
        int exit=1;string? reportPath=null;
        try
        {
            Require(args.Length==3 && (args[0]=="line" || args[0]=="text" || args[0]=="textclone") && (args[1]=="part" || args[1]=="sheet"), "Usage: line|text|textclone part|sheet <new-output-directory>.");
            bool cloneCase=args[0]=="textclone",textCase=args[0]=="text";
            string directory=Path.GetFullPath(args[2]);Require(!Directory.Exists(directory) && !File.Exists(directory),"Evidence directory already exists.");
            Directory.CreateDirectory(directory);reportPath=Path.Combine(directory,"report.json");
            Journal=new FileStream(Path.Combine(directory,"journal.jsonl"),FileMode.CreateNew,FileAccess.Write,FileShare.Read,4096,System.IO.FileOptions.WriteThrough);
            Report["captured_utc"]=DateTime.UtcNow.ToString("O");Report["case"]=args[0];Report["kind"]=args[1];
            Report["evidence"]=cloneCase ? "ISOLATED API RESEARCH: fitted native AddFitted constructor cloning from native FormattedText and owned local style; literal and parameter-tag cases. No MoveSketchObjects Copy, desktop parity or product capability."
                : textCase ? "ISOLATED API RESEARCH: one fitted TextBox only; Copy=true then two native Origin edits of the new TextBox, with raw model-space native glyph comparison. No mixed-copy, rotate-copy, other geometry, desktop parity, product, HTTP, Quest or physical proof."
                : "ISOLATED API RESEARCH: free line only; Copy=true then two native edits of the copied line. No mixed-copy, radius/text/image/spline, constraint preservation, desktop parity, product, HTTP, Quest or physical proof.";
            Report["second_update_scope"]=cloneCase ? "Literal: two native Origin edits. Parameter: two source-expression updates 10 to22 to7 mm, requiring both existing TextBoxes and raw glyphs to update before a separate fresh constructor oracle."
                : textCase ? "Two Origin setter translations of the actual new TextBox verify native identity/editability, unchanged original and raw glyph translation. No substitute creation or glyph manipulation; no parametric associativity claim."
                : "Two independent translations of the new copied line verify its native identity/editability and unchanged original; they do not prove parametric associativity.";
            Report["probe_event_tracker_installed"]=false;Report["other_addins_disabled"]=false;
            Report["preexisting_document_content_readback"]="NOT COVERED by this worker: no ROT/user-process COM access. Coordinator must verify its separate target/document baseline.";
            Report["interop_identity"]=typeof(Inventor.Application).Assembly.FullName;
            Require(typeof(Inventor.Application).Assembly.GetName().Version?.Major==31,"Installed Inventor 2027 interop 31.x required.");
            Existing=Processes();Report["existing_processes_before"]=JsonSerializer.SerializeToNode(Existing);
            Tick("worker.ready");
            long activationStarted=DateTime.UtcNow.Ticks;Report["activation_started_utc_ticks"]=activationStarted;
            App=Native("Activator.CreateInstance",()=> (Inventor.Application)Activator.CreateInstance(Type.GetTypeFromProgID("Inventor.Application",true)!)!);
            // Only read the HWND until a distinct process has been established.
            int hwnd=Native("Application.MainFrameHWND",()=>App!.MainFrameHWND);
            Require(GetWindowThreadProcessId(new IntPtr(hwnd),out uint pid)!=0 && pid!=0,"Cannot identify returned Inventor HWND process.");
            using(var process=Process.GetProcessById((int)pid))
            {
                long start=process.StartTime.ToUniversalTime().Ticks;
                Require(pid!=91056 && start>=activationStarted && !Existing.Any(p=>p.Pid==pid) && !Existing.Any(p=>p.Pid==pid && p.StartUtcTicks==start),"COM did not create a distinct permitted Inventor process; no document access or Quit permitted.");
                OwnedPid=(int)pid;OwnedStart=start;
            }
            Report["owned_pid"]=OwnedPid;Report["owned_start_utc_ticks"]=OwnedStart;Tick("owned.identity.verified");
            GuardProcess();Require(Native("Application.Documents.Count.initial",()=>App!.Documents.Count)==0,"Owned process did not start empty.");
            Require(Native("Application.ActiveDocument.initial",()=>App!.ActiveDocument)==null,"Owned process has an active document initially.");
            string template=Native("FileManager.GetTemplateFile",()=>args[1]=="sheet"
                ? App!.FileManager.GetTemplateFile(DocumentTypeEnum.kPartDocumentObject,SystemOfMeasureEnum.kDefaultSystemOfMeasure,DraftingStandardEnum.kDefault_DraftingStandard,"{9C464203-9BAE-11D3-8BAD-0060B0CE6BB4}")
                : App!.FileManager.GetTemplateFile(DocumentTypeEnum.kPartDocumentObject));
            GuardProcess();OwnedDocument=Native("Documents.Add.owned",()=> (PartDocument)App!.Documents.Add(DocumentTypeEnum.kPartDocumentObject,template,true));
            OwnedDocumentId=Native("Document.InternalName.owned",()=>OwnedDocument!.InternalName);GuardDocument();
            Native("Document.DisplayName.set",()=>{OwnedDocument!.DisplayName=cloneCase ? "S1_TextClone72.ipt" : textCase ? "S1_TextCopy70.ipt" : "S1_CopyIsolation65_Line.ipt";});
            Require(string.IsNullOrEmpty(Native("Document.FullFileName",()=>OwnedDocument!.FullFileName)),"Owned fixture is unexpectedly saved.");
            Require((OwnedDocument!.ComponentDefinition is SheetMetalComponentDefinition)==(args[1]=="sheet"),"Owned template context differs.");
            var sketch=Native("Sketches.Add",()=>OwnedDocument!.ComponentDefinition.Sketches.Add(OwnedDocument.ComponentDefinition.WorkPlanes[3]));
            Native("Sketch.Name.set",()=>{sketch.Name=cloneCase ? "S1_TextClone72" : textCase ? "S1_TextCopy70" : "S1_CopyIsolation65";});
            Require(Native("Document.Update2.initial",()=>OwnedDocument!.Update2(false)),"Initial owned fixture update failed.");
            if(cloneCase) RunTextClone72(sketch);else if(textCase) RunText(sketch);else RunLine(sketch);GuardDocument();Report["native_case_completed"]=true;exit=0;
        }
        catch(Exception error)
        {
            Report["failure"]=Error(error);WriteSanitized(error);try {Tick("worker.failure",Error(error));} catch { }
        }
        finally
        {
            var cleanup=new JsonArray();Report["cleanup"]=cleanup;
            if(OwnedPid!=0 && App!=null)
            {
                try
                {
                    if(OwnedDocument!=null)
                    {
                        GuardDocument();Require(string.IsNullOrEmpty(Native("Document.FullFileName.cleanup",()=>OwnedDocument!.FullFileName)),"Refusing to close a saved or changed owned fixture.");
                        Native("OwnedDocument.Close.skipSave",()=>OwnedDocument!.Close(true));OwnedDocument=null;OwnedDocumentId=null;
                        cleanup.Add("Only the explicitly owned unsaved fixture was closed.");
                    }
                    GuardProcess();Require(Native("Application.Documents.Count.beforeQuit",()=>App!.Documents.Count)==0,"Unexpected document prevents Quit.");
                    Native("OwnedApplication.Quit",()=>App!.Quit());Report["owned_quit_requested"]=true;
                }
                catch(Exception error) {cleanup.Add(Error(error));exit=1;WriteSanitized(error);}
            }
            if(App!=null && Marshal.IsComObject(App))
                try {Marshal.ReleaseComObject(App);} catch(Exception error) {cleanup.Add(Error(error));exit=1;}
            App=null;
            if(OwnedPid!=0)
            {
                bool exited;
                try {using var process=Process.GetProcessById(OwnedPid);exited=process.StartTime.ToUniversalTime().Ticks!=OwnedStart || process.WaitForExit(15000);}
                catch(ArgumentException) {exited=true;}
                catch(Exception error) {exited=false;cleanup.Add(Error(error));}
                Report["owned_process_exited"]=exited;if(!exited) exit=1;
            }
            try
            {
                var after=Processes();Report["existing_processes_after"]=JsonSerializer.SerializeToNode(after);
                bool preserved=Existing.All(before=>after.Any(p=>p==before));Report["preexisting_process_identities_preserved"]=preserved;
                if(!preserved) exit=1;
            }
            catch(Exception error) {Report["process_readback_failure"]=Error(error);exit=1;}
            Report["exit_code"]=exit;Report["outcome"]=exit==0 ? "PASS isolated copy geometry, two native edits, exact Abort and owned-process lifecycle only; inspect case scope" : "FAIL isolated copy research; inspect last durable journal phase; no automatic retry or kill";
            try
            {
                if(reportPath!=null) {using var output=new FileStream(reportPath,FileMode.CreateNew,FileAccess.Write,FileShare.Read);JsonSerializer.Serialize(output,Report,JsonOptions);output.Flush(true);}
                Tick("worker.finished",new JsonObject {["exit_code"]=exit});
            }
            catch(Exception error) {WriteSanitized(error);exit=1;}
            Journal?.Dispose();
        }
        return exit;
    }

    private static void RunLine(PlanarSketch sketch)
    {
        GuardDocument();var baseline=Capture(sketch);Report["baseline"]=baseline.DeepClone();Transaction? transaction=null;
        Exception? primary=null;
        try
        {
            transaction=Native("TransactionManager.StartTransaction",()=>App!.TransactionManager.StartTransaction((_Document)OwnedDocument!,"S1 isolated copy65, always aborted"));
            Require(!Native("Transaction.HasParentTransaction",()=>transaction.HasParentTransaction),"Copy research refuses nested transactions.");
            var source=Native("SketchLines.AddByTwoPoints",()=>sketch.SketchLines.AddByTwoPoints(Point(0,0),Point(20,10)));
            Native("Sketch.Solve.seed",()=>sketch.Solve());Require(Native("Document.Update2.seed",()=>OwnedDocument!.Update2(false)),"Seed update failed.");
            var sourceState=LineState(source);Report["source_before_copy"]=sourceState.DeepClone();Report["before_copy"]=Capture(sketch);
            string sourceKey=Key(source);var objects=Native("TransientObjects.CreateObjectCollection.copy",()=>App!.TransientObjects.CreateObjectCollection());
            Native("ObjectCollection.Add.sourceOnly",()=>objects.Add(source));
            var vector=Native("TransientGeometry.CreateVector2d.copy",()=>App!.TransientGeometry.CreateVector2d(UnitConvert.MmToCm(12),UnitConvert.MmToCm(-8)));
            GuardDocument();var returned=Native("Sketch.MoveSketchObjects.CopyTrue.RemoveFalse",()=>sketch.MoveSketchObjects(objects,vector,true,false));
            // Enumerator marshalling is a separate phase from the mutation itself.
            Report["returned_enumerator_null"]=returned==null;
            Report["native_returned_objects"]=Native("CopyResult.enumerate",()=>returned==null ? new JsonArray() : new JsonArray(returned.Cast<object>().Select(item=>(JsonNode)new JsonObject {["key"]=Key(item),["native_type"]=Native<string>("CopyResult.object.Type",()=> (string)((dynamic)item).Type.ToString())}).ToArray()));
            Native("Sketch.Solve.afterCopy",()=>sketch.Solve());Require(Native("Document.Update2.afterCopy",()=>OwnedDocument!.Update2(false)),"Copy update failed.");
            Report["after_copy"]=Capture(sketch);RequireOriginalConstraints((JsonObject)Report["before_copy"]!,(JsonObject)Report["after_copy"]!);
            var copies=Native("SketchLines.enumerate.afterCopy",()=>sketch.SketchLines.Cast<SketchLine>().Where(line=>Key(line)!=sourceKey).ToArray());
            Require(copies.Length==1 && sketch.SketchLines.Count==2,"Expected one distinct new copied line.");var copy=copies[0];string copyKey=Key(copy);
            Require(Key(copy.StartSketchPoint)!=Key(source.StartSketchPoint) && Key(copy.EndSketchPoint)!=Key(source.EndSketchPoint),"Copy shares original endpoint identities.");
            Require(JsonNode.DeepEquals(sourceState,LineState(source)),"Copy changed original line geometry or reference keys.");
            RequireLine(copy,12,-8,32,2);Report["copy_line_key"]=copyKey;
            var updates=new JsonArray();Report["copy_native_edits"]=updates;double x=12,y=-8;
            foreach(var delta in new[]{(x:3d,y:4d),(x:-2d,y:5d)})
            {
                var edit=new JsonObject {["delta_mm"]=new JsonArray(delta.x,delta.y)};updates.Add(edit);
                var selected=Native("TransientObjects.CreateObjectCollection.edit",()=>App!.TransientObjects.CreateObjectCollection());
                Native("ObjectCollection.Add.copyLine",()=>selected.Add(copy));Native("ObjectCollection.Add.copyStart",()=>selected.Add(copy.StartSketchPoint));Native("ObjectCollection.Add.copyEnd",()=>selected.Add(copy.EndSketchPoint));
                var motion=Native("TransientGeometry.CreateVector2d.edit",()=>App!.TransientGeometry.CreateVector2d(UnitConvert.MmToCm(delta.x),UnitConvert.MmToCm(delta.y)));
                GuardDocument();Native("Sketch.MoveSketchObjects.CopyFalse.edit",()=>sketch.MoveSketchObjects(selected,motion,false,false));
                Native("Sketch.Solve.afterEdit",()=>sketch.Solve());Require(Native("Document.Update2.afterEdit",()=>OwnedDocument!.Update2(false)),"Copied line edit update failed.");
                x+=delta.x;y+=delta.y;RequireLine(copy,x,y,x+20,y+10);
                Require(Key(copy)==copyKey && JsonNode.DeepEquals(sourceState,LineState(source)),"Editing copy changed identity or original line.");
                edit["same_native_copy_key"]=true;edit["original_unchanged"]=true;edit["native_copy"]=LineState(copy);edit["state"]=Capture(sketch);
                RequireOriginalConstraints((JsonObject)Report["before_copy"]!,(JsonObject)edit["state"]!);edit["original_constraints_unchanged"]=true;
            }
            Report["copy_and_second_native_edit_verified"]=true;
        }
        catch(Exception error) {primary=error;Report["case_failure"]=Error(error);throw;}
        finally
        {
            if(transaction!=null)
            {
                try
                {
                    GuardDocument();Require(Same(Native("TransactionManager.CurrentTransaction.beforeAbort",()=>App!.TransactionManager.CurrentTransaction),transaction),"Owned transaction is no longer current; refusing unrelated Abort.");
                    Native("Transaction.Abort",()=>transaction.Abort());
                    var restored=Capture(sketch);Report["after_abort"]=restored;bool exact=JsonNode.DeepEquals(baseline,restored);Report["exact_abort"]=exact;
                    Require(exact,"Abort did not restore exact owned native baseline.");
                }
                catch(Exception rollbackError) {Report["rollback_failure"]=Error(rollbackError);if(primary==null) throw;}
            }
        }
        Require((bool?)Report["exact_abort"]==true,"Exact native Abort is required.");
    }

    private static JsonObject Capture(PlanarSketch sketch)=>Native("NativeReadback.capture",()=>
    {
        GuardDocument();Require(OwnedDocument!.ComponentDefinition.Sketches.Count==1 && Same(OwnedDocument.ComponentDefinition.Sketches[1],sketch),"Owned sketch collection changed.");
        var lines=new JsonArray(sketch.SketchLines.Cast<SketchLine>().Select(line=>(JsonNode)LineState(line)).OrderBy(node=>(string?)node["key"],StringComparer.Ordinal).ToArray());
        var points=new JsonArray(sketch.SketchPoints.Cast<SketchPoint>().Select(point=>(JsonNode)new JsonObject {["key"]=Key(point),["point_mm"]=Coordinates(point.Geometry),["hole_center"]=point.HoleCenter}).OrderBy(node=>(string?)node["key"],StringComparer.Ordinal).ToArray());
        var constraints=new JsonArray();foreach(GeometricConstraint constraint in sketch.GeometricConstraints)
        {
            var item=new JsonObject {["key"]=Key(constraint),["type"]=constraint.Type.ToString(),["deletable"]=constraint.Deletable};constraints.Add(item);
            if(constraint is CoincidentConstraint coincidence) {item["entity_one_key"]=Key(coincidence.EntityOne);item["entity_two_key"]=Key(coincidence.EntityTwo);}
            else {Report["unread_constraint"]=item.DeepClone();throw new ResearchFailure("Unexpected relation type in minimal free-line fixture; no inferred constraint associations.");}
        }
        var parameters=new JsonArray();foreach(Parameter parameter in OwnedDocument.ComponentDefinition.Parameters)
            parameters.Add(new JsonObject {["key"]=Key(parameter),["name"]=parameter.Name,["expression"]=parameter.Expression,["units"]=parameter.get_Units(),["database_scalar"]=Convert.ToDouble(parameter.Value),["health"]=parameter.HealthStatus.ToString()});
        Require(sketch.DimensionConstraints.Count==0 && sketch.SketchArcs.Count==0 && sketch.SketchCircles.Count==0 && sketch.TextBoxes.Count==0 && sketch.SketchImages.Count==0,"Unexpected geometry or dimensions in minimal line fixture.");
        Require(sketch.ConstraintStatus!=ConstraintStatusEnum.kOverConstrainedConstraintStatus,"Native solver conflict.");
        return new JsonObject {["document_id"]=OwnedDocument.InternalName,["title"]=OwnedDocument.DisplayName,["path"]=OwnedDocument.FullFileName,["sketch_key"]=Key(sketch),["sketch_name"]=sketch.Name,
            ["sketch_count"]=OwnedDocument.ComponentDefinition.Sketches.Count,["entity_count"]=sketch.SketchEntities.Count,["line_count"]=sketch.SketchLines.Count,["point_count"]=sketch.SketchPoints.Count,["dimension_count"]=sketch.DimensionConstraints.Count,
            ["solver_status"]=sketch.ConstraintStatus.ToString(),["lines"]=lines,["points"]=points,["constraints"]=new JsonArray(constraints.OrderBy(node=>(string?)node!["key"],StringComparer.Ordinal).Select(node=>node!.DeepClone()).ToArray()),["parameters"]=parameters};
    });
    private static JsonObject LineState(SketchLine line)=>Native("LineReadback",()=>new JsonObject {["key"]=Key(line),["start_key"]=Key(line.StartSketchPoint),["end_key"]=Key(line.EndSketchPoint),["start_mm"]=Coordinates(line.StartSketchPoint.Geometry),["end_mm"]=Coordinates(line.EndSketchPoint.Geometry),["construction"]=line.Construction,["centerline"]=line.Centerline});
    private static void RequireOriginalConstraints(JsonObject before,JsonObject after)
    {
        foreach(var original in (JsonArray)before["constraints"]!)
            Require(((JsonArray)after["constraints"]!).Any(current=>JsonNode.DeepEquals(original,current)),"Copy or copied-line edit changed an original native constraint/key/member binding.");
    }
    private static void RequireLine(SketchLine line,double x,double y,double ex,double ey)
    {
        var a=line.StartSketchPoint.Geometry;var b=line.EndSketchPoint.Geometry;
        Require(Math.Abs(UnitConvert.CmToMm(a.X)-x)<=1e-5 && Math.Abs(UnitConvert.CmToMm(a.Y)-y)<=1e-5 && Math.Abs(UnitConvert.CmToMm(b.X)-ex)<=1e-5 && Math.Abs(UnitConvert.CmToMm(b.Y)-ey)<=1e-5,"Native copied line differs from independent translated endpoints.");
    }
    private static Point2d Point(double x,double y)=>Native("TransientGeometry.CreatePoint2d",()=>App!.TransientGeometry.CreatePoint2d(UnitConvert.MmToCm(x),UnitConvert.MmToCm(y)));
    private static JsonArray Coordinates(Point2d point)=>new(UnitConvert.CmToMm(point.X),UnitConvert.CmToMm(point.Y));
    private static string Key(object entity)=>Native("Native.GetReferenceKey",()=>{byte[] bytes=Array.Empty<byte>();((dynamic)entity).GetReferenceKey(ref bytes,0);return Convert.ToBase64String(bytes);});
    private static ProcessIdentity[] Processes()=>Process.GetProcessesByName("Inventor").Select(process=>{using(process) return new ProcessIdentity(process.Id,process.StartTime.ToUniversalTime().Ticks);}).OrderBy(p=>p.Pid).ToArray();
    private static void GuardProcess()
    {
        Require(App!=null && OwnedPid!=0 && OwnedPid!=91056 && !Existing.Any(p=>p.Pid==OwnedPid),"No permitted owned Inventor process.");
        int hwnd=Native("Application.MainFrameHWND.guard",()=>App!.MainFrameHWND);Require(GetWindowThreadProcessId(new IntPtr(hwnd),out uint pid)!=0 && pid==OwnedPid,"Owned HWND process changed.");
        using var process=Process.GetProcessById(OwnedPid);Require(process.StartTime.ToUniversalTime().Ticks==OwnedStart,"Owned PID start identity changed.");
    }
    private static void GuardDocument()
    {
        GuardProcess();Require(OwnedDocument!=null && OwnedDocumentId!=null,"No explicitly owned document.");
        Require(Native("Application.Documents.Count.guard",()=>App!.Documents.Count)==1 && Same(Native("Application.Documents.Item.guard",()=>App!.Documents[1]),OwnedDocument!),"Unexpected document prevents mutation or closure.");
        Require(Native("Document.InternalName.guard",()=>OwnedDocument!.InternalName)==OwnedDocumentId,"Owned document identity changed.");
        Require(Same(Native("Application.ActiveDocument.guard",()=>App!.ActiveDocument),OwnedDocument!),"Owned fixture is not active.");
    }
    private static bool Same(object? one,object? two)
    {
        if(one==null || two==null) return false;IntPtr a=Marshal.GetIUnknownForObject(one),b=Marshal.GetIUnknownForObject(two);
        try {return a==b;} finally {Marshal.Release(a);Marshal.Release(b);}
    }
    private static T Native<T>(string operation,Func<T> action)
    {
        Phase=operation;Tick("api.before",new JsonObject {["operation"]=operation});
        try {T result=action();Phase=operation;Tick("api.after",new JsonObject {["operation"]=operation});return result;}
        catch(Exception error) {Phase=operation;Tick("api.failure",Error(error));throw;}
    }
    private static void Native(string operation,Action action)=>Native(operation,()=>{action();return true;});
    private static void Tick(string stage,JsonObject? details=null)
    {
        if(Journal==null) return;var entry=new JsonObject {["sequence"]=++Sequence,["utc"]=DateTime.UtcNow.ToString("O"),["stage"]=stage,["phase"]=Phase,["owned_pid"]=OwnedPid,["details"]=details};
        byte[] data=System.Text.Encoding.UTF8.GetBytes(entry.ToJsonString()+"\n");Journal.Write(data);Journal.Flush(true);
    }
    private static JsonObject Error(Exception error)
    {
        var result=new JsonObject {["phase"]=Phase,["type"]=error.GetType().FullName,["hresult"]="0x"+error.HResult.ToString("X8")};
        if(error is ResearchFailure) result["static_assertion"]=error.Message;
        return result;
    }
    private static void WriteSanitized(Exception error)=>Console.Error.WriteLine(new JsonObject {["phase"]=Phase,["type"]=error.GetType().FullName,["hresult"]="0x"+error.HResult.ToString("X8")}.ToJsonString());
    private static void Require(bool test,string staticMessage) {if(!test) throw new ResearchFailure(staticMessage);}
}

