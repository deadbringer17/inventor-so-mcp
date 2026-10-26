using System;
using System.Collections.Generic;
using System.Linq;
using InventorXrSo.Core.Backend;
using InventorXrSo.Unity.Ui;
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
        private LineRenderer _selectionWindow;
        private MaterialPropertyBlock _windowProperties;
        private GameObject _errorContext;
        private Material _errorMaterial;
        private NativeSketchConstraintLayer _constraintLayer;
        private NativeSketchImageLayer _imageLayer;
        private NativeSketchTextLayer _textLayer;
        public void BindNativeImages(INativeSketchImageBackend backend)
        {
            if (_imageLayer == null) _imageLayer = NativeSketchImageLayer.Create(transform, backend);
            else _imageLayer.BindBackend(backend);
        }
        public NativeSketchConstraintGlyphResult ConstraintLayout => _constraintLayer?.Layout;
        public string ConstraintLayoutUnavailableReason => _constraintLayer?.UnavailableReason ?? "";
        public IReadOnlyList<SketchEntityRef> PickConstraintLabels(NativeSketchSnapshot current, Ray ray)
            => _constraintLayer == null ? Array.Empty<SketchEntityRef>() : _constraintLayer.PickTargets(current,ray);
        public bool HasErrorContext => _errorContext != null && _errorContext.activeSelf;
        public void Initialize(Material material)
        {
            _material = material;
            _cursor = CadCoordinates.Line(transform, "Sketch cursor", Array.Empty<CadPoint>(), material, 0.002f);
            _selectionWindow = CadCoordinates.Line(transform, "Native selection window", Array.Empty<CadPoint>(), material, 0.0015f);
            _windowProperties = new MaterialPropertyBlock();
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
        public void ShowNative(NativeSketchSnapshot snapshot, Transform head = null, IEnumerable<SketchEntityRef> selected = null,bool showConstraints=true,
            IEnumerable<string> constraintNativeTypes=null)
        {
            if (snapshot == null) { ClearContent(); return; }
            var references = (selected ?? Array.Empty<SketchEntityRef>()).ToArray();
            if (references.Any(r => r == null || !r.Matches(snapshot.State,snapshot.SketchId)))
                throw new InvalidOperationException("Refresh native selection references before rendering another revision.");
            foreach(var reference in references)
            {
                var entity=snapshot.Entities.Concat(snapshot.Dimensions).Concat(snapshot.Constraints).SingleOrDefault(e=>e.Reference?.EntityId==reference.EntityId);
                if(entity==null || !entity.CanSelectSubpart(reference.Subpart) || entity.Type!=reference.Type)
                    throw new InvalidOperationException("Native selection subpart is absent from the authoritative snapshot.");
            }
            if(_constraintLayer == null) _constraintLayer=NativeSketchConstraintLayer.Create(transform,transform,_material);
            _imageLayer?.Synchronize(snapshot);
            _constraintLayer.Synchronize(snapshot,references.FirstOrDefault(r=>r.Type=="constraint"),showConstraints,constraintNativeTypes);
            references=references.Concat(_constraintLayer.Layout?.Entries.Where(c=>c.Reference!=null && references.Any(r=>r.EntityId==c.Reference.EntityId))
                .SelectMany(c=>c.RelatedEntities) ?? Enumerable.Empty<SketchEntityRef>())
                .GroupBy(r=>(r.EntityId,r.Subpart)).Select(g=>g.First()).ToArray();
            ClearContent(true); _content = new GameObject("Native sketch snapshot"); _content.transform.SetParent(transform,false);
            if(_textLayer==null)_textLayer=NativeSketchTextLayer.Create(transform);
            _textLayer.Synchronize(snapshot,references);
            foreach (var entity in snapshot.Entities)
            {
                bool highlight = entity.Reference != null && references.Any(r => r.EntityId == entity.Reference.EntityId && r.Subpart==SketchSubpart.Entity);
                float width = highlight ? .003f : .0012f;
                if (entity.Strokes.Count > 1)
                    CadCoordinates.Line(_content.transform,entity.Type,entity.Strokes.Select(snapshot.Frame.ToModel),_material,width);
                else if (entity.Strokes.Count == 1)
                {
                    var p = entity.Strokes[0];
                    CadCoordinates.Line(_content.transform,"Native point X",new[] {p+new CadPoint(-.5,0),p+new CadPoint(.5,0)}.Select(snapshot.Frame.ToModel),_material,width);
                    CadCoordinates.Line(_content.transform,"Native point Y",new[] {p+new CadPoint(0,-.5),p+new CadPoint(0,.5)}.Select(snapshot.Frame.ToModel),_material,width);
                }
                var data = entity.Data;
                string formatLabel=SketchSnapshot.NativeFormatLabel(data);
                var formatAnchor=data["point_mm"] ?? data["center_mm"] ?? data["start_mm"];
                if(formatLabel.Length>0 && formatAnchor!=null)
                    SketchDimensionLabel.Create(_content.transform,transform,head,snapshot.Frame.ToModel(CadPoint.Parse(formatAnchor,2)),formatLabel);
                if (entity.TextGlyphs==null && data["text_mm"] != null && data["text"] != null)
                    SketchDimensionLabel.Create(_content.transform,transform,head,snapshot.Frame.ToModel(CadPoint.Parse(data["text_mm"],2)),(string)data["text"]);
                foreach(var reference in references.Where(r=>r.EntityId==entity.Reference?.EntityId && r.Subpart!=SketchSubpart.Entity))
                {
                    entity.TryGetSubpartPoint(reference.Subpart,out var p);
                    CadCoordinates.Line(_content.transform,"Native selected "+reference.Subpart+" X",new[] {p+new CadPoint(-1,0),p+new CadPoint(1,0)}.Select(snapshot.Frame.ToModel),_material,.003f);
                    CadCoordinates.Line(_content.transform,"Native selected "+reference.Subpart+" Y",new[] {p+new CadPoint(0,-1),p+new CadPoint(0,1)}.Select(snapshot.Frame.ToModel),_material,.003f);
                }
            }
            foreach (var dimension in snapshot.Dimensions.Where(d => d.Supported && d.Data["text_mm"] != null))
            {
                var data = dimension.Data;
                if(references.Any(r=>r.EntityId==dimension.Reference?.EntityId && r.Subpart==SketchSubpart.Entity))
                {
                    var p=CadPoint.Parse(data["text_mm"],2);
                    CadCoordinates.Line(_content.transform,"Selected native dimension X",new[] {p+new CadPoint(-1,0),p+new CadPoint(1,0)}.Select(snapshot.Frame.ToModel),_material,.003f);
                    CadCoordinates.Line(_content.transform,"Selected native dimension Y",new[] {p+new CadPoint(0,-1),p+new CadPoint(0,1)}.Select(snapshot.Frame.ToModel),_material,.003f);
                }
                SketchDimensionLabel.Create(_content.transform,transform,head,snapshot.Frame.ToModel(CadPoint.Parse(data["text_mm"],2)),
                    (string)data["name"] + " = " + (string)data["expression"]);
            }
            foreach(var glyph in _constraintLayer.Layout?.Entries ?? Array.Empty<NativeSketchConstraintGlyph>())
            {
                if(glyph.State!=NativeSketchConstraintGlyphState.Visible || !glyph.AnchorMm.HasValue || !glyph.PositionMm.HasValue) continue;
                var anchor=glyph.AnchorMm.Value;
                if(glyph.Selected)
                {
                    CadCoordinates.Line(_content.transform,"Selected native constraint X",new[] {anchor+new CadPoint(-1,0),anchor+new CadPoint(1,0)}.Select(snapshot.Frame.ToModel),_material,.003f);
                    CadCoordinates.Line(_content.transform,"Selected native constraint Y",new[] {anchor+new CadPoint(0,-1),anchor+new CadPoint(0,1)}.Select(snapshot.Frame.ToModel),_material,.003f);
                }
            }
            if (snapshot.Partial)
                SketchDimensionLabel.Create(_content.transform,transform,head,snapshot.Frame.OriginMm,snapshot.NativeMutationComplete
                    ? "Immagini di sola lettura — altre geometrie modificabili" : "Schizzo parziale — modifica non disponibile");
        }
        /// <summary>
        /// The one edge on screen when exactly one real edge is shown (a picked edge, not a sketch profile or a multi-selection): its
        /// world bounds, for the selection label. False otherwise.
        /// </summary>
        public bool TryGetSingleEdgeBounds(out Bounds worldBounds)
        {
            worldBounds = default;
            if (_content == null || !_content.activeSelf || _singleEdge == null || _singleEdge.PointsMm.Count == 0) return false;
            bool first = true;
            foreach (var point in _singleEdge.PointsMm)
            {
                var world = transform.TransformPoint(ToLocalPoint(point));
                if (first) { worldBounds = new Bounds(world, Vector3.zero); first = false; } else worldBounds.Encapsulate(world);
            }
            return true;
        }
        private static Vector3 ToLocalPoint(CadPoint p) => CadCoordinates.ToLocal(p);
        public void ShowEdges(IEnumerable<DesignEdge> edges)
        {
            ClearContent();
            _content = new GameObject("Selected edges"); _content.transform.SetParent(transform, false);
            int count = 0; DesignEdge only = null;
            if (!_selectedEdgeMaterialReady) _selectedEdgeMaterial = SelectedEdgeMaterial();
            foreach (var edge in edges)
            {
                // A sketch profile preview ("profile:n") is not a selection: it keeps the neutral line.
                bool selected = _selectedEdgeMaterial != null && !edge.Id.StartsWith("profile:");
                CadCoordinates.Line(_content.transform, edge.Id, edge.PointsMm, selected ? _selectedEdgeMaterial : _material,
                    selected ? UiTheme.SelectionLineWidth : 0.002f);
                count++; only = edge;
            }
            _singleEdge = count == 1 && !only.Id.StartsWith("profile:") ? only : null;
        }
        private Material _selectedEdgeMaterial;
        private bool _selectedEdgeMaterialReady;
        private DesignEdge _singleEdge;
        private Material SelectedEdgeMaterial()
        {
            _selectedEdgeMaterialReady = true;
            var shader = Shader.Find("XrSo/HighlightOverlay");
            if (shader == null) return null;
            var material = new Material(shader) { name = "Selected edge" };
            material.SetColor("_Color", UiTheme.SelectionOutline);
            material.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
            return material;
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
            if (_cursor == null) return; // Scene teardown can precede native editor cleanup.
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
        public void ArcCursor(SketchFrame frame,CadPoint center,double radius,double startDegrees,double sweepDegrees)
        {
            if(frame==null || radius<=0 || double.IsNaN(radius) || double.IsInfinity(radius)
                || double.IsNaN(startDegrees) || double.IsInfinity(startDegrees) || double.IsNaN(sweepDegrees) || double.IsInfinity(sweepDegrees))
            {_cursor.positionCount=0;return;}
            double sweep=Math.Max(-360,Math.Min(360,sweepDegrees));
            int segments=Math.Max(1,(int)Math.Ceiling(Math.Abs(sweep)/5));_cursor.positionCount=segments+1;
            for(int i=0;i<=segments;i++)
            {
                double angle=(startDegrees+sweep*i/segments)*Math.PI/180;
                var point=center+new CadPoint(radius*Math.Cos(angle),radius*Math.Sin(angle));
                if(Math.Abs(sweep)<1e-9 && i==0) point=center;
                _cursor.SetPosition(i,CadCoordinates.ToLocal(frame.ToModel(point)));
            }
        }
        public void SelectionWindow(SketchFrame frame,CadPoint first,CadPoint second,bool crossing)
        {
            if (_selectionWindow==null) return;
            if (frame==null) { _selectionWindow.positionCount=0;return; }
            var color=crossing ? new Color(.2f,1,.4f) : new Color(.3f,.65f,1);
            _selectionWindow.startColor=_selectionWindow.endColor=color;
            _windowProperties.SetColor("_BaseColor",color);_windowProperties.SetColor("_Color",color);
            _windowProperties.SetFloat("_UseVertexColor",0);_selectionWindow.SetPropertyBlock(_windowProperties);
            _selectionWindow.positionCount=5;
            _selectionWindow.SetPosition(0,CadCoordinates.ToLocal(frame.ToModel(first)));
            _selectionWindow.SetPosition(1,CadCoordinates.ToLocal(frame.ToModel(new CadPoint(second.X,first.Y))));
            _selectionWindow.SetPosition(2,CadCoordinates.ToLocal(frame.ToModel(second)));
            _selectionWindow.SetPosition(3,CadCoordinates.ToLocal(frame.ToModel(new CadPoint(first.X,second.Y))));
            _selectionWindow.SetPosition(4,_selectionWindow.GetPosition(0));
        }
        public void Clear() { ClearContent(); ClearErrorContext(); if (_cursor != null) _cursor.positionCount = 0; if (_selectionWindow!=null) _selectionWindow.positionCount=0; }
        private void ClearContent(bool preserveConstraints = false)
        {
            if(!preserveConstraints) { _constraintLayer?.Clear(); _imageLayer?.Clear(); _textLayer?.Clear(); }
            _singleEdge = null;
            if (_content == null) return; _content.SetActive(false);
            if (Application.isPlaying) Destroy(_content); else DestroyImmediate(_content);
            _content = null;
        }
        private void OnDisable() => Clear();
        private void OnDestroy()
        {
            _imageLayer?.Dispose();
            _textLayer?.Dispose();
            foreach (var material in new[] { _errorMaterial, _selectedEdgeMaterial })
            {
                if (material == null) continue;
                if (Application.isPlaying) Destroy(material); else DestroyImmediate(material);
            }
        }
    }
}
