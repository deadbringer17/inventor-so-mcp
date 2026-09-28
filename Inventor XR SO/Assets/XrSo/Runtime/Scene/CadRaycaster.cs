using System;
using UnityEngine;

namespace InventorXrSo.Unity.Scene
{
    /// <summary>Nearest CAD body along a ray; colliders that are not CAD bodies are passed through.</summary>
    public static class CadRaycaster
    {
        private static RaycastHit[] Hits = new RaycastHit[32];

        public static bool TryPick(Ray ray, float maxDistance, out CadBody body, out int triangle, out Vector3 point)
        {
            body = null;
            triangle = -1;
            point = default;
            int count = Physics.RaycastNonAlloc(ray, Hits, maxDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            // A full NonAlloc buffer is an unordered subset, not necessarily the closest hits.
            while (count == Hits.Length)
            {
                Array.Resize(ref Hits, Hits.Length * 2);
                count = Physics.RaycastNonAlloc(ray, Hits, maxDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            }
            Array.Sort(Hits, 0, count, HitDistance.Instance);
            for (int i = 0; i < count; i++)
            {
                var candidate = Hits[i].collider.GetComponent<CadBody>();
                if (candidate == null || Hits[i].triangleIndex < 0) continue;
                var section = candidate.Section;
                if (section != null && section.IsClipped(Hits[i].point)) continue;
                body = candidate;
                triangle = Hits[i].triangleIndex;
                point = Hits[i].point;
                return true;
            }
            return false;
        }

        private sealed class HitDistance : System.Collections.Generic.IComparer<RaycastHit>
        {
            public static readonly HitDistance Instance = new HitDistance();
            public int Compare(RaycastHit a, RaycastHit b) => a.distance.CompareTo(b.distance);
        }
    }
}
