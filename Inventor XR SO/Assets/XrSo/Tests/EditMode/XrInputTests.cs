using System.Collections.Generic;
using InventorXrSo.Xr.Input;
using NUnit.Framework;
using UnityEngine;

namespace InventorXrSo.Tests
{
    public class XrInputTests
    {
        private GameObject _go;
        private XrInput _input;
        private readonly List<string> _log = new List<string>();

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("input");
            _input = _go.AddComponent<XrInput>();
            _input.PenPressed += () => _log.Add("press");
            _input.PenReleased += () => _log.Add("release");
            _input.Back += () => _log.Add("back");
            _input.Fit += () => _log.Add("fit");
            _input.Recenter += () => _log.Add("recenter");
            _input.SnapToggled += () => _log.Add("snap");
            _input.StepDelta += d => _log.Add("step" + d);
            _input.StepSizeDelta += d => _log.Add("size" + d);
            _input.TabDelta += d => _log.Add("tab" + d);
            _input.TwoHandChanged += on => _log.Add("two" + on);
        }

        [TearDown]
        public void TearDown() { Object.DestroyImmediate(_go); _log.Clear(); }

        private static XrInputFrame F() => new XrInputFrame { PenTracked = true, PaletteTracked = true };

        [Test]
        public void TriggerPressAndReleaseAreEdges()
        {
            var down = F(); down.PenTrigger = true;
            _input.Poll(down, 0); _input.Poll(down, 0.1f); _input.Poll(F(), 0.2f);
            CollectionAssert.AreEqual(new[] { "press", "release" }, _log);
        }

        [Test]
        public void LosingTrackingReleasesTheHeldPen()
        {
            var down = F(); down.PenTrigger = true;
            _input.Poll(down, 0);
            var lost = down; lost.PenTracked = false;
            _input.Poll(lost, 0.1f);
            CollectionAssert.AreEqual(new[] { "press", "release" }, _log);
            Assert.IsFalse(_input.PenHeld);
        }

        [Test]
        public void StickFlicksFireOncePerFlickWithHysteresis()
        {
            var right = F(); right.PenStick = new Vector2(0.9f, 0);
            var half = F(); half.PenStick = new Vector2(0.5f, 0);
            _input.Poll(right, 0); _input.Poll(half, 0.1f); _input.Poll(right, 0.2f);
            _input.Poll(F(), 0.3f); _input.Poll(right, 0.4f);
            CollectionAssert.AreEqual(new[] { "step1", "step1" }, _log);
        }

        [Test]
        public void PenStickVerticalChangesStepSizeAndPaletteStickChangesTab()
        {
            var up = F(); up.PenStick = new Vector2(0, 0.9f);
            var left = F(); left.PaletteStick = new Vector2(-0.9f, 0);
            _input.Poll(up, 0); _input.Poll(F(), 0.1f); _input.Poll(left, 0.2f);
            CollectionAssert.AreEqual(new[] { "size1", "tab-1" }, _log);
        }

        [Test]
        public void YTapFitsAndYHoldRecentersOnce()
        {
            var y = F(); y.Y = true;
            _input.Poll(y, 0); _input.Poll(F(), 0.3f);
            _input.Poll(y, 1); _input.Poll(y, 1.5f); _input.Poll(y, 2.1f); _input.Poll(y, 2.5f); _input.Poll(F(), 2.6f);
            CollectionAssert.AreEqual(new[] { "fit", "recenter" }, _log);
        }

        [Test]
        public void XIsBackAndAIsSnap()
        {
            var x = F(); x.X = true;
            var a = F(); a.A = true;
            _input.Poll(x, 0); _input.Poll(F(), 0.1f); _input.Poll(a, 0.2f);
            CollectionAssert.AreEqual(new[] { "back", "snap" }, _log);
        }

        [Test]
        public void BothGripsMakeTwoHandAndPaletteTriggerIsPrecision()
        {
            var both = F(); both.PenGrip = true; both.PaletteGrip = true; both.PaletteTrigger = true;
            _input.Poll(both, 0);
            Assert.IsTrue(_input.TwoHand);
            Assert.IsTrue(_input.Precision);
            _input.Poll(F(), 0.1f);
            CollectionAssert.AreEqual(new[] { "twoTrue", "twoFalse" }, _log);
        }

        [Test]
        public void ZoomReportsPaletteStickVerticalBeyondTheDeadZone()
        {
            float zoom = 0; _input.Zoom += z => zoom = z;
            var f = F(); f.PaletteStick = new Vector2(0, 0.1f);
            _input.Poll(f, 0);
            Assert.AreEqual(0, zoom);
            f.PaletteStick = new Vector2(0, -0.6f);
            _input.Poll(f, 0.1f);
            Assert.AreEqual(-0.6f, zoom, 1e-5);
        }

        [Test]
        public void SyntheticSourceIsReported()
        {
            _input.Source = new SyntheticInputSource();
            Assert.IsTrue(_input.Synthetic);
        }

        [Test]
        public void HapticsGoThroughTheReplaceableSink()
        {
            var played = new List<string>();
            var old = Haptics.Sink;
            Haptics.Sink = (p, pen) => played.Add(p + ":" + pen);
            try { Haptics.Play(HapticPulse.Success); Haptics.Play(HapticPulse.Tick, false); }
            finally { Haptics.Sink = old; }
            CollectionAssert.AreEqual(new[] { "Success:True", "Tick:False" }, played);
        }
    }
}
