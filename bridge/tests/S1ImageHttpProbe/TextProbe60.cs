using Inventor;
using Bimwright.Ipt.Shared.Contracts;
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Mcp;
using InventorXrSo.Core.Pairing;
using Newtonsoft.Json.Linq;

internal static partial class Program
{
    private static void RunText60(Inventor.Application app,PartDocument doc,PlanarSketch sketch,InventorBackend backend,
        ProbeTransport transport,PairedServer paired,JObject report,JArray cases,bool sheet,bool glyph62=false)
    {
        string kind=sheet ? "sheetmetal" : "part";report["phase"]=kind+"_text_setup";
        var parameter=doc.ComponentDefinition.Parameters.UserParameters.AddByExpression("S1TextLength60","10 mm","mm");
        doc.Update2(false);string documentId=DocumentIdComposer.Compose(doc.InternalName,doc.FullFileName,false);
        string sketchId=Reference56(documentId,sketch,"sketch");
        var mcp=new McpClient(transport,paired.BaseUrl,paired.Token);mcp.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
        DocumentState state;
        var deadline=DateTime.UtcNow.AddSeconds(15);
        while(true)
        {
            try{state=backend.GetDocumentStateAsync(CancellationToken.None).GetAwaiter().GetResult();break;}
            catch(McpToolException error) when(error.Code=="NO_TARGET"&&DateTime.UtcNow<deadline){Thread.Sleep(500);}
        }
        if(state.DocumentId!=documentId) throw new ProbeFailure("Text host targets another document.");
        JObject Content(params JObject[] runs)=>new(){["schema"]=1,["runs"]=new JArray(runs)};
        JObject Operation(string variant,JObject options)=>new(){["command"]="edit_sketch",["arguments"]=new JObject{
            ["sketch_id"]=sketchId,["variant"]=variant,["options"]=options}};
        JObject Reference(TextBox text)=>new(){["document_id"]=documentId,["sketch_id"]=sketchId,["revision"]=state.Revision,
            ["entity_id"]=Reference56(documentId,text,"sketch_text"),["type"]="text",["subpart"]="Entity"};
        var row=new JObject{["kind"]=kind,["render_confirmation"]="Synthetic Core confirmation; glyph rendering and XR are NOT COVERED."};cases.Add(row);
        void Workflow(string name,string variant,JObject options)
        {
            report["phase"]=kind+"_"+name;var before=TextState60(doc,sketch);
            using var workflow=new DesignSession(backend);workflow.SetContext(state,true,true);workflow.SetDraft(new JArray(Operation(variant,options)));
            workflow.PreviewAsync().GetAwaiter().GetResult();
            if(workflow.Status!=DesignStatus.PreviewReady||workflow.Preview==null||workflow.Preview.Sketches.Count!=1
                ||workflow.Preview.Sketches.Any(s=>!s.NativeMutationComplete||s.NativePartial)||workflow.CanApply
                ||!JToken.DeepEquals(before,TextState60(doc,sketch))) throw new ProbeFailure("Native text preview failed or did not restore exactly.");
            if(glyph62&&workflow.Preview.Sketches[0].NativeTextGlyphs.Count<1)throw new ProbeFailure("Native glyph preview is missing.");
            var refreshed=backend.GetDocumentStateAsync(CancellationToken.None).GetAwaiter().GetResult();
            if(!SameState56(state,refreshed)) throw new ProbeFailure("Text preview changed document revision.");
            workflow.ConfirmRendered(workflow.Preview.PlanId);if(!workflow.CanApply) throw new ProbeFailure("Text preview cannot reach explicit Apply.");
            workflow.ApplyAsync().GetAwaiter().GetResult();var after=backend.GetDocumentStateAsync(CancellationToken.None).GetAwaiter().GetResult();
            if(workflow.LastCommit==null||workflow.CommitOutcomeUnknown||workflow.LastCommit.DocumentId!=after.DocumentId
                ||workflow.LastCommit.Revision!=after.Revision||state.Revision==after.Revision) throw new ProbeFailure("Native text commit was not confirmed.");
            state=after;row[name]=new JObject{["preview_exact"]=true,["commit_confirmed"]=true,["after"]=TextState60(doc,sketch)};
        }
        const string literal="A&B <Parameter Name='foreign'>literal</Parameter>";
        Workflow("fitted_create","text_create",new JObject{["position_mm"]=new JArray(10,20),["content"]=Content(new JObject{["text"]=literal})});
        if(sketch.TextBoxes.Count!=1||sketch.TextBoxes[1].Text!=literal||!sketch.TextBoxes[1].Fitted) throw new ProbeFailure("Escaped fitted text differs from the literal input.");
        var text=sketch.TextBoxes[1];string key=Identity(text);
        Workflow("parameter_update","text_update",new JObject{["references"]=new JArray(Reference(text)),["position_mm"]=new JArray(35,45),
            ["content"]=Content(new JObject{["text"]="L="},new JObject{["parameter_name"]="S1TextLength60"})});
        if(Identity(text)!=key||Math.Abs(text.Origin.X*10-35)>1e-5||Math.Abs(text.Origin.Y*10-45)>1e-5) throw new ProbeFailure("Text update changed identity or origin.");
        row["external_rebuild_or_edit_context_used"]=false;
        var updates=new JArray();row["source_updates"]=updates;
        foreach(int mm in new[]{22,7})
        {
            parameter=doc.ComponentDefinition.Parameters.UserParameters["S1TextLength60"];
            parameter.Expression=mm+" mm";if(!doc.Update2(false)) throw new ProbeFailure("Source parameter update failed.");
            var authoritativeText=sketch.TextBoxes.Cast<TextBox>().Single(t=>Identity(t)==key);
            state=backend.GetDocumentStateAsync(CancellationToken.None).GetAwaiter().GetResult();
            var current=backend.GetNativeSketchAsync(state,sketchId,CancellationToken.None).GetAwaiter().GetResult();
            var represented=current.Entities.Single(t=>t.Reference?.EntityId==Reference56(documentId,authoritativeText,"sketch_text")).Data;
            if(glyph62)
            {
                var snapshotGlyphs=current.Entities.Single(t=>t.Reference?.EntityId==Reference56(documentId,authoritativeText,"sketch_text")).TextGlyphs;
                int nativeVertices=0,nativeFaces=0;double[] nativeCoordinates=Array.Empty<double>();int[] nativeIndices=Array.Empty<int>();
                authoritativeText.GetFacetsInfo(out nativeVertices,out nativeFaces,out nativeCoordinates,out nativeIndices);
                if(snapshotGlyphs==null||snapshotGlyphs.VerticesMm.Count!=nativeVertices||snapshotGlyphs.Indices.Count!=nativeIndices.Length)throw new ProbeFailure("HTTP glyph mesh count differs.");
                for(int i=0;i<nativeVertices;i++)if((snapshotGlyphs.VerticesMm[i]-new CadPoint(nativeCoordinates[i*3]*10,nativeCoordinates[i*3+1]*10,nativeCoordinates[i*3+2]*10)).Length>1e-5)throw new ProbeFailure("HTTP glyph model coordinates differ.");
                if(!snapshotGlyphs.Indices.SequenceEqual(nativeIndices.Select(i=>i-1)))throw new ProbeFailure("HTTP glyph triangle topology differs.");
                row["http_glyph_model_geometry_exact"]=true;
            }
            updates.Add(new JObject{["parameter_mm"]=mm,["native_parameter_mm"]=Convert.ToDouble(parameter.Value)*10,
                ["text"]=authoritativeText.Text,["cached_proxy_text"]=text.Text,["formatted_text"]=authoritativeText.FormattedText,["same_key"]=Identity(authoritativeText)==key});
            var observation=(JObject)updates.Last!;observation["snapshot_text"]=represented["text"]?.DeepClone();
            observation["snapshot_formatted_text"]=represented["formatted_text"]?.DeepClone();
            if(Math.Abs(Convert.ToDouble(parameter.Value)*10-mm)>1e-5||Identity(authoritativeText)!=key
                ||!authoritativeText.Text.Contains(mm.ToString(System.Globalization.CultureInfo.InvariantCulture))
                ||(string?)represented["text"]!=authoritativeText.Text) throw new ProbeFailure("Native structured text did not follow its local parameter.");
            // Only after asserting the production snapshot and live object are current,
            // create an independent native glyph oracle. It cannot repair the tested path.
            var glyphOracle=sketch.TextBoxes.AddFitted(app.TransientGeometry.CreatePoint2d(10,20),
                "L=<Parameter Name='S1TextLength60' Precision='0'>S1TextLength60</Parameter>",Type.Missing);
            var glyphs=GlyphComparison60(authoritativeText,glyphOracle);observation["glyph_comparison"]=glyphs;
            if((bool?)glyphs["centered_points_equal"]!=true||!JToken.DeepEquals(glyphs["actual_vertices"],glyphs["expected_vertices"])
                ||!JToken.DeepEquals(glyphs["actual_facets"],glyphs["expected_facets"])) throw new ProbeFailure("Modified text glyph geometry differs from direct native creation.");
            glyphOracle.Delete();
            text=authoritativeText;state=backend.GetDocumentStateAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
        if(glyph62)
        {
            var boundsCases=new JArray();row["native_rotated_text_picks"]=boundsCases;
            foreach(int angle in new[]{0,90,180,270})
            {
                text.Rotation=angle*Math.PI/180;if(!doc.Rebuild2(false))throw new ProbeFailure("Text bounds rebuild failed.");
                state=backend.GetDocumentStateAsync(CancellationToken.None).GetAwaiter().GetResult();
                string revision=doc.DatabaseRevisionId;bool dirty=doc.Dirty;
                var boundsSnapshot=backend.GetNativeSketchAsync(state,sketchId,CancellationToken.None).GetAwaiter().GetResult();
                var entity=boundsSnapshot.Entities.Single(t=>t.Reference?.EntityId==Reference56(documentId,text,"sketch_text"));
                if(entity.TextBoundsMm.Count!=4)throw new ProbeFailure("Native text bounds missing.");
                var box=text.RangeBox;var origin=text.Origin;double c=Math.Cos(text.Rotation),s=Math.Sin(text.Rotation);
                double[] xs={box.MinPoint.X,box.MaxPoint.X,box.MaxPoint.X,box.MinPoint.X},ys={box.MinPoint.Y,box.MinPoint.Y,box.MaxPoint.Y,box.MaxPoint.Y};
                for(int i=0;i<4;i++)
                {
                    double x=xs[i]-origin.X,y=ys[i]-origin.Y;
                    if((entity.TextBoundsMm[i]-new CadPoint((origin.X+c*x-s*y)*10,(origin.Y+s*x+c*y)*10)).Length>1e-5)throw new ProbeFailure("HTTP native rotated text bounds differ.");
                }
                var center=(entity.TextBoundsMm[0]+entity.TextBoundsMm[2])*.5;
                var hits=boundsSnapshot.PickTargets(center,.001,"text",SketchSubpart.Entity);
                if(!hits.Any(h=>h.Reference.EntityId==entity.Reference.EntityId)||entity.DistanceTo(center)>1e-5
                    ||doc.DatabaseRevisionId!=revision||doc.Dirty!=dirty)throw new ProbeFailure("Native text bounds read/pick failed or changed CAD.");
                boundsCases.Add(new JObject{["rotation_deg"]=angle,["corners_exact"]=true,["center_pick_native_identity"]=true,["read_document_unchanged"]=true});
            }
            text.Rotation=0;if(!doc.Rebuild2(false))throw new ProbeFailure("Text rotation restore failed.");
            state=backend.GetDocumentStateAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
        var snapshot=backend.GetNativeSketchAsync(state,sketchId,CancellationToken.None).GetAwaiter().GetResult();
        if(snapshot.SupportsVariant("text_create")||snapshot.SupportsVariant("text_update")||snapshot.SupportsVariant("text_delete")) throw new ProbeFailure("Text foundation was disclosed before full XR integration.");
        row["text_capabilities_undisclosed"]=true;row["snapshot_mutation_complete"]=snapshot.NativeMutationComplete;
        var refusals=new JArray();row["refusals"]=refusals;
        void Refuse(string name,JObject options,string expected="INVALID_ARGUMENT")
        {
            var before=TextState60(doc,sketch);bool refused=false;
            try{mcp.CallToolAsync("inventor_atomic_batch",new JObject{["document_id"]=documentId,["expected_revision"]=state.Revision,
                ["operations"]=new JArray(Operation("text_update",options))},CancellationToken.None).GetAwaiter().GetResult();}
            catch(McpToolException error)
            {
                refused=error.Code=="ROLLED_BACK"&&(string?)error.Details?["details"]?["step_code"]==expected;
                refusals.Add(new JObject{["name"]=name,["code"]=error.Code,["step_code"]=error.Details?["details"]?["step_code"]?.DeepClone()});
            }
            if(!refused||!JToken.DeepEquals(before,TextState60(doc,sketch))||!SameState56(state,backend.GetDocumentStateAsync(CancellationToken.None).GetAwaiter().GetResult()))
                throw new ProbeFailure("Text refusal was missing or changed native state.");
        }
        foreach(string name in new[]{"document_id","sketch_id","revision","type","subpart"})
        {
            var reference=Reference(text);reference[name]="wrong";
            Refuse("foreign_"+name,new JObject{["references"]=new JArray(reference),["position_mm"]=new JArray(40,50)},name=="revision" ? "STALE_REVISION" : "INVALID_ARGUMENT");
        }
        Refuse("missing_local_parameter",new JObject{["references"]=new JArray(Reference(text)),["content"]=Content(new JObject{["parameter_name"]="MissingLocal60"})});
        Refuse("raw_markup_option",new JObject{["references"]=new JArray(Reference(text)),["content"]=Content(new JObject{["formatted_text"]="<Property Document='foreign'/>"})});
        var ownedStyle=(TextStyle)text.Style.Copy("S1TextStyle60");ownedStyle.FontSize=.4;ownedStyle.Bold=true;ownedStyle.Italic=true;
        state=backend.GetDocumentStateAsync(CancellationToken.None).GetAwaiter().GetResult();
        Workflow("rectangle_create","text_create",new JObject{["position_mm"]=new JArray(10,70),["opposite_corner_mm"]=new JArray(80,50),["style_name"]=ownedStyle.Name,
            ["content"]=Content(new JObject{["text"]="First\nSecond"})});
        if(sketch.TextBoxes.Count!=2||sketch.TextBoxes[2].Fitted||!sketch.TextBoxes[2].Text.Contains("Second")
            ||sketch.TextBoxes[2].Style.Name!=ownedStyle.Name||!sketch.TextBoxes[2].Style.Bold||!sketch.TextBoxes[2].Style.Italic)
            throw new ProbeFailure("Rectangle text or selected native style differs.");
        Workflow("fitted_delete","text_delete",new JObject{["references"]=new JArray(Reference(text))});
        if(sketch.TextBoxes.Count!=1||sketch.TextBoxes[1].Fitted) throw new ProbeFailure("Text deletion removed the wrong native object.");
        row["outcome"]="PASS";
    }
    private static JObject GlyphComparison60(TextBox actual,TextBox expected)
    {
        int actualVertices=0,actualFacets=0,expectedVertices=0,expectedFacets=0;
        double[] actualCoordinates=Array.Empty<double>(),expectedCoordinates=Array.Empty<double>();
        int[] actualIndices=Array.Empty<int>(),expectedIndices=Array.Empty<int>();
        var row=new JObject{["actual_plain_before"]=actual.Text,["expected_plain_before"]=expected.Text};
        actual.GetFacetsInfo(out actualVertices,out actualFacets,out actualCoordinates,out actualIndices);
        expected.GetFacetsInfo(out expectedVertices,out expectedFacets,out expectedCoordinates,out expectedIndices);
        string[] Centered(double[] coordinates)
        {
            if(coordinates.Length==0||coordinates.Length%3!=0||coordinates.Any(v=>!double.IsFinite(v))) throw new ProbeFailure("Native glyph coordinates are unavailable.");
            var center=Enumerable.Range(0,3).Select(axis=>Enumerable.Range(0,coordinates.Length/3).Average(i=>coordinates[i*3+axis])).ToArray();
            return Enumerable.Range(0,coordinates.Length/3).Select(i=>string.Join(",",Enumerable.Range(0,3).Select(axis=>
                ((coordinates[i*3+axis]-center[axis])*10).ToString("F5",System.Globalization.CultureInfo.InvariantCulture)))).OrderBy(x=>x,StringComparer.Ordinal).ToArray();
        }
        row["actual_vertices"]=actualVertices;row["expected_vertices"]=expectedVertices;
        row["actual_facets"]=actualFacets;row["expected_facets"]=expectedFacets;
        row["centered_points_equal"]=Centered(actualCoordinates).SequenceEqual(Centered(expectedCoordinates));
        row["actual_plain_after"]=actual.Text;row["expected_plain_after"]=expected.Text;
        return row;
    }
    private static JObject TextState60(PartDocument doc,PlanarSketch sketch)=>new()
    {
        ["internal_name"]=doc.InternalName,["database_revision"]=doc.DatabaseRevisionId,["dirty"]=doc.Dirty,["path"]=doc.FullFileName,
        ["entities"]=sketch.SketchEntities.Count,["constraints"]=sketch.GeometricConstraints.Count,["dimensions"]=sketch.DimensionConstraints.Count,
        ["ole"]=doc.ReferencedOLEFileDescriptors.Count,["images"]=sketch.SketchImages.Count,
        ["text"]=new JArray(sketch.TextBoxes.Cast<TextBox>().Select(t=>new JObject{["key"]=Identity(t),["text"]=t.Text,["formatted"]=t.FormattedText,
            ["origin_cm"]=new JArray(t.Origin.X,t.Origin.Y),["width_cm"]=t.Width,["height_cm"]=t.Height,["rotation_rad"]=t.Rotation,["fitted"]=t.Fitted,["style"]=t.Style.Name})),
        ["parameters"]=new JArray(doc.ComponentDefinition.Parameters.Cast<Parameter>().Select(p=>new JObject{["name"]=p.Name,["expression"]=p.Expression})),
        ["styles"]=new JArray(doc.TextStyles.Cast<TextStyle>().Select(s=>new JObject{["name"]=s.Name,["location"]=s.StyleLocation.ToString(),["font"]=s.Font,["size_cm"]=s.FontSize,["bold"]=s.Bold,["italic"]=s.Italic}))
    };
}
