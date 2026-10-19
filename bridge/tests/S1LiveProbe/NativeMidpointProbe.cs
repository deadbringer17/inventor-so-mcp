using System.Reflection;
using Inventor;
using Newtonsoft.Json.Linq;
using Bimwright.Ipt.Shared.Infrastructure;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Plugin;

internal static partial class Program
{
    private static void ProbeMidpoint(global::Inventor.Application app,PartDocument fixture,
        IReadOnlyDictionary<string,IInventorCommand> commands,InventorCommandContext context,CadEventJournal journal,JArray rows)
    {
        var g=app.TransientGeometry;string documentId="doc_"+fixture.InternalName;
        JObject State()=>new() {["document_id"]=documentId,["expected_revision"]=journal.Revision(documentId)};
        JObject Call(string name,JObject args)
        {var result=commands[name].Execute(context,args);if(!result.Ok) throw new InvalidOperationException(name+": "+result.Error?.Code+" "+result.Error?.Message);return (JObject)result.Data!;}
        foreach(string source in new[] {"standalone","line_endpoints","circle_centers"})
        foreach(int mode in new[] {0,1,2,3})
        {
            Console.WriteLine($"Native midpoint: {source}, references={mode}");
            var row=new JObject {["source"]=source,["reference_mode"]=mode,["outcome"]="INCOMPLETE"};rows.Add(row);
            PlanarSketch sketch;SketchPoint middle,end;SketchEntity middleOwner,endOwner;string middlePart,endPart;
            var seed=app.TransactionManager.StartTransaction((_Document)fixture,"S1 midpoint seed");
            try
            {
                sketch=fixture.ComponentDefinition.Sketches.Add(fixture.ComponentDefinition.WorkPlanes[3]);sketch.Name="S1_Midpoint_"+source+mode;
                if(source=="standalone") {middle=sketch.SketchPoints.Add(g.CreatePoint2d(1.3,2.1),false);end=sketch.SketchPoints.Add(g.CreatePoint2d(3.7,3.8),false);middleOwner=(SketchEntity)middle;endOwner=(SketchEntity)end;middlePart=endPart="Point";}
                else if(source=="line_endpoints") {var a=sketch.SketchLines.AddByTwoPoints(g.CreatePoint2d(.4,1.2),g.CreatePoint2d(1.3,2.1));var b=sketch.SketchLines.AddByTwoPoints(g.CreatePoint2d(3.7,3.8),g.CreatePoint2d(5,4.3));middle=a.EndSketchPoint;end=b.StartSketchPoint;middleOwner=(SketchEntity)a;endOwner=(SketchEntity)b;middlePart="End";endPart="Start";}
                else {var a=sketch.SketchCircles.AddByCenterRadius(g.CreatePoint2d(1.3,2.1),.5);var b=sketch.SketchCircles.AddByCenterRadius(g.CreatePoint2d(3.7,3.8),.7);middle=a.CenterSketchPoint;end=b.CenterSketchPoint;middleOwner=(SketchEntity)a;endOwner=(SketchEntity)b;middlePart=endPart="Center";}
                seed.End();
            }catch {seed.Abort();throw;}
            string sketchId=(string)((JArray)Call("get_design_context_xr",State())["sketches"]!).Single(s=>(string?)s["name"]==sketch.Name)["sketch_id"]!;
            JObject Snapshot(){var args=State();args["sketch_id"]=sketchId;return Call("get_sketch_editor_xr",args);}
            var before=Snapshot();string revision=journal.Revision(documentId);
            var resolver=typeof(InventorCommandRegistry).Assembly.GetType("Bimwright.Ipt.Shared.Handlers.Experimental.SketchReferences",true)!;
            JObject Reference(SketchEntity entity,string part)
            {
                var item=((JArray)before["entities"]!).Single(e=>SameComObject(resolver.GetMethod("Resolve",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic)!.Invoke(null,new object[] {fixture,(string)e["id"]!,"sketch_entity"})!,entity));
                return new JObject {["document_id"]=documentId,["revision"]=revision,["sketch_id"]=sketchId,["entity_id"]=item["id"],["type"]=item["type"],["subpart"]=part};
            }
            var options=new JObject {["points_mm"]=new JArray(new JArray(13,21),new JArray(37,38))};var refs=new JArray();var roles=new JArray();
            if((mode&1)!=0) {refs.Add(Reference(middleOwner,middlePart));roles.Add("midpoint");}
            if((mode&2)!=0) {refs.Add(Reference(endOwner,endPart));roles.Add("end");}
            if(mode!=0){options["references"]=refs;options["reference_roles"]=roles;}
            var op=new JObject {["command"]="edit_sketch",["arguments"]=new JObject {["sketch_id"]=sketchId,["variant"]="line_midpoint",["options"]=options}};
            JObject Batch(JObject operation,bool preview=false){var args=State();args["operations"]=new JArray(operation.DeepClone());args["preview"]=preview;args["include_preview_mesh"]=preview;return args;}
            void Unchanged(){if(journal.Revision(documentId)!=revision || !JToken.DeepEquals(before,Snapshot())) throw new InvalidOperationException("Midpoint preview/guard failed exact rollback.");}
            void Refuse(JObject invalid){if(commands["atomic_batch"].Execute(context,Batch(invalid)).Ok) throw new InvalidOperationException("Invalid midpoint input accepted.");Unchanged();}
            var collapsed=(JObject)op.DeepClone();collapsed["arguments"]!["options"]=new JObject {["points_mm"]=new JArray(new JArray(13,21),new JArray(13,21))};Refuse(collapsed);
            var badRoles=(JObject)op.DeepClone();badRoles["arguments"]!["options"]!["reference_roles"]=new JArray("middle");Refuse(badRoles);
            if(mode!=0)
                foreach(var bad in new[] {("revision","old"),("document_id","foreign"),("type","text"),("subpart","Entity")})
                {
                    if(bad.Item1=="subpart" && source=="standalone") continue;
                    var invalid=(JObject)op.DeepClone();invalid["arguments"]!["options"]!["references"]![0]![bad.Item1]=bad.Item2;Refuse(invalid);
                }
            JObject Normalize(JObject snapshot)
            {
                var copy=(JObject)snapshot.DeepClone();copy.Remove("revision");
                foreach(var item in copy.Descendants().OfType<JObject>()) {item.Remove("id");item.Remove("entity_id");}
                return copy;
            }
            void UpdateSources(int cycle)
            {middle.MoveTo(g.CreatePoint2d(1.3+.3*cycle,2.1+.2*cycle));end.MoveTo(g.CreatePoint2d(3.7-.2*cycle,3.8+.3*cycle));sketch.Solve();fixture.Update2(true);}
            JObject oracle;var updates=new JArray();
            var direct=app.TransactionManager.StartTransaction((_Document)fixture,"S1 native midpoint oracle, aborted");
            try
            {
                sketch.SketchLines.AddByMidEndPoints((mode&1)!=0 ? middle : g.CreatePoint2d(1.3,2.1),(mode&2)!=0 ? end : g.CreatePoint2d(3.7,3.8));sketch.Solve();fixture.Update2(true);oracle=Snapshot();
                for(int cycle=1;cycle<=2;cycle++){UpdateSources(cycle);updates.Add(Normalize(Snapshot()));}
            }finally{direct.Abort();}
            if(!JToken.DeepEquals(Normalize(before),Normalize(Snapshot())) || !journal.TryRestoreRevision(documentId,revision)) throw new InvalidOperationException("Midpoint oracle failed rollback.");Unchanged();
            var preview=Call("atomic_batch",Batch(op,true));Unchanged();
            var ghost=((JArray)preview["preview_mesh"]!["sketches"]!).Single(s=>(string?)s["sketch_name"]==sketch.Name);
            var expectedGeometry=(JArray)oracle["entities"]!.DeepClone();foreach(JObject entity in expectedGeometry) entity.Remove("id");
            if((bool)ghost["partial"]! || !JToken.DeepEquals(expectedGeometry,ghost["native_entities"])) throw new InvalidOperationException("Midpoint preview differs from direct API.");
            Call("atomic_batch",Batch(op));var after=Snapshot();
            if((bool)after["partial"]! || !JToken.DeepEquals(Normalize(oracle),Normalize(after)) || journal.Revision(documentId)==revision
                || ((JArray)before["entities"]!).Any(e=>!((JArray)after["entities"]!).Any(a=>(string?)a["id"]==(string?)e["id"]))) throw new InvalidOperationException("Midpoint commit differs from native API or lost source identity.");
            var newLines=((JArray)after["entities"]!).Where(e=>(string?)e["type"]=="line" && !((JArray)before["entities"]!).Any(b=>(string?)b["id"]==(string?)e["id"])).ToArray();
            if(newLines.Length!=1 || !TransformEqual(new[] {-11d,4,37,38},((JArray)newLines[0]["start_mm"]!).Concat((JArray)newLines[0]["end_mm"]!).Select(v=>(double)v).ToArray())) throw new InvalidOperationException("Midpoint line endpoints do not match 2M-E and E.");
            if((mode&1)!=0 && !((JArray)after["constraints"]!).Any(c=>(string?)c["native_type"]=="kMidpointConstraintObject" && (string?)c["binding_state"]=="complete")) throw new InvalidOperationException("Midpoint association missing from native snapshot.");
            for(int cycle=1;cycle<=2;cycle++) {UpdateSources(cycle);if(!JToken.DeepEquals(updates[cycle-1],Normalize(Snapshot()))) throw new InvalidOperationException("Midpoint source update differs from native API.");}
            row["native_type"]="kSketchLineObject";row["source_updates"]=2;row["selected_references"]=refs.Count;row["outcome"]="PASS direct API geometry/constraints, preview rollback, source identities, 2 source updates and invalid inputs";
            var cleanup=app.TransactionManager.StartTransaction((_Document)fixture,"S1 midpoint fixture cleanup");try{sketch.Delete();cleanup.End();}catch{cleanup.Abort();throw;}
        }
    }
}
