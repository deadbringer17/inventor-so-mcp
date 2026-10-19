using System;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace InventorXrSo.Core.Backend
{
    public static class NativeSketchMidpointRules
    {
        public static bool IsPoint(SketchEntityRef reference)=>reference!=null && IsPoint(reference.Type,reference.Subpart);
        public static bool IsPoint(string type,SketchSubpart part)=>part==SketchSubpart.Start || part==SketchSubpart.End || part==SketchSubpart.Center
             || type=="point" && (part==SketchSubpart.Point || part==SketchSubpart.Entity);
        public static string[] Roles(JObject options)
        {
            if(options.Properties().Any(p=>p.Name!="points_mm" && p.Name!="references" && p.Name!="reference_roles")) throw new ArgumentException("Opzione Linea punto medio sconosciuta.");
            if(!(options["points_mm"] is JArray points) || points.Count!=2 || points.Any(p=>!(p is JArray a) || a.Count!=2))
                throw new ArgumentException("Indica punto medio ed estremo in mm.");
            foreach(JArray point in points) foreach(var value in point)
                if((value.Type!=JTokenType.Integer && value.Type!=JTokenType.Float) || double.IsNaN((double)value) || double.IsInfinity((double)value)) throw new ArgumentException("Coordinate finite richieste.");
            if(options["references"]==null && options["reference_roles"]==null) return Array.Empty<string>();
            if(!(options["reference_roles"] is JArray roles) || roles.Count<1 || roles.Count>2
                || roles.Any(r=>r.Type!=JTokenType.String || ((string)r!="midpoint" && (string)r!="end"))
                || roles.Select(r=>(string)r).Distinct(StringComparer.Ordinal).Count()!=roles.Count
                || !(options["references"] is JArray references) || references.Count!=roles.Count)
                throw new ArgumentException("Assegna un riferimento distinto a punto medio e/o estremo.");
            return roles.Select(r=>(string)r).ToArray();
        }
    }
}
