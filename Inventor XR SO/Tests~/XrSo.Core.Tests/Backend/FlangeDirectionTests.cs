using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Glb;

namespace InventorXrSo.Core.Tests.Backend;

public class FlangeDirectionTests
{
    private static readonly CadPoint Origin = new(50, 0, 2), EdgeDir = new(1, 0, 0), Up = new(0, 0, 1);

    private static GlbModel Model(params (double x, double y, double z)[] mm)
    {
        var positions = new float[mm.Length * 3];
        for (int i = 0; i < mm.Length; i++)
        { positions[i * 3] = (float)(mm[i].x / 1000); positions[i * 3 + 1] = (float)(mm[i].y / 1000); positions[i * 3 + 2] = (float)(mm[i].z / 1000); }
        return new GlbModel("doc", new[] { new GlbPrimitive(1, "b", true, positions, new float[0], new uint[0], System.Array.Empty<FaceRange>()) });
    }

    /// <summary>Flat sheet (z 0..2, y from -100 to 0) plus a flange tip line from the edge (y=0, z=2) to (dy, dz).</summary>
    private static GlbModel Part(double dy, double dz, double x0 = 0, double x1 = 100) => Model(
        (x0, -100, 0), (x1, -100, 2), (x0, 0, 0), (x1, 0, 2), (x0, 0, 2), (x1, -50, 2),
        (x0, dy, 2 + dz), (x1, dy, 2 + dz), (50, dy, 2 + dz));

    [Fact]
    public void FlangeUpKeepsAPositiveAxis()
    {
        Assert.True(FlangeDirection.Calibrate(Part(0, 20), Origin, EdgeDir, 100, Up, 20, out var axis, out var confidence, 2));
        Assert.Equal(1, axis.Z, 6); Assert.InRange(confidence, 0.5, 1);
    }

    [Fact]
    public void FlangeDownFlipsTheAxis()
    {
        Assert.True(FlangeDirection.Calibrate(Part(0, -22), Origin, EdgeDir, 100, Up, 20, out var axis, out _, 2));
        Assert.Equal(-1, axis.Z, 6);
    }

    [Fact]
    public void AngledFlangeStillResolvesItsSide()
    {
        Assert.True(FlangeDirection.Calibrate(Part(14, 14), Origin, EdgeDir, 100, Up, 20, out var axis, out _, 2));
        Assert.Equal(1, axis.Z, 6);
        Assert.True(FlangeDirection.Calibrate(Part(14, -16), Origin, EdgeDir, 100, Up, 20, out axis, out _, 2));
        Assert.Equal(-1, axis.Z, 6);
    }

    [Fact]
    public void MaterialOnBothSidesIsAmbiguous()
    {
        var both = Model((50, 0, 22), (50, 0, -18), (50, -50, 2));
        Assert.False(FlangeDirection.Calibrate(both, Origin, EdgeDir, 100, Up, 20, out var axis, out var confidence, 2));
        Assert.Equal(0, confidence); Assert.Equal(1, axis.Z, 6); // untouched candidate
    }

    [Fact]
    public void NoFlangeMaterialIsNotCalibrated()
    {
        Assert.False(FlangeDirection.Calibrate(Model((0, -100, 0), (100, 0, 2), (50, -30, 2)), Origin, EdgeDir, 100, Up, 20, out _, out _, 2));
    }

    [Fact]
    public void EmptyOrInvalidInputsAreNotCalibrated()
    {
        var empty = new GlbModel("doc", System.Array.Empty<GlbPrimitive>());
        Assert.False(FlangeDirection.Calibrate(empty, Origin, EdgeDir, 100, Up, 20, out _, out _));
        Assert.False(FlangeDirection.Calibrate(null, Origin, EdgeDir, 100, Up, 20, out _, out _));
        Assert.False(FlangeDirection.Calibrate(Part(0, 20), Origin, EdgeDir, 100, Up, 0, out _, out _));
        Assert.False(FlangeDirection.Calibrate(Part(0, 20), Origin, EdgeDir, 0, Up, 20, out _, out _));
        Assert.False(FlangeDirection.Calibrate(Part(0, 20), Origin, new CadPoint(0, 0, 0), 100, Up, 20, out _, out _));
        Assert.False(FlangeDirection.Calibrate(Part(0, 20), Origin, EdgeDir, 100, EdgeDir, 20, out _, out _), "Axis parallel to the edge.");
    }

    [Fact]
    public void MaterialOutsideTheEdgeSpanIsIgnored()
    {
        // A tall neighbour 200 mm away along the edge must not vote.
        var model = Model((0, -100, 0), (50, 0, 2), (400, 0, 22));
        Assert.False(FlangeDirection.Calibrate(model, Origin, EdgeDir, 100, Up, 20, out _, out _, 2));
    }

    [Fact]
    public void ThinSheetNeighbourhoodDoesNotVoteWhenThicknessIsKnown()
    {
        // Thick sheet (5 mm) with a 6 mm flange height: only the flange side reaches 6 mm.
        var model = Model((50, -50, 0), (50, -50, -5), (50, 0, 6));
        Assert.True(FlangeDirection.Calibrate(model, new CadPoint(50, 0, 0), EdgeDir, 100, Up, 6, out var axis, out _, 5));
        Assert.Equal(1, axis.Z, 6);
    }

    [Fact]
    public void FallbackCandidateCanBeCorrectedToThePerpendicularSide()
    {
        // Vertical plate, candidate "up" is wrong: the flange grows along -Y (model), 20 mm.
        var origin = new CadPoint(50, 0, 0);
        var model = Model((50, -20, 0), (0, -20, 0), (100, -20, 0), (50, 0, 0), (50, 0, -100)); // sheet hangs down along -Z
        Assert.True(FlangeDirection.Calibrate(model, origin, EdgeDir, 100, Up, 20, out var axis, out _, considerPerpendicular: true));
        // p = e x a = (1,0,0) x (0,0,1) = (0,-1,0)
        Assert.Equal(-1, axis.Y, 6);
    }

    [Fact]
    public void PerpendicularIsNotConsideredUnlessAsked()
    {
        var model = Model((50, -20, 0), (50, 0, 0));
        Assert.False(FlangeDirection.Calibrate(model, new CadPoint(50, 0, 0), EdgeDir, 100, Up, 20, out _, out _));
    }
}
