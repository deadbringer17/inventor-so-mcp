using System.Runtime.InteropServices;
using Bimwright.Ipt.Shared.Contracts;
using Inventor;
using Newtonsoft.Json.Linq;
using File = System.IO.File;
using Path = System.IO.Path;

internal static partial class Program
{

    private sealed record CopyBinding89(string DocumentId,string SketchKey,string LineKey,string TextKey);
    private static void Require89(bool value,string assertion) { if(!value) throw new HostFailure73(assertion); }
    private static void RequireIdle89()
    {
        GuardDocuments73();
        Require89(Application!.CommandManager.ActiveCommand=="AppSelectNorthwestArrowCmd","Native command prevents owned lifecycle89.");
        var current=Application.TransactionManager.CurrentTransaction;
        if(current==null) return;
        bool identified=true;
        try { _=current.HasParentTransaction; }
        catch(COMException error) when(error.ErrorCode==unchecked((int)0x80004005)) { identified=false; }
        Require89(!identified,"Identified transaction prevents lifecycle89.");
    }
    private static CopyBinding89 LifecycleBinding89(OwnedDocument73 owned,JObject client)
    {
        var workflows=((JArray)client["cases"]!).OfType<JObject>().Where(w=>(string?)w["document_id"]==owned.DocumentId).ToArray();
        Require89(workflows.Length==3,"Three copy workflows required per owned document.");
        string Source(string type)
        {
            var ids=workflows.SelectMany(w=>((JArray)w["pairs"]!).OfType<JObject>()).Where(p=>(string?)p["type"]==type)
                .Select(p=>(string)p["source"]!["entity_id"]!).Distinct(StringComparer.Ordinal).ToArray();
            Require89(ids.Length==1,"Source pair identity is ambiguous.");
            var reference=PersistentEntityReference.Decode(ids[0]);
            Require89(reference.DocumentId==owned.DocumentId&&reference.EntityType==(type=="text"?"sketch_text":"sketch_entity")&&reference.Context.Length==0,"Source typed ownership differs.");
            return Convert.ToBase64String(reference.Key);
        }
        var sketchIds=workflows.SelectMany(w=>((JArray)w["pairs"]!).OfType<JObject>()).Select(p=>(string)p["source"]!["sketch_id"]!).Distinct(StringComparer.Ordinal).ToArray();
        Require89(sketchIds.Length==1,"Lifecycle source sketch is ambiguous.");
        var sketchRef=PersistentEntityReference.Decode(sketchIds[0]);
        Require89(sketchRef.DocumentId==owned.DocumentId&&sketchRef.EntityType=="sketch"&&sketchRef.Context.Length==0,"Lifecycle typed sketch ownership differs.");
        return new CopyBinding89(owned.DocumentId,Convert.ToBase64String(sketchRef.Key),Source("line"),Source("text"));
    }
    private static JObject ReadCopyLifecycle89(OwnedDocument73 owned,CopyBinding89 binding)
    {
        string previousPhase=Phase;
        GuardDocuments73(); var doc=owned.Document; string revision=doc.DatabaseRevisionId; bool dirty=doc.Dirty;
        var full=CopyFullNative88();
        var document=((JArray)full["documents"]!).Single(d=>(string?)d["document_id"]==owned.DocumentId);
        var sketch=((JArray)document["sketches"]!).Single(s=>(string?)s["key"]==binding.SketchKey);
        Require89((string?)sketch["name"]=="S1_Copy88","Exact source sketch key resolves to wrong name.");
        var styles=((JArray)full["local_text_styles88"]!).Single(d=>(string?)d["document_id"]==owned.DocumentId)["styles"]!;
        var users=new JArray(doc.ComponentDefinition.Parameters.UserParameters.Cast<UserParameter>().Select(p=>new JObject {
            ["key"]=Key73(p),["name"]=p.Name,["expression"]=p.Expression,["units"]=p.get_Units(),["value"]=JToken.FromObject(p.Value) }));
        Require89(revision==doc.DatabaseRevisionId&&dirty==doc.Dirty,"Lifecycle native read changed Dirty/DBRevision."); GuardDocuments73();
        var result=new JObject { ["document_id"]=owned.DocumentId,["internal_name"]=owned.InternalName,["database_revision_id"]=revision,["dirty"]=dirty,
            ["sketch"]=sketch.DeepClone(),["styles"]=styles.DeepClone(),["user_parameters"]=users };
        Phase=previousPhase; return result;
    }
    private static JObject Semantic89(JObject state)=>new(){["sketch"]=state["sketch"]!.DeepClone(),["styles"]=state["styles"]!.DeepClone(),["user_parameters"]=state["user_parameters"]!.DeepClone()};
    private static void CheckIndependence89(JObject initial,JObject after,CopyBinding89 binding)
    {
        var a=initial["sketch"]!;var b=after["sketch"]!;
        var sourceLine=((JArray)a["entities"]!).Single(e=>(string?)e["key"]==binding.LineKey);
        var sourceText=((JArray)a["texts"]!).Single(e=>(string?)e["key"]==binding.TextKey);
        var excluded=new HashSet<string>(new[]{binding.LineKey,(string)sourceLine["start_key"]!,(string)sourceLine["end_key"]!,
            (string)sourceText["copy_details88"]!["origin_key"]!},StringComparer.Ordinal);
        foreach(string collection in new[]{"entities","texts"})
        {
            var before=(JArray)a[collection]!;var current=(JArray)b[collection]!;
            Require89(before.Select(e=>(string)e["key"]!).ToHashSet(StringComparer.Ordinal).SetEquals(current.Select(e=>(string)e["key"]!)),"Native identity set changed during source updates.");
            foreach(var entity in before)
            {
                string key=(string)entity["key"]!;var same=current.Single(e=>(string?)e["key"]==key);
                if(collection=="texts"&&key==binding.TextKey)
                {
                    Require89((string?)entity["copy_details88"]!["origin_key"]==(string?)same["copy_details88"]!["origin_key"],"Source text origin native identity changed.");
                    continue;
                }
                if(!excluded.Contains(key)) Require89(JToken.DeepEquals(entity,same),"Copy or unrelated native geometry/properties/glyphs changed with its source.");
                if(key==binding.LineKey) Require89((string?)entity["start_key"]==(string?)same["start_key"]&&(string?)entity["end_key"]==(string?)same["end_key"],"Source line endpoint identity changed.");
            }
        }
        foreach(string field in new[]{"constraints","dimensions"}) Require89(JToken.DeepEquals(a[field],b[field]),"Native relation/dimension changed with a free source edit.");
        Require89(JToken.DeepEquals(initial["styles"],after["styles"]),"Shared native style changed; no style edits are permitted in independence test.");
        Require89(JToken.DeepEquals(initial["user_parameters"],after["user_parameters"]),"Unrelated user parameters changed.");
    }
    private static void NearPoint89(JToken? token,double x,double y)
    {
        Require89(token is JArray p&&p.Count==2&&Math.Abs((double)p[0]-x)<=1e-5&&Math.Abs((double)p[1]-y)<=1e-5,"Native source point differs from requested millimetres.");
    }
    private static JObject? FirstDifference89(JToken? before,JToken? after,string path)
    {
        if(JToken.DeepEquals(before,after)) return null;
        if(before is JObject a&&after is JObject b)
        {
            foreach(string name in a.Properties().Select(p=>p.Name).Union(b.Properties().Select(p=>p.Name),StringComparer.Ordinal))
            {
                var difference=FirstDifference89(a[name],b[name],path+"."+name);
                if(difference!=null) return difference;
            }
        }
        if(before is JArray x&&after is JArray y&&x.Count==y.Count)
            for(int i=0;i<x.Count;i++)
            {
                var difference=FirstDifference89(x[i],y[i],path+"["+i+"]");
                if(difference!=null) return difference;
            }
        JToken Summary(JToken? value)=>value is JValue scalar ? scalar.DeepClone()
            : new JObject { ["type"]=value?.Type.ToString()??"missing",["count"]=value is JContainer container?container.Count:0 };
        return new JObject { ["path"]=path,["expected"]=Summary(before),["actual"]=Summary(after) };
    }
}
