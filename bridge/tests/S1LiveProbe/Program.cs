using System.Reflection;
using System.Runtime.InteropServices;
using Inventor;
using Newtonsoft.Json.Linq;
using Path = System.IO.Path;
using File = System.IO.File;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;
using Bimwright.Ipt.Shared.Plugin;

internal static partial class Program
{
    [DllImport("oleaut32.dll", PreserveSig = false)]
    private static extern void GetActiveObject(ref Guid clsid, IntPtr reserved,
        [MarshalAs(UnmanagedType.IUnknown)] out object app);

    private static JObject State(Document? doc) => doc == null ? new JObject() : new JObject
    {
        ["id"] = doc.InternalName, ["revision"] = doc.DatabaseRevisionId, ["dirty"] = doc.Dirty,
        ["title"] = doc.DisplayName
    };

    [STAThread]
    private static int Main(string[] args)
    {
        if(args.Length==2 && args[1]=="state") return ReadNativeState(args[0]);
        if(args.Length==2 && args[1]=="image-product-isolated") return RunIsolatedImageProbe45(args[0]);
        if(args.Length==2 && args[1]=="image-transform-isolated") return RunIsolatedImageProbe45(args[0], true);
        if(args.Length==2 && args[1]=="image-history-isolated") return RunIsolatedImageProbe45(args[0], history: true);
        if(args.Length==2 && args[1]=="image-savecopy-isolated") return RunIsolatedImageProbe45(args[0], saveCopy: true);
        if(args.Length==2 && args[1]=="image-download-isolated") return RunIsolatedImageProbe45(args[0], download: true);
        if(args.Length==2 && args[1]=="image-frame-isolated") return RunIsolatedImageProbe45(args[0], frame: true);
        if(args.Length==2 && args[1]=="image-pixels-inprocess-isolated") return RunIsolatedImageProbe45(args[0], nativePixels: true);
        if(args.Length==2 && args[1]=="image-liveframe-inprocess-isolated") return RunIsolatedImageProbe45(args[0], liveFrame: true);
        if(args.Length==2 && args[1]=="image-live-savecopy-inprocess-isolated") return RunIsolatedImageProbe45(args[0], liveSaveCopy: true);
        if(args.Length==2 && args[1].StartsWith("ellipse-ui-",StringComparison.Ordinal)) return EllipseUiProbe(args[0],args[1]);
        if(args.Length==2 && args[1].StartsWith("arc-ui-",StringComparison.Ordinal)) return EllipseUiProbe(args[0],args[1],"arc");
        if(args.Length==2 && args[1].StartsWith("slot-ui-",StringComparison.Ordinal)) return EllipseUiProbe(args[0],args[1],"slot");
        if(args.Length==2 && args[1].StartsWith("polygon-ui-",StringComparison.Ordinal)) return EllipseUiProbe(args[0],args[1],"polygon");
        if(args.Length==2 && args[1].StartsWith("arcslot-ui-",StringComparison.Ordinal)) return EllipseUiProbe(args[0],args[1],"arcslot");
        if (args.Length < 1 || args.Skip(1).Count(a=>a!="isolated")>1 || args.Skip(1).Any(a=>a!="constraints" && a!="dimensions" && a!="transforms" && a!="stretch" && a!="offsets" && a!="patterns" && a!="scale-api" && a!="midpoint" && a!="isolated")) { Console.Error.WriteLine("Usage: S1LiveProbe <report.json> [constraints|dimensions|transforms|stretch|offsets|patterns|scale-api|midpoint|state] [isolated]"); return 64; }
        var report = new JObject { ["captured_utc"] = DateTime.UtcNow.ToString("O"), ["evidence"] = "Inventor 2027 native fixture, not Quest or physical" };
        report["offset_shape_filter"]=System.Environment.GetEnvironmentVariable("S1_PROBE_OFFSET_SHAPE");
        report["transform_mode_filter"]=System.Environment.GetEnvironmentVariable("S1_PROBE_TRANSFORM_MODE");
        report["constraint_scenario_filter"]=System.Environment.GetEnvironmentVariable("S1_PROBE_CONSTRAINT_SCENARIO");
        report["pattern_expressions"]=System.Environment.GetEnvironmentVariable("S1_PROBE_PATTERN_EXPRESSIONS")=="1";
        report["pattern_kind_filter"]=System.Environment.GetEnvironmentVariable("S1_PROBE_PATTERN_KIND");
        report["pattern_independent_options"]=System.Environment.GetEnvironmentVariable("S1_PROBE_PATTERN_INDEPENDENT_OPTIONS")=="1";
        report["pattern_multiple_suppression"]=System.Environment.GetEnvironmentVariable("S1_PROBE_PATTERN_MULTIPLE_SUPPRESSION")=="1";
        global::Inventor.Application? app = null;
        Document? previous = null;
        var before = new JObject();
        PartDocument? fixture = null;
        bool restored = false;
        bool ownedApplication = false;
        IDisposable? tracker = null;
        try
        {
            var clsid = Type.GetTypeFromProgID("Inventor.Application")!.GUID;
            if(args.Contains("isolated"))
            {
                // Never replace or close an existing user's Inventor instance.
                object? existing=null;
                try { GetActiveObject(ref clsid,IntPtr.Zero,out existing); }
                catch(COMException ex) when(ex.HResult==unchecked((int)0x800401E3)) { }
                if(existing!=null) throw new InvalidOperationException("Isolated probe requires Inventor to be closed.");
                app=(global::Inventor.Application)Activator.CreateInstance(Type.GetTypeFromProgID("Inventor.Application")!)!;
                ownedApplication=true; report["application_owned_by_probe"]=true;
            }
            else { GetActiveObject(ref clsid, IntPtr.Zero, out var active); app = (global::Inventor.Application)active; }
            previous = app.ActiveDocument;
            before = State(previous);
            report["previous_before"] = before;
            report["version"] = app.SoftwareVersion.DisplayVersion;
            report["interop"] = InteropSignatures();
            var trackerType = typeof(InventorCommandRegistry).Assembly.GetType("Bimwright.Ipt.Shared.Plugin.CadEventTracker", true)!;
            tracker = (IDisposable)Activator.CreateInstance(trackerType, app)!;
            var journal = (CadEventJournal)trackerType.GetProperty("Journal")!.GetValue(tracker)!;
            var commands = InventorCommandRegistry.Build(new PluginOptions(2027, false, false, 0));
            var commandContext = new InventorCommandContext { Application = app, Events = journal, Commands = commands, AllowExperimental = true, InventorYear = 2027 };
            var contexts = new JArray(); report["contexts"] = contexts;
            foreach (bool sheet in new[] { false, true })
            {
                string template = sheet
                    ? app.FileManager.GetTemplateFile(DocumentTypeEnum.kPartDocumentObject,
                        SystemOfMeasureEnum.kDefaultSystemOfMeasure, DraftingStandardEnum.kDefault_DraftingStandard,
                        "{9C464203-9BAE-11D3-8BAD-0060B0CE6BB4}")
                    : app.FileManager.GetTemplateFile(DocumentTypeEnum.kPartDocumentObject);
                fixture = (PartDocument)app.Documents.Add(DocumentTypeEnum.kPartDocumentObject, template, true);
                fixture.DisplayName = sheet ? "S1_Probe_Lamiera.ipt" : "S1_Probe_Parte.ipt";
                var context = new JObject { ["kind"] = sheet ? "sheetmetal" : "part", ["outside_sketch"] = ReadRibbon(app) };
                contexts.Add(context);
                var tx = app.TransactionManager.StartTransaction((_Document)fixture, "S1 native oracle, aborted");
                try
                {
                    var sketch = fixture.ComponentDefinition.Sketches.Add(fixture.ComponentDefinition.WorkPlanes[3]);
                    sketch.Name = "S1_NativeOracle";
                    sketch.Edit();
                    context["editing_sketch"] = ReadRibbon(app);
                    context["geometry"] = ProbeGeometry(app, sketch);
                    sketch.ExitEdit();
                    string documentId = "doc_" + fixture.InternalName;
                    string revision = journal.Revision(documentId);
                    var state = new JObject { ["document_id"] = documentId, ["expected_revision"] = revision };
                    var design = commands["get_design_context_xr"].Execute(commandContext, state);
                    if (!design.Ok) throw new InvalidOperationException("Design context failed: " + design.Error?.Message);
                    string sketchId = (string)design.Data!["sketches"]![0]!["sketch_id"]!;
                    state["sketch_id"] = sketchId;
                    var snapshot = commands["get_sketch_editor_xr"].Execute(commandContext, state);
                    if (!snapshot.Ok) throw new InvalidOperationException("Editor snapshot failed: " + snapshot.Error?.Message);
                    context["editor_snapshot"] = snapshot.Data;
                    if (journal.Revision(documentId) != revision) throw new InvalidOperationException("Read changed the native revision.");
                }
                finally { tx.Abort(); }
                context["rollback_sketch_count"] = fixture.ComponentDefinition.Sketches.Count;
                if (fixture.ComponentDefinition.Sketches.Count != 0) throw new InvalidOperationException("S1 fixture rollback left a sketch.");
                bool full=!args.Contains("constraints") && !args.Contains("dimensions") && !args.Contains("transforms") && !args.Contains("stretch") && !args.Contains("offsets") && !args.Contains("patterns") && !args.Contains("scale-api") && !args.Contains("midpoint");
                context["incremental_commands"] = full ? ProbeIncremental(app, fixture, commands, commandContext, journal) : new JArray();
                context["format_commands"] = full ? ProbeFormat(app,fixture,commands,commandContext,journal) : new JArray();
                var constraintRows=new JArray();context["constraint_commands"]=constraintRows;
                if(full || args.Contains("constraints")) ProbeConstraints(app,fixture,commands,commandContext,journal,constraintRows);
                var dimensionRows=new JArray(); context["dimension_commands"]=dimensionRows;
                if(full || args.Contains("dimensions")) ProbeDimensions(app,fixture,commands,commandContext,journal,dimensionRows);
                var transformRows=new JArray();context["transform_commands"]=transformRows;
                if(full || args.Contains("transforms")) ProbeTransforms(app,fixture,commands,commandContext,journal,transformRows);
                var stretchRows=new JArray();context["stretch_commands"]=stretchRows;
                if(full || args.Contains("stretch")) ProbeStretch(app,fixture,commands,commandContext,journal,stretchRows);
                var offsetRows=new JArray();context["offset_commands"]=offsetRows;
                if(full || args.Contains("offsets")) ProbeOffsets(app,fixture,commands,commandContext,journal,offsetRows);
                var patternRows=new JArray();context["pattern_commands"]=patternRows;
                if(full || args.Contains("patterns")) ProbePatterns(app,fixture,commands,commandContext,journal,patternRows);
                var scaleRows=new JArray();context["scale_api_cases"]=scaleRows;
                if(args.Contains("scale-api")) ProbeScaleApi(app,fixture,scaleRows);
                var midpointRows=new JArray();context["midpoint_commands"]=midpointRows;
                if(args.Contains("midpoint")) ProbeMidpoint(app,fixture,commands,commandContext,journal,midpointRows);
                fixture.Close(true); fixture = null;
            }
            report["creation_cases"] = contexts.Cast<JObject>().Sum(c=>((JArray)c["incremental_commands"]!).Count(row=>(string?)row["variant"]!="failure_and_stale_guards"));
            report["format_cases"] = contexts.Cast<JObject>().Sum(c=>((JArray)c["format_commands"]!).Count(row=>(string?)row["variant"]!="format_guards"));
            report["constraint_cases"] = contexts.Cast<JObject>().Sum(c=>((JArray)c["constraint_commands"]!).Count(row=>(string?)row["variant"]!="constraint_guards"));
            report["constraint_deletion_checks"] = contexts.Cast<JObject>().Sum(c=>((JArray)c["constraint_commands"]!).Sum(row=>(int?)row["deletion_checks"] ?? 0));
            report["transform_cases"] = contexts.Cast<JObject>().Sum(c=>((JArray)c["transform_commands"]!).Count);
            report["stretch_cases"] = contexts.Cast<JObject>().Sum(c=>((JArray)c["stretch_commands"]!).Count);
            report["offset_cases"] = contexts.Cast<JObject>().Sum(c=>((JArray)c["offset_commands"]!).Count);
            report["pattern_cases"] = contexts.Cast<JObject>().Sum(c=>((JArray)c["pattern_commands"]!).Count);
            report["scale_api_cases"] = contexts.Cast<JObject>().Sum(c=>((JArray)c["scale_api_cases"]!).Count);
            report["dimension_cases"] = contexts.Cast<JObject>().Sum(c=>((JArray)c["dimension_commands"]!).Count(d=>!((string?)d["variant"] ?? "").StartsWith("dimension_lifecycle_",StringComparison.Ordinal)));
            report["dimension_lifecycle_cases"] = contexts.Cast<JObject>().Sum(c=>((JArray)c["dimension_commands"]!).Count(d=>((string?)d["variant"] ?? "").StartsWith("dimension_lifecycle_",StringComparison.Ordinal)));
            report["outcome"] = "PASS native fixture: creations="+report["creation_cases"]+", formats="+report["format_cases"]+", constraints="+report["constraint_cases"]+", dimensions="+report["dimension_cases"]+", dimension_lifecycles="+report["dimension_lifecycle_cases"]+", transforms="+report["transform_cases"]+", offsets="+report["offset_cases"]+", patterns="+report["pattern_cases"]+"; rollback and reference guards; full desktop equivalence and Quest gates pending";
            if(args.Contains("scale-api"))
            {
                var observations=contexts.Cast<JObject>().SelectMany(c=>((JArray)c["scale_api_cases"]!).OfType<JObject>()).ToArray();
                report["uniform_scale_observations"]=observations.Count(r=>(bool?)r["matches_uniform_scale"]==true);
                report["nonuniform_or_failed_observations"]=observations.Count(r=>(bool?)r["matches_uniform_scale"]!=true);
                report["outcome"]="PASS feasibility collection and rollback: "+observations.Length+" observations; observed failures are recorded, no production Scale or desktop parity certified";
            }
        }
        catch (Exception ex)
        { report["outcome"] = "FAIL"; report["error"] = ex.ToString(); }
        finally
        {
            try
            {
                fixture?.Close(true);
                previous?.Activate();
                var after = State(previous);
                report["previous_after"] = after;
                var activeAfter=State(app?.ActiveDocument);
                report["active_document_after"]=activeAfter;
                restored = app!=null && JToken.DeepEquals(before, after) && JToken.DeepEquals(before,activeAfter);
                report["previous_restored"] = restored;
            }
            catch (Exception ex) { report["restoration_error"] = ex.ToString(); }
            try { tracker?.Dispose(); }
            catch (Exception ex) { report["tracker_cleanup_error"] = ex.ToString(); report["outcome"] = "FAIL"; }
            if(ownedApplication && app!=null)
            {
                try { if(app.Documents.Count!=0) throw new InvalidOperationException("Owned Inventor still has open documents; refusing automatic shutdown."); app.Quit(); report["owned_application_closed"]=true; }
                catch(Exception ex) {report["application_cleanup_error"]=ex.ToString(); report["outcome"]="FAIL";}
            }
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[0]))!);
            File.WriteAllText(args[0], report.ToString());
        }
        Console.WriteLine(report["outcome"] + "; previous_restored=" + restored);
        return restored && (string?)report["outcome"] != "FAIL" ? 0 : 1;
    }

    private static JArray ProbeConstraints(global::Inventor.Application app,PartDocument fixture,
        IReadOnlyDictionary<string,IInventorCommand> commands,InventorCommandContext context,CadEventJournal journal,JArray rows)
    {
        var g=app.TransientGeometry;
        string documentId="doc_"+fixture.InternalName;
        JObject State()=>new JObject {["document_id"]=documentId,["expected_revision"]=journal.Revision(documentId)};
        JObject Call(string name,JObject args)
        { var result=commands[name].Execute(context,args); if(!result.Ok) throw new InvalidOperationException(name+": "+result.Error?.Code+" "+result.Error?.Message); return (JObject)result.Data!; }
        JObject? foreign=null;
        string[] scenarios={"ground","horizontal","vertical","parallel","collinear","perpendicular","equal","concentric","tangent","coincident","symmetry","smooth","coincident_point_curve","horizontal_align","vertical_align"};
        string? scenarioFilter=System.Environment.GetEnvironmentVariable("S1_PROBE_CONSTRAINT_SCENARIO");
        if(scenarioFilter!=null && !scenarios.Contains(scenarioFilter,StringComparer.Ordinal)) throw new ArgumentException("Unknown constraint probe scenario filter.");
        foreach(string scenario in scenarios)
        {
            if(scenarioFilter!=null && scenario!=scenarioFilter && scenario!="ground") continue;
            string kind=scenario=="coincident_point_curve" ? "coincident" : scenario=="horizontal_align" ? "horizontal" : scenario=="vertical_align" ? "vertical" : scenario;
            var row=new JObject {["variant"]="constraint_"+kind,["scenario"]=scenario,["outcome"]="INCOMPLETE"};rows.Add(row);
            Console.WriteLine("Native constraint case: "+scenario);
            var seed=app.TransactionManager.StartTransaction((_Document)fixture,"S1 constraint fixture "+kind);
            var sketch=fixture.ComponentDefinition.Sketches.Add(fixture.ComponentDefinition.WorkPlanes[3]); sketch.Name="S1_Constraint_"+scenario;
            var selections=new List<(object entity,string part)>();
            try
            {
                SketchLine Line(double x,double y,double xx,double yy)=>sketch.SketchLines.AddByTwoPoints(g.CreatePoint2d(x,y),g.CreatePoint2d(xx,yy));
                if(scenario=="coincident_point_curve")
                {selections.Add((sketch.SketchPoints.Add(g.CreatePoint2d(2,1.3),false),"Point")); selections.Add((Line(1,1,4,2),"Entity"));}
                else if(scenario=="horizontal_align" || scenario=="vertical_align")
                {selections.Add((Line(1,1,2,2),"End")); selections.Add((Line(4,4,5,4.5),"Start"));}
                else if(kind=="concentric")
                { selections.Add((sketch.SketchCircles.AddByCenterRadius(g.CreatePoint2d(1,1),.5),"Entity")); selections.Add((sketch.SketchCircles.AddByCenterRadius(g.CreatePoint2d(2,2),.7),"Entity")); }
                else if(kind=="tangent")
                { selections.Add((sketch.SketchCircles.AddByCenterRadius(g.CreatePoint2d(2,2),1),"Entity")); selections.Add((Line(0,.8,4,1.1),"Entity")); }
                else if(kind=="coincident")
                { selections.Add((Line(1,1,2,2),"End")); selections.Add((Line(4,4,5,4.5),"Start")); }
                else if(kind=="symmetry")
                { selections.Add((sketch.SketchPoints.Add(g.CreatePoint2d(-1,1),false),"Point")); selections.Add((sketch.SketchPoints.Add(g.CreatePoint2d(1.2,1.3),false),"Point")); selections.Add((Line(0,-2,0,3),"Entity")); }
                else if(kind=="smooth")
                {
                    SketchSpline Spline(params double[] xy)
                    { var points=app.TransientObjects.CreateObjectCollection(); for(int i=0;i<xy.Length;i+=2) points.Add(g.CreatePoint2d(xy[i],xy[i+1])); return sketch.SketchSplines.Add(points); }
                    var first=Spline(0,0,.5,.05,1,.2,1.5,.5,2,1);
                    var second=Spline(2,1,2.5,1.4,3,1.7,3.5,1.9,4,2);
                    if(!SameComObject(first.EndSketchPoint,second.StartSketchPoint)) first.EndSketchPoint.Merge(second.StartSketchPoint);
                    selections.Add((first,"Entity")); selections.Add((second,"Entity"));
                }
                else
                { selections.Add((Line(0,0,2,1),"Entity")); if(kind!="ground" && kind!="horizontal" && kind!="vertical") selections.Add((Line(1,3,4,4.7),"Entity")); }
                seed.End();
            }
            catch { seed.Abort(); throw; }
            string sketchId=(string)((JArray)Call("get_design_context_xr",State())["sketches"]!).OfType<JObject>().Single(s=>(string?)s["name"]==sketch.Name)["sketch_id"]!;
            JObject Snapshot() { var args=State(); args["sketch_id"]=sketchId; return Call("get_sketch_editor_xr",args); }
            var before=Snapshot(); string revision=journal.Revision(documentId); int count=sketch.GeometricConstraints.Count,entities=sketch.SketchEntities.Count;
            var refs=new JArray();
            foreach(var selection in selections)
            {
                var item=((JArray)before["entities"]!).OfType<JObject>().Single(e=>
                {
                    var args=State(); args["entity_id"]=e["id"];
                    // Resolve the same opaque key used by the editor, then compare native COM identity.
                    var resolver=typeof(InventorCommandRegistry).Assembly.GetType("Bimwright.Ipt.Shared.Handlers.Experimental.SketchReferences",true)!;
                    var resolved=resolver.GetMethod("Resolve",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic)!.Invoke(null,new object[] {fixture,(string)e["id"]!,"sketch_entity"});
                    IntPtr a=Marshal.GetIUnknownForObject(resolved!),b=Marshal.GetIUnknownForObject(selection.entity);
                    try {return a==b;} finally {Marshal.Release(a);Marshal.Release(b);}
                });
                refs.Add(new JObject {["document_id"]=documentId,["revision"]=revision,["sketch_id"]=sketchId,["entity_id"]=item["id"],["type"]=item["type"],["subpart"]=selection.part});
            }
            var options=new JObject {["references"]=refs}; if(kind=="smooth") options["proximity_mm"]=new JArray(20,10);
            var op=new JObject {["command"]="edit_sketch",["arguments"]=new JObject {["sketch_id"]=sketchId,["variant"]="constraint_"+kind,["options"]=options}};
            JObject Batch(bool preview) {var args=State();args["operations"]=new JArray(op.DeepClone());args["preview"]=preview;args["include_preview_mesh"]=preview;return args;}
            if(scenario=="horizontal")
            {
                void Refuse(JObject invalid)
                {var args=State();args["operations"]=new JArray(invalid);if(commands["atomic_batch"].Execute(context,args).Ok) throw new InvalidOperationException("Invalid constraint was accepted.");
                    if(journal.Revision(documentId)!=revision || !JToken.DeepEquals(before,Snapshot())) throw new InvalidOperationException("Invalid constraint left a document change.");}
                var stale=(JObject)op.DeepClone(); stale["arguments"]!["options"]!["references"]![0]!["revision"]="obsolete"; Refuse(stale);
                var owner=(JObject)op.DeepClone(); var wrongOwner=(JObject)foreign!.DeepClone(); wrongOwner["revision"]=revision; wrongOwner["sketch_id"]=sketchId;
                owner["arguments"]!["options"]!["references"]=new JArray(wrongOwner); Refuse(owner);
                var malformed=(JObject)op.DeepClone(); malformed["arguments"]!["options"]!["ellipse_one_major_axis"]="yes"; Refuse(malformed);
                var badProximity=(JObject)op.DeepClone(); badProximity["arguments"]!["options"]!["proximity_mm"]=new JArray(1); Refuse(badProximity);
                rows.Add(new JObject {["variant"]="constraint_guards",["outcome"]="PASS stale references, foreign sketch owner, malformed axis/proximity; exact snapshot/revision rollback"});
            }
            try
            {
                var preview=Call("atomic_batch",Batch(true));
                var ghost=((JArray)preview["preview_mesh"]!["sketches"]!).OfType<JObject>().Single(s=>(string?)s["sketch_name"]==sketch.Name);
                if(scenario=="coincident")
                {
                    var curves=((JArray)ghost["native_entities"]!).OfType<JObject>().Where(e=>(string?)e["type"]=="line").ToArray();
                    if(!JToken.DeepEquals(curves[0]["end_mm"],curves[1]["start_mm"])) throw new InvalidOperationException("Coincidence preview lacks joined endpoints.");
                }
                else if(((JArray)ghost["geometric_constraints"]!).Count<=count) throw new InvalidOperationException("Preview lacks native constraint.");
                if(journal.Revision(documentId)!=revision || sketch.GeometricConstraints.Count!=count || sketch.SketchEntities.Count!=entities
                    || !JToken.DeepEquals(before,Snapshot())) throw new InvalidOperationException("Constraint preview did not completely restore snapshot/revision.");
                Call("atomic_batch",Batch(false)); var after=Snapshot();
                var added=((JArray)after["constraints"]!).OfType<JObject>().Where(c=>!((JArray)before["constraints"]!).Any(b=>(string?)b["id"]==(string?)c["id"])).ToArray();
                bool joined=scenario=="coincident" && SameComObject(((SketchLine)selections[0].entity).EndSketchPoint,((SketchLine)selections[1].entity).StartSketchPoint);
                string expectedType=scenario switch {"smooth"=>"kSmoothConstraintObject","coincident_point_curve"=>"kCoincidentConstraintObject","horizontal_align"=>"kHorizontalAlignConstraintObject","vertical_align"=>"kVerticalAlignConstraintObject",_=>""};
                if((scenario=="coincident" ? !joined : added.Length==0) || (expectedType!="" && !added.Any(c=>(string?)c["native_type"]==expectedType))
                    || (bool)after["partial"]! || sketch.ConstraintStatus==ConstraintStatusEnum.kOverConstrainedConstraintStatus)
                    throw new InvalidOperationException("Constraint commit failed native solver/identity checks.");
                if(kind=="ground") foreign=(JObject)refs[0]!.DeepClone();
                foreach(var constraint in added)
                {
                    if((string?)constraint["binding_state"]!="complete" || constraint["related_entities"] is not JArray bindings || bindings.Count==0
                        || constraint["annotation_mm"] is not JArray annotation || annotation.Count!=2)
                        throw new InvalidOperationException("Native constraint lacks its associations or annotation anchor.");
                    foreach(JObject input in refs)
                        if(!bindings.Any(b=>(string?)b["entity_id"]==(string?)input["entity_id"] && (string?)b["type"]==(string?)input["type"]
                            && ((string?)b["subpart"]==(string?)input["subpart"] || ((string?)input["type"]=="point" && (string?)b["subpart"]=="Entity"))))
                            throw new InvalidOperationException("Native constraint associations do not include its ordered input geometry/subparts.");
                    if(((JArray)ghost["geometric_constraints"]!).Any(c=>c["id"]!=null || c["related_entities"]!=null))
                        throw new InvalidOperationException("Preview constraint exported a tentative selectable identity.");
                }
                int deletionChecks=0;
                foreach(var target in added.Where(c=>(bool?)c["deletable"]==true))
                {
                    var deleteBefore=Snapshot();string deleteRevision=journal.Revision(documentId),targetId=(string)target["id"]!;
                    var reference=new JObject {["document_id"]=documentId,["revision"]=deleteRevision,["sketch_id"]=sketchId,
                        ["entity_id"]=targetId,["type"]="constraint",["subpart"]="Entity"};
                    var deleteOp=new JObject {["command"]="edit_sketch",["arguments"]=new JObject {["sketch_id"]=sketchId,["variant"]="constraint_delete",
                        ["options"]=new JObject {["references"]=new JArray(reference)}}};
                    JObject DeleteBatch(JObject operation,bool preview=false)
                    {var args=State();args["operations"]=new JArray(operation.DeepClone());args["preview"]=preview;args["include_preview_mesh"]=preview;return args;}
                    void RefuseDelete(JObject operation)
                    {
                        if(commands["atomic_batch"].Execute(context,DeleteBatch(operation)).Ok
                            || journal.Revision(documentId)!=deleteRevision || !JToken.DeepEquals(deleteBefore,Snapshot()))
                            throw new InvalidOperationException("Invalid constraint deletion was accepted or changed native state.");
                    }
                    foreach(var bad in new[] {("revision","old"),("document_id","other"),("type","line"),("subpart","Start")})
                    {var invalid=(JObject)deleteOp.DeepClone();invalid["arguments"]!["options"]!["references"]![0]![bad.Item1]=bad.Item2;RefuseDelete(invalid);}
                    var extra=(JObject)deleteOp.DeepClone();extra["arguments"]!["options"]!["value"]=true;RefuseDelete(extra);
                    if(foreign!=null && (string?)foreign["sketch_id"]!=sketchId)
                    {
                        var owner=(JObject)deleteOp.DeepClone();owner["arguments"]!["sketch_id"]=foreign["sketch_id"]!.DeepClone();
                        owner["arguments"]!["options"]!["references"]![0]!["sketch_id"]=foreign["sketch_id"]!.DeepClone();RefuseDelete(owner);
                    }
                    JObject deleteOracle;
                    var directDelete=app.TransactionManager.StartTransaction((_Document)fixture,"S1 direct constraint deletion oracle, aborted");
                    try
                    {
                        var resolver=typeof(InventorCommandRegistry).Assembly.GetType("Bimwright.Ipt.Shared.Handlers.Experimental.SketchReferences",true)!;
                        var native=(GeometricConstraint)resolver.GetMethod("Resolve",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic)!
                            .Invoke(null,new object[] {fixture,targetId,"sketch_constraint"})!;
                        native.Delete();sketch.Solve();fixture.Update2(true);deleteOracle=Snapshot();
                    }
                    finally {directDelete.Abort();}
                    var restoredDelete=Snapshot();var deleteBaseline=(JObject)deleteBefore.DeepClone();
                    restoredDelete.Remove("revision");deleteBaseline.Remove("revision");
                    if(!JToken.DeepEquals(restoredDelete,deleteBaseline) || !journal.TryRestoreRevision(documentId,deleteRevision)
                        || !JToken.DeepEquals(deleteBefore,Snapshot())) throw new InvalidOperationException("Direct constraint deletion failed exact rollback.");
                    var deletePreview=Call("atomic_batch",DeleteBatch(deleteOp,true));
                    var deleteGhost=((JArray)deletePreview["preview_mesh"]!["sketches"]!).OfType<JObject>().Single(s=>(string?)s["sketch_name"]==sketch.Name);
                    var expectedGhost=(JArray)deleteOracle["entities"]!.DeepClone();foreach(JObject entity in expectedGhost) entity.Remove("id");
                    if(((JArray)deleteGhost["geometric_constraints"]!).Count!=((JArray)deleteBefore["constraints"]!).Count-1
                        || !JToken.DeepEquals(expectedGhost,deleteGhost["native_entities"])
                        || journal.Revision(documentId)!=deleteRevision || !JToken.DeepEquals(deleteBefore,Snapshot()))
                        throw new InvalidOperationException("Constraint deletion preview was missing or failed exact rollback.");
                    Call("atomic_batch",DeleteBatch(deleteOp));var deleted=Snapshot();
                    var expected=(JObject)deleteOracle.DeepClone();var actual=(JObject)deleted.DeepClone();expected.Remove("revision");actual.Remove("revision");
                    if(!JToken.DeepEquals(expected,actual) || (bool)deleted["partial"]!
                        || journal.Revision(documentId)==deleteRevision)
                    {
                        row["deletion_before"]=deleteBefore;row["deletion_after"]=deleted;
                        row["deletion_target"]=targetId;
                        row["deletion_oracle"]=deleteOracle;
                        throw new InvalidOperationException("Constraint deletion differs from the direct native API or failed to advance revision.");
                    }
                    if(!SameDimensionGeometry((JArray)deleteBefore["entities"]!,(JArray)deleted["entities"]!))
                        row["native_deletion_geometry_changes"]=(int?)row["native_deletion_geometry_changes"]+1 ?? 1;
                    deletionChecks++;
                }
                var completed=new JObject {["native_types"]=new JArray(added.Select(c=>c["native_type"])),["constraint_status"]=after["constraint_status"],
                    ["joined_native_points"]=joined,["association_checks"]=added.Length,["deletion_checks"]=deletionChecks,["outcome"]="PASS ordered native references and associations, preview snapshot/revision rollback and committed native constraint/topology; deletable additions targeted with exact rollback and guards"};
                foreach(var property in completed.Properties()) row[property.Name]=property.Value.DeepClone();
            }
            catch(Exception ex) {throw new InvalidOperationException("Native constraint "+kind+" failed.",ex);}
            if(kind!="ground")
            {
                var cleanup=app.TransactionManager.StartTransaction((_Document)fixture,"S1 completed constraint fixture cleanup");
                try {sketch.Delete();cleanup.End();} catch {cleanup.Abort();throw;}
            }
        }
        return rows;
    }

    private static bool SameComObject(object one,object two)
    {
        IntPtr a=Marshal.GetIUnknownForObject(one),b=Marshal.GetIUnknownForObject(two);
        try {return a==b;} finally {Marshal.Release(a);Marshal.Release(b);}
    }

    private static JArray ProbeFormat(global::Inventor.Application app,PartDocument fixture,
        IReadOnlyDictionary<string,IInventorCommand> commands,InventorCommandContext context,CadEventJournal journal)
    {
        string documentId="doc_"+fixture.InternalName;
        JObject State()=>new JObject {["document_id"]=documentId,["expected_revision"]=journal.Revision(documentId)};
        JObject Call(string name,JObject args)
        { var result=commands[name].Execute(context,args); if(!result.Ok) throw new InvalidOperationException(name+": "+result.Error?.Message); return (JObject)result.Data!; }
        var sketch=fixture.ComponentDefinition.Sketches[1];
        string sketchId=(string)Call("get_design_context_xr",State())["sketches"]![0]!["sketch_id"]!;
        JObject Snapshot() { var args=State(); args["sketch_id"]=sketchId; return Call("get_sketch_editor_xr",args); }
        JObject Reference(JObject item)=>new JObject {["document_id"]=documentId,["revision"]=journal.Revision(documentId),
            ["sketch_id"]=sketchId,["entity_id"]=item["id"],["type"]=item["type"],["subpart"]="Entity"};
        JObject Operation(string variant,JArray refs,bool value)=>new JObject {["command"]="edit_sketch",["arguments"]=new JObject
            {["sketch_id"]=sketchId,["variant"]=variant,["options"]=new JObject {["references"]=refs,["value"]=value}}};
        var rows=new JArray();
        foreach(var definition in new[] {("format_construction","construction","line"),("format_centerline","centerline","line"),("format_hole_center","hole_center","point")})
            foreach(bool value in new[] {true,false})
            {
                var snapshot=Snapshot(); var entities=(JArray)snapshot["entities"]!;
                var selected=entities.OfType<JObject>().First(e=>(string?)e["type"]==definition.Item3);
                var refs=new JArray(Reference(selected));
                // Construction exercises a mixed selection, owned by one sketch and one transaction.
                if(definition.Item1=="format_construction") refs.Add(Reference(entities.OfType<JObject>().First(e=>(string?)e["type"]=="circle")));
                var property=definition.Item2;
                var before=refs.OfType<JObject>().Select(r=>entities.OfType<JObject>().Single(e=>(string?)e["id"]==(string?)r["entity_id"]))
                    .Select(e=>(bool)e[property]!).ToArray();
                int entityCount=sketch.SketchEntities.Count,constraintCount=sketch.GeometricConstraints.Count;
                string revision=journal.Revision(documentId);
                var op=Operation(definition.Item1,refs,value); var preview=State(); preview["preview"]=true;
                preview["include_preview_mesh"]=true; preview["operations"]=new JArray(op.DeepClone());
                var ghost=Call("atomic_batch",preview);
                var nativeGhost=(JArray)ghost["preview_mesh"]!["sketches"]![0]!["native_entities"]!;
                // Preview has no opaque IDs: the selected first line/point and first circle have matching native types.
                if((bool)nativeGhost.OfType<JObject>().First(e=>(string?)e["type"]==definition.Item3)[property]! !=value
                    || (definition.Item1=="format_construction" && (bool)nativeGhost.OfType<JObject>().First(e=>(string?)e["type"]=="circle")[property]! !=value))
                    throw new InvalidOperationException("Native format preview did not capture the requested property.");
                var restored=(JArray)Snapshot()["entities"]!;
                for(int i=0;i<refs.Count;i++) if((bool)restored.OfType<JObject>().Single(e=>(string?)e["id"]==(string?)refs[i]!["entity_id"])[property]! !=before[i])
                    throw new InvalidOperationException("Native format preview left a property changed.");
                if(journal.Revision(documentId)!=revision || sketch.SketchEntities.Count!=entityCount || sketch.GeometricConstraints.Count!=constraintCount)
                    throw new InvalidOperationException("Format preview changed revision, geometry or constraints.");
                var commit=State(); commit["operations"]=new JArray(op.DeepClone()); Call("atomic_batch",commit);
                var after=(JArray)Snapshot()["entities"]!;
                foreach(JObject reference in refs) if((bool)after.OfType<JObject>().Single(e=>(string?)e["id"]==(string?)reference["entity_id"])[property]! !=value)
                    throw new InvalidOperationException("Native format commit did not retain the requested property.");
                if(sketch.SketchEntities.Count!=entityCount || sketch.GeometricConstraints.Count!=constraintCount)
                    throw new InvalidOperationException("Format recreated native geometry or constraints.");
                rows.Add(new JObject {["variant"]=definition.Item1,["value"]=value,["outcome"]="PASS native property, preview rollback, commit and original entities retained"});
            }
        var original=(JArray)Snapshot()["entities"]!; var line=original.OfType<JObject>().First(e=>(string?)e["type"]=="line");
        var stale=Reference(line); stale["revision"]="obsolete";
        foreach(var invalid in new[] {Operation("format_construction",new JArray(stale),true),Operation("format_hole_center",new JArray(Reference(line)),true)})
        {
            var args=State(); args["operations"]=new JArray(invalid); args["preview"]=true;
            if(commands["atomic_batch"].Execute(context,args).Ok) throw new InvalidOperationException("Invalid format reference or type was accepted.");
        }
        var otherTx=app.TransactionManager.StartTransaction((_Document)fixture,"S1 separate sketch ownership fixture");
        var other=fixture.ComponentDefinition.Sketches.Add(fixture.ComponentDefinition.WorkPlanes[3]);
        other.Name="S1_OtherOwner"; other.SketchLines.AddByTwoPoints(app.TransientGeometry.CreatePoint2d(0,0),app.TransientGeometry.CreatePoint2d(1,1)); otherTx.End();
        var design=Call("get_design_context_xr",State()); var otherId=(string)((JArray)design["sketches"]!).OfType<JObject>().Single(s=>(string?)s["name"]==other.Name)["sketch_id"]!;
        var otherArgs=State(); otherArgs["sketch_id"]=otherId; var foreign=(JObject)Call("get_sketch_editor_xr",otherArgs)["entities"]![0]!;
        // Forge the container id while keeping the native key: COM ownership must still refuse it.
        var wrongOwner=Reference(foreign); var refused=State(); refused["operations"]=new JArray(Operation("format_construction",new JArray(wrongOwner),true));
        if(commands["atomic_batch"].Execute(context,refused).Ok) throw new InvalidOperationException("Format accepted an entity owned by another sketch.");
        rows.Add(new JObject {["variant"]="format_guards",["outcome"]="PASS stale revision, incompatible type and forged sketch ownership refused"});
        return rows;
    }

    private static JObject ProbeGeometry(global::Inventor.Application app, PlanarSketch sketch)
    {
        var g = app.TransientGeometry;
        sketch.SketchLines.AddByTwoPoints(g.CreatePoint2d(0, 0), g.CreatePoint2d(4, 0));
        sketch.SketchCircles.AddByCenterRadius(g.CreatePoint2d(2, 2), 1);
        sketch.SketchArcs.AddByCenterStartEndPoint(g.CreatePoint2d(6, 2), g.CreatePoint2d(7, 2), g.CreatePoint2d(6, 3), true);
        sketch.SketchEllipses.Add(g.CreatePoint2d(10, 2), g.CreateUnitVector2d(1, 0), 2, 1);
        var points = app.TransientObjects.CreateObjectCollection();
        points.Add(g.CreatePoint2d(0, 5)); points.Add(g.CreatePoint2d(2, 7)); points.Add(g.CreatePoint2d(4, 5));
        sketch.SketchSplines.Add(points);
        sketch.TextBoxes.AddFitted(g.CreatePoint2d(0, 10), "S1 native oracle");
        return new JObject
        {
            ["lines"] = sketch.SketchLines.Count, ["circles"] = sketch.SketchCircles.Count,
            ["arcs"] = sketch.SketchArcs.Count, ["ellipses"] = sketch.SketchEllipses.Count,
            ["splines"] = sketch.SketchSplines.Count, ["texts"] = sketch.TextBoxes.Count,
            ["circle_radius_mm"] = sketch.SketchCircles[1].Radius * 10,
            ["constraint_status"] = sketch.ConstraintStatus.ToString()
        };
    }

    private static JArray ProbeIncremental(global::Inventor.Application app, PartDocument fixture,
        IReadOnlyDictionary<string, IInventorCommand> commands, InventorCommandContext context, CadEventJournal journal)
    {
        var g = app.TransientGeometry;
        var seed = app.TransactionManager.StartTransaction((_Document)fixture, "S1 dedicated incremental seed");
        var sketch = fixture.ComponentDefinition.Sketches.Add(fixture.ComponentDefinition.WorkPlanes[3]);
        sketch.Name = "S1_Incremental";
        var originalLine = sketch.SketchLines.AddByTwoPoints(g.CreatePoint2d(0, 0), g.CreatePoint2d(4, 0));
        seed.End();
        string documentId = "doc_" + fixture.InternalName;
        JObject Call(string name, JObject arguments)
        {
            var result = commands[name].Execute(context, arguments);
            if (!result.Ok) throw new InvalidOperationException(name + ": " + result.Error?.Code + " " + result.Error?.Message);
            return (JObject)result.Data!;
        }
        JObject State() => new JObject { ["document_id"] = documentId, ["expected_revision"] = journal.Revision(documentId) };
        var design = Call("get_design_context_xr", State());
        string sketchId = (string)design["sketches"]![0]!["sketch_id"]!;
        var rows = new JArray();
        var tests = new[]
        {
            ("line", new JObject { ["points_mm"] = new JArray(new JArray(0, 20), new JArray(40, 20)) }),
            ("rectangle_two_points", new JObject { ["points_mm"] = new JArray(new JArray(100, 100), new JArray(140, 130)) }),
            ("rectangle_three_points", new JObject { ["points_mm"] = new JArray(new JArray(1500, 100), new JArray(1540, 120), new JArray(1530, 140)) }),
            ("rectangle_center_two_points", new JObject { ["points_mm"] = new JArray(new JArray(1700, 100), new JArray(1720, 115)) }),
            ("rectangle_center_three_points", new JObject { ["points_mm"] = new JArray(new JArray(1900, 100), new JArray(1920, 110), new JArray(1910, 130)) }),
            ("polygon", new JObject { ["points_mm"] = new JArray(new JArray(2100, 100), new JArray(2120, 100)), ["sides"]=5, ["inscribed"]=true }),
            ("polygon", new JObject { ["points_mm"] = new JArray(new JArray(2300, 100), new JArray(2320, 100)), ["sides"]=6, ["inscribed"]=false }),
            ("circle_center", new JObject { ["points_mm"] = new JArray { new JArray(200, 100) }, ["radius_mm"] = 10 }),
            ("point", new JObject { ["points_mm"] = new JArray { new JArray(210, 130) }, ["hole_center"] = true }),
            ("arc_three_points", new JObject { ["points_mm"] = new JArray(new JArray(300, 100), new JArray(310, 110), new JArray(320, 100)) }),
            ("arc_center", new JObject { ["points_mm"] = new JArray { new JArray(400, 100) }, ["radius_mm"] = 10, ["start_deg"] = 0, ["sweep_deg"] = -90 }),
            ("ellipse", new JObject { ["points_mm"] = new JArray { new JArray(500, 100) }, ["major_radius_mm"] = 20, ["minor_radius_mm"] = 10, ["rotation_deg"] = 30 }),
            ("spline_fit", new JObject { ["points_mm"] = new JArray(new JArray(600, 100), new JArray(610, 110), new JArray(620, 100)) }),
            ("spline_control", new JObject { ["points_mm"] = new JArray(new JArray(700, 100), new JArray(710, 110), new JArray(720, 100)) }),
            ("slot_center_to_center", new JObject { ["points_mm"] = new JArray(new JArray(800, 100), new JArray(840, 100)), ["width_mm"] = 10 }),
            ("slot_overall", new JObject { ["points_mm"] = new JArray(new JArray(900, 100), new JArray(940, 100)), ["width_mm"] = 10 }),
            ("slot_center", new JObject { ["points_mm"] = new JArray(new JArray(1000, 100), new JArray(1020, 100)), ["width_mm"] = 10 }),
            ("slot_three_point_arc", new JObject { ["points_mm"] = new JArray(new JArray(1100, 100), new JArray(1120, 120), new JArray(1140, 100)), ["width_mm"] = 5 }),
            ("slot_center_arc", new JObject { ["points_mm"] = new JArray(new JArray(1200, 100), new JArray(1220, 100)), ["sweep_deg"] = 90, ["width_mm"] = 5 }),
        };
        foreach (var (variant, options) in tests)
        {
            Console.WriteLine("Native preview case: " + variant);
            var operation = new JObject { ["command"] = "edit_sketch", ["arguments"] = new JObject
                { ["sketch_id"] = sketchId, ["variant"] = variant, ["options"] = options } };
            int count = sketch.SketchEntities.Count; string revision = journal.Revision(documentId);
            var preview = State(); preview["operations"] = new JArray(operation.DeepClone()); preview["preview"] = true; preview["include_preview_mesh"] = true;
            var ghost = Call("atomic_batch", preview);
            if ((string?)ghost["status"] != "preview_rolled_back" || sketch.SketchEntities.Count != count || journal.Revision(documentId) != revision)
                throw new InvalidOperationException(variant + " preview did not restore native geometry/revision.");
            var ghostEntities = ghost["preview_mesh"]?["sketches"]?[0]?["native_entities"] as JArray;
            if (ghostEntities == null || ghostEntities.Count <= count || ghostEntities.Descendants().OfType<JProperty>().Any(p => p.Name == "id"))
                throw new InvalidOperationException("Native preview omitted tentative geometry or exported tentative identities.");
            var commit = State(); commit["operations"] = new JArray(operation.DeepClone());
            var applied = Call("atomic_batch", commit);
            if ((string?)applied["status"] != "committed" || sketch.SketchEntities.Count <= count || fixture.ComponentDefinition.Sketches.Count != 1
                || originalLine.StartSketchPoint.Geometry.X != 0 || originalLine.EndSketchPoint.Geometry.X != 4)
                throw new InvalidOperationException(variant + " recreated or corrupted existing native geometry.");
            var snapshotArgs = State(); snapshotArgs["sketch_id"] = sketchId;
            var snapshot = Call("get_sketch_editor_xr", snapshotArgs);
            if(variant=="line")
                foreach(string id in new[] {sketchId,(string)snapshot["entities"]![0]!["id"]!})
                {
                    var resolved=Call("resolve_entity",new JObject {["entity_id"]=id});
                    if((string?)resolved["status"]!="resolved" || (string?)resolved["document_id"]!=documentId)
                        throw new InvalidOperationException("Generic entity resolver failed for a context-free sketch reference.");
                }
            if((bool?)snapshot["partial"]!=false) throw new InvalidOperationException("Native creation snapshot is partial: "+
                string.Join("; ",((JArray)snapshot["entities"]!).OfType<JObject>().Where(e=>(bool?)e["supported"]!=true)
                    .Select(e=>(string?)e["type"]+": "+(string?)e["unavailable_reason"])));
            var resolver=commands["get_sketch_editor_xr"].GetType().Assembly
                .GetType("Bimwright.Ipt.Shared.Handlers.Experimental.SketchReferences")!.GetMethod("Resolve")!;
            int checkedAnchors=0;
            foreach(var item in ((JArray)snapshot["entities"]!).OfType<JObject>())
            {
                if((string?)item["type"] is "text" or "image") continue;
                object native=resolver.Invoke(null,new object[] {(Document)fixture,(string)item["id"]!,"sketch_entity"})!;
                void Anchor(string field,Point2d? expected)
                {
                    if(expected==null) return;
                    if(item[field] is not JArray actual || actual.Count!=2
                        || Math.Abs((double)actual[0]-expected.X*10)>1e-7 || Math.Abs((double)actual[1]-expected.Y*10)>1e-7)
                        throw new InvalidOperationException("Opaque native reference has an incorrect "+field+" anchor.");
                    checkedAnchors++;
                }
                switch(native)
                {
                    case SketchLine line: Anchor("start_mm",line.StartSketchPoint.Geometry); Anchor("end_mm",line.EndSketchPoint.Geometry); break;
                    case SketchArc arc: Anchor("start_mm",arc.StartSketchPoint.Geometry); Anchor("end_mm",arc.EndSketchPoint.Geometry); Anchor("center_mm",arc.CenterSketchPoint.Geometry); break;
                    case SketchCircle circle: Anchor("center_mm",circle.CenterSketchPoint.Geometry); break;
                    case SketchEllipse ellipse: Anchor("center_mm",ellipse.CenterSketchPoint.Geometry); break;
                    case SketchSpline spline: Anchor("start_mm",spline.StartSketchPoint?.Geometry); Anchor("end_mm",spline.EndSketchPoint?.Geometry); break;
                    case SketchControlPointSpline spline: Anchor("start_mm",spline.StartSketchPoint?.Geometry); Anchor("end_mm",spline.EndSketchPoint?.Geometry); break;
                    case SketchPoint point: Anchor("point_mm",point.Geometry); break;
                }
            }
            rows.Add(new JObject { ["variant"] = variant, ["outcome"] = "PASS native creation, preview rollback, incremental commit and original geometry retained",
                ["added_entities"] = sketch.SketchEntities.Count - count, ["snapshot_partial"] = snapshot["partial"], ["opaque_subpart_anchors_checked"]=checkedAnchors });
        }
        var direct = commands["edit_sketch"].Execute(context, new JObject { ["sketch_id"] = sketchId, ["variant"] = "line", ["options"] = tests[0].Item2 });
        if (direct.Ok || direct.Error?.Code != "ATOMIC_REQUIRED") throw new InvalidOperationException("Direct sketch mutation was not refused.");
        int beforeFailure = sketch.SketchEntities.Count, constraintsBefore = sketch.GeometricConstraints.Count;
        string revisionBefore = journal.Revision(documentId), solverBefore = sketch.ConstraintStatus.ToString();
        var failure = State(); failure["operations"] = new JArray(
            new JObject { ["command"] = "edit_sketch", ["arguments"] = new JObject
                { ["sketch_id"] = sketchId, ["variant"] = "line", ["options"] = tests[0].Item2.DeepClone() } },
            new JObject { ["command"] = "edit_sketch", ["arguments"] = new JObject
                { ["sketch_id"] = sketchId, ["variant"] = "circle_center", ["options"] = new JObject
                    { ["points_mm"] = new JArray { new JArray(2000,2000) }, ["radius_mm"] = -10 } } });
        var rejected = commands["atomic_batch"].Execute(context, failure);
        if (rejected.Ok || sketch.SketchEntities.Count != beforeFailure || sketch.GeometricConstraints.Count != constraintsBefore
            || sketch.ConstraintStatus.ToString() != solverBefore || journal.Revision(documentId) != revisionBefore)
            throw new InvalidOperationException("Failure of the second semantic operation did not restore the complete batch.");
        var stale = State(); stale["sketch_id"] = sketchId; stale["expected_revision"] = "invalid-old-revision";
        var staleResult = commands["get_sketch_editor_xr"].Execute(context, stale);
        if (staleResult.Ok || staleResult.Error?.Code != "STALE_REVISION") throw new InvalidOperationException("Stale native snapshot was not refused.");
        rows.Add(new JObject { ["variant"] = "failure_and_stale_guards", ["outcome"] = "PASS second-operation failure rolls back entities, constraints, solver and revision; stale snapshot refused; direct mutation refused" });
        return rows;
    }

    private static JArray InteropSignatures()
    {
        var assembly = typeof(PlanarSketch).Assembly;
        return new JArray(assembly.GetTypes().Where(t => t.IsInterface &&
            (t.Name.StartsWith("Sketch", StringComparison.Ordinal) || t.Name is "PlanarSketch" or "TextBoxes" or "SketchImages"))
            .Select(t => new JObject { ["type"] = t.Name,
                ["members"] = new JArray(t.GetMethods().Where(m => !m.IsSpecialName).Select(m => m.ToString())) }));
    }

    private static JArray ReadRibbon(global::Inventor.Application app)
    {
        var result = new JArray();
        foreach (string tabName in new[] { "id_TabSketch", "id_TabSketch_Exit" })
        {
            dynamic tab = app.UserInterfaceManager.Ribbons["Part"].RibbonTabs[tabName];
            var panels = new JArray();
            result.Add(new JObject { ["id"] = tabName, ["panels"] = panels });
            foreach (dynamic panel in tab.RibbonPanels)
            {
                var controls = new JArray();
                panels.Add(new JObject { ["id"] = (string)panel.InternalName, ["label"] = (string)panel.DisplayName, ["controls"] = controls });
                foreach (dynamic control in panel.CommandControls) controls.Add(ReadControl(control, 0));
            }
        }
        return result;
    }

    private static JObject ReadControl(dynamic control, int depth)
    {
        var row = new JObject { ["id"] = (string)control.InternalName, ["visible"] = (bool)control.Visible };
        try
        {
            dynamic definition = control.ControlDefinition;
            row["command"] = new JObject { ["id"] = (string)definition.InternalName, ["label"] = (string)definition.DisplayName,
                ["enabled"] = (bool)definition.Enabled, ["description"] = (string)definition.DescriptionText };
        }
        catch (Exception ex) { row["definition_unavailable"] = ex.GetType().Name; }
        if (depth < 5)
        {
            try
            {
                var children = new JArray();
                foreach (dynamic child in control.ChildControls) children.Add(ReadControl(child, depth + 1));
                row["children"] = children;
            }
            catch (Exception ex) { row["children_unavailable"] = ex.GetType().Name; }
        }
        return row;
    }
}
