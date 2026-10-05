using System;
using InventorXrSo.Unity.Scene;
using UnityEngine;
using UnityEngine.EventSystems;

namespace InventorXrSo.Xr
{
    /// <summary>Ray from the dominant controller (spec §9): trigger picks the CAD body under it. Only picks; never edits.</summary>
    [DefaultExecutionOrder(100)] // EventSystem processes the current UI ray first, avoiding click-through.
    public sealed class ControllerRay : MonoBehaviour
    {
        [SerializeField] private Transform origin;
        [SerializeField] private LineRenderer line;
        [SerializeField] private float maxDistance = 20f;
        [SerializeField] private OVRInput.Controller controller = OVRInput.Controller.RTouch;

        public OVRInput.Controller Controller { get => controller; set => controller = value; }
        public event Action<CadBody, int> Picked;
        public event Action PickedNothing;
        public event Action<Vector3> PointPicked;
        /// <summary>The CAD body under the ray changed (null: none, ray over the UI, or controller lost). Works in every workspace; the app gates it by session. Raised on change only.</summary>
        public event Action<CadBody> Hovered;
        private CadBody _hovered;
        public Transform Origin => origin;
        public Material LineMaterial => line != null ? line.sharedMaterial : null;
        public bool CanPick { get; set; } = true;
        /// <summary>Current ray direction (the double Trigger detector compares it between the two presses); zero when unset.</summary>
        public Vector3 Direction => origin != null ? origin.forward : Vector3.zero;
        /// <summary>Stable id of a picked target for the double Trigger detector: the occurrence it belongs to, null for no hit.</summary>
        public static string TargetId(CadBody body) => body != null && body.Instance != null ? body.Instance.OccurrenceId : null;

        public void Configure(Transform rayOrigin, LineRenderer rayLine)
        {
            origin = rayOrigin;
            line = rayLine;
        }

        private void SetHovered(CadBody body)
        {
            if (body == _hovered) return;
            _hovered = body;
            Hovered?.Invoke(body);
        }

        private void OnDisable() { if (line != null) line.enabled = false; SetHovered(null); }
        private void OnEnable() { if (line != null) line.enabled = true; }

        private void Update()
        {
            if (!CanPick || origin == null || !OVRInput.IsControllerConnected(controller) ||
                !OVRInput.GetControllerOrientationTracked(controller) ||
                !OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, controller)) return;
            if (OVRInput.Get(OVRInput.Button.PrimaryHandTrigger, controller)) return;
            if (EventSystem.current != null && EventSystem.current.currentInputModule is ControllerUiInputModule ui && ui.CurrentHit.isValid) return;
            if (CadRaycaster.TryPick(new Ray(origin.position, origin.forward), maxDistance, out var body, out var triangle, out var point))
            { Picked?.Invoke(body, triangle); PointPicked?.Invoke(point); }
            else PickedNothing?.Invoke();
        }

        private void LateUpdate()
        {
            if (origin == null || line == null) return;
            bool tracked = OVRInput.IsControllerConnected(controller) && OVRInput.GetControllerOrientationTracked(controller);
            line.enabled = tracked;
            if (!tracked) { SetHovered(null); return; }
            var ray = new Ray(origin.position, origin.forward);
            bool hit = CadRaycaster.TryPick(ray, maxDistance, out var body, out var triangle, out var point);
            if (EventSystem.current != null &&
                EventSystem.current.currentInputModule is ControllerUiInputModule input && input.CurrentHit.isValid)
            {
                hit = true;
                point = input.CurrentHit.worldPosition;
            }
            bool overUi = EventSystem.current != null &&
                EventSystem.current.currentInputModule is ControllerUiInputModule ui && ui.CurrentHit.isValid;
            SetHovered(!overUi ? body : null);
            line.positionCount = 2;
            line.SetPosition(0, ray.origin);
            line.SetPosition(1, hit ? point : ray.origin + ray.direction * maxDistance);
        }
    }
}
