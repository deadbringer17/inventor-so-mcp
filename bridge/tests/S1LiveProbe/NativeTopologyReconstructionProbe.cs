using System.Runtime.InteropServices;
using Inventor;
using Newtonsoft.Json.Linq;
using Bimwright.Ipt.Shared.Handlers;

internal static partial class Program
{
    // Research only: Add/Delete reconstruction, not an implementation of native
    // Trim/Split or a claim that constraints/dependents have been transferred.
    private static void RunTopologyReconstructionProbe(Inventor.Application app,PartDocument doc,JObject report)
    {
        const string fixtureTitle="S1_ArcInput_Oracle.ipt";
        var component=doc.ComponentDefinition;
        if(component.Sketches.Count!=1) throw new InvalidOperationException("Topology research requires exactly the initial owned sketch.");
        var sketch=component.Sketches[1];string initialName=sketch.Name;
        void ValidateFixture(bool empty)
        {
            if(app.Documents.Count!=1 || app.ActiveDocument is not PartDocument active || !TopologySame(active,doc)
                || doc.DisplayName!=fixtureTitle || !string.IsNullOrEmpty(doc.FullFileName)
                || doc.DocumentType!=DocumentTypeEnum.kPartDocumentObject || component.Sketches.Count!=1
                || !TopologySame(component.Sketches[1],sketch) || sketch.Name!=initialName)
                throw new InvalidOperationException("Exact single unsaved owned arc oracle document and original sketch required; no user document may be changed.");
            if(app.ActiveEditObject is object editing && !TopologySame(editing,sketch))
                throw new InvalidOperationException("Another edit object is active; topology research will not change editing context.");
            if(empty && (sketch.SketchEntities.Count!=0 || sketch.GeometricConstraints.Count!=0 || sketch.DimensionConstraints.Count!=0
                || sketch.TextBoxes.Count!=0 || sketch.SketchImages.Count!=0))
                throw new InvalidOperationException("Topology research requires the initial sketch to be completely empty.");
        }
        ValidateFixture(true);
        var baseline=TopologyReadback(doc,sketch);var rows=new JArray();report["reconstruction_cases"]=rows;
        report["evidence"]="RESEARCH native Add/Delete reconstruction primitives only; NOT desktop Trim/Split equivalence, batch parity, production support, Quest or physical PASS. Every case Abort with exact readback.";
        report["kind"]=component is SheetMetalComponentDefinition ? "sheetmetal" : "part";
        report["baseline"]=baseline.DeepClone();
        void Observe(JObject row,Action<JObject> action)
        {
            ValidateFixture(true);
            if(!JToken.DeepEquals(baseline,TopologyReadback(doc,sketch))) throw new InvalidOperationException("Baseline differs before reconstruction case.");
            rows.Add(row);row["before"]=baseline.DeepClone();
            var transaction=app.TransactionManager.StartTransaction((_Document)doc,"S1 topology primitive research, always aborted");
            try
            {
                if(transaction.HasParentTransaction) throw new InvalidOperationException("Research may not run inside another transaction.");
                ValidateFixture(true);row["phase"]="create source";action(row);ValidateFixture(false);
                if(sketch.ConstraintStatus==ConstraintStatusEnum.kOverConstrainedConstraintStatus) throw new InvalidOperationException("Native solver reports over-constrained reconstructed geometry.");
                row["after"]=TopologyReadback(doc,sketch);
                row["phase"]="primitive checks completed";
                row["observation"]="PRIMITIVE CHECKS COMPLETED; no desktop or dependency-transfer equivalence claimed";
            }
            catch(Exception ex)
            {
                row["observation"]="NATIVE EXCEPTION OR PRIMITIVE DIVERGENCE";row["hresult"]="0x"+ex.HResult.ToString("X8");row["error"]=ex.ToString();
                try { row["failure_state"]=TopologyReadback(doc,sketch); } catch(Exception readError) { row["failure_read_error"]=readError.ToString(); }
            }
            finally
            {
                if(!TopologySame(app.TransactionManager.CurrentTransaction,transaction))
                    throw new InvalidOperationException("Owned topology research transaction is no longer current; do not touch another transaction.");
                transaction.Abort();
            }
            ValidateFixture(true);var restored=TopologyReadback(doc,sketch);row["rollback"]=restored;
            row["exact_rollback"]=JToken.DeepEquals(baseline,restored);
            if(!(bool)row["exact_rollback"]!) throw new InvalidOperationException("Topology primitive Abort did not restore the exact baseline.");
        }
        var g=app.TransientGeometry;
        foreach(string operation in new[]{"split","trim-middle"})
        foreach(bool shared in new[]{false,true})
        foreach(string relation in new[]{"none","horizontal","vertical"})
        {
            Observe(new JObject {["curve"]="line",["operation"]=operation,["shared_endpoints"]=shared,["source_constraint"]=relation},row=>
            {
                bool vertical=relation=="vertical";
                Point2d P(double mm)=>g.CreatePoint2d(vertical ? 0 : UnitConvert.MmToCm(mm),vertical ? UnitConvert.MmToCm(mm) : 0);
                var source=sketch.SketchLines.AddByTwoPoints(P(0),P(20));
                var start=source.StartSketchPoint;var end=source.EndSketchPoint;
                var neighbours=new List<SketchLine>();
                if(shared) { neighbours.Add(sketch.SketchLines.AddByTwoPoints(P(-10),start));neighbours.Add(sketch.SketchLines.AddByTwoPoints(end,P(30))); }
                if(relation=="horizontal") sketch.GeometricConstraints.AddHorizontal((SketchEntity)source,true);
                if(relation=="vertical") sketch.GeometricConstraints.AddVertical((SketchEntity)source,true);
                sketch.Solve();doc.Update2(true);
                row["source_state"]=TopologyReadback(doc,sketch);
                var sourceKey=TopologyKey(source);var startKey=TopologyKey(start);var endKey=TopologyKey(end);
                var neighbourKeys=neighbours.Select(n=>TopologyKey(n)).ToArray();
                row["phase"]="create replacement pieces";
                SketchLine first,second;
                if(operation=="split")
                {
                    var middle=sketch.SketchPoints.Add(P(10),false);
                    first=sketch.SketchLines.AddByTwoPoints(start,middle);second=sketch.SketchLines.AddByTwoPoints(middle,end);
                    row["shared_new_split_point"]=TopologySame(first.EndSketchPoint,second.StartSketchPoint);
                    if(!(bool)row["shared_new_split_point"]!) throw new InvalidOperationException("New split lines did not share the supplied native split point.");
                }
                else
                {
                    first=sketch.SketchLines.AddByTwoPoints(start,P(6));second=sketch.SketchLines.AddByTwoPoints(P(14),end);
                }
                row["before_delete"]=TopologyReadback(doc,sketch);row["phase"]="delete source and solve";source.Delete();sketch.Solve();doc.Update2(true);
                row["phase"]="check line topology and native identities";
                TopologyRequirePoint(first.StartSketchPoint,P(0));TopologyRequirePoint(second.EndSketchPoint,P(20));
                TopologyRequirePoint(first.EndSketchPoint,P(operation=="split" ? 10 : 6));TopologyRequirePoint(second.StartSketchPoint,P(operation=="split" ? 10 : 14));
                bool preserved=TopologySame(start,first.StartSketchPoint) && TopologySame(end,second.EndSketchPoint)
                    && TopologyKey(first.StartSketchPoint)==startKey && TopologyKey(second.EndSketchPoint)==endKey;
                row["outer_point_identity_preserved"]=preserved;
                if(!preserved) throw new InvalidOperationException("Deleting the source did not preserve reused outer-point identity/keys.");
                if(sketch.SketchLines.Count!=2+neighbours.Count || sketch.SketchArcs.Count!=0 || sketch.SketchCircles.Count!=0)
                    throw new InvalidOperationException("Unexpected reconstructed line topology/counts.");
                for(int i=0;i<neighbours.Count;i++) if(TopologyKey(neighbours[i])!=neighbourKeys[i]) throw new InvalidOperationException("Neighbour line reference key changed.");
                if(shared && (!TopologySame(neighbours[0].EndSketchPoint,first.StartSketchPoint) || !TopologySame(neighbours[1].StartSketchPoint,second.EndSketchPoint)))
                    throw new InvalidOperationException("Connected neighbours lost shared outer points.");
                row["old_curve_key_binding"]=TopologyBinding(doc,sourceKey);
                row["source_relation_count_after"]=sketch.GeometricConstraints.Cast<GeometricConstraint>().Count(c=>relation=="horizontal" ? c is HorizontalConstraint : relation=="vertical" && c is VerticalConstraint);
                row["relations_transferred"]=false;row["relation_note"]="No constraint replay attempted; any source H/V deletion or survival is an observation, not native command equivalence.";
            });
        }
        foreach(double sweep in new[]{90d,-90d,270d,-270d})
        foreach(string operation in new[]{"split","trim-middle"})
        foreach(bool shared in new[]{false,true})
        {
            Observe(new JObject {["curve"]="arc",["sweep_deg"]=sweep,["operation"]=operation,["shared_endpoints"]=shared},row=>
            {
                var centre=sketch.SketchPoints.Add(g.CreatePoint2d(1.3,2.1),false);
                var source=sketch.SketchArcs.AddByCenterStartSweepAngle(centre,2,17*Math.PI/180,sweep*Math.PI/180);
                // Prior native arc probes confirm that clockwise input is stored
                // as a CCW arc with swapped native endpoint roles and positive extent.
                var start=sweep>0 ? source.StartSketchPoint : source.EndSketchPoint;
                var end=sweep>0 ? source.EndSketchPoint : source.StartSketchPoint;
                var sourceCentre=source.CenterSketchPoint;
                SketchPoint RequestedStart(SketchArc arc)=>sweep>0 ? arc.StartSketchPoint : arc.EndSketchPoint;
                SketchPoint RequestedEnd(SketchArc arc)=>sweep>0 ? arc.EndSketchPoint : arc.StartSketchPoint;
                row["native_endpoint_convention"]=sweep>0 ? "requested start/end match native start/end" : "clockwise requested start/end map to native end/start";
                var sourceKey=TopologyKey(source);var startKey=TopologyKey(start);var endKey=TopologyKey(end);var centreKey=TopologyKey(sourceCentre);
                var neighbours=new List<SketchLine>();
                if(shared)
                {
                    neighbours.Add(sketch.SketchLines.AddByTwoPoints(start,g.CreatePoint2d(start.Geometry.X+.7,start.Geometry.Y+.4)));
                    neighbours.Add(sketch.SketchLines.AddByTwoPoints(end,g.CreatePoint2d(end.Geometry.X-.6,end.Geometry.Y+.5)));
                }
                sketch.Solve();doc.Update2(true);row["source_state"]=TopologyReadback(doc,sketch);
                row["phase"]="create replacement arcs with reused centre/endpoints";
                Point2d At(double t)=>g.CreatePoint2d(1.3+2*Math.Cos((17+sweep*t)*Math.PI/180),2.1+2*Math.Sin((17+sweep*t)*Math.PI/180));
                double left=operation=="split" ? .5 : .3,right=operation=="split" ? .5 : .7;
                object firstEnd=operation=="split" ? sketch.SketchPoints.Add(At(.5),false) : At(left);
                object secondStart=operation=="split" ? firstEnd : At(right);
                var first=sketch.SketchArcs.AddByCenterStartEndPoint(sourceCentre,start,firstEnd,sweep>0);
                var second=sketch.SketchArcs.AddByCenterStartEndPoint(sourceCentre,secondStart,end,sweep>0);
                row["before_delete"]=TopologyReadback(doc,sketch);row["phase"]="delete source and solve";source.Delete();sketch.Solve();doc.Update2(true);
                row["phase"]="check arc topology and native identities";
                TopologyRequirePoint(RequestedStart(first),At(0));TopologyRequirePoint(RequestedEnd(first),At(left));
                TopologyRequirePoint(RequestedStart(second),At(right));TopologyRequirePoint(RequestedEnd(second),At(1));
                // Midpoint geometry verifies which side of the circle is retained,
                // independently of the always-positive native SweepAngle property.
                TopologyRequireCoordinate(first.Geometry.MidPoint,At(left/2));
                TopologyRequireCoordinate(second.Geometry.MidPoint,At((right+1)/2));
                bool preserved=TopologySame(start,RequestedStart(first)) && TopologySame(end,RequestedEnd(second))
                    && TopologySame(sourceCentre,first.CenterSketchPoint) && TopologySame(sourceCentre,second.CenterSketchPoint)
                    && TopologyKey(RequestedStart(first))==startKey && TopologyKey(RequestedEnd(second))==endKey
                    && TopologyKey(first.CenterSketchPoint)==centreKey && TopologyKey(second.CenterSketchPoint)==centreKey;
                row["outer_point_and_centre_identity_preserved"]=preserved;
                if(!preserved) throw new InvalidOperationException("Arc reconstruction did not preserve reused endpoints/centre identity and keys.");
                if(operation=="split" && !TopologySame(RequestedEnd(first),RequestedStart(second))) throw new InvalidOperationException("Split arcs lost their shared split point.");
                if(Math.Abs(first.SweepAngle-Math.Abs(sweep*left*Math.PI/180))>1e-9 || Math.Abs(second.SweepAngle-Math.Abs(sweep*(1-right)*Math.PI/180))>1e-9)
                    throw new InvalidOperationException("Reconstructed arc absolute extent differs from the requested finite piece.");
                if(sketch.SketchArcs.Count!=2 || sketch.SketchLines.Count!=neighbours.Count || sketch.SketchCircles.Count!=0)
                    throw new InvalidOperationException("Unexpected reconstructed arc topology/counts.");
                if(shared && (!TopologySame(neighbours[0].StartSketchPoint,RequestedStart(first)) || !TopologySame(neighbours[1].StartSketchPoint,RequestedEnd(second))))
                    throw new InvalidOperationException("Arc neighbours lost their shared endpoints.");
                row["old_curve_key_binding"]=TopologyBinding(doc,sourceKey);row["relations_transferred"]=false;
            });
        }
        foreach(bool shared in new[]{false,true})
        {
            Observe(new JObject {["curve"]="circle",["operation"]="two-cut retained seam arc",["shared_centre"]=shared},row=>
            {
                var source=sketch.SketchCircles.AddByCenterRadius(g.CreatePoint2d(3,4),2);
                var centre=source.CenterSketchPoint;var key=TopologyKey(source);var centreKey=TopologyKey(centre);
                SketchLine? neighbour=shared ? sketch.SketchLines.AddByTwoPoints(centre,g.CreatePoint2d(3,1)) : null;
                Point2d At(double degrees)=>g.CreatePoint2d(3+2*Math.Cos(degrees*Math.PI/180),4+2*Math.Sin(degrees*Math.PI/180));
                sketch.Solve();doc.Update2(true);row["source_state"]=TopologyReadback(doc,sketch);
                row["phase"]="create retained seam arc with reused centre";
                var retained=sketch.SketchArcs.AddByCenterStartEndPoint(centre,At(300),At(60),true);
                row["before_delete"]=TopologyReadback(doc,sketch);row["phase"]="delete source and solve";source.Delete();sketch.Solve();doc.Update2(true);
                row["phase"]="check circle-to-arc topology and centre identity";
                TopologyRequirePoint(retained.StartSketchPoint,At(300));TopologyRequirePoint(retained.EndSketchPoint,At(60));
                bool preserved=TopologySame(centre,retained.CenterSketchPoint) && TopologyKey(retained.CenterSketchPoint)==centreKey;
                row["centre_identity_preserved"]=preserved;
                if(!preserved || shared && !TopologySame(neighbour!.StartSketchPoint,retained.CenterSketchPoint)) throw new InvalidOperationException("Circle-to-arc reconstruction lost reused centre identity.");
                if(sketch.SketchCircles.Count!=0 || sketch.SketchArcs.Count!=1 || sketch.SketchLines.Count!=(shared ? 1 : 0)
                    || Math.Abs(retained.SweepAngle-120*Math.PI/180)>1e-9 || Math.Abs(retained.Radius-2)>1e-9)
                    throw new InvalidOperationException("Circle-to-arc seam topology differs from the requested retained arc.");
                row["old_curve_key_binding"]=TopologyBinding(doc,key);row["relations_transferred"]=false;
            });
        }
        report["case_count"]=rows.Count;
        report["completed_primitive_checks"]=rows.OfType<JObject>().Count(r=>(string?)r["observation"]=="PRIMITIVE CHECKS COMPLETED; no desktop or dependency-transfer equivalence claimed");
        report["native_exceptions_or_divergences"]=rows.Count-(int)report["completed_primitive_checks"]!;
        report["research_outcome"]="OBSERVATIONS COLLECTED; all30 cases aborted with exact baseline restoration. Exceptions/divergences are data, not passing primitive checks.";
        report["final_state"]=TopologyReadback(doc,sketch);ValidateFixture(true);
    }

    private static bool TopologySame(object? first,object? second)
    {
        if(first==null || second==null) return first==second;
        IntPtr a=IntPtr.Zero,b=IntPtr.Zero;
        try { a=Marshal.GetIUnknownForObject(first);b=Marshal.GetIUnknownForObject(second);return a==b; }
        finally {if(b!=IntPtr.Zero) Marshal.Release(b);if(a!=IntPtr.Zero) Marshal.Release(a);}
    }
    private static string TopologyKey(object entity)
    { byte[] key=Array.Empty<byte>();((dynamic)entity).GetReferenceKey(ref key,0);return Convert.ToBase64String(key); }
    private static JObject TopologyBinding(PartDocument doc,string encoded)
    {
        byte[] key=Convert.FromBase64String(encoded);
        bool resolved=doc.ReferenceKeyManager.CanBindKeyToObject(ref key,0,out object native,out object detail);
        return new JObject {["resolved"]=resolved,["ambiguous_collection"]=native is ObjectCollection,["native_type"]=resolved ? ((dynamic)native).Type.ToString() : null};
    }
    private static void TopologyRequirePoint(SketchPoint point,Point2d expected)
        =>TopologyRequireCoordinate(point.Geometry,expected);
    private static void TopologyRequireCoordinate(Point2d actual,Point2d expected)
    { if(UnitConvert.CmToMm(actual.DistanceTo(expected))>1e-6) throw new InvalidOperationException("Reconstructed curve point differs by more than 1e-6 mm."); }
    private static JObject TopologyReadback(PartDocument doc,PlanarSketch sketch)
    {
        JArray P(SketchPoint point)=>new(UnitConvert.CmToMm(point.Geometry.X),UnitConvert.CmToMm(point.Geometry.Y));
        var entities=new JArray();
        foreach(SketchEntity entity in sketch.SketchEntities)
        {
            var item=new JObject {["key"]=TopologyKey(entity),["type"]=entity.Type.ToString(),["construction"]=entity.Construction};
            switch(entity)
            {
                case SketchPoint point: item["point_mm"]=P(point);item["hole_center"]=point.HoleCenter;break;
                case SketchLine line: item["start_mm"]=P(line.StartSketchPoint);item["end_mm"]=P(line.EndSketchPoint);item["start_key"]=TopologyKey(line.StartSketchPoint);item["end_key"]=TopologyKey(line.EndSketchPoint);item["centerline"]=line.Centerline;break;
                case SketchArc arc: item["start_mm"]=P(arc.StartSketchPoint);item["end_mm"]=P(arc.EndSketchPoint);item["centre_mm"]=P(arc.CenterSketchPoint);item["start_key"]=TopologyKey(arc.StartSketchPoint);item["end_key"]=TopologyKey(arc.EndSketchPoint);item["centre_key"]=TopologyKey(arc.CenterSketchPoint);item["radius_mm"]=UnitConvert.CmToMm(arc.Radius);item["start_deg"]=arc.StartAngle*180/Math.PI;item["sweep_deg"]=arc.SweepAngle*180/Math.PI;break;
                case SketchCircle circle: item["centre_mm"]=P(circle.CenterSketchPoint);item["centre_key"]=TopologyKey(circle.CenterSketchPoint);item["radius_mm"]=UnitConvert.CmToMm(circle.Radius);break;
                default: throw new InvalidOperationException("Unexpected entity in owned primitive-reconstruction fixture: "+entity.Type);
            }
            entities.Add(item);
        }
        var constraints=new JArray();foreach(GeometricConstraint constraint in sketch.GeometricConstraints)
            constraints.Add(new JObject {["key"]=TopologyKey(constraint),["type"]=constraint.Type.ToString(),["deletable"]=constraint.Deletable});
        return new JObject {["document_id"]=doc.InternalName,["display_name"]=doc.DisplayName,["full_filename"]=doc.FullFileName,
            ["sketch_name"]=sketch.Name,["sketch_key"]=TopologyKey(sketch),["sketch_count"]=doc.ComponentDefinition.Sketches.Count,
            ["entity_count"]=sketch.SketchEntities.Count,["line_count"]=sketch.SketchLines.Count,["arc_count"]=sketch.SketchArcs.Count,["circle_count"]=sketch.SketchCircles.Count,
            ["point_count"]=sketch.SketchPoints.Count,["dimension_count"]=sketch.DimensionConstraints.Count,["constraint_status"]=sketch.ConstraintStatus.ToString(),
            ["entities"]=entities,["constraints"]=constraints};
    }
}
