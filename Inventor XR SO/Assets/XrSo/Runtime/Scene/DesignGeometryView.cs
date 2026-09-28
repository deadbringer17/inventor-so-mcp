using System;
using System.Collections.Generic;
using System.Linq;
using InventorXrSo.Core.Backend;
using UnityEngine;

namespace InventorXrSo.Unity.Scene
{
    public static class CadCoordinates
    {
        public static Vector3 ToLocal(CadPoint p) => new Vector3((float)-p.X, (float)p.Y, (float)p.Z) * 0.001f;
        public static CadPoint FromLocal(Vector3 p) => new CadPoint(-p.x*1000.0, p.y*1000.0, p.z*1000.0);
        public static CadPoint FromWorld(Transform model, Vector3 p) => FromLocal(model.InverseTransformPoint(p));
        public static bool SketchRay(Transform model, SketchFrame frame, Ray worldRay, out CadPoint point)
        {
            var origin = FromWorld(model, worldRay.origin);
            var second = FromWorld(model, worldRay.origin + worldRay.direction);
            return frame.IntersectRay(origin, second-origin, out point);
        }
        public static LineRenderer Line(Transform parent, string name, IEnumerable<CadPoint> modelPoints, Material material, float width = 0.0012f)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            var line = go.AddComponent<LineRenderer>(); line.useWorldSpace = false;
            line.sharedMaterial = material; line.widthMultiplier = width;
            var points = modelPoints.Select(ToLocal).ToArray(); line.positionCount = points.Length; line.SetPositions(points);
            return line;
        }
        public static string PickEdge(Transform model, IReadOnlyList<DesignEdge> edges, Ray ray, float toleranceMetres = 0.012f, bool requireVisible = false)
        {
            string best = null; float bestDistance = toleranceMetres, bestDepth = float.PositiveInfinity;
            foreach (var edge in edges)
            for (int i = 1; i < edge.PointsMm.Count; i++)
            {
                var a = model.TransformPoint(ToLocal(edge.PointsMm[i-1]));
                var b = model.TransformPoint(ToLocal(edge.PointsMm[i]));
                float distance = RaySegmentDistance(ray, a, b, out float depth, out var point);
                if (distance > toleranceMetres || distance > bestDistance+0.00001f) continue;
                if (requireVisible)
                {
                    var sight = point-ray.origin;
                    if (Vector3.Dot(sight,ray.direction)<=0) continue;
                    // Test the actual candidate point, not the centre ray: a near-edge ray
                    // can miss the front surface while still selecting a hidden rear edge.
                    if (CadRaycaster.TryPick(new Ray(ray.origin,sight.normalized),
                        Mathf.Max(0,sight.magnitude-0.00001f),out _,out _,out _)) continue;
                }
                if (distance < bestDistance || (Mathf.Abs(distance-bestDistance) < 0.00001f && depth < bestDepth))
                { best = edge.Id; bestDistance = distance; bestDepth = depth; }
            }
            return best;
        }
        public static Vector3 ClosestPointOnSegment(Ray ray,Vector3 a,Vector3 b)
        {
            RaySegmentDistance(ray,a,b,out _,out var point); return point;
        }
        private static float RaySegmentDistance(Ray ray, Vector3 a, Vector3 b, out float depth, out Vector3 point)
        {
            var direction = ray.direction.normalized;
            var edge = b-a; var offset = ray.origin-a;
            float edgeLength = edge.sqrMagnitude, dot = Vector3.Dot(direction, edge);
            float d = Vector3.Dot(direction, offset), e = Vector3.Dot(edge, offset);
            float denominator = edgeLength-dot*dot;
            float t = denominator > 1e-12f ? Mathf.Clamp01((e-dot*d)/denominator) : 0;
            depth = Mathf.Max(0, dot*t-d);
            if (depth == 0 && edgeLength > 1e-12f) t = Mathf.Clamp01(e/edgeLength);
            point = a+edge*t;
            return Vector3.Distance(ray.origin+direction*depth, point);
        }
    }

    public sealed class DesignGeometryView : MonoBehaviour
    {
        private Material _material;
        private GameObject _content;
        private LineRenderer _cursor;
        private GameObject _errorContext;
        private Material _errorMaterial;
        public bool HasErrorContext => _errorContext != null && _errorContext.activeSelf;
        public void Initialize(Material material)
        {
            _material = material;
            _cursor = CadCoordinates.Line(transform, "Sketch cursor", Array.Empty<CadPoint>(), material, 0.002f);
        }
        public void ShowDraft(SketchDraft draft, int selected = -1, int selectedSide = -1)
        {
            ClearContent();
            if (draft?.Frame == null) return;
            _content = new GameObject("Local sketch draft"); _content.transform.SetParent(transform, false);
            for (int i=0;i<draft.Elements.Count;i++)
            {
                var shape=draft.Elements[i];
                CadCoordinates.Line(_content.transform, shape.Shape.ToString(), shape.Outline().Select(draft.Frame.ToModel), _material,i==selected && selectedSide<0 ? 0.003f : 0.0012f);
                if(i==selected && shape.Shape==SketchShape.Rectangle && selectedSide>=0 && selectedSide<4)
                {
                    var outline=shape.Outline();
                    CadCoordinates.Line(_content.transform,"Selected rectangle side",new[]{outline[selectedSide],outline[selectedSide+1]}.Select(draft.Frame.ToModel),_material,0.003f);
                }
            }
        }
        public void ShowEdges(IEnumerable<DesignEdge> edges)
        {
            ClearContent();
            _content = new GameObject("Selected edges"); _content.transform.SetParent(transform, false);
            foreach (var edge in edges) CadCoordinates.Line(_content.transform, edge.Id, edge.PointsMm, _material, 0.002f);
        }
        public void ShowErrorContext(IEnumerable<IEnumerable<CadPoint>> paths)
        {
            ClearErrorContext();
            if (_errorMaterial == null)
            {
                _errorMaterial = new Material(Shader.Find("XrSo/HighlightOverlay")) { name = "Design error context" };
                _errorMaterial.SetColor("_Color",new Color(1f,0.42f,0.08f,1f));
            }
            foreach (var path in paths)
            {
                var points = path.ToArray(); if (points.Length<2) continue;
                if (_errorContext == null)
                {
                    _errorContext = new GameObject("Command geometry requiring review");
                    _errorContext.transform.SetParent(transform,false);
                }
                CadCoordinates.Line(_errorContext.transform,"Review geometry",points,_errorMaterial,0.003f);
            }
        }
        public void ClearErrorContext()
        {
            if (_errorContext == null) return;
            _errorContext.SetActive(false);
            if (Application.isPlaying) Destroy(_errorContext); else DestroyImmediate(_errorContext);
            _errorContext = null;
        }
        public void Cursor(SketchFrame frame, CadPoint point, CadPoint? start, SketchShape shape)
        {
            if (frame == null) { _cursor.positionCount = 0; return; }
            CadPoint[] points;
            if (start.HasValue && (point-start.Value).Length > 0.00001)
            {
                try
                {
                    var element = new SketchElement(shape, start.Value, point, (point-start.Value).Length);
                    points = element.Outline().Select(frame.ToModel).ToArray();
                }
                catch (ArgumentException) { points = new[] { frame.ToModel(start.Value), frame.ToModel(point) }; }
            }
            else points = new[] { frame.ToModel(point+new CadPoint(-1,0)), frame.ToModel(point+new CadPoint(1,0)),
                frame.ToModel(point), frame.ToModel(point+new CadPoint(0,-1)), frame.ToModel(point+new CadPoint(0,1)) };
            _cursor.positionCount = points.Length;
            for (int i = 0; i < points.Length; i++) _cursor.SetPosition(i, CadCoordinates.ToLocal(points[i]));
        }
        public void Clear() { ClearContent(); ClearErrorContext(); if (_cursor != null) _cursor.positionCount = 0; }
        private void ClearContent()
        {
            if (_content == null) return; _content.SetActive(false);
            if (Application.isPlaying) Destroy(_content); else DestroyImmediate(_content);
            _content = null;
        }
        private void OnDisable() => Clear();
        private void OnDestroy()
        {
            if (_errorMaterial == null) return;
            if (Application.isPlaying) Destroy(_errorMaterial); else DestroyImmediate(_errorMaterial);
        }
    }
}
