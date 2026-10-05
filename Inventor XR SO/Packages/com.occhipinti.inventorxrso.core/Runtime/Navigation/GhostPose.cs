using System;

namespace InventorXrSo.Core.Navigation
{
    /// <summary>
    /// Posa dell'assieme fantasma. La parte aperta vive nella propria origine (identita); nell'assieme padre la stessa parte e
    /// posata dalla matrice dell'occorrenza M (parte -> assieme, glTF, column-major, metri). Per far coincidere la parte con la sua
    /// occorrenza il fantasma (la scena del padre) va trasformato con M^-1: M^-1 * (M * p) = p.
    /// L'inversione commuta con la coniugazione di lateralita S*M*S, quindi si inverte in glTF e poi si converte come ogni altra matrice.
    /// </summary>
    public static class GhostPose
    {
        public static readonly float[] Identity = { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1 };

        /// <summary>Inversa di una matrice affine 4x4 column-major (ultima riga 0,0,0,1). Pose null o non invertibile -> identita.</summary>
        public static float[] Inverse(float[] m)
        {
            if (m == null || m.Length != 16) return (float[])Identity.Clone();
            // A (3x3) in colonne: a[r,c] = m[c*4+r]
            double a00 = m[0], a10 = m[1], a20 = m[2];
            double a01 = m[4], a11 = m[5], a21 = m[6];
            double a02 = m[8], a12 = m[9], a22 = m[10];
            double tx = m[12], ty = m[13], tz = m[14];
            double c00 = a11 * a22 - a12 * a21, c01 = a12 * a20 - a10 * a22, c02 = a10 * a21 - a11 * a20;
            double det = a00 * c00 + a01 * c01 + a02 * c02;
            if (Math.Abs(det) < 1e-12) return (float[])Identity.Clone();
            double inv = 1.0 / det;
            double i00 = c00 * inv, i01 = (a02 * a21 - a01 * a22) * inv, i02 = (a01 * a12 - a02 * a11) * inv;
            double i10 = c01 * inv, i11 = (a00 * a22 - a02 * a20) * inv, i12 = (a02 * a10 - a00 * a12) * inv;
            double i20 = c02 * inv, i21 = (a01 * a20 - a00 * a21) * inv, i22 = (a00 * a11 - a01 * a10) * inv;
            return new[]
            {
                (float)i00, (float)i10, (float)i20, 0,
                (float)i01, (float)i11, (float)i21, 0,
                (float)i02, (float)i12, (float)i22, 0,
                (float)-(i00 * tx + i01 * ty + i02 * tz), (float)-(i10 * tx + i11 * ty + i12 * tz), (float)-(i20 * tx + i21 * ty + i22 * tz), 1,
            };
        }
    }
}
