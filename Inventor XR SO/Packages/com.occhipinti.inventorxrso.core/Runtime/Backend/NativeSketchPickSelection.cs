using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace InventorXrSo.Core.Backend
{
    /// <summary>Freezes the two ordered curve picks needed to distinguish corner solutions.
    /// This input helper does not authorize or enable any native command.</summary>
    public static class NativeSketchPickSelection
    {
        public static JObject FreezeTwoCurveOptions(NativeSketchSnapshot snapshot,
            IReadOnlyList<NativeSketchHit> picks,JObject options)
        {
            if(snapshot==null || !snapshot.NativeMutationComplete) throw new InvalidOperationException("A structurally complete current native sketch is required.");
            if(options==null) throw new ArgumentNullException(nameof(options));
            if(picks==null || picks.Count!=2) throw new ArgumentException("Select two ordered curve picks.",nameof(picks));
            foreach(var pick in picks)
                if(pick==null || !pick.Reference.Matches(snapshot.State,snapshot.SketchId)
                    || !snapshot.Entities.Any(entity=>ReferenceEquals(entity,pick.Entity)) || !pick.Entity.CanSelectSubpart(pick.Reference.Subpart)
                    || pick.Reference.Subpart!=SketchSubpart.Entity || (pick.Entity.Type!="line" && pick.Entity.Type!="arc")
                    || !pick.PickPointMm.HasValue || pick.PickPointMm.Value.Z!=0)
                    throw new InvalidOperationException("Pick each line or arc directly in the current sketch; window selections do not identify a corner solution.");
            if(picks[0].Reference.EntityId==picks[1].Reference.EntityId)
                throw new InvalidOperationException("Select two distinct curves.");
            var frozen=(JObject)options.DeepClone();
            frozen["references"]=new JArray(picks.Select(pick=>pick.Reference.ToJson()));
            frozen["pick_points_mm"]=new JArray(picks.Select(pick=>new JArray(pick.PickPointMm.Value.X,pick.PickPointMm.Value.Y)));
            return frozen;
        }
    }
}
