using Inventor;
using Newtonsoft.Json.Linq;

internal static partial class Program
{
    private static JObject UpdateRow95(bool sheet,int pass)=>((JArray)Rows95[sheet]["updates"]!).OfType<JObject>().Single(r=>(int?)r["pass"]==pass);
    private static void EditCoreSource95(bool sheet,int pass)
    {
        RequireIdle95();var owned=Owned95(sheet);owned.Document.Activate();RequireIdle95();var sketch=CopySketch94(owned);
        var before=FullState92();LastFull95[sheet]=before;var row=new JObject { ["pass"]=pass,["state"]="before_owned_edit",["before_native"]=Raw95("before_edit_"+(sheet?"Sheet":"Part")+"_"+pass,before),
            ["native_fixture_edit"]=true,["product_last_commit"]=false,["requested"]=new JObject { ["offset_from_initial_mm"]=pass==1?new JArray(5,7):new JArray(-6,4),
                ["translation_from_previous_mm"]=pass==1?new JArray(5,7):new JArray(-11,-3),["circle_radius_mm"]=pass==1?12:6,["arc_radius_mm"]=9,["ground_added"]=false,["move_to_used"]=false } };
        ((JArray)Rows95[sheet]["updates"]!).Add(row);WritePublic73("copy-native-lifecycle95.json",Report95!);
        Transaction? transaction=null;bool ended=false;
        try
        {
            transaction=Application!.TransactionManager.StartTransaction((_Document)owned.Document,"S1 CurveCopy95 native source edit");
            Require89(!transaction.HasParentTransaction&&SameCom73(transaction,Application.TransactionManager.CurrentTransaction),"Source95 transaction ownership differs.");
            foreach(string label in CurveRoles95)
            {
                string key=CreatorKeys94[sheet][label];var source=sketch.SketchEntities.Cast<SketchEntity>().Single(e=>CurveKey92(e)==key);
                var objects=Application.TransientObjects.CreateObjectCollection();objects.Add(source);
                // A whole arc alone leaves its defining points free to deform
                // during the native move/solve. Use the same whole-object
                // selection semantics as NativeSketchTransforms: the curve
                // plus its actual typed defining points, never coordinate picks.
                if(source is SketchCircle circle)objects.Add(circle.CenterSketchPoint);
                else if(source is SketchArc arc){objects.Add(arc.CenterSketchPoint);objects.Add(arc.StartSketchPoint);objects.Add(arc.EndSketchPoint);}
                else throw new HostFailure73("Source95 creator is not a native circle/arc.");
                Require89(SameCom73(transaction,Application.TransactionManager.CurrentTransaction),"Source95 transaction lost before native mutation.");
                Phase="core95_move_"+label;sketch.MoveSketchObjects(objects,Application.TransientGeometry.CreateVector2d(pass==1?0.5:-1.1,pass==1?0.7:-0.3),false,false);
                if(label=="circle"){Phase="core95_set_circle_radius";((SketchCircle)source).Radius=pass==1?1.2:0.6;}
            }
            // Explicit completion once, before End; no post-failure repair/retry.
            Phase="core95_solve_source";sketch.Solve();Phase="core95_update_source";Require89(owned.Document.Update2(false),"Source95 Update2 returned false.");
            Require89(SameCom73(transaction,Application.TransactionManager.CurrentTransaction),"Source95 transaction lost before End.");
            Phase="core95_end_source";transaction.End();ended=true;row["state"]="committed_awaiting_core_first_snapshot";row["post_end_native_semantic_reads"]=0;
            RequireIdle95();
        }
        catch
        {
            string failedPhase=Phase;
            if(transaction!=null&&!ended)
            {
                Require89(SameCom73(transaction,Application!.TransactionManager.CurrentTransaction),"Source95 transaction lost before Abort.");transaction.Abort();
                var restored=FullState92();row["after_abort_native"]=Raw95("failed_edit_abort",restored);row["abort_full_native_exact"]=ExactNative94(before,restored);
                Require89((bool)row["abort_full_native_exact"]!,"Source95 Abort changed full native state.");
            }
            Phase=failedPhase;throw;
        }
        finally{WritePublic73("copy-native-lifecycle95.json",Report95!);}
    }
    private static void InspectCoreEdit95(bool sheet,int pass)
    {
        RequireIdle95();var owned=Owned95(sheet);Require89(SameCom73(Application!.ActiveDocument,owned.Document),"Source95 inspection active owner differs.");
        var row=UpdateRow95(sheet,pass);Require89((string?)row["state"]=="committed_awaiting_core_first_snapshot","Source95 not awaiting first Core read.");
        var full=FullState92();row["first_native_read_after_core"]=Raw95("inspect_edit_"+(sheet?"Sheet":"Part")+"_"+pass,full);
        var current=DocumentState95(full,owned);CheckSourceEdit95(sheet,current,pass);
        Equal88(Unrelated95(LastFull95[sheet],owned.DocumentId),Unrelated95(full,owned.DocumentId),"source95 unrelated document state");
        Equal88(OwnedRemainder95(LastFull95[sheet],owned.DocumentId,false),OwnedRemainder95(full,owned.DocumentId,false),"source95 other sketches/all native parameters/body");
        row["copies_independent_exact"]=true;row["state"]="native_checked_after_core_first_snapshot";row["client_first_snapshot_precedes_native_read"]="Inspect95 follows successful client ONE first snapshot; independent client evidence required.";
        LastInspect95[sheet]=current;LastFull95[sheet]=full;Report95!["source_updates"]=(int)Report95!["source_updates"]!+1;
    }
    private static JObject Unrelated95(JObject full,string id)
    {
        var clone=(JObject)full.DeepClone();clone.Remove("active_document_id");
        foreach(string collection in new[]{"documents","curve_point_graph92","local_text_styles88"})((JArray)clone[collection]!).Single(d=>(string?)d["document_id"]==id).Remove();return clone;
    }
    private static JObject OwnedRemainder95(JObject full,string id,bool reopen)
    {
        var document=(JObject)((JArray)full["documents"]!).Single(d=>(string?)d["document_id"]==id).DeepClone();
        if(reopen)document=OwnedUntouchedParameterRelocation95.Normalize(document,(string)document["path"]!,UntouchedBindings95.Values.Single(b=>b.DocumentId==id));
        document.Remove("database_revision_id");document.Remove("dirty");((JArray)document["sketches"]!).Single(s=>(string?)s["name"]=="S1_Copy88").Remove();
        if(reopen)foreach(string field in new[]{"path","name","file_sha256","baseline_file_unchanged"})document.Remove(field);return document;
    }
    private static void Near95(JToken? token,double x,double y)
    {
        Require89(token is JArray p&&p.Count==2&&double.IsFinite((double)p[0])&&double.IsFinite((double)p[1])&&Math.Abs((double)p[0]-x)<=1e-5&&Math.Abs((double)p[1]-y)<=1e-5,"Source95 expected native XY differs.");
    }
    private static void CheckSourceEdit95(bool sheet,JObject current,int pass)
    {
        var baseline=Initial95[sheet];var a=(JObject)baseline["curve_graph"]!;var b=(JObject)current["curve_graph"]!;
        var old=Index94(a["entities"]!);var now=Index94(b["entities"]!);Require89(old.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(now.Keys),"Source95 entity identity set changed.");
        foreach(string field in new[]{"constraints","dimension_count","sketch_entities_keys","sketch_point_keys","constraint_status"})Equal88(a[field],b[field],"source95 graph invariant");
        var allowed=new HashSet<string>(StringComparer.Ordinal);double ox=pass==1?5:-6,oy=pass==1?7:4;
        foreach(string label in CurveRoles95)
        {
            string key=CreatorKeys94[sheet][label];allowed.Add(key);var source=old[key];var actual=now[key];var sp=(JObject)source["structural_points"]!;var cp=(JObject)actual["structural_points"]!;
            double radius=label=="circle"?(pass==1?12:6):9,cx=(double)sp["center"]!["point_mm"]![0]!+ox,cy=(double)sp["center"]!["point_mm"]![1]!+oy;
            Require89(Math.Abs((double)actual["radius_mm"]!-radius)<=1e-8,"Source95 radius differs.");
            foreach(var point in sp.Properties())
            {
                string pk=(string)point.Value["key"]!;allowed.Add(pk);Require89((string?)cp[point.Name]!["key"]==pk,"Source95 structural identity changed.");
                double x=(double)point.Value["point_mm"]![0]!+ox,y=(double)point.Value["point_mm"]![1]!+oy;Near95(cp[point.Name]!["point_mm"],x,y);Near95(now[pk]["point_mm"],x,y);
                var oldPoint=(JObject)old[pk].DeepClone();var newPoint=(JObject)now[pk].DeepClone();oldPoint.Remove("point_mm");newPoint.Remove("point_mm");Equal88(oldPoint,newPoint,"Source95 point properties");
            }
            var expect=(JObject)source.DeepClone();var observed=(JObject)actual.DeepClone();
            foreach(string field in new[]{"start_angle_rad","sweep_angle_rad","geometry_start_angle_rad","geometry_sweep_angle_rad"})if(source[field]!=null)
                Require89(double.IsFinite((double)actual[field]!)&&Math.Abs((double)source[field]!-(double)actual[field]!)<=1e-10,"Source95 arc extent changed.");
            for(int index=0;index<2;index++)Require89(double.IsFinite((double)actual["domain"]![index]!)&&Math.Abs((double)source["domain"]![index]!-(double)actual["domain"]![index]!)<=1e-10,"Source95 domain changed.");
            foreach(string field in new[]{"samples_mm","strokes_mm"})
            {
                var values=(JArray)actual[field]!;Require89(values.Count>=4&&values.Count%2==0,"Source95 curve samples malformed.");
                for(int index=0;index<values.Count;index+=2){double x=(double)values[index],y=(double)values[index+1];Require89(double.IsFinite(x)&&double.IsFinite(y)&&Math.Abs(Math.Sqrt((x-cx)*(x-cx)+(y-cy)*(y-cy))-radius)<=1e-5,"Source95 samples leave expected circle.");}
                if(label!="circle")Shift88((JArray)source[field]!,values,2,new[]{ox,oy});
                else if(field=="samples_mm")
                {
                    var original=(JArray)source[field]!;Require89(original.Count==values.Count,"Source95 circle evaluator sample count changed.");
                    for(int index=0;index<values.Count;index+=2)Near95(new JArray(values[index],values[index+1]),cx+((double)original[index]-(double)sp["center"]!["point_mm"]![0]!)*radius/9,cy+((double)original[index+1]-(double)sp["center"]!["point_mm"]![1]!)*radius/9);
                }
            }
            foreach(string field in new[]{"structural_points","radius_mm","domain","samples_mm","strokes_mm","start_angle_rad","sweep_angle_rad","geometry_start_angle_rad","geometry_sweep_angle_rad"}){expect.Remove(field);observed.Remove(field);}Equal88(expect,observed,"Source95 curve immutable properties");
        }
        foreach(var entity in old)if(!allowed.Contains(entity.Key))Equal88(entity.Value,now[entity.Key],"Source95 copy/unrelated native entity");
        // The older inspector duplicates only native point/arc coordinates. Each
        // changed coordinate is checked against the independent typed graph.
        var expectedSketch=(JObject)baseline["sketch"]!.DeepClone();var actualSketch=(JObject)current["sketch"]!.DeepClone();
        var oldRows=Index94(expectedSketch["entities"]!);var newRows=Index94(actualSketch["entities"]!);Require89(oldRows.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(newRows.Keys),"Source95 base entity set changed.");
        foreach(var pair in oldRows)
        {
            var actual=newRows[pair.Key];if(allowed.Contains(pair.Key))
            {
                foreach(string field in new[]{"point_mm","start_mm","end_mm","center_mm","radius_mm"})if(pair.Value[field]!=null)
                {
                    var graph=now[pair.Key];if(field=="radius_mm")Require89(Math.Abs((double)actual[field]!-(double)graph[field]!)<=1e-8,"Source95 duplicate radius differs.");
                    else {JToken xy=field=="point_mm"?graph[field]!:graph["structural_points"]![field=="start_mm"?"start":field=="end_mm"?"end":"center"]!["point_mm"]!;Near95(actual[field],(double)xy[0]!, (double)xy[1]!);}pair.Value.Remove(field);actual.Remove(field);
                }
            }
            Equal88(pair.Value,actual,"Source95 base native immutable entity");
        }
        Equal88(expectedSketch,actualSketch,"Source95 lines/texts/glyphs/constraints unchanged");Equal88(baseline["styles"],current["styles"],"Source95 styles unchanged");Equal88(baseline["user_parameters"],current["user_parameters"],"Source95 parameters unchanged");
        // Reassigning an already parented JArray clones it in Json.NET. Append
        // to the array actually attached to the report, including pass two.
        if(Rows95[sheet]["source_anchor_observations"]==null)
            Rows95[sheet]["source_anchor_observations"]=new JArray();
        var anchors=(JArray)Rows95[sheet]["source_anchor_observations"]!;
        foreach(var curve in Bindings95[sheet].SourceCurves.Properties())foreach(var constraint in (JArray)curve.Value["constraint_anchors"]!)
        {
            string firstKey=(string)constraint["members"]![0]!["key"]!;var member=now[firstKey];JToken actualAnchor=member["kind"] switch {
                null=>member["point_mm"]!, JValue v when (string?)v=="circle"=>member["structural_points"]!["center"]!["point_mm"]!,
                JValue v when (string?)v=="arc"=>member["structural_points"]!["start"]!["point_mm"]!, _=>throw new HostFailure73("Source95 actual anchor type unsupported.") };
            double x=(double)constraint["anchor_mm"]![0]!+ox,y=(double)constraint["anchor_mm"]![1]!+oy;Near95(actualAnchor,x,y);
            anchors.Add(new JObject { ["pass"]=pass,["constraint_key"]=constraint["constraint_key"]!.DeepClone(),["expected_anchor_mm"]=new JArray(x,y),["actual_anchor_mm"]=actualAnchor.DeepClone(),["ordered_members"]=constraint["members"]!.DeepClone() });
        }
    }
}
