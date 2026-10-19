using System.Collections.Generic;
using System.Linq;

namespace InventorXrSo.Core.Backend
{
    public static class NativeSketchPatternRules
    {
        public static bool Accepts(string variant,IReadOnlyList<SketchEntityRef> references,bool twoDirections=false)
        {
            bool circular=variant=="pattern_circular";
            int axes=circular || !twoDirections ? 1 : 2;
            if((!circular && variant!="pattern_rectangular") || references==null || references.Count<=axes || references.Count>2000
                || references.Any(r=>r==null || r.Subpart!=SketchSubpart.Entity || (!NativeSketchOffsetRules.IsCurve(r.Type) && r.Type!="point"))) return false;
            var sources=references.Take(references.Count-axes).ToArray();
            if(sources.Select(r=>r.EntityId).Distinct().Count()!=sources.Length) return false;
            return circular ? references[references.Count-1].Type=="point" || references[references.Count-1].Type=="arc" || references[references.Count-1].Type=="circle"
                : references.Skip(references.Count-axes).All(r=>r.Type=="line");
        }
    }
}
