using UnityEngine;

namespace InventorXrSo.Unity.Scene
{
    /// <summary>Where the model appears (spec §7.1): 1:1, in front of the user, slightly below eye height.</summary>
    public static class ScenePlacement
    {
        public const float MinDistance = 1.0f;
        public const float BelowEyes = 0.2f;

        public static Bounds LocalBounds(Transform root)
        {
            var bodies = root.GetComponentsInChildren<CadBody>(true);
            if (bodies.Length == 0) return new Bounds(Vector3.zero, Vector3.zero);
            var bounds = new Bounds(root.InverseTransformPoint(bodies[0].transform.TransformPoint(bodies[0].Mesh.bounds.center)), Vector3.zero);
            foreach (var body in bodies)
            {
                var b = body.Mesh.bounds;
                for (int corner = 0; corner < 8; corner++)
                    bounds.Encapsulate(root.InverseTransformPoint(body.transform.TransformPoint(b.center + Vector3.Scale(b.extents,
                        new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1)))));
            }
            return bounds;
        }

        public static Pose InFront(Bounds localBounds, Vector3 headPosition, Vector3 headForward)
        {
            var forward = Vector3.ProjectOnPlane(headForward, Vector3.up);
            if (forward.sqrMagnitude < 1e-6f) forward = Vector3.forward;
            forward.Normalize();
            float distance = Mathf.Max(MinDistance, localBounds.extents.magnitude * 1.5f);
            var rotation = Quaternion.LookRotation(forward, Vector3.up);
            var centre = headPosition + forward * distance;
            centre.y = headPosition.y - BelowEyes;
            return new Pose(centre - rotation * localBounds.center, rotation);
        }
    }
}
