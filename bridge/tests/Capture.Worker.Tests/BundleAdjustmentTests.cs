using Inventor.So.Capture.Worker;
using Inventor.So.RealityCapture;

namespace Capture.Worker.Tests;

public sealed class BundleAdjustmentTests
{
    [Fact]
    public void TriangulationPreservesMetresAndRejectsPureRotation()
    {
        var a=Camera(0,-0.15); var b=Camera(1,0.15); var truth=new V3(0.12,0.08,2);
        var p=a.Project(truth); var q=b.Project(truth);
        Assert.True(FeatureReconstructor.TryTriangulate(a,p.u,p.v,b,q.u,q.v,out var measured));
        Assert.True((measured-truth).Norm<1e-9);
        Assert.False(FeatureReconstructor.TryTriangulate(a,p.u,p.v,a,p.u,p.v,out _));
    }

    [Fact]
    public void JointSolveReducesReprojectionAndDistanceResidualWithoutUsingValidation()
    {
        var truthCameras=new[] { Camera(0,-0.15),Camera(1,0),Camera(2,0.15) };
        var cameras=new[] { Camera(0,-0.15),Camera(1,0.008),Camera(2,0.16) };
        var points=new List<BundlePoint>();
        for(int i=0;i<30;i++)
        {
            var truth=new V3((i%6-2.5)*0.07,(i/6-2)*0.05,1.8+(i%3)*0.03);
            var observations=truthCameras.Select((c,j)=> { var p=c.Project(truth); return new Observation(j,p.u,p.v); }).ToList();
            points.Add(new BundlePoint("p"+i,truth+new V3(0.003,-0.004,0.015),new byte[] {128,128,128},observations));
        }
        double known=(new V3(-2.5*0.07,-2*0.05,1.8)-new V3(2.5*0.07,-2*0.05,1.86)).Norm*1000;
        var calibration=new MetricConstraint("cal","distance",new[] {"p0","p5"},known,0.5);
        var absurdValidation=new MetricConstraint("validation","distance",new[] {"p6","p11"},50000,0.001,"validation",true,true);
        var result=ConstrainedBundleAdjuster.Solve(cameras,points,new[] {calibration,absurdValidation},maxIterations:30);
        Assert.True(result.FinalCost<result.InitialCost*0.1,$"cost {result.InitialCost} -> {result.FinalCost}");
        Assert.True(result.ReprojectionRmsPixels<0.2,$"rms {result.ReprojectionRmsPixels}");
        double distance=(points[0].Position-points[5].Position).Norm*1000;
        Assert.InRange(Math.Abs(distance-known),0,0.6);
        Assert.True((points[6].Position-points[11].Position).Norm<1); // impossible validation was not fitted
    }

    [Fact]
    public void IndependentValidationNeverReusesCalibrationGeometryOrPromotesSyntheticEvidence()
    {
        var points=Enumerable.Range(0,8).ToDictionary(i=>"p"+i,i=>new V3(i*100,0,0));
        var constraints=new[] {
            new MetricConstraint("cal","distance",new[] {"p0","p1"},100,0.1),
            new MetricConstraint("duplicate","distance",new[] {"p1","p0"},100,0.1,"validation",true,true),
            new MetricConstraint("v1","distance",new[] {"p2","p3"},100,0.1,"validation",true,true,MeasurementId:"measurement-1"),
            new MetricConstraint("v2","distance",new[] {"p4","p5"},100,0.1,"validation",true,true,MeasurementId:"measurement-2"),
            new MetricConstraint("v3","distance",new[] {"p6","p7"},100,0.1,"validation",true,true,MeasurementId:"measurement-3")
        };
        var synthetic=MetricQualityEvaluator.Evaluate(constraints,points,true,true,true);
        Assert.Equal(3,synthetic.IndependentValidationCount); Assert.NotEqual("VALIDATED",synthetic.AccuracyStatus);
        Assert.NotEqual("VALIDATED",MetricQualityEvaluator.Evaluate(constraints,points,false,true).AccuracyStatus);
        Assert.Equal("VALIDATED",MetricQualityEvaluator.Evaluate(constraints,points,false,true,true).AccuracyStatus);
        var correlated=constraints.Select(c=>c.Role=="validation"?c with {MeasurementId="same-measurement"}:c).ToArray();
        Assert.NotEqual("VALIDATED",MetricQualityEvaluator.Evaluate(correlated,points,false,true,true).AccuracyStatus);
        var reused=constraints.Select(c=>c.Id=="cal"?c with {MeasurementId="measurement-1"}:c).ToArray();
        Assert.Equal(2,MetricQualityEvaluator.Evaluate(reused,points,false,true,true).IndependentValidationCount);
    }

    [Fact]
    public void ContradictoryDimensionsRemainConflictsAndDiameterRequiresNonCollinearRim()
    {
        var points=new Dictionary<string,V3> { ["a"]=new(0,0,0),["b"]=new(100,0,0),["c"]=new(50,50,0) };
        var constraints=new[] { new MetricConstraint("one","distance",new[] {"a","b"},100,0.1),new MetricConstraint("two","distance",new[] {"a","b"},110,0.1) };
        var quality=MetricQualityEvaluator.Evaluate(constraints,points,false,true,true);
        Assert.True(quality.Conflicts); Assert.Equal("METRIC",quality.AccuracyStatus);
        Assert.Contains(quality.Constraints,c=>c.Status=="conflict");
        var diameter=new MetricConstraint("diameter","diameter",new[] {"a","b","c"},100,0.1);
        Assert.InRange(MetricQualityEvaluator.Measure(diameter,points),99.999,100.001);
        points["c"]=new(50,0,0);
        Assert.Throws<CaptureSolveException>(()=>MetricQualityEvaluator.Measure(diameter,points));
        points["c"]=new(double.NaN,0,0);
        Assert.Throws<CaptureSolveException>(()=>MetricQualityEvaluator.Measure(diameter,points));
    }
    private static BundleCamera Camera(int sequence,double x)=>new(sequence,640,480,520,520,320,240,new double[] {0,0,0,-x,0,0});
}
