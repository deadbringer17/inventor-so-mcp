using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace InventorXrSo.Unity.Scene
{
    public enum OccurrenceVisibility { Normal, Ghost, Hidden }

    /// <summary>
    /// Ispeziona visibility (spec M7): per scene occurrence Normal, Ghost (translucent, still hit by the ray) or Hidden (renderer and
    /// collider off, the ray passes through). View state only: Inventor is never touched. A scene rebuild forgets every state.
    /// </summary>
    public sealed class ComponentVisibility : MonoBehaviour
    {
        public static readonly Color GhostColor = new Color(0.72f, 0.74f, 0.77f, 0.2f);

        private readonly Dictionary<string, OccurrenceVisibility> _states = new Dictionary<string, OccurrenceVisibility>();
        private CadSceneView _view;
        private Material _material;
        private GhostBodies _ghosts;

        public event Action Changed;
        public bool AnyChanged => _states.Count > 0;

        public void Initialize(CadSceneView view)
        {
            if (_view != null) throw new InvalidOperationException("Visibility is already initialized.");
            _view = view ?? throw new ArgumentNullException(nameof(view));
            _material = GhostBodies.CreateMaterial("Visibility ghost", GhostColor);
            _ghosts = new GhostBodies(_material);
            _view.Rebuilt += OnRebuilt;
        }

        public OccurrenceVisibility Get(string occurrenceId) =>
            occurrenceId != null && _states.TryGetValue(occurrenceId, out var state) ? state : OccurrenceVisibility.Normal;

        public void XRay(IEnumerable<string> occurrenceIds) => Set(occurrenceIds, OccurrenceVisibility.Ghost);
        public void Hide(IEnumerable<string> occurrenceIds) => Set(occurrenceIds, OccurrenceVisibility.Hidden);

        /// <summary>The given occurrences stay Normal, every other one becomes a ghost.</summary>
        public void Isolate(IEnumerable<string> keep)
        {
            var kept = new HashSet<string>(keep ?? Enumerable.Empty<string>());
            foreach (var instance in _view.Instances)
                if (instance != null) Apply(instance.OccurrenceId, kept.Contains(instance.OccurrenceId) ? OccurrenceVisibility.Normal : OccurrenceVisibility.Ghost);
            Changed?.Invoke();
        }

        public void ShowAll()
        {
            bool had = _states.Count > 0;
            foreach (var id in _states.Keys.ToList()) Apply(id, OccurrenceVisibility.Normal);
            if (had) Changed?.Invoke();
        }

        public IReadOnlyDictionary<string, OccurrenceVisibility> Snapshot() => new Dictionary<string, OccurrenceVisibility>(_states);

        public void Restore(IReadOnlyDictionary<string, OccurrenceVisibility> snapshot)
        {
            foreach (var id in _states.Keys.ToList()) Apply(id, OccurrenceVisibility.Normal);
            if (snapshot != null) foreach (var pair in snapshot) Apply(pair.Key, pair.Value);
            Changed?.Invoke();
        }

        private void Set(IEnumerable<string> occurrenceIds, OccurrenceVisibility state)
        {
            bool any = false;
            foreach (var id in occurrenceIds ?? Enumerable.Empty<string>()) any |= Apply(id, state);
            if (any) Changed?.Invoke();
        }

        private bool Apply(string occurrenceId, OccurrenceVisibility state)
        {
            var instance = _view?.Find(occurrenceId);
            if (instance == null) { _states.Remove(occurrenceId ?? ""); return false; }
            foreach (var body in instance.Bodies)
            {
                _ghosts.Remove(body);
                if (body.Renderer != null) body.Renderer.enabled = state == OccurrenceVisibility.Normal;
                var collider = body.GetComponent<Collider>();
                if (collider != null) collider.enabled = state != OccurrenceVisibility.Hidden;
                if (state == OccurrenceVisibility.Ghost) _ghosts.Add(body);
            }
            if (state == OccurrenceVisibility.Normal) _states.Remove(occurrenceId); else _states[occurrenceId] = state;
            return true;
        }

        // The instances are already destroyed: the states no longer mean anything.
        private void OnRebuilt()
        {
            _ghosts.Forget();
            bool had = _states.Count > 0;
            _states.Clear();
            if (had) Changed?.Invoke();
        }

        private void OnDestroy()
        {
            if (_view != null) _view.Rebuilt -= OnRebuilt;
            _ghosts?.Clear();
            if (_material != null) { if (Application.isPlaying) Destroy(_material); else DestroyImmediate(_material); }
        }
    }
}
