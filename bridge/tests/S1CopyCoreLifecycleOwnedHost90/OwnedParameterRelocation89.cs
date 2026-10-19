using System.Xml;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;

// Fixture-only comparison. This does not enable copying parameter text.
internal static class OwnedParameterRelocation89
{
    internal static JObject Normalize(JObject state,string ownedNativePath)
    {
        if(!Path.IsPathFullyQualified(ownedNativePath)) throw new InvalidOperationException("Owned native absolute path required.");
        string documentId=(string?)state["document_id"]??"";
        if(!documentId.StartsWith("doc_",StringComparison.Ordinal)||!Guid.TryParse(documentId.Substring(4),out _))
            throw new InvalidOperationException("Native document identity required.");
        var users=(JArray)state["user_parameters"]!;
        var matches=users.OfType<JObject>().Where(p=>(string?)p["name"]=="S1Copy88Length").ToArray();
        if(matches.Length!=1 || string.IsNullOrEmpty((string?)matches[0]["key"])
            || (string?)matches[0]["units"]!="mm" || (double?)matches[0]["value"]!=1.3)
            throw new InvalidOperationException("Exact unchanged fixture parameter required.");
        var result=new JObject { ["sketch"]=state["sketch"]!.DeepClone(),["styles"]=state["styles"]!.DeepClone(),["user_parameters"]=users.DeepClone() };
        int fields=0;
        foreach(JObject text in (JArray)result["sketch"]!["texts"]!)
        {
            string markup=(string?)text["formatted_text"]??"";
            if(!markup.Contains("<Parameter",StringComparison.Ordinal)) continue;
            var settings=new XmlReaderSettings { DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=4096 };
            using var input=new StringReader("<owner>"+markup+"</owner>");
            using var reader=XmlReader.Create(input,settings);
            var root=XElement.Load(reader,LoadOptions.PreserveWhitespace);
            var nodes=root.Nodes().ToArray();
            if(nodes.Length!=2 || nodes[0] is not XText prefix || prefix.Value!="Bound=" || nodes[1] is not XElement parameter
                || parameter.Name!=XName.Get("Parameter") || parameter.Attributes().Count()!=4
                || parameter.Attributes().Any(a=>a.Name.Namespace!=XNamespace.None || a.Name.LocalName is not ("Resolved" or "ComponentIdentifier" or "Name" or "Precision"))
                || (string?)parameter.Attribute("Resolved")!="True" || (string?)parameter.Attribute("Name")!="S1Copy88Length"
                || (string?)parameter.Attribute("Precision")!="0" || parameter.Nodes().Count()!=1 || parameter.FirstNode is not XText
                || parameter.Value!="13") throw new InvalidOperationException("Unexpected fixture parameter markup; no normalization allowed.");
            string component=(string?)parameter.Attribute("ComponentIdentifier")??"";
            if(!Path.IsPathFullyQualified(component) || !string.Equals(Path.GetFullPath(component),Path.GetFullPath(ownedNativePath),StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Native field does not bind the exact owned local document path.");
            parameter.Attribute("ComponentIdentifier")!.Value="owned-local:"+documentId+"#"+(string)matches[0]["key"]!;
            string canonical=root.ToString(SaveOptions.DisableFormatting);
            text["formatted_text"]=canonical.Substring(7,canonical.Length-15);
            fields++;
        }
        if(fields!=1) throw new InvalidOperationException("Exactly one unselected fixture parameter field required.");
        return result;
    }
}
