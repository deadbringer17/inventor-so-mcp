using System;
using InventorXrSo.Core.Glb;

namespace InventorXrSo.Core.Backend
{
    /// <summary>
    /// Calibrates the flange handle direction against the flange Inventor itself computed (the preview GLB).
    /// Pure and read-only: it never changes the draft nor writes CAD; on ambiguous evidence it says so and the
    /// caller keeps its heuristic. Preview GLB positions are metres in glTF axes, which are the CAD axes
    /// (the Unity mirror of X is applied only at render time, as in <c>CadCoordinates</c>), so
    /// CAD millimetres = GLB metres x 1000 with no axis change.
    /// </summary>
    public static class FlangeDirection
    {
        /// <summary>Preview material must reach this fraction of the height (a 45 degree flange projects to 0.71).</summary>
        public const double MinExtentRatio = 0.5;
        /// <summary>...and not exceed this one (a longer arm is the base sheet, not the flange).</summary>
        public const double MaxExtentRatio = 1.3;
        /// <summary>The losing directions must stay below this fraction of the height.</summary>
        public const double RivalRatio = 0.35;
        public const double MinConfidence = 0.5;

        /// <param name="preview">Previewed part, same coordinates as the scene.</param>
        /// <param name="originMm">Edge midpoint on the base sheet.</param>
        /// <param name="edgeDirection">Direction of the edge (need not be unit).</param>
        /// <param name="edgeLengthMm">Length of the edge span used as slab length.</param>
        /// <param name="candidateAxis">Heuristic axis; its sign (and, if <paramref name="considerPerpendicular"/>, its plane) is what gets checked.</param>
        /// <param name="heightMm">Flange height of the draft that produced the preview.</param>
        /// <param name="thicknessMm">Sheet thickness when known: vertices closer than 1.2x this to the sheet plane are ignored.</param>
        /// <param name="considerPerpendicular">When the candidate came from the fallback, also test the perpendicular in the plane orthogonal to the edge.</param>
        public static bool Calibrate(GlbModel preview, CadPoint originMm, CadPoint edgeDirection, double edgeLengthMm, CadPoint candidateAxis,
            double heightMm, out CadPoint axis, out double confidence, double? thicknessMm = null, bool considerPerpendicular = false)
        {
            axis = candidateAxis; confidence = 0;
            if (preview == null || heightMm <= 0 || double.IsNaN(heightMm) || double.IsInfinity(heightMm)) return false;
            if (edgeLengthMm <= 0 || double.IsNaN(edgeLengthMm) || double.IsInfinity(edgeLengthMm)) return false;
            if (edgeDirection.Length < 1e-9) return false;
            var e = edgeDirection * (1 / edgeDirection.Length);
            var a = candidateAxis - e * candidateAxis.Dot(e);
            if (a.Length < 1e-6) return false;
            a = a * (1 / a.Length);
            var p = e.Cross(a);
            double exclusion = thicknessMm.HasValue && thicknessMm.Value > 0 ? thicknessMm.Value * 1.2 : 0;
            double halfSpan = edgeLengthMm * 0.5 * 0.9, cap = heightMm * 1.5 + exclusion;

            // extent[0..3] = +a, -a, +p, -p : how far preview material reaches beyond the exclusion, inside the slab.
            var extent = new double[4];
            foreach (var primitive in preview.Primitives)
            {
                if (primitive == null || !primitive.Visible) continue;
                var v = primitive.Positions;
                for (int i = 0; i + 2 < v.Length; i += 3)
                {
                    double rx = v[i] * 1000.0 - originMm.X, ry = v[i + 1] * 1000.0 - originMm.Y, rz = v[i + 2] * 1000.0 - originMm.Z;
                    double s = rx * e.X + ry * e.Y + rz * e.Z;
                    if (Math.Abs(s) > halfSpan) continue;
                    double u = rx * a.X + ry * a.Y + rz * a.Z, w = rx * p.X + ry * p.Y + rz * p.Z;
                    if (Math.Abs(u) > cap || Math.Abs(w) > cap) continue;
                    if (u > exclusion) extent[0] = Math.Max(extent[0], u);
                    if (-u > exclusion) extent[1] = Math.Max(extent[1], -u);
                    if (considerPerpendicular)
                    {
                        if (w > exclusion) extent[2] = Math.Max(extent[2], w);
                        if (-w > exclusion) extent[3] = Math.Max(extent[3], -w);
                    }
                }
            }

            int count = considerPerpendicular ? 4 : 2, best = -1, qualifying = 0;
            for (int i = 0; i < count; i++)
            {
                double ratio = extent[i] / heightMm;
                if (ratio < MinExtentRatio || ratio > MaxExtentRatio) continue;
                qualifying++; best = i;
            }
            if (qualifying != 1) return false; // neither side, or both sides: ambiguous
            double rival = 0;
            for (int i = 0; i < count; i++) if (i != best) rival = Math.Max(rival, extent[i] / heightMm);
            if (rival >= RivalRatio) return false;
            double own = extent[best] / heightMm;
            confidence = Math.Min(1, own / 0.9) * (1 - rival / RivalRatio * 0.5);
            if (confidence < MinConfidence) { confidence = 0; return false; }
            axis = best == 0 ? a : best == 1 ? a * -1 : best == 2 ? p : p * -1;
            return true;
        }
    }
}
