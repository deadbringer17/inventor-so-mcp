using System;
using System.Collections.Generic;
using InventorXrSo.Core.Input;
using InventorXrSo.Xr;
using InventorXrSo.Xr.Input;
using NUnit.Framework;
using UnityEngine;

namespace InventorXrSo.Tests
{
    /// <summary>M9 Task 13 (gate M9-06): XrInput raw events pass through InputMap in the current state; unbound keys do nothing.</summary>
    public class InputDispatcherTests
    {
        private static readonly InputState[] States =
            { InputState.Rest, InputState.ComponentSelected, InputState.SketchOpen, InputState.ArmedOrHandle, InputState.Keypad };

        private GameObject _go;
        private XrInput _input;
        private InputState _state;
        private float _clock;
        private readonly List<string> _log = new List<string>();
        private readonly List<HapticPulse> _haptics = new List<HapticPulse>();
        private Action<HapticPulse, bool> _previousSink;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("input");
            _input = _go.AddComponent<XrInput>();
            _input.Source = new SyntheticInputSource();
            _state = InputState.Rest;
            _input.Dispatcher.StateProbe = () => _state;
            _input.Dispatcher.Invoked += (a, d) => _log.Add(a + ":" + d);
            _input.RestingProbe = () => _state == InputState.Rest;
            _previousSink = Haptics.Sink;
            Haptics.Sink = (pulse, pen) => _haptics.Add(pulse);
        }

        [TearDown]
        public void TearDown()
        {
            Haptics.Sink = _previousSink;
            UnityEngine.Object.DestroyImmediate(_go);
            _log.Clear(); _haptics.Clear();
        }

        private static XrInputFrame F() => new XrInputFrame { PenTracked = true, PaletteTracked = true };
        private void Poll(XrInputFrame f) => _input.Poll(f, _clock += 0.016f);
        private void Rest() => Poll(F());
        private string[] Log() => _log.ToArray();

        /// <summary>Presses/flicks the control of <paramref name="key"/> for one frame (the caller lets go).</summary>
        private void Drive(Key key, int dir = 1)
        {
            var f = F();
            switch (key)
            {
                case Key.Trigger: f.PenTrigger = true; break;
                case Key.Grip: f.PenGrip = true; break;
                case Key.StickRightH: f.PenStick = new Vector2(0.9f * dir, 0); break;
                case Key.StickRightV: f.PenStick = new Vector2(0, 0.9f * dir); break;
                case Key.A: f.A = true; break;
                case Key.TriggerLeft: f.PaletteTrigger = true; break;
                case Key.StickLeftH: f.PaletteStick = new Vector2(0.9f * dir, 0); break;
                case Key.StickLeftV: f.PaletteStick = new Vector2(0, 0.9f * dir); break;
                case Key.X: f.X = true; break;
                case Key.Y: f.Y = true; break;
                case Key.TwoGrips: f.PenGrip = true; f.PaletteGrip = true; break;
                default: Assert.Fail("not a physical control: " + key); break;
            }
            Poll(f);
        }

        // ---- the whole table: every state x key routes to InputMap's action, unbound keys do nothing

        [Test]
        public void EveryRowOfTheTableYieldsItsActionInItsStateAndUnboundKeysDoNothing()
        {
            foreach (var state in States)
            foreach (Key key in Enum.GetValues(typeof(Key)))
            {
                if (key == Key.B || key == Key.X) continue;   // B: PushToTalkInput; X: threshold semantics, own tests below
                _state = state; _log.Clear();
                Rest();
                Drive(key);
                var expected = new List<string>();
                var binding = InputMap.Lookup(state, key);
                if (key == Key.TwoGrips)
                {
                    // the pen grip rising raises Grip (MoveView) first, then the two-hand change
                    var grip = InputMap.Lookup(state, Key.Grip);
                    if (grip != null) expected.Add(grip.Action + ":0");
                    if (binding != null) expected.Add(binding.Action + ":1");
                }
                else if (binding != null)
                {
                    bool stick = key == Key.StickRightH || key == Key.StickRightV || key == Key.StickLeftH || key == Key.StickLeftV;
                    expected.Add(binding.Action + ":" + (stick || key == Key.TriggerLeft ? 1 : 0));
                }
                CollectionAssert.AreEqual(expected, _log, state + " / " + key);
                Rest();
            }
        }

        [Test]
        public void SpecTableRowsAreExactInEveryState()
        {
            var expect = new Dictionary<(InputState, Key), InputAction>
            {
                [(InputState.Rest, Key.Trigger)] = InputAction.Select,
                [(InputState.ComponentSelected, Key.Trigger)] = InputAction.Select,
                [(InputState.SketchOpen, Key.Trigger)] = InputAction.Point,
                [(InputState.ArmedOrHandle, Key.Trigger)] = InputAction.Drag,
                [(InputState.Keypad, Key.Trigger)] = InputAction.PressKeys,
                [(InputState.Rest, Key.StickRightH)] = InputAction.RotateView,
                [(InputState.ComponentSelected, Key.StickRightH)] = InputAction.RotateView,
                [(InputState.SketchOpen, Key.StickRightH)] = InputAction.RotateView,
                [(InputState.ArmedOrHandle, Key.StickRightH)] = InputAction.StepChange,
                [(InputState.ArmedOrHandle, Key.StickRightV)] = InputAction.StepSize,
                [(InputState.ComponentSelected, Key.A)] = InputAction.Isolate,
                [(InputState.SketchOpen, Key.A)] = InputAction.SnapToggle,
                [(InputState.ArmedOrHandle, Key.A)] = InputAction.OpenKeypad,
                [(InputState.Keypad, Key.A)] = InputAction.KeypadOk,
                [(InputState.SketchOpen, Key.TriggerLeft)] = InputAction.Precision,
                [(InputState.ArmedOrHandle, Key.TriggerLeft)] = InputAction.Precision,
                [(InputState.Rest, Key.StickLeftH)] = InputAction.Tabs,
                [(InputState.Rest, Key.StickLeftV)] = InputAction.Zoom,
                [(InputState.Rest, Key.Y)] = InputAction.Fit,
                [(InputState.ArmedOrHandle, Key.Y)] = InputAction.Fit,
                [(InputState.Keypad, Key.Y)] = InputAction.None,
                [(InputState.Keypad, Key.StickLeftH)] = InputAction.None,
                [(InputState.Rest, Key.TriggerLeft)] = InputAction.None,
            };
            foreach (var kv in expect)
            {
                _state = kv.Key.Item1; _log.Clear();
                Rest(); Drive(kv.Key.Item2); Rest();
                if (kv.Value == InputAction.None) CollectionAssert.IsEmpty(_log, kv.Key.ToString());
                else
                {
                    Assert.IsNotEmpty(_log, kv.Key.ToString());
                    StringAssert.StartsWith(kv.Value + ":", _log[0], kv.Key.ToString());
                }
            }
        }

        [Test]
        public void AAtRestDoesNothingBecauseItHasNoBinding()
        {
            _state = InputState.Rest;
            Drive(Key.A); Rest();
            CollectionAssert.IsEmpty(_log);
        }

        [Test]
        public void StickRightVerticalOnlyActsWhenArmed()
        {
            foreach (var state in new[] { InputState.Rest, InputState.ComponentSelected, InputState.SketchOpen, InputState.Keypad })
            {
                _state = state; _log.Clear();
                Rest(); Drive(Key.StickRightV); Rest(); Drive(Key.StickRightV, -1); Rest();
                CollectionAssert.IsEmpty(_log, state.ToString());
            }
            _state = InputState.ArmedOrHandle;
            Rest(); Drive(Key.StickRightV); Rest(); Drive(Key.StickRightV, -1); Rest();
            CollectionAssert.AreEqual(new[] { "StepSize:1", "StepSize:-1" }, Log());
        }

        [Test]
        public void StickRightHorizontalIsStepWhenArmedAndNothingInTheKeypad()
        {
            _state = InputState.ArmedOrHandle; Rest(); Drive(Key.StickRightH, -1); Rest();
            CollectionAssert.AreEqual(new[] { "StepChange:-1" }, Log());
            _log.Clear(); _state = InputState.Keypad; Drive(Key.StickRightH); Rest();
            CollectionAssert.IsEmpty(_log);
        }

        // ---- A: the quick action of the context

        [Test]
        public void AIsTheQuickActionOfTheContext()
        {
            var expect = new[]
            {
                (InputState.ComponentSelected, "Isolate:0"), (InputState.SketchOpen, "SnapToggle:0"),
                (InputState.ArmedOrHandle, "OpenKeypad:0"), (InputState.Keypad, "KeypadOk:0"),
            };
            foreach (var (state, action) in expect)
            {
                _state = state; _log.Clear();
                Rest(); Drive(Key.A); Rest();
                CollectionAssert.AreEqual(new[] { action }, Log(), state.ToString());
            }
        }

        // ---- Y: Fit only

        [Test]
        public void YIsFitOnlyAndHoldingItNeverRecenters()
        {
            Drive(Key.Y);
            _clock += 3f;
            var held = F(); held.Y = true;
            _input.Poll(held, _clock);
            Rest();
            CollectionAssert.AreEqual(new[] { "Fit:0" }, Log());
            Assert.IsFalse(Enum.IsDefined(typeof(InputAction), "Recenter"), "Ricentra is not a controller action any more (Documento tab)");
        }

        // ---- X and double Trigger (threshold actions)

        [Test]
        public void XTapAtRestIsSuggestHeldIsTornaAndAwayFromRestItIsBackOrAnnulla()
        {
            _state = InputState.Rest;
            var x = F(); x.X = true;
            _input.Poll(x, 0); _input.Poll(F(), 0.2f);
            CollectionAssert.AreEqual(new[] { "Suggest:0" }, Log());
            _log.Clear();
            _input.Poll(x, 1); _input.Poll(x, 1.5f); _input.Poll(x, 2.1f); _input.Poll(F(), 2.2f);
            CollectionAssert.AreEqual(new[] { "BackHold:0" }, Log());
            foreach (var (state, action) in new[]
                { (InputState.ComponentSelected, "Back:0"), (InputState.SketchOpen, "Back:0"), (InputState.ArmedOrHandle, "Back:0"), (InputState.Keypad, "KeypadCancel:0") })
            {
                _state = state; _log.Clear();
                _input.Poll(x, 10); _input.Poll(F(), 10.1f);
                CollectionAssert.AreEqual(new[] { action }, Log(), state.ToString());
            }
        }

        [Test]
        public void DoubleTriggerIsTheSecondaryOfTriggerAtRestAndOnASelectedComponentOnly()
        {
            foreach (var state in new[] { InputState.Rest, InputState.ComponentSelected })
            {
                _state = state; _log.Clear();
                var detector = new DoubleTriggerDetector();
                Assert.IsFalse(detector.Press(0, "occ1", 0, 0, 1));
                Assert.IsTrue(detector.Press(0.2, "occ1", 0, 0, 1));
                Assert.IsTrue(_input.Dispatcher.InvokeSecondary(Key.Trigger), state.ToString());
                CollectionAssert.AreEqual(new[] { "DoubleSelect:0" }, Log());
            }
            foreach (var state in new[] { InputState.SketchOpen, InputState.ArmedOrHandle, InputState.Keypad })
            {
                _state = state; _log.Clear();
                Assert.IsFalse(_input.Dispatcher.InvokeSecondary(Key.Trigger), state.ToString());
                CollectionAssert.IsEmpty(_log);
            }
        }

        // ---- stick right at rest rotates the view by 15 degrees with a haptic tick per notch

        [Test]
        public void StickRightAtRestRotatesTheViewFifteenDegreesWithAHapticTickPerNotch()
        {
            Assert.AreEqual(15f, InputDispatcher.RotateStepDegrees);
            var bench = _go.AddComponent<Workbench>();
            var root = new GameObject("root").transform;
            try
            {
                root.position = new Vector3(1, 0.5f, 2);
                _input.Dispatcher.Invoked += (a, d) => { if (a == InputAction.RotateView) bench.RotateView(root, d); };
                _state = InputState.Rest;

                Rest(); Drive(Key.StickRightH); Rest();
                Assert.AreEqual(15f, Mathf.DeltaAngle(0, root.eulerAngles.y), 1e-3f);
                Drive(Key.StickRightH); Rest();
                Assert.AreEqual(30f, Mathf.DeltaAngle(0, root.eulerAngles.y), 1e-3f);
                Drive(Key.StickRightH, -1); Rest(); Drive(Key.StickRightH, -1); Rest(); Drive(Key.StickRightH, -1); Rest();
                Assert.AreEqual(-15f, Mathf.DeltaAngle(0, root.eulerAngles.y), 1e-3f);
                CollectionAssert.AreEqual(new[] { HapticPulse.Tick, HapticPulse.Tick, HapticPulse.Tick, HapticPulse.Tick, HapticPulse.Tick }, _haptics);
                Assert.AreEqual(new Vector3(1, 0.5f, 2), root.position, "only the orientation changes around the model centre");

                _haptics.Clear();
                _state = InputState.ArmedOrHandle;   // armed: the same flick is a step, never a rotation
                float yaw = root.eulerAngles.y;
                Drive(Key.StickRightH); Rest();
                Assert.AreEqual(yaw, root.eulerAngles.y, 1e-4f);
                CollectionAssert.IsEmpty(_haptics);
            }
            finally { UnityEngine.Object.DestroyImmediate(root.gameObject); }
        }

        [Test]
        public void RotateViewTurnsAroundTheVerticalAxisAndIgnoresNothingToRotate()
        {
            var bench = _go.AddComponent<Workbench>();
            var root = new GameObject("root").transform;
            try
            {
                root.position = new Vector3(0, 1, 0);
                root.rotation = Quaternion.Euler(0, 40, 0);
                Assert.IsTrue(bench.RotateView(root, 1));
                Assert.AreEqual(55f, Mathf.DeltaAngle(0, root.eulerAngles.y), 1e-3f);
                Assert.IsFalse(bench.RotateView(root, 0));
                Assert.IsFalse(bench.RotateView(null, 1));
            }
            finally { UnityEngine.Object.DestroyImmediate(root.gameObject); }
        }

        // ---- plumbing

        [Test]
        public void NoProbeMeansRestAndDisposeStopsTheRouting()
        {
            _input.Dispatcher.StateProbe = null;
            Assert.AreEqual(InputState.Rest, _input.Dispatcher.State);
            Drive(Key.A); Rest();
            CollectionAssert.IsEmpty(_log);
            Drive(Key.Y); Rest();
            CollectionAssert.AreEqual(new[] { "Fit:0" }, Log());
            _log.Clear();
            _input.Dispatcher.Dispose();
            Drive(Key.Y); Rest();
            CollectionAssert.IsEmpty(_log);
        }

        [Test]
        public void BIsNeverRoutedByTheDispatcher()
        {
            var b = F(); b.B = true;
            foreach (var state in States) { _state = state; Poll(b); Rest(); }
            CollectionAssert.IsEmpty(_log);
        }
    }
}
