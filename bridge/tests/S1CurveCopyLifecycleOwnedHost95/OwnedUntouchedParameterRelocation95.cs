using System.Xml;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;

// Fixture-only SaveCopy comparison. No parameter text copy capability.
internal static class OwnedUntouchedParameterRelocation95
{
    internal sealed record Binding(string DocumentId,string SketchKey,string TextKey,string ParameterKey)
    {
        internal JObject Json()=>new(){["document_id"]=DocumentId,["sketch_key"]=SketchKey,["text_key"]=TextKey,["parameter_key"]=ParameterKey};
    }
    private static void Require(bool condition,string reason)
    {if(!condition)throw new InvalidOperationException(reason);}
    private static JObject Parameter(JObject document)
    {
        var matches=((JArray)document["parameters"]!).OfType<JObject>().Where(p=>(string?)p["name"]=="S1QuestLength73").ToArray();
        Require(matches.Length==1,"Exactly one untouched fixture parameter required.");var parameter=matches[0];
        Require(!string.IsNullOrEmpty((string?)parameter["key"])&&(string?)parameter["expression"]=="10 mm"
            &&(string?)parameter["unit"]=="mm"&&(string?)parameter["kind"]=="kUserParameter"&&(double?)parameter["native_value"]==1,
            "Untouched fixture parameter identity/type/value differs.");return parameter;
    }
    internal static Binding Attest(JObject document,string ownedPath)
    {
        var sketch=((JArray)document["sketches"]!).OfType<JObject>().Single(s=>(string?)s["name"]=="S1_TextRead");
        var texts=((JArray)sketch["texts"]!).OfType<JObject>().Where(t=>(string?)t["text"]=="L=10").ToArray();
        Require(texts.Length==1,"Exactly one known untouched fixture parameter text required.");
        var binding=new Binding((string)document["document_id"]!,(string)sketch["key"]!,(string)texts[0]["key"]!,(string)Parameter(document)["key"]!);
        _=Normalize(document,ownedPath,binding);return binding;
    }
    internal static JObject Normalize(JObject document,string ownedPath,Binding binding)
    {
        Require(Path.IsPathFullyQualified(ownedPath),"Absolute owned fixture path required.");
        Require(binding.DocumentId.StartsWith("doc_",StringComparison.Ordinal)&&Guid.TryParse(binding.DocumentId.Substring(4),out _)
            &&(string?)document["document_id"]==binding.DocumentId&&"doc_"+(string?)document["internal_name"]==binding.DocumentId,
            "Untouched field native document identity differs.");
        Require(!string.IsNullOrEmpty(binding.SketchKey)&&!string.IsNullOrEmpty(binding.TextKey)
            &&(string?)Parameter(document)["key"]==binding.ParameterKey,"Untouched field parameter binding differs.");
        Require(Path.IsPathFullyQualified((string?)document["path"]??"")&&string.Equals(Path.GetFullPath((string)document["path"]!),Path.GetFullPath(ownedPath),StringComparison.OrdinalIgnoreCase),
            "Untouched field document path differs.");
        var result=(JObject)document.DeepClone();var sketches=((JArray)result["sketches"]!).OfType<JObject>().Where(s=>(string?)s["key"]==binding.SketchKey).ToArray();
        Require(sketches.Length==1&&(string?)sketches[0]["name"]=="S1_TextRead","Untouched fixture sketch binding differs.");
        var texts=((JArray)sketches[0]["texts"]!).OfType<JObject>().Where(t=>(string?)t["key"]==binding.TextKey).ToArray();
        Require(texts.Length==1,"Untouched fixture text identity is missing or duplicated.");var text=texts[0];
        Require((string?)text["key"]==binding.TextKey&&(string?)text["native_type"]=="kTextBoxObject"&&(string?)text["text"]=="L=10",
            "Untouched fixture text native identity/value differs.");
        string markup=(string?)text["formatted_text"]??"";Require(markup.Length is >0 and <=4096,"Untouched field markup bound differs.");
        var settings=new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=4111};
        using var input=new StringReader("<owner>"+markup+"</owner>");using var reader=XmlReader.Create(input,settings);
        var root=XElement.Load(reader,LoadOptions.PreserveWhitespace);var nodes=root.Nodes().ToArray();
        Require(nodes.Length==2&&nodes[0].GetType()==typeof(XText)&&((XText)nodes[0]).Value=="L="&&nodes[1] is XElement,"Untouched field grammar differs.");
        var element=(XElement)nodes[1];
        Require(element.Name==XName.Get("Parameter")&&element.Attributes().Count()==4
            &&element.Attributes().All(a=>a.Name.Namespace==XNamespace.None&&a.Name.LocalName is "Resolved" or "ComponentIdentifier" or "Name" or "Precision")
            &&(string?)element.Attribute("Resolved")=="True"&&(string?)element.Attribute("Name")=="S1QuestLength73"
            &&(string?)element.Attribute("Precision")=="0"&&element.Nodes().Count()==1&&element.FirstNode!.GetType()==typeof(XText)&&element.Value=="10",
            "Untouched field XML binding/value differs.");
        string component=(string?)element.Attribute("ComponentIdentifier")??"";
        Require(Path.IsPathFullyQualified(component)&&string.Equals(Path.GetFullPath(component),Path.GetFullPath(ownedPath),StringComparison.OrdinalIgnoreCase),
            "Untouched field is not bound to the exact owned native document.");
        element.Attribute("ComponentIdentifier")!.Value="owned-local:"+binding.DocumentId+"#"+binding.ParameterKey;
        string canonical=root.ToString(SaveOptions.DisableFormatting);text["formatted_text"]=canonical.Substring(7,canonical.Length-15);
        return result;
    }
}
