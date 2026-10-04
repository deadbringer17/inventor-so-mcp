using TMPro;
using System;
using System.Collections.Generic;
using System.Globalization;
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Ui;
using InventorXrSo.Unity.Ui;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace InventorXrSo.Unity.Scene
{
    /// <summary>
    /// Draws <see cref="FlatPatternView.Asset"/> (Inventor's own flat pattern GLB). With a work plane
    /// (<see cref="SetWorkPlane"/>) the pattern lies on the plane in front of the user, sized to the sheet area
    /// (<see cref="WorkbenchLayout.SheetWidth"/> x <see cref="WorkbenchLayout.SheetDepth"/>) and replaces the folded part there
    /// (the folded renderers are hidden: the "Sviluppo" view); "Piegato" hides the pattern again and brings the folded part back.
    /// Without a work plane (headless tests) it keeps the legacy place beside the folded part. Display only: no colliders,
    /// no <see cref="CadBody"/>, on the built-in "Ignore Raycast" layer that <see cref="CadRaycaster"/> never queries, so it
    /// can never become a CAD reference. The view offset (Grip, "Stacca") is view state; nothing here reaches the backend.
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
        private TextMeshProUGUI _labelText;
        private bool _presented;
        private WorkbenchFrame _frame;
        private Transform _proxy;
        private readonly PoseTween _tween = new PoseTween();
        private bool _planePlaced, _animateNext, _lastDetached, _hasTarget;
        private Vector3 _targetPosition;
        private Quaternion _targetRotation;
        private float _targetScale;
        private float _zoom = 1;

        /// <summary>The pattern is on view ("Sviluppo"); false is the "Piegato" view with the pattern hidden. Default true.</summary>
        public bool FlatViewOn { get; private set; } = true;
        /// <summary>True while the folded part's renderers are hidden because the pattern replaces it on the work plane.</summary>
        public bool FoldedHidden { get; private set; }
        public bool HasWorkPlane => _frame != null;
        public bool Tweening => _tween.Active;
        public bool IsShowing => _meshRoot != null && _meshRoot.activeSelf;
        public Transform MeshRoot => _meshRoot != null ? _meshRoot.transform : null;
        public string ShownKey => _shownKey;
        public string LabelText => _labelText != null ? _labelText.text : "";
        public bool LabelVisible => _label != null && _label.gameObject.activeSelf;

        /// <summary>Bounds of the flat mesh in the view's local frame (metres), including the current placement.</summary>
        public Bounds LocalBounds
        {
            get
            {
                if (_meshRoot == null || _view == null) return new Bounds(_bounds.center, _bounds.size);
                var t = _meshRoot.transform; var e = _bounds.extents;
                Bounds result = default; bool first = true;
                for (int corner = 0; corner < 8; corner++)
                {
                    var p = _view.transform.InverseTransformPoint(t.TransformPoint(_bounds.center + Vector3.Scale(e,
                        new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1))));
                    if (first) { result = new Bounds(p, Vector3.zero); first = false; } else result.Encapsulate(p);
                }
                return result;
            }
        }

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
            // The pose of the pattern on the plane is tweened on this neutral transform and copied to the mesh root each frame,
            // so a simultaneous change of the folded part's scale can never leave the pattern at the wrong size.
            _proxy = new GameObject("Flat pattern pose").transform;
            _proxy.SetParent(transform, false);
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

        /// <summary>
        /// The seated work plane (frame of the last Recenter). The pattern is placed on it in front of the user and moves there
        /// with a short pose transition. Null: legacy placement beside the folded part.
        /// </summary>
        public void SetWorkPlane(WorkbenchFrame frame)
        {
            bool changed = !ReferenceEquals(_frame, frame);
            _frame = frame;
            if (changed) { _animateNext = true; _zoom = 1; }
            Refresh();
        }

        /// <summary>"Sviluppo" (true) puts the pattern on the work plane, "Piegato" (false) hides it and brings the folded part back.</summary>
        public void SetFlatView(bool on)
        {
            if (FlatViewOn == on) return;
            FlatViewOn = on; _animateNext = true; _zoom = 1;
            Refresh();
        }

        /// <summary>Back to the placed pose and scale (Adatta).</summary>
        public void ResetPlane()
        {
            _zoom = 1; _animateNext = true;
            if (_frame != null && _meshRoot != null && _meshRoot.activeSelf) Place();
        }

        /// <summary>Left stick zoom of the pattern on the plane; the world scale stays inside [MinScale, MaxScale].</summary>
        public void ZoomPlane(float axis, float seconds)
        {
            if (_frame == null || _meshRoot == null || !_meshRoot.activeSelf) return;
            PlaneTarget(out _, out _, out float baseScale, includeOffset: true, zoom: 1);
            float target = Mathf.Clamp(baseScale * _zoom * Mathf.Exp(axis * 1.5f * seconds), (float)WorkbenchLayout.MinScale, (float)WorkbenchLayout.MaxScale);
            _zoom = target / baseScale;
            _animateNext = false;
            Place();
        }

        /// <summary>Finishes any running pose transition (tests, hand grabs of the scene).</summary>
        public void Snap() { _tween.Snap(); ApplyProxy(); }

        /// <summary>Default place beside the folded part (view-local metres): to its +X side with a gap, centred on Y/Z.</summary>
        public Vector3 DefaultLocalPosition()
        {
            var folded = ScenePlacement.LocalBounds(_view.transform);
            float gap = Mathf.Max(0.06f, folded.size.x * 0.15f);
            var target = new Vector3(folded.max.x + gap + _bounds.extents.x, folded.center.y, folded.center.z);
            return target - _bounds.center;
        }

        /// <summary>
        /// The view offset (what <see cref="FlatPatternView.MoveLocal"/> stores) that puts the mesh root at this world position:
        /// on the work plane the offset is in plane metres (frame yaw), otherwise in the folded part's local frame.
        /// </summary>
        public Vector3 OffsetForRootPosition(Vector3 rootWorld)
        {
            if (_frame != null && _meshRoot != null)
            {
                PlaneTarget(out var position, out _, out _, includeOffset: false, zoom: _zoom);
                return Quaternion.Inverse(Quaternion.Euler(0, (float)_frame.YawDegrees, 0)) * (rootWorld - position);
            }
            return _view.transform.InverseTransformPoint(rootWorld) - DefaultLocalPosition();
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
            bool show = _presented && _state != null && _state.IsVisible && _state.Asset != null && FlatViewOn;
            if (!show) { Hide(); return; }
            if (_shownKey != _state.Asset.Key || _meshRoot == null) Build(_state.Asset);
            _meshRoot.SetActive(true);
            SetFoldedHidden(_frame != null);
            Place();
            UpdateLabel();
        }

        private void Place()
        {
            if (_frame == null) { PlaceBesideFolded(); return; }
            PlaneTarget(out var position, out var rotation, out float scale, includeOffset: true, zoom: _zoom);
            bool detached = _state != null && _state.Detached;
            bool same = _hasTarget && (position - _targetPosition).sqrMagnitude < 1e-10f && Quaternion.Angle(rotation, _targetRotation) < 1e-3f
                && Mathf.Abs(scale - _targetScale) < 1e-7f;
            if (_planePlaced && _tween.Active && same && !_animateNext) { ApplyProxy(); return; }   // the running transition already goes there
            // A transition in progress is never cut short by a later refresh: it continues towards the new target (no jump).
            bool attached = _lastDetached && !detached;   // only "Riaggancia" moves the pattern back; "Stacca" alone moves nothing
            bool animate = _animateNext || !_planePlaced || attached || _tween.Active;
            _lastDetached = detached; _animateNext = false;
            _hasTarget = true; _targetPosition = position; _targetRotation = rotation; _targetScale = scale;
            float parent = Mathf.Max(1e-6f, _proxy.parent != null ? _proxy.parent.lossyScale.x : 1f);
            if (!_planePlaced)
            {
                // The pattern grows out of the folded part's centre on its way to the plane: no jump.
                var centre = _view.transform.TransformPoint(ScenePlacement.LocalBounds(_view.transform).center);
                _proxy.SetPositionAndRotation(centre, _view.transform.rotation);
                _proxy.localScale = Vector3.one * (scale * 0.5f / parent);
                _planePlaced = true;
            }
            if (animate) _tween.Start(_proxy, position, rotation, scale / parent);
            else { _tween.Cancel(); _proxy.SetPositionAndRotation(position, rotation); _proxy.localScale = Vector3.one * (scale / parent); }
            ApplyProxy();
        }

        private void ApplyProxy()
        {
            if (_proxy == null || _meshRoot == null || _view == null || _frame == null || !_planePlaced) return;
            float parent = Mathf.Max(1e-6f, _proxy.parent != null ? _proxy.parent.lossyScale.x : 1f);
            float viewScale = Mathf.Max(1e-6f, _view.transform.lossyScale.x);
            _meshRoot.transform.SetPositionAndRotation(_proxy.position, _proxy.rotation);
            _meshRoot.transform.localScale = Vector3.one * (_proxy.localScale.x * parent / viewScale);
        }

        /// <summary>
        /// Rotation that lays the thinnest axis of the mesh on the work plane's up axis and its longest axis along the frame's
        /// right (the pattern is flat: the sheet normal is its smallest extent), then the frame yaw.
        /// </summary>
        private Quaternion LayFlat(out int thin, out int along, out int across)
        {
            var size = _bounds.size;
            thin = 0;
            for (int i = 1; i < 3; i++) if (size[i] < size[thin]) thin = i;
            along = (thin + 1) % 3; across = (thin + 2) % 3;
            if (size[across] > size[along]) { int swap = along; along = across; across = swap; }
            Vector3 up = Axis(thin), right = Axis(along), forward = Vector3.Cross(right, up);
            var q = Quaternion.Inverse(Quaternion.LookRotation(forward, up));
            return Quaternion.Euler(0, (float)_frame.YawDegrees, 0) * q;
        }

        private static Vector3 Axis(int i) => i == 0 ? Vector3.right : i == 1 ? Vector3.up : Vector3.forward;

        /// <summary>World pose of the mesh root that centres the pattern on the work plane, and the world scale that fits the sheet area.</summary>
        private void PlaneTarget(out Vector3 position, out Quaternion rotation, out float scale, bool includeOffset, float zoom)
        {
            rotation = LayFlat(out int thin, out int along, out int across);
            var size = _bounds.size;
            float baseScale = (float)WorkbenchLayout.SheetScale(size[along], size[across]);
            scale = zoom == 1 ? baseScale : Mathf.Clamp(baseScale * zoom, (float)WorkbenchLayout.MinScale, (float)WorkbenchLayout.MaxScale);
            var floor = WorkbenchLayout.Part(_frame, 0.4).Position;
            var centre = new Vector3((float)floor.X, (float)floor.Y + 0.002f + 0.5f * size[thin] * scale, (float)floor.Z);
            if (includeOffset && _state != null && _state.Detached)
                centre += Quaternion.Euler(0, (float)_frame.YawDegrees, 0) * new Vector3((float)_state.OffsetX, (float)_state.OffsetY, (float)_state.OffsetZ);
            position = centre - rotation * (_bounds.center * scale);
        }

        private void PlaceBesideFolded()
        {
            var offset = _state == null ? Vector3.zero : new Vector3((float)_state.OffsetX, (float)_state.OffsetY, (float)_state.OffsetZ);
            _meshRoot.transform.localPosition = DefaultLocalPosition() + (_state != null && _state.Detached ? offset : Vector3.zero);
        }

        /// <summary>The folded part gives way to the pattern on the work plane; its renderers come back when the pattern goes.</summary>
        private void SetFoldedHidden(bool hidden)
        {
            FoldedHidden = hidden;
            if (_view == null) return;
            foreach (var body in _view.GetComponentsInChildren<CadBody>(true))
            {
                var renderer = body.GetComponent<MeshRenderer>();
                if (renderer != null) renderer.enabled = !hidden;
            }
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
            _planePlaced = false;
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
            SetFoldedHidden(false); _planePlaced = false; _hasTarget = false; _tween.Cancel();
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
            if (_frame != null && _meshRoot != null && _meshRoot.activeSelf) { _tween.Tick(Time.unscaledDeltaTime); ApplyProxy(); }
            if (_label == null || !_label.gameObject.activeSelf || _meshRoot == null || _head == null) return;
            var bounds = LocalBounds;
            _label.transform.SetPositionAndRotation(_view.transform.TransformPoint(bounds.center + Vector3.up * (bounds.extents.y + 0.07f)), _head.rotation);
        }

        private void OnDestroy()
        {
            if (_view != null) _view.Rebuilt -= Refresh;
            if (_state != null) _state.Changed -= Refresh;
            if (_view != null) SetFoldedHidden(false);
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
