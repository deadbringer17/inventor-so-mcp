using System.Reflection;
using Inventor;
using Newtonsoft.Json.Linq;
using Bimwright.Ipt.Shared.Infrastructure;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Plugin;

internal static partial class Program
{
    private static void ProbeStretch(global::Inventor.Application app,PartDocument fixture,
        IReadOnlyDictionary<string,IInventorCommand> commands,InventorCommandContext context,CadEventJournal journal,JArray rows)
    {
        var g=app.TransientGeometry;string documentId="doc_"+fixture.InternalName;
        JObject State()=>new() {["document_id"]=documentId,["expected_revision"]=journal.Revision(documentId)};
        JObject Call(string name,JObject args)
        {var result=commands[name].Execute(context,args);if(!result.Ok) throw new InvalidOperationException(name+": "+result.Error?.Code+" "+result.Error?.Message);return (JObject)result.Data!;}
        foreach(string scenario in new[] {"line_end","line_both","whole_line","shared_point","whole_plus_endpoint","arc_end","circle_center","ellipse_center",
            "spline_end","control_end","whole_spline","whole_control","standalone_point","mixed","grounded_line","dimensioned_line_end"})
        foreach(bool remove in new[] {false,true})
        {
            Console.WriteLine("Native stretch case: "+scenario+", remove="+remove);
            var row=new JObject {["scenario"]=scenario,["remove_constraints"]=remove,["outcome"]="INCOMPLETE"};rows.Add(row);
            var selected=new List<(SketchEntity Entity,string Part)>();var directObjects=new List<SketchEntity>();
            var movingPoints=new List<SketchPoint>();var nativeEntities=new List<SketchEntity>();
            PlanarSketch sketch,foreign;
            void AddDirect(SketchEntity entity)
            {if(!directObjects.Any(e=>SameComObject(e,entity))) directObjects.Add(entity);}
            void Point(SketchEntity entity,string part,SketchPoint point)
            {selected.Add((entity,part));AddDirect((SketchEntity)point);if(!movingPoints.Any(p=>SameComObject(p,point))) movingPoints.Add(point);}
            void WholeLine(SketchLine line)
            {selected.Add(((SketchEntity)line,"Entity"));AddDirect((SketchEntity)line);AddDirect((SketchEntity)line.StartSketchPoint);AddDirect((SketchEntity)line.EndSketchPoint);
                movingPoints.Add(line.StartSketchPoint);movingPoints.Add(line.EndSketchPoint);}
            var seed=app.TransactionManager.StartTransaction((_Document)fixture,"S1 stretch fixture "+scenario);
            try
            {
                sketch=fixture.ComponentDefinition.Sketches.Add(fixture.ComponentDefinition.WorkPlanes[3]);sketch.Name="S1_Stretch_"+scenario+"_"+remove;
                var line=sketch.SketchLines.AddByTwoPoints(g.CreatePoint2d(.7,.4),g.CreatePoint2d(3.2,1.6));nativeEntities.Add((SketchEntity)line);
                switch(scenario)
                {
                    case "line_end": Point((SketchEntity)line,"End",line.EndSketchPoint);break;
                    case "line_both": Point((SketchEntity)line,"Start",line.StartSketchPoint);Point((SketchEntity)line,"End",line.EndSketchPoint);break;
                    case "whole_line":case "whole_plus_endpoint":case "grounded_line":
                        WholeLine(line);if(scenario=="whole_plus_endpoint") Point((SketchEntity)line,"End",line.EndSketchPoint);
                        if(scenario=="grounded_line") sketch.GeometricConstraints.AddGround((SketchEntity)line);break;
                    case "shared_point":
                        var connected=sketch.SketchLines.AddByTwoPoints(line.EndSketchPoint,g.CreatePoint2d(4.7,3.1));nativeEntities.Add((SketchEntity)connected);
                        if(!SameComObject(line.EndSketchPoint,connected.StartSketchPoint)) throw new InvalidOperationException("Stretch fixture endpoints are not shared natively.");
                        Point((SketchEntity)line,"End",line.EndSketchPoint);Point((SketchEntity)connected,"Start",connected.StartSketchPoint);break;
                    case "arc_end":
                        var arc=sketch.SketchArcs.AddByCenterStartSweepAngle(g.CreatePoint2d(6,3),1,.2,1.2);nativeEntities.Add((SketchEntity)arc);
                        Point((SketchEntity)arc,"End",arc.EndSketchPoint);break;
                    case "circle_center":
                        var circle=sketch.SketchCircles.AddByCenterRadius(g.CreatePoint2d(6,3),1);nativeEntities.Add((SketchEntity)circle);
                        Point((SketchEntity)circle,"Center",circle.CenterSketchPoint);break;
                    case "ellipse_center":
                        var ellipse=sketch.SketchEllipses.Add(g.CreatePoint2d(6,3),g.CreateUnitVector2d(1,0),1.2,.6);nativeEntities.Add((SketchEntity)ellipse);
                        Point((SketchEntity)ellipse,"Center",ellipse.CenterSketchPoint);break;
                    case "spline_end":case "control_end":case "whole_spline":case "whole_control":
                        var points=app.TransientObjects.CreateObjectCollection();points.Add(g.CreatePoint2d(6,3));points.Add(g.CreatePoint2d(7,4.1));points.Add(g.CreatePoint2d(8.2,3.4));
                        if(scenario=="spline_end" || scenario=="whole_spline")
                        {
                            var spline=sketch.SketchSplines.Add(points);nativeEntities.Add((SketchEntity)spline);
                            if(scenario=="spline_end") Point((SketchEntity)spline,"End",spline.EndSketchPoint);
                            else {selected.Add(((SketchEntity)spline,"Entity"));AddDirect((SketchEntity)spline);for(int i=1;i<=spline.FitPointCount;i++){AddDirect((SketchEntity)spline.get_FitPoint(i));movingPoints.Add(spline.get_FitPoint(i));}}
                        }
                        else
                        {
                            var spline=sketch.SketchControlPointSplines.Add(points);nativeEntities.Add((SketchEntity)spline);
                            if(scenario=="control_end") Point((SketchEntity)spline,"End",spline.EndSketchPoint);
                            else {selected.Add(((SketchEntity)spline,"Entity"));for(int i=1;i<=spline.ControlPointCount;i++){AddDirect((SketchEntity)spline.get_ControlPoint(i));movingPoints.Add(spline.get_ControlPoint(i));}}
                        }
                        break;
                    case "standalone_point":case "mixed":
                        var standalone=sketch.SketchPoints.Add(g.CreatePoint2d(6,3),false);nativeEntities.Add((SketchEntity)standalone);
                        Point((SketchEntity)standalone,"Point",standalone);if(scenario=="mixed") WholeLine(line);break;
                    case "dimensioned_line_end":
                        sketch.DimensionConstraints.AddTwoPointDistance(line.StartSketchPoint,line.EndSketchPoint,DimensionOrientationEnum.kAlignedDim,g.CreatePoint2d(2,3),false);
                        Point((SketchEntity)line,"End",line.EndSketchPoint);break;
                    default:throw new InvalidOperationException("Unknown stretch fixture.");
                }
                foreign=fixture.ComponentDefinition.Sketches.Add(fixture.ComponentDefinition.WorkPlanes[3]);foreign.Name="S1_Stretch_Foreign";
                foreign.SketchLines.AddByTwoPoints(g.CreatePoint2d(0,0),g.CreatePoint2d(1,1));seed.End();
            }
            catch {seed.Abort();throw;}
            var sketches=(JArray)Call("get_design_context_xr",State())["sketches"]!;
            string sketchId=(string)sketches.Single(s=>(string?)s["name"]==sketch.Name)["sketch_id"]!;
            string foreignId=(string)sketches.Single(s=>(string?)s["name"]==foreign.Name)["sketch_id"]!;
            JObject Snapshot(){var args=State();args["sketch_id"]=sketchId;return Call("get_sketch_editor_xr",args);}
            var before=Snapshot();string revision=journal.Revision(documentId);
            if((bool)before["partial"]!) throw new InvalidOperationException("Stretch fixture snapshot is partial.");
            var resolver=typeof(InventorCommandRegistry).Assembly.GetType("Bimwright.Ipt.Shared.Handlers.Experimental.SketchReferences",true)!;
            SketchEntity Resolve(string id)=>(SketchEntity)resolver.GetMethod("Resolve",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic)!.Invoke(null,new object[] {fixture,id,"sketch_entity"})!;
            var refs=new JArray(selected.Select(s=>
            {
                var item=((JArray)before["entities"]!).OfType<JObject>().Single(e=>SameComObject(Resolve((string)e["id"]!),s.Entity));
                return new JObject {["document_id"]=documentId,["revision"]=revision,["sketch_id"]=sketchId,["entity_id"]=item["id"],
                    ["type"]=item["type"],["subpart"]=s.Part};
            }));
            var op=new JObject {["command"]="edit_sketch",["arguments"]=new JObject {["sketch_id"]=sketchId,["variant"]="transform_stretch",
                ["options"]=new JObject {["references"]=refs,["delta_mm"]=new JArray(12,-8),["remove_constraints"]=remove}}};
            JObject Batch(JObject operation,bool preview=false){var args=State();args["operations"]=new JArray(operation.DeepClone());args["preview"]=preview;args["include_preview_mesh"]=preview;return args;}
            void Unchanged(){if(journal.Revision(documentId)!=revision || !JToken.DeepEquals(before,Snapshot())) throw new InvalidOperationException("Stretch rollback changed snapshot or revision.");}
            void Refuse(JObject invalid){var result=commands["atomic_batch"].Execute(context,Batch(invalid));if(result.Ok) throw new InvalidOperationException("Invalid stretch was accepted.");Unchanged();}
            foreach(var bad in new[] {("revision","old"),("document_id","other"),("type","text"),("subpart","Bogus")})
            {var invalid=(JObject)op.DeepClone();invalid["arguments"]!["options"]!["references"]![0]![bad.Item1]=bad.Item2;Refuse(invalid);}
            var owner=(JObject)op.DeepClone();owner["arguments"]!["sketch_id"]=foreignId;foreach(JObject r in (JArray)owner["arguments"]!["options"]!["references"]!) r["sketch_id"]=foreignId;Refuse(owner);
            var duplicate=(JObject)op.DeepClone();((JArray)duplicate["arguments"]!["options"]!["references"]!).Add(refs[0]!.DeepClone());Refuse(duplicate);
            var extra=(JObject)op.DeepClone();extra["arguments"]!["options"]!["copy"]=true;Refuse(extra);
            var malformed=(JObject)op.DeepClone();malformed["arguments"]!["options"]!["remove_constraints"]="yes";Refuse(malformed);
            var pointBefore=movingPoints.Select(p=>new[] {p.Geometry.X,p.Geometry.Y}).ToArray();
            JObject oracle;
            var direct=app.TransactionManager.StartTransaction((_Document)fixture,"S1 direct native stretch oracle, aborted");
            try
            {
                var collection=app.TransientObjects.CreateObjectCollection();foreach(var entity in directObjects) collection.Add(entity);
                sketch.MoveSketchObjects(collection,g.CreateVector2d(1.2,-.8),false,remove);sketch.Solve();fixture.Update2(true);oracle=Snapshot();
                if(scenario!="grounded_line" && scenario!="dimensioned_line_end")
                    for(int i=0;i<movingPoints.Count;i++)
                        if(!TransformEqual(new[] {pointBefore[i][0]+1.2,pointBefore[i][1]-.8},new[] {movingPoints[i].Geometry.X,movingPoints[i].Geometry.Y}))
                            throw new InvalidOperationException("Native stretch did not move its free selected point by the requested vector.");
            }
            finally {direct.Abort();}
            var restored=Snapshot();var baseline=(JObject)before.DeepClone();restored.Remove("revision");baseline.Remove("revision");
            if(!JToken.DeepEquals(restored,baseline) || !journal.TryRestoreRevision(documentId,revision)) throw new InvalidOperationException("Native stretch oracle failed exact rollback.");Unchanged();
            var preview=Call("atomic_batch",Batch(op,true));Unchanged();
            var ghost=((JArray)preview["preview_mesh"]!["sketches"]!).Single(s=>(string?)s["sketch_name"]==sketch.Name);
            var expectedGhost=(JArray)oracle["entities"]!.DeepClone();foreach(JObject entity in expectedGhost) entity.Remove("id");
            if((bool)ghost["partial"]! || !JToken.DeepEquals(expectedGhost,ghost["native_entities"])) throw new InvalidOperationException("Stretch preview differs from native API geometry.");
            Call("atomic_batch",Batch(op));var after=Snapshot();var expected=(JObject)oracle.DeepClone();var actual=(JObject)after.DeepClone();expected.Remove("revision");actual.Remove("revision");
            if((bool)after["partial"]! || journal.Revision(documentId)==revision || !JToken.DeepEquals(expected,actual)
                || !((JArray)before["entities"]!).Select(e=>(string)e["id"]!).ToHashSet(StringComparer.Ordinal).SetEquals(((JArray)after["entities"]!).Select(e=>(string)e["id"]!)))
                throw new InvalidOperationException("Stretch commit differs from native API geometry, constraints, dimensions or identities.");
            row["selected_references"]=refs.Count;row["distinct_native_objects"]=directObjects.Count;
            row["outcome"]="PASS native point vector, exact direct API snapshot/preview, stable identities, rollback and reference/option guards";
            var cleanup=app.TransactionManager.StartTransaction((_Document)fixture,"S1 completed stretch fixture cleanup");
            try {sketch.Delete();foreign.Delete();cleanup.End();}catch {cleanup.Abort();throw;}
        }
    }
}
