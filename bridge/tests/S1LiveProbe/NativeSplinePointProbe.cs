using Inventor;
using Newtonsoft.Json.Linq;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;
using Bimwright.Ipt.Shared.Plugin;

internal static partial class Program
{
    private static void RunSplinePointBatches(Inventor.Application app,PartDocument doc,JObject report,bool mixedPolygon=false,bool verifyReadOnlyPartial=false,bool allFitMethods=false)
    {
        if(app.ActiveEditObject is PlanarSketch editing) editing.ExitEdit();
        var trackerType=typeof(InventorCommandRegistry).Assembly.GetType("Bimwright.Ipt.Shared.Plugin.CadEventTracker",true)!;
        using var tracker=(IDisposable)Activator.CreateInstance(trackerType,app)!;
        var journal=(CadEventJournal)trackerType.GetProperty("Journal")!.GetValue(tracker)!;
        var commands=InventorCommandRegistry.Build(new PluginOptions(2027,false,false,0));
        var context=new InventorCommandContext {Application=app,Events=journal,Commands=commands,AllowExperimental=true,InventorYear=2027};
        string documentId="doc_"+doc.InternalName;var rows=new JArray();report["batch_cases"]=rows;
        report["kind"]=doc.ComponentDefinition is SheetMetalComponentDefinition ? "sheetmetal" : "part";
        report["mixed_polygon"]=mixedPolygon;
        report["verify_readonly_partial"]=verifyReadOnlyPartial;
        var desktopPreference=app.SketchOptions.SplineFitMethod;
        report["desktop_preference_fit_method"]=(int)desktopPreference;
        report["explicit_fit_method_matrix"]=allFitMethods;
        var refusals=new JArray();report["guard_cases"]=refusals;
        report["evidence"]="Native API versus atomic_batch variable point splines; geometry/constraints, preview rollback and commit. Not desktop inference, Quest or physical.";
        JObject State()=>new() {["document_id"]=documentId,["expected_revision"]=journal.Revision(documentId)};
        JObject Call(string name,JObject args)
        {var result=commands[name].Execute(context,args);if(!result.Ok) throw new InvalidOperationException(name+": "+result.Error?.Code+" "+result.Error?.Message);return (JObject)result.Data!;}
        JObject Normalize(JObject snapshot)
        {var copy=(JObject)snapshot.DeepClone();copy.Remove("revision");foreach(var item in copy.Descendants().OfType<JObject>()) {item.Remove("id");item.Remove("entity_id");}return copy;}
        foreach(string variant in allFitMethods ? new[] {"spline_fit"} : new[] {"spline_fit","spline_control"})
        foreach(var fitMethod in allFitMethods ? new[] {SplineFitMethodEnum.kSmoothSplineFit,SplineFitMethodEnum.kSweetSplineFit,SplineFitMethodEnum.kACADSplineFit} : new[] {desktopPreference})
        foreach(int count in mixedPolygon ? new[] {3,5} : allFitMethods ? new[] {2,3,5} : new[] {2,3,4,5,64})
        foreach(double degrees in new[] {0d,53.13010235415598})
        {
            var sketch=doc.ComponentDefinition.Sketches.Add(doc.ComponentDefinition.WorkPlanes[3]);sketch.Name="S1_SplinePoints_"+doc.ComponentDefinition.Sketches.Count;
            if(mixedPolygon)
            {
                var setup=app.TransactionManager.StartTransaction((_Document)doc,"S1 mixed polygon spline setup");
                try {sketch.SketchLines.AddAsPolygon(3,app.TransientGeometry.CreatePoint2d(-10,-10),app.TransientGeometry.CreatePoint2d(-8,-10),true);sketch.Solve();doc.Update2(true);setup.End();}
                catch {setup.Abort();throw;}
            }
            string sketchId=(string)((JArray)Call("get_design_context_xr",State())["sketches"]!).Single(s=>(string?)s["name"]==sketch.Name)["sketch_id"]!;
            JObject Snapshot(){var args=State();args["sketch_id"]=sketchId;return Call("get_sketch_editor_xr",args);}
            var before=Snapshot();string revision=journal.Revision(documentId);double angle=degrees*Math.PI/180;
            var points=new JArray();
            for(int i=0;i<count;i++) {double x=10*i,y=10*Math.Sin(i*.7);points.Add(new JArray(13+x*Math.Cos(angle)-y*Math.Sin(angle),21+x*Math.Sin(angle)+y*Math.Cos(angle)));}
            var operation=new JObject {["command"]="edit_sketch",["arguments"]=new JObject {["sketch_id"]=sketchId,["variant"]=variant,["options"]=new JObject {["points_mm"]=points}}};
            if(variant=="spline_fit") operation["arguments"]!["options"]!["fit_method"]=(int)fitMethod;
            JObject Batch(bool preview){var args=State();args["operations"]=new JArray(operation.DeepClone());args["preview"]=preview;args["include_preview_mesh"]=preview;return args;}
            if(!mixedPolygon && !allFitMethods && count==2 && degrees==0)
            {
                var invalids=new System.Collections.Generic.List<(string,JObject)>();
                foreach(int invalidCount in new[] {0,1,65})
                {
                    var invalid=(JObject)operation.DeepClone();var invalidPoints=new JArray();
                    for(int i=0;i<invalidCount;i++) invalidPoints.Add(new JArray(i,Math.Sin(i)));
                    invalid["arguments"]!["options"]!["points_mm"]=invalidPoints;invalids.Add(("point_count_"+invalidCount,invalid));
                }
                if(variant=="spline_fit")
                {
                    foreach(string invalidMethod in new[] {"missing","string","unknown"})
                    {
                        var invalid=(JObject)operation.DeepClone();var opts=(JObject)invalid["arguments"]!["options"]!;
                        if(invalidMethod=="missing") opts.Remove("fit_method");
                        else opts["fit_method"]=invalidMethod=="string" ? new JValue("26369") : new JValue(26372);
                        invalids.Add(("fit_method_"+invalidMethod,invalid));
                    }
                }
                foreach(var (name,invalid) in invalids)
                {
                    var args=State();args["operations"]=new JArray(invalid);args["preview"]=true;args["include_preview_mesh"]=true;
                    var refusal=commands["atomic_batch"].Execute(context,args);
                    if(refusal.Ok || refusal.Error?.Code!=InventorErrorCodes.ROLLED_BACK || journal.Revision(documentId)!=revision
                        || !JToken.DeepEquals(before,Snapshot())) throw new InvalidOperationException("Spline guard failed: "+name);
                    refusals.Add(new JObject {["variant"]=variant,["invalid"]=name,["code"]=refusal.Error?.Code,["outcome"]="PASS refusal and exact snapshot/revision rollback"});
                }
            }
            double maxPointErrorMm=0;
            // Record the direct solver's input displacement; compare its actual
            // points exactly against the batch rather than weakening a tolerance.
            JArray CheckPoints()
            {
                var actualPoints=new JArray();
                bool fit=variant=="spline_fit";
                if((fit ? sketch.SketchSplines.Count : sketch.SketchControlPointSplines.Count)!=1) throw new InvalidOperationException("Spline native entity count differs.");
                dynamic spline=fit ? (object)sketch.SketchSplines[1] : sketch.SketchControlPointSplines[1];
                if(fit && (SplineFitMethodEnum)spline.FitMethod!=fitMethod) throw new InvalidOperationException("Spline method differs from the explicitly requested method.");
                int actual=fit ? spline.FitPointCount : spline.ControlPointCount;
                if(actual!=count) throw new InvalidOperationException("Spline native point count differs.");
                for(int i=0;i<count;i++)
                {
                    SketchPoint point=fit ? spline.FitPoint[i+1] : spline.ControlPoint[i+1];
                    maxPointErrorMm=Math.Max(maxPointErrorMm,Math.Max(Math.Abs(point.Geometry.X*10-(double)points[i][0]!),Math.Abs(point.Geometry.Y*10-(double)points[i][1]!)));
                    actualPoints.Add(new JArray(point.Geometry.X*10,point.Geometry.Y*10));
                }
                return actualPoints;
            }
            bool readOnlyPartial=false;
            JObject oracle;JArray oraclePoints;var transaction=app.TransactionManager.StartTransaction((_Document)doc,"S1 direct spline point oracle, aborted");
            try
            {
                var collection=app.TransientObjects.CreateObjectCollection();
                foreach(var point in points) collection.Add(app.TransientGeometry.CreatePoint2d((double)point[0]!/10,(double)point[1]!/10));
                if(variant=="spline_fit") sketch.SketchSplines.Add(collection,fitMethod);else sketch.SketchControlPointSplines.Add(collection);
                sketch.Solve();doc.Update2(true);oraclePoints=CheckPoints();oracle=Snapshot();
                if((bool)oracle["partial"]!)
                {
                    if(mixedPolygon && verifyReadOnlyPartial && variant=="spline_control")
                    {
                        var nativeEntities=((JArray)oracle["entities"]!).OfType<JObject>().ToArray();
                        var nativePatterns=((JArray)oracle["constraints"]!).OfType<JObject>().Where(c=>(string?)c["native_type"]=="kPatternConstraintObject").ToArray();
                        var cv=nativeEntities.Single(e=>(string?)e["type"]=="control_point_spline");
                        if(nativeEntities.Any(e=>(bool?)e["supported"]!=true) || cv["strokes_mm"] is not JArray strokes || strokes.Count<4
                            || nativePatterns.Length==0 || nativePatterns.Any(c=>(bool?)c["supported"]!=true || (string?)c["binding_state"]!="unavailable"
                                || string.IsNullOrWhiteSpace((string?)c["binding_reason"]) || c["related_entities"]!=null)
                            || !nativeEntities.Any(e=>(string?)e["pattern_membership_state"]=="unavailable" && e["pattern_membership_reason"]!=null))
                            throw new InvalidOperationException("Read-only partial snapshot lost valid geometry or advertised incomplete pattern bindings as complete.");
                        readOnlyPartial=true;
                    }
                    else
                    {
                    report["partial_oracle"]=oracle;var diagnostic=new JArray();report["constraints_diagnostic"]=diagnostic;
                    foreach(SketchEntity entity in sketch.SketchEntities)
                    {
                        var row=new JObject {["type"]=entity.Type.ToString()};diagnostic.Add(row);
                        try {row["count"]=entity.Constraints.Count;var types=new JArray();row["constraint_types"]=types;foreach(object attached in entity.Constraints) types.Add(attached.GetType().FullName);}
                        catch(Exception ex) {row["error"]=ex.ToString();}
                    }
                    throw new InvalidOperationException("Spline snapshot is partial.");
                    }
                }
            }
            finally {transaction.Abort();}
            if(!JToken.DeepEquals(Normalize(before),Normalize(Snapshot())) || !journal.TryRestoreRevision(documentId,revision)) throw new InvalidOperationException("Spline oracle rollback failed.");
            if(readOnlyPartial)
            {
                var refusal=commands["atomic_batch"].Execute(context,Batch(true));
                if(refusal.Ok || refusal.Error?.Code!=InventorErrorCodes.ROLLED_BACK
                    || journal.Revision(documentId)!=revision || !JToken.DeepEquals(before,Snapshot()))
                    throw new InvalidOperationException("Partial CV preview was not refused with exact snapshot/revision rollback.");
                rows.Add(new JObject {["variant"]=variant,["rotation_deg"]=degrees,["point_count"]=count,["snapshot"]=oracle,
                    ["preview_refusal"]=new JObject {["code"]=refusal.Error?.Code,["message"]=refusal.Error?.Message},
                    ["outcome"]="PASS read-only partial snapshot: valid CV geometry retained, pattern associations explicitly unavailable, preview refused with exact rollback; no CV commit verified"});
                var partialCleanup=app.TransactionManager.StartTransaction((_Document)doc,"S1 partial spline cleanup");try {sketch.Delete();partialCleanup.End();}catch {partialCleanup.Abort();throw;}
                continue;
            }
            var preview=Call("atomic_batch",Batch(true));
            if(journal.Revision(documentId)!=revision || !JToken.DeepEquals(before,Snapshot())) throw new InvalidOperationException("Spline preview mutated fixture.");
            var ghost=((JArray)preview["preview_mesh"]!["sketches"]!).Single(s=>(string?)s["sketch_name"]==sketch.Name);
            var expected=(JArray)oracle["entities"]!.DeepClone();foreach(JObject entity in expected) entity.Remove("id");
            if((bool)ghost["partial"]! || !JToken.DeepEquals(expected,ghost["native_entities"])) throw new InvalidOperationException("Spline preview differs from native API.");
            Call("atomic_batch",Batch(false));var after=Snapshot();var committedPoints=CheckPoints();
            if(!JToken.DeepEquals(oraclePoints,committedPoints)) throw new InvalidOperationException("Spline committed native points differ from the direct oracle.");
            if((bool)after["partial"]! || !JToken.DeepEquals(Normalize(oracle),Normalize(after)) || journal.Revision(documentId)==revision) throw new InvalidOperationException("Spline commit differs from native API.");
            rows.Add(new JObject {["variant"]=variant,["fit_method"]=(int)fitMethod,["rotation_deg"]=degrees,["point_count"]=count,["input_points_mm"]=points.DeepClone(),["actual_native_points_mm"]=committedPoints,["max_solver_input_displacement_mm"]=maxPointErrorMm,["native_entities"]=after["entities"]!.DeepClone(),["native_constraints"]=after["constraints"]!.DeepClone(),["outcome"]="PASS exact native point oracle, geometry/constraints, preview rollback and commit"});
            var cleanup=app.TransactionManager.StartTransaction((_Document)doc,"S1 spline point batch cleanup");try {sketch.Delete();cleanup.End();}catch {cleanup.Abort();throw;}
        }
    }
}
