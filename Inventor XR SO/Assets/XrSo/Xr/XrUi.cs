using UnityEngine;

namespace InventorXrSo.Xr
{
    public static class XrUi
    {
        /// <summary>Let the controller ray (OVRInputModule) press this world-space canvas.</summary>
        public static void MakeInteractive(Canvas canvas, Camera eye)
        {
            canvas.worldCamera = eye;
            if (canvas.GetComponent<OVRRaycaster>() == null) canvas.gameObject.AddComponent<OVRRaycaster>();
        }
    }
}
