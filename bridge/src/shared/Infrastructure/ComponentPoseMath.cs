using System;
namespace Bimwright.Ipt.Shared.Infrastructure;

public static class ComponentPoseMath
{
    // Input/output use Inventor's cm translation and column-vector convention.
    // Rotate around assembly-frame pivot, then apply assembly-frame translation.
    public static double[,] Apply(double[,] original, ComponentMoveRequest move)
    {
        var rotation = new double[,] {{1,0,0},{0,1,0},{0,0,1}};
        var pivot = new double[3];
        if (move.RotationAxis is { } a)
        {
            double angle = (move.RotationDegrees % 360) * Math.PI / 180;
            double c = Math.Cos(angle), s = Math.Sin(angle), t = 1-c;
            double x=a[0], y=a[1], z=a[2];
            rotation = new double[,] {{t*x*x+c,t*x*y-s*z,t*x*z+s*y},
                {t*x*y+s*z,t*y*y+c,t*y*z-s*x},{t*x*z-s*y,t*y*z+s*x,t*z*z+c}};
            for (int i=0;i<3;i++) pivot[i] = move.RotationCenterMm![i]/10;
        }
        var result = new double[4,4];
        var delta = new[] {move.X/10,move.Y/10,move.Z/10};
        for (int i=0;i<3;i++)
        {
            for (int j=0;j<3;j++)
                for (int k=0;k<3;k++) result[i,j] += rotation[i,k]*original[k,j];
            result[i,3] = pivot[i]+delta[i];
            for (int k=0;k<3;k++) result[i,3] += rotation[i,k]*(original[k,3]-pivot[k]);
            for (int j=0;j<4;j++)
                if (double.IsNaN(result[i,j]) || double.IsInfinity(result[i,j])) throw new ArgumentException("Transform overflow.");
        }
        result[3,3]=1;
        return result;
    }
}
