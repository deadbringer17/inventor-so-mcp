using InventorXrSo.Core.Capture;

namespace XrSo.Core.Tests;

public sealed class ScanRegistrationTests
{
    private static readonly double[][] Source={new[]{10d,20,30},new[]{110d,20,30},new[]{10d,220,30}};
    private static double[] Known=>new double[]{0,-1,0,500,1,0,0,-200,0,0,-1,900,0,0,0,1};
    [Fact] public void ThreePointsMapsLHScanToRHInventorWithoutScaleAndPreservesAnOffPlanePoint()
    {
        var target=Source.Select(p=>ScanRegistration.Point(Known,p)).ToArray();var fit=ScanRegistration.ThreePoints(Source,target);
        ScanRegistration.Validate(fit,-1);for(int i=0;i<16;i++) Assert.Equal(Known[i],fit[i],7);
        var fourth=new[]{25d,33,400};Assert.Equal(ScanRegistration.Point(Known,fourth),ScanRegistration.Point(fit,fourth));
        var inverse=ScanRegistration.Inverse(fit);var round=ScanRegistration.Point(inverse,ScanRegistration.Point(fit,fourth));for(int i=0;i<3;i++) Assert.Equal(fourth[i],round[i],7);
    }
    [Fact] public void PhysicalRegistrationRemainsLHAndOverlayCompositionMapsCadMetresBackToTracking()
    {
        var trackingMatrix=ScanRegistration.Identity;trackingMatrix[3]=1000;trackingMatrix[7]=2000;
        var tracking=Source.Select(p=>ScanRegistration.Point(trackingMatrix,p)).ToArray();var physical=ScanRegistration.ThreePoints(Source,tracking,false);
        ScanRegistration.Validate(physical,1);
        var overlay=ScanRegistration.Multiply(ScanRegistration.Multiply(ScanRegistration.InMetres(physical),ScanRegistration.InMetres(ScanRegistration.Inverse(Known))),ScanRegistration.ScanToCadBasis);
        var cad=ScanRegistration.Point(Known,Source[0]);var cadUnityMetres=new[]{cad[0]/1000,cad[1]/1000,-cad[2]/1000};var world=ScanRegistration.Point(overlay,cadUnityMetres);
        for(int i=0;i<3;i++) Assert.Equal(tracking[0][i]/1000,world[i],8);ScanRegistration.Validate(overlay,1);
    }
    [Fact] public void PlaneAxisIncludesYawAndOriginAndRefusesAnUnobservableNormalAxis()
    {
        var origin=new[]{12d,34,56};var n=new[]{0d,0,1};var x=new[]{1d,0,0};
        var fit=ScanRegistration.PlaneAxis(origin,n,x,ScanRegistration.Point(Known,origin),ScanRegistration.Direction(Known,n),ScanRegistration.Direction(Known,x));
        for(int i=0;i<16;i++) Assert.Equal(Known[i],fit[i],7);
        Assert.Throws<ArgumentException>(()=>ScanRegistration.PlaneAxis(origin,n,n,origin,n,n));
        Assert.Throws<ArgumentException>(()=>ScanRegistration.PlaneAxis(origin,n,x,origin,n,new[]{1d,0,1}));
    }
    [Fact] public void ScaleShearCollinearityNaNsAndIncompatibleDistancesAreRejected()
    {
        var matrix=ScanRegistration.Identity;matrix[0]=2;Assert.Throws<ArgumentException>(()=>ScanRegistration.Inverse(matrix));
        Assert.Throws<ArgumentException>(()=>ScanRegistration.ThreePoints(Source,Source.Select(p=>p.Select(n=>n*2).ToArray()).ToArray()));
        var line=new[]{new[]{0d,0,0},new[]{100d,0,0},new[]{200d,0,0}};Assert.Throws<ArgumentException>(()=>ScanRegistration.ThreePoints(line,line));
        matrix=ScanRegistration.Identity;matrix[3]=double.NaN;Assert.Throws<ArgumentException>(()=>ScanRegistration.Validate(matrix));
        Assert.Throws<ArgumentException>(()=>ScanRegistration.Validate(ScanRegistration.ScanToCadBasis,1));
    }
}
