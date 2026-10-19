using System;
using System.Collections.Generic;
using System.Linq;

namespace InventorXrSo.Core.Backend
{
    public static class NativeSketchDimensionRules
    {
        public static IReadOnlyList<string> Variants(IReadOnlyList<SketchEntityRef> refs)
        {
            if(refs==null || refs.Count<1 || refs.Count>3 || refs.Any(r=>r==null)
                || refs.Select(r=>(r.EntityId,r.Type=="point" && r.Subpart==SketchSubpart.Point ? SketchSubpart.Entity : r.Subpart)).Distinct().Count()!=refs.Count)
                return Array.Empty<string>();
            var types=refs.Select(r=>r.Subpart==SketchSubpart.Entity ? r.Type : "point").ToArray();
            var variants=new List<string>();
            bool circles(string t)=>t=="circle" || t=="arc";
            if((refs.Count==1 && types[0]=="line") || (refs.Count==2 && types.All(t=>t=="point")))
                variants.AddRange(new[] {"aligned","horizontal","vertical"});
            if(refs.Count==2 && types.All(t=>t=="line")) variants.Add("angle_two_lines");
            if(refs.Count==3 && types.All(t=>t=="point")) variants.Add("angle_three_points");
            if(refs.Count==1 && circles(types[0])) variants.AddRange(new[] {"radius","diameter"});
            if(refs.Count==1 && types[0]=="arc") variants.Add("arc_length");
            if(refs.Count==1 && (types[0]=="ellipse" || types[0]=="elliptical_arc")) variants.AddRange(new[] {"ellipse_major","ellipse_minor"});
            if(refs.Count==2 && types[0]=="line" && (types[1]=="line" || types[1]=="point")) variants.Add("offset");
            if(refs.Count==2 && types.All(t=>t=="line" || circles(t)) && types.Any(circles)) variants.Add("tangent_distance");
            if(refs.Count==1 && types[0]=="offset_spline") variants.Add("offset_spline");
            return variants.AsReadOnly();
        }
        public static bool Angular(string variant)=>variant=="angle_two_lines" || variant=="angle_three_points";
    }
}
