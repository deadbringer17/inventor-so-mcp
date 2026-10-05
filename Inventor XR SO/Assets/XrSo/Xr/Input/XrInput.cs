using System;
using InventorXrSo.Core.Input;
using UnityEngine;

namespace InventorXrSo.Xr.Input
{
    /// <summary>
    /// Unico lettore dei controller per la UI M6: trasforma lo stato grezzo in eventi semantici
    /// (spec M6, mappatura dei controller). B resta a PushToTalkInput.
    /// </summary>
    public sealed class XrInput : MonoBehaviour
    {
        public const float FlickOn = 0.6f, FlickOff = 0.3f, ZoomDeadZone = 0.2f, BackHoldSeconds = 1f;

        private XrInputFrame _last;
        private bool _penStickX, _penStickY, _paletteStickX;
        private float _xDownAt = -1;
        private bool _xRest, _xHeld;
        private Action<int> _flickH, _flickV;

        public IXrInputSource Source { get; set; } = new OvrInputSource();
        public bool Synthetic => Source != null && Source.Synthetic;
        public bool PenHeld { get; private set; }
        public bool Precision { get; private set; }
        public bool TwoHand { get; private set; }
        /// <summary>Controller destro (penna) connesso e tracciato nell'ultimo frame letto.</summary>
        public bool PenTracked { get; private set; }
        public bool PaletteTracked { get; private set; }
        /// <summary>Grip destro tenuto (e tracciato): serve ai workspace per distinguere "solo vista" dal trigger.</summary>
        public bool PenGripHeld { get; private set; }

        /// <summary>
        /// Decides, at the X press, whether the user is at rest (no ring, keypad, tab group, draft/command, isolation). At rest X
        /// is tap/hold: <see cref="BackTapped"/> (released before <see cref="BackHoldSeconds"/>) or <see cref="BackHeld"/> (Torna).
        /// Not at rest (or no probe) X raises <see cref="Back"/> on press, exactly as before, and never Torna.
        /// </summary>
        public Func<bool> RestingProbe { get; set; }
        /// <summary>0..1 progress of the held X toward Torna (for the legend); 0 when X is not being held at rest.</summary>
        public float BackHoldProgress { get; private set; }

        public event Action BackTapped, BackHeld;
        public event Action PenPressed, PenReleased, PenGrabStarted, PenGrabEnded, Back;
        public event Action<int> TabDelta;
        /// <summary>Raw press edge of A and Y (M9): what they DO depends on the state, so <see cref="InputDispatcher"/> routes them.</summary>
        public event Action<Key> KeyPressed;
        /// <summary>Right-stick flick (<see cref="Key.StickRightH"/> / <see cref="Key.StickRightV"/>), direction +1 / -1; routed by <see cref="InputDispatcher"/>.</summary>
        public event Action<Key, int> AxisFlick;
        /// <summary>Left trigger (precision) changed.</summary>
        public event Action<bool> PrecisionChanged;
        private InputDispatcher _dispatcher;
        /// <summary>The single place that gates keys by <see cref="InputMap"/>; owned here so every workspace shares it. AppController sets its StateProbe.</summary>
        public InputDispatcher Dispatcher => _dispatcher ?? (_dispatcher = new InputDispatcher(this));
        public event Action<float> Zoom;
        public event Action<bool> TwoHandChanged;
        /// <summary>
        /// Il controller destro ha perso il tracking. Scatta PRIMA di <see cref="PenReleased"/> (se il trigger era tenuto), cosi
        /// i chiamanti distinguono il rilascio voluto dalla perdita e chiudono la cattura senza anteprima.
        /// </summary>
        public event Action TrackingLost;

        private void Update() { if (Source != null) Poll(Source.Read(), Time.unscaledTime); }

        public void Poll(XrInputFrame f, float time)
        {
            if (_last.PenTracked && !f.PenTracked) TrackingLost?.Invoke();
            PenTracked = f.PenTracked; PaletteTracked = f.PaletteTracked;
            bool grip = f.PenGrip && f.PenTracked;
            PenGripHeld = grip;   // before the Trigger event: a Grip pressed in the same frame already makes the Trigger view-only
            bool trigger = f.PenTrigger && f.PenTracked;
            if (trigger && !PenHeld) { PenHeld = true; PenPressed?.Invoke(); }
            else if (!trigger && PenHeld) { PenHeld = false; PenReleased?.Invoke(); }

            bool lastGrip = _last.PenGrip && _last.PenTracked;
            if (grip && !lastGrip) PenGrabStarted?.Invoke();
            else if (!grip && lastGrip) PenGrabEnded?.Invoke();

            bool two = grip && f.PaletteGrip && f.PaletteTracked;
            if (two != TwoHand) { TwoHand = two; TwoHandChanged?.Invoke(two); }
            bool precision = f.PaletteTrigger && f.PaletteTracked;
            if (precision != Precision) { Precision = precision; PrecisionChanged?.Invoke(precision); }

            if (f.X && !_last.X)
            {
                if (RestingProbe != null && RestingProbe()) { _xRest = true; _xHeld = false; _xDownAt = time; BackHoldProgress = 0; }
                else { _xRest = false; Back?.Invoke(); }
            }
            if (f.X && _xRest && !_xHeld)
            {
                BackHoldProgress = Mathf.Clamp01((time - _xDownAt) / BackHoldSeconds);
                if (time - _xDownAt >= BackHoldSeconds) { _xHeld = true; BackHoldProgress = 1; BackHeld?.Invoke(); }
            }
            if (!f.X && _last.X && _xRest)
            {
                _xRest = false; BackHoldProgress = 0;
                if (!_xHeld) BackTapped?.Invoke();
                _xHeld = false;
            }
            if (f.A && !_last.A) KeyPressed?.Invoke(Key.A);
            if (f.Y && !_last.Y) KeyPressed?.Invoke(Key.Y);

            Flick(f.PenStick.x, ref _penStickX, _flickH ?? (_flickH = d => AxisFlick?.Invoke(Key.StickRightH, d)));
            Flick(f.PenStick.y, ref _penStickY, _flickV ?? (_flickV = d => AxisFlick?.Invoke(Key.StickRightV, d)));
            Flick(f.PaletteStick.x, ref _paletteStickX, TabDelta);
            if (Mathf.Abs(f.PaletteStick.y) > ZoomDeadZone) Zoom?.Invoke(f.PaletteStick.y);

            _last = f;
        }

        private static void Flick(float axis, ref bool armed, Action<int> raise)
        {
            if (!armed && Mathf.Abs(axis) >= FlickOn) { armed = true; raise?.Invoke(axis > 0 ? 1 : -1); }
            else if (armed && Mathf.Abs(axis) <= FlickOff) armed = false;
        }
    }
}
