using Inventor;
using Newtonsoft.Json.Linq;

internal static partial class Program
{
    // Feasibility only: these primitives are not the desktop Scale command.
    // Every trial aborts; no production capability is advertised by this probe.
    private static void ProbeScaleApi(global::Inventor.Application app,PartDocument fixture,JArray rows)
    {
        var g=app.TransientGeometry;
        foreach(string kind in new[] {"line","connected_lines","circle","arc","ellipse","spline","control_point_spline","grounded_line","dimensioned_line"})
        foreach(string strategy in new[] {"move_to","move_objects"})
        foreach(bool defer in new[] {false,true})
        foreach(double factor in new[] {.5,2.0})
        {
            var row=new JObject {["kind"]=kind,["strategy"]=strategy,["factor"]=factor,["defer_updates"]=defer,["outcome"]="INCOMPLETE"};rows.Add(row);
            Console.WriteLine($"Scale API feasibility: {kind}, strategy={strategy}, factor={factor}, defer={defer}");
            int sketchesBefore=fixture.ComponentDefinition.Sketches.Count;
            var tx=app.TransactionManager.StartTransaction((_Document)fixture,"S1 scale API feasibility, aborted");
            try
            {
                var sketch=fixture.ComponentDefinition.Sketches.Add(fixture.ComponentDefinition.WorkPlanes[3]);
                sketch.Name="S1_ScaleApi_"+kind;
                var entities=new List<object>();var points=new List<SketchPoint>();var radii=new List<Action>();
                void Point(SketchPoint p){if(!points.Any(old=>SameComObject(old,p))) points.Add(p);}
                if(kind=="line" || kind=="connected_lines" || kind=="grounded_line" || kind=="dimensioned_line")
                {
                    var line=sketch.SketchLines.AddByTwoPoints(g.CreatePoint2d(.7,.4),g.CreatePoint2d(3.2,1.6));
                    entities.Add(line);Point(line.StartSketchPoint);Point(line.EndSketchPoint);
                    if(kind=="connected_lines")
                    {
                        var second=sketch.SketchLines.AddByTwoPoints(line.EndSketchPoint,g.CreatePoint2d(4.7,3.1));entities.Add(second);
                        Point(second.StartSketchPoint);Point(second.EndSketchPoint);
                        if(points.Count!=3) throw new InvalidOperationException("Native shared point was not deduplicated.");
                    }
                    if(kind=="grounded_line") sketch.GeometricConstraints.AddGround((SketchEntity)line);
                    if(kind=="dimensioned_line") sketch.DimensionConstraints.AddTwoPointDistance(line.StartSketchPoint,line.EndSketchPoint,DimensionOrientationEnum.kAlignedDim,g.CreatePoint2d(2,3),false);
                }
                else if(kind=="circle")
                {
                    var circle=sketch.SketchCircles.AddByCenterRadius(g.CreatePoint2d(6,3),1.2);entities.Add(circle);Point(circle.CenterSketchPoint);
                    double radius=circle.Radius;radii.Add(()=>circle.Radius=radius*factor);
                }
                else if(kind=="arc")
                {
                    var arc=sketch.SketchArcs.AddByCenterStartSweepAngle(g.CreatePoint2d(6,3),1.2,.2,1.2);entities.Add(arc);
                    Point(arc.CenterSketchPoint);Point(arc.StartSketchPoint);Point(arc.EndSketchPoint);
                }
                else if(kind=="ellipse")
                {
                    var ellipse=sketch.SketchEllipses.Add(g.CreatePoint2d(6,3),g.CreateUnitVector2d(.8,.6),1.2,.6);entities.Add(ellipse);Point(ellipse.CenterSketchPoint);
                    double major=ellipse.MajorRadius,minor=ellipse.MinorRadius;
                    radii.Add(()=>{if(factor>=1){ellipse.MajorRadius=major*factor;ellipse.MinorRadius=minor*factor;}else{ellipse.MinorRadius=minor*factor;ellipse.MajorRadius=major*factor;}});
                }
                else
                {
                    var controls=app.TransientObjects.CreateObjectCollection();controls.Add(g.CreatePoint2d(6,3));controls.Add(g.CreatePoint2d(7,4.1));controls.Add(g.CreatePoint2d(8.2,3.4));
                    if(kind=="spline") {var spline=sketch.SketchSplines.Add(controls);entities.Add(spline);for(int i=1;i<=spline.FitPointCount;i++) Point(spline.get_FitPoint(i));}
                    else {var spline=sketch.SketchControlPointSplines.Add(controls);entities.Add(spline);for(int i=1;i<=spline.ControlPointCount;i++) Point(spline.get_ControlPoint(i));}
                }
                sketch.Solve();fixture.Update2(true);
                var before=entities.Select(TransformSamples).ToArray();
                var targets=points.Select(p=>g.CreatePoint2d(.5+(p.Geometry.X-.5)*factor,.6+(p.Geometry.Y-.6)*factor)).ToArray();
                int geometricBefore=sketch.GeometricConstraints.Count,dimensionsBefore=sketch.DimensionConstraints.Count;
                double? radiusBefore=entities[0] is SketchCircle c ? c.Radius : null;
                bool deferredBefore=sketch.DeferUpdates;
                string? apiError=null;
                try
                {
                    sketch.DeferUpdates=defer;
                    for(int i=0;i<points.Count;i++)
                    {
                        if(strategy=="move_to") points[i].MoveTo(targets[i]);
                        else
                        {
                            var selection=app.TransientObjects.CreateObjectCollection();selection.Add(points[i]);
                            var current=points[i].Geometry;
                            sketch.MoveSketchObjects(selection,g.CreateVector2d(targets[i].X-current.X,targets[i].Y-current.Y),false,false);
                        }
                    }
                    foreach(var radius in radii) radius();
                }
                catch(Exception ex){apiError=ex.GetType().Name+": "+ex.Message;}
                finally {sketch.DeferUpdates=deferredBefore;}
                double[][] actual=Array.Empty<double[]>();string? resultError=null;
                try {sketch.Solve();fixture.Update2(true);actual=entities.Select(TransformSamples).ToArray();}
                catch(Exception ex){resultError=ex.GetType().Name+": "+ex.Message;}
                var expected=before.Select(samples=>samples.Select((v,i)=>(i%2==0 ? .5 : .6)+(v-(i%2==0 ? .5 : .6))*factor).ToArray()).ToArray();
                bool matches=apiError==null && resultError==null && expected.Select((samples,i)=>TransformEqual(samples,actual[i])).All(x=>x);
                if(radiusBefore.HasValue) matches &= Math.Abs(((SketchCircle)entities[0]).Radius-radiusBefore.Value*factor)<1e-6;
                row["before_samples_cm"]=new JArray(before.Select(v=>new JArray(v)));row["expected_samples_cm"]=new JArray(expected.Select(v=>new JArray(v)));row["actual_samples_cm"]=new JArray(actual.Select(v=>new JArray(v)));
                row["matches_uniform_scale"]=matches;row["api_error"]=apiError;
                row["result_error"]=resultError;
                row["geometric_constraints_before"]=geometricBefore;row["geometric_constraints_after"]=sketch.GeometricConstraints.Count;
                row["dimension_constraints_before"]=dimensionsBefore;row["dimension_constraints_after"]=sketch.DimensionConstraints.Count;
                if(geometricBefore!=sketch.GeometricConstraints.Count || dimensionsBefore!=sketch.DimensionConstraints.Count || sketch.DeferUpdates!=deferredBefore)
                    throw new InvalidOperationException("Scale primitive unexpectedly changed constraints or deferred solving state.");
                row["outcome"]=matches ? "OBSERVED uniform scale via native primitives; desktop parity unverified" : "OBSERVED primitive does not produce requested uniform scale";
            }
            finally {tx.Abort();}
            if(fixture.ComponentDefinition.Sketches.Count!=sketchesBefore) throw new InvalidOperationException("Scale feasibility rollback left a sketch.");
            row["rollback_sketch_count_restored"]=true;
        }
    }
}
