using UnityEngine;

namespace InventorXrSo.Unity.Scene
{
    /// <summary>Transizione di posa (posizione, rotazione, scala uniforme) in tempo non scalato; nessun salto istantaneo.</summary>
    public sealed class PoseTween
    {
        public const float DefaultSeconds = 0.25f;
        private Transform _target;
        private Vector3 _fromPos, _toPos;
        private Quaternion _fromRot, _toRot;
        private float _fromScale, _toScale, _elapsed, _duration;

        public bool Active { get; private set; }

        public void Start(Transform target, Vector3 position, Quaternion rotation, float scale, float seconds = DefaultSeconds)
        {
            _target = target;
            _fromPos = target.position; _fromRot = target.rotation; _fromScale = target.localScale.x;
            _toPos = position; _toRot = rotation; _toScale = scale;
            _elapsed = 0; _duration = seconds;
            Active = true;
            if (seconds <= 0) Snap();
        }

        /// <summary>Avanza; true finche la transizione e in corso.</summary>
        public bool Tick(float deltaSeconds)
        {
            if (!Active) return false;
            if (_target == null) { Active = false; return false; }
            _elapsed += deltaSeconds;
            float t = Mathf.Clamp01(_elapsed / _duration);
            if (t >= 1f) { Snap(); return false; }
            float e = t * t * (3f - 2f * t);
            _target.SetPositionAndRotation(Vector3.Lerp(_fromPos, _toPos, e), Quaternion.Slerp(_fromRot, _toRot, e));
            _target.localScale = Vector3.one * Mathf.Lerp(_fromScale, _toScale, e);
            return true;
        }

        /// <summary>Salta alla posa finale (test e uscita).</summary>
        public void Snap()
        {
            if (!Active) return;
            Active = false;
            if (_target == null) return;
            _target.SetPositionAndRotation(_toPos, _toRot);
            _target.localScale = Vector3.one * _toScale;
        }

        public void Cancel() => Active = false;
    }
}
