using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace InventorXrSo.Core.Backend
{
    /// <summary>Native references/endpoint input only. Inventor evaluates the tangent and constructs the associated arc.</summary>
    public static class NativeSketchTangentArcRules
    {
        public static bool IsSource(SketchEntityRef reference)=>reference!=null
            && (reference.Subpart==SketchSubpart.Start || reference.Subpart==SketchSubpart.End)
            && new[]{"line","arc","elliptical_arc","spline","control_point_spline"}.Contains(reference.Type,StringComparer.Ordinal);

        public static bool Accepts(NativeSketchSnapshot snapshot,IReadOnlyList<SketchEntityRef> references)
        {
            try{Resolve(snapshot,references);return true;}
            catch(ArgumentException){return false;}catch(InvalidOperationException){return false;}
        }
        public static bool AcceptsReferences(NativeSketchSnapshot snapshot,JToken token)
        {
            try{return Accepts(snapshot,ReadReferences(token));}
            catch(ArgumentException){return false;}catch(InvalidOperationException){return false;}catch(FormatException){return false;}
        }
        public static JObject CreateOptions(NativeSketchSnapshot snapshot,IReadOnlyList<SketchEntityRef> references,CadPoint? end=null)
        {
            var anchors=Resolve(snapshot,references);
            if(references.Count==2) end=anchors[1]; // Always freeze actual native target, never the pointer's approximate hit.
            if(!end.HasValue) throw new ArgumentException("Indica l'estremo finale dell'arco tangente.");
            Finite(end.Value);
            if(anchors[0].X==end.Value.X && anchors[0].Y==end.Value.Y) throw new ArgumentException("L'estremo finale deve differire dal punto iniziale.");
            return new JObject{["references"]=new JArray(references.Select(r=>r.ToJson())),["points_mm"]=new JArray{new JArray(end.Value.X,end.Value.Y)}};
        }
        public static void ValidateOptions(NativeSketchSnapshot snapshot,JObject options)
        {
            if(options==null || options.Count!=2 || options.Properties().Any(p=>p.Name!="references" && p.Name!="points_mm")
                || !(options["points_mm"] is JArray points) || points.Count!=1 || !(points[0] is JArray point) || point.Count!=2
                || point.Any(v=>v.Type!=JTokenType.Integer && v.Type!=JTokenType.Float)) throw new ArgumentException("Indica sorgente nativa ed un solo estremo finale XY in mm.");
            var end=new CadPoint((double)point[0],(double)point[1]);Finite(end);
            var references=ReadReferences(options["references"]);var anchors=Resolve(snapshot,references);
            if(anchors[0].X==end.X && anchors[0].Y==end.Y) throw new ArgumentException("Arco tangente di lunghezza nulla.");
            // Backend83 uses 1e-5 mm to compare the native target anchor; no tangent/radius solver is duplicated here.
            if(references.Count==2 && (Math.Abs(anchors[1].X-end.X)>1e-5 || Math.Abs(anchors[1].Y-end.Y)>1e-5))
                throw new ArgumentException("L'estremo finale non coincide con il riferimento nativo selezionato.");
        }
        public static bool OptionsCurrent(NativeSketchSnapshot snapshot,JObject options)
        {
            try{ValidateOptions(snapshot,options);return true;}
            catch(ArgumentException){return false;}catch(InvalidOperationException){return false;}catch(FormatException){return false;}
        }
        public static JObject ReconfirmOptions(NativeSketchSnapshot snapshot,JObject previous,IReadOnlyList<SketchEntityRef> references)
        {
            if(previous==null || !(previous["points_mm"] is JArray points) || points.Count!=1 || !(points[0] is JArray point) || point.Count!=2
                || point.Any(v=>v.Type!=JTokenType.Integer && v.Type!=JTokenType.Float)) throw new ArgumentException("Bozza dell'arco tangente incompleta.");
            return CreateOptions(snapshot,references,new CadPoint((double)point[0],(double)point[1]));
        }
        private static IReadOnlyList<SketchEntityRef> ReadReferences(JToken token)
        {
            if(!(token is JArray array) || array.Count<1 || array.Count>2) throw new ArgumentException("Seleziona un estremo sorgente e, facoltativamente, un punto destinazione.");
            var fields=new[]{"document_id","revision","sketch_id","entity_id","type","subpart"};
            return array.Select(item=>
            {
                if(!(item is JObject reference) || reference.Count!=6 || reference.Properties().Any(p=>!fields.Contains(p.Name,StringComparer.Ordinal) || p.Value.Type!=JTokenType.String)
                    || !Enum.TryParse((string)reference["subpart"],false,out SketchSubpart part) || !Enum.IsDefined(typeof(SketchSubpart),part)
                    || part.ToString()!=(string)reference["subpart"]) throw new ArgumentException("Riferimento nativo tipizzato incompleto.");
                return new SketchEntityRef((string)reference["document_id"],(string)reference["revision"],(string)reference["sketch_id"],(string)reference["entity_id"],(string)reference["type"],part);
            }).ToArray();
        }
        private static CadPoint[] Resolve(NativeSketchSnapshot snapshot,IReadOnlyList<SketchEntityRef> references)
        {
            if(snapshot==null || !snapshot.NativeMutationComplete) throw new InvalidOperationException("Ricarica uno schizzo nativo completo.");
            if(references==null || references.Count<1 || references.Count>2 || !IsSource(references[0])
                || references.Count==2 && !NativeSketchMidpointRules.IsPoint(references[1])) throw new ArgumentException("Seleziona prima Start/End di una curva, poi un eventuale punto finale.");
            if(references.Count==2 && references[0].EntityId==references[1].EntityId && references[0].Subpart==references[1].Subpart)
                throw new ArgumentException("Il riferimento sorgente e destinazione deve essere distinto.");
            return references.Select(reference=>
            {
                if(reference==null || !reference.Matches(snapshot.State,snapshot.SketchId)) throw new InvalidOperationException("Riferimento scaduto o appartenente ad un altro schizzo.");
                var entity=snapshot.Entities.SingleOrDefault(e=>e.Reference?.EntityId==reference.EntityId);
                var part=reference.Type=="point" && reference.Subpart==SketchSubpart.Entity ? SketchSubpart.Point : reference.Subpart;
                if(entity==null || entity.Type!=reference.Type || !entity.CanSelectSubpart(reference.Subpart) || !entity.TryGetSubpartPoint(part,out var point))
                    throw new ArgumentException("La sottoparte selezionata non fornisce un punto nativo modificabile.");
                Finite(point);return point;
            }).ToArray();
        }
        private static void Finite(CadPoint point)
        {if(double.IsNaN(point.X)||double.IsInfinity(point.X)||double.IsNaN(point.Y)||double.IsInfinity(point.Y)||point.Z!=0) throw new ArgumentException("Coordinate planari finite richieste.");}
    }
}
