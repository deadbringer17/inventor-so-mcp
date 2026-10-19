using System;
using System.Collections.Generic;
using System.Linq;

namespace InventorXrSo.Core.Backend
{
    public static class NativeSketchConstraintRules
    {
        public static string Label(string nativeType)=>nativeType switch
        {
            "kCoincidentConstraintObject"=>"Coincidente", "kParallelConstraintObject"=>"Parallelo",
            "kCollinearConstraintObject"=>"Collineare", "kPerpendicularConstraintObject"=>"Perpendicolare",
            "kConcentricConstraintObject"=>"Concentrico", "kTangentSketchConstraintObject"=>"Tangente",
            "kSmoothConstraintObject"=>"Levigato G2", "kEqualLengthConstraintObject"=>"Uguale lunghezza",
            "kEqualRadiusConstraintObject"=>"Uguale raggio", "kGroundConstraintObject"=>"Fissaggio",
            "kHorizontalConstraintObject"=>"Orizzontale", "kHorizontalAlignConstraintObject"=>"Allineamento orizzontale",
            "kVerticalConstraintObject"=>"Verticale", "kVerticalAlignConstraintObject"=>"Allineamento verticale",
            "kSymmetryConstraintObject"=>"Simmetrico", "kMidpointConstraintObject"=>"Punto medio", "kPatternConstraintObject"=>"Serie", _=>"Vincolo nativo"
        };
        public static bool Accepts(string kind,IReadOnlyList<SketchEntityRef> refs)
        {
            if(refs==null || refs.Count==0 || refs.Any(r=>r==null)
                || refs.Select(r=>(r.EntityId,r.Type=="point" && r.Subpart==SketchSubpart.Point ? SketchSubpart.Entity : r.Subpart)).Distinct().Count()!=refs.Count) return false;
            string Type(SketchEntityRef r)=>r.Subpart==SketchSubpart.Entity ? r.Type : "point";
            var types=refs.Select(Type).ToArray();
            bool Directional(string t)=>t=="line" || t=="ellipse" || t=="elliptical_arc";
            bool Round(string t)=>t=="circle" || t=="arc" || t=="ellipse" || t=="elliptical_arc";
            bool Curve(string t)=>Directional(t) || Round(t) || t=="spline" || t=="control_point_spline";
            switch(kind)
            {
                case "ground": return refs.Count==1 && (Curve(types[0]) || types[0]=="point");
                case "horizontal": case "vertical": return (refs.Count==1 && Directional(types[0])) || (refs.Count==2 && types.All(t=>t=="point"));
                case "coincident": return refs.Count==2 && types.Contains("point") && types.All(t=>t=="point" || Curve(t));
                case "parallel": case "collinear": case "perpendicular": return refs.Count==2 && types.All(Directional);
                case "concentric": return refs.Count==2 && types.All(Round);
                case "equal": return refs.Count==2 && (types.All(t=>t=="line") || types.All(t=>t=="circle" || t=="arc"));
                case "tangent": return refs.Count==2 && types.All(Curve) && !types.All(t=>t=="line");
                case "smooth": return refs.Count==2 && types.All(Curve) && types.Any(t=>t=="spline" || t=="control_point_spline");
                case "symmetry": return refs.Count==3 && types[2]=="line" && types[0]==types[1]
                    && (types[0]=="point" || Curve(types[0]));
                default: return false;
            }
        }
    }
}
