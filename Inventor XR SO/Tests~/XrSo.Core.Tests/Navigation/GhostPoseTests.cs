using InventorXrSo.Core.Navigation;

namespace XrSo.Core.Tests.Navigation
{
    public class GhostPoseTests
    {
        private static float[] Mul(float[] a, float[] b)
        {
            var r = new float[16];
            for (int c = 0; c < 4; c++)
            for (int row = 0; row < 4; row++)
            {
                float s = 0;
                for (int k = 0; k < 4; k++) s += a[k * 4 + row] * b[c * 4 + k];
                r[c * 4 + row] = s;
            }
            return r;
        }

        private static void AssertIdentity(float[] m)
        {
            for (int i = 0; i < 16; i++) Assert.Equal(GhostPose.Identity[i], m[i], 4);
        }

        [Fact]
        public void Null_or_bad_pose_gives_identity()
        {
            AssertIdentity(GhostPose.Inverse(null));
            AssertIdentity(GhostPose.Inverse(new float[3]));
            AssertIdentity(GhostPose.Inverse(new float[16]));   // singolare
        }

        [Fact]
        public void Translation_is_negated()
        {
            var inv = GhostPose.Inverse(new float[] { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0.5f, -1f, 2f, 1 });
            Assert.Equal(-0.5f, inv[12], 5); Assert.Equal(1f, inv[13], 5); Assert.Equal(-2f, inv[14], 5);
        }

        [Fact]
        public void Rotation_with_translation_times_inverse_is_identity()
        {
            // 90 gradi attorno a Y (column-major) + traslazione + scala uniforme 2.
            var m = new float[] { 0, 0, -2, 0, 0, 2, 0, 0, 2, 0, 0, 0, 0.1f, 0.2f, 0.3f, 1 };
            AssertIdentity(Mul(m, GhostPose.Inverse(m)));
            AssertIdentity(Mul(GhostPose.Inverse(m), m));
        }
    }
}
