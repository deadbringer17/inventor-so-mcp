using System;
using System.Collections.Generic;
using System.Linq;
using InventorXrSo.Core.Backend;
using InventorXrSo.Unity.Ui;
using UnityEngine;

namespace InventorXrSo.Unity.Scene
{
    /// <summary>Pooled planar constraint labels. Does not mutate CAD or claim desktop glyph equivalence.</summary>
    public sealed class NativeSketchConstraintLayer : MonoBehaviour, IDisposable
    {
        private readonly Dictionary<string,NativeSketchConstraintLabelView> _pool = new Dictionary<string,NativeSketchConstraintLabelView>(StringComparer.Ordinal);
        private readonly Dictionary<string,LineRenderer> _leaders = new Dictionary<string,LineRenderer>(StringComparer.Ordinal);
        private Transform _model;
        private Material _leaderMaterial;
        private NativeSketchConstraintLabelView _measurement;
        private NativeSketchSnapshot _snapshot;
        private SketchEntityRef _selected;
        private bool _showAll = true, _disposed;
        private string[] _types;
        private int _maxVisible = 256;
        private Matrix4x4 _modelMatrix, _parentMatrix;
        public NativeSketchConstraintGlyphResult Layout { get; private set; }
        public string UnavailableReason { get; private set; } = "";
        public int VisibleCount => _pool.Values.Count(v=>v != null && v.IsUsable);
        /// <summary>At most 512 pooled labels, plus a single inactive footprint measurement label.</summary>
        public int PooledCount => _pool.Count;
        public int CreatedLabelCount { get; private set; }
        public int VisibleLeaderCount => _leaders.Values.Count(line=>line != null && line.gameObject.activeInHierarchy);

        /// <summary>The optional leader material is borrowed and never destroyed or modified by this layer.</summary>
        public static NativeSketchConstraintLayer Create(Transform parent,Transform model,Material leaderMaterial = null)
        {
            if(parent == null) throw new ArgumentNullException(nameof(parent));
            if(model == null) throw new ArgumentNullException(nameof(model));
            var go = new GameObject("Native constraint layer"); go.transform.SetParent(parent,false);
            var view = go.AddComponent<NativeSketchConstraintLayer>(); view._model = model; view._leaderMaterial = leaderMaterial; return view;
        }

        public void Synchronize(NativeSketchSnapshot snapshot,SketchEntityRef selected = null,bool showAll = true,
            IEnumerable<string> nativeTypes = null,int maxVisible = 256)
        {
            if(_disposed) throw new ObjectDisposedException(nameof(NativeSketchConstraintLayer));
            if(snapshot == null) { Clear(); return; }
            var validated = new NativeSketchConstraintGlyphOptions(maxVisible:maxVisible,showAll:showAll,nativeTypes:nativeTypes);
            var types = validated.NativeTypes?.ToArray();
            bool same = ReferenceEquals(snapshot,_snapshot) && SameReference(selected,_selected) && showAll == _showAll
                && maxVisible == _maxVisible && ((_types == null && types == null) || (_types != null && types != null && _types.SequenceEqual(types)));
            if(_snapshot != null && (_snapshot.State.DocumentId != snapshot.State.DocumentId || _snapshot.SketchId != snapshot.SketchId)) DestroyPool();
            _snapshot = snapshot; _selected = selected; _showAll = showAll; _types = types; _maxVisible = maxVisible;
            if(!same || TransformsChanged()) Rebuild();
        }

        private static bool SameReference(SketchEntityRef a,SketchEntityRef b) => ReferenceEquals(a,b) || a != null && b != null
            && a.DocumentId == b.DocumentId && a.Revision == b.Revision && a.SketchId == b.SketchId
            && a.EntityId == b.EntityId && a.Type == b.Type && a.Subpart == b.Subpart;
        private bool TransformsChanged() => _model == null || _model.localToWorldMatrix != _modelMatrix || transform.localToWorldMatrix != _parentMatrix;

        private void Rebuild()
        {
            Layout = null; UnavailableReason = "";
            foreach(var view in _pool.Values) if(view != null) view.gameObject.SetActive(false);
            foreach(var line in _leaders.Values) if(line != null) line.gameObject.SetActive(false);
            if(_snapshot == null || _model == null) { UnavailableReason = "Sketch label model is unavailable."; return; }
            _modelMatrix = _model.localToWorldMatrix; _parentMatrix = transform.localToWorldMatrix;
            if(_measurement == null)
                _measurement = NativeSketchConstraintLabelView.Create(transform,_model,_snapshot.Frame,new CadPoint(0,0),"");
            _measurement.gameObject.SetActive(true);
            _measurement.SetAnchor(_snapshot.Frame,new CadPoint(0,0));
            bool measured = _measurement.TryGetSketchFootprint(out var minimum,out var maximum);
            _measurement.gameObject.SetActive(false);
            double width = maximum.X-minimum.X, height = maximum.Y-minimum.Y;
            if(!measured || width <= 0 || height <= 0 || width > 100000 || height > 100000
                || double.IsNaN(width) || double.IsNaN(height) || double.IsInfinity(width) || double.IsInfinity(height))
            { UnavailableReason = "Actual planar label footprint is degenerate, sheared or exceeds the CAD layout budget."; return; }
            Layout = NativeSketchConstraintGlyphLayout.Build(_snapshot,new NativeSketchConstraintGlyphOptions(
                _maxVisible,width,height,1,_showAll,_types),_selected);
            var currentIds = new HashSet<string>(_snapshot.Constraints.Where(c=>c.Reference != null).Select(c=>c.Reference.EntityId),StringComparer.Ordinal);
            foreach(var id in _pool.Keys.Where(id=>!currentIds.Contains(id)).ToArray()) Remove(id);
            var visible = Layout.Entries.Where(e=>e.State == NativeSketchConstraintGlyphState.Visible).ToArray();
            var wanted = new HashSet<string>(visible.Select(e=>e.Reference.EntityId),StringComparer.Ordinal);
            foreach(var glyph in visible)
            {
                string id = glyph.Reference.EntityId;
                if(!_pool.TryGetValue(id,out var view))
                {
                    if(_pool.Count >= NativeSketchConstraintGlyphOptions.MaximumVisible)
                        Remove(_pool.Keys.Where(key=>!wanted.Contains(key)).OrderBy(key=>key,StringComparer.Ordinal).First());
                    view = NativeSketchConstraintLabelView.Create(transform,_model,_snapshot.Frame,glyph.PositionMm.Value,glyph.Label);
                    _pool.Add(id,view); CreatedLabelCount++;
                }
                view.gameObject.SetActive(true);
                view.SetContent(glyph.Label,new Vector2(48,12));
                view.SetAnchor(_snapshot.Frame,glyph.PositionMm.Value);
                UpdateLeader(glyph,view.IsUsable);
            }
        }

        private void UpdateLeader(NativeSketchConstraintGlyph glyph,bool visible)
        {
            if(!visible || _leaderMaterial == null || !glyph.AnchorMm.HasValue || !glyph.PositionMm.HasValue
                || (glyph.PositionMm.Value-glyph.AnchorMm.Value).Length <= 1e-9) return;
            string id = glyph.Reference.EntityId;
            if(!_leaders.TryGetValue(id,out var line))
            {
                var go = new GameObject("Constraint label leader"); go.transform.SetParent(transform,false);
                line = go.AddComponent<LineRenderer>(); line.useWorldSpace = true; line.positionCount = 2;
                line.sharedMaterial = _leaderMaterial; line.widthMultiplier = .0007f;
                line.startColor = line.endColor = UiTheme.Signal; _leaders.Add(id,line);
            }
            line.gameObject.SetActive(true);
            line.SetPosition(0,_model.TransformPoint(CadCoordinates.ToLocal(_snapshot.Frame.ToModel(glyph.AnchorMm.Value))));
            line.SetPosition(1,_model.TransformPoint(CadCoordinates.ToLocal(_snapshot.Frame.ToModel(glyph.PositionMm.Value))));
        }

        /// <summary>All actually displayed ray hits, ordered by physical depth then opaque identity.</summary>
        public IReadOnlyList<SketchEntityRef> PickTargets(NativeSketchSnapshot current,Ray ray)
        {
            if(_disposed || !isActiveAndEnabled || !ReferenceEquals(current,_snapshot) || current == null) return Array.Empty<SketchEntityRef>();
            if(TransformsChanged()) Rebuild();
            if(Layout == null) return Array.Empty<SketchEntityRef>();
            var hits = new List<(SketchEntityRef reference,float distance)>();
            foreach(var glyph in Layout.Entries.Where(e=>e.State == NativeSketchConstraintGlyphState.Visible))
                if(glyph.Constraint.Selectable && glyph.Reference.Matches(current.State,current.SketchId) && _pool.TryGetValue(glyph.Reference.EntityId,out var view)
                    && view.TryHit(ray,out float distance,out _)) hits.Add((glyph.Reference,distance));
            return hits.OrderBy(h=>h.distance).ThenBy(h=>h.reference.EntityId,StringComparer.Ordinal).Select(h=>h.reference).ToArray();
        }
        public bool TryPick(NativeSketchSnapshot current,Ray ray,out SketchEntityRef reference)
        { reference = PickTargets(current,ray).FirstOrDefault(); return reference != null; }
        public bool TryGetLabel(string entityId,out NativeSketchConstraintLabelView view) => _pool.TryGetValue(entityId,out view);
        public bool TryGetLeader(string entityId,out LineRenderer line) => _leaders.TryGetValue(entityId,out line);

        private void LateUpdate() { if(!_disposed && _snapshot != null && TransformsChanged()) Rebuild(); }
        private void Remove(string id)
        {
            if(_pool.TryGetValue(id,out var view)) { _pool.Remove(id); if(view != null) view.Dispose(); }
            if(_leaders.TryGetValue(id,out var line))
            {
                _leaders.Remove(id);
                if(line != null) { line.gameObject.SetActive(false); if(Application.isPlaying) Destroy(line.gameObject); else DestroyImmediate(line.gameObject); }
            }
        }
        private void DestroyPool() { foreach(var id in _pool.Keys.ToArray()) Remove(id); }
        public void Clear()
        {
            DestroyPool(); _snapshot = null; _selected = null; Layout = null; UnavailableReason = "";
            if(_measurement != null) { _measurement.Dispose(); _measurement = null; }
        }
        public void Dispose()
        {
            if(_disposed) return; _disposed = true; Clear(); gameObject.SetActive(false);
            if(Application.isPlaying) Destroy(gameObject); else DestroyImmediate(gameObject);
        }
        private void OnDestroy() { _disposed = true; _pool.Clear(); _leaders.Clear(); _snapshot = null; Layout = null; }
    }
}
