using System.Runtime.InteropServices;
using Inventor;
using Newtonsoft.Json.Linq;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;
using Bimwright.Ipt.Shared.Plugin;

internal static partial class Program
{
    // Staged desktop oracle: the clicks are performed through Computer Use,
    // while this helper reads native geometry and owns only an unsaved fixture.
    private static int EllipseUiProbe(string output,string mode,string family="ellipse")
    {
        string title=family=="arcslot" ? "S1_ArcSlotInput_Oracle.ipt" : family=="polygon" ? "S1_PolygonInput_Oracle.ipt" : family=="slot" ? "S1_SlotInput_Oracle.ipt" : family=="arc" ? "S1_ArcInput_Oracle.ipt" : "S1_EllipseInput_Oracle.ipt";
        var report=new JObject { ["captured_utc"]=DateTime.UtcNow.ToString("O"),["mode"]=mode,
            ["evidence"]="Native desktop "+family+" command, Computer Use clicks; not Quest or physical" };
        if(family=="arc") mode=mode.Replace("arc-ui-","ellipse-ui-");
        if(family=="slot") mode=mode.Replace("slot-ui-","ellipse-ui-");
        if(family=="polygon") mode=mode.Replace("polygon-ui-","ellipse-ui-");
        if(family=="arcslot") mode=mode.Replace("arcslot-ui-","ellipse-ui-");
        try
        {
            var clsid=Type.GetTypeFromProgID("Inventor.Application")!.GUID;
            GetActiveObject(ref clsid,IntPtr.Zero,out var active);var app=(Inventor.Application)active;
            PartDocument doc;
            bool prepare=mode=="ellipse-ui-prepare" || mode=="ellipse-ui-prepare-sheet";
            if(prepare)
            {
                if(app.Documents.Count!=0) throw new InvalidOperationException("Prepare requires no open documents; no user document may be changed.");
                string template=mode=="ellipse-ui-prepare-sheet"
                    ? app.FileManager.GetTemplateFile(DocumentTypeEnum.kPartDocumentObject,SystemOfMeasureEnum.kDefaultSystemOfMeasure,DraftingStandardEnum.kDefault_DraftingStandard,"{9C464203-9BAE-11D3-8BAD-0060B0CE6BB4}")
                    : app.FileManager.GetTemplateFile(DocumentTypeEnum.kPartDocumentObject);
                doc=(PartDocument)app.Documents.Add(DocumentTypeEnum.kPartDocumentObject,template,true);
                doc.DisplayName=title;
            }
            else
            {
                if(app.Documents.Count!=1 || app.ActiveDocument is not PartDocument part || part.DisplayName!=title || !string.IsNullOrEmpty(part.FullFileName))
                    throw new InvalidOperationException("Exact single unsaved oracle fixture required.");
                doc=part;
            }
            report["document_id"]=doc.InternalName;
            if(prepare || mode=="ellipse-ui-next")
            {
                if(app.ActiveEditObject is PlanarSketch editing) editing.ExitEdit();
                var sketch=doc.ComponentDefinition.Sketches.Add(doc.ComponentDefinition.WorkPlanes[3]);
                sketch.Name=(family=="arcslot" ? "S1_ArcSlotInput_" : family=="polygon" ? "S1_PolygonInput_" : family=="slot" ? "S1_SlotInput_" : family=="arc" ? "S1_ArcInput_" : "S1_EllipseInput_")+doc.ComponentDefinition.Sketches.Count;sketch.Edit();
                report["sketch_name"]=sketch.Name;
            }
            else if(mode=="ellipse-ui-topology-reconstruct" && family=="arc") RunTopologyReconstructionProbe(app,doc,report);
            else if(mode=="ellipse-ui-topology-relations" && family=="arc") RunTopologyRelationsProbe(app,doc,report);
            else if(mode=="ellipse-ui-topology-constraint-replay" && family=="arc") RunTopologyConstraintReplayProbe(app,doc,report);
            else if(mode=="ellipse-ui-line-split-dependencies" && family=="arc") RunLineSplitDependencyProbe(app,doc,report);
            else if(mode=="ellipse-ui-fillet-research" && family=="arc") RunSketchFilletResearchProbe(app,doc,report);
            else if(mode=="ellipse-ui-fillet-radius" && family=="arc") RunSketchFilletRadiusProbe(app,doc,report);
            else if(mode=="ellipse-ui-bmp-images" && family=="arc") RunBmpImageProbe(app,doc,report);
            else if(mode=="ellipse-ui-bmp-lifecycle" && family=="arc") RunBmpImageLifecycleProbe(app,doc,report);
            else if(mode=="ellipse-ui-bmp-product" && family=="arc") RunImageBatchProbe(app,doc,report);
            else if(mode=="ellipse-ui-spline-batch" && family=="arc") RunSplinePointBatches(app,doc,report);
            else if(mode=="ellipse-ui-spline-methods" && family=="arc") RunSplinePointBatches(app,doc,report,allFitMethods:true);
            else if(mode=="ellipse-ui-spline-mixed-batch" && family=="arc") RunSplinePointBatches(app,doc,report,true);
            else if(mode=="ellipse-ui-spline-constraints" && family=="arc") RunSplineConstraintAccess(app,doc,report);
            else if(mode=="ellipse-ui-spline-options" && family=="arc") RunSplineOptions(app,doc,report);
            else if(mode=="ellipse-ui-spline-readonly-batch" && family=="arc") RunSplinePointBatches(app,doc,report,true,true);
            else if(mode=="ellipse-ui-threepoint-batch" && family=="arc") RunThreePointArcBatches(app,doc,report);
            else if(mode=="ellipse-ui-batch" && family=="arcslot") RunArcSlotBatches(app,doc,report);
            else if(mode=="ellipse-ui-batch" && family=="slot") RunStraightSlotBatches(app,doc,report);
            else if(mode=="ellipse-ui-batch" && family=="polygon") RunPolygonBatches(app,doc,report);
            else if(mode=="ellipse-ui-batch") RunEllipseBatches(app,doc,report,family);
            else if(mode=="ellipse-ui-api" && family=="ellipse")
            {
                var sketch=doc.ComponentDefinition.Sketches[doc.ComponentDefinition.Sketches.Count];
                var transaction=app.TransactionManager.StartTransaction((_Document)doc,"S1 ellipse axis order API, aborted");
                try
                {
                    var g=app.TransientGeometry;var ellipse=sketch.SketchEllipses.Add(g.CreatePoint2d(10,10),g.CreateUnitVector2d(.8,.6),1,2);
                    report["major_radius_mm"]=ellipse.MajorRadius*10;report["minor_radius_mm"]=ellipse.MinorRadius*10;
                    if(Math.Abs(ellipse.MajorRadius-1)>1e-9 || Math.Abs(ellipse.MinorRadius-2)>1e-9) throw new InvalidOperationException("Native API changed axis order.");
                }
                finally {transaction.Abort();}
            }
            else if(mode=="ellipse-ui-top")
            {
                var camera=app.ActiveView.Camera;camera.ViewOrientationType=ViewOrientationTypeEnum.kTopViewOrientation;
                camera.Perspective=false;camera.Apply();
            }
            else if(mode=="ellipse-ui-read")
            {
                report["perspective"]=app.ActiveView.Camera.Perspective;
                var sketches=new JArray();report["sketches"]=sketches;
                foreach(PlanarSketch sketch in doc.ComponentDefinition.Sketches)
                {
                    var ellipses=new JArray();var arcs=new JArray();var lines=new JArray();
                    var splines=new JArray();
                    sketches.Add(new JObject { ["name"]=sketch.Name,["ellipses"]=ellipses,["arcs"]=arcs,["lines"]=lines,["splines"]=splines });
                    foreach(SketchSpline spline in sketch.SketchSplines)
                    {
                        var points=new JArray();for(int i=1;i<=spline.FitPointCount;i++) {var p=spline.FitPoint[i].Geometry;points.Add(new JArray(p.X*10,p.Y*10));}
                        splines.Add(new JObject {["variant"]="fit",["closed"]=spline.Closed,["fit_method"]=(int)spline.FitMethod,["points_mm"]=points});
                    }
                    foreach(SketchControlPointSpline spline in sketch.SketchControlPointSplines)
                    {
                        var points=new JArray();for(int i=1;i<=spline.ControlPointCount;i++) {var p=spline.ControlPoint[i].Geometry;points.Add(new JArray(p.X*10,p.Y*10));}
                        splines.Add(new JObject {["variant"]="control",["closed"]=spline.IsClosed,["points_mm"]=points});
                    }
                    foreach(SketchLine line in sketch.SketchLines)
                    {
                        var start=line.StartSketchPoint.Geometry;var end=line.EndSketchPoint.Geometry;
                        lines.Add(new JObject {["start_mm"]=new JArray(start.X*10,start.Y*10),["end_mm"]=new JArray(end.X*10,end.Y*10),["construction"]=line.Construction});
                    }
                    foreach(SketchEllipse ellipse in sketch.SketchEllipses)
                    {
                        var center=ellipse.CenterSketchPoint.Geometry;var axis=ellipse.MajorAxisVector;
                        ellipses.Add(new JObject { ["center_mm"]=new JArray(center.X*10,center.Y*10),
                            ["major_radius_mm"]=ellipse.MajorRadius*10,["minor_radius_mm"]=ellipse.MinorRadius*10,
                            ["major_axis"]=new JArray(axis.X,axis.Y),["constraint_count"]=sketch.GeometricConstraints.Count });
                    }
                    foreach(SketchArc arc in sketch.SketchArcs)
                    {
                        var center=arc.CenterSketchPoint.Geometry;var start=arc.StartSketchPoint.Geometry;var end=arc.EndSketchPoint.Geometry;
                        arcs.Add(new JObject {["center_mm"]=new JArray(center.X*10,center.Y*10),["start_mm"]=new JArray(start.X*10,start.Y*10),
                            ["end_mm"]=new JArray(end.X*10,end.Y*10),["radius_mm"]=arc.Radius*10,["start_deg"]=arc.StartAngle*180/Math.PI,
                            ["sweep_deg"]=arc.SweepAngle*180/Math.PI,["constraint_count"]=sketch.GeometricConstraints.Count});
                    }
                }
            }
            else if(mode=="ellipse-ui-cleanup")
            {
                if(app.ActiveEditObject is PlanarSketch editing) editing.ExitEdit();
                doc.Close(true);report["remaining_documents"]=app.Documents.Count;
            }
            else throw new ArgumentException("Unknown ellipse desktop oracle stage.");
            report["outcome"]="PASS";return 0;
        }
        catch(Exception ex) {report["outcome"]="FAIL";report["error"]=ex.ToString();return 1;}
        finally {System.IO.File.WriteAllText(output,report.ToString());Console.WriteLine(report["outcome"]);}
    }

    private static void RunEllipseBatches(Inventor.Application app,PartDocument doc,JObject report,string family="ellipse")
    {
        if(app.ActiveEditObject is PlanarSketch editing) editing.ExitEdit();
        var trackerType=typeof(InventorCommandRegistry).Assembly.GetType("Bimwright.Ipt.Shared.Plugin.CadEventTracker",true)!;
        using var tracker=(IDisposable)Activator.CreateInstance(trackerType,app)!;
        var journal=(CadEventJournal)trackerType.GetProperty("Journal")!.GetValue(tracker)!;
        var commands=InventorCommandRegistry.Build(new PluginOptions(2027,false,false,0));
        var context=new InventorCommandContext {Application=app,Events=journal,Commands=commands,AllowExperimental=true,InventorYear=2027};
        string documentId="doc_"+doc.InternalName;var rows=new JArray();report["batch_cases"]=rows;
        report["kind"]=doc.ComponentDefinition is SheetMetalComponentDefinition ? "sheetmetal" : "part";
        report["evidence"]="Native API oracle versus atomic_batch "+family+" preview/commit; not desktop inference, Quest or physical";
        JObject State()=>new() {["document_id"]=documentId,["expected_revision"]=journal.Revision(documentId)};
        JObject Call(string name,JObject args)
        {var result=commands[name].Execute(context,args);if(!result.Ok) throw new InvalidOperationException(name+": "+result.Error?.Code+" "+result.Error?.Message);return (JObject)result.Data!;}
        JObject Normalize(JObject snapshot)
        {var copy=(JObject)snapshot.DeepClone();copy.Remove("revision");foreach(var item in copy.Descendants().OfType<JObject>()) {item.Remove("id");item.Remove("entity_id");}return copy;}
        foreach(var radii in family=="arc" ? new[] {(20d,90d),(20d,-90d),(20d,270d),(20d,-270d)} : new[] {(20d,10d),(10d,20d),(20d,20d)})
        foreach(double degrees in family=="arc" ? new[] {0d,170d} : new[] {0d,53.13010235415598,175.6})
        {
            var sketch=doc.ComponentDefinition.Sketches.Add(doc.ComponentDefinition.WorkPlanes[3]);sketch.Name="S1_EllipseBatch_"+doc.ComponentDefinition.Sketches.Count;
            string sketchId=(string)((JArray)Call("get_design_context_xr",State())["sketches"]!).Single(s=>(string?)s["name"]==sketch.Name)["sketch_id"]!;
            JObject Snapshot(){var args=State();args["sketch_id"]=sketchId;return Call("get_sketch_editor_xr",args);}
            var before=Snapshot();string revision=journal.Revision(documentId);
            var operation=new JObject {["command"]="edit_sketch",["arguments"]=new JObject {["sketch_id"]=sketchId,["variant"]="ellipse",
                ["options"]=new JObject {["points_mm"]=new JArray {new JArray(13,21)},["major_radius_mm"]=radii.Item1,["minor_radius_mm"]=radii.Item2,["rotation_deg"]=degrees}}};
            if(family=="arc")
            {
                operation["arguments"]!["variant"]="arc_center";
                operation["arguments"]!["options"]=new JObject {["points_mm"]=new JArray {new JArray(13,21)},
                    ["radius_mm"]=radii.Item1,["start_deg"]=degrees,["sweep_deg"]=radii.Item2};
            }
            JObject Batch(bool preview){var args=State();args["operations"]=new JArray(operation.DeepClone());args["preview"]=preview;args["include_preview_mesh"]=preview;return args;}
            JObject oracle;var transaction=app.TransactionManager.StartTransaction((_Document)doc,"S1 direct ellipse oracle, aborted");
            try
            {
                double angle=degrees*Math.PI/180;var g=app.TransientGeometry;
                if(family=="arc") sketch.SketchArcs.AddByCenterStartSweepAngle(g.CreatePoint2d(1.3,2.1),radii.Item1/10,angle,radii.Item2*Math.PI/180);
                else sketch.SketchEllipses.Add(g.CreatePoint2d(1.3,2.1),g.CreateUnitVector2d(Math.Cos(angle),Math.Sin(angle)),radii.Item1/10,radii.Item2/10);
                sketch.Solve();doc.Update2(true);oracle=Snapshot();
            }
            finally {transaction.Abort();}
            if(!JToken.DeepEquals(Normalize(before),Normalize(Snapshot())) || !journal.TryRestoreRevision(documentId,revision)) throw new InvalidOperationException("Ellipse oracle rollback failed.");
            var preview=Call("atomic_batch",Batch(true));
            if(journal.Revision(documentId)!=revision || !JToken.DeepEquals(before,Snapshot())) throw new InvalidOperationException("Ellipse preview mutated the fixture.");
            var ghost=((JArray)preview["preview_mesh"]!["sketches"]!).Single(s=>(string?)s["sketch_name"]==sketch.Name);
            var expected=(JArray)oracle["entities"]!.DeepClone();foreach(JObject entity in expected) entity.Remove("id");
            if((bool)ghost["partial"]! || !JToken.DeepEquals(expected,ghost["native_entities"])) throw new InvalidOperationException("Ellipse preview differs from native API.");
            Call("atomic_batch",Batch(false));var after=Snapshot();
            if((bool)after["partial"]! || !JToken.DeepEquals(Normalize(oracle),Normalize(after)) || journal.Revision(documentId)==revision) throw new InvalidOperationException("Ellipse commit differs from native API.");
            rows.Add(new JObject {["first_radius_mm"]=radii.Item1,["second_radius_mm"]=radii.Item2,["rotation_deg"]=degrees,
                ["native_entities"]=after["entities"]!.DeepClone(),["outcome"]="PASS native geometry/constraints, exact preview rollback and commit"});
            if(family=="arc")
            {
                var row=(JObject)rows.Last!;row.Remove("first_radius_mm");row.Remove("second_radius_mm");row.Remove("rotation_deg");
                row["radius_mm"]=radii.Item1;row["start_deg"]=degrees;row["sweep_deg"]=radii.Item2;
            }
            var cleanup=app.TransactionManager.StartTransaction((_Document)doc,"S1 ellipse batch cleanup");try {sketch.Delete();cleanup.End();}catch {cleanup.Abort();throw;}
        }
    }
}
