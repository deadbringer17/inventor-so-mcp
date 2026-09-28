using UnityEngine;

namespace InventorXrSo.Unity.Scene
{
    /// <summary>One local visual section for the active scene. No persistent CAD operation.</summary>
    public sealed class SectionPlane : MonoBehaviour
    {
        private static readonly int ClipPlane = Shader.PropertyToID("_XrSectionPlane");
        private static readonly int ClipEnabled = Shader.PropertyToID("_XrSectionEnabled");
        private LineRenderer _outline;
        private Material _lineMaterial;
        private Vector3 _center;
        private float _radius;
        public bool Active { get; private set; }
        public float OffsetMm { get; private set; }
        public float AngleDegrees { get; private set; }
        public Plane WorldPlane => new Plane(transform.forward, transform.position);

        public void Initialize(Transform model, Material lineMaterial)
        {
            transform.SetParent(model, false);
            _lineMaterial = lineMaterial;
            _outline = gameObject.AddComponent<LineRenderer>();
            _outline.sharedMaterial = _lineMaterial;
            _outline.useWorldSpace = false;
            _outline.loop = true;
            _outline.positionCount = 4;
            _outline.enabled = false;
        }
        public void ResetPlane(Bounds bounds)
        {
            SetActive(false);
            _center = bounds.center;
            _radius = Mathf.Max(0.05f, bounds.extents.magnitude);
            _outline.SetPositions(new[] { new Vector3(-_radius,-_radius,0), new Vector3(_radius,-_radius,0),
                new Vector3(_radius,_radius,0), new Vector3(-_radius,_radius,0) });
            SetNumeric(0, 0);
        }
        public void SetActive(bool active)
        {
            Active = active;
            if (_outline != null) _outline.enabled = active;
            Publish();
        }
        public void SetNumeric(float offsetMm, float angleDegrees)
        {
            if (float.IsNaN(offsetMm) || float.IsInfinity(offsetMm) || float.IsNaN(angleDegrees) || float.IsInfinity(angleDegrees)) return;
            OffsetMm = offsetMm;
            AngleDegrees = angleDegrees % 360f;
            transform.localRotation = Quaternion.Euler(0, AngleDegrees, 0);
            transform.localPosition = _center + transform.localRotation * Vector3.forward * (OffsetMm / 1000f);
            Publish();
        }
        public void SetWorldPose(Vector3 position, Quaternion rotation)
        {
            transform.SetPositionAndRotation(position, rotation);
            OffsetMm = Vector3.Dot(transform.localPosition - _center, transform.localRotation * Vector3.forward) * 1000f;
            AngleDegrees = transform.localEulerAngles.y;
            Publish();
        }
        public void SetOffset(float offsetMm)
        {
            if (float.IsNaN(offsetMm) || float.IsInfinity(offsetMm)) return;
            OffsetMm = offsetMm;
            transform.localPosition = _center + transform.localRotation * Vector3.forward * (offsetMm / 1000f);
            Publish();
        }
        public void SetAngle(float angleDegrees)
        {
            if (float.IsNaN(angleDegrees) || float.IsInfinity(angleDegrees)) return;
            var angles = transform.localEulerAngles;
            angles.y = angleDegrees;
            transform.localRotation = Quaternion.Euler(angles);
            AngleDegrees = angleDegrees;
            SetOffset(OffsetMm);
        }
        public bool HitHandle(Ray ray, out float distance)
        {
            distance = 0;
            if (!Active || !WorldPlane.Raycast(ray, out distance)) return false;
            var point = transform.InverseTransformPoint(ray.GetPoint(distance));
            return Mathf.Abs(point.x) <= _radius && Mathf.Abs(point.y) <= _radius;
        }
        public bool IsClipped(Vector3 worldPoint) => Active && WorldPlane.GetDistanceToPoint(worldPoint) > 0.00001f;
        private void LateUpdate() { if (Active) Publish(); }
        private void Publish()
        {
            Shader.SetGlobalFloat(ClipEnabled, Active && isActiveAndEnabled ? 1f : 0f);
            var plane = WorldPlane;
            Shader.SetGlobalVector(ClipPlane, new Vector4(plane.normal.x, plane.normal.y, plane.normal.z, plane.distance));
            if (_outline != null) _outline.widthMultiplier = 0.002f / Mathf.Max(0.0001f, transform.lossyScale.x);
        }
        private void OnEnable() => Publish();
        private void OnDisable() => Shader.SetGlobalFloat(ClipEnabled, 0);
    }
}
