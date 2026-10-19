using Inventor;
using Newtonsoft.Json.Linq;
using Bimwright.Ipt.Shared.Handlers;
using System.Globalization;

internal static partial class Program
{
    // Research of explicitly chosen reconstruction policies. Neither policy is
    // asserted to reproduce Inventor's desktop Split command.
    private static void RunTopologyConstraintReplayProbe(Inventor.Application app,PartDocument doc,JObject report)
    {
        if(doc.ComponentDefinition.Sketches.Count!=1) throw new InvalidOperationException("Replay research requires exactly the original sketch.");
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
                throw new InvalidOperationException("Replay research requires a completely empty original sketch.");
        }
        Guard(true);var aliases=new Dictionary<string,string>(StringComparer.Ordinal);
        JObject Snapshot()=>TopologyReplayReadback(doc,sketch,aliases);
        var baseline=Snapshot();var cases=new JArray();report["constraint_replay_cases"]=cases;
        report["evidence"]="RESEARCH line Add/Delete with explicit H/V and linear dimension policies, native keys/roles and two source parameter updates; NOT a Trim/Split command, desktop parity, product capability or Quest PASS.";
        report["requires_desktop_oracle"]="Whether Split preserves the outer-endpoint dimension, replaces it by proportional piece dimensions, or uses another split-point policy remains unverified. The 40/60 ratio is a declared research input, not desktop semantics.";
        report["not_covered"]=new JArray("Other geometric/dimensional constraints", "Arcs, circles, splines and other curves", "Consumed profiles and feature dependencies", "External/projected references", "Trim and Extend", "One-cut/tangent/circle split semantics", "Reference IDs in product preview/commit");
        report["kind"]=doc.ComponentDefinition is SheetMetalComponentDefinition ? "sheetmetal" : "part";
        report["baseline"]=baseline.DeepClone();var g=app.TransientGeometry;
        report["parameter_readback_units"]="Geometry, driving length assertions and updates are mm. all_native_parameters.database_scalar is an explicitly labelled raw Inventor database scalar (cm for length, radians for angles), retained for exact rollback of arbitrary pre-existing parameter types; units labels are display units.";
        foreach(bool vertical in new[]{false,true})
        foreach(string policy in new[]{"observe without replay","outer endpoint total","proportional piece dimensions"})
        {
            Guard(true);aliases.Clear();if(!JToken.DeepEquals(baseline,Snapshot())) throw new InvalidOperationException("Replay baseline changed before case.");
            // Inventor can expose an idle "Unidentified transaction" here.
            // The owned transaction's HasParentTransaction is the actual guard.
            var row=new JObject {["axis"]=vertical ? "vertical" : "horizontal",["policy"]=policy,["requires_desktop_oracle"]=true,["before"]=baseline.DeepClone()};cases.Add(row);
            var transaction=app.TransactionManager.StartTransaction((_Document)doc,"S1 line constraint replay research, always aborted");
            try
            {
                if(transaction.HasParentTransaction) throw new InvalidOperationException("Research cannot run inside another transaction.");
                Guard(true);row["phase"]="create constrained parametric source and explicit shared neighbours";
                Point2d P(double along,double across=0)=>g.CreatePoint2d(UnitConvert.MmToCm(13+(vertical ? across : along)),UnitConvert.MmToCm(21+(vertical ? along : across)));
                var sourceParameter=doc.ComponentDefinition.Parameters.UserParameters.AddByExpression("S1_Replay_Length","20 mm","mm");
                var source=sketch.SketchLines.AddByTwoPoints(P(0),P(20));
                var start=source.StartSketchPoint;var end=source.EndSketchPoint;
                var left=sketch.SketchLines.AddByTwoPoints(P(-5,5),start);
                var right=sketch.SketchLines.AddByTwoPoints(end,P(25,5));
                var ground=sketch.GeometricConstraints.AddGround((SketchEntity)start);
                GeometricConstraint SourceAxis(SketchLine line)=>vertical ? (GeometricConstraint)sketch.GeometricConstraints.AddVertical((SketchEntity)line,true) : (GeometricConstraint)sketch.GeometricConstraints.AddHorizontal((SketchEntity)line,true);
                var axis=SourceAxis(source);
                TwoPointDistanceDimConstraint Dimension(SketchPoint one,SketchPoint two,string name,string expression,double textAlong)
                {
                    var dimension=sketch.DimensionConstraints.AddTwoPointDistance(one,two,vertical ? DimensionOrientationEnum.kVerticalDim : DimensionOrientationEnum.kHorizontalDim,P(textAlong,8),false);
                    dimension.Parameter.Name=name;dimension.Parameter.Expression=expression;return dimension;
                }
                var total=Dimension(start,end,"S1_Replay_Total",sourceParameter.Name,10);
                aliases["source_line"]=TopologyKey(source);aliases["source_start"]=TopologyKey(start);aliases["source_end"]=TopologyKey(end);
                aliases["source_axis_constraint"]=TopologyKey(axis);aliases["start_ground"]=TopologyKey(ground);
                aliases["left_neighbour"]=TopologyKey(left);aliases["right_neighbour"]=TopologyKey(right);
                aliases["source_total_dimension"]=TopologyKey(total);aliases["source_dimension_parameter"]=TopologyKey(total.Parameter);
                aliases["source_user_parameter"]=TopologyKey(sourceParameter);
                sketch.Solve();doc.Update2(false);row["source_state"]=Snapshot();row["source_lineage"]=TopologyRelationsLineage(doc,aliases);
                TopologyRequirePoint(start,P(0));TopologyRequirePoint(end,P(20));
                row["phase"]="reconstruct two pieces using supplied native endpoint identities";
                var middle=sketch.SketchPoints.Add(P(8),false);
                var first=sketch.SketchLines.AddByTwoPoints(start,middle);var second=sketch.SketchLines.AddByTwoPoints(middle,end);
                aliases["split_input"]=TopologyKey(middle);aliases["first_piece"]=TopologyKey(first);aliases["second_piece"]=TopologyKey(second);
                sketch.Solve();doc.Update2(false);row["before_delete"]=Snapshot();row["before_delete_lineage"]=TopologyRelationsLineage(doc,aliases);
                row["phase"]="delete original line and observe actual survivors";
                source.Delete();sketch.Solve();doc.Update2(false);
                row["after_delete"]=Snapshot();row["after_delete_lineage"]=TopologyRelationsLineage(doc,aliases);
                var replays=new JArray();row["explicit_replay_actions"]=replays;
                var survivors=sketch.DimensionConstraints.Cast<DimensionConstraint>().Where(d=>TopologyKey(d)==aliases["source_total_dimension"]).ToArray();
                row["source_total_dimension_survived"]=survivors.Length==1;
                if(policy!="observe without replay")
                {
                    row["phase"]="replay H/V by explicit line identity";
                    foreach(var piece in new[]{first,second})
                    {
                        bool exists=sketch.GeometricConstraints.Cast<GeometricConstraint>().Any(c=>vertical ? c is VerticalConstraint v && TopologySame(v.Entity,piece) : c is HorizontalConstraint h && TopologySame(h.Entity,piece));
                        var action=new JObject {["piece_key"]=TopologyKey(piece),["axis_constraint_already_present"]=exists};replays.Add(action);
                        if(!exists) action["created_constraint_key"]=TopologyKey(SourceAxis(piece));
                    }
                    row["phase"]="apply declared dimension policy";
                    if(policy=="outer endpoint total")
                    {
                        if(survivors.Length==0)
                        {
                            var replacement=Dimension(first.StartSketchPoint,second.EndSketchPoint,"S1_Replay_Outer",sourceParameter.Name,10);
                            aliases["replayed_outer_dimension"]=TopologyKey(replacement);
                            replays.Add(new JObject {["action"]="create outer-endpoint total dimension",["key"]=TopologyKey(replacement)});
                        }
                        else replays.Add(new JObject {["action"]="retain exact surviving outer-endpoint total dimension",["key"]=TopologyKey(survivors[0])});
                    }
                    else
                    {
                        // This explicit deletion is part of the research policy,
                        // not an assumed requirement of the desktop Split command.
                        foreach(var survivor in survivors) {replays.Add(new JObject {["action"]="delete surviving total dimension for declared piece policy",["key"]=TopologyKey(survivor)});survivor.Delete();}
                        var one=Dimension(first.StartSketchPoint,first.EndSketchPoint,"S1_Replay_First","0.4 * "+sourceParameter.Name,4);
                        var two=Dimension(second.StartSketchPoint,second.EndSketchPoint,"S1_Replay_Second","0.6 * "+sourceParameter.Name,14);
                        aliases["first_piece_dimension"]=TopologyKey(one);aliases["second_piece_dimension"]=TopologyKey(two);
                        replays.Add(new JObject {["action"]="create explicit 40/60 proportional dimensions",["first_key"]=TopologyKey(one),["second_key"]=TopologyKey(two)});
                    }
                }
                sketch.Solve();doc.Update2(false);row["after_replay"]=Snapshot();row["after_replay_lineage"]=TopologyRelationsLineage(doc,aliases);
                var updates=new JArray();row["source_parameter_updates"]=updates;bool all=true;
                foreach(double requested in new[]{24d,16d})
                {
                    row["phase"]="update source user parameter and verify explicit reconstruction";
                    var update=new JObject {["requested_mm"]=requested};updates.Add(update);
                    try
                    {
                        Guard(false);sourceParameter.Expression=requested.ToString(CultureInfo.InvariantCulture)+" mm";
                        doc.Update2(false);sketch.Solve();doc.Update2(false);
                        update["after"]=Snapshot();update["lineage"]=TopologyRelationsLineage(doc,aliases);
                        bool outer=TopologyKey(first.StartSketchPoint)==aliases["source_start"] && TopologyKey(second.EndSketchPoint)==aliases["source_end"];
                        bool neighbours=TopologySame(left.EndSketchPoint,first.StartSketchPoint) && TopologySame(right.StartSketchPoint,second.EndSketchPoint);
                        bool join=TopologySame(first.EndSketchPoint,second.StartSketchPoint);
                        bool keys=TopologyKey(left)==aliases["left_neighbour"] && TopologyKey(right)==aliases["right_neighbour"] && TopologyKey(first)==aliases["first_piece"] && TopologyKey(second)==aliases["second_piece"];
                        double totalMm=UnitConvert.CmToMm(first.StartSketchPoint.Geometry.DistanceTo(second.EndSketchPoint.Geometry));
                        bool totalFollows=Math.Abs(totalMm-requested)<=1e-6;
                        bool aligned=new[]{first,second}.All(line=>Math.Abs(UnitConvert.CmToMm(vertical ? line.EndSketchPoint.Geometry.X-line.StartSketchPoint.Geometry.X : line.EndSketchPoint.Geometry.Y-line.StartSketchPoint.Geometry.Y))<=1e-6);
                        bool explicitAxis=new[]{first,second}.All(piece=>sketch.GeometricConstraints.Cast<GeometricConstraint>().Any(c=>vertical ? c is VerticalConstraint v && TopologySame(v.Entity,piece) : c is HorizontalConstraint h && TopologySame(h.Entity,piece)));
                        bool ratio=Math.Abs(UnitConvert.CmToMm(first.Length)-requested*.4)<=1e-6 && Math.Abs(UnitConvert.CmToMm(second.Length)-requested*.6)<=1e-6;
                        bool parameterIdentity=TopologyKey(sourceParameter)==aliases["source_user_parameter"] && Math.Abs(UnitConvert.CmToMm(Convert.ToDouble(sourceParameter.Value,CultureInfo.InvariantCulture))-requested)<=1e-6;
                        bool groundBinding=sketch.GeometricConstraints.Cast<GeometricConstraint>().Any(c=>c is GroundConstraint groundConstraint && TopologyKey(c)==aliases["start_ground"] && TopologySame(groundConstraint.Entity,first.StartSketchPoint));
                        bool startAnchored=UnitConvert.CmToMm(first.StartSketchPoint.Geometry.DistanceTo(P(0)))<=1e-6;
                        bool DimensionBinding(SketchPoint one,SketchPoint two,double factor)=>sketch.DimensionConstraints.Cast<DimensionConstraint>().Any(d=>d is TwoPointDistanceDimConstraint distance
                            && TopologySame(distance.PointOne,one) && TopologySame(distance.PointTwo,two) && !d.Driven
                            && d.Parameter.Expression.Contains(sourceParameter.Name,StringComparison.Ordinal)
                            && Math.Abs(UnitConvert.CmToMm(Convert.ToDouble(d.Parameter.Value,CultureInfo.InvariantCulture))-requested*factor)<=1e-6);
                        bool dimensionBindings=policy=="proportional piece dimensions"
                            ? DimensionBinding(first.StartSketchPoint,first.EndSketchPoint,.4) && DimensionBinding(second.StartSketchPoint,second.EndSketchPoint,.6)
                            : DimensionBinding(first.StartSketchPoint,second.EndSketchPoint,1);
                        update["outer_endpoint_keys_preserved"]=outer;update["neighbours_share_actual_native_points"]=neighbours;update["pieces_share_actual_native_split_point"]=join;
                        update["line_keys_preserved"]=keys;update["outer_distance_mm"]=totalMm;update["total_follows_source"]=totalFollows;
                        update["pieces_geometrically_aligned"]=aligned;update["explicit_axis_bindings_on_both_pieces"]=explicitAxis;update["declared_40_60_ratio_observed"]=ratio;
                        update["source_parameter_identity_and_value"]=parameterIdentity;update["original_ground_binding_preserved"]=groundBinding;update["original_start_still_anchored"]=startAnchored;
                        update["explicit_driving_dimension_members_expression_and_value"]=dimensionBindings;
                        update["solver_status"]=sketch.ConstraintStatus.ToString();
                        bool common=outer && neighbours && join && keys && parameterIdentity && groundBinding && startAnchored && sketch.SketchLines.Count==4 && sketch.ConstraintStatus!=ConstraintStatusEnum.kOverConstrainedConstraintStatus;
                        bool expected=common && (policy=="observe without replay" || totalFollows && aligned && explicitAxis && dimensionBindings && (policy!="proportional piece dimensions" || ratio));
                        update["declared_policy_checks_completed"]=expected;
                        if(!expected) throw new InvalidOperationException("Native identities, solver or declared replay policy diverged; expectations have not been relaxed.");
                        update["observation"]=policy=="observe without replay" ? "CONTROL OBSERVATION; propagation and axis bindings reported without assuming preservation" : "DECLARED REPLAY POLICY OBSERVED; desktop semantics still require oracle";
                    }
                    catch(Exception ex)
                    {
                        all=false;update["observation"]="NATIVE EXCEPTION OR POLICY DIVERGENCE";update["error"]=ex.ToString();update["hresult"]="0x"+ex.HResult.ToString("X8");
                        try {update["failure_state"]=Snapshot();update["failure_lineage"]=TopologyRelationsLineage(doc,aliases);} catch(Exception readError) {update["failure_read_error"]=readError.ToString();}
                    }
                }
                if(!all) throw new InvalidOperationException("At least one source update diverged; both attempts retained.");
                Guard(false);row["observation"]="RESEARCH OBSERVATIONS COMPLETED; no desktop Split equivalence";
            }
            catch(Exception ex)
            {
                row["observation"]="NATIVE EXCEPTION OR REPLAY DIVERGENCE";row["error"]=ex.ToString();row["hresult"]="0x"+ex.HResult.ToString("X8");
                try {row["failure_state"]=Snapshot();row["failure_lineage"]=TopologyRelationsLineage(doc,aliases);} catch(Exception readError) {row["failure_read_error"]=readError.ToString();}
            }
            finally
            {
                if(!TopologySame(app.TransactionManager.CurrentTransaction,transaction)) throw new InvalidOperationException("Owned replay transaction is no longer current; no unrelated transaction will be aborted.");
                transaction.Abort();
            }
            Guard(true);aliases.Clear();var restored=Snapshot();row["rollback"]=restored;row["exact_rollback"]=JToken.DeepEquals(baseline,restored);
            if(!(bool)row["exact_rollback"]!) throw new InvalidOperationException("Replay Abort did not restore exact native geometry, constraints, dimensions and parameter baseline.");
        }
        report["case_count"]=cases.Count;report["completed_observations"]=cases.OfType<JObject>().Count(r=>(string?)r["observation"]=="RESEARCH OBSERVATIONS COMPLETED; no desktop Split equivalence");
        report["native_exceptions_or_divergences"]=cases.Count-(int)report["completed_observations"]!;
        report["final_state"]=Snapshot();Guard(true);
    }

    private static JObject TopologyReplayReadback(PartDocument doc,PlanarSketch sketch,Dictionary<string,string> aliases)
    {
        var state=TopologyRelationsReadback(doc,sketch,aliases);
        JObject Member(SketchEntity entity)
        {
            string key=TopologyKey(entity);var member=new JObject {["key"]=key,["type"]=entity.Type.ToString(),["original_aliases"]=new JArray(aliases.Where(a=>a.Value==key).Select(a=>a.Key))};
            if(entity is SketchPoint point) member["point_mm"]=new JArray(UnitConvert.CmToMm(point.Geometry.X),UnitConvert.CmToMm(point.Geometry.Y));
            return member;
        }
        var axes=new JArray();state["axis_and_ground_bindings"]=axes;
        foreach(GeometricConstraint constraint in sketch.GeometricConstraints)
        {
            SketchEntity? entity=constraint switch {HorizontalConstraint h=>h.Entity,VerticalConstraint v=>v.Entity,GroundConstraint ground=>ground.Entity,_=>null};
            if(entity!=null) axes.Add(new JObject {["constraint_key"]=TopologyKey(constraint),["type"]=constraint.Type.ToString(),["native_entity"]=Member(entity)});
        }
        JObject ParameterState(Parameter parameter)=>new JObject {["key"]=TopologyKey(parameter),["name"]=parameter.Name,["expression"]=parameter.Expression,["units"]=parameter.get_Units(),["database_scalar"]=Convert.ToDouble(parameter.Value,CultureInfo.InvariantCulture),["type"]=parameter.ParameterType.ToString(),["health"]=parameter.HealthStatus.ToString()};
        var dimensions=new JArray();state["dimension_bindings"]=dimensions;
        foreach(DimensionConstraint dimension in sketch.DimensionConstraints)
        {
            var item=new JObject {["key"]=TopologyKey(dimension),["type"]=dimension.Type.ToString(),["driven"]=dimension.Driven,["parameter"]=ParameterState(dimension.Parameter),["text_mm"]=new JArray(UnitConvert.CmToMm(dimension.TextPoint.X),UnitConvert.CmToMm(dimension.TextPoint.Y))};dimensions.Add(item);
            if(dimension is TwoPointDistanceDimConstraint distance)
            {
                item["native_point_one"]=Member((SketchEntity)distance.PointOne);item["native_point_two"]=Member((SketchEntity)distance.PointTwo);
                item["value_mm"]=UnitConvert.CmToMm(Convert.ToDouble(dimension.Parameter.Value,CultureInfo.InvariantCulture));
            }
            else item["binding_state"]="not read for this research dimension type";
        }
        var parameters=new JArray();state["all_native_parameters"]=parameters;
        foreach(Parameter parameter in doc.ComponentDefinition.Parameters) parameters.Add(ParameterState(parameter));
        return state;
    }
}
