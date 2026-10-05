using System;
using System.Linq;
using InventorXrSo.Core.Input;
using InventorXrSo.Unity.Ui;
using InventorXrSo.Xr;
using NUnit.Framework;
using UnityEngine;

namespace InventorXrSo.Tests
{
    public class ControllerLegendTests
    {
        private GameObject _head, _left, _right;
        private ControllerLegend _legend;

        [SetUp]
        public void SetUp()
        {
            PlayerPrefs.DeleteKey(ViewActions.LegendPrefKey);
            _head = new GameObject("head");   // looks along +Z
            _left = new GameObject("left"); _right = new GameObject("right");
            _left.transform.position = new Vector3(-0.2f, -0.3f, 0.4f);
            _right.transform.position = new Vector3(0.2f, -0.3f, 0.4f);
            _legend = ControllerLegend.Create(_head.transform, _left.transform, _right.transform);
        }

        [TearDown]
        public void TearDown()
        {
            if (_legend != null) UnityEngine.Object.DestroyImmediate(_legend.gameObject);
            foreach (var go in new[] { _head, _left, _right }) if (go != null) UnityEngine.Object.DestroyImmediate(go);
            PlayerPrefs.DeleteKey(ViewActions.LegendPrefKey);
        }

        private static InputState[] States => (InputState[])Enum.GetValues(typeof(InputState));

        [Test]
        public void PrefKeyMatchesTheViewPreference() => Assert.AreEqual(ViewActions.LegendPrefKey, ControllerLegend.PrefKey);

        [Test]
        public void ForEveryStateTheLabelsAreExactlyTheActiveKeysAndShortEnough()
        {
            foreach (var state in States)
            {
                _legend.SetState(state);
                var expected = InputMap.Active(state).ToList();
                CollectionAssert.AreEquivalent(expected.Select(e => e.key), _legend.Labels.Select(l => l.Key), state.ToString());
                foreach (var (key, binding) in expected)
                {
                    var label = _legend.Labels.Single(l => l.Key == key);
                    Assert.AreEqual(binding.Label, label.Text, state + "/" + key);
                    Assert.LessOrEqual(label.Text.Length, 12, state + "/" + key);
                    Assert.True(label.Visible, state + "/" + key);
                }
            }
        }

        [Test]
        public void InactiveKeysHaveNoLabelAndNoVisibleObject()
        {
            _legend.SetState(InputState.Keypad);
            Assert.False(_legend.Labels.Any(l => l.Key == Key.StickRightH), "stick right is not bound in the keypad");
            _legend.SetState(InputState.Rest);
            var roots = _legend.GetComponent<ControllerLegend>();
            Assert.NotNull(roots);
            Assert.AreEqual(InputMap.Active(InputState.Rest).Count(),
                _left.GetComponentsInChildren<Canvas>(false).Length + _right.GetComponentsInChildren<Canvas>(false).Length,
                "only the active keys have an active canvas");
        }

        [Test]
        public void StateChangeAppliesInTheSameFrameWithoutAnimation()
        {
            _legend.SetState(InputState.Rest);
            Assert.AreEqual("Suggerisci", _legend.Labels.Single(l => l.Key == Key.X).Text);
            _legend.SetState(InputState.ComponentSelected);   // no frame in between
            Assert.AreEqual("Indietro", _legend.Labels.Single(l => l.Key == Key.X).Text);
            Assert.AreEqual("Isola", _legend.Labels.Single(l => l.Key == Key.A).Text);
        }

        [Test]
        public void OpacityIsLowByDefaultAndFullWithinTheGazeCone()
        {
            _legend.SetState(InputState.ComponentSelected);
            // controllers 0.5 m ahead-low-side: well outside 25 degrees
            _right.transform.position = new Vector3(0.6f, 0f, 0.1f);
            _legend.Tick();
            var trigger = _legend.Labels.Single(l => l.Key == Key.Trigger);
            Assert.AreEqual(LegendVisibility.BaseOpacity, trigger.Opacity, 1e-5);
            // inside the cone
            _right.transform.position = new Vector3(0.05f, 0f, 0.5f);
            _legend.Tick();
            Assert.AreEqual(LegendVisibility.FullOpacity, trigger.Opacity, 1e-5);
            // each hand is judged on its own
            Assert.AreEqual(LegendVisibility.BaseOpacity, _legend.Labels.Single(l => l.Key == Key.X).Opacity, 1e-5);
        }

        [Test]
        public void ProgressRingShowsForDoubleTriggerAndHeldX()
        {
            float trigger = 0f, back = 0f;
            _legend.TriggerProgress = () => trigger; _legend.BackProgress = () => back;
            _legend.SetState(InputState.Rest);
            trigger = 0.4f; back = 0.7f; _legend.Tick();
            Assert.AreEqual(0.4f, _legend.Labels.Single(l => l.Key == Key.Trigger).Progress, 1e-5);
            Assert.AreEqual(0.7f, _legend.Labels.Single(l => l.Key == Key.X).Progress, 1e-5);
            Assert.AreEqual(0f, _legend.Labels.Single(l => l.Key == Key.Grip).Progress, "no timed action on Grip");
            // away from rest X is the Back chain (no hold), and in the sketch the Trigger has no double action
            _legend.SetState(InputState.SketchOpen);
            Assert.AreEqual(0f, _legend.Labels.Single(l => l.Key == Key.Trigger).Progress);
            Assert.AreEqual(0f, _legend.Labels.Single(l => l.Key == Key.X).Progress);
        }

        [Test]
        public void DisablingHidesEverythingAndKeepsTheChoiceOnTheHeadset()
        {
            _legend.SetState(InputState.Rest);
            _legend.Enabled = false;
            Assert.True(_legend.Labels.Count > 0);
            Assert.True(_legend.Labels.All(l => !l.Visible));
            Assert.AreEqual(0, _left.GetComponentsInChildren<Canvas>(false).Length + _right.GetComponentsInChildren<Canvas>(false).Length);
            Assert.AreEqual(0, PlayerPrefs.GetInt(ViewActions.LegendPrefKey, 1));
            // a state change while off stays hidden
            _legend.SetState(InputState.SketchOpen);
            Assert.True(_legend.Labels.All(l => !l.Visible));
            // a new legend (new session) reads the saved choice
            var again = ControllerLegend.Create(_head.transform, _left.transform, _right.transform);
            try { Assert.False(again.Enabled); } finally { UnityEngine.Object.DestroyImmediate(again.gameObject); }
            _legend.Enabled = true;
            Assert.True(_legend.Labels.All(l => l.Visible));
            Assert.AreEqual(1, PlayerPrefs.GetInt(ViewActions.LegendPrefKey, 0));
        }

        [Test]
        public void HiddenOutsideTheSessionWithoutTouchingThePreference()
        {
            _legend.SetState(InputState.Rest);
            _legend.Shown = false;
            Assert.True(_legend.Labels.All(l => !l.Visible));
            Assert.AreEqual(1, PlayerPrefs.GetInt(ViewActions.LegendPrefKey, 1));
            _legend.Shown = true;
            Assert.True(_legend.Labels.All(l => l.Visible));
        }
    }
}
