using System;
using InventorXrSo.Core.Backend;

namespace InventorXrSo.Core.Ui
{
    /// <summary>
    /// Trasformazione del model-root che stende il piano di schizzo sul tavolo (spec M6, «tavolo da disegno»).
    /// Convenzione: p_world = Position + Scale * R(Q) * p_local, con p_local in METRI nello spazio locale della mesh
    /// Unity, cioe coordinate modello/1000 con X specchiata (Handedness.FlipX del GLB, vedi <see cref="ToMeshLocal"/>).
    /// </summary>
    public struct SheetPose
    {
        public double Qx, Qy, Qz, Qw;
        /// <summary>Metri, mondo.</summary>
        public CadPoint Position;
        public double Scale;

        /// <summary>Applica la trasformazione a un punto locale (metri, X gia specchiata).</summary>
        public CadPoint Transform(CadPoint local)
        {
            var r = Rotate(local);
            return new CadPoint(Position.X + Scale * r.X, Position.Y + Scale * r.Y, Position.Z + Scale * r.Z);
        }

        /// <summary>Solo rotazione (vettori direzione), senza scala ne traslazione.</summary>
        public CadPoint Rotate(CadPoint v)
        {
            var q = new CadPoint(Qx, Qy, Qz);
            var t = q.Cross(v) * 2;
            var u = q.Cross(t);
            return new CadPoint(v.X + Qw * t.X + u.X, v.Y + Qw * t.Y + u.Y, v.Z + Qw * t.Z + u.Z);
        }
    }

    public static class SketchSheetLayout
    {
        /// <summary>Punto o direzione del modello CAD (mm) nello spazio locale della mesh Unity (metri, X specchiata).</summary>
        public static CadPoint ToMeshLocal(CadPoint modelMm) => new CadPoint(-modelMm.X / 1000, modelMm.Y / 1000, modelMm.Z / 1000);

        private static CadPoint FlipDir(CadPoint d) => new CadPoint(-d.X, d.Y, d.Z);

        /// <summary>
        /// Pose del foglio: il piano di schizzo diventa orizzontale a bench.DeskY, asse X -> bench.Right, asse Y ->
        /// bench.Forward, normale CAD (X x Y) -> +Y. Il mondo Unity e mancino e la mesh e specchiata in X: la rotazione
        /// e propria (mappa gli assi specchiati su Right/Forward) e la normale specchiata finisce su +Y.
        /// Il centro del foglio e origine + X*w/2 + Y*h/2 (estensioni misurate dall'origine dello schizzo, mm),
        /// posto a WorkbenchLayout.PartDistance davanti a bench.Origin. Scala = WorkbenchLayout.SheetScale; estensioni
        /// degeneri (&lt;= 0) danno scala 1 e centro = origine.
        /// </summary>
        public static SheetPose Compute(SketchFrame frame, double extentWidthMm, double extentHeightMm, WorkbenchFrame bench)
        {
            var a = FlipDir(frame.XAxis);
            var b = FlipDir(frame.YAxis);
            var n = a.Cross(b);
            var right = bench.Right;
            var fwd = bench.Forward;
            var up = right.Cross(fwd);

            // R = [right fwd up] * [a b n]^T  (matrice 3x3, r[row,col])
            double[,] r = new double[3, 3];
            double[] R = { right.X, right.Y, right.Z }, F = { fwd.X, fwd.Y, fwd.Z }, U = { up.X, up.Y, up.Z };
            double[] A = { a.X, a.Y, a.Z }, B = { b.X, b.Y, b.Z }, N = { n.X, n.Y, n.Z };
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                    r[i, j] = R[i] * A[j] + F[i] * B[j] + U[i] * N[j];

            double qw, qx, qy, qz;
            double tr = r[0, 0] + r[1, 1] + r[2, 2];
            if (tr > 0)
            {
                double s = Math.Sqrt(tr + 1) * 2;
                qw = 0.25 * s; qx = (r[2, 1] - r[1, 2]) / s; qy = (r[0, 2] - r[2, 0]) / s; qz = (r[1, 0] - r[0, 1]) / s;
            }
            else if (r[0, 0] > r[1, 1] && r[0, 0] > r[2, 2])
            {
                double s = Math.Sqrt(1 + r[0, 0] - r[1, 1] - r[2, 2]) * 2;
                qw = (r[2, 1] - r[1, 2]) / s; qx = 0.25 * s; qy = (r[0, 1] + r[1, 0]) / s; qz = (r[0, 2] + r[2, 0]) / s;
            }
            else if (r[1, 1] > r[2, 2])
            {
                double s = Math.Sqrt(1 + r[1, 1] - r[0, 0] - r[2, 2]) * 2;
                qw = (r[0, 2] - r[2, 0]) / s; qx = (r[0, 1] + r[1, 0]) / s; qy = 0.25 * s; qz = (r[1, 2] + r[2, 1]) / s;
            }
            else
            {
                double s = Math.Sqrt(1 + r[2, 2] - r[0, 0] - r[1, 1]) * 2;
                qw = (r[1, 0] - r[0, 1]) / s; qx = (r[0, 2] + r[2, 0]) / s; qy = (r[1, 2] + r[2, 1]) / s; qz = 0.25 * s;
            }

            bool valid = extentWidthMm > 0 && extentHeightMm > 0;
            var centreMm = valid
                ? frame.OriginMm + frame.XAxis * (extentWidthMm / 2) + frame.YAxis * (extentHeightMm / 2)
                : frame.OriginMm;
            double scale = WorkbenchLayout.SheetScale(extentWidthMm / 1000, extentHeightMm / 1000);

            var pose = new SheetPose { Qx = qx, Qy = qy, Qz = qz, Qw = qw, Scale = scale, Position = new CadPoint(0, 0, 0) };
            var rotated = pose.Rotate(ToMeshLocal(centreMm));
            var target = bench.Origin + fwd * WorkbenchLayout.PartDistance;
            pose.Position = new CadPoint(target.X - scale * rotated.X, bench.DeskY - scale * rotated.Y, target.Z - scale * rotated.Z);
            return pose;
        }
    }
}
