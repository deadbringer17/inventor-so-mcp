using System;
using System.Linq;
using InventorXrSo.Core.Input;

namespace XrSo.Core.Tests.Input
{
    public class InputMapTests
    {
        private static readonly InputState[] States = (InputState[])Enum.GetValues(typeof(InputState));
        private static readonly Key[] Keys = (Key[])Enum.GetValues(typeof(Key));

        [Fact]
        public void Resolve_PriorityForAll16Combinations()
        {
            for (int m = 0; m < 16; m++)
            {
                bool keypad = (m & 8) != 0, armed = (m & 4) != 0, sketch = (m & 2) != 0, comp = (m & 1) != 0;
                var expected = keypad ? InputState.Keypad
                    : armed ? InputState.ArmedOrHandle
                    : sketch ? InputState.SketchOpen
                    : comp ? InputState.ComponentSelected
                    : InputState.Rest;
                Assert.Equal(expected, InputMap.Resolve(keypad, armed, sketch, comp));
            }
        }

        [Fact]
        public void Labels_AreAtMost12Chars()
        {
            foreach (var s in States)
                foreach (var (_, b) in InputMap.Active(s))
                {
                    Assert.NotNull(b.Label);
                    Assert.True(b.Label.Length <= 12, b.Label);
                }
        }

        [Fact]
        public void Active_HasNoDuplicateKeys_AndMatchesLookup()
        {
            foreach (var s in States)
            {
                var keys = InputMap.Active(s).Select(t => t.key).ToList();
                Assert.Equal(keys.Count, keys.Distinct().Count());
                foreach (var k in Keys)
                    Assert.Equal(keys.Contains(k), InputMap.Lookup(s, k) != null);
            }
        }

        [Fact]
        public void B_IsSpeakEverywhere()
        {
            foreach (var s in States)
                Assert.Equal(InputAction.Speak, InputMap.Lookup(s, Key.B).Action);
        }

        [Fact]
        public void TwoGripsAndY_PresentExceptKeypad()
        {
            foreach (var s in States)
            {
                var two = InputMap.Lookup(s, Key.TwoGrips);
                var y = InputMap.Lookup(s, Key.Y);
                if (s == InputState.Keypad) { Assert.Null(two); Assert.Null(y); }
                else
                {
                    Assert.Equal(InputAction.TwoHands, two.Action);
                    Assert.Equal(InputAction.Fit, y.Action);
                }
            }
        }

        [Fact]
        public void X_PerState()
        {
            var rest = InputMap.Lookup(InputState.Rest, Key.X);
            Assert.Equal(InputAction.Suggest, rest.Action);
            Assert.Equal(InputAction.BackHold, rest.Secondary);
            foreach (var s in new[] { InputState.ComponentSelected, InputState.SketchOpen, InputState.ArmedOrHandle })
                Assert.Equal(InputAction.Back, InputMap.Lookup(s, Key.X).Action);
            Assert.Equal(InputAction.KeypadCancel, InputMap.Lookup(InputState.Keypad, Key.X).Action);
        }

        [Fact]
        public void A_PerState()
        {
            Assert.Null(InputMap.Lookup(InputState.Rest, Key.A));
            Assert.Equal(InputAction.Isolate, InputMap.Lookup(InputState.ComponentSelected, Key.A).Action);
            Assert.Equal(InputAction.SnapToggle, InputMap.Lookup(InputState.SketchOpen, Key.A).Action);
            Assert.Equal(InputAction.OpenKeypad, InputMap.Lookup(InputState.ArmedOrHandle, Key.A).Action);
            Assert.Equal(InputAction.KeypadOk, InputMap.Lookup(InputState.Keypad, Key.A).Action);
        }

        [Fact]
        public void Keypad_OnlyTriggerABX()
        {
            var keys = InputMap.Active(InputState.Keypad).Select(t => t.key).OrderBy(k => k).ToArray();
            Assert.Equal(new[] { Key.Trigger, Key.A, Key.B, Key.X }.OrderBy(k => k).ToArray(), keys);
            Assert.Equal(InputAction.PressKeys, InputMap.Lookup(InputState.Keypad, Key.Trigger).Action);
        }

        [Fact]
        public void SpecTable_RemainingRows()
        {
            foreach (var s in new[] { InputState.Rest, InputState.ComponentSelected, InputState.SketchOpen, InputState.ArmedOrHandle })
            {
                Assert.Equal(InputAction.MoveView, InputMap.Lookup(s, Key.Grip).Action);
                Assert.Equal(InputAction.Tabs, InputMap.Lookup(s, Key.StickLeftH).Action);
                Assert.Equal(InputAction.Zoom, InputMap.Lookup(s, Key.StickLeftV).Action);
            }
            foreach (var s in new[] { InputState.Rest, InputState.ComponentSelected, InputState.SketchOpen })
            {
                Assert.Equal(InputAction.RotateView, InputMap.Lookup(s, Key.StickRightH).Action);
                Assert.Null(InputMap.Lookup(s, Key.StickRightV));
            }
            Assert.Equal(InputAction.StepChange, InputMap.Lookup(InputState.ArmedOrHandle, Key.StickRightH).Action);
            Assert.Equal(InputAction.StepSize, InputMap.Lookup(InputState.ArmedOrHandle, Key.StickRightV).Action);
            Assert.Equal(InputAction.Select, InputMap.Lookup(InputState.Rest, Key.Trigger).Action);
            Assert.Equal(InputAction.DoubleSelect, InputMap.Lookup(InputState.Rest, Key.Trigger).Secondary);
            Assert.Equal(InputAction.Point, InputMap.Lookup(InputState.SketchOpen, Key.Trigger).Action);
            Assert.Equal(InputAction.Drag, InputMap.Lookup(InputState.ArmedOrHandle, Key.Trigger).Action);
            Assert.Null(InputMap.Lookup(InputState.Rest, Key.TriggerLeft));
            Assert.Null(InputMap.Lookup(InputState.ComponentSelected, Key.TriggerLeft));
            Assert.Equal(InputAction.Precision, InputMap.Lookup(InputState.SketchOpen, Key.TriggerLeft).Action);
            Assert.Equal(InputAction.Precision, InputMap.Lookup(InputState.ArmedOrHandle, Key.TriggerLeft).Action);
        }
    }
}
