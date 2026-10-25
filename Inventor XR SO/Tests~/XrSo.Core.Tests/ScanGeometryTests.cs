using InventorXrSo.Core.Capture;

namespace XrSo.Core.Tests;

public sealed class ScanGeometryTests
{
    private static ScanPoint P(int id,double x,double y,double z)=>new(){Id="p"+id,Position=new[]{x,y,z},Observations=3};
    [Fact]
    public void ObliquePlanePointDistanceAndParallelPlaneMeasurementRemainMillimetres()
    {
        var points=Enumerable.Range(0,25).Select(i=>P(i,i%5*20,i/5*20,100+i%5*10)).ToArray();
        var plane=ScanGeometry.Fit("plane","plane",points);
        Assert.InRange(plane.FitRms,0,1e-8);Assert.Null(plane.Uncertainty);Assert.Equal("estimated",plane.Status);
        var shifted=ScanGeometry.Fit("shift","plane",points.Select((p,i)=>P(i,p.Position[0],p.Position[1],p.Position[2]+50)).ToArray());
        Assert.Equal(50/Math.Sqrt(1.25),ScanGeometry.ParallelPlanes(plane,shifted),6);
        Assert.Equal(0,ScanGeometry.PointPlane(points[0].Position,plane),6);
    }
    [Fact]
    public void ObliqueCircularRimHasCorrectCentreRadiusAndNoClaimedUncertainty()
    {
        var points=Enumerable.Range(0,48).Select(i=>{double a=i*Math.PI/24;return P(i,120+50*Math.Cos(a),-70+30*Math.Sin(a),400+40*Math.Sin(a));}).ToArray();
        var circle=ScanGeometry.Fit("rim","circle",points);
        Assert.Equal(50,circle.Radius,6);Assert.InRange(circle.FitRms,0,1e-7);
        Assert.Equal(120,circle.Origin[0],6);Assert.Equal(-70,circle.Origin[1],6);Assert.Equal(400,circle.Origin[2],6);
        Assert.Equal(points.Select(p=>p.Id),circle.SupportIds);Assert.Null(circle.Uncertainty);
    }
    [Fact]
    public void CylinderFitsSpatialSupportIncludingAnObliqueAxis()
    {
        var points=Enumerable.Range(0,144).Select(i=>{double a=i%24*Math.PI/12,z=(i/24-2.5)*20;
            double x=30*Math.Cos(a),y=30*Math.Sin(a);return P(i,100+x*.8+z*.6,200+y,300-x*.6+z*.8);}).ToArray();
        var cylinder=ScanGeometry.Fit("cyl","cylinder",points);
        Assert.Equal(30,cylinder.Radius,1);Assert.Equal(100,cylinder.Length,1);Assert.InRange(cylinder.FitRms,0,.1);
        Assert.Equal(1,Math.Abs(cylinder.Direction[0]*.6+cylinder.Direction[2]*.8),3);
    }
    [Fact]
    public void LineAndSinglePointAndAnglesAreExplicit()
    {
        var line=ScanGeometry.Fit("line","axis",new[]{P(0,0,0,0),P(1,0,100,0),P(2,0,200,0)});
        Assert.Equal(200,line.Length,6);Assert.Equal(0,line.FitRms,6);
        var other=ScanGeometry.Fit("line2","axis",new[]{P(0,0,0,0),P(1,100,0,0)});
        Assert.Equal(90,ScanGeometry.AngleDegrees(line,other),6);
        var point=ScanGeometry.Fit("point","point",new[]{P(0,13,24,35)});Assert.Equal(new[]{13d,24,35},point.Origin);
        Assert.Equal(1000,ScanGeometry.Distance(new[]{0d,0,0},new[]{0d,0,1000}));
    }
    [Theory]
    [InlineData("plane")][InlineData("circle")][InlineData("cylinder")]
    public void CollinearSupportIsRejected(string kind)=>Assert.Throws<ArgumentException>(()=>ScanGeometry.Fit("bad",kind,Enumerable.Range(0,24).Select(i=>P(i,i,0,0)).ToArray()));
    [Fact]
    public void ShortArcsFlatCylindersDuplicateIdsAndNonfinitePointsAreRejected()
    {
        var arc=Enumerable.Range(0,24).Select(i=>P(i,30*Math.Cos(i*.01),30*Math.Sin(i*.01),0)).ToArray();
        Assert.Throws<ArgumentException>(()=>ScanGeometry.Fit("bad","circle",arc));
        var ring=Enumerable.Range(0,24).Select(i=>P(i,30*Math.Cos(i*Math.PI/12),30*Math.Sin(i*Math.PI/12),i%2*.01)).ToArray();
        Assert.Throws<ArgumentException>(()=>ScanGeometry.Fit("bad","cylinder",ring));
        Assert.Throws<ArgumentException>(()=>ScanGeometry.Fit("bad","axis",new[]{P(0,0,0,0),P(0,1,0,0)}));
        Assert.Throws<ArgumentException>(()=>ScanGeometry.Fit("bad","point",new[]{P(0,double.NaN,0,0)}));
    }
}
