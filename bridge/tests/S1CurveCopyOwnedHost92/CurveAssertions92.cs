using Newtonsoft.Json.Linq;

internal static partial class Program
{
    private static JObject CaseSketch92(JObject full,CurveFixture92 fixture)=>(JObject)((JArray)((JArray)full["curve_point_graph92"]!)
        .Single(d=>(string?)d["document_id"]==Documents.Single(o=>o.Sheet==fixture.Sheet).DocumentId)["sketches"]!).Single(s=>(string?)s["key"]==fixture.SketchKey);
    private static void ValidatePhase92(JObject before,JObject after,CurveFixture92 fixture,string[] selected,double dx,double dy,JArray returned,JObject phase)
    {
        var a=CaseSketch92(before,fixture);var b=CaseSketch92(after,fixture);
        Require89(!((string)b["constraint_status"]!).Contains("Over",StringComparison.OrdinalIgnoreCase)
            &&!((string)b["constraint_status"]!).Contains("Unknown",StringComparison.OrdinalIgnoreCase),"Curve92 sketch solver status unknown/overconstrained.");
        var oldEntities=((JArray)a["entities"]!).OfType<JObject>().ToDictionary(e=>(string)e["key"]!,StringComparer.Ordinal);
        var nowEntities=((JArray)b["entities"]!).OfType<JObject>().ToDictionary(e=>(string)e["key"]!,StringComparer.Ordinal);
        foreach(var pair in oldEntities) Require89(nowEntities.TryGetValue(pair.Key,out var same)&&JToken.DeepEquals(pair.Value,same),"Original or prior-copy entity changed during native Copy.");
        var oldConstraints=((JArray)a["constraints"]!).OfType<JObject>().ToDictionary(e=>(string)e["key"]!,StringComparer.Ordinal);
        var nowConstraints=((JArray)b["constraints"]!).OfType<JObject>().ToDictionary(e=>(string)e["key"]!,StringComparer.Ordinal);
        foreach(var pair in oldConstraints) Require89(nowConstraints.TryGetValue(pair.Key,out var same)&&JToken.DeepEquals(pair.Value,same),"Existing native constraint graph changed.");
        Require89(JToken.DeepEquals(a["dimension_count"],b["dimension_count"]),"Native Copy introduced/lost dimension constraints.");
        var newEntities=nowEntities.Values.Where(e=>!oldEntities.ContainsKey((string)e["key"]!)).ToArray();
        var newCurves=newEntities.Where(e=>e["kind"]!=null).ToArray();
        Require89(newCurves.Length==selected.Length,"Native Copy did not create exactly the selected curve count.");
        var map=new Dictionary<string,string>(StringComparer.Ordinal);var newPointKeys=new HashSet<string>(StringComparer.Ordinal);var pairs=new JArray();
        foreach(string key in selected)
        {
            var source=oldEntities[key];var candidates=newCurves.Where(c=>(string?)c["kind"]==(string?)source["kind"]).ToArray();
            Require89(candidates.Length==1,"First profile curve types do not give a unique native source/copy pairing.");
            var copy=candidates[0];string copyKey=(string)copy["key"]!;map.Add(key,copyKey);
            Require89((string?)copy["parent_key"]==fixture.SketchKey,"Native copy parent sketch differs.");
            Require89(!((string)copy["constraint_status"]!).Contains("Over",StringComparison.OrdinalIgnoreCase)
                &&!((string)copy["constraint_status"]!).Contains("Unknown",StringComparison.OrdinalIgnoreCase),"Native copy solver status unknown/overconstrained.");
            var sourcePoints=(JObject)source["structural_points"]!;var copyPoints=(JObject)copy["structural_points"]!;
            Require89(sourcePoints.Properties().Select(p=>p.Name).SequenceEqual(copyPoints.Properties().Select(p=>p.Name)),"Native copy structural roles differ.");
            foreach(var property in sourcePoints.Properties())
            {
                string oldPoint=(string)property.Value["key"]!,newPoint=(string)copyPoints[property.Name]!["key"]!;
                Require89(!oldEntities.ContainsKey(newPoint)&&nowEntities.ContainsKey(newPoint),"Native copy reuses an old point or has an unresolved structural point.");
                if(map.TryGetValue(oldPoint,out string? existing)) Require89(existing==newPoint,"Native shared-point mapping inconsistent.");else map.Add(oldPoint,newPoint);
                newPointKeys.Add(newPoint);TranslateArray92((JArray)property.Value["point_mm"]!,(JArray)copyPoints[property.Name]!["point_mm"]!,dx,dy);
            }
            foreach(string field in new[]{"native_type","construction","reference","has_reference_component","style_flags","centerline","domain"})
                if(source[field]!=null) Require89(JToken.DeepEquals(source[field],copy[field]),"Native copy known flags/domain differ: "+field);
            Require89(((JArray)copy["owned_by"]!).All(owner=>!oldEntities.ContainsKey((string)owner!)),"Native copy has an owner in old geometry.");
            foreach(string field in new[]{"radius_mm","start_angle_rad","sweep_angle_rad","geometry_start_angle_rad","geometry_sweep_angle_rad"})
                if(source[field]!=null) Require89(double.IsFinite((double)copy[field]!)&&Math.Abs((double)source[field]!-(double)copy[field]!)<= (field=="radius_mm"?1e-8:1e-10),"Native copy radius/direction/extent differs.");
            foreach(string field in new[]{"strokes_mm","samples_mm"}) TranslateArray92((JArray)source[field]!,(JArray)copy[field]!,dx,dy);
            CheckAnalytic92(copy);
            pairs.Add(new JObject { ["source_key"]=key,["copy_key"]=copyKey,["point_mapping"]=new JObject(sourcePoints.Properties().Select(p=>new JProperty(p.Name,new JObject{["source_key"]=p.Value["key"],["copy_key"]=copyPoints[p.Name]!["key"]}))),
                ["source"]=source,["copy"]=copy,["native_point_roles_not_coordinate_ownership"]=true });
        }
        Require89(newEntities.All(e=>e["kind"]!=null||newPointKeys.Contains((string)e["key"]!)),"New native entity is not explained by copied curves and their explicit structural points.");
        var addedConstraints=nowConstraints.Values.Where(c=>!oldConstraints.ContainsKey((string)c["key"]!)).ToArray();
        var expected=new List<JObject>();
        foreach(var constraint in oldConstraints.Values)
        {
            if((string?)constraint["binding_status"]!="COMPLETE_TYPED_MEMBERS") throw new UnknownCurve92("Source graph has an unsupported public constraint adapter.");
            var members=(JArray)constraint["members"]!;bool touches=members.Any(m=>map.ContainsKey((string)m["key"]!));
            if(!touches) continue;
            if(!members.All(m=>map.ContainsKey((string)m["key"]!))) throw new UnknownCurve92("Selected profile has an external source relation; no independence claim.");
            var mapped=(JObject)constraint.DeepClone();mapped.Remove("key");
            foreach(var member in (JArray)mapped["members"]!) member["key"]=map[(string)member["key"]!];expected.Add(mapped);
        }
        foreach(var constraint in addedConstraints)
        {
            if((string?)constraint["binding_status"]!="COMPLETE_TYPED_MEMBERS") throw new UnknownCurve92("Copied constraint has unknown native members.");
            Require89(((JArray)constraint["members"]!).All(m=>nowEntities.ContainsKey((string)m["key"]!)&&!oldEntities.ContainsKey((string)m["key"]!)),"New constraint crosses into source/unrelated/prior-copy geometry.");
            var candidate=(JObject)constraint.DeepClone();candidate.Remove("key");int found=expected.FindIndex(e=>JToken.DeepEquals(e,candidate));
            Require89(found>=0,"Copied intrinsic graph differs from observed source graph; no hardcoded constraint counts.");expected.RemoveAt(found);
        }
        Require89(expected.Count==0,"Native Copy omitted an observed internal source relation.");
        var newKeys=new HashSet<string>(newEntities.Select(e=>(string)e["key"]!).Concat(addedConstraints.Select(c=>(string)c["key"]!)),StringComparer.Ordinal);
        Require89(returned.All(r=>newKeys.Contains((string)r["key"]!))&&newCurves.All(c=>returned.Any(r=>(string?)r["key"]==(string?)c["key"])),"Returned enumerator/native inventory disagree.");
        Require89(JToken.DeepEquals(Unrelated92(before,fixture),Unrelated92(after,fixture)),"Unrelated native state/styles/parameters changed during Copy.");
        phase["pairs"]=pairs;phase["new_entity_keys"]=new JArray(newEntities.Select(e=>(string)e["key"]!));phase["new_constraints"]=new JArray(addedConstraints);
        phase["existing_and_prior_copies_exact"]=true;phase["intrinsic_graph_from_native_members_exact"]=true;
        phase["known_style_flags_exact"]=true;phase["optional_style_unknowns_are_not_coverage"]=new JArray(newCurves.SelectMany(c=>((JObject)c["style_flags"]!).Properties().Where(p=>p.Value is JObject unknown&&(string?)unknown["status"]=="UNKNOWN").Select(p=>(string)c["key"]!+":"+p.Name)));
    }
    private static JObject Unrelated92(JObject state,CurveFixture92 fixture)
    {
        var copy=(JObject)state.DeepClone();string docId=Documents.Single(d=>d.Sheet==fixture.Sheet).DocumentId;
        foreach(var document in (JArray)copy["documents"]!)
            if((string?)document["document_id"]==docId)
            { document["database_revision_id"]?.Parent?.Remove();document["dirty"]?.Parent?.Remove();var sketches=(JArray)document["sketches"]!;sketches.Single(s=>(string?)s["key"]==fixture.SketchKey).Remove(); }
        foreach(var document in (JArray)copy["curve_point_graph92"]!)
            if((string?)document["document_id"]==docId) ((JArray)document["sketches"]!).Single(s=>(string?)s["key"]==fixture.SketchKey).Remove();
        return copy;
    }
    private static void TranslateArray92(JArray source,JArray copy,double dx,double dy)
    {
        Require89(source.Count>0&&source.Count==copy.Count&&source.Count%2==0,"Curve92 translated array shape differs.");
        for(int i=0;i<source.Count;i++) Require89(double.IsFinite((double)copy[i])&&Math.Abs((double)copy[i]-(double)source[i]-(i%2==0?dx:dy))<=1e-5,"Native curve coordinate is not the requested translation.");
    }
    private static void CheckAnalytic92(JObject curve)
    {
        var points=(JObject)curve["structural_points"]!;var samples=(JArray)curve["samples_mm"]!;
        if((string?)curve["kind"]=="line")
        {
            var a=(JArray)points["start"]!["point_mm"]!;var b=(JArray)points["end"]!["point_mm"]!;
            double dx=(double)b[0]-(double)a[0],dy=(double)b[1]-(double)a[1],length=Math.Sqrt(dx*dx+dy*dy);Require89(length>0,"Native copied line degenerate.");
            for(int i=0;i<samples.Count;i+=2)Require89(Math.Abs(((double)samples[i]-(double)a[0])*dy-((double)samples[i+1]-(double)a[1])*dx)/length<=1e-5,"Native line evaluator leaves analytic support.");
        }
        else
        {
            var center=(JArray)points["center"]!["point_mm"]!;double r=(double)curve["radius_mm"]!;Require89(double.IsFinite(r)&&r>0,"Native radius invalid.");
            for(int i=0;i<samples.Count;i+=2){double x=(double)samples[i]-(double)center[0],y=(double)samples[i+1]-(double)center[1];Require89(Math.Abs(Math.Sqrt(x*x+y*y)-r)<=1e-5,"Native circle/arc evaluator leaves analytic circle.");}
        }
    }
}
