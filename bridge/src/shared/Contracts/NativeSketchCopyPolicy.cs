using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Contracts;

/// <summary>Pure preflight rules for the experimental copy foundation, not capability or desktop parity.</summary>
public static class NativeSketchCopyPolicy
{
    public const int MaxSelections = 2000; // Existing native editor representation ceiling.
    public const int MaxLiteralCharacters = 4096; // Existing SketchTextContent literal ceiling.
    public sealed class Reference
    {
        internal Reference(string document, string revision, string sketch, string entity, string type)
        { DocumentId=document; Revision=revision; SketchId=sketch; EntityId=entity; Type=type; }
        public string DocumentId { get; }
        public string Revision { get; }
        public string SketchId { get; }
        public string EntityId { get; }
        public string Type { get; }
    }
    public sealed class Request
    {
        internal Request(IEnumerable<Reference> references,double x,double y)
        { References=Array.AsReadOnly(references.ToArray()); DeltaXmm=x; DeltaYmm=y; }
        public IReadOnlyList<Reference> References { get; }
        public double DeltaXmm { get; }
        public double DeltaYmm { get; }
    }
    public static Request Parse(JObject options)
    {
        if(options==null || options.Properties().Any(p=>p.Name!="references" && p.Name!="delta_mm" && p.Name!="remove_constraints"))
            throw new ArgumentException("Unknown native copy option.");
        if(options.TryGetValue("remove_constraints",out var remove)
            && (remove.Type!=JTokenType.Boolean || (bool)remove))
            throw new ArgumentException("Copy with constraint removal is not implemented.");
        if(options["delta_mm"] is not JArray delta || delta.Count!=2)
            throw new ArgumentException("Copy requires a planar delta in millimetres.");
        double x=Number(delta[0]),y=Number(delta[1]);
        if(x/10==0 && y/10==0) throw new ArgumentException("Copy translation must be nonzero.");
        if(options["references"] is not JArray refs || refs.Count<1 || refs.Count>MaxSelections)
            throw new ArgumentException("Select 1–2000 whole native line/text/circle/arc objects.");
        var result=new List<Reference>();var ids=new HashSet<string>(StringComparer.Ordinal);
        string[] names={"document_id","revision","sketch_id","entity_id","type","subpart"};
        foreach(var token in refs)
        {
            if(token is not JObject reference || reference.Count!=6 || reference.Properties().Any(p=>!names.Contains(p.Name,StringComparer.Ordinal)))
                throw new ArgumentException("Copy requires complete typed native references.");
            string Text(string name)
            {
                if(reference[name]?.Type!=JTokenType.String || string.IsNullOrWhiteSpace((string?)reference[name])
                    || ((string)reference[name]!).Length>2048 || ((string)reference[name]!).Any(char.IsControl))
                    throw new ArgumentException("Native copy reference text is invalid.");
                return (string)reference[name]!;
            }
            string document=Text("document_id"),revision=Text("revision"),sketch=Text("sketch_id"),id=Text("entity_id"),type=Text("type");
            if((type!="line" && type!="text" && type!="circle" && type!="arc") || Text("subpart")!="Entity" || !ids.Add(id))
                throw new ArgumentException("Select each supported whole native line/text/circle/arc only once.");
            result.Add(new Reference(document,revision,sketch,id,type));
        }
        return new Request(result,x,y);
    }
    public static void ValidateBinding(Request request,string documentId,string revision,string sketchId)
    {
        if(request==null || string.IsNullOrWhiteSpace(documentId) || string.IsNullOrWhiteSpace(revision) || string.IsNullOrWhiteSpace(sketchId)
            || request.References.Any(r=>r.DocumentId!=documentId || r.Revision!=revision || r.SketchId!=sketchId))
            throw new ArgumentException("Copy references do not match the current document/sketch/revision.");
    }
    public static void ValidateTextProfile(bool fitted,bool singleLine,double rotationRadians,double widthScale,double lineSpacing,
        double fontSizeMm,bool localOwnedStyle,bool enumsValid,bool useBackgroundColor=false)
    {
        if(!fitted || !singleLine || rotationRadians!=0 || !Finite(rotationRadians) || !Finite(widthScale) || widthScale<=0
            || !Finite(lineSpacing) || lineSpacing<=0 || !Finite(fontSizeMm) || fontSizeMm<=0 || !localOwnedStyle || !enumsValid || useBackgroundColor)
            throw new ArgumentException("Native copy currently requires fitted single-line unrotated text with finite properties and an owned local style.");
    }
    /// <summary>Validate the exact native constructor input; do not reserialize it for copying.
    /// Parameter fields remain an explicit backlog until generic native local-component binding is proved.</summary>
    public static string ValidateLiteralMarkup(string markup)
    {
        if(string.IsNullOrEmpty(markup) || markup.Length>MaxLiteralCharacters*6)
            throw new ArgumentException("Native literal markup exceeds the existing text character budget.");
        try
        {
            var settings=new XmlReaderSettings { DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,
                MaxCharactersInDocument=MaxLiteralCharacters*6+13,IgnoreWhitespace=false,IgnoreComments=false };
            using(var input=new StringReader("<copy>"+markup+"</copy>"))
            using(var reader=XmlReader.Create(input,settings))
            {
                var root=XElement.Load(reader,LoadOptions.PreserveWhitespace);
                if(root.Nodes().Any(node=>node.GetType()!=typeof(XText)) || root.Value.Length==0 || root.Value.Length>MaxLiteralCharacters
                    || root.Value.Any(c=>c=='\r' || c=='\n' || char.IsControl(c)))
                    throw new ArgumentException("Only single-line literal/escaped text can currently be copied; parameter fields and formatting tags are unavailable.");
                return root.Value;
            }
        }
        catch(XmlException) { throw new ArgumentException("Native literal markup is invalid."); }
    }
    /// <summary>Only the two nondeletable native curve/own-endpoint incidences are intrinsic.
    /// External coincidence (even nondeletable), point/point and inferred coordinates are not free-line proof.</summary>
    public static bool IsIntrinsicLineCoincidence(bool deletable,string one,string two,string line,string start,string end)
        =>!deletable && !string.IsNullOrEmpty(line) && !string.IsNullOrEmpty(start) && !string.IsNullOrEmpty(end)
          && line!=start && line!=end && start!=end
          && (one==line && (two==start || two==end) || two==line && (one==start || one==end));
    public static void ValidateLineProfile(bool reference,bool construction,bool centerline,int curveConstraints,int startConstraints,
        int endConstraints,bool hasStructuralOwner,bool endpointsShared)
    {
        if(reference || construction || centerline || curveConstraints!=0 || startConstraints!=0 || endConstraints!=0
            || hasStructuralOwner || endpointsShared)
            throw new ArgumentException("Copy currently requires an independent free native line without structural or constraint dependencies.");
    }
    public static (double X,double Y) Translate(double x,double y,Request request)
    {
        if(request==null || !Finite(x) || !Finite(y) || !Finite(x+request.DeltaXmm) || !Finite(y+request.DeltaYmm))
            throw new ArgumentException("Translated native coordinates are not finite.");
        return (x+request.DeltaXmm,y+request.DeltaYmm);
    }
    public static bool CopiedPointMatches(double x,double y,double expectedX,double expectedY)
        =>Finite(x) && Finite(y) && Finite(expectedX) && Finite(expectedY)
          && Math.Abs(x-expectedX)<=1e-5 && Math.Abs(y-expectedY)<=1e-5;
    private static double Number(JToken value)
    {
        if(value.Type is not (JTokenType.Float or JTokenType.Integer)) throw new ArgumentException("Copy delta must contain finite numeric coordinates.");
        double number=(double)value;
        if(!Finite(number)) throw new ArgumentException("Copy delta is not finite.");
        return number;
    }
    private static bool Finite(double value)=>!double.IsNaN(value) && !double.IsInfinity(value);
}
