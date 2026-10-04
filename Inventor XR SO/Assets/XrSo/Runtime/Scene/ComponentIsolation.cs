using System;
using System.Linq;
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Ui;
using UnityEngine;

namespace InventorXrSo.Unity.Scene
{
    /// <summary>
    /// Isolamento di un componente in Assieme (spec M6): il componente avanza a meta strada verso l'utente
    /// (<see cref="WorkbenchLayout.Isolated"/>) e il resto dell'assieme resta al 20% di opacita. Solo visivo:
    /// si muove il Transform dell'istanza nella scena locale, mai l'occorrenza in Inventor. Il rilascio riporta
    /// il componente alla posa di partenza con la stessa transizione di ~250 ms.
    /// </summary>
    public sealed class ComponentIsolation : MonoBehaviour
    {
        public const float FadeAlpha = 0.2f, DefaultSeconds = PoseTween.DefaultSeconds;
        private static readonly Color FadeColor = new Color(0.72f, 0.74f, 0.77f, FadeAlpha);

        private CadSceneView _view;
        private Material _fade;
        private CadInstance _instance;
        private Vector3 _fromLocal, _toLocal, _homeLocal;
        private float _elapsed, _duration;
        private bool _tweening;
        private GhostBodies _ghosts;

        /// <summary>Occorrenza isolata; null se nessuna (anche mentre il componente torna al suo posto).</summary>
        public string OccurrenceId { get; private set; }
        public bool Active => OccurrenceId != null;
        public bool Tweening => _tweening;
        /// <summary>Posizione locale dell'istanza prima dell'isolamento.</summary>
        public Vector3 HomeLocalPosition => _homeLocal;
        public int FadedBodies => _ghosts?.Count ?? 0;
        public event Action Changed;

        public void Initialize(CadSceneView view)
        {
            if (_view != null) throw new InvalidOperationException("Isolation is already initialized.");
            if (view == null) throw new ArgumentNullException(nameof(view));
            _fade = GhostBodies.CreateMaterial("Isolation fade", FadeColor);
            _ghosts = new GhostBodies(_fade, "IsolationGhost");
            _view = view;
            _view.Rebuilt += OnRebuilt;
        }

        public bool IsFaded(string occurrenceId)
        {
            if (!Active || occurrenceId == OccurrenceId) return false;
            var instance = _view.Find(occurrenceId);
            return instance != null && instance.Bodies.Any(_ghosts.Contains);
        }

        /// <summary>False se l'occorrenza non e nella scena. Riisolare lo stesso componente non fa nulla.</summary>
        public bool Isolate(string occurrenceId, WorkbenchFrame frame, float seconds = DefaultSeconds)
        {
            if (_view == null || frame == null || string.IsNullOrEmpty(occurrenceId)) return false;
            if (Active && OccurrenceId == occurrenceId) return true;
            var instance = _view.Find(occurrenceId);
            if (instance == null || instance.Bodies.Count == 0) return false;
            if (Active) Release(true);

            var root = _view.transform;
            var local = ScenePlacement.LocalBounds(instance.transform);
            var center = instance.transform.TransformPoint(local.center);
            var pose = WorkbenchLayout.Isolated(frame, new CadPoint(center.x, center.y, center.z), root.lossyScale.x);
            var target = new Vector3((float)pose.Position.X, (float)pose.Position.Y, (float)pose.Position.Z);
            var parent = instance.transform.parent;
            var targetWorld = instance.transform.position + (target - center);

            // Se il componente sta ancora tornando a casa, la sua casa resta quella di prima.
            if (!(_tweening && _instance == instance)) _homeLocal = instance.transform.localPosition;
            _instance = instance;
            OccurrenceId = occurrenceId;
            _fromLocal = instance.transform.localPosition;
            _toLocal = parent != null ? parent.InverseTransformPoint(targetWorld) : targetWorld;
            StartTween(seconds);

            foreach (var other in _view.Instances)
            {
                if (other == null || other == instance) continue;
                foreach (var body in other.Bodies) _ghosts.Add(body);
            }
            Changed?.Invoke();
            return true;
        }

        /// <summary>Riporta il componente al suo posto e ridà l'opacita piena al resto.</summary>
        public void Release(bool immediate = false, float seconds = DefaultSeconds)
        {
            if (!Active) return;
            OccurrenceId = null;
            _ghosts.Clear();
            if (_instance != null)
            {
                _fromLocal = _instance.transform.localPosition;
                _toLocal = _homeLocal;
                StartTween(immediate ? 0f : seconds);
            }
            Changed?.Invoke();
        }

        /// <summary>Salta alla fine della transizione (test e uscita).</summary>
        public void Snap()
        {
            if (!_tweening) return;
            Finish();
        }

        private void StartTween(float seconds)
        {
            _elapsed = 0; _duration = seconds;
            _tweening = true;
            if (seconds <= 0) Finish();
        }

        private void Finish()
        {
            _tweening = false;
            if (_instance != null) _instance.transform.localPosition = _toLocal;
            if (!Active) _instance = null;
        }

        private void Update()
        {
            if (!_tweening) return;
            if (_instance == null) { _tweening = false; return; }
            _elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(_elapsed / _duration);
            if (t >= 1f) { Finish(); return; }
            float e = t * t * (3f - 2f * t);
            _instance.transform.localPosition = Vector3.Lerp(_fromLocal, _toLocal, e);
        }

        // La scena e stata ricostruita: le istanze sono gia distrutte, lo stato di isolamento non ha piu senso.
        private void OnRebuilt()
        {
            bool was = Active || _tweening;
            _ghosts.Forget();
            OccurrenceId = null; _instance = null; _tweening = false;
            if (was) Changed?.Invoke();
        }

        private void OnDestroy()
        {
            if (_view != null) _view.Rebuilt -= OnRebuilt;
            if (Active) Release(true);
            if (_fade != null) { if (Application.isPlaying) Destroy(_fade); else DestroyImmediate(_fade); }
        }
    }
}
