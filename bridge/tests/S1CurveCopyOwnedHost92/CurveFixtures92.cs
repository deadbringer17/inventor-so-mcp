using Inventor;
using Newtonsoft.Json.Linq;

internal static partial class Program
{
    private sealed record CurveFixture92(bool Sheet,string Label,string SketchKey,string PrimaryKey,string CompanionKey,string LineKey,JObject Request);
    private static readonly List<CurveFixture92> CurveFixtures92=new();
    private static void CreateCurveFixtures92(PartDocument doc)
    {
        GuardDocuments73();var g=Application!.TransientGeometry;bool sheet=doc.ComponentDefinition is SheetMetalComponentDefinition;
        var profiles=new[]{("circle",0d,360d),("arc_quarter",0d,90d),("arc_wrap",170d,90d),("arc_major",30d,270d),("arc_negative_ctor",30d,-90d)};
        int ordinal=0;
        foreach(var (label,start,sweep) in profiles)
        {
            var sketch=doc.ComponentDefinition.Sketches.Add(doc.ComponentDefinition.WorkPlanes[3]);sketch.Name="S1_CurveCopy92_"+(ordinal++);
            SketchEntity primary;
            if(label=="circle") primary=(SketchEntity)sketch.SketchCircles.AddByCenterRadius(g.CreatePoint2d(1.7,-1.1),0.9);
            else
            {
                Point2d At(double degrees){double angle=degrees*Math.PI/180;return g.CreatePoint2d(1.7+0.9*Math.Cos(angle),-1.1+0.9*Math.Sin(angle));}
                primary=(SketchEntity)sketch.SketchArcs.AddByCenterStartEndPoint(g.CreatePoint2d(1.7,-1.1),At(start),At(start+sweep),sweep>0);
            }
            SketchEntity companion=label=="circle"
                ?(SketchEntity)sketch.SketchArcs.AddByCenterStartSweepAngle(g.CreatePoint2d(8,2.5),0.7,10*Math.PI/180,120*Math.PI/180)
                :(SketchEntity)sketch.SketchCircles.AddByCenterRadius(g.CreatePoint2d(8,2.5),0.6);
            var line=sketch.SketchLines.AddByTwoPoints(g.CreatePoint2d(-3,4),g.CreatePoint2d(-1.2,4.7));
            CurveFixtures92.Add(new CurveFixture92(sheet,label,Key73(sketch),CurveKey92(primary),CurveKey92(companion),CurveKey92((SketchEntity)line),
                new JObject { ["center_mm"]=new JArray(17,-11),["radius_mm"]=9,["requested_start_deg"]=start,["requested_sweep_deg"]=sweep,
                    ["constructor"]=label=="circle"?"AddByCenterRadius":"AddByCenterStartEndPoint",["counterclockwise"]=sweep>0,
                    ["native_direction_policy"]="Record actual native start/end/startAngle/sweep; negative constructor does not imply a signed SweepAngle getter" }));
        }
        GuardDocuments73();Require89(doc.Update2(false),"Curve92 fixture update failed.");
    }
}
