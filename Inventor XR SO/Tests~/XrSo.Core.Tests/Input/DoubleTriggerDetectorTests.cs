using System;
using InventorXrSo.Core.Input;

namespace XrSo.Core.Tests.Input
{
    public class DoubleTriggerDetectorTests
    {
        private static (double x, double y, double z) Dir(double degrees)
        {
            var r = degrees * Math.PI / 180.0;
            return (Math.Sin(r), 0, Math.Cos(r));
        }

        private static bool Press(DoubleTriggerDetector d, double t, string target, double deg = 0)
        {
            var v = Dir(deg);
            return d.Press(t, target, v.x, v.y, v.z);
        }

        [Fact]
        public void ValidDoubleAt300ms()
        {
            var d = new DoubleTriggerDetector();
            Assert.False(Press(d, 0, "a"));
            Assert.True(Press(d, 0.30, "a"));
        }

        [Fact]
        public void TooSlowAt351msBecomesFirstPress()
        {
            var d = new DoubleTriggerDetector();
            Assert.False(Press(d, 0, "a"));
            Assert.False(Press(d, 0.351, "a"));
            Assert.True(Press(d, 0.40, "a")); // la lenta e' diventata la prima
        }

        [Fact]
        public void DifferentTargetIsNotDouble()
        {
            var d = new DoubleTriggerDetector();
            Assert.False(Press(d, 0, "a"));
            Assert.False(Press(d, 0.1, "b"));
        }

        [Fact]
        public void AngleBelowThresholdIsDouble()
        {
            var d = new DoubleTriggerDetector();
            Assert.False(Press(d, 0, "a", 0));
            Assert.True(Press(d, 0.2, "a", 1.9));
        }

        [Fact]
        public void AngleAboveThresholdIsNotDouble()
        {
            var d = new DoubleTriggerDetector();
            Assert.False(Press(d, 0, "a", 0));
            Assert.False(Press(d, 0.2, "a", 2.1));
        }

        [Fact]
        public void NullTargetResetsAndIsNotFirst()
        {
            var d = new DoubleTriggerDetector();
            Assert.False(Press(d, 0, "a"));
            Assert.False(Press(d, 0.1, null));
            Assert.False(Press(d, 0.2, "a")); // la prima e' stata azzerata
            var d2 = new DoubleTriggerDetector();
            Assert.False(Press(d2, 0, null));
            Assert.False(Press(d2, 0.1, null));
            Assert.False(Press(d2, 0.2, "a"));
        }

        [Fact]
        public void ThirdPressAfterDoubleIsAFirst()
        {
            var d = new DoubleTriggerDetector();
            Assert.False(Press(d, 0, "a"));
            Assert.True(Press(d, 0.1, "a"));
            Assert.False(Press(d, 0.15, "a"));
            Assert.True(Press(d, 0.25, "a"));
        }

        [Fact]
        public void PendingProgressHalfAt175msAndZeroAfterWindow()
        {
            var d = new DoubleTriggerDetector();
            Assert.Equal(0, d.PendingProgress(0));
            Press(d, 10.0, "a");
            Assert.Equal(0.5, d.PendingProgress(10.175), 3);
            Assert.Equal(0, d.PendingProgress(10.36));
        }

        [Fact]
        public void ResetClearsPending()
        {
            var d = new DoubleTriggerDetector();
            Press(d, 0, "a");
            d.Reset();
            Assert.Equal(0, d.PendingProgress(0.1));
            Assert.False(Press(d, 0.2, "a"));
        }
    }
}
