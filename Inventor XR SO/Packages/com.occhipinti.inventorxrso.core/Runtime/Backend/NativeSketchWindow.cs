using System;
using System.Collections.Generic;
using System.Linq;

namespace InventorXrSo.Core.Backend
{
    public enum NativeSketchWindowMode { Contained, Crossing }
    public enum NativeSketchSelectionChange { Replace, Add, Toggle }

    /// <summary>Selezione di vista sui dati dello snapshot: nessuna mutazione CAD. Le curve generiche usano i tratti nativi.</summary>
    public static class NativeSketchWindow
    {
        public static IReadOnlyList<NativeSketchHit> Query(NativeSketchSnapshot snapshot, CadPoint first, CadPoint second,
            NativeSketchWindowMode mode, string type = null, SketchSubpart? part = SketchSubpart.Entity)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (!Enum.IsDefined(typeof(NativeSketchWindowMode), mode)) throw new ArgumentOutOfRangeException(nameof(mode));
            if (part.HasValue && !Enum.IsDefined(typeof(SketchSubpart), part.Value)) throw new ArgumentOutOfRangeException(nameof(part));
            double left = Math.Min(first.X, second.X), right = Math.Max(first.X, second.X);
            double bottom = Math.Min(first.Y, second.Y), top = Math.Max(first.Y, second.Y);
            if (left == right || bottom == top) throw new ArgumentException("La finestra deve avere larghezza e altezza positive.");
            bool Inside(CadPoint p) => p.X >= left && p.X <= right && p.Y >= bottom && p.Y <= top;
            bool Segment(CadPoint a, CadPoint b)
            {
                double low = 0, high = 1;
                bool Slab(double origin, double delta, double min, double max)
                {
                    if (delta == 0) return origin >= min && origin <= max;
                    double one = (min-origin)/delta, two = (max-origin)/delta;
                    low = Math.Max(low, Math.Min(one,two));high = Math.Min(high, Math.Max(one,two));
                    return low <= high;
                }
                return Slab(a.X,b.X-a.X,left,right) && Slab(a.Y,b.Y-a.Y,bottom,top);
            }
            bool Geometry(NativeSketchObject entity)
            {
                if(entity.TextBoundsMm.Count==4)
                {
                    var corners=entity.TextBoundsMm;
                    if(mode==NativeSketchWindowMode.Contained) return corners.All(Inside);
                    if(corners.Any(Inside)) return true;
                    for(int i=0;i<4;i++) if(Segment(corners[i],corners[(i+1)%4])) return true;
                    // A crossing window may be entirely inside the text, including whitespace.
                    return entity.DistanceTo(new CadPoint(left,bottom))<=1e-8;
                }
                // Un cerchio non è il suo disco: una finestra interamente nel foro non lo attraversa.
                var data = entity.Data;
                if (entity.Type == "circle" && entity.TryGetSubpartPoint(SketchSubpart.Center,out var center)
                    && data["radius_mm"] != null)
                {
                    double radius = (double)data["radius_mm"];
                    if (!(radius > 0) || double.IsInfinity(radius)) throw new FormatException("Raggio nativo non valido.");
                    if (mode == NativeSketchWindowMode.Contained)
                        return center.X-radius >= left && center.X+radius <= right && center.Y-radius >= bottom && center.Y+radius <= top;
                    double dx = center.X-Math.Max(left,Math.Min(right,center.X)), dy = center.Y-Math.Max(bottom,Math.Min(top,center.Y));
                    double farX = Math.Max(Math.Abs(center.X-left),Math.Abs(center.X-right));
                    double farY = Math.Max(Math.Abs(center.Y-bottom),Math.Abs(center.Y-top));
                    return dx*dx+dy*dy <= radius*radius && farX*farX+farY*farY >= radius*radius;
                }
                if (entity.Strokes.Count == 0) return false;
                if (mode == NativeSketchWindowMode.Contained) return entity.Strokes.All(Inside);
                if (entity.Strokes.Any(Inside)) return true;
                for (int i=1;i<entity.Strokes.Count;i++) if (Segment(entity.Strokes[i-1],entity.Strokes[i])) return true;
                return false;
            }
            var hits = new List<NativeSketchHit>();
            foreach (var entity in snapshot.Entities.Where(e=>e.Selectable && e.Reference != null && (type == null || type == e.Type)))
            {
                if ((!part.HasValue || part == SketchSubpart.Entity) && Geometry(entity)) hits.Add(new NativeSketchHit(entity,SketchSubpart.Entity,0));
                foreach (var anchor in new[] {SketchSubpart.Start,SketchSubpart.End,SketchSubpart.Center,SketchSubpart.Point})
                    if ((!part.HasValue || part == anchor) && entity.CanSelectSubpart(anchor) && entity.TryGetSubpartPoint(anchor,out var point) && Inside(point))
                        hits.Add(new NativeSketchHit(entity,anchor,0));
            }
            return hits.OrderBy(h=>h.Reference.EntityId,StringComparer.Ordinal).ThenBy(h=>h.Reference.Subpart).ToArray();
        }
    }
}
