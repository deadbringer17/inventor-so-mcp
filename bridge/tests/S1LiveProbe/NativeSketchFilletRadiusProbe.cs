using Inventor;
using Newtonsoft.Json.Linq;
using Bimwright.Ipt.Shared.Handlers;
using System.Globalization;

internal static partial class Program
{
    // A declared AddRadius policy, not a default attributed to desktop Fillet.
    private static void RunSketchFilletRadiusProbe(Inventor.Application app,PartDocument doc,JObject report)
    {
        if(doc.ComponentDefinition.Sketches.Count!=1) throw new InvalidOperationException("Fillet radius research requires the original sketch only.");
        var sketch=doc.ComponentDefinition.Sketches[1];string initialName=sketch.Name;
        void Guard(bool empty)
        {
            if(app.Documents.Count!=1 || app.ActiveDocument is not PartDocument active || !TopologySame(active,doc)
                || doc.DisplayName!="S1_ArcInput_Oracle.ipt" || !string.IsNullOrEmpty(doc.FullFileName)
                || doc.DocumentType!=DocumentTypeEnum.kPartDocumentObject || doc.ComponentDefinition.Sketches.Count!=1
                || !TopologySame(doc.ComponentDefinition.Sketches[1],sketch) || sketch.Name!=initialName)
                throw new InvalidOperationException("Exact single owned unsaved arc oracle and original sketch required.");
            if(app.ActiveEditObject is object editing && !TopologySame(editing,sketch)) throw new InvalidOperationException("Another editing context is active.");
            if(empty && (sketch.SketchEntities.Count!=0 || sketch.GeometricConstraints.Count!=0 || sketch.DimensionConstraints.Count!=0 || sketch.TextBoxes.Count!=0 || sketch.SketchImages.Count!=0))
                throw new InvalidOperationException("Fillet radius research requires a completely empty sketch.");
        }
        Guard(true);var aliases=new Dictionary<string,string>(StringComparer.Ordinal);
        JObject Snapshot()=>FilletResearchReadback(doc,sketch,aliases);
        var baseline=Snapshot();var rows=new JArray();report["fillet_radius_cases"]=rows;
        report["evidence"]="API RESEARCH: expression-only AddByFillet control versus explicitly added driving RadiusDim whose Parameter.Expression refers to the user radius. NOT desktop Fillet equivalence, Trim/Preserve semantics, product capability or Quest PASS. Always Abort with exact geometry/constraints/dimensions/all-parameter readback.";
        report["matrix"]="12 cases/context: line-line, line-arc, arc-arc x expression-only control/explicit RadiusDim x free/parametric sources. Fixed declared ++ pick solution, not all sides. Two radius updates 4/2 mm in every case; two source updates 22/18 mm only for sources with explicit driving dimensions.";
        report["requires_desktop_oracle"]="Whether and how desktop Fillet creates a radius dimension, manages source constraints, trims curves and chooses a solution remains unverified. This is an explicit research policy, not an inferred default.";
        report["not_covered"]=new JArray("Other solution sides", "Chamfer", "Trim and preserve/remove options", "Invalid/degenerate/tangent/coincident input pairs", "Projected references and consumed profiles", "Downstream feature dependencies", "Product reference remapping and preview/commit", "Quest and physical interaction");
        report["parameter_readback_units"]="Geometry and asserted lengths are mm; reused database_scalar fields explicitly retain native database units solely for exact arbitrary-parameter rollback readback.";
        report["kind"]=doc.ComponentDefinition is SheetMetalComponentDefinition ? "sheetmetal" : "part";report["baseline"]=baseline.DeepClone();
        var g=app.TransientGeometry;
        foreach(string pair in new[]{"line-line","line-arc","arc-arc"})
        foreach(bool explicitDimension in new[]{false,true})
        foreach(bool controlledSources in new[]{false,true})
        {
            Guard(true);aliases.Clear();if(!JToken.DeepEquals(baseline,Snapshot())) throw new InvalidOperationException("Fillet radius baseline changed before case.");
            var row=new JObject {["pair"]=pair,["explicit_radius_dimension_policy"]=explicitDimension,["controlled_sources"]=controlledSources,["requires_desktop_oracle"]=true,["before"]=baseline.DeepClone()};rows.Add(row);
            var transaction=app.TransactionManager.StartTransaction((_Document)doc,"S1 fillet radius dependency research, always aborted");
            try
            {
                if(transaction.HasParentTransaction) throw new InvalidOperationException("Research cannot run inside another transaction.");
                Guard(true);row["phase"]="create finite source pair and input parameters";
                Point2d P(double x,double y)=>g.CreatePoint2d(UnitConvert.MmToCm(x),UnitConvert.MmToCm(y));
                Point2d Polar(double cx,double cy,double degrees)=>P(cx+20*Math.Cos(UnitConvert.DegToRad(degrees)),cy+20*Math.Sin(UnitConvert.DegToRad(degrees)));
                var userRadius=doc.ComponentDefinition.Parameters.UserParameters.AddByExpression("S1_RadiusPolicy_Input","3 mm","mm");
                var userSize=doc.ComponentDefinition.Parameters.UserParameters.AddByExpression("S1_RadiusPolicy_Source","20 mm","mm");
                aliases["radius_input_parameter"]=TopologyKey(userRadius);aliases["source_input_parameter"]=TopologyKey(userSize);
                SketchEntity one,two;Point2d pickOne,pickTwo;
                if(pair=="line-line")
                {
                    one=(SketchEntity)sketch.SketchLines.AddByTwoPoints(P(-20,0),P(20,0));two=(SketchEntity)sketch.SketchLines.AddByTwoPoints(P(0,-20),P(0,20));
                    pickOne=P(10,0);pickTwo=P(0,10);
                }
                else if(pair=="line-arc")
                {
                    one=(SketchEntity)sketch.SketchLines.AddByTwoPoints(P(-10,0),P(35,0));
                    two=(SketchEntity)sketch.SketchArcs.AddByCenterStartSweepAngle(P(0,0),UnitConvert.MmToCm(20),UnitConvert.DegToRad(-90),Math.PI);
                    pickOne=P(28,0);pickTwo=Polar(0,0,25);
                }
                else
                {
                    one=(SketchEntity)sketch.SketchArcs.AddByCenterStartSweepAngle(P(0,0),UnitConvert.MmToCm(20),0,Math.PI);
                    two=(SketchEntity)sketch.SketchArcs.AddByCenterStartSweepAngle(P(20,0),UnitConvert.MmToCm(20),0,Math.PI);
                    pickOne=Polar(0,0,75);pickTwo=Polar(20,0,135);
                }
                aliases["source_one"]=TopologyKey(one);aliases["source_two"]=TopologyKey(two);
                var sourceDimensions=new List<(DimensionConstraint dimension,SketchEntity entity,double factor,string dimensionKey,string parameterKey)>();
                if(controlledSources)
                foreach(var entry in new[]{(entity:one,role:"source_one"),(entity:two,role:"source_two")})
                {
                    DimensionConstraint dimension;double factor;
                    if(entry.entity is SketchLine line)
                    {
                        bool vertical=pair=="line-line" && entry.role=="source_two";
                        GeometricConstraint axis=vertical ? (GeometricConstraint)sketch.GeometricConstraints.AddVertical(entry.entity,true) : (GeometricConstraint)sketch.GeometricConstraints.AddHorizontal(entry.entity,true);
                        aliases[entry.role+"_axis"]=TopologyKey(axis);
                        dimension=(DimensionConstraint)sketch.DimensionConstraints.AddTwoPointDistance(line.StartSketchPoint,line.EndSketchPoint,DimensionOrientationEnum.kAlignedDim,P(-25,-25),false);
                        factor=pair=="line-arc" ? 2.25 : 2;
                    }
                    else {dimension=(DimensionConstraint)sketch.DimensionConstraints.AddRadius(entry.entity,P(-25,25),false);factor=1;}
                    dimension.Parameter.Name="S1_RadiusPolicy_"+entry.role;dimension.Parameter.Expression=factor.ToString(CultureInfo.InvariantCulture)+" * "+userSize.Name;
                    aliases[entry.role+"_dimension"]=TopologyKey(dimension);aliases[entry.role+"_dimension_parameter"]=TopologyKey(dimension.Parameter);
                    sourceDimensions.Add((dimension,entry.entity,factor,TopologyKey(dimension),TopologyKey(dimension.Parameter)));
                }
                sketch.Solve();doc.Update2(false);row["source_state"]=Snapshot();row["source_lineage"]=TopologyRelationsLineage(doc,aliases);
                if(!FilletResearchOnCurve(one,pickOne,1e-6) || !FilletResearchOnCurve(two,pickTwo,1e-6)) throw new InvalidOperationException("Declared picks do not belong to the finite native sources after solving.");
                row["pick_one_mm"]=new JArray(UnitConvert.CmToMm(pickOne.X),UnitConvert.CmToMm(pickOne.Y));row["pick_two_mm"]=new JArray(UnitConvert.CmToMm(pickTwo.X),UnitConvert.CmToMm(pickTwo.Y));
                row["phase"]="AddByFillet with same named expression in both arms";
                var fillet=sketch.SketchArcs.AddByFillet(one,two,userRadius.Name,pickOne,pickTwo);aliases["returned_fillet"]=TopologyKey(fillet);
                sketch.Solve();doc.Update2(false);row["before_explicit_radius_dimension"]=Snapshot();row["before_explicit_radius_lineage"]=TopologyRelationsLineage(doc,aliases);
                RadiusDimConstraint? radiusDimension=null;
                if(explicitDimension)
                {
                    row["phase"]="add driving native RadiusDim and bind its model parameter expression";
                    radiusDimension=sketch.DimensionConstraints.AddRadius((SketchEntity)fillet,P(35,25),false);
                    radiusDimension.Parameter.Name="S1_RadiusPolicy_Fillet";radiusDimension.Parameter.Expression=userRadius.Name;
                    aliases["explicit_radius_dimension"]=TopologyKey(radiusDimension);aliases["explicit_radius_model_parameter"]=TopologyKey(radiusDimension.Parameter);
                }
                sketch.Solve();doc.Update2(false);row["after_radius_policy"]=Snapshot();row["after_radius_policy_lineage"]=TopologyRelationsLineage(doc,aliases);
                row["initial_radius_mm"]=UnitConvert.CmToMm(fillet.Radius);row["initial_tangency"]=FilletResearchTangency(fillet,one,two);
                if(Math.Abs(UnitConvert.CmToMm(fillet.Radius)-3)>1e-6 || (bool?)row["initial_tangency"]?["finite_source_tangency_observed"]!=true || sketch.ConstraintStatus==ConstraintStatusEnum.kOverConstrainedConstraintStatus)
                    throw new InvalidOperationException("Initial radius policy did not preserve requested radius, finite tangency or non-conflicting solver.");
                var updates=new JArray();row["updates"]=updates;bool all=true;
                foreach(var updateInput in new[]{(role:"radius",value:4d),(role:"radius",value:2d),(role:"source",value:22d),(role:"source",value:18d)})
                {
                    if(updateInput.role=="source" && !controlledSources) continue;
                    row["phase"]="update "+updateInput.role+" parameter and verify declared native dependency policy";
                    var update=new JObject {["input_role"]=updateInput.role,["requested_mm"]=updateInput.value};updates.Add(update);
                    try
                    {
                        Guard(false);if(updateInput.role=="radius") userRadius.Expression=updateInput.value.ToString(CultureInfo.InvariantCulture)+" mm";else userSize.Expression=updateInput.value.ToString(CultureInfo.InvariantCulture)+" mm";
                        doc.Update2(false);sketch.Solve();doc.Update2(false);
                        update["after"]=Snapshot();update["lineage"]=TopologyRelationsLineage(doc,aliases);update["tangency"]=FilletResearchTangency(fillet,one,two);
                        double requestedRadius=UnitConvert.CmToMm(Convert.ToDouble(userRadius.Value,CultureInfo.InvariantCulture));double actualRadius=UnitConvert.CmToMm(fillet.Radius);
                        bool tracks=Math.Abs(actualRadius-requestedRadius)<=1e-6;
                        bool identities=TopologyKey(fillet)==aliases["returned_fillet"] && TopologyKey(one)==aliases["source_one"] && TopologyKey(two)==aliases["source_two"]
                            && TopologyKey(userRadius)==aliases["radius_input_parameter"] && TopologyKey(userSize)==aliases["source_input_parameter"];
                        update["radius_input_mm"]=requestedRadius;update["actual_radius_mm"]=actualRadius;update["radius_tracks_input"]=tracks;update["native_entity_and_input_parameter_keys_preserved"]=identities;
                        update["native_radius_dimensions_on_fillet"]=sketch.DimensionConstraints.Cast<DimensionConstraint>().Count(d=>d is RadiusDimConstraint r && TopologySame(r.Entity,fillet));
                        bool radiusBinding=true;
                        if(radiusDimension!=null)
                        {
                            var dependencies=FilletRadiusDrivenBy(radiusDimension.Parameter);update["radius_model_parameter_driven_by"]=dependencies;
                            bool directDependency=dependencies.OfType<JObject>().Any(p=>(string?)p["key"]==aliases["radius_input_parameter"]);
                            radiusBinding=TopologyKey(radiusDimension)==aliases["explicit_radius_dimension"] && TopologyKey(radiusDimension.Parameter)==aliases["explicit_radius_model_parameter"]
                                && TopologySame(radiusDimension.Entity,fillet) && !radiusDimension.Driven && radiusDimension.Parameter.Expression.Contains(userRadius.Name,StringComparison.Ordinal)
                                && Math.Abs(UnitConvert.CmToMm(Convert.ToDouble(radiusDimension.Parameter.Value,CultureInfo.InvariantCulture))-requestedRadius)<=1e-6 && directDependency
                                && sketch.DimensionConstraints.Cast<DimensionConstraint>().Any(d=>TopologySame(d,radiusDimension));
                            update["explicit_radius_dimension_identity_entity_expression_value_and_dependency"]=radiusBinding;
                        }
                        else update["radius_dependency_expectation"]="Expression-only control: actual tracking is observed, never asserted parametric success.";
                        var sourceChecks=new JArray();update["controlled_source_dimensions"]=sourceChecks;bool sources=true;
                        foreach(var source in sourceDimensions)
                        {
                            double size=UnitConvert.CmToMm(Convert.ToDouble(userSize.Value,CultureInfo.InvariantCulture)),expected=size*source.factor;
                            var dependencies=FilletRadiusDrivenBy(source.dimension.Parameter);
                            double geometry=source.entity is SketchLine line ? UnitConvert.CmToMm(line.Length) : UnitConvert.CmToMm(((SketchArc)source.entity).Radius);
                            bool sourceMembers=source.dimension is RadiusDimConstraint sourceRadius ? TopologySame(sourceRadius.Entity,source.entity)
                                : source.dimension is TwoPointDistanceDimConstraint sourceLength && source.entity is SketchLine sourceLine
                                    && TopologySame(sourceLength.PointOne,sourceLine.StartSketchPoint) && TopologySame(sourceLength.PointTwo,sourceLine.EndSketchPoint);
                            bool preserved=TopologyKey(source.dimension)==source.dimensionKey && TopologyKey(source.dimension.Parameter)==source.parameterKey && !source.dimension.Driven
                                && Math.Abs(UnitConvert.CmToMm(Convert.ToDouble(source.dimension.Parameter.Value,CultureInfo.InvariantCulture))-expected)<=1e-6 && Math.Abs(geometry-expected)<=1e-6
                                && sourceMembers && source.dimension.Parameter.Expression.Contains(userSize.Name,StringComparison.Ordinal) && dependencies.OfType<JObject>().Any(p=>(string?)p["key"]==aliases["source_input_parameter"])
                                && sketch.DimensionConstraints.Cast<DimensionConstraint>().Any(d=>TopologySame(d,source.dimension));
                            sources &= preserved;sourceChecks.Add(new JObject {["dimension_key"]=source.dimensionKey,["expected_geometry_mm"]=expected,["actual_geometry_mm"]=geometry,["native_dimension_members_preserved"]=sourceMembers,["driven_by"]=dependencies,["native_source_dependency_preserved"]=preserved});
                        }
                        update["controlled_source_dependencies_preserved"]=sources;update["solver_status"]=sketch.ConstraintStatus.ToString();
                        double actualInput=updateInput.role=="radius" ? requestedRadius : UnitConvert.CmToMm(Convert.ToDouble(userSize.Value,CultureInfo.InvariantCulture));
                        bool expectedChecks=identities && sources && radiusBinding && (!explicitDimension || tracks) && Math.Abs(actualInput-updateInput.value)<=1e-6
                            && double.IsFinite(actualRadius) && actualRadius>0 && (bool?)update["tangency"]?["finite_source_tangency_observed"]==true && sketch.ConstraintStatus!=ConstraintStatusEnum.kOverConstrainedConstraintStatus;
                        update["declared_policy_checks_completed"]=expectedChecks;
                        if(!expectedChecks) throw new InvalidOperationException("Declared radius/source dependency policy, finite tangency, identity or solver diverged; expectations have not been relaxed.");
                        update["observation"]=explicitDimension ? "EXPLICIT RADIUSDIM POLICY VERIFIED FOR THIS UPDATE; not desktop equivalence" : "EXPRESSION-ONLY CONTROL OBSERVED; no parametric claim";
                    }
                    catch(Exception ex)
                    {
                        all=false;update["observation"]="NATIVE EXCEPTION OR DEPENDENCY DIVERGENCE";update["error"]=ex.ToString();update["hresult"]="0x"+ex.HResult.ToString("X8");
                        try {update["failure_state"]=Snapshot();update["failure_lineage"]=TopologyRelationsLineage(doc,aliases);} catch(Exception readError) {update["failure_read_error"]=readError.ToString();}
                    }
                }
                if(!all) throw new InvalidOperationException("One or more declared radius/source update checks diverged; subsequent attempts are retained.");
                row["observation"]="API RADIUS POLICY OBSERVATIONS COMPLETED; no desktop Fillet equivalence";Guard(false);
            }
            catch(Exception ex)
            {
                row["observation"]="NATIVE EXCEPTION OR DEPENDENCY DIVERGENCE";row["error"]=ex.ToString();row["hresult"]="0x"+ex.HResult.ToString("X8");
                try {row["failure_state"]=Snapshot();row["failure_lineage"]=TopologyRelationsLineage(doc,aliases);} catch(Exception readError) {row["failure_read_error"]=readError.ToString();}
            }
            finally
            {
                if(!TopologySame(app.TransactionManager.CurrentTransaction,transaction)) throw new InvalidOperationException("Owned fillet radius transaction is no longer current; no unrelated transaction will be touched.");
                transaction.Abort();
            }
            Guard(true);aliases.Clear();var restored=Snapshot();row["rollback"]=restored;row["exact_rollback"]=JToken.DeepEquals(baseline,restored);
            if(!(bool)row["exact_rollback"]!) throw new InvalidOperationException("Fillet radius Abort did not restore exact native geometry, constraints, dimensions and parameter baseline.");
        }
        report["case_count"]=rows.Count;report["completed_observations"]=rows.OfType<JObject>().Count(r=>(string?)r["observation"]=="API RADIUS POLICY OBSERVATIONS COMPLETED; no desktop Fillet equivalence");
        report["completed_explicit_radius_policy_cases"]=rows.OfType<JObject>().Count(r=>(bool?)r["explicit_radius_dimension_policy"]==true && (string?)r["observation"]=="API RADIUS POLICY OBSERVATIONS COMPLETED; no desktop Fillet equivalence");
        report["completed_expression_only_controls"]=rows.OfType<JObject>().Count(r=>(bool?)r["explicit_radius_dimension_policy"]==false && (string?)r["observation"]=="API RADIUS POLICY OBSERVATIONS COMPLETED; no desktop Fillet equivalence");
        report["native_exceptions_or_divergences"]=rows.Count-(int)report["completed_observations"]!;report["final_state"]=Snapshot();Guard(true);
    }

    private static JArray FilletRadiusDrivenBy(Parameter parameter)
    {
        var result=new JArray();foreach(object dependency in parameter.DrivenBy)
        {
            // Read actual native dependencies. Never substitute the expression
            // string for an inaccessible or missing dependency association.
            var item=new JObject {["key"]=TopologyKey(dependency)};result.Add(item);
            if(dependency is Parameter native) {item["name"]=native.Name;item["parameter_type"]=native.ParameterType.ToString();item["health"]=native.HealthStatus.ToString();}
            else item["parameter_state"]="native dependency is not exposed as Parameter; key retained without an inferred name or type";
        }
        return result;
    }
}
