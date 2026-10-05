using System;
using InventorXrSo.Core.Input;

namespace InventorXrSo.Xr.Input
{
    /// <summary>
    /// M9: the single place that routes <see cref="XrInput"/> events through <see cref="InputMap"/>. For every key it looks up
    /// <c>InputMap.Lookup(state, key)</c> with the state given by <see cref="StateProbe"/> and raises <see cref="Invoked"/> only
    /// when the key has a binding in that state: a key without a binding does nothing. Without a probe the state is Rest.
    /// B belongs to PushToTalkInput and is never routed here.
    /// </summary>
    public sealed class InputDispatcher : IDisposable
    {
        /// <summary>Notch of the stick-right view rotation at rest (spec M9 §3).</summary>
        public const float RotateStepDegrees = 15f;

        private readonly XrInput _input;
        private readonly Action<int> _tab;
        private readonly Action<float> _zoom;
        private readonly Action<bool> _two, _precision;
        private readonly Action<Key> _keyPressed;
        private readonly Action<Key, int> _flick;
        private readonly Action _pressed, _grab, _back, _tap, _held;

        public InputDispatcher(XrInput input)
        {
            _input = input;
            _pressed = () => Raise(Key.Trigger, 0);
            _grab = () => Raise(Key.Grip, 0);
            _back = RaiseBack;
            _tap = () => Raise(Key.X, 0, suggestOnly: true);
            _held = () => InvokeSecondary(Key.X);
            _tab = d => Raise(Key.StickLeftH, d);
            _zoom = z => Raise(Key.StickLeftV, z > 0 ? 1 : -1);
            _two = on => Raise(Key.TwoGrips, on ? 1 : 0);
            _precision = on => Raise(Key.TriggerLeft, on ? 1 : 0);
            _keyPressed = k => Raise(k, 0);
            _flick = (k, d) => Raise(k, d);
            if (input == null) return;
            input.PenPressed += _pressed;
            input.PenGrabStarted += _grab;
            input.Back += _back;
            input.BackTapped += _tap;
            input.BackHeld += _held;
            input.TabDelta += _tab;
            input.Zoom += _zoom;
            input.TwoHandChanged += _two;
            input.PrecisionChanged += _precision;
            input.KeyPressed += _keyPressed;
            input.AxisFlick += _flick;
        }

        /// <summary>Current state (priority keypad > armed/handle > sketch > component > rest). Set by AppController.</summary>
        public Func<InputState> StateProbe { get; set; }

        public InputState State => StateProbe != null ? StateProbe() : InputState.Rest;

        /// <summary>(action, argument): argument is the direction (+1/-1) of a stick, 1/0 for held controls, else 0.</summary>
        public event Action<InputAction, int> Invoked;

        public void Dispose()
        {
            if (_input == null) return;
            _input.PenPressed -= _pressed;
            _input.PenGrabStarted -= _grab;
            _input.Back -= _back;
            _input.BackTapped -= _tap;
            _input.BackHeld -= _held;
            _input.TabDelta -= _tab;
            _input.Zoom -= _zoom;
            _input.TwoHandChanged -= _two;
            _input.PrecisionChanged -= _precision;
            _input.KeyPressed -= _keyPressed;
            _input.AxisFlick -= _flick;
        }

        /// <summary>The binding of <paramref name="key"/> in the current state, or null.</summary>
        public InputBinding Binding(Key key) => InputMap.Lookup(State, key);

        /// <summary>
        /// Time-threshold action of a key (double Trigger, held X): raises the binding's Secondary when it has one in the current
        /// state. The double-Trigger recognition itself (same target, ray angle) stays in the workspace, which calls this.
        /// </summary>
        public bool InvokeSecondary(Key key)
        {
            var binding = Binding(key);
            if (binding == null || binding.Secondary == InputAction.None) return false;
            Invoked?.Invoke(binding.Secondary, 0);
            return true;
        }

        private void Raise(Key key, int arg, bool suggestOnly = false)
        {
            var binding = Binding(key);
            if (binding == null) return;
            if (suggestOnly && binding.Action != InputAction.Suggest) return;
            Invoked?.Invoke(binding.Action, arg);
        }

        // XrInput.Back fires on X away from rest: the Back chain (Annulla in the keypad). A Rest binding (Suggest) still means Back here.
        private void RaiseBack()
        {
            var binding = Binding(Key.X);
            if (binding == null) return;
            Invoked?.Invoke(binding.Action == InputAction.Suggest ? InputAction.Back : binding.Action, 0);
        }
    }
}
