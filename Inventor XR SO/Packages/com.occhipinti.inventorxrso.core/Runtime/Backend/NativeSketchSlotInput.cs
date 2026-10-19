using System;

namespace InventorXrSo.Core.Backend
{
    public static class NativeSketchSlotInput
    {
        /// <summary>Desktop order: start, end, then a point on the center arc.</summary>
        public static bool TryThreePointArc(CadPoint start,CadPoint end,CadPoint through,out CadPoint center,out double sweep)
        {
            center=default;sweep=0;
            double ax=end.X-start.X,ay=end.Y-start.Y,bx=through.X-start.X,by=through.Y-start.Y;
            double determinant=2*(ax*by-ay*bx),a2=ax*ax+ay*ay,b2=bx*bx+by*by;
            if(a2<=1e-18 || b2<=1e-18 || Math.Abs(determinant)<=1e-12*Math.Max(a2,b2)) return false;
            double x=(a2*by-b2*ay)/determinant,y=(ax*b2-bx*a2)/determinant;
            double radius=Math.Sqrt(x*x+y*y);
            if(!Finite(radius) || radius<=1e-9 || radius>100000) return false;
            center=new CadPoint(start.X+x,start.Y+y);
            double initial=Math.Atan2(-y,-x),last=Math.Atan2(ay-y,ax-x),middle=Math.Atan2(by-y,bx-x);
            double endAngle=Positive(last-initial),throughAngle=Positive(middle-initial);
            sweep=(throughAngle<=endAngle ? endAngle : endAngle-2*Math.PI)*180/Math.PI;
            return Math.Abs(sweep)>1e-9 && Math.Abs(sweep)<360-1e-9;
        }

        /// <summary>Twice the distance to the finite center arc, including its end caps.</summary>
        public static bool TryArcWidth(CadPoint center,CadPoint start,double sweep,CadPoint point,out double width)
        {
            width=0;double sx=start.X-center.X,sy=start.Y-center.Y,radius=Math.Sqrt(sx*sx+sy*sy);
            if(!Finite(sweep) || Math.Abs(sweep)<=1e-9 || Math.Abs(sweep)>=360-1e-9 || radius<=1e-9 || radius>100000) return false;
            double px=point.X-center.X,py=point.Y-center.Y,angle=Math.Atan2(sy,sx),radians=sweep*Math.PI/180;
            double relative=Positive(Math.Sign(sweep)*(Math.Atan2(py,px)-angle));
            if(relative<=Math.Abs(radians)) width=2*Math.Abs(Math.Sqrt(px*px+py*py)-radius);
            else
            {
                double ex=radius*Math.Cos(angle+radians),ey=radius*Math.Sin(angle+radians);
                double start2=(px-sx)*(px-sx)+(py-sy)*(py-sy),end2=(px-ex)*(px-ex)+(py-ey)*(py-ey);
                width=2*Math.Sqrt(Math.Min(start2,end2));
            }
            return Finite(width) && width>1e-9 && width<=100000;
        }

        private static double Positive(double angle) {angle%=2*Math.PI;return angle<0 ? angle+2*Math.PI : angle;}
        private static bool Finite(double value)=>!double.IsNaN(value) && !double.IsInfinity(value);

        /// <summary>Derives native straight-slot width from its third desktop-style point.</summary>
        public static bool TryWidth(string variant,CadPoint first,CadPoint second,CadPoint point,out double width)
        {
            width=0;double dx=second.X-first.X,dy=second.Y-first.Y,length=Math.Sqrt(dx*dx+dy*dy);
            if(length<=1e-9) return false;
            double ux=dx/length,uy=dy/length,px=point.X-first.X,py=point.Y-first.Y;
            double along=px*ux+py*uy,across=px*uy-py*ux;
            if(variant=="slot_overall") width=2*Math.Abs(across);
            else
            {
                double low;
                if(variant=="slot_center_to_center") low=0;
                else if(variant=="slot_center") low=-length;
                else return false;
                double nearest=Math.Max(low,Math.Min(length,along));
                double excess=along-nearest;width=2*Math.Sqrt(excess*excess+across*across);
            }
            return width>1e-9 && width<=100000 && !double.IsNaN(width) && !double.IsInfinity(width);
        }
    }
}
