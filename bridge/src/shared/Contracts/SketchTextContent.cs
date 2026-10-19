using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Contracts;

/// <summary>Bounded structured native text. Callers must resolve every parameter in the owning document before mutation.</summary>
public sealed class SketchTextContent
{
    public string FormattedText { get; }
    public IReadOnlyList<string> ParameterNames { get; }
    private SketchTextContent(string text,IEnumerable<string> parameters)
    { FormattedText=text;ParameterNames=Array.AsReadOnly(parameters.Distinct(StringComparer.Ordinal).ToArray()); }

    public static SketchTextContent Parse(JObject value)
    {
        if(value==null || value.Count!=2 || value["schema"]?.Type!=JTokenType.Integer || (string?)value["schema"]!="1"
            || !(value["runs"] is JArray runs) || runs.Count<1 || runs.Count>64)
            throw new ArgumentException("Text content requires schema 1 and 1–64 structured runs.");
        var parameters=new List<string>();var output=new StringBuilder();int characters=0;
        using(var writer=XmlWriter.Create(new StringWriter(output,System.Globalization.CultureInfo.InvariantCulture),
            new XmlWriterSettings{OmitXmlDeclaration=true,ConformanceLevel=ConformanceLevel.Fragment,CheckCharacters=true,NewLineHandling=NewLineHandling.None}))
        {
            foreach(var token in runs)
            {
                if(!(token is JObject run) || run.Count!=1) throw new ArgumentException("Each text run must contain only literal text or a local parameter name.");
                if(run["text"] is JValue textValue && textValue.Type==JTokenType.String && textValue.Value is string text)
                {
                    characters+=text.Length;
                    if(characters>4096) throw new ArgumentException("Literal text exceeds the 4096-character budget.");
                    VerifyCharacters(text);
                    string[] lines=text.Replace("\r\n","\n").Replace('\r','\n').Split('\n');
                    for(int line=0;line<lines.Length;line++)
                    { if(line>0){writer.WriteStartElement("Br");writer.WriteEndElement();}writer.WriteString(lines[line]); }
                }
                else if(run["parameter_name"] is JValue nameValue && nameValue.Type==JTokenType.String && nameValue.Value is string name)
                {
                    if(string.IsNullOrWhiteSpace(name) || name.Length>128 || name.Any(char.IsControl)) throw new ArgumentException("Local parameter name is invalid.");
                    VerifyCharacters(name);parameters.Add(name);
                    writer.WriteStartElement("Parameter");writer.WriteAttributeString("Name",name);writer.WriteAttributeString("Precision","0");
                    writer.WriteString(name);writer.WriteEndElement();
                }
                else throw new ArgumentException("Unknown native text run.");
            }
        }
        if(output.Length==0) throw new ArgumentException("Text content must not be empty.");
        return new SketchTextContent(output.ToString(),parameters);
    }
    private static void VerifyCharacters(string value)
    {
        try{XmlConvert.VerifyXmlChars(value);}
        catch(XmlException){throw new ArgumentException("Native text contains invalid XML characters.");}
    }
}
