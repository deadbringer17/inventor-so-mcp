using System;
using System.Collections.Generic;
using System.Linq;

namespace InventorXrSo.Core.Backend
{
    public static class NativeSketchStretchRules
    {
        public static bool Accepts(IReadOnlyList<SketchEntityRef> references)=>references!=null && references.Count>0 && references.Count<=2000
            && references.All(r=>r!=null && IsSelection(r.Type,r.Subpart.ToString()))
            && references.Select(r=>(r.EntityId,r.Subpart)).Distinct().Count()==references.Count;
        public static bool IsSelection(string type,string subpart)
        {
            bool curve=NativeSketchOffsetRules.IsCurve(type);
            if(subpart=="Entity") return curve || type=="point";
            if(subpart=="Point") return type=="point";
            if(subpart=="Center") return type=="circle" || type=="arc" || type=="ellipse";
            return (subpart=="Start" || subpart=="End") && (type=="line" || type=="arc" || type=="elliptical_arc"
                || type=="spline" || type=="control_point_spline");
        }
    }
}
