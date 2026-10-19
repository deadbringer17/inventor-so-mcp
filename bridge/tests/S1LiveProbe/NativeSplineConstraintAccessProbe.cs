using Inventor;
using Newtonsoft.Json.Linq;

internal static partial class Program
{
    private static void RunSplineConstraintAccess(Inventor.Application app,PartDocument doc,JObject report)
    {
        if(app.ActiveEditObject is PlanarSketch editing) editing.ExitEdit();
        report["evidence"]="Native API constraint access observations in owned unsaved fixture; not a product PASS, desktop oracle or Quest test.";
        var cases=new JArray();report["access_cases"]=cases;
        foreach(bool polygon in new[] {false,true})
        foreach(bool edit in new[] {false,true})
        foreach(string ground in new[] {"none","spline","point","side"})
        {
            var transaction=app.TransactionManager.StartTransaction((_Document)doc,"S1 spline Constraints access, aborted");
            try
            {
                var sketch=doc.ComponentDefinition.Sketches.Add(doc.ComponentDefinition.WorkPlanes[3]);
                var g=app.TransientGeometry;
                if(polygon) sketch.SketchLines.AddAsPolygon(3,g.CreatePoint2d(-10,-10),g.CreatePoint2d(-8,-10),true);
                var collection=app.TransientObjects.CreateObjectCollection();
                collection.Add(g.CreatePoint2d(1,2));collection.Add(g.CreatePoint2d(2,3));collection.Add(g.CreatePoint2d(3,2));
                var spline=sketch.SketchControlPointSplines.Add(collection);
                var row=new JObject {["polygon"]=polygon,["editing"]=edit,["ground_target"]=ground};cases.Add(row);
                if(ground!="none")
                {
                    try
                    {
                        SketchEntity target=ground=="spline" ? (SketchEntity)spline : ground=="point" ? (SketchEntity)spline.ControlPoint[2] : (SketchEntity)spline.ControlPolygonSide[1];
                        var constraint=sketch.GeometricConstraints.AddGround(target);row["ground_type"]=constraint.Type.ToString();
                    }
                    catch(Exception ex) {row["ground_error"]=ex.ToString();}
                }
                sketch.Solve();doc.Update2(true);
                if(edit) sketch.Edit();
                var constraints=new JArray();row["global_constraints"]=constraints;
                foreach(GeometricConstraint constraint in sketch.GeometricConstraints) constraints.Add(constraint.Type.ToString());
                var entities=new JArray();row["entities"]=entities;
                foreach(SketchEntity entity in sketch.SketchEntities)
                {
                    var item=new JObject {["type"]=entity.Type.ToString()};entities.Add(item);
                    try {var owners=new JArray();foreach(SketchEntity owner in entity.OwnedBy) owners.Add(owner.Type.ToString());item["owners"]=owners;}
                    catch(Exception ex) {item["owner_error"]=ex.ToString();}
                    try {var attached=new JArray();foreach(object constraint in entity.Constraints) {dynamic native=constraint;attached.Add(native.Type.ToString());}item["constraints"]=attached;}
                    catch(Exception ex) {item["constraint_error"]=ex.ToString();}
                }
            }
            finally {if(app.ActiveEditObject is PlanarSketch probeEditing) probeEditing.ExitEdit();transaction.Abort();}
        }
        report["observation_outcome"]="PASS sixteen observations completed, all transactions aborted; failures are recorded as data.";
    }
}
