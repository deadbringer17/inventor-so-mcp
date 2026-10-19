using Inventor;
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Mcp;
using InventorXrSo.Core.Pairing;
using Newtonsoft.Json.Linq;

internal static partial class Program
{
    private static void RunParameters67(PartDocument doc,InventorBackend backend,ProbeTransport transport,PairedServer paired,JObject report,JArray cases,bool sheet)
    {
        string kind=sheet ? "sheetmetal" : "part";report["phase"]=kind+"_parameters67";
        var boolean=doc.ComponentDefinition.Parameters.UserParameters.AddByValue("S1Boolean67",true,UnitsTypeEnum.kBooleanUnits);
        var text=doc.ComponentDefinition.Parameters.UserParameters.AddByValue("S1Text67","Initial67",UnitsTypeEnum.kTextUnits);
        if(!doc.Update2(false))throw new ProbeFailure("Typed parameter fixture update failed.");
        var mcp=new McpClient(transport,paired.BaseUrl,paired.Token);mcp.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
        DocumentState state;var deadline=DateTime.UtcNow.AddSeconds(15);
        while(true)
        {
            try{state=backend.GetDocumentStateAsync(CancellationToken.None).GetAwaiter().GetResult();break;}
            catch(McpToolException error)when(error.Code=="NO_TARGET"&&DateTime.UtcNow<deadline){Thread.Sleep(500);}
        }
        var row=new JObject{["kind"]=kind,["observations"]=new JArray()};cases.Add(row);var observations=(JArray)row["observations"]!;
        foreach(var values in new[]{(flag:false,text:"false"),(flag:true,text:"Acciaio α🔧 & <tag> \"Q\"")})
        {
            boolean.Value=values.flag;text.Value=values.text;if(!doc.Update2(false))throw new ProbeFailure("Typed parameter source update failed.");
            state=backend.GetDocumentStateAsync(CancellationToken.None).GetAwaiter().GetResult();
            string revision=doc.DatabaseRevisionId;bool dirty=doc.Dirty;
            var b=mcp.CallToolAsync("inventor_get_parameter",new JObject{["name"]=boolean.Name},CancellationToken.None).GetAwaiter().GetResult();
            var t=mcp.CallToolAsync("inventor_get_parameter",new JObject{["name"]=text.Name},CancellationToken.None).GetAwaiter().GetResult();
            var list=mcp.CallToolAsync("inventor_list_parameters",new JObject{["document_id"]=state.DocumentId},CancellationToken.None).GetAwaiter().GetResult();
            if(b["value_bool"]?.Type!=JTokenType.Boolean||(bool)b["value_bool"]! !=values.flag||(bool)boolean.Value!=values.flag
                ||t["value_text"]?.Type!=JTokenType.String||(string?)t["value_text"]!=values.text||(string)text.Value!=values.text
                ||b["evaluated"]?.Type!=JTokenType.Null||t["evaluated"]?.Type!=JTokenType.Null
                ||!(list["parameters"] is JArray all)||!JToken.DeepEquals(all.Single(p=>(string?)p["name"]==boolean.Name),b)
                ||!JToken.DeepEquals(all.Single(p=>(string?)p["name"]==text.Name),t)
                ||doc.DatabaseRevisionId!=revision||doc.Dirty!=dirty)throw new ProbeFailure("Typed native parameter HTTP read differs or changed CAD.");
            observations.Add(new JObject{["boolean_exact"]=true,["text_exact"]=true,["list_get_equal"]=true,["legacy_evaluated_null"]=true,["read_document_unchanged"]=true});
        }
        row["outcome"]="PASS";
    }
}
