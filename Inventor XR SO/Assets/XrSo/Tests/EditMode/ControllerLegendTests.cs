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

        // --- geometria (legenda accanto ai tasti reali) ---

        private void Pose()
        {
            // head turned and tilted, controllers rotated: the layout must not depend on the controller axes
            _head.transform.rotation = Quaternion.Euler(10f, 25f, 8f);
            _left.transform.rotation = Quaternion.Euler(40f, -30f, 15f);
            _right.transform.rotation = Quaternion.Euler(-20f, 50f, -10f);
            _legend.Tick(); // no LateUpdate in EditMode
        }

        [Test]
        public void EachLabelIsCloseToItsRealButton()
        {
            foreach (var posed in new[] { false, true })
                foreach (var state in States)
                {
                    if (posed) Pose();
                    _legend.SetState(state);
                    foreach (var l in _legend.Labels)
                    {
                        var anchor = l.Right ? _right.transform : _left.transform;
                        var local = (l.Right ? ControllerLegend.RightButtonPositions : ControllerLegend.LeftButtonPositions)[l.Key];
                        Assert.AreEqual(0f, Vector3.Distance(anchor.TransformPoint(local), l.ButtonWorldPosition), 1e-5f, state + "/" + l.Key);
                        Assert.Less(Vector3.Distance(l.WorldPosition, l.ButtonWorldPosition), posed ? 0.14f : 0.125f, state + "/" + l.Key + (posed ? " posed" : ""));
                    }
                }
        }

        [Test]
        public void LabelsOfOneHandDoNotOverlap()
        {
            Pose();
            const float height = ControllerLegend.LabelHeightMm * 0.001f;
            foreach (var state in States)
            {
                _legend.SetState(state);
                foreach (var hand in new[] { true, false })
                {
                    var ls = _legend.Labels.Where(l => l.Right == hand).ToList();
                    for (int i = 0; i < ls.Count; i++)
                        for (int j = i + 1; j < ls.Count; j++)
                            Assert.GreaterOrEqual(Mathf.Abs(Vector3.Dot(ls[i].WorldPosition - ls[j].WorldPosition, _head.transform.up)), height - 1e-5f,
                                state + " " + ls[i].Key + "/" + ls[j].Key);
                }
            }
        }

        [Test]
        public void LabelsSitOutwardOfEachHandRelativeToTheHeadRight()
        {
            Pose();
            _legend.SetState(InputState.ComponentSelected);
            var headRight = LegendGeometry.HeadRight(_head.transform);
            foreach (var l in _legend.Labels)
            {
                var anchor = l.Right ? _right.transform : _left.transform;
                var buttons = l.Right ? ControllerLegend.RightButtonPositions : ControllerLegend.LeftButtonPositions;
                var centroid = anchor.TransformPoint(LegendGeometry.Centroid(buttons.Values));
                float side = Vector3.Dot(l.WorldPosition - centroid, headRight);
                // the column axis is head.up, not exactly orthogonal to the flattened right when the head rolls: sign and rough size only
                Assert.Greater(l.Right ? side : -side, LegendGeometry.OutwardMeters * 0.75f, l.Key.ToString());
            }
        }

        [Test]
        public void LabelsFaceTheHeadAndStayUpright()
        {
            Pose();
            foreach (var state in States)
            {
                _legend.SetState(state);
                foreach (var l in _legend.Labels)
                {
                    var canvas = (l.Right ? _right : _left).transform.Find("Legend." + l.Key);
                    Assert.NotNull(canvas, l.Key.ToString());
                    var toHead = (_head.transform.position - canvas.position).normalized;
                    // a world-space canvas reads correctly when seen from behind its forward axis, so forward points away from the head
                    Assert.Greater(Vector3.Dot(-canvas.forward, toHead), 0.9f, state + "/" + l.Key);
                    Assert.Greater(Vector3.Dot(canvas.up, _head.transform.up), 0.85f, state + "/" + l.Key);
                }
            }
        }

        [Test]
        public void LeaderLineGoesFromTheButtonToTheLabelEdge()
        {
            Pose();
            _legend.SetState(InputState.ComponentSelected);
            foreach (var l in _legend.Labels)
            {
                var canvas = (l.Right ? _right : _left).transform.Find("Legend." + l.Key);
                var line = canvas.Find("Guida").GetComponent<LineRenderer>();
                Assert.True(canvas.gameObject.activeInHierarchy);
                Assert.AreEqual(2, line.positionCount);
                Assert.AreEqual(0f, Vector3.Distance(line.GetPosition(0), l.ButtonWorldPosition), 1e-5f, l.Key.ToString());
                Assert.AreEqual(0f, Vector3.Distance(line.GetPosition(1), l.LeaderEnd), 1e-5f, l.Key.ToString());
                // end point lies on the label rectangle's boundary region (within its half-extents) and near the label
                var d = l.LeaderEnd - canvas.position;
                Assert.LessOrEqual(Mathf.Abs(Vector3.Dot(d, canvas.right)), ControllerLegend.LabelWidthMm * 0.0005f + 1e-5f);
                Assert.LessOrEqual(Mathf.Abs(Vector3.Dot(d, canvas.up)), ControllerLegend.LabelHeightMm * 0.0005f + 1e-5f);
                Assert.Less(Vector3.Distance(l.LeaderEnd, l.WorldPosition), 0.05f);
                Assert.AreEqual(l.Opacity, line.startColor.a, 0.005f); // gradient colors are 8-bit
                Assert.AreEqual(ControllerLegend.LeaderWidth, line.startWidth, 1e-6f);
            }
            // both stick axes share one button point
            var h = _legend.Labels.FirstOrDefault(l => l.Key == Key.StickRightH);
            var v = _legend.Labels.FirstOrDefault(l => l.Key == Key.StickRightV);
            if (h != null && v != null) Assert.AreEqual(h.ButtonWorldPosition, v.ButtonWorldPosition);
        }

        [Test]
        public void LeaderLineIsHiddenWithItsLabel()
        {
            _legend.SetState(InputState.ComponentSelected);
            _legend.Enabled = false;
            Assert.AreEqual(0, _right.GetComponentsInChildren<LineRenderer>(false).Length + _left.GetComponentsInChildren<LineRenderer>(false).Length);
        }

        [Test]
        public void ActiveLabelsTakeConsecutiveSlotsWithoutGaps()
        {
            foreach (var state in States)
            {
                _legend.SetState(state);
                foreach (var hand in new[] { true, false })
                {
                    var ls = _legend.Labels.Where(l => l.Right == hand).ToList();
                    var order = hand ? ControllerLegend.RightOrder : ControllerLegend.LeftOrder;
                    var expected = order.Where(k => ls.Any(l => l.Key == k)).ToList();
                    CollectionAssert.AreEqual(Enumerable.Range(0, ls.Count), ls.Select(l => l.Slot).OrderBy(s => s), state + " " + hand);
                    CollectionAssert.AreEqual(expected, ls.OrderBy(l => l.Slot).Select(l => l.Key), state + " " + hand);
                    // top to bottom in world space
                    var ys = ls.OrderBy(l => l.Slot).Select(l => Vector3.Dot(l.WorldPosition, _head.transform.up)).ToList();
                    for (int i = 1; i < ys.Count; i++) Assert.Less(ys[i], ys[i - 1], state + " " + hand);
                }
            }
        }

        [Test]
        public void StackMathCentersTheColumnAndCompacts()
        {
            Assert.AreEqual(LegendGeometry.ColumnLift, LegendGeometry.StackY(0, 1), 1e-6f);
            Assert.AreEqual(LegendGeometry.ColumnLift + LegendGeometry.SlotSpacing, LegendGeometry.StackY(0, 3), 1e-6f);
            Assert.AreEqual(LegendGeometry.ColumnLift, LegendGeometry.StackY(1, 3), 1e-6f);
            Assert.AreEqual(LegendGeometry.ColumnLift - LegendGeometry.SlotSpacing, LegendGeometry.StackY(2, 3), 1e-6f);
        }

        [Test]
        public void NearestEdgePointClampsToTheRectangle()
        {
            var p = LegendGeometry.NearestEdgePoint(Vector3.zero, Vector3.right, Vector3.up, 0.02f, 0.005f, new Vector3(0.1f, 0.001f, 0f));
            Assert.AreEqual(new Vector3(0.02f, 0.001f, 0f), p);
            var inside = LegendGeometry.NearestEdgePoint(Vector3.zero, Vector3.right, Vector3.up, 0.02f, 0.005f, new Vector3(0.019f, 0.001f, 0f));
            Assert.AreEqual(0.02f, inside.x, 1e-6f);
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
