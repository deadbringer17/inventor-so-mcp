using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using InventorXrSo.Core.Pairing;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

internal static partial class Program
{
    private sealed record Sources94(string Context,string DocumentId,string SketchId,Dictionary<string,string> Sources);
    private sealed record Catalog94(JObject Raw,int Pid,long Start,Dictionary<string,Sources94> Documents);
    private static readonly string[] Roles94={"line","text","parameter_text","circle","arc_quarter","arc_wrap","arc_major","arc_clockwise"};
    private static readonly string[] Workflows94={"circle","arc_quarter","arc_wrap","arc_major","arc_clockwise","mixed_curves","mixed_all","line","text"};
    private static readonly UTF8Encoding Utf8Strict94=new(false,true);
    private static void Closed94(JObject value,params string[] fields)=>Require(value.Count==fields.Length&&value.Properties().All(p=>fields.Contains(p.Name,StringComparer.Ordinal)),"Closed public metadata contains missing/unexpected fields.");
    private static string Text94(JObject value,string name,int limit=131072)
    {
        Require(value[name]?.Type==JTokenType.String,"Public metadata string field is missing.");string text=(string)value[name]!;
        Require(text.Length>0&&text.Length<=limit&&!text.Any(char.IsControl),"Public metadata string field is invalid.");return text;
    }
    private static JObject Parse94(string json,int depth)
    {
        using var input=new StringReader(json);using var reader=new JsonTextReader(input){MaxDepth=depth,DateParseHandling=DateParseHandling.None};
        var result=JObject.Load(reader,new JsonLoadSettings{DuplicatePropertyNameHandling=DuplicatePropertyNameHandling.Error});
        Require(!reader.Read(),"Public metadata contains trailing JSON.");return result;
    }
    private static string Opaque94(string value,string document,string type)
    {
        Require(value.StartsWith("ent_",StringComparison.Ordinal)&&value.Length<=131072,"Public identity is not bounded opaque data.");
        string encoded=value[4..].Replace('-','+').Replace('_','/');encoded=encoded.PadRight((encoded.Length+3)/4*4,'=');
        var raw=Parse94(Utf8Strict94.GetString(Convert.FromBase64String(encoded)),8);Closed94(raw,"Version","DocumentId","EntityType","Key","Context");
        Require(raw["Version"]?.Type==JTokenType.Integer&&(int)raw["Version"]! ==1&&Text94(raw,"DocumentId",256)==document&&Text94(raw,"EntityType",128)==type
            &&raw["Context"]?.Type==JTokenType.String&&(string)raw["Context"]! =="","Opaque identity document/type/context differs.");
        string key=Text94(raw,"Key",131072);Require(Convert.FromBase64String(key).Length is >0 and <=65536,"Opaque native key exceeds bound.");return key;
    }
    private static async Task<JObject> ReadPublic94(string path,int maxBytes,CancellationToken ct)
    {
        Require((File.GetAttributes(path)&FileAttributes.ReparsePoint)==0,"Public metadata reparse file forbidden.");
        using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
        Require(stream.Length>0&&stream.Length<=maxBytes,"Public metadata exceeds the explicit IPC byte bound.");
        byte[] bytes=new byte[checked((int)stream.Length)];await stream.ReadExactlyAsync(bytes,ct);Require(stream.ReadByte()==-1,"Public metadata changed during read.");
        return Parse94(Utf8Strict94.GetString(bytes),16);
    }
    private static async Task<Catalog94> ReadCatalog94(string path,S1AcceptanceBootstrapMetadata binding,CancellationToken ct)
    {
        var raw=await ReadPublic94(path,1048576,ct);Closed94(raw,"schema","run_id","target_id","owned_pid","owned_start_utc_ticks","documents");
        Require(raw["schema"]?.Type==JTokenType.Integer&&(int)raw["schema"]! ==1&&Text94(raw,"run_id",32)==binding.RunId&&Text94(raw,"target_id",128)==binding.TargetId
            &&raw["owned_pid"]?.Type==JTokenType.Integer&&raw["owned_start_utc_ticks"]?.Type==JTokenType.Integer,"Source catalog bootstrap/schema binding differs.");
        int pid=(int)raw["owned_pid"]!;long start=(long)raw["owned_start_utc_ticks"]!;
        Require(pid>0&&start>0&&start<=DateTime.UtcNow.Ticks&&binding.TargetId=="inventor-2027-"+pid.ToString(CultureInfo.InvariantCulture),"Source catalog owner process differs.");
        Require(raw["documents"] is JArray documents&&documents.Count==2&&documents.All(d=>d is JObject),"Source catalog requires exactly two documents.");
        var result=new Dictionary<string,Sources94>(StringComparer.Ordinal);
        foreach(var doc in ((JArray)raw["documents"]!).Cast<JObject>())
        {
            Closed94(doc,"context","document_id","sketch_id","sources");string context=Text94(doc,"context",16);Require(context is "Part" or "Sheet","Source context is invalid.");
            string id=Text94(doc,"document_id",256),sketch=Text94(doc,"sketch_id");Require(id==(context=="Part"?binding.PartDocumentId:binding.SheetDocumentId),"Catalog document is not an owned bootstrap fixture.");
            Opaque94(sketch,id,"sketch");Require(doc["sources"] is JObject,"Source catalog map is missing.");var sources=(JObject)doc["sources"]!;Closed94(sources,Roles94);
            var ids=new Dictionary<string,string>(StringComparer.Ordinal);var keys=new HashSet<string>(StringComparer.Ordinal);
            foreach(string role in Roles94){string value=Text94(sources,role);Require(keys.Add(Opaque94(value,id,role is "text" or "parameter_text"?"sketch_text":"sketch_entity")),"Catalog source native keys collide.");ids.Add(role,value);}
            Require(result.TryAdd(context,new Sources94(context,id,sketch,ids)),"Catalog context duplicated.");
        }
        Require(result.Count==2&&result.ContainsKey("Part")&&result.ContainsKey("Sheet"),"Catalog contexts incomplete.");return new Catalog94((JObject)raw.DeepClone(),pid,start,result);
    }
    private static async Task Publish94(string path,JObject value,CancellationToken ct,bool replace=false)
    {
        string dir=Path.GetDirectoryName(Path.GetFullPath(path))!;Require(Directory.Exists(dir)&&(File.GetAttributes(dir)&FileAttributes.ReparsePoint)==0,"Owned public IPC directory required.");
        string temporary=Path.Combine(dir,".copy94-"+Guid.NewGuid().ToString("N")+".tmp");
        try{using(var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)){byte[] bytes=Utf8Strict94.GetBytes(value.ToString(Formatting.Indented));await stream.WriteAsync(bytes,ct);stream.Flush(true);}File.Move(temporary,path,replace);}
        finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
}
