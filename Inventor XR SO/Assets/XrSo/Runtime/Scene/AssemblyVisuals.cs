using System.Collections.Generic;
using System.Linq;
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Glb;
using UnityEngine;

namespace InventorXrSo.Unity.Scene
{
    /// <summary>Native DOF axes and the explicitly chosen relationship references in CAD coordinates.</summary>
    public sealed class AssemblyVisuals : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private Material _material;
        private Material _faceMaterial;
        private CadSceneView _view;
        private readonly List<GameObject> _objects = new List<GameObject>();
        private readonly List<(GameObject overlay, Mesh mesh)> _faceOverlays = new List<(GameObject, Mesh)>();
        private CadPoint[] _handle;
        public void Initialize(Material material)
        {
            _material = material;
            var view = GetComponent<CadSceneView>();
            if (_view == view) return;
            if (_view != null) _view.Rebuilt -= Clear;
            _view = view;
            if (_view != null) _view.Rebuilt += Clear;
        }
        public void Clear()
        {
            foreach (var item in _objects) { if (item == null) continue; item.SetActive(false); if (Application.isPlaying) Destroy(item); else DestroyImmediate(item); }
            _objects.Clear(); _handle = null;
            foreach (var (overlay, mesh) in _faceOverlays)
            {
                if (overlay != null) overlay.SetActive(false);
                if (Application.isPlaying)
                {
                    if (mesh != null) Destroy(mesh);
                    if (overlay != null) Destroy(overlay);
                }
                else
                {
                    if (mesh != null) DestroyImmediate(mesh);
                    if (overlay != null) DestroyImmediate(overlay);
                }
            }
            _faceOverlays.Clear();
        }
        private void Line(string name, IEnumerable<CadPoint> points, Color color, float width = 0.002f)
        {
            var line = CadCoordinates.Line(transform, name, points, _material, width);
            line.startColor = line.endColor = color;
            var properties = new MaterialPropertyBlock();
            line.GetPropertyBlock(properties);
            properties.SetColor(BaseColorId, color);
            properties.SetColor(ColorId, color);
            line.SetPropertyBlock(properties);
            _objects.Add(line.gameObject);
        }
        public void Show(AssemblyOccurrence occurrence, AssemblyReference a, AssemblyReference b, bool moving, bool rotation, int index)
        {
            Clear(); Reference(a, Color.cyan); Reference(b, Color.yellow);
            if (occurrence?.DofComplete != true || !occurrence.Center.HasValue) return;
            var center = occurrence.Center.Value;
            for (int i = 0; i < occurrence.TranslationAxes.Count; i++)
            {
                var axis = occurrence.TranslationAxes[i]; var end = center + axis * 100;
                var points = new[] { center, end };
                bool active = moving && !rotation && i == index;
                if (moving && !active) continue;
                Line("DOF traslazione " + (i + 1), points, active ? Color.yellow : Color.cyan, active ? 0.008f : 0.002f);
                var side = axis.Cross(System.Math.Abs(axis.Y) < 0.9 ? new CadPoint(0, 1, 0) : new CadPoint(1, 0, 0));
                side = side * (1 / side.Length);
                Line("Freccia", new[] { end - axis * 12 + side * 6, end, end - axis * 12 - side * 6 }, active ? Color.yellow : Color.cyan, active ? 0.005f : 0.002f);
                if (active) _handle = points;
            }
            for (int i = 0; i < occurrence.RotationAxes.Count; i++)
            {
                var axis = occurrence.RotationAxes[i]; var x = axis.Cross(System.Math.Abs(axis.Y) < 0.9 ? new CadPoint(0, 1, 0) : new CadPoint(1, 0, 0));
                x = x * (1 / x.Length); var y = axis.Cross(x);
                var points = Enumerable.Range(0, 41).Select(n => center + x * (60 * System.Math.Cos(n * System.Math.PI / 24)) + y * (60 * System.Math.Sin(n * System.Math.PI / 24))).ToArray();
                bool active = moving && rotation && i == index;
                if (moving && !active) continue;
                Line("DOF rotazione " + (i + 1), points, active ? Color.yellow : Color.green, active ? 0.008f : 0.002f);
                if (active) _handle = points;
            }
        }
        private void Reference(AssemblyReference reference, Color color)
        {
            if (reference == null || !reference.Point.HasValue) return;
            FaceOverlay(reference, color);
            var point = reference.Point.Value;
            if (reference.Polyline.Count > 0) Line(reference.Name, reference.Polyline, color, 0.003f);
            Line(reference.Name, new[] { point - new CadPoint(4, 0, 0), point + new CadPoint(4, 0, 0), point, point - new CadPoint(0, 4, 0), point + new CadPoint(0, 4, 0) }, color);
            if (reference.Axis.HasValue)
            {
                var axis = reference.Axis.Value; Line("Direzione " + reference.Name, new[] { point, point + axis * 50 }, color);
                if (reference.IsPlane)
                {
                    var x = axis.Cross(System.Math.Abs(axis.Y) < 0.9 ? new CadPoint(0, 1, 0) : new CadPoint(1, 0, 0));
                    x = x * (20 / x.Length); var y = axis.Cross(x);
                    Line("Piano " + reference.Name, new[] { point-x-y, point+x-y, point+x+y, point-x+y, point-x-y }, color);
                }
            }
        }
        private void FaceOverlay(AssemblyReference reference, Color color)
        {
            if (_view == null || reference.Kind != "face" || reference.BodyIndex <= 0 || reference.FaceOrdinal <= 0) return;
            var instance = _view.Find(reference.OccurrenceId);
            if (instance == null) return;
            foreach (var body in instance.Bodies)
            {
                if (body.Primitive.BodyIndex != reference.BodyIndex) continue;
                var range = body.Primitive.Faces.FirstOrDefault(face => face.Ordinal == reference.FaceOrdinal);
                if (range == null) return;
                var overlay = new GameObject("AssemblyFaceHighlight");
                overlay.transform.SetParent(body.transform, false);
                var mesh = MeshFactory.BuildFaceOverlay(body.Mesh, range);
                overlay.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = overlay.AddComponent<MeshRenderer>();
                if (_faceMaterial == null)
                {
                    var shader = Shader.Find("XrSo/HighlightOverlay");
                    if (shader == null) { DestroyOverlay(overlay, mesh); return; }
                    _faceMaterial = new Material(shader) { name = "Assembly face highlight" };
                }
                renderer.sharedMaterial = _faceMaterial;
                var properties = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(properties);
                var tint = color == Color.cyan ? new Color(0.1f, 0.9f, 1f, 0.42f) : new Color(1f, 0.78f, 0.08f, 0.42f);
                properties.SetColor(ColorId, tint);
                renderer.SetPropertyBlock(properties);
                _faceOverlays.Add((overlay, mesh));
                return;
            }
        }
        private static void DestroyOverlay(GameObject overlay, Mesh mesh)
        {
            if (overlay != null) overlay.SetActive(false);
            if (Application.isPlaying)
            {
                if (mesh != null) Destroy(mesh);
                if (overlay != null) Destroy(overlay);
            }
            else
            {
                if (mesh != null) DestroyImmediate(mesh);
                if (overlay != null) DestroyImmediate(overlay);
            }
        }
        public bool HitHandle(Ray ray)
        {
            if (_handle == null) return false;
            for (int i = 1; i < _handle.Length; i++)
            {
                var point = CadCoordinates.ClosestPointOnSegment(ray, transform.TransformPoint(CadCoordinates.ToLocal(_handle[i-1])), transform.TransformPoint(CadCoordinates.ToLocal(_handle[i])));
                float distance = Vector3.Dot(point - ray.origin, ray.direction);
                if (distance >= 0 && Vector3.Distance(ray.GetPoint(distance), point) < 0.025f) return true;
            }
            return false;
        }
        private void OnDestroy()
        {
            if (_view != null) _view.Rebuilt -= Clear;
            Clear();
            if (_faceMaterial == null) return;
            if (Application.isPlaying) Destroy(_faceMaterial); else DestroyImmediate(_faceMaterial);
            _faceMaterial = null;
        }
    }
}
