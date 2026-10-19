using System.Reflection;
using Inventor;
using Newtonsoft.Json.Linq;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;
using Bimwright.Ipt.Shared.Plugin;

internal static partial class Program
{
    private static JArray ProbeOffsets(global::Inventor.Application app,PartDocument fixture,
        IReadOnlyDictionary<string,IInventorCommand> commands,InventorCommandContext context,CadEventJournal journal,JArray rows)
    {
        var g=app.TransientGeometry;string documentId="doc_"+fixture.InternalName;
        JObject State()=>new() {["document_id"]=documentId,["expected_revision"]=journal.Revision(documentId)};
        JObject Call(string name,JObject args)
        {var result=commands[name].Execute(context,args);if(!result.Ok) throw new InvalidOperationException(name+": "+result.Error?.Message);return (JObject)result.Data!;}
        var resolver=typeof(InventorCommandRegistry).Assembly.GetType("Bimwright.Ipt.Shared.Handlers.Experimental.SketchReferences",true)!;
        object Resolve(string id)=>resolver.GetMethod("Resolve",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic)!.Invoke(null,new object[] {fixture,id,"sketch_entity"})!;
        var shapes=new[] {"line","circle","chain_manual","chain_auto","spline"};
        string? filter=System.Environment.GetEnvironmentVariable("S1_PROBE_OFFSET_SHAPE");
        if(filter!=null && !shapes.Contains(filter,StringComparer.Ordinal)) throw new ArgumentException("Unknown S1 offset probe shape filter.");
        foreach(string shape in shapes.Where(shape=>filter==null || shape==filter))
        foreach(bool point in new[] {false,true})
        foreach(bool constraints in new[] {false,true})
        foreach(bool natural in new[] {false,true})
        {
            var row=new JObject {["shape"]=shape,["point_mode"]=point,["constraints"]=constraints,["natural"]=natural,["outcome"]="INCOMPLETE"};rows.Add(row);
            var seed=app.TransactionManager.StartTransaction((_Document)fixture,"S1 dedicated native offset");
            PlanarSketch sketch,foreign;var originals=new List<object>();
            try
            {
                sketch=fixture.ComponentDefinition.Sketches.Add(fixture.ComponentDefinition.WorkPlanes[3]);sketch.Name="S1_Offset_"+shape+"_"+point+"_"+constraints+"_"+natural;
                if(shape=="circle") originals.Add(sketch.SketchCircles.AddByCenterRadius(g.CreatePoint2d(0,0),2));
                else if(shape=="spline")
                {var p=app.TransientObjects.CreateObjectCollection();p.Add(g.CreatePoint2d(0,0));p.Add(g.CreatePoint2d(1,1));p.Add(g.CreatePoint2d(2,0));originals.Add(sketch.SketchSplines.Add(p));}
                else
                {
                    var line=sketch.SketchLines.AddByTwoPoints(g.CreatePoint2d(0,0),g.CreatePoint2d(2,0));originals.Add(line);
                    if(shape.StartsWith("chain",StringComparison.Ordinal)) originals.Add(sketch.SketchLines.AddByTwoPoints(line.EndSketchPoint,g.CreatePoint2d(2,2)));
                }
                foreign=fixture.ComponentDefinition.Sketches.Add(fixture.ComponentDefinition.WorkPlanes[3]);foreign.Name="S1_Offset_Foreign";
                foreign.SketchLines.AddByTwoPoints(g.CreatePoint2d(0,0),g.CreatePoint2d(1,0));seed.End();
            }
            catch {seed.Abort();throw;}
            var sketches=(JArray)Call("get_design_context_xr",State())["sketches"]!;
            string sketchId=(string)sketches.Single(s=>(string?)s["name"]==sketch.Name)["sketch_id"]!;
            string foreignId=(string)sketches.Single(s=>(string?)s["name"]==foreign.Name)["sketch_id"]!;
            JObject Snapshot(){var args=State();args["sketch_id"]=sketchId;return Call("get_sketch_editor_xr",args);}
            var before=Snapshot();if((bool)before["partial"]!) throw new InvalidOperationException("Native offset fixture is partial.");
            string revision=journal.Revision(documentId);bool connected=shape=="chain_auto";
            var source=originals.Select(o=>((JArray)before["entities"]!).OfType<JObject>().Single(e=>SameComObject(Resolve((string)e["id"]!),o))).ToArray();
            var selected=connected ? source.Take(1) : source.AsEnumerable();
            var refs=new JArray(selected.Select(e=>new JObject {["document_id"]=documentId,["revision"]=revision,["sketch_id"]=sketchId,
                ["entity_id"]=e["id"],["type"]=e["type"],["subpart"]="Entity"}));
            var options=new JObject {["references"]=refs,["include_connected"]=connected,["create_constraints"]=constraints};
            if(point) options["point_mm"]=new JArray(5,natural ? -5 : 5);else {options["distance_mm"]=5;options["natural_direction"]=natural;}
            var op=new JObject {["command"]="edit_sketch",["arguments"]=new JObject {["sketch_id"]=sketchId,["variant"]=point ? "offset_point" : "offset_distance",["options"]=options}};
            JObject Batch(JObject operation,bool preview=false)
            {var args=State();args["operations"]=new JArray(operation.DeepClone());args["preview"]=preview;args["include_preview_mesh"]=preview;return args;}
            void Unchanged(){if(journal.Revision(documentId)!=revision || !JToken.DeepEquals(before,Snapshot())) throw new InvalidOperationException("Offset rollback changed source geometry, constraints or revision.");}
            void Refuse(JObject invalid){if(commands["atomic_batch"].Execute(context,Batch(invalid)).Ok) throw new InvalidOperationException("Invalid offset accepted.");Unchanged();}
            foreach(var bad in new[] {("revision","old"),("document_id","other"),("type","point"),("subpart","Start")})
            {var invalid=(JObject)op.DeepClone();invalid["arguments"]!["options"]!["references"]![0]![bad.Item1]=bad.Item2;Refuse(invalid);}
            var owner=(JObject)op.DeepClone();owner["arguments"]!["sketch_id"]=foreignId;foreach(JObject r in (JArray)owner["arguments"]!["options"]!["references"]!) r["sketch_id"]=foreignId;Refuse(owner);
            var duplicate=(JObject)op.DeepClone();((JArray)duplicate["arguments"]!["options"]!["references"]!).Add(refs[0]!.DeepClone());Refuse(duplicate);
            var invalidBoolean=(JObject)op.DeepClone();invalidBoolean["arguments"]!["options"]!["create_constraints"]="yes";Refuse(invalidBoolean);
            var unknown=(JObject)op.DeepClone();unknown["arguments"]!["options"]!["remove_constraints"]=true;Refuse(unknown);
            if(!point){var zero=(JObject)op.DeepClone();zero["arguments"]!["options"]!["distance_mm"]=0;Refuse(zero);}
            if(shape=="chain_manual") {var invalidChain=(JObject)op.DeepClone();invalidChain["arguments"]!["options"]!["include_connected"]=true;Refuse(invalidChain);}
            var oracle=new List<JObject>();
            bool nativeCreated=false;int? nativeRefusal=null;
            void UpdateSources()
            {
                var priorSamples=originals.Select(TransformSamples).ToArray();
                var objects=app.TransientObjects.CreateObjectCollection();foreach(var original in originals) objects.Add(original);
                sketch.MoveSketchObjects(objects,g.CreateVector2d(.3,.2),false,false);sketch.Solve();fixture.Update2(true);
                if(sketch.ConstraintStatus==ConstraintStatusEnum.kOverConstrainedConstraintStatus) throw new InvalidOperationException("Offset dependency update conflicts.");
                for(int index=0;index<originals.Count;index++)
                {
                    var actual=TransformSamples(originals[index]);
                    // Native MoveSketchObjects can preserve a line's tangential
                    // coordinates. Require an actual source change, then compare
                    // the entire solved result with the same direct native API.
                    if(actual.Any(value=>!double.IsFinite(value)) || TransformEqual(priorSamples[index],actual))
                    {row["source_before"]=new JArray(priorSamples[index]);row["source_actual"]=new JArray(actual);throw new InvalidOperationException("Native offset source update produced no finite geometry change.");}
                }
            }
            var direct=app.TransactionManager.StartTransaction((_Document)fixture,"S1 native offset API oracle, aborted");
            try
            {
                var curves=app.TransientObjects.CreateObjectCollection();foreach(var original in (connected ? originals.Take(1) : originals)) curves.Add(original);
                if(point) sketch.OffsetSketchEntitiesUsingPoint(curves,g.CreatePoint2d(.5,natural ? -.5 : .5),connected,constraints);
                else sketch.OffsetSketchEntitiesUsingDistance(curves,.5,natural,connected,constraints);
                nativeCreated=true;
                sketch.Solve();fixture.Update2(true);oracle.Add(OffsetSemanticSnapshot(Snapshot()));
                for(int i=0;i<2;i++){UpdateSources();oracle.Add(OffsetSemanticSnapshot(Snapshot()));}
            }
            catch(System.Runtime.InteropServices.COMException ex) when(!nativeCreated &&
                (ex.HResult==unchecked((int)0x80004005) || ex.HResult==unchecked((int)0x80070057))) {nativeRefusal=ex.HResult;}
            finally {direct.Abort();}
            var restored=Snapshot();var baseline=(JObject)before.DeepClone();restored.Remove("revision");baseline.Remove("revision");
            if(!JToken.DeepEquals(baseline,restored) || !journal.TryRestoreRevision(documentId,revision)) throw new InvalidOperationException("Direct offset oracle rollback failed.");Unchanged();
            if(nativeRefusal!=null)
            {
                foreach(bool previewRefusal in new[] {true,false})
                {
                    var refusal=commands["atomic_batch"].Execute(context,Batch(op,previewRefusal));
                    row[previewRefusal ? "preview_refusal" : "commit_refusal"]=JObject.FromObject(refusal);
                    if(refusal.Ok || (string?)refusal.Error?.Details?["step_code"]!=InventorErrorCodes.API_ERROR) throw new InvalidOperationException("Offset refusal differs from the native API error.");
                    Unchanged();
                }
                row["native_refusal_hresult"]=nativeRefusal;row["dependency_updates"]=0;
                row["outcome"]="PASS same native API geometry refusal, exact preview/commit rollback; no offset creation or dependency updates";
                Console.WriteLine("PASS native offset refusal fixture="+fixture.DisplayName+", shape="+shape+", point="+point+", constraints="+constraints+", natural="+natural);
                var rejectedCleanup=app.TransactionManager.StartTransaction((_Document)fixture,"S1 refused offset cleanup");
                try {sketch.Delete();foreign.Delete();rejectedCleanup.End();}catch {rejectedCleanup.Abort();throw;}
                continue;
            }
            var preview=Call("atomic_batch",Batch(op,true));Unchanged();
            var ghost=((JArray)preview["preview_mesh"]!["sketches"]!).Single(s=>(string?)s["sketch_name"]==sketch.Name);
            if(((JArray)ghost["native_entities"]!).Any(e=>e["id"]!=null)) throw new InvalidOperationException("Offset preview exported tentative references.");
            if((bool)ghost["partial"]! || !JToken.DeepEquals(oracle[0]["entities"],ghost["native_entities"])) throw new InvalidOperationException("Offset preview geometry differs from direct native API.");
            Call("atomic_batch",Batch(op));var after=Snapshot();
            if((bool)after["partial"]! || journal.Revision(documentId)==revision || !JToken.DeepEquals(oracle[0],OffsetSemanticSnapshot(after))) throw new InvalidOperationException("Offset differs from direct native API.");
            foreach(var item in source) if(!((JArray)after["entities"]!).Any(e=>NativeSnapshotEquivalent(item,e)))
            {row["source_before"]=item;row["source_after"]=((JArray)after["entities"]!).FirstOrDefault(e=>(string?)e["id"]==(string?)item["id"])?.DeepClone();throw new InvalidOperationException("Offset changed its source curve.");}
            if(((JArray)after["entities"]!).Count<=((JArray)before["entities"]!).Count) throw new InvalidOperationException("Offset created no native geometry.");
            var ids=((JArray)after["entities"]!).Select(e=>(string)e["id"]!).ToHashSet(StringComparer.Ordinal);
            for(int i=0;i<2;i++)
            {
                var update=app.TransactionManager.StartTransaction((_Document)fixture,"S1 offset source dependency update "+i);
                try {UpdateSources();update.End();}catch {update.Abort();throw;}
                after=Snapshot();if((bool)after["partial"]! || !JToken.DeepEquals(oracle[i+1],OffsetSemanticSnapshot(after))
                    || !ids.SetEquals(((JArray)after["entities"]!).Select(e=>(string)e["id"]!))) throw new InvalidOperationException("Offset dependency/identity differs after source update "+i);
            }
            row["variant"]=point ? "offset_point" : "offset_distance";row["create_constraints"]=constraints;row["side"]=natural ? "natural" : "opposite";
            row["include_connected"]=connected;row["dependency_updates"]=2;row["outcome"]="PASS native API geometry/constraint oracle, originals retained, preview exact rollback, reference/option guards and two source updates with stable identities";
            Console.WriteLine("PASS offset fixture="+fixture.DisplayName+", shape="+shape+", point="+point+", constraints="+constraints+", natural="+natural);
            var cleanup=app.TransactionManager.StartTransaction((_Document)fixture,"S1 completed offset cleanup");
            try {sketch.Delete();foreign.Delete();cleanup.End();}catch {cleanup.Abort();throw;}
        }
        return rows;
    }
    private static JObject OffsetSemanticSnapshot(JObject snapshot)
    {
        var clone=(JObject)snapshot.DeepClone();clone.Remove("revision");
        var ids=((JArray)clone["entities"]!).Select((e,i)=>(id:(string)e["id"]!,index:i)).ToDictionary(e=>e.id,e=>e.index,StringComparer.Ordinal);
        void Normalize(JToken token)
        {
            if(token is JObject obj)
            {
                obj.Remove("id");
                if(obj["entity_id"]!=null){string id=(string)obj["entity_id"]!;if(!ids.TryGetValue(id,out int index)) throw new InvalidOperationException("Offset oracle has a dangling association.");obj["entity_id"]=index;}
                foreach(var property in obj.Properties().ToArray()) Normalize(property.Value);
            }
            else if(token is JArray array) foreach(var child in array) Normalize(child);
        }
        Normalize(clone);return clone;
    }
    // Native spline tessellation can vary by sub-micrometre amounts after a
    // solve. Keep IDs, flags, topology and array sizes exact, while comparing
    // numeric coordinates well below the snapshot's 0.001 mm stroke tolerance.
    private static bool NativeSnapshotEquivalent(JToken? left,JToken? right)
    {
        if(left==null || right==null) return left==null && right==null;
        bool Number(JToken value)=>value.Type==JTokenType.Float || value.Type==JTokenType.Integer;
        if(Number(left) && Number(right)) return double.IsFinite((double)left) && double.IsFinite((double)right) && Math.Abs((double)left-(double)right)<=1e-5;
        if(left is JObject a && right is JObject b) return a.Count==b.Count && a.Properties().All(p=>b.TryGetValue(p.Name,out var value) && NativeSnapshotEquivalent(p.Value,value));
        if(left is JArray x && right is JArray y) return x.Count==y.Count && x.Zip(y,(a,b)=>NativeSnapshotEquivalent(a,b)).All(equal=>equal);
        return JToken.DeepEquals(left,right);
    }
}
