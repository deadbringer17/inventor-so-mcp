using Inventor;
using Newtonsoft.Json.Linq;
using Bimwright.Ipt.Shared.Handlers;
using System.Globalization;

internal static partial class Program
{
    // Research gap after wave 37: a Coincident constraint whose EntityTwo is
    // the entire deleted source line, rather than a shared endpoint identity.
    private static void RunLineSplitDependencyProbe(Inventor.Application app,PartDocument doc,JObject report)
    {
        if(doc.ComponentDefinition.Sketches.Count!=1) throw new InvalidOperationException("Line dependency research requires exactly the original sketch.");
        var sketch=doc.ComponentDefinition.Sketches[1];string name=sketch.Name;
        void Guard(bool empty)
        {
            if(app.Documents.Count!=1 || app.ActiveDocument is not PartDocument active || !TopologySame(active,doc)
                || doc.DisplayName!="S1_ArcInput_Oracle.ipt" || !string.IsNullOrEmpty(doc.FullFileName)
                || doc.DocumentType!=DocumentTypeEnum.kPartDocumentObject || doc.ComponentDefinition.Sketches.Count!=1
                || !TopologySame(doc.ComponentDefinition.Sketches[1],sketch) || sketch.Name!=name)
                throw new InvalidOperationException("Exact single owned unsaved arc oracle and original sketch required.");
            if(app.ActiveEditObject is object editing && !TopologySame(editing,sketch)) throw new InvalidOperationException("Another editing context is active.");
            if(empty && (sketch.SketchEntities.Count!=0 || sketch.GeometricConstraints.Count!=0 || sketch.DimensionConstraints.Count!=0 || sketch.TextBoxes.Count!=0 || sketch.SketchImages.Count!=0))
                throw new InvalidOperationException("Line dependency research requires the original empty sketch.");
        }
        Guard(true);var aliases=new Dictionary<string,string>(StringComparer.Ordinal);
        JObject Snapshot()=>TopologyReplayReadback(doc,sketch,aliases);
        var baseline=Snapshot();var rows=new JArray();report["line_dependency_cases"]=rows;
        report["evidence"]="RESEARCH whole-line Coincident dependency after Add/Delete reconstruction; source H/V is explicitly replayed in both arms to isolate Coincident replay. NOT Split/Trim/Extend, desktop equivalence, a product capability or Quest PASS.";
        report["contract"]="Four observations: H/V source x retain automatic Coincident observations / explicit replay to the first piece. Each reports native source/replacement/follower/constraint/parameter keys, native EntityOne/Two and point roles, before/after Delete, replay actions, two transverse parameter updates (8 and 3 mm), solver and exact Abort including all parameter state. Failure or lack of propagation remains data; rollback mismatch is fatal.";
        report["role_selection"]="Fixture-owned follower point is explicitly created at along=6 mm on source 0..20 mm; split input is at along=10 mm. Replay target is the declared first piece, not a coordinate-nearness search. Finite membership is independently checked after each update; no reassignment to another piece.";
        report["requires_desktop_oracle"]="Desktop Split handling of a point-on-whole-line Coincident constraint is unknown. Replay to the declared first piece is a research policy, including whether support-line coincidence should remain finite. No proportional split-point law is imposed.";
        report["not_covered"]=new JArray("Other geometric or dimensional constraints", "Parallel/tangent/symmetry/pattern relations", "Arcs/circles/splines", "Consumed profiles and feature dependencies", "Projected/external geometry", "Opaque product ID remapping and preview/commit", "Desktop Split semantics");
        report["parameter_readback_units"]="Geometry, dimension assertions and requested updates are mm; database_scalar in the reused parameter readback is a labelled raw Inventor database scalar, for exact rollback of arbitrary parameter types.";
        report["kind"]=doc.ComponentDefinition is SheetMetalComponentDefinition ? "sheetmetal" : "part";report["baseline"]=baseline.DeepClone();
        var g=app.TransientGeometry;
        foreach(bool vertical in new[]{false,true})
        foreach(bool replay in new[]{false,true})
        {
            Guard(true);aliases.Clear();if(!JToken.DeepEquals(baseline,Snapshot())) throw new InvalidOperationException("Line dependency baseline changed before case.");
            var row=new JObject {["axis"]=vertical ? "vertical" : "horizontal",["explicit_coincident_replay"]=replay,["requires_desktop_oracle"]=true,["before"]=baseline.DeepClone()};rows.Add(row);
            var transaction=app.TransactionManager.StartTransaction((_Document)doc,"S1 whole-line dependency research, always aborted");
            try
            {
                // CurrentTransaction can describe an idle Unidentified transaction.
                if(transaction.HasParentTransaction) throw new InvalidOperationException("Research cannot run inside another transaction.");
                Guard(true);row["phase"]="create transverse parametric source and whole-line Coincident follower";
                Point2d P(double along,double across)=>g.CreatePoint2d(UnitConvert.MmToCm(13+(vertical ? across : along)),UnitConvert.MmToCm(21+(vertical ? along : across)));
                var anchor=sketch.SketchPoints.Add(P(0,0),false);var ground=sketch.GeometricConstraints.AddGround((SketchEntity)anchor);
                var sourceParameter=doc.ComponentDefinition.Parameters.UserParameters.AddByExpression("S1_Dependency_Offset","5 mm","mm");
                var source=sketch.SketchLines.AddByTwoPoints(P(0,5),P(20,5));var start=source.StartSketchPoint;var end=source.EndSketchPoint;
                if(vertical) sketch.GeometricConstraints.AddHorizontalAlign(anchor,start);else sketch.GeometricConstraints.AddVerticalAlign(anchor,start);
                GeometricConstraint Axis(SketchLine line)=>vertical ? (GeometricConstraint)sketch.GeometricConstraints.AddVertical((SketchEntity)line,true) : (GeometricConstraint)sketch.GeometricConstraints.AddHorizontal((SketchEntity)line,true);
                var sourceAxis=Axis(source);
                TwoPointDistanceDimConstraint Dim(SketchPoint one,SketchPoint two,bool transverse,string parameterName,string expression,double textAlong,double textAcross)
                {
                    bool horizontal=vertical==transverse;
                    var dimension=sketch.DimensionConstraints.AddTwoPointDistance(one,two,horizontal ? DimensionOrientationEnum.kHorizontalDim : DimensionOrientationEnum.kVerticalDim,P(textAlong,textAcross),false);
                    dimension.Parameter.Name=parameterName;dimension.Parameter.Expression=expression;return dimension;
                }
                var offset=Dim(anchor,start,true,"S1_Dependency_Offset_Dim",sourceParameter.Name,-5,2);
                var total=Dim(start,end,false,"S1_Dependency_Total","20 mm",10,12);
                var follower=sketch.SketchPoints.Add(P(6,5),false);
                var followerLine=sketch.SketchLines.AddByTwoPoints(follower,P(6,12));
                var along=Dim(anchor,follower,false,"S1_Dependency_Along","6 mm",3,-5);
                var coincidence=sketch.GeometricConstraints.AddCoincident((SketchEntity)follower,(SketchEntity)source);
                var downstream=doc.ComponentDefinition.Parameters.UserParameters.AddByExpression("S1_Dependency_Downstream",offset.Parameter.Name,"mm");
                aliases["ground_anchor"]=TopologyKey(anchor);aliases["ground_constraint"]=TopologyKey(ground);
                aliases["source_line"]=TopologyKey(source);aliases["source_start"]=TopologyKey(start);aliases["source_end"]=TopologyKey(end);aliases["source_axis"]=TopologyKey(sourceAxis);
                aliases["follower_point"]=TopologyKey(follower);aliases["follower_line"]=TopologyKey(followerLine);aliases["source_coincident"]=TopologyKey(coincidence);
                aliases["offset_dimension"]=TopologyKey(offset);aliases["offset_model_parameter"]=TopologyKey(offset.Parameter);aliases["total_dimension"]=TopologyKey(total);aliases["follower_along_dimension"]=TopologyKey(along);
                aliases["source_user_parameter"]=TopologyKey(sourceParameter);aliases["downstream_user_parameter"]=TopologyKey(downstream);
                sketch.Solve();doc.Update2(false);row["source_state"]=Snapshot();row["source_lineage"]=TopologyRelationsLineage(doc,aliases);
                TopologyRequirePoint(start,P(0,5));TopologyRequirePoint(end,P(20,5));TopologyRequirePoint(follower,P(6,5));
                if(!TopologySame(coincidence.EntityOne,follower) || !TopologySame(coincidence.EntityTwo,source)) throw new InvalidOperationException("Coincident native EntityOne/Two do not match the declared follower/source roles.");
                row["phase"]="create replacement pieces with explicit shared point inputs";
                var split=sketch.SketchPoints.Add(P(10,5),false);var first=sketch.SketchLines.AddByTwoPoints(start,split);var second=sketch.SketchLines.AddByTwoPoints(split,end);
                aliases["split_input"]=TopologyKey(split);aliases["first_piece"]=TopologyKey(first);aliases["second_piece"]=TopologyKey(second);
                row["explicit_reconstruction_lineage"]=new JObject {
                    ["deleted_source_key"]=aliases["source_line"],["replacement_line_keys"]=new JArray(aliases["first_piece"],aliases["second_piece"]),
                    ["retained_start_key"]=aliases["source_start"],["retained_end_key"]=aliases["source_end"],
                    ["declared_replay_target_key"]=aliases["first_piece"],
                    ["mapping_scope"]="Research native reference keys only; one-to-many whole-line correspondence is not interchangeable identity or an opaque product reference remap"};
                sketch.Solve();doc.Update2(false);row["before_delete"]=Snapshot();row["before_delete_lineage"]=TopologyRelationsLineage(doc,aliases);
                row["phase"]="delete source line and observe whole-line relation loss or survival";
                source.Delete();sketch.Solve();doc.Update2(false);row["after_delete"]=Snapshot();row["after_delete_lineage"]=TopologyRelationsLineage(doc,aliases);
                row["original_coincident_key_survives_delete"]=sketch.GeometricConstraints.Cast<GeometricConstraint>().Any(c=>TopologyKey(c)==aliases["source_coincident"]);
                var actions=new JArray();row["replay_actions"]=actions;
                foreach(var piece in new[]{first,second})
                {
                    bool exists=sketch.GeometricConstraints.Cast<GeometricConstraint>().Any(c=>vertical ? c is VerticalConstraint v && TopologySame(v.Entity,piece) : c is HorizontalConstraint h && TopologySame(h.Entity,piece));
                    var action=new JObject {["kind"]="axis on explicitly selected piece, both research arms",["piece_key"]=TopologyKey(piece),["already_present"]=exists};actions.Add(action);
                    if(!exists) action["created_key"]=TopologyKey(Axis(piece));
                }
                bool HasFollowerBinding()=>sketch.GeometricConstraints.Cast<GeometricConstraint>().Any(c=>c is CoincidentConstraint coincident &&
                    (TopologySame(coincident.EntityOne,follower) && TopologySame(coincident.EntityTwo,first) || TopologySame(coincident.EntityTwo,follower) && TopologySame(coincident.EntityOne,first)));
                row["follower_to_declared_first_piece_before_replay"]=HasFollowerBinding();
                if(replay && !HasFollowerBinding())
                {
                    row["phase"]="explicit Coincident replay to declared first piece";
                    var replacement=sketch.GeometricConstraints.AddCoincident((SketchEntity)follower,(SketchEntity)first);
                    aliases["replayed_coincident"]=TopologyKey(replacement);
                    actions.Add(new JObject {["kind"]="Coincident follower/first piece",["created_key"]=TopologyKey(replacement),["entity_one_key"]=TopologyKey(replacement.EntityOne),["entity_two_key"]=TopologyKey(replacement.EntityTwo)});
                }
                sketch.Solve();doc.Update2(false);row["after_replay"]=Snapshot();row["after_replay_lineage"]=TopologyRelationsLineage(doc,aliases);
                var updates=new JArray();row["transverse_updates"]=updates;bool all=true;
                foreach(double requested in new[]{8d,3d})
                {
                    row["phase"]="transverse source parameter update and dependency observation";
                    var update=new JObject {["requested_offset_mm"]=requested};updates.Add(update);
                    try
                    {
                        Guard(false);sourceParameter.Expression=requested.ToString(CultureInfo.InvariantCulture)+" mm";doc.Update2(false);sketch.Solve();doc.Update2(false);
                        update["after"]=Snapshot();update["lineage"]=TopologyRelationsLineage(doc,aliases);
                        bool identities=TopologyKey(first.StartSketchPoint)==aliases["source_start"] && TopologyKey(second.EndSketchPoint)==aliases["source_end"]
                            && TopologyKey(first)==aliases["first_piece"] && TopologyKey(second)==aliases["second_piece"] && TopologyKey(follower)==aliases["follower_point"]
                            && TopologyKey(followerLine)==aliases["follower_line"] && TopologySame(followerLine.StartSketchPoint,follower) && TopologySame(first.EndSketchPoint,second.StartSketchPoint);
                        bool sourceMoves=UnitConvert.CmToMm(first.StartSketchPoint.Geometry.DistanceTo(P(0,requested)))<=1e-6 && UnitConvert.CmToMm(second.EndSketchPoint.Geometry.DistanceTo(P(20,requested)))<=1e-6;
                        bool follows=UnitConvert.CmToMm(follower.Geometry.DistanceTo(P(6,requested)))<=1e-6;
                        var a=first.StartSketchPoint.Geometry;var b=first.EndSketchPoint.Geometry;var point=follower.Geometry;double dx=b.X-a.X,dy=b.Y-a.Y,len2=dx*dx+dy*dy;
                        double parameter=len2>0 ? ((point.X-a.X)*dx+(point.Y-a.Y)*dy)/len2 : double.NaN;
                        double distanceMm=len2>0 ? UnitConvert.CmToMm(Math.Abs((point.X-a.X)*dy-(point.Y-a.Y)*dx)/Math.Sqrt(len2)) : double.PositiveInfinity;
                        bool finiteMember=len2>0 && parameter>=0 && parameter<=1 && distanceMm<=1e-6;
                        bool downstreamFollows=TopologyKey(downstream)==aliases["downstream_user_parameter"] && Math.Abs(UnitConvert.CmToMm(Convert.ToDouble(downstream.Value,CultureInfo.InvariantCulture))-requested)<=1e-6;
                        bool originalDimension=sketch.DimensionConstraints.Cast<DimensionConstraint>().Any(d=>TopologyKey(d)==aliases["offset_dimension"] && TopologyKey(d.Parameter)==aliases["offset_model_parameter"]);
                        update["native_identities_preserved"]=identities;update["source_pieces_follow_transverse_update"]=sourceMoves;update["follower_follows_transverse_update"]=follows;
                        update["explicit_follower_first_piece_binding"]=HasFollowerBinding();update["follower_on_finite_first_piece"]=finiteMember;
                        // JSON cannot carry NaN/Infinity as ordinary numeric observations.
                        update["first_piece_parameter"]=double.IsFinite(parameter) ? new JValue(parameter) : JValue.CreateNull();
                        update["distance_to_first_support_mm"]=double.IsFinite(distanceMm) ? new JValue(distanceMm) : JValue.CreateNull();
                        update["original_offset_dimension_and_model_parameter_preserved"]=originalDimension;update["downstream_expression_follows_original_model_parameter"]=downstreamFollows;
                        update["solver_status"]=sketch.ConstraintStatus.ToString();
                        bool expected=identities && sourceMoves && downstreamFollows && originalDimension && sketch.SketchLines.Count==3 && sketch.ConstraintStatus!=ConstraintStatusEnum.kOverConstrainedConstraintStatus
                            && (!replay || follows && finiteMember && HasFollowerBinding());
                        update["declared_checks_completed"]=expected;
                        if(!expected) throw new InvalidOperationException("Native dependency or declared replay policy diverged; no changed expectations or fallback piece.");
                        update["observation"]=replay ? "DECLARED COINCIDENT REPLAY OBSERVED; requires desktop oracle" : "CONTROL OBSERVATION; actual propagation and relation survival recorded";
                    }
                    catch(Exception ex)
                    {
                        all=false;update["observation"]="NATIVE EXCEPTION OR DEPENDENCY DIVERGENCE";update["error"]=ex.ToString();update["hresult"]="0x"+ex.HResult.ToString("X8");
                        try {update["failure_state"]=Snapshot();update["failure_lineage"]=TopologyRelationsLineage(doc,aliases);} catch(Exception readError) {update["failure_read_error"]=readError.ToString();}
                    }
                }
                if(!all) throw new InvalidOperationException("One or both transverse dependency updates diverged; both attempts are retained.");
                row["observation"]="RESEARCH DEPENDENCY OBSERVATIONS COMPLETED; no desktop Split equivalence";Guard(false);
            }
            catch(Exception ex)
            {
                row["observation"]="NATIVE EXCEPTION OR DEPENDENCY DIVERGENCE";row["error"]=ex.ToString();row["hresult"]="0x"+ex.HResult.ToString("X8");
                try {row["failure_state"]=Snapshot();row["failure_lineage"]=TopologyRelationsLineage(doc,aliases);} catch(Exception readError) {row["failure_read_error"]=readError.ToString();}
            }
            finally
            {
                if(!TopologySame(app.TransactionManager.CurrentTransaction,transaction)) throw new InvalidOperationException("Owned line dependency transaction is no longer current; no unrelated transaction will be touched.");
                transaction.Abort();
            }
            Guard(true);aliases.Clear();var restored=Snapshot();row["rollback"]=restored;row["exact_rollback"]=JToken.DeepEquals(baseline,restored);
            if(!(bool)row["exact_rollback"]!) throw new InvalidOperationException("Line dependency Abort did not restore exact geometry, relations, dimensions and parameter baseline.");
        }
        report["case_count"]=rows.Count;report["completed_observations"]=rows.OfType<JObject>().Count(r=>(string?)r["observation"]=="RESEARCH DEPENDENCY OBSERVATIONS COMPLETED; no desktop Split equivalence");
        report["native_exceptions_or_divergences"]=rows.Count-(int)report["completed_observations"]!;report["final_state"]=Snapshot();Guard(true);
    }
}
