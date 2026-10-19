using Inventor;
using Newtonsoft.Json.Linq;
using Bimwright.Ipt.Shared.Handlers;

internal static partial class Program
{
    // Research observations, never native Trim/Split equivalence or production support.
    private static void RunTopologyRelationsProbe(Inventor.Application app,PartDocument doc,JObject report)
    {
        if(doc.ComponentDefinition.Sketches.Count!=1) throw new InvalidOperationException("Centre-relations research requires exactly the initial owned sketch.");
        var sketch=doc.ComponentDefinition.Sketches[1];string originalName=sketch.Name;
        void Guard(bool empty)
        {
            if(app.Documents.Count!=1 || app.ActiveDocument is not PartDocument active || !TopologySame(active,doc)
                || doc.DisplayName!="S1_ArcInput_Oracle.ipt" || !string.IsNullOrEmpty(doc.FullFileName)
                || doc.DocumentType!=DocumentTypeEnum.kPartDocumentObject || doc.ComponentDefinition.Sketches.Count!=1
                || !TopologySame(doc.ComponentDefinition.Sketches[1],sketch) || sketch.Name!=originalName)
                throw new InvalidOperationException("Exact single owned unsaved arc oracle and original sketch required.");
            if(app.ActiveEditObject is object editing && !TopologySame(editing,sketch)) throw new InvalidOperationException("Research will not change another active editing context.");
            if(empty && (sketch.SketchEntities.Count!=0 || sketch.GeometricConstraints.Count!=0 || sketch.DimensionConstraints.Count!=0 || sketch.TextBoxes.Count!=0 || sketch.SketchImages.Count!=0))
                throw new InvalidOperationException("Centre-relations research requires the initial completely empty sketch.");
        }
        Guard(true);var aliases=new Dictionary<string,string>(StringComparer.Ordinal);
        JObject Snapshot()=>TopologyRelationsReadback(doc,sketch,aliases);
        var baseline=Snapshot();var rows=new JArray();report["centre_relation_cases"]=rows;
        report["evidence"]="RESEARCH Add/Delete centre associations and both SketchPoint.Merge orders, explicit native constraint graph and two MoveTo perturbations. NOT desktop Trim/Split equivalence, transferred feature dependencies, product capability or Quest PASS. Always Abort.";
        report["kind"]=doc.ComponentDefinition is SheetMetalComponentDefinition ? "sheetmetal" : "part";
        report["baseline"]=baseline.DeepClone();var g=app.TransientGeometry;
        foreach(double sweep in new[]{90d,-270d})
        foreach(string strategy in new[]{"observe automatic relations","source-centre receiver Merge","replacement-centre receiver Merge"})
        foreach(bool centreLine in new[]{false,true})
        {
            Guard(true);aliases.Clear();
            if(!JToken.DeepEquals(baseline,Snapshot())) throw new InvalidOperationException("Baseline changed before centre-relations research case.");
            var row=new JObject {["sweep_deg"]=sweep,["strategy"]=strategy,["line_attached_to_source_centre"]=centreLine,
                ["motion_anchor_role"]=centreLine ? "current start point of explicitly attached line" : "original explicitly supplied input SketchPoint"};rows.Add(row);
            row["before"]=baseline.DeepClone();
            var transaction=app.TransactionManager.StartTransaction((_Document)doc,"S1 centre-relations research, always aborted");
            try
            {
                if(transaction.HasParentTransaction) throw new InvalidOperationException("Research cannot run inside another transaction.");
                Guard(true);row["phase"]="create native source and explicit anchor";
                var input=sketch.SketchPoints.Add(g.CreatePoint2d(UnitConvert.MmToCm(13),UnitConvert.MmToCm(21)),false);
                var source=sketch.SketchArcs.AddByCenterStartSweepAngle(input,UnitConvert.MmToCm(20),UnitConvert.DegToRad(17),UnitConvert.DegToRad(sweep));
                var centre=source.CenterSketchPoint;
                var start=sweep>0 ? source.StartSketchPoint : source.EndSketchPoint;
                var end=sweep>0 ? source.EndSketchPoint : source.StartSketchPoint;
                SketchLine? anchorLine=centreLine ? sketch.SketchLines.AddByTwoPoints(centre,g.CreatePoint2d(UnitConvert.MmToCm(5),UnitConvert.MmToCm(5))) : null;
                aliases["input_anchor"]=TopologyKey(input);aliases["source_arc"]=TopologyKey(source);aliases["source_centre"]=TopologyKey(centre);
                aliases["requested_start"]=TopologyKey(start);aliases["requested_end"]=TopologyKey(end);
                if(anchorLine!=null) aliases["attached_anchor_line"]=TopologyKey(anchorLine);
                sketch.Solve();doc.Update2(true);row["source_state"]=Snapshot();row["source_lineage"]=TopologyRelationsLineage(doc,aliases);
                Point2d At(double t)=>g.CreatePoint2d(1.3+2*Math.Cos(UnitConvert.DegToRad(17+sweep*t)),2.1+2*Math.Sin(UnitConvert.DegToRad(17+sweep*t)));
                var middle=sketch.SketchPoints.Add(At(.5),false);
                var first=sketch.SketchArcs.AddByCenterStartEndPoint(centre,start,middle,sweep>0);
                var second=sketch.SketchArcs.AddByCenterStartEndPoint(centre,middle,end,sweep>0);
                aliases["split_point"]=TopologyKey(middle);aliases["first_arc"]=TopologyKey(first);aliases["second_arc"]=TopologyKey(second);
                aliases["first_centre_before_merge"]=TopologyKey(first.CenterSketchPoint);aliases["second_centre_before_merge"]=TopologyKey(second.CenterSketchPoint);
                sketch.Solve();doc.Update2(true);row["created_state"]=Snapshot();row["created_lineage"]=TopologyRelationsLineage(doc,aliases);
                var mergeSteps=new JArray();row["merge_steps"]=mergeSteps;
                if(strategy!="observe automatic relations")
                {
                    foreach(var replacement in new[]{first,second})
                    {
                        row["phase"]="Merge native centres, "+strategy;
                        // Read the current points from live owning arcs after each
                        // Merge: do not assume which operand keeps its reference key.
                        SketchPoint receiver=strategy=="source-centre receiver Merge" ? source.CenterSketchPoint : replacement.CenterSketchPoint;
                        SketchPoint argument=strategy=="source-centre receiver Merge" ? replacement.CenterSketchPoint : source.CenterSketchPoint;
                        var step=new JObject {["receiver_key_before"]=TopologyKey(receiver),["argument_key_before"]=TopologyKey(argument)};mergeSteps.Add(step);
                        receiver.Merge(argument);sketch.Solve();doc.Update2(true);
                        step["after"]=Snapshot();step["lineage_after"]=TopologyRelationsLineage(doc,aliases);
                    }
                }
                row["phase"]="delete source arc";row["before_delete"]=Snapshot();row["before_delete_lineage"]=TopologyRelationsLineage(doc,aliases);
                source.Delete();sketch.Solve();doc.Update2(true);row["after_delete"]=Snapshot();row["after_delete_lineage"]=TopologyRelationsLineage(doc,aliases);
                if(sketch.SketchArcs.Count!=2 || sketch.SketchLines.Count!=(centreLine ? 1 : 0) || sketch.SketchCircles.Count!=0)
                    throw new InvalidOperationException("Unexpected reconstructed topology after source deletion.");
                row["centre_identity_after_delete"]=new JObject {
                    ["first_and_second_same_native_point"]=TopologySame(first.CenterSketchPoint,second.CenterSketchPoint),
                    ["first_key"]=TopologyKey(first.CenterSketchPoint),["second_key"]=TopologyKey(second.CenterSketchPoint)};
                var moves=new JArray();row["anchor_perturbations"]=moves;bool allMovesFollow=true;
                foreach(var offset in new[]{(x:1d,y:2d),(x:-2d,y:-1d)})
                {
                    row["phase"]="MoveTo explicit current anchor and verify propagation";
                    var requested=g.CreatePoint2d(UnitConvert.MmToCm(13+offset.x),UnitConvert.MmToCm(21+offset.y));
                    var move=new JObject {["requested_mm"]=new JArray(13+offset.x,21+offset.y)};moves.Add(move);
                    try
                    {
                        Guard(false);SketchPoint anchor=anchorLine==null ? input : anchorLine.StartSketchPoint;
                        move["anchor_key_before"]=TopologyKey(anchor);anchor.MoveTo(requested);sketch.Solve();doc.Update2(true);
                        move["after"]=Snapshot();move["lineage_after"]=TopologyRelationsLineage(doc,aliases);
                        move["anchor_key_after"]=TopologyKey(anchor);
                        move["anchor_at_requested_position"]=UnitConvert.CmToMm(anchor.Geometry.DistanceTo(requested))<=1e-6;
                        move["first_centre_follows_anchor"]=UnitConvert.CmToMm(first.CenterSketchPoint.Geometry.DistanceTo(anchor.Geometry))<=1e-6;
                        move["second_centre_follows_anchor"]=UnitConvert.CmToMm(second.CenterSketchPoint.Geometry.DistanceTo(anchor.Geometry))<=1e-6;
                        if(!(bool)move["anchor_at_requested_position"]! || !(bool)move["first_centre_follows_anchor"]! || !(bool)move["second_centre_follows_anchor"]!)
                            throw new InvalidOperationException("Explicit native anchor MoveTo did not propagate to both reconstructed centres.");
                        if(sketch.ConstraintStatus==ConstraintStatusEnum.kOverConstrainedConstraintStatus) throw new InvalidOperationException("Centre perturbation produced solver conflict.");
                        move["observation"]="two reconstructed centres follow the explicitly selected native anchor; no inference from initial proximity";
                    }
                    catch(Exception moveError)
                    {allMovesFollow=false;move["observation"]="NATIVE EXCEPTION OR PROPAGATION DIVERGENCE";move["error"]=moveError.ToString();move["hresult"]="0x"+moveError.HResult.ToString("X8");}
                }
                if(!allMovesFollow) throw new InvalidOperationException("One or both explicit anchor perturbations failed; see both recorded observations.");
                Guard(false);row["phase"]="observations completed";
                row["observation"]="CENTRE GRAPH AND TWO ANCHOR UPDATES COMPLETED; research only, no desktop command equivalence";
            }
            catch(Exception ex)
            {
                row["observation"]="NATIVE EXCEPTION OR ASSOCIATION DIVERGENCE";row["hresult"]="0x"+ex.HResult.ToString("X8");row["error"]=ex.ToString();
                try { row["failure_state"]=Snapshot();row["failure_lineage"]=TopologyRelationsLineage(doc,aliases); }
                catch(Exception readError) {row["failure_read_error"]=readError.ToString();}
            }
            finally
            {
                if(!TopologySame(app.TransactionManager.CurrentTransaction,transaction)) throw new InvalidOperationException("Owned centre-relations transaction is no longer current; no unrelated transaction will be touched.");
                transaction.Abort();
            }
            Guard(true);aliases.Clear();var restored=Snapshot();row["rollback"]=restored;row["exact_rollback"]=JToken.DeepEquals(baseline,restored);
            if(!(bool)row["exact_rollback"]!) throw new InvalidOperationException("Centre-relations Abort did not restore the exact baseline.");
        }
        report["case_count"]=rows.Count;
        report["completed_graph_and_two_updates"]=rows.OfType<JObject>().Count(r=>(string?)r["observation"]=="CENTRE GRAPH AND TWO ANCHOR UPDATES COMPLETED; research only, no desktop command equivalence");
        report["native_exceptions_or_divergences"]=rows.Count-(int)report["completed_graph_and_two_updates"]!;
        report["research_outcome"]="OBSERVATIONS COLLECTED; twelve owned transactions aborted with exact baseline restoration. Exceptions/divergences remain explicit; no capability exposed.";
        report["final_state"]=Snapshot();Guard(true);
    }

    private static JObject TopologyRelationsReadback(PartDocument doc,PlanarSketch sketch,Dictionary<string,string> aliases)
    {
        var state=TopologyReadback(doc,sketch);var roles=new Dictionary<string,List<string>>(StringComparer.Ordinal);
        void Role(object native,string role)
        { string key=TopologyKey(native);if(!roles.TryGetValue(key,out var list)) roles[key]=list=new List<string>();if(!list.Contains(role)) list.Add(role); }
        foreach(SketchEntity entity in sketch.SketchEntities)
        {
            string key=TopologyKey(entity);Role(entity,"entity:"+key);
            switch(entity)
            {
                case SketchArc arc: Role(arc.CenterSketchPoint,"arc:"+key+":Centre");Role(arc.StartSketchPoint,"arc:"+key+":Start");Role(arc.EndSketchPoint,"arc:"+key+":End");break;
                case SketchLine line: Role(line.StartSketchPoint,"line:"+key+":Start");Role(line.EndSketchPoint,"line:"+key+":End");break;
                case SketchCircle circle: Role(circle.CenterSketchPoint,"circle:"+key+":Centre");break;
            }
        }
        JObject Member(SketchEntity entity)
        {
            string key=TopologyKey(entity);var labels=roles.TryGetValue(key,out var list) ? new List<string>(list) : new List<string>();
            foreach(var alias in aliases) if(alias.Value==key) labels.Add("original_alias:"+alias.Key);
            var member=new JObject {["key"]=key,["type"]=entity.Type.ToString(),["roles"]=new JArray(labels)};
            if(entity is SketchPoint point) member["point_mm"]=new JArray(UnitConvert.CmToMm(point.Geometry.X),UnitConvert.CmToMm(point.Geometry.Y));
            return member;
        }
        var graph=new JArray();state["relation_graph"]=graph;
        foreach(GeometricConstraint constraint in sketch.GeometricConstraints)
        {
            var relation=new JObject {["key"]=TopologyKey(constraint),["type"]=constraint.Type.ToString()};graph.Add(relation);
            SketchEntity[]? members=constraint switch {ConcentricConstraint c=>new[]{c.EntityOne,c.EntityTwo},CoincidentConstraint c=>new[]{c.EntityOne,c.EntityTwo},_=>null};
            if(members==null) {relation["binding_state"]="not read for this research type";continue;}
            relation["binding_state"]="explicit native EntityOne/EntityTwo";
            relation["entity_one"]=Member(members[0]);relation["entity_two"]=Member(members[1]);
        }
        return state;
    }
    private static JObject TopologyRelationsLineage(PartDocument doc,Dictionary<string,string> aliases)
    {
        var lineage=new JObject();
        foreach(var alias in aliases)
        {
            byte[] key=Convert.FromBase64String(alias.Value);
            var item=new JObject {["original_key"]=alias.Value};lineage[alias.Key]=item;
            try
            {
                bool resolved=doc.ReferenceKeyManager.CanBindKeyToObject(ref key,0,out object native,out object detail);
                item["resolved"]=resolved;item["ambiguous_collection"]=native is ObjectCollection;
                if(resolved && native is not ObjectCollection) {item["current_key"]=TopologyKey(native);item["current_type"]=((dynamic)native).Type.ToString();}
            }
            catch(Exception ex) {item["binding_error"]=ex.ToString();item["hresult"]="0x"+ex.HResult.ToString("X8");}
        }
        return lineage;
    }
}
