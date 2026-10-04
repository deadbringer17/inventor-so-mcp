using System;
using System.Collections.Generic;
using UnityEngine;

namespace InventorXrSo.Unity.Scene
{
    public enum ModelScaleMode { OneToOne, FitToRoom, Table }

    public static class InspectionGeometry
    {
        public static float Scale(Bounds bounds, ModelScaleMode mode, float availableMetres = 2f)
        {
            if (mode == ModelScaleMode.OneToOne) return 1f;
            if (float.IsNaN(availableMetres) || float.IsInfinity(availableMetres) || availableMetres <= 0)
                throw new ArgumentOutOfRangeException(nameof(availableMetres));
            var extent = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            return extent < 1e-6f ? 1f : Mathf.Min(1f, (mode == ModelScaleMode.Table ? 0.6f : availableMetres) / extent);
        }

        public static float DistanceMm(Transform model, Vector3 worldA, Vector3 worldB) =>
            Vector3.Distance(model.InverseTransformPoint(worldA), model.InverseTransformPoint(worldB)) * 1000f;

        public static void ApplyScale(Transform model, Bounds bounds, ModelScaleMode mode, float availableMetres)
        {
            var center = model.TransformPoint(bounds.center);
            model.localScale = Vector3.one * Scale(bounds, mode, availableMetres);
            model.position += center - model.TransformPoint(bounds.center);
        }

        /// <summary>Bounds of the given instances in the unscaled local space of <paramref name="root"/>; null when there is no body.</summary>
        public static Bounds? InstancesBounds(Transform root, IEnumerable<CadInstance> instances)
        {
            Bounds? result = null;
            foreach (var instance in instances)
            {
                if (instance == null) continue;
                foreach (var body in instance.Bodies)
                {
                    if (body == null || body.Mesh == null) continue;
                    var b = body.Mesh.bounds;
                    for (int corner = 0; corner < 8; corner++)
                    {
                        var local = b.center + Vector3.Scale(b.extents, new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1));
                        var point = root.InverseTransformPoint(body.transform.TransformPoint(local));
                        if (result == null) result = new Bounds(point, Vector3.zero);
                        else { var grown = result.Value; grown.Encapsulate(point); result = grown; }
                    }
                }
            }
            return result;
        }
    }
}
