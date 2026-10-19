using Inventor;
using Newtonsoft.Json.Linq;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;
using Bimwright.Ipt.Shared.Plugin;

internal static partial class Program
{
    private static void RunStraightSlotBatches(Inventor.Application app,PartDocument doc,JObject report)
    {
        if(app.ActiveEditObject is PlanarSketch editing) editing.ExitEdit();
        var trackerType=typeof(InventorCommandRegistry).Assembly.GetType("Bimwright.Ipt.Shared.Plugin.CadEventTracker",true)!;
        using var tracker=(IDisposable)Activator.CreateInstance(trackerType,app)!;
        var journal=(CadEventJournal)trackerType.GetProperty("Journal")!.GetValue(tracker)!;
        var commands=InventorCommandRegistry.Build(new PluginOptions(2027,false,false,0));
        var context=new InventorCommandContext {Application=app,Events=journal,Commands=commands,AllowExperimental=true,InventorYear=2027};
        string documentId="doc_"+doc.InternalName;var rows=new JArray();report["batch_cases"]=rows;
        report["kind"]=doc.ComponentDefinition is SheetMetalComponentDefinition ? "sheetmetal" : "part";
        report["evidence"]="Native API versus atomic_batch straight slots; geometry/constraints, preview rollback and commit. Not desktop inference, Quest or physical.";
        JObject State()=>new() {["document_id"]=documentId,["expected_revision"]=journal.Revision(documentId)};
        JObject Call(string name,JObject args)
        {var result=commands[name].Execute(context,args);if(!result.Ok) throw new InvalidOperationException(name+": "+result.Error?.Code+" "+result.Error?.Message);return (JObject)result.Data!;}
        JObject Normalize(JObject snapshot)
        {var copy=(JObject)snapshot.DeepClone();copy.Remove("revision");foreach(var item in copy.Descendants().OfType<JObject>()) {item.Remove("id");item.Remove("entity_id");}return copy;}
        foreach(string variant in new[] {"slot_center_to_center","slot_overall","slot_center"})
        foreach(double degrees in new[] {0d,53.13010235415598})
        foreach(double width in new[] {5d,20d})
        {
            var sketch=doc.ComponentDefinition.Sketches.Add(doc.ComponentDefinition.WorkPlanes[3]);sketch.Name="S1_StraightSlotBatch_"+doc.ComponentDefinition.Sketches.Count;
            string sketchId=(string)((JArray)Call("get_design_context_xr",State())["sketches"]!).Single(s=>(string?)s["name"]==sketch.Name)["sketch_id"]!;
            JObject Snapshot(){var args=State();args["sketch_id"]=sketchId;return Call("get_sketch_editor_xr",args);}
            var before=Snapshot();string revision=journal.Revision(documentId);
            double angle=degrees*Math.PI/180,bx=13+40*Math.Cos(angle),by=21+40*Math.Sin(angle);
            var operation=new JObject {["command"]="edit_sketch",["arguments"]=new JObject {["sketch_id"]=sketchId,["variant"]=variant,
                ["options"]=new JObject {["points_mm"]=new JArray {new JArray(13,21),new JArray(bx,by)},["width_mm"]=width}}};
            JObject Batch(bool preview){var args=State();args["operations"]=new JArray(operation.DeepClone());args["preview"]=preview;args["include_preview_mesh"]=preview;return args;}
            JObject oracle;var transaction=app.TransactionManager.StartTransaction((_Document)doc,"S1 direct straight slot oracle, aborted");
            try
            {
                var g=app.TransientGeometry;var a=g.CreatePoint2d(1.3,2.1);var b=g.CreatePoint2d(bx/10,by/10);
                if(variant=="slot_center_to_center") sketch.AddStraightSlotByCenterToCenter(a,b,width/10);
                else if(variant=="slot_overall") sketch.AddStraightSlotByOverall(a,b,width/10);
                else sketch.AddStraightSlotBySlotCenter(a,b,width/10);
                sketch.Solve();doc.Update2(true);oracle=Snapshot();
            }
            finally {transaction.Abort();}
            if(!JToken.DeepEquals(Normalize(before),Normalize(Snapshot())) || !journal.TryRestoreRevision(documentId,revision)) throw new InvalidOperationException("Straight slot oracle rollback failed.");
            var preview=Call("atomic_batch",Batch(true));
            if(journal.Revision(documentId)!=revision || !JToken.DeepEquals(before,Snapshot())) throw new InvalidOperationException("Straight slot preview mutated fixture.");
            var ghost=((JArray)preview["preview_mesh"]!["sketches"]!).Single(s=>(string?)s["sketch_name"]==sketch.Name);
            var expected=(JArray)oracle["entities"]!.DeepClone();foreach(JObject entity in expected) entity.Remove("id");
            if((bool)ghost["partial"]! || !JToken.DeepEquals(expected,ghost["native_entities"])) throw new InvalidOperationException("Straight slot preview differs from native API.");
            Call("atomic_batch",Batch(false));var after=Snapshot();
            if((bool)after["partial"]! || !JToken.DeepEquals(Normalize(oracle),Normalize(after)) || journal.Revision(documentId)==revision) throw new InvalidOperationException("Straight slot commit differs from native API.");
            rows.Add(new JObject {["variant"]=variant,["rotation_deg"]=degrees,["width_mm"]=width,["native_entities"]=after["entities"]!.DeepClone(),
                ["outcome"]="PASS native geometry/constraints, exact preview rollback and commit"});
            var cleanup=app.TransactionManager.StartTransaction((_Document)doc,"S1 straight slot batch cleanup");try {sketch.Delete();cleanup.End();}catch {cleanup.Abort();throw;}
        }
    }
}
