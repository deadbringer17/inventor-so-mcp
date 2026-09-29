using System;
using System.Collections.Generic;
using System.Globalization;
using InventorXrSo.Core.Backend;
using InventorXrSo.Unity.Ui;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace InventorXrSo.Unity.Scene
{
    /// <summary>
    /// Draws <see cref="FlatPatternView.Asset"/> (Inventor's own flat pattern GLB) beside the folded part, in the
    /// folded part's frame and scale so the two can be compared. Display only: no colliders, no <see cref="CadBody"/>,
    /// on the built-in "Ignore Raycast" layer that <see cref="CadRaycaster"/> never queries, so it can never become a
    /// CAD reference. The view offset (Grip) is view state; nothing here reaches the backend.
    /// </summary>
    public sealed class FlatPatternDisplay : MonoBehaviour
    {
        /// <summary>Unity's built-in "Ignore Raycast" layer, outside Physics.DefaultRaycastLayers.</summary>
        public const int NoRaycastLayer = 2;
        private const float LabelScale = 0.0007f;
        private static readonly CultureInfo Italian = CultureInfo.GetCultureInfo("it-IT");
        private static readonly Color Normal = new Color(0.08f, 0.09f, 0.11f, 0.92f), ReadOnly = new Color(0.45f, 0.30f, 0.05f, 0.95f);

        private CadSceneView _view;
        private Transform _head;
        private FlatPatternView _state;
        private Material _material;
        private GameObject _meshRoot;
        private readonly List<Mesh> _meshes = new List<Mesh>();
        private string _shownKey;
        private Bounds _bounds;
        private Canvas _label;
        private Image _labelBackground;
        private Text _labelText;
        private bool _presented;

        public bool IsShowing => _meshRoot != null && _meshRoot.activeSelf;
        public Transform MeshRoot => _meshRoot != null ? _meshRoot.transform : null;
        public string ShownKey => _shownKey;
        public string LabelText => _labelText != null ? _labelText.text : "";
        public bool LabelVisible => _label != null && _label.gameObject.activeSelf;
        /// <summary>Bounds of the flat mesh in the view's local frame (metres), including the current placement.</summary>
        public Bounds LocalBounds => new Bounds(_bounds.center + (_meshRoot != null ? _meshRoot.transform.localPosition : Vector3.zero), _bounds.size);

        public void Initialize(CadSceneView view, Transform head)
        {
            if (_view != null) throw new InvalidOperationException("Flat pattern display is already initialized.");
            if (view == null || view.BodyMaterial == null) throw new ArgumentException("A CAD view with a body material is required.");
            _view = view; _head = head;
            _material = new Material(view.BodyMaterial) { name = "Flat pattern (view only)" };
            _material.SetColor("_BaseColor", new Color(0.95f, 0.7f, 0.25f, 1f));
            _material.SetFloat("_UseVertexColor", 0f);   // flat pattern keeps its own colour
            _label = UiFactory.WorldCanvas(transform, "Etichetta sviluppo", new Vector2(560, 210));
            _label.transform.localScale = Vector3.one * LabelScale;
            _labelBackground = UiFactory.Panel(_label.transform, "Background", Normal).GetComponent<Image>();
            UiFactory.Stretch((RectTransform)_labelBackground.transform);
            _labelBackground.raycastTarget = false; // never a UI target either
            _labelText = UiFactory.Label(_labelBackground.transform, "", 24);
            _labelText.raycastTarget = false;
            UiFactory.Stretch(_labelText.rectTransform);
            _labelText.rectTransform.offsetMin = new Vector2(12, 8); _labelText.rectTransform.offsetMax = new Vector2(-12, -8);
            _label.gameObject.SetActive(false);
            _view.Rebuilt += Refresh;
        }

        public void Bind(FlatPatternView state)
        {
            if (_state != null) _state.Changed -= Refresh;
            _state = state;
            if (_state != null) _state.Changed += Refresh;
            Refresh();
        }

        /// <summary>The display is shown only while its workspace is active; the state itself is kept.</summary>
        public void SetPresented(bool presented) { _presented = presented; Refresh(); }

        /// <summary>Default place beside the folded part (view-local metres): to its +X side with a gap, centred on Y/Z.</summary>
        public Vector3 DefaultLocalPosition()
        {
            var folded = ScenePlacement.LocalBounds(_view.transform);
            float gap = Mathf.Max(0.06f, folded.size.x * 0.15f);
            var target = new Vector3(folded.max.x + gap + _bounds.extents.x, folded.center.y, folded.center.z);
            return target - _bounds.center;
        }

        /// <summary>Ray against the flat display, padded so a controller ray can grab it (Grip, view only).</summary>
        public bool HitTest(Ray worldRay)
        {
            if (!IsShowing) return false;
            var t = _view.transform;
            var local = new Ray(t.InverseTransformPoint(worldRay.origin), t.InverseTransformDirection(worldRay.direction));
            var box = LocalBounds;
            float scale = Mathf.Max(1e-6f, t.lossyScale.x);
            box.Expand(0.04f / scale);
            return box.IntersectRay(local);
        }

        public void Refresh()
        {
            if (_view == null) return;
            bool show = _presented && _state != null && _state.IsVisible && _state.Asset != null;
            if (!show) { Hide(); return; }
            if (_shownKey != _state.Asset.Key || _meshRoot == null) Build(_state.Asset);
            _meshRoot.SetActive(true);
            Place();
            UpdateLabel();
        }

        private void Place()
        {
            var offset = _state == null ? Vector3.zero : new Vector3((float)_state.OffsetX, (float)_state.OffsetY, (float)_state.OffsetZ);
            _meshRoot.transform.localPosition = DefaultLocalPosition() + (_state != null && _state.Detached ? offset : Vector3.zero);
        }

        private void Build(FlatPatternMesh asset)
        {
            Clear();
            _meshRoot = new GameObject("Flat pattern (view only)");
            _meshRoot.transform.SetParent(_view.transform, false);
            _meshRoot.layer = NoRaycastLayer;
            bool first = true; _bounds = new Bounds(Vector3.zero, Vector3.zero);
            foreach (var primitive in asset.Model.Primitives)
            {
                if (!primitive.Visible) continue;
                var mesh = MeshFactory.Build(primitive); _meshes.Add(mesh);
                var body = new GameObject(primitive.BodyName);
                body.transform.SetParent(_meshRoot.transform, false);
                body.layer = NoRaycastLayer;
                body.AddComponent<MeshFilter>().sharedMesh = mesh;
                body.AddComponent<MeshRenderer>().sharedMaterial = _material;
                // No collider and no CadBody by design: the flat pattern is not a CAD reference.
                if (first) { _bounds = mesh.bounds; first = false; } else _bounds.Encapsulate(mesh.bounds);
            }
            _shownKey = asset.Key;
        }

        private void UpdateLabel()
        {
            var a = _state.Asset;
            string measures = "Lunghezza " + Fmt(a.LengthMm) + " mm • Larghezza " + Fmt(a.WidthMm) + " mm"
                + (a.ThicknessMm.HasValue ? " • Spessore " + Fmt(a.ThicknessMm.Value) + " mm" : "") + " • Pieghe " + a.BendCount;
            string text = _state.StatusText + "\n" + measures;
            if (_state.Detached) text += "\nStaccato: Grip sposta solo la vista";
            _labelText.text = text;
            _labelBackground.color = _state.State == FlatPatternState.StaleReadOnly ? ReadOnly : Normal;
            _label.gameObject.SetActive(true);
        }

        private static string Fmt(double value) => value.ToString("0.###", Italian);

        private void Hide()
        {
            if (_meshRoot != null) _meshRoot.SetActive(false);
            if (_label != null) _label.gameObject.SetActive(false);
            if (_state == null || !_state.IsVisible) Clear(); // a hidden pattern keeps no mesh, so nothing stale can reappear
        }

        private void Clear()
        {
            _shownKey = null;
            if (_meshRoot != null) { _meshRoot.SetActive(false); Release(_meshRoot); _meshRoot = null; }
            foreach (var mesh in _meshes) Release(mesh);
            _meshes.Clear();
        }

        private void LateUpdate()
        {
            if (_label == null || !_label.gameObject.activeSelf || _meshRoot == null || _head == null) return;
            var top = _meshRoot.transform.localPosition + _bounds.center + Vector3.up * (_bounds.extents.y + 0.07f);
            _label.transform.SetPositionAndRotation(_view.transform.TransformPoint(top), _head.rotation);
        }

        private void OnDestroy()
        {
            if (_view != null) _view.Rebuilt -= Refresh;
            if (_state != null) _state.Changed -= Refresh;
            Clear(); Release(_material);
            if (_label != null) Release(_label.gameObject);
        }

        private static void Release(Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
        }
    }
}
