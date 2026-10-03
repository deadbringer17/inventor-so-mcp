using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Ui;

namespace XrSo.Core.Tests.Ui
{
    public class SketchSheetLayoutTests
    {
        private static readonly WorkbenchFrame Front = WorkbenchFrame.FromHead(new CadPoint(0, 1.2, 0), 0, null);
        private static readonly SketchFrame Xy = new SketchFrame(new CadPoint(0, 0, 0), new CadPoint(1, 0, 0), new CadPoint(0, 1, 0));

        private static void Near(CadPoint expected, CadPoint actual)
        {
            Assert.Equal(expected.X, actual.X, 6);
            Assert.Equal(expected.Y, actual.Y, 6);
            Assert.Equal(expected.Z, actual.Z, 6);
        }

        private static void AxesMapToBench(SketchFrame f, SheetPose p, WorkbenchFrame bench)
        {
            Near(bench.Right, p.Rotate(SketchSheetLayoutFlip(f.XAxis)));
            Near(bench.Forward, p.Rotate(SketchSheetLayoutFlip(f.YAxis)));
            Near(new CadPoint(0, 1, 0), p.Rotate(SketchSheetLayoutFlip(f.Normal)));
        }

        // direzione CAD -> spazio locale mesh (X specchiata, senza /1000)
        private static CadPoint SketchSheetLayoutFlip(CadPoint d) => new CadPoint(-d.X, d.Y, d.Z);

        [Fact]
        public void Sketch_axes_map_to_right_forward_and_up()
        {
            AxesMapToBench(Xy, SketchSheetLayout.Compute(Xy, 100, 100, Front), Front);
        }

        [Fact]
        public void Sketch_point_lands_on_the_desk_plane()
        {
            var p = SketchSheetLayout.Compute(Xy, 100, 100, Front);
            Assert.Equal(Front.DeskY, p.Transform(SketchSheetLayout.ToMeshLocal(new CadPoint(30, 70, 0))).Y, 6);
        }

        [Fact]
        public void Sheet_centre_sits_part_distance_ahead_on_the_desk()
        {
            var p = SketchSheetLayout.Compute(Xy, 200, 100, Front);
            Near(new CadPoint(0, 0.75, 0.40), p.Transform(SketchSheetLayout.ToMeshLocal(new CadPoint(100, 50, 0))));
        }

        [Fact]
        public void Model_x_goes_right_and_model_y_goes_away_from_the_user()
        {
            var p = SketchSheetLayout.Compute(Xy, 200, 100, Front);
            var c = p.Transform(SketchSheetLayout.ToMeshLocal(new CadPoint(100, 50, 0)));
            var px = p.Transform(SketchSheetLayout.ToMeshLocal(new CadPoint(200, 50, 0)));
            var py = p.Transform(SketchSheetLayout.ToMeshLocal(new CadPoint(100, 100, 0)));
            Assert.True(px.X > c.X);
            Assert.True(py.Z > c.Z);
        }

        [Fact]
        public void Scale_fits_the_sheet_and_degenerate_extents_give_one()
        {
            Assert.Equal(WorkbenchLayout.SheetScale(0.2, 0.1), SketchSheetLayout.Compute(Xy, 200, 100, Front).Scale, 9);
            Assert.Equal(1.5, SketchSheetLayout.Compute(Xy, 300, 200, Front).Scale, 9);
            var d = SketchSheetLayout.Compute(Xy, 0, 0, Front);
            Assert.Equal(1, d.Scale, 9);
            Near(new CadPoint(0, 0.75, 0.40), d.Transform(new CadPoint(0, 0, 0)));
        }

        [Fact]
        public void Plane_with_arbitrary_normal_is_laid_flat()
        {
            // piano YZ: X = +Y, Y = +Z, normale = +X
            var f = new SketchFrame(new CadPoint(10, 20, 30), new CadPoint(0, 1, 0), new CadPoint(0, 0, 1));
            Near(new CadPoint(1, 0, 0), f.Normal);
            var p = SketchSheetLayout.Compute(f, 80, 60, Front);
            AxesMapToBench(f, p, Front);
            Near(new CadPoint(0, 0.75, 0.40), p.Transform(SketchSheetLayout.ToMeshLocal(new CadPoint(10, 60, 60))));
        }

        [Fact]
        public void Yaw_rotates_the_sheet_with_the_bench()
        {
            var bench = WorkbenchFrame.FromHead(new CadPoint(1, 1.2, 2), 90, null);
            var p = SketchSheetLayout.Compute(Xy, 200, 100, bench);
            AxesMapToBench(Xy, p, bench);
            Near(new CadPoint(1.40, 0.75, 2), p.Transform(SketchSheetLayout.ToMeshLocal(new CadPoint(100, 50, 0))));
        }

        [Fact]
        public void Rotation_is_unit_quaternion()
        {
            var p = SketchSheetLayout.Compute(Xy, 100, 100, WorkbenchFrame.FromHead(new CadPoint(0, 1.2, 0), 37, null));
            Assert.Equal(1, p.Qx * p.Qx + p.Qy * p.Qy + p.Qz * p.Qz + p.Qw * p.Qw, 9);
        }
    }
}
