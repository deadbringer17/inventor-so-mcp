using Inventor;
using Newtonsoft.Json.Linq;

internal static partial class Program
{
    private static JObject UpdateRow90(bool sheet,int pass)=>((JArray)Rows90[sheet]["updates"]!).OfType<JObject>().Single(u=>(int?)u["pass"]==pass);
    private static void EditCoreSource90(bool sheet,int pass)
    {
        RequireIdle89(); var owned=Owned90(sheet); owned.Document.Activate(); RequireIdle89();
        var doc=owned.Document;var binding=Bindings90[sheet];
        var sketch=doc.ComponentDefinition.Sketches.Cast<PlanarSketch>().Single(s=>Key73(s)==binding.Source.SketchKey);
        var line=sketch.SketchLines.Cast<SketchLine>().Single(l=>Key73(l)==binding.Source.LineKey);
        var text=sketch.TextBoxes.Cast<TextBox>().Single(t=>Key73(t)==binding.Source.TextKey);
        // Allowed baseline only BEFORE native mutation. No glyph or semantic reader
        // follows End until the client has requested the separate inspect action.
        var before=CopyFullNative88();
        var row=new JObject { ["pass"]=pass,["state"]="before_owned_edit",["before_native"]=before,
            ["native_fixture_edit"]=true,["product_last_commit"]=false };
        ((JArray)Rows90[sheet]["updates"]!).Add(row); WritePublic73("copy-native-lifecycle90.json",Report90!);
        Transaction? transaction=null; bool ended=false;
        try
        {
            Phase="core90_start_owned_edit";
            transaction=Application!.TransactionManager.StartTransaction((_Document)doc,"S1 Core90 native fixture source edit");
            Require89(!transaction.HasParentTransaction,"Core90 native source edit refuses nested transaction.");
            var g=Application.TransientGeometry;
            double sx=pass==1?8:13,sy=pass==1?14:4,ex=pass==1?40:29,ey=pass==1?22:9;
            double tx=pass==1?41:16,ty=pass==1?37:44,scale=pass==1?1.5:0.75;
            string markup=pass==1?"Source89 pass1 &amp; changed":"Source89 second content";
            byte red=(byte)(pass==1?33:95),green=(byte)(pass==1?117:42),blue=(byte)(pass==1?155:61);
            row["requested"]=new JObject { ["start_mm"]=new JArray(sx,sy),["end_mm"]=new JArray(ex,ey),["origin_mm"]=new JArray(tx,ty),
                ["formatted_text"]=markup,["width_scale"]=scale,["color_rgb"]=new JArray((int)red,(int)green,(int)blue) };
            Phase="core90_move_start";line.StartSketchPoint.MoveTo(g.CreatePoint2d(sx/10,sy/10));
            Phase="core90_move_end";line.EndSketchPoint.MoveTo(g.CreatePoint2d(ex/10,ey/10));
            Phase="core90_set_literal";text.FormattedText=markup;
            Phase="core90_move_literal";text.Origin=g.CreatePoint2d(tx/10,ty/10);
            Phase="core90_set_overrides";text.WidthScale=scale;text.Color=Application.TransientObjects.CreateColor(red,green,blue,1);
            Phase="core90_solve_source";sketch.Solve();
            Phase="core90_update_source";Require89(doc.Update2(false),"Core90 source Update2 returned false.");
            Phase="core90_rebuild_source";Require89(doc.Rebuild2(false),"Core90 source Rebuild2 returned false.");
            Require89(SameCom73(transaction,Application.TransactionManager.CurrentTransaction),"Core90 transaction ownership lost before End.");
            Phase="core90_end_source";transaction.End();ended=true;
            row["state"]="committed_awaiting_core_first_snapshot";
            row["post_end_native_semantic_reads"]=0;
            RequireIdle89(); // Identity/transaction metadata only; no geometry sampling.
        }
        catch
        {
            string failedPhase=Phase;
            if(transaction!=null&&!ended)
            {
                Require89(SameCom73(transaction,Application!.TransactionManager.CurrentTransaction),"Core90 transaction ownership lost before Abort.");
                transaction.Abort(); var aborted=CopyFullNative88();
                row["abort_full_native_exact"]=JToken.DeepEquals(before,aborted);
                row["after_abort_native"]=aborted;
                Require89((bool)row["abort_full_native_exact"]!,"Core90 failed edit rollback differs; stop.");
            }
            Phase=failedPhase;throw;
        }
        finally { WritePublic73("copy-native-lifecycle90.json",Report90!); }
    }
    private static void InspectCoreEdit90(bool sheet,int pass)
    {
        RequireIdle89();var owned=Owned90(sheet);var b=Bindings90[sheet];
        Require89(Application!.ActiveDocument!=null&&SameCom73(Application.ActiveDocument,owned.Document),"Core90 inspect active document differs.");
        var row=UpdateRow90(sheet,pass);Require89((string?)row["state"]=="committed_awaiting_core_first_snapshot","Core90 edit is not awaiting first Core proof.");
        var current=ReadCopyLifecycle89(owned,b.Source);row["first_native_read_after_core"]=current;
        CheckIndependence89(Initial90[sheet],current,b.Source);
        var request=row["requested"]!;var line=((JArray)current["sketch"]!["entities"]!).Single(e=>(string?)e["key"]==b.Source.LineKey);
        var text=((JArray)current["sketch"]!["texts"]!).Single(e=>(string?)e["key"]==b.Source.TextKey);
        NearPoint89(line["start_mm"],(double)request["start_mm"]![0]!, (double)request["start_mm"]![1]!);
        NearPoint89(line["end_mm"],(double)request["end_mm"]![0]!, (double)request["end_mm"]![1]!);
        NearPoint89(text["origin_mm"],(double)request["origin_mm"]![0]!, (double)request["origin_mm"]![1]!);
        Require89((string?)text["formatted_text"]==(string?)request["formatted_text"]&&(string?)text["text"]==(pass==1?"Source89 pass1 & changed":"Source89 second content"),"Core90 native source text readback differs.");
        Require89(Math.Abs((double)text["width_scale"]!-(double)request["width_scale"]!)<=1e-12,"Core90 native source width scale differs.");
        var color=text["copy_details88"]!["color"]!;var rgb=request["color_rgb"]!;
        Require89((int)color["red"]! ==(int)rgb[0]!&&(int)color["green"]! ==(int)rgb[1]!&&(int)color["blue"]! ==(int)rgb[2]!,"Core90 native source text color differs.");
        var vertices=(JArray)text["copy_details88"]!["raw_vertices_model_mm"]!;
        var indices=(JArray)text["copy_details88"]!["raw_indices_native_one_based"]!;
        var priorText=((JArray)LastInspect90[sheet]["sketch"]!["texts"]!).Single(t=>(string?)t["key"]==b.Source.TextKey);
        Require89(vertices.Count>0&&indices.Count>0&&!JToken.DeepEquals(vertices,priorText["copy_details88"]!["raw_vertices_model_mm"]),"Core90 source glyphs empty or unchanged.");
        row["copies_independent_exact"]=true;row["state"]="native_checked_after_core_first_snapshot";
        row["client_first_snapshot_precedes_native_read"]="Protocol ordering: inspect request sent only after client assertions; independent client report required.";
        LastInspect90[sheet]=current;
        Report90!["source_updates"]=(int)Report90!["source_updates"]!+1;
    }
}

