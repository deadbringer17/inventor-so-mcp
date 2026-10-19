using Inventor;
using Newtonsoft.Json.Linq;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;
using Bimwright.Ipt.Shared.Plugin;

internal static partial class Program
{
    private static void RunArcSlotBatches(Inventor.Application app,PartDocument doc,JObject report)
    {
        if(app.ActiveEditObject is PlanarSketch editing) editing.ExitEdit();
        var trackerType=typeof(InventorCommandRegistry).Assembly.GetType("Bimwright.Ipt.Shared.Plugin.CadEventTracker",true)!;
        using var tracker=(IDisposable)Activator.CreateInstance(trackerType,app)!;
        var journal=(CadEventJournal)trackerType.GetProperty("Journal")!.GetValue(tracker)!;
        var commands=InventorCommandRegistry.Build(new PluginOptions(2027,false,false,0));
        var context=new InventorCommandContext {Application=app,Events=journal,Commands=commands,AllowExperimental=true,InventorYear=2027};
        string documentId="doc_"+doc.InternalName;var rows=new JArray();report["batch_cases"]=rows;
        report["kind"]=doc.ComponentDefinition is SheetMetalComponentDefinition ? "sheetmetal" : "part";
        report["evidence"]="Native API versus atomic_batch arc slots; geometry/constraints, preview rollback and commit. Not desktop inference, Quest or physical.";
        JObject State()=>new() {["document_id"]=documentId,["expected_revision"]=journal.Revision(documentId)};
        JObject Call(string name,JObject args)
        {var result=commands[name].Execute(context,args);if(!result.Ok) throw new InvalidOperationException(name+": "+result.Error?.Code+" "+result.Error?.Message);return (JObject)result.Data!;}
        JObject Normalize(JObject snapshot)
        {var copy=(JObject)snapshot.DeepClone();copy.Remove("revision");foreach(var item in copy.Descendants().OfType<JObject>()) {item.Remove("id");item.Remove("entity_id");}return copy;}
        foreach(string variant in new[] {"slot_center_arc","slot_three_point_arc"})
        foreach(double degrees in new[] {0d,53.13010235415598})
        foreach(double width in new[] {5d,20d})
        foreach(double sweep in new[] {90d,-90d,270d,-270d})
        {
            var sketch=doc.ComponentDefinition.Sketches.Add(doc.ComponentDefinition.WorkPlanes[3]);sketch.Name="S1_ArcSlotBatch_"+doc.ComponentDefinition.Sketches.Count;
            string sketchId=(string)((JArray)Call("get_design_context_xr",State())["sketches"]!).Single(s=>(string?)s["name"]==sketch.Name)["sketch_id"]!;
            JObject Snapshot(){var args=State();args["sketch_id"]=sketchId;return Call("get_sketch_editor_xr",args);}
            var before=Snapshot();string revision=journal.Revision(documentId);
                        double angle=degrees*Math.PI/180,radians=sweep*Math.PI/180;
            JArray Point(double offset)=>new(13+40*Math.Cos(angle+offset),21+40*Math.Sin(angle+offset));
            var points=variant=="slot_center_arc" ? new JArray {new JArray(13,21),Point(0)} : new JArray {Point(0),Point(radians),Point(radians/2)};
            var operation=new JObject {["command"]="edit_sketch",["arguments"]=new JObject {["sketch_id"]=sketchId,["variant"]=variant,
                ["options"]=new JObject {["points_mm"]=points,["width_mm"]=width,["sweep_deg"]=sweep}}};
            JObject Batch(bool preview){var args=State();args["operations"]=new JArray(operation.DeepClone());args["preview"]=preview;args["include_preview_mesh"]=preview;return args;}
            JObject oracle;var transaction=app.TransactionManager.StartTransaction((_Document)doc,"S1 direct arc slot oracle, aborted");
            try
            {
                var g=app.TransientGeometry;
                Point2d P(int index)=>g.CreatePoint2d((double)points[index][0]!/10,(double)points[index][1]!/10);
                if(variant=="slot_center_arc") sketch.AddArcSlotByCenterPointArc(P(0),P(1),radians,width/10);
                else sketch.AddArcSlotByThreePointArc(P(0),P(2),P(1),width/10);
                sketch.Solve();doc.Update2(true);oracle=Snapshot();
                var centerArc=sketch.SketchArcs.Cast<SketchArc>().Single(a=>a.Construction);
                var center=centerArc.CenterSketchPoint.Geometry;
                report["current_oracle"]=new JObject {["variant"]=variant,["sweep_deg"]=sweep,["rotation_deg"]=degrees,["width_mm"]=width,
                    ["center_mm"]=new JArray(center.X*10,center.Y*10),["radius_mm"]=centerArc.Radius*10,["native_sweep_deg"]=centerArc.SweepAngle*180/Math.PI,
                    ["arc_count"]=sketch.SketchArcs.Count,["snapshot"]=oracle.DeepClone()};
                if(sketch.SketchArcs.Count!=5 || (bool)oracle["partial"]! || Math.Abs(center.X-1.3)>1e-7 || Math.Abs(center.Y-2.1)>1e-7
                    || Math.Abs(centerArc.Radius-4)>1e-7 || Math.Abs(centerArc.SweepAngle-Math.Abs(radians))>1e-7)
                    throw new InvalidOperationException("Arc slot API point order/geometry or complete snapshot did not match the specified center arc.");
            }
            finally {transaction.Abort();}
            if(!JToken.DeepEquals(Normalize(before),Normalize(Snapshot())) || !journal.TryRestoreRevision(documentId,revision)) throw new InvalidOperationException("Arc slot oracle rollback failed.");
            var preview=Call("atomic_batch",Batch(true));
            if(journal.Revision(documentId)!=revision || !JToken.DeepEquals(before,Snapshot())) throw new InvalidOperationException("Arc slot preview mutated fixture.");
            var ghost=((JArray)preview["preview_mesh"]!["sketches"]!).Single(s=>(string?)s["sketch_name"]==sketch.Name);
            var expected=(JArray)oracle["entities"]!.DeepClone();foreach(JObject entity in expected) entity.Remove("id");
            if((bool)ghost["partial"]! || !JToken.DeepEquals(expected,ghost["native_entities"])) throw new InvalidOperationException("Arc slot preview differs from native API.");
            Call("atomic_batch",Batch(false));var after=Snapshot();
            if((bool)after["partial"]! || !JToken.DeepEquals(Normalize(oracle),Normalize(after)) || journal.Revision(documentId)==revision) throw new InvalidOperationException("Arc slot commit differs from native API.");
            rows.Add(new JObject {["variant"]=variant,["rotation_deg"]=degrees,["sweep_deg"]=sweep,["width_mm"]=width,["native_entities"]=after["entities"]!.DeepClone(),["native_constraints"]=after["constraints"]!.DeepClone(),
                ["outcome"]="PASS native geometry/constraints, exact preview rollback and commit"});
            report.Remove("current_oracle");
            var cleanup=app.TransactionManager.StartTransaction((_Document)doc,"S1 arc slot batch cleanup");try {sketch.Delete();cleanup.End();}catch {cleanup.Abort();throw;}
        }
    }
}
