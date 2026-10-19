using Inventor;
using Newtonsoft.Json.Linq;
using Bimwright.Ipt.Shared.Handlers;
using System.Globalization;

internal static partial class Program
{
    // Installed interop exposes precisely five required AddByFillet arguments.
    // No trim or preserve-constraints switch is assumed or emulated here.
    private static void RunSketchFilletResearchProbe(Inventor.Application app,PartDocument doc,JObject report)
    {
        if(doc.ComponentDefinition.Sketches.Count!=1) throw new InvalidOperationException("Fillet research requires the original sketch only.");
        var sketch=doc.ComponentDefinition.Sketches[1];string originalName=sketch.Name;
        void Guard(bool empty)
        {
            if(app.Documents.Count!=1 || app.ActiveDocument is not PartDocument active || !TopologySame(active,doc)
                || doc.DisplayName!="S1_ArcInput_Oracle.ipt" || !string.IsNullOrEmpty(doc.FullFileName)
                || doc.DocumentType!=DocumentTypeEnum.kPartDocumentObject || doc.ComponentDefinition.Sketches.Count!=1
                || !TopologySame(doc.ComponentDefinition.Sketches[1],sketch) || sketch.Name!=originalName)
                throw new InvalidOperationException("Exact single owned unsaved arc oracle and original sketch required.");
            if(app.ActiveEditObject is object editing && !TopologySame(editing,sketch)) throw new InvalidOperationException("Another editing context is active.");
            if(empty && (sketch.SketchEntities.Count!=0 || sketch.GeometricConstraints.Count!=0 || sketch.DimensionConstraints.Count!=0 || sketch.TextBoxes.Count!=0 || sketch.SketchImages.Count!=0))
                throw new InvalidOperationException("Fillet research requires a completely empty sketch.");
        }
        Guard(true);var aliases=new Dictionary<string,string>(StringComparer.Ordinal);
        JObject Snapshot()=>FilletResearchReadback(doc,sketch,aliases);
        var baseline=Snapshot();var rows=new JArray();report["fillet_research_cases"]=rows;
        report["evidence"]="API RESEARCH SketchArcs.AddByFillet only; NOT desktop fillet/chamfer equivalence, a product capability, Quest or physical PASS. No invented Trim/Preserve options; every case Abort with exact native readback including parameters.";
        report["installed_signature"]="SketchArc AddByFillet(SketchEntity EntityOne, SketchEntity EntityTwo, object Radius, Point2d PointOnEntityOne, Point2d PointOnEntityTwo); all five required, interop 31.0.19200.0";
        report["matrix"]="27 cases/context: three curve pairs x (four independent side pairs with numeric radius/free sources, four with numeric radius/parametric sources, one ++ side pair with named-expression radius/free sources). Parametric source cases update source size twice; other cases update the radius input parameter twice.";
        report["requires_desktop_oracle"]="Meaning of pick points and solution selection, automatic trim versus retained curves, preserve/remove constraints options, radius expression associativity and desktop feature history are unverified. Native observations must not be converted into desktop defaults.";
        report["not_covered"]=new JArray("Chamfer: no native Sketch chamfer method found in installed interop", "A custom chamfer reconstruction and dependency transfer", "Complete side x expression x constraint Cartesian product", "Invalid radii, collinear/tangent/coincident/degenerate source pairs", "Projected curves, consumed profiles, downstream feature dependencies", "User interaction/desktop dialog, product reference remapping, Quest input/preview/commit");
        report["parameter_readback_units"]="All geometry/asserted lengths are mm; database_scalar in reused parameter snapshots explicitly retains native database units for exact rollback of arbitrary parameter types.";
        report["kind"]=doc.ComponentDefinition is SheetMetalComponentDefinition ? "sheetmetal" : "part";report["baseline"]=baseline.DeepClone();
        var g=app.TransientGeometry;
        foreach(string pair in new[]{"line-line","line-arc","arc-arc"})
        foreach(string profile in new[]{"free numeric radius","parametric sources numeric radius","free named radius expression"})
        foreach(int sideOne in new[]{1,-1})
        foreach(int sideTwo in new[]{1,-1})
        {
            bool constrained=profile=="parametric sources numeric radius",expression=profile=="free named radius expression";
            if(expression && (sideOne!=1 || sideTwo!=1)) continue;
            Guard(true);aliases.Clear();if(!JToken.DeepEquals(baseline,Snapshot())) throw new InvalidOperationException("Fillet baseline changed before case.");
            var row=new JObject {["pair"]=pair,["profile"]=profile,["side_one"]=sideOne,["side_two"]=sideTwo,["requires_desktop_oracle"]=true,["before"]=baseline.DeepClone()};rows.Add(row);
            var transaction=app.TransactionManager.StartTransaction((_Document)doc,"S1 AddByFillet API research, always aborted");
            try
            {
                // CurrentTransaction may expose an idle Unidentified transaction.
                if(transaction.HasParentTransaction) throw new InvalidOperationException("Research cannot run inside another transaction.");
                Guard(true);row["phase"]="create owned finite source curves";
                Point2d P(double x,double y)=>g.CreatePoint2d(UnitConvert.MmToCm(x),UnitConvert.MmToCm(y));
                Point2d Polar(double cx,double cy,double radius,double degrees)=>P(cx+radius*Math.Cos(UnitConvert.DegToRad(degrees)),cy+radius*Math.Sin(UnitConvert.DegToRad(degrees)));
                var radiusParameter=doc.ComponentDefinition.Parameters.UserParameters.AddByExpression("S1_Fillet_Radius","3 mm","mm");
                var sizeParameter=doc.ComponentDefinition.Parameters.UserParameters.AddByExpression("S1_Fillet_SourceSize","20 mm","mm");
                SketchEntity one,two;Point2d pickOne,pickTwo;
                if(pair=="line-line")
                {
                    one=(SketchEntity)sketch.SketchLines.AddByTwoPoints(P(-20,0),P(20,0));two=(SketchEntity)sketch.SketchLines.AddByTwoPoints(P(0,-20),P(0,20));
                    pickOne=P(sideOne*10,0);pickTwo=P(0,sideTwo*10);
                }
                else if(pair=="line-arc")
                {
                    one=(SketchEntity)sketch.SketchLines.AddByTwoPoints(P(-10,0),P(35,0));
                    two=(SketchEntity)sketch.SketchArcs.AddByCenterStartSweepAngle(P(0,0),UnitConvert.MmToCm(20),UnitConvert.DegToRad(-90),Math.PI);
                    pickOne=P(20+sideOne*8,0);pickTwo=Polar(0,0,20,sideTwo*25);
                }
                else
                {
                    one=(SketchEntity)sketch.SketchArcs.AddByCenterStartSweepAngle(P(0,0),UnitConvert.MmToCm(20),0,Math.PI);
                    two=(SketchEntity)sketch.SketchArcs.AddByCenterStartSweepAngle(P(20,0),UnitConvert.MmToCm(20),0,Math.PI);
                    pickOne=Polar(0,0,20,60+sideOne*15);pickTwo=Polar(20,0,20,120+sideTwo*15);
                }
                aliases["source_one"]=TopologyKey(one);aliases["source_two"]=TopologyKey(two);aliases["radius_user_parameter"]=TopologyKey(radiusParameter);aliases["source_size_user_parameter"]=TopologyKey(sizeParameter);
                foreach(var entry in new[]{(entity:one,role:"source_one"),(entity:two,role:"source_two")})
                {
                    if(entry.entity is SketchLine line)
                    {
                        aliases[entry.role+"_start"]=TopologyKey(line.StartSketchPoint);aliases[entry.role+"_end"]=TopologyKey(line.EndSketchPoint);
                        if(constrained)
                        {
                            bool vertical=pair=="line-line" && entry.role=="source_two";
                            GeometricConstraint axis=vertical ? (GeometricConstraint)sketch.GeometricConstraints.AddVertical(entry.entity,true) : (GeometricConstraint)sketch.GeometricConstraints.AddHorizontal(entry.entity,true);
                            aliases[entry.role+"_axis"]=TopologyKey(axis);
                            var dimension=sketch.DimensionConstraints.AddTwoPointDistance(line.StartSketchPoint,line.EndSketchPoint,DimensionOrientationEnum.kAlignedDim,P(-25,-25),false);
                            dimension.Parameter.Name="S1_Fillet_"+entry.role+"_Length";dimension.Parameter.Expression=(pair=="line-arc" ? "2.25 * " : "2 * ")+sizeParameter.Name;
                            aliases[entry.role+"_dimension"]=TopologyKey(dimension);aliases[entry.role+"_dimension_parameter"]=TopologyKey(dimension.Parameter);
                        }
                    }
                    else if(entry.entity is SketchArc arc)
                    {
                        aliases[entry.role+"_start"]=TopologyKey(arc.StartSketchPoint);aliases[entry.role+"_end"]=TopologyKey(arc.EndSketchPoint);aliases[entry.role+"_centre"]=TopologyKey(arc.CenterSketchPoint);
                        if(constrained)
                        {
                            var dimension=sketch.DimensionConstraints.AddRadius(entry.entity,P(-25,25),false);
                            dimension.Parameter.Name="S1_Fillet_"+entry.role+"_Radius";dimension.Parameter.Expression=sizeParameter.Name;
                            aliases[entry.role+"_dimension"]=TopologyKey(dimension);aliases[entry.role+"_dimension_parameter"]=TopologyKey(dimension.Parameter);
                        }
                    }
                }
                sketch.Solve();doc.Update2(false);row["source_state"]=Snapshot();row["source_lineage"]=TopologyRelationsLineage(doc,aliases);
                if(!FilletResearchOnCurve(one,pickOne,1e-6) || !FilletResearchOnCurve(two,pickTwo,1e-6)) throw new InvalidOperationException("Fixture pick points do not belong to the finite native sources after source solve.");
                row["pick_one_mm"]=new JArray(UnitConvert.CmToMm(pickOne.X),UnitConvert.CmToMm(pickOne.Y));row["pick_two_mm"]=new JArray(UnitConvert.CmToMm(pickTwo.X),UnitConvert.CmToMm(pickTwo.Y));
                object radius=expression ? (object)radiusParameter.Name : UnitConvert.MmToCm(3);
                row["radius_argument_kind"]=expression ? "named parameter expression string; acceptance/associativity under research" : "numeric database length from 3 mm";
                row["phase"]="call exact native AddByFillet signature";
                var fillet=sketch.SketchArcs.AddByFillet(one,two,radius,pickOne,pickTwo);aliases["returned_fillet_arc"]=TopologyKey(fillet);
                sketch.Solve();doc.Update2(false);row["after_add"]=Snapshot();row["after_add_lineage"]=TopologyRelationsLineage(doc,aliases);
                row["initial_returned_radius_mm"]=UnitConvert.CmToMm(fillet.Radius);
                if(Math.Abs(UnitConvert.CmToMm(fillet.Radius)-3)>1e-6) throw new InvalidOperationException("Returned fillet arc does not have the requested initial 3 mm radius.");
                row["initial_geometric_tangency"]=FilletResearchTangency(fillet,one,two);
                row["source_entity_state_changes"]=FilletResearchSourceChanges((JObject)row["source_state"]!,(JObject)row["after_add"]!,aliases);
                row["source_relation_survival"]=FilletResearchConstraintChanges((JObject)row["source_state"]!,(JObject)row["after_add"]!);
                if((bool?)row["initial_geometric_tangency"]?["finite_source_tangency_observed"]!=true || sketch.ConstraintStatus==ConstraintStatusEnum.kOverConstrainedConstraintStatus)
                    throw new InvalidOperationException("Initial returned arc did not independently verify as tangent to both finite native sources, or the solver reports a conflict.");
                var updates=new JArray();row["parameter_updates"]=updates;bool all=true;
                foreach(double value in constrained ? new[]{22d,18d} : new[]{4d,2d})
                {
                    row["phase"]="update explicit input parameter and observe native result";
                    var update=new JObject {["parameter_role"]=constrained ? "source size" : "radius input",["requested_mm"]=value};updates.Add(update);
                    try
                    {
                        Guard(false);if(constrained) sizeParameter.Expression=value.ToString(CultureInfo.InvariantCulture)+" mm";else radiusParameter.Expression=value.ToString(CultureInfo.InvariantCulture)+" mm";
                        doc.Update2(false);sketch.Solve();doc.Update2(false);update["after"]=Snapshot();update["lineage"]=TopologyRelationsLineage(doc,aliases);
                        double actual=UnitConvert.CmToMm(fillet.Radius);update["returned_radius_mm"]=actual;update["returned_fillet_key_unchanged"]=TopologyKey(fillet)==aliases["returned_fillet_arc"];
                        update["radius_tracks_input_parameter"]=Math.Abs(actual-value)<=1e-6;update["radius_remains_initial_numeric_value"]=Math.Abs(actual-3)<=1e-6;
                        update["geometric_tangency"]=FilletResearchTangency(fillet,one,two);
                        update["source_relation_survival"]=FilletResearchConstraintChanges((JObject)row["source_state"]!,(JObject)update["after"]!);
                        update["solver_status"]=sketch.ConstraintStatus.ToString();
                        double parameterValue=UnitConvert.CmToMm(Convert.ToDouble(constrained ? sizeParameter.Value : radiusParameter.Value,CultureInfo.InvariantCulture));
                        update["actual_input_parameter_mm"]=parameterValue;
                        // Association/trim/deletion observations deliberately have no
                        // invented success expectation. Report the actual graph and
                        // propagation, even if a named expression evaluated only once.
                        if(TopologyKey(fillet)!=aliases["returned_fillet_arc"] || !double.IsFinite(actual) || actual<=0 || Math.Abs(parameterValue-value)>1e-6
                            || (bool?)update["geometric_tangency"]?["finite_source_tangency_observed"]!=true || sketch.ConstraintStatus==ConstraintStatusEnum.kOverConstrainedConstraintStatus)
                            throw new InvalidOperationException("Returned native fillet identity/radius or solver diverged during the parameter observation.");
                        update["observation"]="NATIVE UPDATE OBSERVED; association and constraint survival remain explicit data, not desktop semantics";
                    }
                    catch(Exception ex)
                    {
                        all=false;update["observation"]="NATIVE EXCEPTION OR READBACK DIVERGENCE";update["error"]=ex.ToString();update["hresult"]="0x"+ex.HResult.ToString("X8");
                        try {update["failure_state"]=Snapshot();update["failure_lineage"]=TopologyRelationsLineage(doc,aliases);} catch(Exception readError) {update["failure_read_error"]=readError.ToString();}
                    }
                }
                if(!all) throw new InvalidOperationException("One or both native parameter observations failed; both attempts retained.");
                row["observation"]="API RESEARCH OBSERVATIONS COMPLETED; no desktop or constraint-preservation equivalence";Guard(false);
            }
            catch(Exception ex)
            {
                row["observation"]="NATIVE EXCEPTION OR READBACK DIVERGENCE";row["error"]=ex.ToString();row["hresult"]="0x"+ex.HResult.ToString("X8");
                try {row["failure_state"]=Snapshot();row["failure_lineage"]=TopologyRelationsLineage(doc,aliases);} catch(Exception readError) {row["failure_read_error"]=readError.ToString();}
            }
            finally
            {
                if(!TopologySame(app.TransactionManager.CurrentTransaction,transaction)) throw new InvalidOperationException("Owned fillet research transaction is no longer current; no unrelated transaction will be touched.");
                transaction.Abort();
            }
            Guard(true);aliases.Clear();var restored=Snapshot();row["rollback"]=restored;row["exact_rollback"]=JToken.DeepEquals(baseline,restored);
            if(!(bool)row["exact_rollback"]!) throw new InvalidOperationException("Fillet research Abort did not restore exact native geometry, constraints, dimensions and parameter baseline.");
        }
        report["case_count"]=rows.Count;report["completed_observations"]=rows.OfType<JObject>().Count(r=>(string?)r["observation"]=="API RESEARCH OBSERVATIONS COMPLETED; no desktop or constraint-preservation equivalence");
        report["initial_finite_source_tangencies_verified"]=rows.OfType<JObject>().Count(r=>(bool?)r["initial_geometric_tangency"]?["finite_source_tangency_observed"]==true);
        report["native_exceptions_or_divergences"]=rows.Count-(int)report["completed_observations"]!;report["final_state"]=Snapshot();Guard(true);
    }

    private static JObject FilletResearchReadback(PartDocument doc,PlanarSketch sketch,Dictionary<string,string> aliases)
    {
        var state=TopologyReplayReadback(doc,sketch,aliases);var tangents=new JArray();state["native_tangent_bindings"]=tangents;
        JObject Member(SketchEntity entity)=>new JObject {["key"]=TopologyKey(entity),["type"]=entity.Type.ToString(),["original_aliases"]=new JArray(aliases.Where(a=>a.Value==TopologyKey(entity)).Select(a=>a.Key))};
        foreach(GeometricConstraint constraint in sketch.GeometricConstraints)
            if(constraint is TangentSketchConstraint tangent) tangents.Add(new JObject {["constraint_key"]=TopologyKey(constraint),["entity_one"]=Member(tangent.EntityOne),["entity_two"]=Member(tangent.EntityTwo)});
        var radii=new JArray();state["native_radius_dimension_bindings"]=radii;
        foreach(DimensionConstraint dimension in sketch.DimensionConstraints)
            if(dimension is RadiusDimConstraint radius) radii.Add(new JObject {["dimension_key"]=TopologyKey(dimension),["native_entity"]=Member(radius.Entity),["parameter_key"]=TopologyKey(dimension.Parameter),["expression"]=dimension.Parameter.Expression,["value_mm"]=UnitConvert.CmToMm(Convert.ToDouble(dimension.Parameter.Value,CultureInfo.InvariantCulture))});
        return state;
    }

    private static bool FilletResearchOnCurve(SketchEntity entity,Point2d point,double toleranceMm)
    {
        double tolerance=UnitConvert.MmToCm(toleranceMm);
        if(entity is SketchLine line)
        {
            var a=line.StartSketchPoint.Geometry;var b=line.EndSketchPoint.Geometry;double dx=b.X-a.X,dy=b.Y-a.Y,len2=dx*dx+dy*dy;
            if(len2<=0) return false;double parameter=((point.X-a.X)*dx+(point.Y-a.Y)*dy)/len2;
            return parameter>=-tolerance/Math.Sqrt(len2) && parameter<=1+tolerance/Math.Sqrt(len2) && Math.Abs((point.X-a.X)*dy-(point.Y-a.Y)*dx)/Math.Sqrt(len2)<=tolerance;
        }
        if(entity is SketchArc arc)
        {
            var centre=arc.CenterSketchPoint.Geometry;double radius=arc.Radius;if(radius<=0 || Math.Abs(point.DistanceTo(centre)-radius)>tolerance) return false;
            double angle=Math.Atan2(point.Y-centre.Y,point.X-centre.X)-arc.StartAngle;angle%=2*Math.PI;if(angle<0) angle+=2*Math.PI;
            return angle<=arc.SweepAngle+tolerance/radius || 2*Math.PI-angle<=tolerance/radius;
        }
        return false;
    }

    private static JObject FilletResearchTangency(SketchArc fillet,SketchEntity one,SketchEntity two)
    {
        var result=new JObject {["distance_tolerance_mm"]=1e-5,["normalized_tangent_cross_tolerance"]=1e-8};
        try
        {
            bool At(Point2d point,SketchEntity source)
            {
                if(!FilletResearchOnCurve(source,point,1e-5)) return false;
                double fx=point.X-fillet.CenterSketchPoint.Geometry.X,fy=point.Y-fillet.CenterSketchPoint.Geometry.Y;
                double sx,sy;
                if(source is SketchLine line) {sx=line.EndSketchPoint.Geometry.X-line.StartSketchPoint.Geometry.X;sy=line.EndSketchPoint.Geometry.Y-line.StartSketchPoint.Geometry.Y;double length=Math.Sqrt((fx*fx+fy*fy)*(sx*sx+sy*sy));return length>0 && Math.Abs(fx*sx+fy*sy)/length<=1e-8;}
                if(source is SketchArc arc) {sx=point.X-arc.CenterSketchPoint.Geometry.X;sy=point.Y-arc.CenterSketchPoint.Geometry.Y;double length=Math.Sqrt((fx*fx+fy*fy)*(sx*sx+sy*sy));return length>0 && Math.Abs(fx*sy-fy*sx)/length<=1e-8;}
                return false;
            }
            var start=fillet.StartSketchPoint.Geometry;var end=fillet.EndSketchPoint.Geometry;
            bool forward=At(start,one) && At(end,two),reverse=At(start,two) && At(end,one);
            result["start_one_end_two"]=forward;result["start_two_end_one"]=reverse;result["finite_source_tangency_observed"]=forward || reverse;
        }
        catch(Exception ex) {result["readback_error"]=ex.ToString();result["hresult"]="0x"+ex.HResult.ToString("X8");}
        return result;
    }

    private static JArray FilletResearchSourceChanges(JObject before,JObject after,Dictionary<string,string> aliases)
    {
        var rows=new JArray();foreach(string role in new[]{"source_one","source_two"})
        {
            var old=((JArray)before["entities"]!).OfType<JObject>().SingleOrDefault(e=>(string?)e["key"]==aliases[role]);
            var current=((JArray)after["entities"]!).OfType<JObject>().SingleOrDefault(e=>(string?)e["key"]==aliases[role]);
            rows.Add(new JObject {["role"]=role,["original_key"]=aliases[role],["same_key_survives"]=current!=null,["exact_same_entity_state"]=current!=null && JToken.DeepEquals(old,current),["before"]=old?.DeepClone(),["after_same_key"]=current?.DeepClone()});
        }
        return rows;
    }

    private static JObject FilletResearchConstraintChanges(JObject before,JObject after)
    {
        JArray Changes(string collection,string keyField)
        {
            var previous=((JArray)before[collection]!).OfType<JObject>().ToArray();var current=((JArray)after[collection]!).OfType<JObject>().ToArray();
            return new JArray(previous.Select(old=>new JObject {["original_key"]=old[keyField],["same_key_survives"]=current.Any(now=>JToken.DeepEquals(now[keyField],old[keyField])),["exact_same_state"]=current.Any(now=>JToken.DeepEquals(now,old))}));
        }
        return new JObject {["geometric_constraints"]=Changes("constraints","key"),["dimensions"]=Changes("dimension_bindings","key")};
    }
}
