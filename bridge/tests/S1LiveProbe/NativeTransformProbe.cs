using System.Reflection;
using Inventor;
using Newtonsoft.Json.Linq;
using Bimwright.Ipt.Shared.Infrastructure;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Plugin;

internal static partial class Program
{
    private static JArray ProbeTransforms(global::Inventor.Application app,PartDocument fixture,
        IReadOnlyDictionary<string,IInventorCommand> commands,InventorCommandContext context,CadEventJournal journal,JArray rows)
    {
        var g=app.TransientGeometry;string documentId="doc_"+fixture.InternalName;
        string? modeFilter=System.Environment.GetEnvironmentVariable("S1_PROBE_TRANSFORM_MODE");
        if(modeFilter!=null && !new[] {"move","copy","rotate","rotate_copy"}.Contains(modeFilter,StringComparer.Ordinal))
            throw new ArgumentException("Unknown transform probe mode filter.");
        JObject State()=>new() {["document_id"]=documentId,["expected_revision"]=journal.Revision(documentId)};
        JObject Call(string name,JObject args)
        {var result=commands[name].Execute(context,args);if(!result.Ok) throw new InvalidOperationException(name+": "+result.Error?.Code+" "+result.Error?.Message);return (JObject)result.Data!;}
        var resolver=typeof(InventorCommandRegistry).Assembly.GetType("Bimwright.Ipt.Shared.Handlers.Experimental.SketchReferences",true)!;
        object Resolve(string id,string type)=>(resolver.GetMethod("Resolve",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic)!.Invoke(null,new object[] {fixture,id,type=="text" ? "sketch_text" : "sketch_entity"}))!;
        foreach(bool constrained in new[] {false,true})
        foreach(string mode in constrained ? new[] {"move","rotate"} : new[] {"move","copy","rotate","rotate_copy"})
        foreach(bool remove in new[] {false,true})
        {
            if(modeFilter!=null && mode!=modeFilter) continue;
            var row=new JObject {["mode"]=mode,["remove_constraints"]=remove,["constrained_fixture"]=constrained,["outcome"]="INCOMPLETE"};rows.Add(row);
            var seed=app.TransactionManager.StartTransaction((_Document)fixture,"S1 dedicated transform geometry");
            PlanarSketch sketch,foreign;var objects=new List<object>();
            try
            {
                sketch=fixture.ComponentDefinition.Sketches.Add(fixture.ComponentDefinition.WorkPlanes[3]);sketch.Name="S1_Transform_"+mode+"_"+remove+"_"+constrained;
                var line=sketch.SketchLines.AddByTwoPoints(g.CreatePoint2d(0,0),g.CreatePoint2d(2,1));objects.Add(line);
                if(constrained) sketch.GeometricConstraints.AddGround((SketchEntity)line);
                else
                {
                    objects.Add(sketch.SketchCircles.AddByCenterRadius(g.CreatePoint2d(5,5),1));
                    objects.Add(sketch.SketchArcs.AddByCenterStartSweepAngle(g.CreatePoint2d(9,5),1,.2,1.2));
                    objects.Add(sketch.SketchEllipses.Add(g.CreatePoint2d(14,5),g.CreateUnitVector2d(1,0),1.2,.6));
                    ObjectCollection Points(double x)
                    {var p=app.TransientObjects.CreateObjectCollection();p.Add(g.CreatePoint2d(x,2));p.Add(g.CreatePoint2d(x+1,3));p.Add(g.CreatePoint2d(x+2,2));return p;}
                    objects.Add(sketch.SketchSplines.Add(Points(18)));objects.Add(sketch.SketchControlPointSplines.Add(Points(23)));
                    objects.Add(sketch.SketchPoints.Add(g.CreatePoint2d(28,3),false));objects.Add(sketch.TextBoxes.AddFitted(g.CreatePoint2d(32,3),"S1 native transform"));
                }
                foreign=fixture.ComponentDefinition.Sketches.Add(fixture.ComponentDefinition.WorkPlanes[3]);foreign.Name="S1_Transform_Foreign";
                foreign.SketchLines.AddByTwoPoints(g.CreatePoint2d(0,0),g.CreatePoint2d(1,1));seed.End();
            }
            catch {seed.Abort();throw;}
            var sketches=(JArray)Call("get_design_context_xr",State())["sketches"]!;
            string sketchId=(string)sketches.Single(s=>(string?)s["name"]==sketch.Name)["sketch_id"]!;
            string foreignId=(string)sketches.Single(s=>(string?)s["name"]==foreign.Name)["sketch_id"]!;
            JObject Snapshot() {var args=State();args["sketch_id"]=sketchId;return Call("get_sketch_editor_xr",args);}
            var before=Snapshot();if((bool)before["partial"]!) throw new InvalidOperationException("Transform fixture snapshot is partial.");
            string revision=journal.Revision(documentId);bool rotate=mode.StartsWith("rotate",StringComparison.Ordinal),copy=mode=="copy" || mode=="rotate_copy";
            var selected=new List<JObject>();var samples=new List<double[]>();var properties=new List<JObject>();
            foreach(var native in objects)
            {
                var item=((JArray)before["entities"]!).OfType<JObject>().Single(e=>SameComObject(Resolve((string)e["id"]!,(string)e["type"]!),native));
                selected.Add(item);samples.Add(TransformSamples(native));properties.Add(TransformProperties(native));
            }
            var refs=new JArray(selected.Select(e=>new JObject {["document_id"]=documentId,["revision"]=revision,["sketch_id"]=sketchId,
                ["entity_id"]=e["id"],["type"]=e["type"],["subpart"]="Entity"}));
            var options=new JObject {["references"]=refs,["remove_constraints"]=remove};
            if(rotate) {options["center_mm"]=new JArray(5,6);options["angle_deg"]=-45;options["copy"]=copy;}else options["delta_mm"]=new JArray(12,-8);
            var op=new JObject {["command"]="edit_sketch",["arguments"]=new JObject {["sketch_id"]=sketchId,["variant"]="transform_"+(rotate ? "rotate" : mode),["options"]=options}};
            JObject Batch(JObject operation,bool preview=false)
            {var args=State();args["operations"]=new JArray(operation.DeepClone());args["preview"]=preview;args["include_preview_mesh"]=preview;return args;}
            void Unchanged()
            {if(journal.Revision(documentId)!=revision || !JToken.DeepEquals(before,Snapshot())) throw new InvalidOperationException("Transform rollback changed native snapshot or revision.");}
            void Refuse(JObject invalid)
            {if(commands["atomic_batch"].Execute(context,Batch(invalid)).Ok) throw new InvalidOperationException("Invalid transform was accepted.");Unchanged();}
            if(rotate && !copy && !constrained)
            {
                // Text glyph rotation is still being isolated. Prove the mixed
                // operation refuses atomically, then test the other native types.
                foreach(bool previewRefusal in new[] {true,false})
                {
                    var refusal=commands["atomic_batch"].Execute(context,Batch(op,previewRefusal));
                    if(refusal.Ok || (string?)refusal.Error?.Details?["step_code"]!=InventorErrorCodes.INVALID_ARGUMENT)
                        throw new InvalidOperationException("Unvalidated text rotation must be explicitly refused.");
                    Unchanged();
                }
                row["text_rotation_refusal_checks"]=2;
                int textIndex=objects.FindIndex(o=>o is TextBox);
                objects.RemoveAt(textIndex);selected.RemoveAt(textIndex);samples.RemoveAt(textIndex);properties.RemoveAt(textIndex);refs.RemoveAt(textIndex);
            }
            foreach(var bad in new[] {("revision","old"),("document_id","other"),("type","dimension"),("subpart","Start")})
            {var invalid=(JObject)op.DeepClone();invalid["arguments"]!["options"]!["references"]![0]![bad.Item1]=bad.Item2;Refuse(invalid);}
            var owner=(JObject)op.DeepClone();owner["arguments"]!["sketch_id"]=foreignId;foreach(JObject r in (JArray)owner["arguments"]!["options"]!["references"]!) r["sketch_id"]=foreignId;Refuse(owner);
            var duplicate=(JObject)op.DeepClone();((JArray)duplicate["arguments"]!["options"]!["references"]!).Add(refs[0]!.DeepClone());Refuse(duplicate);
            var invalidOption=(JObject)op.DeepClone();invalidOption["arguments"]!["options"]!["remove_constraints"]="yes";Refuse(invalidOption);
            var extra=(JObject)op.DeepClone();extra["arguments"]!["options"]!["scale_factor"]=2;Refuse(extra);
            if(copy)
            {
                // Copying terminated Inventor in a prior direct API oracle.
                // Exercise only the backend refusal; never run that oracle here.
                foreach(bool previewRefusal in new[] {true,false})
                {
                    var refusal=commands["atomic_batch"].Execute(context,Batch(op,previewRefusal));
                    if(refusal.Ok || (string?)refusal.Error?.Details?["step_code"]!=InventorErrorCodes.INVALID_ARGUMENT)
                        throw new InvalidOperationException("Copy must be explicitly refused before native execution.");
                    Unchanged();
                }
                row["outcome"]="PASS temporary copy preview/commit refusal, exact rollback; native copy NOT COVERED";
                var blockedCleanup=app.TransactionManager.StartTransaction((_Document)fixture,"S1 refused copy fixture cleanup");
                try {sketch.Delete();foreign.Delete();blockedCleanup.End();}catch {blockedCleanup.Abort();throw;}
                continue;
            }
            JObject? oracle=null;bool oracleFailed=false;var directSamples=new List<double[]>();
            {
                var tx=app.TransactionManager.StartTransaction((_Document)fixture,"S1 direct transform constraint oracle, aborted");
                try
                {
                    var nativeObjects=app.TransientObjects.CreateObjectCollection();foreach(var native in objects)
                    {
                        if(copy){nativeObjects.Add(native);continue;}
                        if(native is SketchControlPointSpline control)
                        {for(int i=1;i<=control.ControlPointCount;i++) nativeObjects.Add(control.get_ControlPoint(i));continue;}
                        nativeObjects.Add(native);
                        if(native is SketchLine line){nativeObjects.Add(line.StartSketchPoint);nativeObjects.Add(line.EndSketchPoint);}
                        if(native is SketchArc arc){nativeObjects.Add(arc.CenterSketchPoint);nativeObjects.Add(arc.StartSketchPoint);nativeObjects.Add(arc.EndSketchPoint);}
                        if(native is SketchCircle circle) nativeObjects.Add(circle.CenterSketchPoint);
                        if(native is SketchEllipse ellipse) nativeObjects.Add(ellipse.CenterSketchPoint);
                        if(native is SketchSpline spline && native is not SketchControlPointSpline) for(int i=1;i<=spline.FitPointCount;i++) nativeObjects.Add(spline.get_FitPoint(i));
                        if(native is TextBox text) nativeObjects.Add(text.OriginSketchPoint);
                    }
                    if(rotate) sketch.RotateSketchObjects(nativeObjects,g.CreatePoint2d(.5,.6),-Math.PI/4,copy,remove);
                    else sketch.MoveSketchObjects(nativeObjects,g.CreateVector2d(1.2,-.8),copy,remove);
                    sketch.Solve();if(sketch.ConstraintStatus==ConstraintStatusEnum.kOverConstrainedConstraintStatus) throw new InvalidOperationException("Native solver conflict.");
                    fixture.Update2(true);foreach(var native in objects) directSamples.Add(TransformSamples(native));
                    oracle=Snapshot();
                }
                catch(System.Runtime.InteropServices.COMException ex)
                {oracleFailed=true;row["native_refusal_hresult"]=ex.HResult;row["native_refusal_error"]=ex.ToString();}
                finally {tx.Abort();}
                var restored=Snapshot();var baseline=(JObject)before.DeepClone();restored.Remove("revision");baseline.Remove("revision");
                if(!JToken.DeepEquals(baseline,restored) || !journal.TryRestoreRevision(documentId,revision)) throw new InvalidOperationException("Native constraint oracle failed exact rollback.");Unchanged();
            }
            if(oracleFailed)
            {
                Refuse(op);row["outcome"]="PASS same refusal as direct native API, exact rollback";
            }
            else
            {
                var preview=Call("atomic_batch",Batch(op,true));Unchanged();
                var ghost=((JArray)preview["preview_mesh"]!["sketches"]!).Single(s=>(string?)s["sketch_name"]==sketch.Name);
                if(((JArray)ghost["native_entities"]!).Any(e=>e["id"]!=null)) throw new InvalidOperationException("Transform preview exported tentative references.");
                var expectedGhost=(JArray)oracle!["entities"]!.DeepClone();foreach(JObject entity in expectedGhost) entity.Remove("id");
                if((bool)ghost["partial"]! || !JToken.DeepEquals(expectedGhost,ghost["native_entities"]))
                    throw new InvalidOperationException("Transform preview differs from direct native API geometry.");
                Call("atomic_batch",Batch(op));var after=Snapshot();
                if((bool)after["partial"]! || journal.Revision(documentId)==revision) throw new InvalidOperationException("Native transform did not yield an authoritative revision.");
                {var expected=(JObject)oracle!.DeepClone();var actual=(JObject)after.DeepClone();expected.Remove("revision");actual.Remove("revision");if(!JToken.DeepEquals(expected,actual)) throw new InvalidOperationException("Constraint handling differs from direct native API.");}
                if(!constrained)
                {
                    var oldIds=((JArray)before["entities"]!).Select(e=>(string)e["id"]!).ToHashSet(StringComparer.Ordinal);
                    if(copy)
                    {
                        foreach(var original in (JArray)before["entities"]!)
                            if(!((JArray)after["entities"]!).Any(e=>JToken.DeepEquals(e,original))) throw new InvalidOperationException("Native copy altered its original geometry.");
                    }
                    else if(!oldIds.SetEquals(((JArray)after["entities"]!).Select(e=>(string)e["id"]!))) throw new InvalidOperationException("Native move/rotate recreated sketch entities.");
                    for(int index=0;index<selected.Count;index++)
                    {
                        var item=selected[index];string type=(string)item["type"]!,id=(string)item["id"]!;
                        var expected=TransformExpected(samples[index],rotate);
                        var candidates=((JArray)after["entities"]!).Where(e=>(string?)e["type"]==type && (copy ? !oldIds.Contains((string)e["id"]!) : (string?)e["id"]==id));
                        if(!candidates.Any(e=>
                        {
                            var native=Resolve((string)e["id"]!,type);
                            return TransformEqual(expected,TransformSamples(native)) && TransformPropertiesEqual(properties[index],TransformProperties(native),rotate);
                        }))
                        {
                            row["mismatched_type"]=type;row["analytical_expected_cm"]=new JArray(expected);
                            row["direct_native_samples_cm"]=new JArray(directSamples[index]);
                            row["handler_samples_cm"]=new JArray(TransformSamples(Resolve(id,type)));
                            row["original_properties"]=properties[index].DeepClone();
                            row["handler_properties"]=TransformProperties(Resolve(id,type));
                            row["initial_constraints"]=before["constraints"]!.DeepClone();
                            throw new InvalidOperationException("Native transform geometry mismatch for "+type);
                        }
                    }
                }
                row["objects_checked"]=objects.Count;row["outcome"]="PASS native geometry, identity/copy preservation, exact preview rollback, guards and constraint API comparison";
            }
            var cleanup=app.TransactionManager.StartTransaction((_Document)fixture,"S1 completed transform fixture cleanup");
            try {sketch.Delete();foreign.Delete();cleanup.End();}catch {cleanup.Abort();throw;}
        }
        return rows;
    }
    private static double[] TransformSamples(object native)
    {
        if(native is SketchPoint point) return new[] {point.Geometry.X,point.Geometry.Y};
        if(native is TextBox text)
        {
            int vertices=0,facets=0;double[] coordinates=Array.Empty<double>();int[] indices=Array.Empty<int>();
            text.GetFacetsInfo(out vertices,out facets,out coordinates,out indices);
            if(vertices<3 || facets<1 || coordinates.Length!=vertices*3) throw new InvalidOperationException("Native text has no usable facet geometry.");
            return coordinates.Where((_,i)=>i%3!=2).ToArray();
        }
        if(native is SketchCircle circle) return new[] {circle.CenterSketchPoint.Geometry.X,circle.CenterSketchPoint.Geometry.Y};
        dynamic evaluator=((dynamic)native).Geometry.Evaluator;double min=0,max=0;evaluator.GetParamExtents(out min,out max);
        double[] parameters={min,min+(max-min)*.25,min+(max-min)*.5,min+(max-min)*.75,max},points=Array.Empty<double>();
        evaluator.GetPointAtParam(ref parameters,ref points);return points;
    }
    private static double[] TransformExpected(double[] points,bool rotate)
    {
        var transformed=(double[])points.Clone();for(int i=0;i<points.Length;i+=2)
        {double x=points[i]-.5,y=points[i+1]-.6;transformed[i]=rotate ? .5+(x+y)/Math.Sqrt(2) : points[i]+1.2;
            transformed[i+1]=rotate ? .6+(-x+y)/Math.Sqrt(2) : points[i+1]-.8;}return transformed;
    }
    private static bool TransformEqual(double[] one,double[] two)=>one.Length==two.Length && one.Length>0 && one.Length%2==0 &&
        !one.Where((v,i)=>!double.IsFinite(v) || !double.IsFinite(two[i]) || Math.Abs(v-two[i])>1e-6).Any();
    private static JObject TransformProperties(object native)
    {
        if(native is SketchCircle circle) return new JObject {["radius"]=circle.Geometry.Radius};
        if(native is TextBox text) return new JObject {["rotation"]=text.Rotation,["text"]=text.FormattedText};
        return new JObject();
    }
    private static bool TransformPropertiesEqual(JObject original,JObject transformed,bool rotate)
    {
        if(original["radius"]!=null)
            return transformed["radius"]!=null && double.IsFinite((double)transformed["radius"]!) && Math.Abs((double)original["radius"]!-(double)transformed["radius"]!)<1e-6;
        if(original["rotation"]!=null && transformed["rotation"]!=null)
        {
            // Rotation is the text override, not the sketch box orientation.
            // Check actual native glyph vertices above and preserve the override.
            double expected=(double)original["rotation"]!;
            double angleError=Math.Atan2(Math.Sin((double)transformed["rotation"]!-expected),Math.Cos((double)transformed["rotation"]!-expected));
            return double.IsFinite(angleError) && Math.Abs(angleError)<1e-6 && JToken.DeepEquals(original["text"],transformed["text"]);
        }
        return JToken.DeepEquals(original,transformed);
    }
}
