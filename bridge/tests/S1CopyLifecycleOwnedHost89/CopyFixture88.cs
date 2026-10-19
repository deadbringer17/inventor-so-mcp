using Inventor;
using Bimwright.Ipt.Shared.Contracts;
using Newtonsoft.Json.Linq;
using File = System.IO.File;
using Path = System.IO.Path;

internal static partial class Program
{
    private static readonly Dictionary<string, JObject> SourceBaseline88 = new(StringComparer.Ordinal);
    private static int CopyCheckpoint88;
    private static void CreateCopyFixture88(PartDocument doc)
    {
        string previousPhase=Phase;
        Phase="copy_fixture88_create_sketch";
        GuardDocuments73(); var g=Application!.TransientGeometry;
        var sketch=doc.ComponentDefinition.Sketches.Add(doc.ComponentDefinition.WorkPlanes[3]); sketch.Name="S1_Copy88";
        sketch.SketchLines.AddByTwoPoints(g.CreatePoint2d(1,1),g.CreatePoint2d(3.3,1.8));
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
    private static void CheckpointCopy88()
    {
        GuardDocuments73(); string requestPath=Path.Combine(Settings.OutputDirectory,"copy-checkpoint-request88.json");
        if((File.GetAttributes(requestPath)&FileAttributes.ReparsePoint)!=0||new FileInfo(requestPath).Length>4096) throw new HostFailure73("Invalid checkpoint request file.");
        var request=JObject.Parse(File.ReadAllText(requestPath)); string nonce=(string?)request["nonce"]??"";
        if(!Guid.TryParseExact(nonce,"N",out _)||(string?)request["run_id"]!=Settings.RunId) throw new HostFailure73("Invalid checkpoint binding.");
        var state=CopyFullNative88();
        var result=new JObject { ["run_id"]=Settings.RunId,["nonce"]=nonce,["native_state_sha256"]=HashText73(state.ToString(Newtonsoft.Json.Formatting.None)),["native_state"]=state };
        WritePublic73("copy-native-checkpoint-"+(++CopyCheckpoint88).ToString("D3")+".json",result);
        WritePublic73("copy-checkpoint-ack88.json",new JObject { ["run_id"]=Settings.RunId,["nonce"]=nonce,["native_state_sha256"]=result["native_state_sha256"] });
    }
    private static void VerifyCopies88()
    {
        GuardDocuments73(); string path=Path.Combine(Settings.OutputDirectory,"copy-client88.json");
        if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0||new FileInfo(path).Length>16*1024*1024) throw new HostFailure73("Invalid client report file.");
        var client=JObject.Parse(File.ReadAllText(path));
        if((string?)client["run_id"]!=Settings.RunId||(string?)client["target_id"]!=TargetId||(string?)client["status"]!="PASS_PC_REPRESENTED_SUBSET")
            throw new HostFailure73("Client identity or terminal result differs.");
        var checks=new JArray();
        foreach(JObject workflow in (JArray)client["cases"]!)
        foreach(JObject pair in (JArray)workflow["pairs"]!)
        {
            string docId=(string)workflow["document_id"]!; var owned=Documents.Single(d=>d.DocumentId==docId);
            var sketch=owned.Document.ComponentDefinition.Sketches.Cast<PlanarSketch>().Single(s=>s.Name=="S1_Copy88");
            string Key(JObject reference,string expectedType)
            {
                var decoded=PersistentEntityReference.Decode((string)reference["entity_id"]!);
                if(decoded.DocumentId!=docId||decoded.EntityType!=expectedType||decoded.Context.Length!=0) throw new HostFailure73("Copy pair reference ownership differs.");
                return Convert.ToBase64String(decoded.Key);
            }
            bool text=(string?)pair["type"]=="text";
            string sourceKey=Key((JObject)pair["source"]!,text?"sketch_text":"sketch_entity");
            string copyKey=Key((JObject)pair["copy"]!,text?"sketch_text":"sketch_entity");
            if(sourceKey==copyKey) throw new HostFailure73("Copy aliases source native key.");
            var delta=((JArray)workflow["delta_mm"]!).Values<double>().ToArray();
            JObject source,copy;
            if(text)
            {
                source=Text73(sketch.TextBoxes.Cast<TextBox>().Single(t=>Key73(t)==sourceKey),sketch);
                copy=Text73(sketch.TextBoxes.Cast<TextBox>().Single(t=>Key73(t)==copyKey),sketch);
                var a=(JObject)source["copy_details88"]!; var b=(JObject)copy["copy_details88"]!;
                if((string?)a["origin_key"]==(string?)b["origin_key"]) throw new HostFailure73("Copied text shares native origin point.");
                foreach(string field in new[]{"text","formatted_text","fitted","style","rotation_rad","width_scale","color_rgb"}) Equal88(source[field],copy[field],field);
                foreach(string field in new[]{"single_line","horizontal","vertical","line_spacing_type","line_spacing","style_key","style_location","font","font_size_mm","bold","italic","underline","color","style_color","raw_indices_native_one_based"}) Equal88(a[field],b[field],field);
                Shift88((JArray)a["origin_mm"]!,(JArray)b["origin_mm"]!,2,delta);
                Shift88((JArray)a["raw_vertices_model_mm"]!,(JArray)b["raw_vertices_model_mm"]!,3,delta);
            }
            else
            {
                source=Entity73((SketchEntity)sketch.SketchLines.Cast<SketchLine>().Single(l=>Key73(l)==sourceKey));
                copy=Entity73((SketchEntity)sketch.SketchLines.Cast<SketchLine>().Single(l=>Key73(l)==copyKey));
                var sourcePoints=new[]{(string)source["start_key"]!,(string)source["end_key"]!};
                if(sourcePoints.Contains((string)copy["start_key"]!)||sourcePoints.Contains((string)copy["end_key"]!)) throw new HostFailure73("Copied line shares native endpoint.");
                foreach(string field in new[]{"start_mm","end_mm"}) Shift88((JArray)source[field]!,(JArray)copy[field]!,2,delta);
                Equal88(source["construction"],copy["construction"],"construction"); Equal88(source["centerline"],copy["centerline"],"centerline");
            }
            Equal88(SourceBaseline88[docId+":"+sourceKey],source,"original exact native source");
            checks.Add(new JObject { ["document_id"]=docId,["source_key"]=sourceKey,["copy_key"]=copyKey,["type"]=text?"text":"line",
                ["native_translation_and_original_exact"]=true,["independent_native_keys_points_glyphs"]=true });
        }
        if(((JArray)client["cases"]!).Count!=6||checks.Count!=8) throw new HostFailure73("Native copy workflow matrix differs.");
        WritePublic73("copy-native-verifier88.json",new JObject { ["run_id"]=Settings.RunId,["target_id"]=TargetId,["status"]="PASS_NATIVE_READBACK_SUBSET",
            ["checks"]=checks,["quest_proof"]=false,["persistence_proof"]=false,["source_update_proof"]=false });
    }
    private static void Equal88(JToken? a,JToken? b,string field){if(!JToken.DeepEquals(a,b))throw new HostFailure73("Native copy differs: "+field);}
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
