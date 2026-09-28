using UnityEngine;

namespace InventorXrSo.Xr
{
    public enum EnvironmentMode { MixedReality, StudioVr }

    /// <summary>Spec §6: same UI and workflows, only the background changes (passthrough or a neutral studio).</summary>
    public sealed class EnvironmentModeController : MonoBehaviour
    {
        [SerializeField] private Camera eye;
        [SerializeField] private OVRPassthroughLayer passthrough;
        [SerializeField] private Color studioColor = new Color(0.16f, 0.17f, 0.19f, 1f);

        public EnvironmentMode Mode { get; private set; } = EnvironmentMode.MixedReality;

        public void Configure(Camera centerEye, OVRPassthroughLayer layer)
        {
            eye = centerEye;
            passthrough = layer;
        }

        public void Set(EnvironmentMode mode)
        {
            Mode = mode;
            bool mixed = mode == EnvironmentMode.MixedReality;
            if (OVRManager.instance != null) OVRManager.instance.isInsightPassthroughEnabled = mixed;
            if (passthrough != null) passthrough.enabled = mixed;
            eye.clearFlags = CameraClearFlags.SolidColor;
            eye.backgroundColor = mixed ? new Color(0, 0, 0, 0) : studioColor;
        }
    }
}
