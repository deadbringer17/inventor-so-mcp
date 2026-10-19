#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace InventorXrSo.Core.Backend
{

/// <summary>Snapshot-local structural proof; slots never identify selectable or persistent CAD objects.</summary>
public static class SketchMutationCompleteness
{
    public static bool Validate(JObject proof,JArray entities,JArray dimensions,JArray constraints,bool preview,bool partial)
    {
        void Fields(JObject value,params string[] names)
        { if(value==null || value.Count!=names.Length || value.Properties().Any(p=>!names.Contains(p.Name,StringComparer.Ordinal))) throw Invalid(); }
        int Integer(JToken value,int max)
        { if(value?.Type!=JTokenType.Integer || !int.TryParse(value.ToString(),out int count) || count<0 || count>max) throw Invalid();return count; }
        Fields(proof,"schema","state","omitted_count","read_error_count","collections","image_boundaries");
        if(Integer(proof["schema"],1)!=1 || proof["state"]?.Type!=JTokenType.String) throw Invalid();
        string state=(string)proof["state"];
        if(state!="complete" && state!="read_only_image_backgrounds" && state!="incomplete") throw Invalid();
        int omitted=Integer(proof["omitted_count"],1000000),errors=Integer(proof["read_error_count"],1000000),missing=0;
        var collections=proof["collections"] as JObject;Fields(collections,"entities","dimensions","constraints");
        foreach(var collection in new[]{("entities",entities),("dimensions",dimensions),("constraints",constraints)})
        {
            if(collection.Item2==null || collection.Item2.Count>2000 || collection.Item2.Any(x=>!(x is JObject))) throw Invalid();
            var counts=collections[collection.Item1] as JObject;Fields(counts,"native_count","represented_count");
            int native=Integer(counts["native_count"],1000000),represented=Integer(counts["represented_count"],2000);
            if(represented!=collection.Item2.Count || native<represented) throw Invalid();missing+=native-represented;
        }
        if(missing!=omitted || !(proof["image_boundaries"] is JArray boundaries) || boundaries.Count>2000) throw Invalid();
        var all=entities.Concat(dimensions).Concat(constraints).ToArray();
        if(preview && all.Any(x=>x["id"]!=null || x["structural_owner_image_id"]!=null || x["structural_subparts"]!=null || x["related_entities"]!=null)) throw Invalid();
        if(state=="incomplete") return false;
        if(omitted!=0 || errors!=0 || partial!=(state=="read_only_image_backgrounds")) throw Invalid();
        if(all.Any(x=>x["supported"]?.Type!=JTokenType.Boolean || x["selection_state"]!=null && (x["selection_state"].Type!=JTokenType.String
            || !new[]{"editable","image_boundary","read_only_image","read_only_spline_handle"}.Contains((string)x["selection_state"],StringComparer.Ordinal))
            || preview && x["id"]!=null)) throw Invalid();
        if(!preview)
        {
            var ids=all.Select(x=>x["id"]).ToArray();
            if(ids.Any(x=>x?.Type!=JTokenType.String || !((string)x).StartsWith("ent_",StringComparison.Ordinal) || ((string)x).Length<=4 || ((string)x).Length>131072)
                || ids.Select(x=>(string)x).Distinct(StringComparer.Ordinal).Count()!=ids.Length) throw Invalid();
        }
        bool Point(JToken value)=>value is JArray p && p.Count==2 && p.All(Finite);
        foreach(var item in entities)
        {
            if((bool?)item["supported"]!=true) continue;
            string type=(string)item["type"];
            if(type=="point") { if(!Point(item["point_mm"])) throw Invalid(); }
            else if(type=="text") { if(item["text"]?.Type!=JTokenType.String || !Point(item["text_mm"])) throw Invalid();
                if(item["text_bounds_mm"]!=null) SketchTextGlyphMeshContract.ValidateBounds(item["text_bounds_mm"] as JArray);
                if(item["text_glyphs"]!=null) SketchTextGlyphMeshContract.Validate(item["text_glyphs"] as JObject); }
            else if(new[]{"line","circle","arc","ellipse","elliptical_arc","spline","control_point_spline","offset_spline","spline_handle"}.Contains(type,StringComparer.Ordinal))
            {
                if(!(item["strokes_mm"] is JArray strokes) || strokes.Count<4 || strokes.Count>16384 || strokes.Count%2!=0 || !strokes.All(Finite)) throw Invalid();
            }
            else throw Invalid();
        }
        if(dimensions.Any(x=>(string)x["type"]!="dimension" || !Point(x["text_mm"]))
            || constraints.Any(x=>(string)x["type"]!="constraint" || x["native_type"]?.Type!=JTokenType.String || string.IsNullOrWhiteSpace((string)x["native_type"]))) throw Invalid();
        ValidateSplineHandles(entities);
        var imageSlots=new HashSet<int>();var structuralSlots=new HashSet<int>();
        foreach(var token in boundaries)
        {
            var map=token as JObject;Fields(map,"image_slot","edge_slots","point_slots");
            int imageSlot=Integer(map["image_slot"],entities.Count-1);
            if(!imageSlots.Add(imageSlot)) throw Invalid();
            var image=entities[imageSlot];var current=image["image_current"] as JObject;
            if((string)image["type"]!="image" || (bool)image["supported"] || (string)image["selection_state"]!="read_only_image"
                || image["linked_to_file"]?.Type!=JTokenType.Boolean || (bool)image["linked_to_file"] || current==null) throw Invalid();
            ValidateCurrent(current);
            foreach(var entry in new[]{("edge_slots","line"),("point_slots","point")})
            {
                if(!(map[entry.Item1] is JArray slots) || slots.Count!=4) throw Invalid();
                foreach(var slot in slots)
                {
                    int index=Integer(slot,entities.Count-1);var item=entities[index];
                    if(!structuralSlots.Add(index) || (string)item["type"]!=entry.Item2 || (bool?)item["supported"]!=true
                        || (string)item["selection_state"]!="image_boundary") throw Invalid();
                    if(!preview && ((string)item["structural_owner_image_id"]!=(string)image["id"] || image["id"]?.Type!=JTokenType.String)) throw Invalid();
                }
            }
        }
        for(int slot=0;slot<entities.Count;slot++)
        {
            var item=entities[slot];
            if((string)item["type"]=="image" && !imageSlots.Contains(slot)
                || (string)item["selection_state"]=="image_boundary" && !structuralSlots.Contains(slot)
                || (bool?)item["supported"]!=true && !imageSlots.Contains(slot)) throw Invalid();
        }
        if(dimensions.Concat(constraints).Any(x=>(bool?)x["supported"]!=true)
            || state!=(imageSlots.Count==0 ? "complete" : "read_only_image_backgrounds")) throw Invalid();
        if(!preview)
        {
            var imageIds=new HashSet<string>(imageSlots.Select(slot=>(string)entities[slot]["id"]),StringComparer.Ordinal);
            foreach(var item in all)
                if(item["structural_subparts"]!=null)
                {
                    if(!(item["structural_subparts"] is JArray anchors) || anchors.Count<1 || anchors.Count>4) throw Invalid();
                    var parts=new HashSet<string>(StringComparer.Ordinal);
                    foreach(var anchor in anchors)
                    {
                        if(!(anchor is JObject binding) || binding.Count!=2 || binding["subpart"]?.Type!=JTokenType.String
                            || binding["owner_image_id"]?.Type!=JTokenType.String || !imageIds.Contains((string)binding["owner_image_id"])) throw Invalid();
                        string part=(string)binding["subpart"];
                        string field=part=="Start" ? "start_mm" : part=="End" ? "end_mm" : part=="Center" ? "center_mm" : part=="Point" ? "point_mm" : "";
                        if(field.Length==0 || !parts.Add(part) || !Point(item[field])) throw Invalid();
                        var map=(JObject)boundaries.Single(m=>(string)entities[(int)m["image_slot"]]["id"]==(string)binding["owner_image_id"]);
                        var position=(JArray)item[field];
                        if(!((JArray)map["point_slots"]).Any(slot=>
                        {
                            var corner=entities[(int)slot]["point_mm"] as JArray;
                            return Point(corner) && Math.Abs((double)corner[0]-(double)position[0])<=1e-5 && Math.Abs((double)corner[1]-(double)position[1])<=1e-5;
                        })) throw Invalid();
                    }
                }
        }
        return true;
    }
    /// <summary>Ownership uses snapshot-local slots, never temporary persistent CAD identities.</summary>
    public static void ValidateSplineHandles(JArray entities)
    {
        var protectedPoints=new HashSet<int>();var fitPoints=new HashSet<int>();
        bool Point(JToken p)=>p is JArray a && a.Count==2 && a.All(Finite);
        bool SamePoint(JToken a,JToken b)=>Point(a)&&Point(b)
            && Math.Abs((double)a[0]-(double)b[0])<=1e-5 && Math.Abs((double)a[1]-(double)b[1])<=1e-5;
        foreach(var item in entities)
        {
            if((string)item["native_type"]=="kSketchSplineHandleObject" && (string)item["type"]!="spline_handle") throw Invalid();
            if((string)item["type"]!="spline_handle") { if(item["spline_handle"]!=null) throw Invalid();continue; }
            var map=item["spline_handle"] as JObject;
            var fields=new[]{"schema","owner_slot","fit_point_slot","start_slot","end_slot","fit_point_role"};
            if((string)item["native_type"]!="kSketchSplineHandleObject" || (bool?)item["supported"]!=true
                || (string)item["selection_state"]!="read_only_spline_handle" || map==null || map.Count!=fields.Length
                || map.Properties().Any(p=>!fields.Contains(p.Name,StringComparer.Ordinal))
                || map["schema"]?.Type!=JTokenType.Integer || map["schema"].ToString()!="1") throw Invalid();
            int Slot(string name)
            { if(map[name]?.Type!=JTokenType.Integer || !int.TryParse(map[name].ToString(),out int slot) || slot<0 || slot>=entities.Count) throw Invalid();return slot; }
            int owner=Slot("owner_slot"),fit=Slot("fit_point_slot"),start=Slot("start_slot"),end=Slot("end_slot");
            fitPoints.Add(fit);
            if(new[]{owner,fit,start,end}.Distinct().Count()!=4 || (string)entities[owner]["type"]!="spline"
                || (bool?)entities[owner]["supported"]!=true) throw Invalid();
            foreach(int slot in new[]{fit,start,end}) if((string)entities[slot]["type"]!="point" || (bool?)entities[slot]["supported"]!=true) throw Invalid();
            string role=(string)map["fit_point_role"];
            if(map["fit_point_role"]?.Type!=JTokenType.String || !new[]{"Start","End","Interior"}.Contains(role,StringComparer.Ordinal)
                || role!="Interior" && !SamePoint(entities[fit]["point_mm"],entities[owner][role=="Start" ? "start_mm" : "end_mm"])) throw Invalid();
            if(!SamePoint(item["start_mm"],entities[start]["point_mm"]) || !SamePoint(item["end_mm"],entities[end]["point_mm"])) throw Invalid();
            foreach(int slot in new[]{start,end})
                if(!protectedPoints.Add(slot) || (string)entities[slot]["selection_state"]!="read_only_spline_handle") throw Invalid();
        }
        if(fitPoints.Overlaps(protectedPoints)) throw Invalid();
        for(int slot=0;slot<entities.Count;slot++)
            if((string)entities[slot]["selection_state"]=="read_only_spline_handle"
                && (string)entities[slot]["type"]!="spline_handle" && !protectedPoints.Contains(slot)) throw Invalid();
    }
    private static bool Finite(JToken token)
    { if(token==null || token.Type!=JTokenType.Integer && token.Type!=JTokenType.Float) return false;double number=(double)token;return !double.IsNaN(number)&&!double.IsInfinity(number); }

    private static void ValidateCurrent(JObject current)
    {
        var currentFields=new[]{"schema","frame","width_mm","height_mm","source_kind","asset_state","visible","transparent"};
        if(current.Count!=8 || current.Properties().Any(p=>!currentFields.Contains(p.Name,StringComparer.Ordinal))
            || current["schema"]?.Type!=JTokenType.Integer || current["schema"].ToString()!="1"
            || current["source_kind"]?.Type!=JTokenType.String || current["asset_state"]?.Type!=JTokenType.String
            || (string)current["source_kind"]!="native_bitmap" || (string)current["asset_state"]!="deferred"
            || current["transparent"]?.Type!=JTokenType.Boolean || (bool)current["transparent"]
            || current["visible"]?.Type!=JTokenType.Boolean) throw Invalid();
        var frame=current["frame"] as JObject;
        var frameFields=new[]{"schema","provenance","corner_order","corners_mm"};
        if(frame==null || frame.Count!=4 || frame.Properties().Any(p=>!frameFields.Contains(p.Name,StringComparer.Ordinal))
            || frame["schema"]?.Type!=JTokenType.Integer || frame["schema"].ToString()!="1" || frame["provenance"]?.Type!=JTokenType.String
            || (string)frame["provenance"]!="native_boundary_roles" || !(frame["corners_mm"] is JArray points) || points.Count!=4
            || !(frame["corner_order"] is JArray order) || order.Count!=4 || order.Any(x=>x.Type!=JTokenType.String)
            || !order.Values<string>().SequenceEqual(new[]{"top_left","top_right","bottom_right","bottom_left"})) throw Invalid();
        double Number(JToken token)
        { if(token==null || token.Type!=JTokenType.Integer && token.Type!=JTokenType.Float) throw Invalid();double n=(double)token;if(double.IsNaN(n)||double.IsInfinity(n)) throw Invalid();return n; }
        double width=Number(current["width_mm"]),height=Number(current["height_mm"]);
        var p=points.Select(x=>x is JArray a && a.Count==2 ? new[]{Number(a[0]),Number(a[1])} : throw Invalid()).ToArray();
        double ux=p[1][0]-p[0][0],uy=p[1][1]-p[0][1],vx=p[3][0]-p[0][0],vy=p[3][1]-p[0][1];
        bool Near(double a,double b)=>!double.IsNaN(a)&&!double.IsInfinity(a)&&Math.Abs(a-b)<=1e-5;
        if(width<=0 || height<=0 || !(ux*ux+uy*uy>0) || !(vx*vx+vy*vy>0) || !Near(Math.Sqrt(ux*ux+uy*uy),width) || !Near(Math.Sqrt(vx*vx+vy*vy),height)
            || !Near(p[2][0],p[0][0]+ux+vx) || !Near(p[2][1],p[0][1]+uy+vy) || !Near((ux*vx+uy*vy)/width,0)
            || !Near((ux*vx+uy*vy)/height,0)) throw Invalid();
    }
    private static FormatException Invalid()=>new FormatException("Native mutation completeness proof is invalid.");
}

}