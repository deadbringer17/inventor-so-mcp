using System;

namespace Bimwright.Ipt.Shared.Handlers.Core;

/// <summary>Initial circle through an endpoint and target, continuing the outgoing tangent. Millimetres.</summary>
internal readonly struct TangentArcGeometry
{
    internal double CenterX { get; }
    internal double CenterY { get; }
    internal double Radius { get; }
    internal bool CounterClockwise { get; }
    private TangentArcGeometry(double x,double y,double radius,bool counterClockwise)
    { CenterX=x;CenterY=y;Radius=radius;CounterClockwise=counterClockwise; }

    internal static TangentArcGeometry Create(double x,double y,double tangentX,double tangentY,double endX,double endY)
    {
        foreach(double value in new[]{x,y,tangentX,tangentY,endX,endY})
            if(!Finite(value)) throw new ArgumentException("Tangent arc coordinates and tangent must be finite.");
        double tangentScale=Math.Max(Math.Abs(tangentX),Math.Abs(tangentY));
        if(tangentScale==0) throw new ArgumentException("The selected endpoint has no tangent direction.");
        double tx=tangentX/tangentScale,ty=tangentY/tangentScale;
        double norm=Math.Sqrt(tx*tx+ty*ty);tx/=norm;ty/=norm;
        double dx=endX-x,dy=endY-y;
        if(!Finite(dx)||!Finite(dy)) throw new ArgumentException("Tangent arc extent is not finite.");
        double scale=Math.Max(Math.Abs(dx),Math.Abs(dy));
        if(scale<=1e-6) throw new ArgumentException("Select distinct tangent arc endpoints.");
        double sx=dx/scale,sy=dy/scale;
        double normalProjection=-ty*sx+tx*sy;
        if(Math.Abs(normalProjection)<=1e-10)
            throw new ArgumentException("The target is collinear with the endpoint tangent; no finite arc exists.");
        double signedRadius=scale*(sx*sx+sy*sy)/(2*normalProjection);
        double cx=x-ty*signedRadius,cy=y+tx*signedRadius,radius=Math.Abs(signedRadius);
        if(!Finite(cx)||!Finite(cy)||!Finite(radius)||radius<=1e-6)
            throw new ArgumentException("Tangent arc circle is outside the finite geometry domain.");
        return new TangentArcGeometry(cx,cy,radius,signedRadius>0);
    }
    private static bool Finite(double value)=>!double.IsNaN(value)&&!double.IsInfinity(value);
}
