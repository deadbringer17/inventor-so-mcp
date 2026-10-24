using Inventor;
using Bimwright.Ipt.Shared.Contracts;
using Newtonsoft.Json.Linq;
using File = System.IO.File;
using Path = System.IO.Path;

internal static partial class Program
{
    private static readonly Dictionary<string, JObject> SourceBaseline88 = new(StringComparer.Ordinal);
    private static void CreateCopyFixture88(PartDocument doc)
    {
        string previousPhase=Phase;
        Phase="copy_fixture88_create_sketch";
        GuardDocuments73(); var g=Application!.TransientGeometry;
        var sketch=doc.ComponentDefinition.Sketches.Add(doc.ComponentDefinition.WorkPlanes[3]); sketch.Name="S1_Copy88";
        var sourceLine94=sketch.SketchLines.AddByTwoPoints(g.CreatePoint2d(1,1),g.CreatePoint2d(3.3,1.8));
        Phase="copy_fixture88_create_seed";
        var seed=sketch.TextBoxes.AddFitted(g.CreatePoint2d(2,3),"Copy88 &amp; native",Type.Missing);
        Phase="copy_fixture88_copy_style";
        var style=(TextStyle)seed.Style.Copy("S1_Copy88_OwnedLocal");
        if(style.StyleLocation!=StyleLocationEnum.kLocalStyleLocation) throw new HostFailure73("Copy88 style is not local.");
        style.FontSize=doc.ComponentDefinition is SheetMetalComponentDefinition ? 0.37 : 0.46;
        style.Bold=false; style.Italic=false; style.Underline=false;
        style.Color=Application.TransientObjects.CreateColor(21,75,133,1);
        seed.Delete();
        Phase="copy_fixture88_create_literal";
        var literal=sketch.TextBoxes.AddFitted(g.CreatePoint2d(2,3),"Copy88 &amp; native",style);
        literal.SingleLineText=true; literal.Rotation=0; literal.WidthScale=1.15;
        literal.Color=Application.TransientObjects.CreateColor(112,39,91,1);
        literal.HorizontalJustification=HorizontalTextAlignmentEnum.kAlignTextLeft;
        literal.VerticalJustification=VerticalTextAlignmentEnum.kAlignTextLower;
        doc.ComponentDefinition.Parameters.UserParameters.AddByExpression("S1Copy88Length","13 mm","mm");
        Phase="copy_fixture88_create_parameter_text";
        var bound=sketch.TextBoxes.AddFitted(g.CreatePoint2d(2,5),"Bound=<Parameter Name='S1Copy88Length' Precision='0'>S1Copy88Length</Parameter>",style);
        bound.SingleLineText=true; bound.Rotation=0;
        if(!doc.Rebuild2(false)||sketch.SketchLines.Count!=1||sketch.TextBoxes.Count!=2)
            throw new HostFailure73("Copy88 native fixture differs.");
        CreateCurves94(doc,sketch,sourceLine94,literal,bound);
        Phase=previousPhase;
    }
    private static JObject CopyTextDetails88(TextBox text)
    {
        string previousPhase=Phase;Phase="copy88_native_glyph_capture";
        int vertices=0,faces=0;double[] coordinates=Array.Empty<double>();int[] indices=Array.Empty<int>();
        text.GetFacetsInfo(out vertices,out faces,out coordinates,out indices);
        if(coordinates.Length!=vertices*3||indices.Length!=faces*3||coordinates.Any(v=>!double.IsFinite(v))||indices.Any(i=>i<1||i>vertices))
            throw new HostFailure73("Copy88 raw glyph arrays are invalid.");
        Phase="copy88_native_text_style_capture";
        var style=text.Style;
        var result=new JObject { ["origin_key"]=Key73(text.OriginSketchPoint),["origin_mm"]=new JArray(text.Origin.X*10,text.Origin.Y*10),
            ["single_line"]=text.SingleLineText,["horizontal"]=(int)text.HorizontalJustification,["vertical"]=(int)text.VerticalJustification,
            ["line_spacing_type"]=(int)text.LineSpacingType,["line_spacing"]=text.LineSpacing,
            ["style_key"]=Key73(style),["style_location"]=style.StyleLocation.ToString(),["font"]=style.Font,["font_size_mm"]=style.FontSize*10,
            ["bold"]=style.Bold,["italic"]=style.Italic,["underline"]=style.Underline,
            ["raw_vertices_model_mm"]=new JArray(coordinates.Select(v=>v*10)),["raw_indices_native_one_based"]=new JArray(indices),
            ["color"]=Color88(text.Color),["style_color"]=Color88(style.Color) };
        Phase=previousPhase;return result;
    }
    private static JObject Color88(Color color)=>new(){["red"]=color.Red,["green"]=color.Green,["blue"]=color.Blue,["opacity"]=color.Opacity,["source_type"]=(int)color.ColorSourceType};
    private static void RememberCopySources88()
    {
        foreach(var owned in Documents)
        {
            var sketch=owned.Document.ComponentDefinition.Sketches.Cast<PlanarSketch>().Single(s=>s.Name=="S1_Copy88");
            foreach(SketchLine line in sketch.SketchLines) SourceBaseline88.Add(owned.DocumentId+":"+Key73(line),Entity73((SketchEntity)line));
            foreach(TextBox text in sketch.TextBoxes) SourceBaseline88.Add(owned.DocumentId+":"+Key73(text),Text73(text,sketch));
        }
    }
    private static JObject CopyFullNative88()
    {
        GuardDocuments73();
        var before=Documents.ToDictionary(d=>d.InternalName,d=>(d.Document.DatabaseRevisionId,d.Document.Dirty));
        var state=Inspect73(); state.Remove("captured_utc");
        state["local_text_styles88"]=new JArray(Documents.Select(owned=>new JObject { ["document_id"]=owned.DocumentId,
            ["styles"]=new JArray(owned.Document.TextStyles.Cast<TextStyle>().Select(style=>new JObject { ["key"]=Key73(style),["name"]=style.Name,
                ["location"]=(int)style.StyleLocation,["font"]=style.Font,["size"]=style.FontSize,["bold"]=style.Bold,["italic"]=style.Italic,
                ["underline"]=style.Underline,["color"]=Color88(style.Color) })) }));
        GuardDocuments73();
        if(Documents.Any(d=>d.Document.DatabaseRevisionId!=before[d.InternalName].DatabaseRevisionId||d.Document.Dirty!=before[d.InternalName].Dirty))
            throw new HostFailure73("Full copy checkpoint read changed native Dirty or DatabaseRevisionId.");
        return state;
    }
    private static void Equal88(JToken? a,JToken? b,string field){if(!ExactNative94(a,b))throw new HostFailure73("Native copy differs: "+field);}
    private static void Shift88(JArray a,JArray b,int stride,double[] delta)
    {
        if(a.Count==0||a.Count!=b.Count||a.Count%stride!=0||delta.Length!=2) throw new HostFailure73("Native translation array shape differs.");
        if(delta.Any(value=>!double.IsFinite(value))) throw new HostFailure73("Native translation delta is not finite.");
        for(int i=0;i<a.Count;i++)
        {
            double source=(double)a[i],copy=(double)b[i];
            if(!double.IsFinite(source)||!double.IsFinite(copy)
                || Math.Abs(copy-source-(i%stride<2?delta[i%stride]:0))>1e-5)
                throw new HostFailure73("Native translated coordinate differs or is not finite.");
        }
    }
}
