using System;
using System.Collections.Generic;
using InventorXrSo.Core.Backend;
using UnityEngine;

namespace InventorXrSo.Unity.Scene
{
    /// <summary>
    /// Spatial handle for the flange height, drawn on the first selected edge along the flange direction.
    /// Pure view + arithmetic: it never talks to the backend. The knob keeps a constant size in metres
    /// (<see cref="KnobMetres"/>) whatever the CAD size or the view scale, so it stays visible and easy to
    /// pick at 1:1 and at reduced scale, while the height is always measured in CAD millimetres through the
    /// model transform (the same physical gesture gives the same parameter at any scale).
    /// </summary>
    public sealed class FlangeManipulator : MonoBehaviour
    {
        public const float KnobMetres = 0.035f;
        public const float PickRadiusMetres = 0.045f;
        public const double MinHeightMm = 0.1;
        private const double PlaneToleranceMm = 0.05, ParallelTolerance = 0.02;

        private Transform _model;
        private Material _idle, _active, _anchor;
        private GameObject _knob, _anchorMark;
        private LineRenderer _stem;
        private CadPoint _dragStart;
        private double _dragHeight;

        public bool Visible { get; private set; }
        public bool Dragging { get; private set; }
        public CadPoint Origin { get; private set; }
        public CadPoint Axis { get; private set; }
        public double HeightMm { get; private set; }
        /// <summary>False when the direction is a fallback (no face pair matched the sheet thickness).</summary>
        public bool AxisFromFaces { get; private set; }
        public Vector3 KnobWorldPosition => _model == null ? Vector3.zero : _model.TransformPoint(CadCoordinates.ToLocal(Origin + Axis * HeightMm));
        public Vector3 OriginWorldPosition => _model == null ? Vector3.zero : _model.TransformPoint(CadCoordinates.ToLocal(Origin));
        /// <summary>Current knob diameter in world metres (constant by construction).</summary>
        public float KnobWorldSize => _knob == null ? 0 : _knob.transform.lossyScale.x;

        public void Initialize(Transform model, Material stemMaterial)
        {
            if (_model != null) throw new InvalidOperationException("Flange manipulator is already initialized.");
            _model = model ?? throw new ArgumentNullException(nameof(model));
            _idle = Overlay("Flange handle", new Color(0.15f, 0.9f, 0.95f, 1f));
            _active = Overlay("Flange handle active", new Color(1f, 0.85f, 0.1f, 1f));
            _anchor = Overlay("Flange anchor", new Color(1f, 1f, 1f, 1f));
            _stem = CadCoordinates.Line(_model, "Flange handle stem", Array.Empty<CadPoint>(), stemMaterial, 0.004f);
            _stem.gameObject.name = "Flange handle stem";
            _knob = Sphere("Flange handle knob", _idle);
            _anchorMark = Sphere("Flange handle anchor", _anchor);
            Hide();
        }

        private static Material Overlay(string name, Color color)
        {
            var shader = Shader.Find("XrSo/HighlightOverlay");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            var material = new Material(shader) { name = name };
            material.SetColor("_Color", color);
            return material;
        }

        private GameObject Sphere(string name, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            // A collider would only be noise for CadRaycaster: the handle is picked by arithmetic, never by physics.
            var collider = go.GetComponent<Collider>();
            if (collider != null) { if (Application.isPlaying) Destroy(collider); else DestroyImmediate(collider); }
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            go.transform.SetParent(_model, false);
            go.layer = 2; // Ignore Raycast
            return go;
        }

        /// <summary>Place the handle. A running drag keeps its own height; only frame changes are applied.</summary>
        public void Show(CadPoint origin, CadPoint axis, double heightMm, bool axisFromFaces)
        {
            Origin = origin; Axis = axis; AxisFromFaces = axisFromFaces;
            if (!Dragging) HeightMm = heightMm;
            Visible = true;
            Apply();
        }

        public void Hide()
        {
            Visible = false; Dragging = false;
            if (_knob != null) _knob.SetActive(false);
            if (_anchorMark != null) _anchorMark.SetActive(false);
            if (_stem != null) _stem.positionCount = 0;
        }

        /// <summary>Set the height without a drag (numeric field changed): the handle follows the shared draft.</summary>
        public void SetHeight(double heightMm)
        {
            if (Dragging) return;
            HeightMm = heightMm;
            if (Visible) Apply();
        }

        /// <summary>Grip+Trigger on the knob: the ray must pass within <see cref="PickRadiusMetres"/> of it.</summary>
        public bool TryBeginDrag(Ray worldRay, CadPoint controllerModelPoint)
        {
            if (!Visible || Dragging || _model == null) return false;
            var direction = worldRay.direction.normalized;
            var toKnob = KnobWorldPosition - worldRay.origin;
            if (Vector3.Dot(toKnob, direction) <= 0) return false;
            if (Vector3.Cross(direction, toKnob).magnitude > PickRadiusMetres) return false;
            Dragging = true; _dragStart = controllerModelPoint; _dragHeight = HeightMm;
            _knob.GetComponent<MeshRenderer>().sharedMaterial = _active;
            return true;
        }

        /// <summary>Height for the controller position (model millimetres), 0.1 mm steps, clamped to the tool range.</summary>
        public double Drag(CadPoint controllerModelPoint)
        {
            if (!Dragging) return HeightMm;
            HeightMm = Clamp(_dragHeight + (controllerModelPoint - _dragStart).Dot(Axis));
            Apply();
            return HeightMm;
        }

        public void EndDrag()
        {
            if (!Dragging) return;
            Dragging = false;
            if (_knob != null) _knob.GetComponent<MeshRenderer>().sharedMaterial = _idle;
        }

        public static double Clamp(double mm)
        {
            if (double.IsNaN(mm)) return MinHeightMm;
            return Math.Min(SheetMetalOperations.MaxFlangeHeightMm, Math.Max(MinHeightMm, Math.Round(mm, 1)));
        }

        private void LateUpdate() { if (Visible) Apply(); }

        private void Apply()
        {
            if (_model == null || !Visible) return;
            float scale = Mathf.Max(1e-6f, _model.lossyScale.x);
            var start = CadCoordinates.ToLocal(Origin);
            var end = CadCoordinates.ToLocal(Origin + Axis * Math.Max(HeightMm, 0));
            _stem.positionCount = 2; _stem.SetPosition(0, start); _stem.SetPosition(1, end);
            _stem.widthMultiplier = 0.004f / scale;
            _knob.SetActive(true); _anchorMark.SetActive(true);
            _knob.transform.localPosition = end;
            _knob.transform.localScale = Vector3.one * (KnobMetres / scale);
            _anchorMark.transform.localPosition = start;
            _anchorMark.transform.localScale = Vector3.one * (KnobMetres * 0.5f / scale);
        }

        /// <summary>
        /// Handle frame for an edge: origin at the length midpoint; direction = outward normal of the planar face
        /// that contains the edge and has a parallel partner one sheet thickness away (the sheet's own face),
        /// which excludes the thin side face. Without a match, the model up axis projected perpendicular to the edge
        /// is used and <paramref name="fromFaces"/> is false (the numeric field stays authoritative).
        /// </summary>
        public static bool TryFrame(DesignEdge edge, IReadOnlyList<DesignFace> faces, double? thicknessMm,
            out CadPoint origin, out CadPoint axis, out bool fromFaces)
        {
            origin = default; axis = default; fromFaces = false;
            if (edge == null || edge.PointsMm == null || edge.PointsMm.Count < 2) return false;
            origin = Midpoint(edge.PointsMm);
            var chord = edge.PointsMm[edge.PointsMm.Count - 1] - edge.PointsMm[0];
            if (chord.Length < 1e-9) chord = edge.PointsMm[1] - edge.PointsMm[0];
            if (chord.Length < 1e-9) return false;
            var direction = chord * (1 / chord.Length);
            DesignFace best = null;
            if (faces != null)
                foreach (var face in faces)
                {
                    if (Math.Abs(face.Normal.Dot(origin - face.PointMm)) > PlaneToleranceMm) continue;
                    if (Math.Abs(face.Normal.Dot(direction)) > ParallelTolerance) continue;
                    if (best == null) best = face; // weak candidate, kept only when no sheet-thickness partner exists
                    if (thicknessMm.HasValue && HasSheetPartner(face, faces, thicknessMm.Value)) { best = face; break; }
                }
            if (best != null) { axis = best.Normal; fromFaces = true; return true; }
            var up = new CadPoint(0, 1, 0);
            var candidate = up - direction * up.Dot(direction);
            if (candidate.Length < 1e-6) { var z = new CadPoint(0, 0, 1); candidate = z - direction * z.Dot(direction); }
            axis = candidate * (1 / candidate.Length);
            return true;
        }

        private static bool HasSheetPartner(DesignFace face, IReadOnlyList<DesignFace> faces, double thickness)
        {
            double tolerance = Math.Max(0.02, thickness * 0.05);
            foreach (var other in faces)
            {
                if (ReferenceEquals(other, face) || Math.Abs(Math.Abs(other.Normal.Dot(face.Normal)) - 1) > ParallelTolerance) continue;
                if (Math.Abs(Math.Abs(face.Normal.Dot(other.PointMm - face.PointMm)) - thickness) <= tolerance) return true;
            }
            return false;
        }

        public static CadPoint Midpoint(IReadOnlyList<CadPoint> points)
        {
            double length = 0;
            for (int i = 1; i < points.Count; i++) length += (points[i] - points[i - 1]).Length;
            double remaining = length * 0.5;
            for (int i = 1; i < points.Count; i++)
            {
                var segment = points[i] - points[i - 1];
                if (segment.Length > 0 && remaining <= segment.Length) return points[i - 1] + segment * (remaining / segment.Length);
                remaining -= segment.Length;
            }
            return points[0];
        }

        private void OnDestroy()
        {
            Release(_idle); Release(_active); Release(_anchor);
            Release(_knob); Release(_anchorMark);
            if (_stem != null) Release(_stem.gameObject);
        }

        private static void Release(UnityEngine.Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
        }
    }
}
