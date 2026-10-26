#if UNITY_EDITOR || XR_SO_ACCEPTANCE
using System.IO;
using System.Text;
using InventorXrSo.Core.Capture;
using Newtonsoft.Json.Linq;

namespace InventorXrSo.Xr.Capture
{
    /// <summary>QA only. Never camera or physical measurement evidence; excluded from ordinary player builds.</summary>
    public static class ScanReviewFixture
    {
        public static ScanReview Create()
        {
            const string id="11111111111111111111111111111111";
            var points=new[] {new ScanPoint {Id="p0",Position=new[]{0d,0,0},Observations=2},new ScanPoint {Id="p1",Position=new[]{1000d,0,0},Observations=3},new ScanPoint {Id="p2",Position=new[]{0d,1000,1000},Observations=2}};
            var json=Encoding.UTF8.GetBytes("{\"asset\":{\"version\":\"2.0\"},\"meshes\":[{\"primitives\":[{\"attributes\":{\"POSITION\":0},\"indices\":1,\"mode\":4}]}],\"buffers\":[{\"byteLength\":48}],\"bufferViews\":[{\"buffer\":0,\"byteOffset\":0,\"byteLength\":36},{\"buffer\":0,\"byteOffset\":36,\"byteLength\":12}],\"accessors\":[{\"bufferView\":0,\"componentType\":5126,\"count\":3,\"type\":\"VEC3\"},{\"bufferView\":1,\"componentType\":5125,\"count\":3,\"type\":\"SCALAR\"}]}");
            byte[] glb;
            using(var stream=new MemoryStream()) using(var writer=new BinaryWriter(stream))
            {
                int padded=(json.Length+3)&~3;writer.Write(0x46546C67u);writer.Write(2u);writer.Write(20+padded+8+48);writer.Write(padded);writer.Write(0x4E4F534Au);writer.Write(json);
                for(int i=json.Length;i<padded;i++) writer.Write((byte)32);writer.Write(48);writer.Write(0x004E4942u);
                foreach(float n in new[]{0f,0,0,1,0,0,0,1,-1}) writer.Write(n);foreach(uint n in new[]{0u,2u,1u}) writer.Write(n);
                glb=stream.ToArray();
            }
            return new ScanReview(id,1,points,new JObject {["frames_used"]=3,["frames_total"]=3,["triangles"]=1,["metric"]=new JObject {["AccuracyStatus"]="UNSCALED"}},
                new JArray(),new JObject {["scan_glb"]="metre; glTF right-handed Y-up"},
                new JObject {["ok"]=true,["scan_id"]=id,["revision"]=1,["version"]=0,["primitives"]=new JArray()},glb);
        }
    }
}
#endif
