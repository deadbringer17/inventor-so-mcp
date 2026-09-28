using UnityEngine;

namespace InventorXrSo.Unity.Scene
{
    public static class AssemblyManipulation
    {
        /// <summary>Extract the controller's twist about the allowed world axis, ignoring swing.</summary>
        public static float TwistDegrees(Quaternion start, Quaternion current, Vector3 axis)
        {
            axis.Normalize();
            var delta = current * Quaternion.Inverse(start);
            float projection = Vector3.Dot(new Vector3(delta.x, delta.y, delta.z), axis);
            if (Mathf.Abs(projection) < 1e-7f && Mathf.Abs(delta.w) < 1e-7f) return 0;
            float angle = 2 * Mathf.Atan2(projection, delta.w) * Mathf.Rad2Deg;
            return Mathf.DeltaAngle(0, angle);
        }
    }
}
