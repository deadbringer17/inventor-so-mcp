using InventorXrSo.Core.Glb;

namespace InventorXrSo.Core.Tests.Glb;

public class HandednessTests
{
    [Fact]
    public void FlipXNegatesEveryXOnly() =>
        Assert.Equal(new[] { -1f, 2, 3, -4, 5, 6 }, Handedness.FlipX(new[] { 1f, 2, 3, 4, 5, 6 }));

    [Fact]
    public void ReverseWindingKeepsTriangleOrder() =>
        Assert.Equal(new uint[] { 0, 2, 1, 3, 5, 4 }, Handedness.ReverseWinding(new uint[] { 0, 1, 2, 3, 4, 5 }));

    [Fact]
    public void TranslationXChangesSign()
    {
        var m = Identity();
        m[12] = 0.03f; m[13] = 0.02f; m[14] = 0.01f;
        var u = Handedness.ConvertMatrix(m);
        Assert.Equal(new[] { -0.03f, 0.02f, 0.01f }, new[] { u[12], u[13], u[14] });
    }

    [Fact]
    public void ConvertedMatrixActsOnFlippedPointsLikeTheOriginal()
    {
        float c = MathF.Cos(0.7f), s = MathF.Sin(0.7f);
        // Rotation about Y plus a translation, glTF column-major.
        var m = new[] { c, 0, -s, 0, 0, 1, 0, 0, s, 0, c, 0, 0.1f, 0.2f, 0.3f, 1 };
        var p = new[] { 0.5f, -0.25f, 0.75f };
        var expected = Handedness.FlipX(Apply(m, p));
        var actual = Apply(Handedness.ConvertMatrix(m), Handedness.FlipX(p));
        for (int i = 0; i < 3; i++) Assert.Equal(expected[i], actual[i], 5);
    }

    private static float[] Identity() => new float[] { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1 };

    private static float[] Apply(float[] m, float[] p) => new[]
    {
        m[0] * p[0] + m[4] * p[1] + m[8] * p[2] + m[12],
        m[1] * p[0] + m[5] * p[1] + m[9] * p[2] + m[13],
        m[2] * p[0] + m[6] * p[1] + m[10] * p[2] + m[14],
    };
}
