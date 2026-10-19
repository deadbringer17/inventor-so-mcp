using System;

namespace InventorXrSo.Core.Backend
{
    /// <summary>Collects signed arc sweep from successive points in native sketch millimeters.</summary>
    public sealed class NativeSketchArcInput
    {
        private readonly CadPoint _center;
        private double? _lastAngle;
        private double _sweep;
        public double RadiusMm {get;}
        public double StartDegrees {get;}
        public double SweepDegrees=>_sweep;
        public NativeSketchArcInput(CadPoint center,CadPoint start)
        {
            _center=center;double x=start.X-center.X,y=start.Y-center.Y;
            RadiusMm=Math.Sqrt(x*x+y*y);
            if(RadiusMm<=1e-9 || RadiusMm>100000) throw new ArgumentException("Indica un punto iniziale distinto dal centro, entro un raggio di 100.000 mm.");
            StartDegrees=Math.Atan2(y,x)*180/Math.PI;_lastAngle=StartDegrees;
        }
        private static double Shortest(double angle)
        {angle%=360;if(angle>180) angle-=360;if(angle<=-180) angle+=360;return angle;}
        public void Suspend()=>_lastAngle=null;
        public bool Observe(CadPoint point)
        {
            double x=point.X-_center.X,y=point.Y-_center.Y;
            if(x*x+y*y<=1e-18) {Suspend();return false;}
            double angle=Math.Atan2(y,x)*180/Math.PI;
            if(_lastAngle.HasValue)
            {
                double step=Shortest(angle-_lastAngle.Value);
                if(Math.Abs(step-180)<1e-9 && _sweep<0) step=-180;
                _sweep+=step;
            }
            else
            {
                double next=Shortest(angle-StartDegrees);
                if(_sweep>1e-9 && next<0) next+=360;
                else if(_sweep< -1e-9 && next>0) next-=360;
                _sweep=next;
            }
            _lastAngle=angle;return true;
        }
        public bool TryEnd(CadPoint point,out double sweep)
        {bool valid=Observe(point);sweep=_sweep;return valid && Math.Abs(sweep)>=1e-9 && Math.Abs(sweep)<360-1e-9;}
    }
}
