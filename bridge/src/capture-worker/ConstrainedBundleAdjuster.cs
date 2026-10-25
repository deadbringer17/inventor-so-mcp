using Inventor.So.RealityCapture;

namespace Inventor.So.Capture.Worker;

public sealed record BundleAdjustmentResult(bool Converged,int Iterations,double InitialCost,double FinalCost,double ReprojectionRmsPixels);

/// <summary>
/// Joint camera/point LM, fixed calibrated intrinsics, tracking pose priors and metric residuals.
/// Matrix-free sparse normal equations avoid allocating a dense matrix proportional to all 3D points.
/// Validation dimensions are deliberately excluded from the objective.
/// </summary>
public static class ConstrainedBundleAdjuster
{
    private sealed record Row(double Residual,int[] Indices,double[] Jacobian);
    public static BundleAdjustmentResult Solve(IReadOnlyList<BundleCamera> cameras,IReadOnlyList<BundlePoint> points,
        IReadOnlyList<MetricConstraint> constraints,int maxIterations = 20,CancellationToken ct = default)
    {
        if (cameras.Count < 2 || points.Count < 6 || cameras.Count>60 || points.Count>2000) throw new CaptureSolveException("CAPTURE_INSUFFICIENT_GEOMETRY");
        var pointIndex=points.Select((p,i)=>(p.Id,i)).ToDictionary(p=>p.Id,p=>p.i,StringComparer.Ordinal);
        foreach (var c in constraints) c.Validate();
        var calibration=constraints.Where(c=>c.Enabled && c.Role=="calibration" && c.PointIds.All(pointIndex.ContainsKey)).ToArray();
        int offset=cameras.Count*6,n=offset+points.Count*3;
        var x=new double[n];
        for(int i=0;i<cameras.Count;i++) Array.Copy(cameras[i].Pose,0,x,i*6,6);
        for(int i=0;i<points.Count;i++) Array.Copy(points[i].Position.Array,0,x,offset+i*3,3);
        var rows=Build(x,cameras,points,calibration,pointIndex); double cost=Cost(rows),initial=cost,lambda=0.001;
        bool converged=false; int iteration=0;
        for(;iteration<maxIterations;iteration++)
        {
            ct.ThrowIfCancellationRequested();
            var diagonal=new double[n]; var rhs=new double[n];
            foreach(var row in rows)
            for(int k=0;k<row.Indices.Length;k++) { int j=row.Indices[k]; diagonal[j]+=row.Jacobian[k]*row.Jacobian[k]; rhs[j]-=row.Jacobian[k]*row.Residual; }
            for(int j=0;j<n;j++) diagonal[j]=Math.Max(diagonal[j],1e-8);
            if(rhs.Select((v,j)=>Math.Abs(v)/diagonal[j]).Max()<1e-7) { converged=true; break; }
            var step=ConjugateGradient(rows,diagonal,rhs,lambda,ct);
            double reduction=1;
            for(int j=0;j<n;j++)
            {
                double limit=j<offset && j%6<3 ? 0.08 : 0.15;
                reduction=Math.Min(reduction,limit/Math.Max(limit,Math.Abs(step[j])));
            }
            var proposal=x.Select((v,j)=>v+step[j]*reduction).ToArray();
            List<Row> trial;
            try { trial=Build(proposal,cameras,points,calibration,pointIndex); }
            catch(CaptureSolveException) { lambda*=10; continue; }
            double nextCost=Cost(trial);
            if(double.IsFinite(nextCost) && nextCost<cost)
            {
                double relative=(cost-nextCost)/Math.Max(1,cost);
                x=proposal; rows=trial; cost=nextCost; lambda=Math.Max(1e-9,lambda/3);
                if(relative<1e-6 || step.Max(Math.Abs)*reduction<1e-7) { converged=true; iteration++; break; }
            }
            else lambda=Math.Min(1e12,lambda*10);
        }
        for(int i=0;i<cameras.Count;i++) cameras[i].Pose=x.Skip(i*6).Take(6).ToArray();
        for(int i=0;i<points.Count;i++) points[i].Position=new V3(x[offset+i*3],x[offset+i*3+1],x[offset+i*3+2]);
        var errors=points.SelectMany(p=>p.Observations.Select(o=>
        { var q=cameras[o.Camera].Project(p.Position); return (q.u-o.U)*(q.u-o.U)+(q.v-o.V)*(q.v-o.V); })).ToArray();
        return new(converged,iteration,initial,cost,Math.Sqrt(errors.Average()));
    }

    private static List<Row> Build(double[] x,IReadOnlyList<BundleCamera> cameras,IReadOnlyList<BundlePoint> points,
        MetricConstraint[] constraints,Dictionary<string,int> pointIndex)
    {
        int offset=cameras.Count*6;
        var rows=new List<Row>(); var rotations=new double[cameras.Count][]; var perturbed=new double[cameras.Count][][];
        const double epsilon=1e-6;
        for(int i=0;i<cameras.Count;i++)
        {
            var pose=x.Skip(i*6).Take(6).ToArray(); rotations[i]=Rotation.FromVector(pose); perturbed[i]=new double[3][];
            for(int j=0;j<3;j++) { pose[j]+=epsilon; perturbed[i][j]=Rotation.FromVector(pose); pose[j]-=epsilon; }
            // A strong first-camera prior fixes the coordinate gauge; remaining priors admit small tracking corrections.
            for(int j=0;j<6;j++)
            {
                double sigma=i==0 ? 0.000001 : j<3 ? 0.03 : 0.02;
                rows.Add(new Row((x[i*6+j]-cameras[i].Prior[j])/sigma,new[] {i*6+j},new[] {1/sigma}));
            }
        }
        for(int pi=0;pi<points.Count;pi++)
        {
            int pstart=offset+pi*3; var point=new V3(x[pstart],x[pstart+1],x[pstart+2]);
            foreach(var o in points[pi].Observations)
            {
                int cstart=o.Camera*6; var camera=cameras[o.Camera]; var r=rotations[o.Camera];
                var pc=Rotation.Apply(r,point)+new V3(x[cstart+3],x[cstart+4],x[cstart+5]);
                if(pc.Z<=0.005 || !double.IsFinite(pc.Norm)) throw new CaptureSolveException("CAPTURE_CHEIRALITY_FAILED");
                double u=camera.Fx*pc.X/pc.Z+camera.Cx,v=camera.Fy*pc.Y/pc.Z+camera.Cy;
                var du=new V3(camera.Fx/pc.Z,0,-camera.Fx*pc.X/(pc.Z*pc.Z));
                var dv=new V3(0,camera.Fy/pc.Z,-camera.Fy*pc.Y/(pc.Z*pc.Z));
                var ju=new double[9]; var jv=new double[9];
                for(int k=0;k<3;k++)
                {
                    var delta=(Rotation.Apply(perturbed[o.Camera][k],point)-Rotation.Apply(r,point))/epsilon;
                    ju[k]=V3.Dot(du,delta); jv[k]=V3.Dot(dv,delta);
                    ju[k+3]=du.Array[k]; jv[k+3]=dv.Array[k];
                    var column=new V3(r[k],r[k+3],r[k+6]);
                    ju[k+6]=V3.Dot(du,column); jv[k+6]=V3.Dot(dv,column);
                }
                var ids=new[] {cstart,cstart+1,cstart+2,cstart+3,cstart+4,cstart+5,pstart,pstart+1,pstart+2};
                AddImageRow(rows,u-o.U,ids,ju); AddImageRow(rows,v-o.V,ids,jv);
            }
        }
        var positions=points.Select((p,i)=>(p.Id,p:new V3(x[offset+i*3],x[offset+i*3+1],x[offset+i*3+2])*1000))
            .ToDictionary(p=>p.Id,p=>p.p,StringComparer.Ordinal);
        foreach(var constraint in constraints)
        {
            double measured=MetricQualityEvaluator.Measure(constraint,positions);
            var ids=new List<int>(); var jacobian=new List<double>();
            foreach(string id in constraint.PointIds)
            {
                int pstart=offset+pointIndex[id]*3; V3 original=positions[id];
                for(int k=0;k<3;k++)
                {
                    var a=original.Array; a[k]+=epsilon*1000; positions[id]=V3.From(a);
                    double derivative=(MetricQualityEvaluator.Measure(constraint,positions)-measured)/(epsilon*constraint.UncertaintyMm);
                    positions[id]=original; ids.Add(pstart+k); jacobian.Add(derivative);
                }
            }
            rows.Add(new Row((measured-constraint.ValueMm)/constraint.UncertaintyMm,ids.ToArray(),jacobian.ToArray()));
        }
        return rows;
    }
    private static void AddImageRow(List<Row> rows,double residual,int[] ids,double[] jacobian)
    {
        double weight=Math.Abs(residual)>3 ? Math.Sqrt(3/Math.Abs(residual)) : 1;
        rows.Add(new Row(residual*weight,ids,jacobian.Select(v=>v*weight).ToArray()));
    }
    private static double Cost(List<Row> rows) => rows.Sum(r=>r.Residual*r.Residual);
    private static double[] ConjugateGradient(List<Row> rows,double[] diagonal,double[] rhs,double lambda,CancellationToken ct)
    {
        int n=rhs.Length; var x=new double[n]; var r=(double[])rhs.Clone(); var z=new double[n]; var d=new double[n]; var product=new double[n];
        for(int i=0;i<n;i++) z[i]=d[i]=r[i]/(diagonal[i]*(1+lambda));
        double rz=Dot(r,z),initial=Math.Max(1e-30,Dot(r,r));
        for(int iteration=0;iteration<150 && rz>1e-30;iteration++)
        {
            ct.ThrowIfCancellationRequested(); Array.Clear(product);
            foreach(var row in rows)
            {
                double sum=0; for(int k=0;k<row.Indices.Length;k++) sum+=row.Jacobian[k]*d[row.Indices[k]];
                for(int k=0;k<row.Indices.Length;k++) product[row.Indices[k]]+=row.Jacobian[k]*sum;
            }
            for(int i=0;i<n;i++) product[i]+=lambda*diagonal[i]*d[i];
            double denom=Dot(d,product); if(denom<=1e-30) break;
            double alpha=rz/denom;
            for(int i=0;i<n;i++) { x[i]+=alpha*d[i]; r[i]-=alpha*product[i]; }
            if(Dot(r,r)<initial*1e-10) break;
            for(int i=0;i<n;i++) z[i]=r[i]/(diagonal[i]*(1+lambda));
            double next=Dot(r,z),beta=next/rz; rz=next;
            for(int i=0;i<n;i++) d[i]=z[i]+beta*d[i];
        }
        return x;
    }
    private static double Dot(double[] a,double[] b) { double v=0; for(int i=0;i<a.Length;i++) v+=a[i]*b[i]; return v; }
}
