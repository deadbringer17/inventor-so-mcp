using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Ui;

namespace XrSo.Core.Tests.Ui
{
    public class WorkbenchLayoutTests
    {
        private static readonly WorkbenchFrame Front = WorkbenchFrame.FromHead(new CadPoint(0, 1.2, 0), 0, null);

        private static void Near(CadPoint expected, CadPoint actual)
        {
            Assert.Equal(expected.X, actual.X, 6);
            Assert.Equal(expected.Y, actual.Y, 6);
            Assert.Equal(expected.Z, actual.Z, 6);
        }

        [Fact]
        public void Default_desk_is_45_cm_below_the_head_and_calibration_wins()
        {
            Assert.Equal(0.75, Front.DeskY, 6);
            Assert.Equal(0.72, WorkbenchFrame.FromHead(new CadPoint(0, 1.2, 0), 0, 0.72).DeskY, 6);
        }

        [Fact]
        public void Yaw_rotates_forward_and_right_around_the_vertical()
        {
            var f = WorkbenchFrame.FromHead(new CadPoint(0, 1.2, 0), 90, null);
            Near(new CadPoint(1, 0, 0), f.Forward);
            Near(new CadPoint(0, 0, -1), f.Right);
        }

        [Fact]
        public void Part_sits_on_the_desk_40_cm_ahead_scaled_into_a_40_cm_box()
        {
            var p = WorkbenchLayout.Part(Front, 0.2);
            Near(new CadPoint(0, 0.75, 0.40), p.Position);
            Assert.Equal(2.0, p.Scale, 6);
            Assert.False(p.Clamped);
        }

        [Fact]
        public void Assembly_is_raised_65_cm_ahead_and_fits_80_cm()
        {
            var p = WorkbenchLayout.Assembly(Front, 1.6);
            Near(new CadPoint(0, 1.05, 0.65), p.Position);
            Assert.Equal(0.5, p.Scale, 6);
        }

        [Fact]
        public void Scale_is_clamped_and_reported()
        {
            var tiny = WorkbenchLayout.Part(Front, 0.00001);
            Assert.Equal(WorkbenchLayout.MaxScale, tiny.Scale);
            Assert.True(tiny.Clamped);
            var huge = WorkbenchLayout.Part(Front, 1000);
            Assert.Equal(WorkbenchLayout.MinScale, huge.Scale);
            Assert.True(huge.Clamped);
            Assert.Equal(1, WorkbenchLayout.Part(Front, 0).Scale);
        }

        [Fact]
        public void Isolated_component_moves_halfway_to_the_user()
        {
            var p = WorkbenchLayout.Isolated(Front, new CadPoint(0.2, 1.0, 0.8), 0.5);
            Near(new CadPoint(0.1, 1.1, 0.4), p.Position);
            Assert.Equal(0.5, p.Scale);
        }

        [Fact]
        public void Sheet_scale_fits_the_sketch_into_45_by_30_cm()
        {
            Assert.Equal(1.5, WorkbenchLayout.SheetScale(0.3, 0.1), 6);
            Assert.Equal(1.0, WorkbenchLayout.SheetScale(0.45, 0.3), 6);
            Assert.Equal(0.5, WorkbenchLayout.SheetScale(0.2, 0.6), 6);
        }

        [Fact]
        public void Commit_bar_is_on_the_near_edge_of_the_desk()
            => Near(new CadPoint(0, 0.75, 0.20), WorkbenchLayout.CommitBarPosition(Front));
    }
}
