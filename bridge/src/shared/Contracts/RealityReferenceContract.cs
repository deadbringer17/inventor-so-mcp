using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Contracts;

/// <summary>Bounded, API-free RC4 native reference input. Points remain scan LH/mm; the explicit matrix maps to Inventor RH/mm.</summary>
public sealed class RealityReferenceContract
{
    public JObject Payload {get;}
    public string Name {get;}
    public double[] Matrix {get;}
    public JArray Points {get;}
    public JArray Primitives {get;}
    public JArray Constraints {get;}
    public RealityReferenceContract(JObject payload)
    {
        if(payload==null||Encoding.UTF8.GetByteCount(payload.ToString(Formatting.None))>900000||payload.Value<string>("unit")!="mm"||payload.Value<string>("geometry")!="estimated") throw new ArgumentException("Invalid reality reference payload.");
        Payload=(JObject)payload.DeepClone();Name=Payload.Value<string>("reference_id")??"";
        if(!Regex.IsMatch(Name,@"\ARC_[a-f0-9]{12}\z")) throw new ArgumentException("Invalid reference name.");
        var source=Payload["source"] as JObject??throw new ArgumentException("Reference source missing.");
        if(string.IsNullOrWhiteSpace(source.Value<string>("document_id"))||string.IsNullOrWhiteSpace(source.Value<string>("cad_revision"))||source.Value<int?>("solve_revision").GetValueOrDefault(0)<1||source.Value<int?>("review_version").GetValueOrDefault(-1)<0||
            !Guid.TryParseExact(source.Value<string>("scan_id"),"N",out _)||!Regex.IsMatch(source.Value<string>("points_sha256")??"",@"\A[a-f0-9]{64}\z")) throw new ArgumentException("Invalid reference provenance.");
        Matrix=source["scan_metric_to_inventor_mm"]?.ToObject<double[]>()??throw new ArgumentException("Registration missing.");
        if(Matrix.Length!=16||Matrix.Any(x=>!Finite(x)||Math.Abs(x)>1e7)||Math.Abs(Matrix[12])+Math.Abs(Matrix[13])+Math.Abs(Matrix[14])+Math.Abs(Matrix[15]-1)>1e-8) throw new ArgumentException("Invalid registration matrix.");
        for(int i=0;i<3;i++) for(int j=0;j<3;j++) {double dot=0;for(int k=0;k<3;k++) dot+=Matrix[4*k+i]*Matrix[4*k+j];if(Math.Abs(dot-(i==j?1:0))>1e-6) throw new ArgumentException("Non-rigid reference.");}
        double det=Matrix[0]*(Matrix[5]*Matrix[10]-Matrix[6]*Matrix[9])-Matrix[1]*(Matrix[4]*Matrix[10]-Matrix[6]*Matrix[8])+Matrix[2]*(Matrix[4]*Matrix[9]-Matrix[5]*Matrix[8]);
        if(Math.Abs(det+1)>1e-6) throw new ArgumentException("Expected scan LH to CAD RH basis.");
        Points=Payload["points"] as JArray??throw new ArgumentException("Point cloud missing.");Primitives=Payload["primitives"] as JArray??new JArray();Constraints=Payload["known_constraints"] as JArray??new JArray();
        if(Points.Count<1||Points.Count>2000||Primitives.Count>32||Constraints.Count>256||Points.Select(p=>p.Value<string>("id")).Distinct(StringComparer.Ordinal).Count()!=Points.Count) throw new ArgumentException("Reference budget exceeded.");
        foreach(var point in Points) {Vec(point["position_mm"]);if(string.IsNullOrEmpty(point.Value<string>("id"))) throw new ArgumentException("Missing source point ID.");}
        foreach(var primitive in Primitives)
        {
            if(!new[]{"point","axis","plane","circle","cylinder"}.Contains(primitive.Value<string>("kind"))) throw new ArgumentException("Unsupported primitive.");
            Vec(primitive["origin_mm"]);var d=Vec(primitive["direction"]);if(Math.Abs(d.Sum(n=>n*n)-1)>.001) throw new ArgumentException("Invalid primitive direction.");
            foreach(string key in new[]{"radius_mm","length_mm","fit_rms_mm"}) {double value=primitive.Value<double?>(key)??-1;if(!Finite(value)||value<0||value>1e7) throw new ArgumentException("Invalid primitive dimension.");}
        }
        foreach(var constraint in Constraints)
        {double v=constraint.Value<double?>("ValueMm")??0,u=constraint.Value<double?>("UncertaintyMm")??0;if(!Finite(v)||v<=0||v>1e6||!Finite(u)||u<.001||u>v||string.IsNullOrWhiteSpace(constraint.Value<string>("Source"))) throw new ArgumentException("Invalid known dimension.");}
    }
    private static bool Finite(double n)=>!double.IsNaN(n)&&!double.IsInfinity(n);
    public static double[] Vec(JToken? token)
    {var p=token?.ToObject<double[]>()??throw new ArgumentException("Coordinate missing.");if(p.Length!=3||p.Any(x=>!Finite(x)||Math.Abs(x)>1e7)) throw new ArgumentException("Invalid coordinate.");return p;}
    public double[] Transform(double[] p,bool direction=false)=>new[]{Matrix[0]*p[0]+Matrix[1]*p[1]+Matrix[2]*p[2]+(direction?0:Matrix[3]),Matrix[4]*p[0]+Matrix[5]*p[1]+Matrix[6]*p[2]+(direction?0:Matrix[7]),Matrix[8]*p[0]+Matrix[9]*p[1]+Matrix[10]*p[2]+(direction?0:Matrix[11])};
}
