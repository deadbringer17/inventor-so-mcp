using Inventor;
using Bimwright.Ipt.Shared.Contracts;
using Newtonsoft.Json.Linq;

internal static partial class Program
{
    private static readonly Dictionary<bool, Dictionary<string,string>> CreatorKeys94=new();
    private static readonly Dictionary<string,JObject> SourceData94=new(StringComparer.Ordinal);
    private static JObject Initial94=null!;
    private static void Require89(bool value,string assertion) { if(!value) throw new HostFailure73(assertion); }
    private static void CreateCurves94(PartDocument doc,PlanarSketch sketch,SketchLine line,TextBox literal,TextBox parameter)
    {
        GuardDocuments73();var g=Application!.TransientGeometry;
        var keys=new Dictionary<string,string>(StringComparer.Ordinal) { ["line"]=Key73(line),["text"]=Key73(literal),["parameter_text"]=Key73(parameter) };
        keys.Add("circle",Key73(sketch.SketchCircles.AddByCenterRadius(g.CreatePoint2d(1.7,-1.1),0.9)));
        var profiles=new[]{("arc_quarter",70d,0d,90d),("arc_wrap",123d,170d,90d),("arc_major",176d,30d,270d),("arc_clockwise",229d,30d,-90d)};
        foreach(var (label,cx,start,sweep) in profiles)
        {
            Point2d At(double degrees){double angle=degrees*Math.PI/180;return g.CreatePoint2d(cx/10+0.9*Math.Cos(angle),-1.1+0.9*Math.Sin(angle));}
            var arc=sketch.SketchArcs.AddByCenterStartEndPoint(g.CreatePoint2d(cx/10,-1.1),At(start),At(start+sweep),sweep>0);
            keys.Add(label,Key73(arc));
        }
        Require89(keys.Count==8&&keys.Values.Distinct(StringComparer.Ordinal).Count()==8,"Creator94 source identities differ.");
        CreatorKeys94.Add(doc.ComponentDefinition is SheetMetalComponentDefinition,keys);
        Require89(doc.Update2(false)&&doc.Rebuild2(false),"Creator94 completion failed.");
    }
    private static PlanarSketch CopySketch94(OwnedDocument73 owned)=>owned.Document.ComponentDefinition.Sketches.Cast<PlanarSketch>().Single(s=>s.Name=="S1_Copy88");
    private static string Encode94(string doc,string kind,string key)=>new PersistentEntityReference { DocumentId=doc,EntityType=kind,Key=Convert.FromBase64String(key) }.Encode();
    private static void WriteCatalog94()
    {
        var rows=new JArray();
        foreach(var owned in Documents)
        {
            var sources=new JObject();
            foreach(var pair in CreatorKeys94[owned.Sheet]) sources[pair.Key]=Encode94(owned.DocumentId,pair.Key is "text" or "parameter_text"?"sketch_text":"sketch_entity",pair.Value);
            rows.Add(new JObject { ["context"]=owned.Sheet?"Sheet":"Part",["document_id"]=owned.DocumentId,
                ["sketch_id"]=Encode94(owned.DocumentId,"sketch",Key73(CopySketch94(owned))),["sources"]=sources });
        }
        WritePublic73("curve-copy-sources94.json",new JObject { ["schema"]=1,["run_id"]=Settings.RunId,["target_id"]=TargetId,
            ["owned_pid"]=OwnedPid,["owned_start_utc_ticks"]=OwnedStart,["documents"]=rows });
    }
    private static void RememberSources94()
    {
        Initial94=FullState92();
        foreach(var owned in Documents)
        {
            var sketch=CopySketch94(owned);
            foreach(var pair in CreatorKeys94[owned.Sheet])
            {
                JObject state=pair.Key is "text" or "parameter_text"
                    ?Text73(sketch.TextBoxes.Cast<TextBox>().Single(t=>Key73(t)==pair.Value),sketch)
                    :EntityState92(sketch.SketchEntities.Cast<SketchEntity>().Single(e=>CurveKey92(e)==pair.Value));
                SourceData94.Add(owned.DocumentId+":"+pair.Value,(JObject)state.DeepClone());
            }
        }
    }
}
