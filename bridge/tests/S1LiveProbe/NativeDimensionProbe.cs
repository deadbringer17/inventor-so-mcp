using System.Reflection;
using Inventor;
using Newtonsoft.Json.Linq;
using Bimwright.Ipt.Shared.Infrastructure;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Plugin;

internal static partial class Program
{
    private static void ProbeDimensions(global::Inventor.Application app,PartDocument fixture,
        IReadOnlyDictionary<string,IInventorCommand> commands,InventorCommandContext context,CadEventJournal journal,JArray rows)
    {
        string documentId="doc_"+fixture.InternalName; var g=app.TransientGeometry;
        JObject State()=>new JObject {["document_id"]=documentId,["expected_revision"]=journal.Revision(documentId)};
        JObject Call(string name,JObject args)
        {var result=commands[name].Execute(context,args);if(!result.Ok) throw new InvalidOperationException(name+": "+result.Error?.Code+" "+result.Error?.Message);return (JObject)result.Data!;}
        foreach(string kind in new[] {"aligned","horizontal","vertical","angle_two_lines","angle_three_points","radius","diameter","arc_length","ellipse_major","ellipse_minor","offset","tangent_distance","offset_spline"})
        foreach(int mode in new[] {0,1,2})
        {
            bool driven=mode==1,expression=mode==2;
            Console.WriteLine("Native dimension case: "+kind+", driven="+driven+", expression="+expression);
            var sketch=fixture.ComponentDefinition.Sketches.Add(fixture.ComponentDefinition.WorkPlanes[3]); sketch.Name="S1_Dimension_"+kind+"_"+driven;
            var seed=app.TransactionManager.StartTransaction((_Document)fixture,"S1 dedicated dimension geometry");
            var selections=new List<(object entity,string part)>();
            try
            {
                SketchLine Line(double x,double y,double xx,double yy)=>sketch.SketchLines.AddByTwoPoints(g.CreatePoint2d(x,y),g.CreatePoint2d(xx,yy));
                if(kind=="radius" || kind=="diameter") selections.Add((sketch.SketchCircles.AddByCenterRadius(g.CreatePoint2d(2,2),1),"Entity"));
                else if(kind=="arc_length") selections.Add((sketch.SketchArcs.AddByCenterStartEndPoint(g.CreatePoint2d(2,2),g.CreatePoint2d(3,2),g.CreatePoint2d(2,3),true),"Entity"));
                else if(kind=="ellipse_major" || kind=="ellipse_minor") selections.Add((sketch.SketchEllipses.Add(g.CreatePoint2d(2,2),g.CreateUnitVector2d(1,0),2,1),"Entity"));
                else if(kind=="angle_three_points")
                {selections.Add((sketch.SketchPoints.Add(g.CreatePoint2d(3,3),false),"Point"));selections.Add((sketch.SketchPoints.Add(g.CreatePoint2d(1,1),false),"Point"));selections.Add((sketch.SketchPoints.Add(g.CreatePoint2d(3,1.4),false),"Point"));}
                else if(kind=="angle_two_lines") {selections.Add((Line(1,1,3,1.7),"Entity"));selections.Add((Line(1,1,3,3),"Entity"));}
                else if(kind=="offset") {selections.Add((Line(1,1,4,2),"Entity"));selections.Add((Line(1,3,4,4),"Entity"));}
                else if(kind=="tangent_distance")
                {selections.Add((sketch.SketchCircles.AddByCenterRadius(g.CreatePoint2d(1,1),.5),"Entity"));selections.Add((sketch.SketchCircles.AddByCenterRadius(g.CreatePoint2d(4,1.4),.7),"Entity"));}
                else if(kind=="offset_spline")
                {
                    var points=app.TransientObjects.CreateObjectCollection();points.Add(g.CreatePoint2d(1,1));points.Add(g.CreatePoint2d(2,3));points.Add(g.CreatePoint2d(4,2));
                    var original=sketch.SketchSplines.Add(points);var selected=app.TransientObjects.CreateObjectCollection();selected.Add(original);
                    var result=sketch.OffsetSketchEntitiesUsingDistance(selected,.5,true,false,false);
                    selections.Add((result[1],"Entity"));
                }
                else selections.Add((Line(1,1,3,2),"Entity"));
                seed.End();
            }
            catch {seed.Abort();throw;}
            string sketchId=(string)((JArray)Call("get_design_context_xr",State())["sketches"]!).OfType<JObject>().Single(s=>(string?)s["name"]==sketch.Name)["sketch_id"]!;
            JObject Snapshot() {var args=State();args["sketch_id"]=sketchId;return Call("get_sketch_editor_xr",args);}
            var before=Snapshot(); if((bool)before["partial"]!) throw new InvalidOperationException("Dimension fixture snapshot is partial: "+kind);
            string revision=journal.Revision(documentId); var refs=new JArray();
            var resolver=typeof(InventorCommandRegistry).Assembly.GetType("Bimwright.Ipt.Shared.Handlers.Experimental.SketchReferences",true)!;
            foreach(var selection in selections)
            {
                var item=((JArray)before["entities"]!).OfType<JObject>().Single(e=>SameComObject(selection.entity,
                    resolver.GetMethod("Resolve",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic)!.Invoke(null,new object[] {fixture,(string)e["id"]!,"sketch_entity"})!));
                refs.Add(new JObject {["document_id"]=documentId,["revision"]=revision,["sketch_id"]=sketchId,["entity_id"]=item["id"],["type"]=item["type"],["subpart"]=selection.part});
            }
            bool angular=kind.StartsWith("angle_",StringComparison.Ordinal);string unit=angular ? "value_deg" : "value_mm";
            var options=new JObject {["references"]=refs,["driven"]=driven,["text_mm"]=new JArray(50,50)};
            double requested=angular ? 60 : 12;
            if(!driven) options[unit]=requested;
            if(expression) {options.Remove(unit);options["expression"]=angular ? "60 deg" : "12 mm";options["parameter_name"]="S1Quote_"+kind;}
            if(kind=="ellipse_major" || kind=="ellipse_minor") options["positive_side"]=false;
            if(kind=="tangent_distance") {options["proximity_one_mm"]=new JArray(10,20);options["proximity_two_mm"]=new JArray(40,24);}
            var op=new JObject {["command"]="edit_sketch",["arguments"]=new JObject {["sketch_id"]=sketchId,["variant"]="dimension_"+kind,["options"]=options}};
            JObject Batch(bool preview) {var args=State();args["preview"]=preview;args["include_preview_mesh"]=preview;args["operations"]=new JArray(op.DeepClone());return args;}
            try
            {
                var preview=Call("atomic_batch",Batch(true));
                var ghost=((JArray)preview["preview_mesh"]!["sketches"]!).OfType<JObject>().Single(s=>(string?)s["sketch_name"]==sketch.Name);
                if(((JArray)ghost["dimensions"]!).Count!=((JArray)before["dimensions"]!).Count+1 || ((JArray)ghost["dimensions"]!).Any(d=>d["id"]!=null))
                    throw new InvalidOperationException("Dimension preview is missing or contains tentative identities.");
                if(revision!=journal.Revision(documentId) || !JToken.DeepEquals(before,Snapshot())) throw new InvalidOperationException("Dimension preview did not restore exact native snapshot/revision.");
                Call("atomic_batch",Batch(false));var after=Snapshot();
                var dimension=((JArray)after["dimensions"]!).OfType<JObject>().Single(d=>!((JArray)before["dimensions"]!).Any(b=>(string?)b["id"]==(string?)d["id"]));
                var actualText=(JArray)dimension["text_mm"]!;
                // Offset splines round annotation coordinates through their evaluator.
                // 0.00001 mm remains far below the 0.001 mm native stroke tolerance.
                if((bool)dimension["driven"]! !=driven || Math.Abs((double)actualText[0]-50)>1e-5 || Math.Abs((double)actualText[1]-50)>1e-5 || (bool)after["partial"]!)
                    throw new InvalidOperationException("Dimension did not preserve its native mode/placement: "+actualText+", driven="+dimension["driven"]+", partial="+after["partial"]);
                if(expression && ((string?)dimension["name"]!=(string?)options["parameter_name"] || string.IsNullOrWhiteSpace((string?)dimension["expression"])))
                    throw new InvalidOperationException("Native expression parameter name was not preserved.");
                if(driven ? !SameDimensionGeometry((JArray)before["entities"]!,(JArray)after["entities"]!) : Math.Abs((double)dimension[unit]! -requested)>1e-6)
                {
                    if(driven) {System.IO.File.WriteAllText("artifacts/s1-verification/dimension-driven-before.json",before.ToString());System.IO.File.WriteAllText("artifacts/s1-verification/dimension-driven-after.json",after.ToString());}
                    throw new InvalidOperationException("Driving value or driven geometry differs from the requested result.");
                }
                rows.Add(new JObject {["variant"]="dimension_"+kind,["driven"]=driven,["expression_mode"]=expression,["native_type"]=dimension["native_type"],
                    ["measured"]=dimension[unit],["outcome"]="PASS native dimension, placement, units, driven geometry and exact preview rollback"});
                if(mode==0) ProbeExistingDimension(app,fixture,commands,context,journal,sketch,sketchId,kind,angular,dimension,rows);
            }
            catch(Exception ex) {throw new InvalidOperationException("Native dimension "+kind+", driven="+driven+" failed.",ex);}
            var cleanup=app.TransactionManager.StartTransaction((_Document)fixture,"S1 completed dimension fixture cleanup");
            try {sketch.Delete();cleanup.End();}catch {cleanup.Abort();throw;}
        }
    }
    private static void ProbeExistingDimension(global::Inventor.Application app,PartDocument fixture,
        IReadOnlyDictionary<string,IInventorCommand> commands,InventorCommandContext context,CadEventJournal journal,
        PlanarSketch sketch,string sketchId,string kind,bool angular,JObject original,JArray rows)
    {
        string documentId="doc_"+fixture.InternalName,id=(string)original["id"]!,unit=angular ? "value_deg" : "value_mm";
        JObject State()=>new JObject {["document_id"]=documentId,["expected_revision"]=journal.Revision(documentId)};
        JObject Snapshot() {var args=State();args["sketch_id"]=sketchId;var result=commands["get_sketch_editor_xr"].Execute(context,args);if(!result.Ok) throw new InvalidOperationException(result.Error?.Message);return (JObject)result.Data!;}
        JObject Reference()=>new JObject {["document_id"]=documentId,["revision"]=journal.Revision(documentId),["sketch_id"]=sketchId,["entity_id"]=id,["type"]="dimension",["subpart"]="Entity"};
        InventorCommandResult Batch(string variant,JObject options,bool preview)
        {
            var args=State();args["preview"]=preview;args["include_preview_mesh"]=preview;
            args["operations"]=new JArray(new JObject {["command"]="edit_sketch",["arguments"]=new JObject {["sketch_id"]=sketchId,["variant"]=variant,["options"]=options}});
            return commands["atomic_batch"].Execute(context,args);
        }
        JObject Quota(JObject snapshot)=>((JArray)snapshot["dimensions"]!).OfType<JObject>().Single(d=>(string?)d["id"]==id);
        void Change(JObject values,Action<JObject,JObject> assert,string variant="dimension_update")
        {
            var before=Snapshot();string revision=journal.Revision(documentId);values["references"]=new JArray(Reference());
            var preview=Batch(variant,(JObject)values.DeepClone(),true);if(!preview.Ok) throw new InvalidOperationException("Dimension edit preview: "+preview.Error?.Message);
            var ghost=((JArray)preview.Data!["preview_mesh"]!["sketches"]!).OfType<JObject>().Single(s=>(string?)s["sketch_name"]==sketch.Name);
            if(((JArray)ghost["dimensions"]!).Any(d=>d["id"]!=null)) throw new InvalidOperationException("Dimension edit preview leaked tentative references.");
            if(revision!=journal.Revision(documentId) || !JToken.DeepEquals(before,Snapshot())) throw new InvalidOperationException("Dimension edit preview did not restore exact snapshot/revision.");
            var commit=Batch(variant,values,false);if(!commit.Ok) throw new InvalidOperationException("Dimension edit commit: "+commit.Error?.Message);
            var after=Snapshot();if((bool)after["partial"]!) throw new InvalidOperationException("Dimension edit snapshot is partial.");
            if(((JArray)before["entities"]!).Select(e=>(string?)e["id"]).Except(((JArray)after["entities"]!).Select(e=>(string?)e["id"])).Any()
                || ((JArray)before["entities"]!).Count!=((JArray)after["entities"]!).Count) throw new InvalidOperationException("Dimension edit recreated geometry.");
            if(variant!="dimension_delete" && ((JArray)after["dimensions"]!).Count!=((JArray)before["dimensions"]!).Count) throw new InvalidOperationException("Dimension edit recreated the dimension.");
            assert(before,after);
        }
        if(kind=="aligned")
        {
            var before=Snapshot();string revision=journal.Revision(documentId);
            foreach(string invalid in new[] {"stale","type","subpart","document","driven_value","wrong_unit","expression","position"})
            {
                var reference=Reference();var options=new JObject {["references"]=new JArray(reference),["text_mm"]=new JArray(10,20)};
                switch(invalid)
                {
                    case "stale":reference["revision"]="stale";break;case "type":reference["type"]="line";break;
                    case "subpart":reference["subpart"]="Start";break;case "document":reference["document_id"]="doc_foreign";break;
                    case "driven_value":options["driven"]=true;options["value_mm"]=15;break;
                    case "wrong_unit":options["value_deg"]=45;break;case "expression":options["expression"]="missing_parameter + ???";break;
                    case "position":options["text_mm"]=new JArray("bad",20);break;
                }
                if(Batch("dimension_update",options,false).Ok || revision!=journal.Revision(documentId) || !JToken.DeepEquals(before,Snapshot()))
                    throw new InvalidOperationException("Dimension edit guard failed: "+invalid);
            }
            var ownerTransaction=app.TransactionManager.StartTransaction((_Document)fixture,"S1 other dimension owner fixture");
            var other=fixture.ComponentDefinition.Sketches.Add(fixture.ComponentDefinition.WorkPlanes[3]);other.Name="S1_Other_Dimension_Owner";ownerTransaction.End();
            var resolver=typeof(InventorCommandRegistry).Assembly.GetType("Bimwright.Ipt.Shared.Handlers.Experimental.SketchReferences",true)!;
            string otherId=(string)resolver.GetMethod("Describe",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic)!.Invoke(null,new object[] {fixture,other,"sketch"})!;
            before=Snapshot();revision=journal.Revision(documentId);var forged=Reference();forged["sketch_id"]=otherId;
            var foreign=State();foreign["operations"]=new JArray(new JObject {["command"]="edit_sketch",["arguments"]=new JObject
                {["sketch_id"]=otherId,["variant"]="dimension_delete",["options"]=new JObject {["references"]=new JArray(forged)}}});
            if(commands["atomic_batch"].Execute(context,foreign).Ok || revision!=journal.Revision(documentId) || !JToken.DeepEquals(before,Snapshot()) || other.DimensionConstraints.Count!=0)
                throw new InvalidOperationException("Forged dimension owner was not refused atomically.");
            var ownerCleanup=app.TransactionManager.StartTransaction((_Document)fixture,"S1 completed owner guard cleanup");other.Delete();ownerCleanup.End();
        }
        double value=angular ? 75 : 15;
        Change(new JObject {[unit]=value},(before,after)=> {if(Math.Abs((double)Quota(after)[unit]! -value)>1e-6) throw new InvalidOperationException("Existing dimension value was not changed in its native unit.");});
        double expectedX=35,expectedY=42;
        if(kind=="arc_length")
        {
            // Arc-length annotations are normalized by Inventor on update.
            // Establish the expected placement through a direct API transaction,
            // independently of edit_sketch, and prove its full rollback first.
            var before=Snapshot();string revision=journal.Revision(documentId);
            var resolver=typeof(InventorCommandRegistry).Assembly.GetType("Bimwright.Ipt.Shared.Handlers.Experimental.SketchReferences",true)!;
            var native=(DimensionConstraint)resolver.GetMethod("Resolve",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic)!.Invoke(null,new object[] {fixture,id,"sketch_dimension"})!;
            var oracle=app.TransactionManager.StartTransaction((_Document)fixture,"S1 direct arc annotation oracle, aborted");
            try {native.TextPoint=app.TransientGeometry.CreatePoint2d(3.5,4.2);sketch.Solve();expectedX=native.TextPoint.X*10;expectedY=native.TextPoint.Y*10;}
            finally {oracle.Abort();}
            var restored=Snapshot();var expected=(JObject)before.DeepClone();restored.Remove("revision");expected.Remove("revision");
            if(!JToken.DeepEquals(expected,restored) || !journal.TryRestoreRevision(documentId,revision) || !JToken.DeepEquals(before,Snapshot()))
                throw new InvalidOperationException("Direct annotation oracle did not restore its exact native snapshot.");
        }
        Change(new JObject {["text_mm"]=new JArray(35,42)},(before,after)=>
        {var p=(JArray)Quota(after)["text_mm"]!;if(Math.Abs((double)p[0]-expectedX)>1e-5 || Math.Abs((double)p[1]-expectedY)>1e-5 || !SameDimensionGeometry((JArray)before["entities"]!,(JArray)after["entities"]!))
            {System.IO.File.WriteAllText("artifacts/s1-verification/dimension-edit-before.json",before.ToString());System.IO.File.WriteAllText("artifacts/s1-verification/dimension-edit-after.json",after.ToString());throw new InvalidOperationException("Moving dimension text changed native geometry or lost placement: "+p);}});
        Change(new JObject {["driven"]=true},(before,after)=> {if(!(bool)Quota(after)["driven"]! || !SameDimensionGeometry((JArray)before["entities"]!,(JArray)after["entities"]!)) throw new InvalidOperationException("Driving to driven changed geometry or mode.");});
        string sourceName="S1_Base_"+kind;
        var sourceTransaction=app.TransactionManager.StartTransaction((_Document)fixture,"S1 expression source parameter fixture");
        var sourceParameter=fixture.ComponentDefinition.Parameters.UserParameters.AddByExpression(sourceName,angular ? "30 deg" : "6 mm",angular ? "deg" : "mm");sourceTransaction.End();
        Change(new JObject {["driven"]=false,["expression"]="2 * "+sourceName,["parameter_name"]="S1_Edit_"+kind},(before,after)=>
        {var q=Quota(after);if((bool)q["driven"]! || (string?)q["name"]!="S1_Edit_"+kind || Math.Abs((double)q[unit]! -(angular ? 60 : 12))>1e-6) throw new InvalidOperationException("Driven to driving did not preserve its native expression/name/value.");});
        foreach(int update in new[] {1,2})
        {
            var dependency=app.TransactionManager.StartTransaction((_Document)fixture,"S1 dependent dimension update "+update);
            try {sourceParameter.Expression=angular ? (30+update)+" deg" : (6+update*.25).ToString(System.Globalization.CultureInfo.InvariantCulture)+" mm";
                fixture.Update2(false);sketch.Solve();dependency.End();}catch {dependency.Abort();throw;}
            var dependent=Snapshot();var q=Quota(dependent);double target=angular ? 60+update*2 : 12+update*.5;
            if((bool)dependent["partial"]! || Math.Abs((double)q[unit]! -target)>1e-6 || !(q["expression"]?.ToString() ?? "").Contains(sourceName,StringComparison.Ordinal)
                || sketch.ConstraintStatus==ConstraintStatusEnum.kOverConstrainedConstraintStatus)
                throw new InvalidOperationException("Named dimension dependency did not survive source parameter update "+update);
        }
        Change(new JObject(),(before,after)=> {if(((JArray)after["dimensions"]!).Any(d=>(string?)d["id"]==id)
            || ((JArray)after["dimensions"]!).Count!=((JArray)before["dimensions"]!).Count-1
            || !SameDimensionGeometry((JArray)before["entities"]!,(JArray)after["entities"]!)) throw new InvalidOperationException("Dimension deletion changed geometry or removed the wrong count.");},"dimension_delete");
        // Inventor can retain a model parameter that still references this source
        // after deleting its dimension. Both live only in the owned fixture and
        // are discarded when that document is closed without saving.
        rows.Add(new JObject {["variant"]="dimension_lifecycle_"+kind,["mutations"]=5,["dependency_updates"]=2,["text_requested_mm"]=new JArray(35,42),["text_native_mm"]=new JArray(expectedX,expectedY),
            ["source_parameter_cleanup"]="owned fixture closure without saving",
            ["outcome"]="PASS same native dimension: value, native text placement, driving/driven, expression/name, targeted deletion; exact preview rollback"});
    }
    private static bool SameDimensionGeometry(JArray before,JArray after)
    {
        if(before.Count!=after.Count) return false;
        for(int i=0;i<before.Count;i++)
        {
            var one=(JObject)before[i].DeepClone();var two=(JObject)after[i].DeepClone();
            foreach(string property in new[] {"point_mm","start_mm","end_mm","center_mm","strokes_mm"})
            {
                if(one[property] is not JArray a) continue;
                if(two[property] is not JArray b || a.Count!=b.Count) return false;
                // Inventor's spline solver can round free fit points on Solve.
                // Permit only 0.00001 mm drift; identity, kind and properties stay exact.
                for(int j=0;j<a.Count;j++) if(Math.Abs((double)a[j]-(double)b[j])>1e-5) return false;
                one.Remove(property);two.Remove(property);
            }
            if(!JToken.DeepEquals(one,two)) return false;
        }
        return true;
    }
}
