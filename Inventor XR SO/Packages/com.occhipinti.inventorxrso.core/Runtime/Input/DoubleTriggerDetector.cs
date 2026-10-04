using System;

namespace InventorXrSo.Core.Input
{
    /// <summary>
    /// Riconosce il doppio Trigger: due pressioni entro <see cref="WindowSeconds"/> sullo stesso
    /// bersaglio, con la direzione del raggio spostata meno di <see cref="MaxAngleDegrees"/>.
    /// </summary>
    public sealed class DoubleTriggerDetector
    {
        public const double WindowSeconds = 0.350, MaxAngleDegrees = 2.0;

        private bool _pending;
        private double _time;
        private string _target;
        private double _dx, _dy, _dz;

        /// <summary>target = id stabile del bersaglio colpito (null se nulla); direction = direzione del raggio.
        /// Restituisce true alla SECONDA pressione valida; poi si riarma (la terza è una prima).</summary>
        public bool Press(double time, string target, double dx, double dy, double dz)
        {
            if (target == null)
            {
                Reset();
                return false;
            }

            if (_pending
                && time - _time <= WindowSeconds
                && string.Equals(_target, target, StringComparison.Ordinal)
                && AngleDegrees(_dx, _dy, _dz, dx, dy, dz) < MaxAngleDegrees)
            {
                Reset();
                return true;
            }

            _pending = true;
            _time = time;
            _target = target;
            _dx = dx; _dy = dy; _dz = dz;
            return false;
        }

        /// <summary>0..1 per l'anellino della legenda, 0 se nessuna prima pressione o finestra scaduta.</summary>
        public double PendingProgress(double now)
        {
            if (!_pending) return 0;
            var elapsed = now - _time;
            if (elapsed < 0 || elapsed > WindowSeconds) return 0;
            return elapsed / WindowSeconds;
        }

        public void Reset()
        {
            _pending = false;
            _target = null;
        }

        private static double AngleDegrees(double ax, double ay, double az, double bx, double by, double bz)
        {
            var la = Math.Sqrt(ax * ax + ay * ay + az * az);
            var lb = Math.Sqrt(bx * bx + by * by + bz * bz);
            if (la < 1e-12 || lb < 1e-12) return 180.0;
            var c = (ax * bx + ay * by + az * bz) / (la * lb);
            if (c > 1) c = 1; else if (c < -1) c = -1;
            return Math.Acos(c) * 180.0 / Math.PI;
        }
    }
}
