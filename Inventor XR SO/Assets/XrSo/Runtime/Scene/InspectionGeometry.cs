using System;
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
    }
}
