using Inventor;
using Newtonsoft.Json.Linq;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;
using Bimwright.Ipt.Shared.Plugin;

internal static partial class Program
{
    private static void RunPolygonBatches(Inventor.Application app,PartDocument doc,JObject report)
    {
        if(app.ActiveEditObject is PlanarSketch editing) editing.ExitEdit();
        var trackerType=typeof(InventorCommandRegistry).Assembly.GetType("Bimwright.Ipt.Shared.Plugin.CadEventTracker",true)!;
        using var tracker=(IDisposable)Activator.CreateInstance(trackerType,app)!;
        var journal=(CadEventJournal)trackerType.GetProperty("Journal")!.GetValue(tracker)!;
        var commands=InventorCommandRegistry.Build(new PluginOptions(2027,false,false,0));
        var context=new InventorCommandContext {Application=app,Events=journal,Commands=commands,AllowExperimental=true,InventorYear=2027};
        string documentId="doc_"+doc.InternalName;var rows=new JArray();report["batch_cases"]=rows;
        report["kind"]=doc.ComponentDefinition is SheetMetalComponentDefinition ? "sheetmetal" : "part";
        report["evidence"]="Native API versus atomic_batch polygon; geometry/constraints, preview rollback and commit. Not desktop inference, Quest or physical.";
        JObject State()=>new() {["document_id"]=documentId,["expected_revision"]=journal.Revision(documentId)};
        JObject Call(string name,JObject args)
        {var result=commands[name].Execute(context,args);if(!result.Ok) throw new InvalidOperationException(name+": "+result.Error?.Code+" "+result.Error?.Message);return (JObject)result.Data!;}
        JObject Normalize(JObject snapshot)
        {var copy=(JObject)snapshot.DeepClone();copy.Remove("revision");foreach(var item in copy.Descendants().OfType<JObject>()) {item.Remove("id");item.Remove("entity_id");}return copy;}
        foreach(int sides in new[] {3,65,120})
        foreach(bool inscribed in new[] {true,false})
        foreach(double degrees in new[] {0d,53.13010235415598})
        {
            var sketch=doc.ComponentDefinition.Sketches.Add(doc.ComponentDefinition.WorkPlanes[3]);sketch.Name="S1_PolygonBatch_"+doc.ComponentDefinition.Sketches.Count;
            string sketchId=(string)((JArray)Call("get_design_context_xr",State())["sketches"]!).Single(s=>(string?)s["name"]==sketch.Name)["sketch_id"]!;
            JObject Snapshot(){var args=State();args["sketch_id"]=sketchId;return Call("get_sketch_editor_xr",args);}
            var before=Snapshot();string revision=journal.Revision(documentId);
            double angle=degrees*Math.PI/180,bx=13+20*Math.Cos(angle),by=21+20*Math.Sin(angle);
            var operation=new JObject {["command"]="edit_sketch",["arguments"]=new JObject {["sketch_id"]=sketchId,["variant"]="polygon",
                ["options"]=new JObject {["points_mm"]=new JArray {new JArray(13,21),new JArray(bx,by)},["sides"]=sides,["inscribed"]=inscribed}}};
            JObject Batch(bool preview,JObject? op=null){var args=State();args["operations"]=new JArray((op??operation).DeepClone());args["preview"]=preview;args["include_preview_mesh"]=preview;return args;}
            if(sides==3 && inscribed && degrees==0)
            {
                var refusals=new JArray();report["invalid_inputs"]=refusals;
                foreach(JToken value in new JToken[] {new JValue(2),new JValue(121),new JValue(5.5),new JValue("120")})
                {
                    var invalid=(JObject)operation.DeepClone();invalid["arguments"]!["options"]!["sides"]=value;
                    var refused=commands["atomic_batch"].Execute(context,Batch(false,invalid));
                    if(refused.Ok || journal.Revision(documentId)!=revision || !JToken.DeepEquals(before,Snapshot())) throw new InvalidOperationException("Invalid polygon side count changed fixture.");
                    refusals.Add(new JObject {["sides"]=value.DeepClone(),["code"]=refused.Error?.Code,["outcome"]="PASS refusal, exact snapshot and revision retained"});
                }
            }
            JObject oracle;var transaction=app.TransactionManager.StartTransaction((_Document)doc,"S1 direct polygon oracle, aborted");
            try
            {
                var g=app.TransientGeometry;
                sketch.SketchLines.AddAsPolygon(sides,g.CreatePoint2d(1.3,2.1),g.CreatePoint2d(bx/10,by/10),inscribed);
                sketch.Solve();doc.Update2(true);oracle=Snapshot();
                if(sketch.SketchLines.Count!=sides || (bool)oracle["partial"]!)
                {
                    report["incomplete_oracle"]=new JObject {["sides"]=sides,["inscribed"]=inscribed,["rotation_deg"]=degrees,["native_line_count"]=sketch.SketchLines.Count,["snapshot"]=oracle.DeepClone()};
                    throw new InvalidOperationException("Incomplete polygon oracle.");
                }
                var nativeConstraints=(JArray)oracle["constraints"]!;
                var polygonConstraints=nativeConstraints.OfType<JObject>().Where(c=>(string?)c["native_type"]=="kPatternConstraintObject").ToArray();
                if(nativeConstraints.Count!=sketch.GeometricConstraints.Count || polygonConstraints.Length==0
                    || polygonConstraints.Any(c=>(string?)c["pattern_definition_state"]!="unavailable" || c["pattern_definition"]!=null
                        || (string?)c["binding_state"]!="complete" || c["related_entities"] is not JArray related || related.Count==0))
                    throw new InvalidOperationException("Polygon native constraint identities or associations are incomplete.");
            }
            finally {transaction.Abort();}
            if(!JToken.DeepEquals(Normalize(before),Normalize(Snapshot())) || !journal.TryRestoreRevision(documentId,revision)) throw new InvalidOperationException("Polygon oracle rollback failed.");
            var preview=Call("atomic_batch",Batch(true));
            if(journal.Revision(documentId)!=revision || !JToken.DeepEquals(before,Snapshot())) throw new InvalidOperationException("Polygon preview mutated fixture.");
            var ghost=((JArray)preview["preview_mesh"]!["sketches"]!).Single(s=>(string?)s["sketch_name"]==sketch.Name);
            var expected=(JArray)oracle["entities"]!.DeepClone();foreach(JObject entity in expected) entity.Remove("id");
            if((bool)ghost["partial"]! || !JToken.DeepEquals(expected,ghost["native_entities"])) throw new InvalidOperationException("Polygon preview differs from native API.");
            Call("atomic_batch",Batch(false));var after=Snapshot();
            if(sketch.SketchLines.Count!=sides || (bool)after["partial"]! || !JToken.DeepEquals(Normalize(oracle),Normalize(after)) || journal.Revision(documentId)==revision) throw new InvalidOperationException("Polygon commit differs from native API.");
            rows.Add(new JObject {["sides"]=sides,["inscribed"]=inscribed,["rotation_deg"]=degrees,["native_entities"]=after["entities"]!.DeepClone(),["native_constraints"]=after["constraints"]!.DeepClone(),
                ["outcome"]="PASS native geometry/constraints, exact preview rollback and commit"});
            Console.WriteLine($"PASS polygon {sides} sides, inscribed={inscribed}, rotation={degrees}");
            var cleanup=app.TransactionManager.StartTransaction((_Document)doc,"S1 polygon batch cleanup");try {sketch.Delete();cleanup.End();}catch {cleanup.Abort();throw;}
        }
    }
}
