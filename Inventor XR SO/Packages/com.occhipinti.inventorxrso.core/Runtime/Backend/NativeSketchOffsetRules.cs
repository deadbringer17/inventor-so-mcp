using System;
using System.Collections.Generic;
using System.Linq;

namespace InventorXrSo.Core.Backend
{
    public static class NativeSketchOffsetRules
    {
        public static bool Accepts(IReadOnlyList<SketchEntityRef> references,bool includeConnected)=>references!=null
            && references.Count>0 && references.Count<=2000 && (!includeConnected || references.Count==1)
            && references.All(r=>r!=null && r.Subpart==SketchSubpart.Entity && IsCurve(r.Type))
            && references.Select(r=>r.EntityId).Distinct(StringComparer.Ordinal).Count()==references.Count;
        public static bool IsCurve(string type)=>type=="line" || type=="circle" || type=="arc" || type=="ellipse"
            || type=="elliptical_arc" || type=="spline" || type=="control_point_spline" || type=="offset_spline";
    }
}
